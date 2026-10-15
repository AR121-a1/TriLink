using System;
using System.Net;
using System.Net.Sockets;
using TriLink.Plugin;

namespace TriLink.Plugins.GameLink
{
    public sealed class UdpGameLinkFactory : IGameLinkFactory
    {
        public IGameLink CreateLink()
        {
            return new UdpGameLink();
        }
    }

    // Driven by the game caller: no worker thread, timer, DNS, or game/session state.
    internal sealed class UdpGameLink : IGameLink
    {
        private const int ReceiveAttemptLimit = 8;
        private readonly byte[] _receiveBuffer = new byte[GameDatagram.MaxPayloadLength + 1];
        private Socket _socket;
        private int _localPort;
        private bool _disposed;

        public bool IsOpen { get { return _socket != null; } }

        public int LocalPort { get { return _localPort; } }

        public string NormalizePeerAddress(string peerAddress)
        {
            ThrowIfDisposed();
            IPAddress address;
            if (!IPAddress.TryParse(peerAddress, out address)
                || address.AddressFamily != AddressFamily.InterNetwork)
            {
                throw new ArgumentException("目标须为 IPv4 地址文字，不执行 DNS 查询。", nameof(peerAddress));
            }
            return address.ToString();
        }

        public void Open(int localPort)
        {
            ThrowIfDisposed();
            if (localPort < 0 || localPort > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(localPort), "本地端口须为 0–65535。");
            }
            if (_socket != null) { throw new InvalidOperationException("游戏传输会话已经打开。"); }

            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                socket.ExclusiveAddressUse = true;
                socket.Blocking = false;
                // Keep the kernel queues bounded; the operating system may adjust these sizes.
                socket.ReceiveBufferSize = 8192;
                socket.SendBufferSize = 8192;
                socket.Bind(new IPEndPoint(IPAddress.Any, localPort));
                _localPort = ((IPEndPoint)socket.LocalEndPoint).Port;
                _socket = socket;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        public bool TryReceive(out GameDatagram datagram)
        {
            datagram = null;
            var socket = _socket;
            if (socket == null) { return false; }

            for (int attempt = 0; attempt < ReceiveAttemptLimit; ++attempt)
            {
                EndPoint source = new IPEndPoint(IPAddress.Any, 0);
                int length;
                try
                {
                    length = socket.ReceiveFrom(_receiveBuffer, 0, _receiveBuffer.Length,
                        SocketFlags.None, ref source);
                }
                catch (SocketException exception)
                {
                    if (exception.SocketErrorCode == SocketError.WouldBlock) { return false; }
                    // On Windows an oversized UDP datagram is discarded with MessageSize.
                    // A delayed ICMP error also cannot be delivered as game input.
                    if (exception.SocketErrorCode == SocketError.MessageSize
                        || exception.SocketErrorCode == SocketError.ConnectionReset) { continue; }
                    throw;
                }
                var peer = source as IPEndPoint;
                if (length > GameDatagram.MaxPayloadLength || peer == null
                    || peer.AddressFamily != AddressFamily.InterNetwork) { continue; }

                var payload = new byte[length];
                Buffer.BlockCopy(_receiveBuffer, 0, payload, 0, length);
                datagram = new GameDatagram
                {
                    PeerAddress = peer.Address.ToString(),
                    PeerPort = peer.Port,
                    Payload = payload,
                };
                return true;
            }
            return false;
        }

        public void Send(string peerAddress, int peerPort, byte[] payload)
        {
            ThrowIfDisposed();
            if (_socket == null) { throw new InvalidOperationException("请先打开游戏传输会话。"); }
            var address = IPAddress.Parse(NormalizePeerAddress(peerAddress));
            if (peerPort < 1 || peerPort > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(peerPort), "目标端口须为 1–65535。");
            }
            if (payload == null) { throw new ArgumentNullException(nameof(payload)); }
            if (payload.Length > GameDatagram.MaxPayloadLength)
            {
                throw new ArgumentException("游戏数据报不能超过 128 字节。", nameof(payload));
            }
            _socket.SendTo(payload, 0, payload.Length, SocketFlags.None,
                new IPEndPoint(address, peerPort));
        }

        public void Dispose()
        {
            if (_disposed) { return; }
            _disposed = true;
            var socket = _socket;
            _socket = null;
            _localPort = 0;
            if (socket != null) { socket.Dispose(); }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) { throw new ObjectDisposedException(nameof(UdpGameLink)); }
        }
    }
}
