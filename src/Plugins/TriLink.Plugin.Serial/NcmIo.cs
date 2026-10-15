using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using TriLink.Core;

namespace TriLink.MinClient.Serial
{
    // CDC and NCM share the watcher's existing single I/O owner. There is no
    // background network scanner, second business queue or write-failover path.
    internal interface IOptionalTransportPolling
    {
        string Diagnostic { get; }
        void ResetOptionalPolling();
    }
    internal sealed class CompositeSerialIo : ISerialIo, IOptionalTransportPolling
    {
        private readonly ISerialIo _serial, _ncm;
        private readonly object _sync = new object();
        private int _optionalFailures, _generation;
        private string _diagnostic;
        public CompositeSerialIo() : this(new WindowsSerialIo(), new NcmIo(new WindowsNcmCatalog(), new NcmHttpClient())) { }
        internal CompositeSerialIo(ISerialIo serial, ISerialIo ncm) { _serial = serial; _ncm = ncm; }
        public string Diagnostic { get { lock (_sync) return _diagnostic; } }
        public void ResetOptionalPolling() { lock (_sync) { ++_generation; _optionalFailures = 0; _diagnostic = null; } }
        public List<SerialCandidate> Enumerate()
        {
            var serial = _serial.Enumerate();
            int generation;
            lock (_sync) { generation = _generation; if (_optionalFailures >= 5) return serial; }
            try
            {
                var ncm = _ncm.Enumerate();
                lock (_sync) if (generation == _generation) { _optionalFailures = 0; _diagnostic = null; }
                return serial.Concat(ncm).OrderBy(candidate => candidate.TransportKind).ToList();
            }
            catch (Exception exception) when (exception is Win32Exception || exception is IOException
                || exception is UnauthorizedAccessException || exception is System.Security.SecurityException)
            {
                if (!serial.Any(candidate => candidate.IsProtocolCandidate)) throw;
                lock (_sync)
                {
                    if (generation == _generation)
                    {
                        ++_optionalFailures;
                        _diagnostic = _optionalFailures >= 5
                            ? "NCM 备选枚举连续 5 次失败，已暂停该支路；CDC 继续可用。手动启动 USB 轮询可重试。"
                            : "NCM 备选枚举失败（" + _optionalFailures + "/5）；保留 CDC 主通道。";
                    }
                }
                return serial;
            }
        }
        public ISerialConnection OpenPort(string endpoint, int timeout, CancellationToken cancellation)
        {
            if (NcmEndpoint.IsEndpointName(endpoint)) return _ncm.OpenPort(endpoint, timeout, cancellation);
            if ((endpoint ?? "").StartsWith("NCM:", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("NCM endpoint is invalid.");
            return _serial.OpenPort(endpoint, timeout, cancellation);
        }
    }

    internal sealed class NcmEndpoint
    {
        public const string ManagementUrl = "http://192.168.240.1/";
        public static readonly IPAddress DeviceAddress = IPAddress.Parse("192.168.240.1");
        public string EndpointName, PnpDeviceId;
        public IPAddress LocalAddress;
        public uint InterfaceIndex;
        public string Fingerprint { get { return PnpDeviceId + "|" + EndpointName + "|" + LocalAddress + "|" + InterfaceIndex; } }
        public NcmEndpoint Snapshot()
        {
            return new NcmEndpoint { EndpointName = EndpointName, PnpDeviceId = PnpDeviceId,
                LocalAddress = new IPAddress(LocalAddress.GetAddressBytes()), InterfaceIndex = InterfaceIndex };
        }
        public bool SameAs(NcmEndpoint other)
        {
            return other != null && string.Equals(Fingerprint, other.Fingerprint, StringComparison.OrdinalIgnoreCase);
        }
        public static bool IsEndpointName(string name)
        {
            Guid id;
            return name != null && name.StartsWith("NCM:", StringComparison.Ordinal)
                && Guid.TryParseExact(name.Substring(4), "D", out id) && id != Guid.Empty;
        }
    }

    internal interface INcmCatalog { List<NcmEndpoint> Enumerate(); }
    internal interface INcmHttpClient
    {
        string Request(NcmEndpoint endpoint, string token, string line, ulong? deadline, CancellationToken cancellation);
    }

    internal sealed class NcmIo : ISerialIo
    {
        private readonly INcmCatalog _catalog;
        private readonly INcmHttpClient _http;
        private readonly object _sync = new object();
        private readonly Dictionary<string, NcmEndpoint> _enumerated = new Dictionary<string, NcmEndpoint>(StringComparer.Ordinal);
        // Exact schema, no duplicate/unknown JSON fields or token coercion.
        private static readonly Regex SessionPattern = new Regex(
            "\\A\\{\\s*\"protocol\"\\s*:\\s*1\\s*,\\s*\"token\"\\s*:\\s*\"([0-9a-f]{32})\"\\s*,\\s*\"firmware\"\\s*:\\s*\"(0\\.16\\.[0-9]{1,5})\"\\s*,\\s*\"endpoint\"\\s*:\\s*\"/api/v1/exchange\"\\s*,\\s*\"uptime_ms\"\\s*:\\s*([0-9]{1,20})\\s*\\}\\s*\\z",
            RegexOptions.CultureInvariant);
        internal NcmIo(INcmCatalog catalog, INcmHttpClient http) { _catalog = catalog; _http = http; }
        public List<SerialCandidate> Enumerate()
        {
            var endpoints = _catalog.Enumerate();
            lock (_sync)
            {
                _enumerated.Clear();
                foreach (var endpoint in endpoints) _enumerated.Add(endpoint.EndpointName, endpoint.Snapshot());
            }
            return endpoints.Select(endpoint => new SerialCandidate {
                PortName = endpoint.EndpointName, PnpDeviceId = endpoint.Fingerprint,
                Name = "TriLink NCM", Manufacturer = "TriLink", TransportKind = NodeTransportKind.UsbNcm,
            }).ToList();
        }
        private NcmEndpoint Resolve(string name, NcmEndpoint binding)
        {
            if (!NcmEndpoint.IsEndpointName(name)) throw new ArgumentException("NCM endpoint is invalid.");
            var found = _catalog.Enumerate().Where(endpoint => endpoint.EndpointName == name).ToList();
            if (found.Count != 1 || (binding != null && !found[0].SameAs(binding)))
                throw new IOException("USB NCM 接口、地址或本机路由已改变；没有发送或重放操作。");
            return found[0].Snapshot();
        }
        public ISerialConnection OpenPort(string name, int timeout, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            NcmEndpoint binding;
            lock (_sync)
                if (name == null || !_enumerated.TryGetValue(name, out binding))
                    throw new IOException("USB NCM 端点尚未经过系统枚举；没有发出网络请求。");
            var endpoint = Resolve(name, binding);
            var session = _http.Request(endpoint, null, null, null, cancellation);
            var sessionAge = Stopwatch.StartNew();
            var match = session != null && session.Length <= 512 ? SessionPattern.Match(session) : Match.Empty;
            ulong uptime;
            if (!match.Success || !ulong.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out uptime))
                throw new InvalidDataException("USB NCM 会话描述无效；未建立控制通道。");
            cancellation.ThrowIfCancellationRequested();
            return new Connection(this, endpoint, match.Groups[1].Value, uptime, sessionAge, cancellation);
        }
        private sealed class Connection : ISerialConnection
        {
            private readonly NcmIo _owner;
            private readonly NcmEndpoint _endpoint;
            private readonly CancellationToken _cancellation;
            private string _token;
            private readonly ulong _uptime;
            private readonly Stopwatch _sessionAge;
            private readonly Queue<char> _input = new Queue<char>();
            private bool _disposed;
            public Connection(NcmIo owner, NcmEndpoint endpoint, string token, ulong uptime, Stopwatch sessionAge, CancellationToken cancellation)
            { _owner = owner; _endpoint = endpoint; _token = token; _uptime = uptime; _sessionAge = sessionAge; _cancellation = cancellation; }
            public void DiscardInBuffer() { _input.Clear(); }
            public void DiscardOutBuffer() { }
            public void WriteLine(string line)
            {
                if (_disposed) throw new ObjectDisposedException("NcmConnection");
                _cancellation.ThrowIfCancellationRequested();
                if (line == null || line.Length == 0 || line.Length + 1 >= 384 || line.Any(c => c < 32 || c > 126))
                    throw new ArgumentException("NCM exchange requires one bounded ASCII protocol line.");
                _owner.Resolve(_endpoint.EndpointName, _endpoint);
                // Exactly once. A new token/transport must never replay a failed write.
                // Device-monotonic deadline is checked server-side before submission.
                // Sampling receive latency only shortens validity; sleep/large skew fail closed.
                ulong deadline = checked(_uptime + (ulong)_sessionAge.ElapsedMilliseconds + 2500UL);
                var reply = _owner._http.Request(_endpoint, _token, line, deadline, _cancellation);
                if (reply == null || reply.Length > 2048 || reply.Any(c => c > 126 || (c < 32 && c != '\r' && c != '\n')))
                    throw new InvalidDataException("USB NCM 回复超过上限或包含非法字符。");
                _input.Clear();
                foreach (var value in reply) _input.Enqueue(value);
            }
            public int ReadChar()
            {
                _cancellation.ThrowIfCancellationRequested();
                if (_disposed) throw new ObjectDisposedException("NcmConnection");
                if (_input.Count == 0) throw new TimeoutException("NCM exchange has no complete protocol reply.");
                return _input.Dequeue();
            }
            public void Dispose() { _disposed = true; _token = null; _input.Clear(); }
        }
    }
}
