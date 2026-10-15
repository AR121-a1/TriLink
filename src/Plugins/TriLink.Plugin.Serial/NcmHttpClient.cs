using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace TriLink.MinClient.Serial
{
    // Minimal fixed-endpoint HTTP/1.1: no DNS, proxy, redirects, cookies, retry,
    // automatic auth, decompression or writable endpoint configuration.
    internal sealed class NcmHttpClient : INcmHttpClient
    {
        internal const int DeadlineMs = 3500;
        internal const int HeaderLimit = 4096;
        internal const int BodyLimit = 2048;
        public string Request(NcmEndpoint endpoint, string token, string line, ulong? deadline, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var request = BuildRequest(token, line, deadline);
            var elapsed = Stopwatch.StartNew();
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            using (cancellation.Register(() => socket.Close()))
            {
                try
                {
                    socket.NoDelay = true;
                    socket.Bind(new IPEndPoint(endpoint.LocalAddress, 0));
                    // Defense in depth against interface routing changes after enumeration.
                    // IP_UNICAST_IF uses network byte order on Windows.
                    socket.SetSocketOption(SocketOptionLevel.IP, (SocketOptionName)31,
                        IPAddress.HostToNetworkOrder((int)endpoint.InterfaceIndex));
                    var connect = socket.BeginConnect(new IPEndPoint(NcmEndpoint.DeviceAddress, 80), null, null);
                    using (connect.AsyncWaitHandle)
                    {
                        if (WaitHandle.WaitAny(new[] { connect.AsyncWaitHandle, cancellation.WaitHandle }, Remaining(elapsed)) != 0)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            throw new TimeoutException("USB NCM 连接超时；未自动切换通道。");
                        }
                        socket.EndConnect(connect);
                    }
                    int sent = 0;
                    while (sent < request.Length)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        socket.SendTimeout = Remaining(elapsed);
                        int count = socket.Send(request, sent, request.Length - sent, SocketFlags.None);
                        if (count <= 0) throw new IOException("USB NCM 写入中断，结果未知；不自动重放。");
                        sent += count;
                    }
                    var response = new byte[HeaderLimit + BodyLimit];
                    int received = 0, bodyStart = -1, bodyLength = -1;
                    while (bodyStart < 0 || received < bodyStart + bodyLength)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        socket.ReceiveTimeout = Remaining(elapsed);
                        int capacity = bodyStart < 0 ? HeaderLimit - received : bodyStart + bodyLength - received;
                        if (capacity <= 0) throw new InvalidDataException("USB NCM HTTP 头超过上限。");
                        int count = socket.Receive(response, received, capacity, SocketFlags.None);
                        if (count == 0) throw new IOException("USB NCM 回复不完整，结果未知；不自动重放。");
                        received += count;
                        if (bodyStart < 0)
                        {
                            bodyStart = FindHeaderEnd(response, received);
                            if (bodyStart >= 0)
                            {
                                bodyLength = ParseHeader(response, bodyStart);
                                if (received > bodyStart + bodyLength) throw new InvalidDataException("USB NCM HTTP 长度不一致。");
                            }
                        }
                    }
                    cancellation.ThrowIfCancellationRequested();
                    for (int i = bodyStart; i < received; ++i)
                        if (response[i] > 126 || (response[i] < 32 && response[i] != 10 && response[i] != 13 && response[i] != 9))
                            throw new InvalidDataException("USB NCM 回复不是受支持的 ASCII 协议。");
                    // Socket timeout granularity cannot authorize a late successful reply.
                    Remaining(elapsed);
                    return Encoding.ASCII.GetString(response, bodyStart, bodyLength);
                }
                catch (SocketException)
                {
                    cancellation.ThrowIfCancellationRequested();
                    throw new IOException("USB NCM 网络读写失败，结果未知；不自动重放或切换通道。");
                }
                catch (ObjectDisposedException)
                {
                    cancellation.ThrowIfCancellationRequested();
                    throw;
                }
            }
        }
        private static int Remaining(Stopwatch elapsed)
        {
            return RemainingMilliseconds(elapsed.ElapsedMilliseconds);
        }
        internal static int RemainingMilliseconds(long elapsed)
        {
            if (elapsed < 0) throw new ArgumentOutOfRangeException("elapsed");
            var remaining = DeadlineMs - elapsed;
            if (remaining <= 0) throw new TimeoutException("USB NCM 总时限已到，结果未知；不自动重放。");
            return (int)remaining;
        }
        internal static byte[] BuildRequest(string token, string line, ulong? deadline = null)
        {
            bool session = line == null;
            if (!session && (token == null || token.Length != 32 || token.Any(c => !(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f'))))
                throw new ArgumentException("Invalid in-memory NCM session token.");
            if (!session && (line.Length == 0 || line.Length + 1 >= 384 || line.Any(c => c < 32 || c > 126)))
                throw new ArgumentException("Invalid bounded NCM protocol line.");
            var body = session ? "" : line + "\n";
            var request = new StringBuilder();
            request.Append(session ? "GET /api/v1/session HTTP/1.1\r\n" : "POST /api/v1/exchange HTTP/1.1\r\n");
            request.Append("Host: 192.168.240.1\r\nConnection: close\r\nCache-Control: no-store\r\n");
            if (!session)
            {
                request.Append("Content-Type: text/plain\r\nX-TriLink-Token: ").Append(token).Append("\r\n");
                var fields = line.Split(' ');
                if (fields.Length >= 2 && fields[0] == "TRILINK/3"
                    && (fields[1] == "ROOM" || fields[1] == "RGB" || fields[1] == "RGBENABLE"))
                {
                    if (!deadline.HasValue) throw new ArgumentException("A mutation needs a device-monotonic deadline.");
                    request.Append("X-TriLink-Confirm: execute\r\nX-TriLink-Deadline: ")
                        .Append(deadline.Value.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
                }
                request.Append("Content-Length: ").Append(body.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            }
            return Encoding.ASCII.GetBytes(request.Append("\r\n").Append(body).ToString());
        }
        internal static int FindHeaderEnd(byte[] bytes, int length)
        {
            for (int i = 3; i < length; ++i)
                if (bytes[i - 3] == 13 && bytes[i - 2] == 10 && bytes[i - 1] == 13 && bytes[i] == 10) return i + 1;
            return -1;
        }
        internal static int ParseHeader(byte[] bytes, int length)
        {
            if (length < 4 || length > HeaderLimit) throw new InvalidDataException("USB NCM HTTP 头长度无效。");
            for (int i = 0; i < length; ++i)
                if (bytes[i] > 126 || (bytes[i] < 32 && bytes[i] != 10 && bytes[i] != 13))
                    throw new InvalidDataException("USB NCM HTTP 头包含非法字符。");
            var lines = Encoding.ASCII.GetString(bytes, 0, length).Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (!(lines[0] == "HTTP/1.1 200 OK" || lines[0] == "HTTP/1.0 200 OK"))
                throw new IOException("USB NCM HTTP 未成功；拒绝重定向，操作不会自动重放。");
            int bodyLength = -1;
            foreach (var line in lines.Skip(1))
            {
                if (line.Length == 0) continue;
                int colon = line.IndexOf(':');
                if (colon <= 0 || char.IsWhiteSpace(line[0])) throw new InvalidDataException("USB NCM HTTP 头格式无效。");
                var name = line.Substring(0, colon);
                var value = line.Substring(colon + 1).Trim();
                if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("USB NCM 不接受 chunked/compressed 回复。");
                if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    if (bodyLength >= 0 || value.Length == 0 || value.Any(c => c < '0' || c > '9')
                        || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out bodyLength)
                        || bodyLength < 0 || bodyLength > BodyLimit)
                        throw new InvalidDataException("USB NCM HTTP 正文长度无效或超过 2048 B。");
                }
            }
            if (bodyLength < 0) throw new InvalidDataException("USB NCM HTTP 缺少明确长度。");
            return bodyLength;
        }
    }
}
