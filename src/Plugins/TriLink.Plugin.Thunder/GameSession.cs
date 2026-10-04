using System;
using TriLink.Plugin;

namespace TriLink.Plugins.Thunder
{
    public enum GameSessionMode { Idle, Solo, Host, Join }

    // A game host orders inputs, independently of Room leader. Both PCs run the same integer engine.
    // At 10 Hz a host commits 3 ticks and sends the oldest unacknowledged contiguous input batch.
    // Lost/reordered/duplicate datagrams never skip simulation ticks; a fixed ring bounds recovery.
    public sealed class GameSession : IDisposable
    {
        public const int HistoryTicks = 120;
        public const int NetworkIntervalMs = 100;
        public const int SilenceTimeoutMs = 5000;
        private readonly IGameLinkFactory _factory;
        private readonly byte[] _history = new byte[HistoryTicks * 2];
        private IGameLink _link;
        private string _peerAddress;
        private int _peerPort;
        private uint _session, _token, _seed, _ack, _inputSequence, _lastInputSequence;
        private bool _hasInputSequence, _disposed;
        private byte _remoteInput;
        private long _clock, _lastReceive, _lastRemoteInput;
        private int _networkAccumulator, _soloAccumulator, _handshakeAccumulator;

        public GameSession(IGameLinkFactory factory)
        { _factory = factory ?? throw new ArgumentNullException(nameof(factory)); Status = "选择单人练习或双人联机"; }
        public GameEngine Engine { get; private set; }
        public GameSessionMode Mode { get; private set; }
        public string Status { get; private set; }
        public bool Running { get; private set; }
        public bool Connected { get; private set; }
        public int LocalPort { get { return _link == null ? 0 : _link.LocalPort; } }
        public long BytesSent { get; private set; }
        public long BytesReceived { get; private set; }

        public void StartSolo(uint seed)
        {
            Reset(); Engine = new GameEngine(seed, false); Mode = GameSessionMode.Solo;
            Running = true; Status = "单人练习 · 蓝色战机";
        }
        public void Host(int localPort, uint seed)
        {
            Reset(); Open(localPort); _seed = seed; _session = NewId(); Mode = GameSessionMode.Host;
            Status = "房间已创建 · 等待队友加入 · 本机端口 " + LocalPort;
        }
        public void Join(string hostAddress, int hostPort, int localPort)
        {
            if (string.IsNullOrWhiteSpace(hostAddress) || hostPort < 1 || hostPort > 65535)
            { throw new ArgumentException("请输入房主地址和有效端口。"); }
            Reset(); Open(localPort);
            try
            {
                _peerAddress = _link.NormalizePeerAddress(hostAddress.Trim());
                _peerPort = hostPort; _token = NewId(); Mode = GameSessionMode.Join;
                Send(GameMessage.Hello);
            }
            catch { CloseLink(); Mode = GameSessionMode.Idle; throw; }
            Status = "正在连接队友…";
        }

        public void Update(byte input, int elapsedMilliseconds)
        {
            ThrowIfDisposed();
            if (Mode == GameSessionMode.Idle) { return; }
            var elapsed = Math.Max(0, Math.Min(200, elapsedMilliseconds));
            _clock += Math.Max(0, elapsedMilliseconds); input &= 31;
            if (Mode == GameSessionMode.Solo)
            {
                _soloAccumulator += elapsed * GameEngine.TickRate;
                while (_soloAccumulator >= 1000) { Engine.Step(input, 0); _soloAccumulator -= 1000; }
                if (Engine.GameOver) { Running = false; Status = "任务结束 · 得分 " + Engine.Score + " · 可以重新出击"; }
                return;
            }
            try
            {
                GameDatagram datagram;
                for (var count = 0; count < 16 && _link != null && _link.TryReceive(out datagram); ++count)
                {
                    BytesReceived += datagram.Payload.Length;
                    GamePacket packet;
                    if (GameWire.TryDecode(datagram.Payload, out packet)) { Receive(datagram, packet); }
                    if (Mode == GameSessionMode.Idle) { return; }
                }
                if (Connected && _clock - _lastReceive > SilenceTimeoutMs)
                { Fail("队友连接已中断 · 请重新创建或加入房间"); return; }
                if (!Connected)
                {
                    if (Mode == GameSessionMode.Join && _clock > 12000)
                    { Fail("连接超时 · 请检查队友地址、端口和防火墙"); return; }
                    if (Mode == GameSessionMode.Host && _peerAddress != null && _clock - _lastReceive > SilenceTimeoutMs)
                    { Fail("队友未完成连接 · 请重新创建房间"); return; }
                    _handshakeAccumulator += elapsed;
                    if (_handshakeAccumulator >= 500)
                    {
                        _handshakeAccumulator %= 500;
                        if (Mode == GameSessionMode.Join) { Send(GameMessage.Hello); }
                        else if (_peerAddress != null) { Send(GameMessage.Start, _seed); }
                    }
                    return;
                }
                _networkAccumulator += elapsed;
                while (_networkAccumulator >= NetworkIntervalMs)
                {
                    _networkAccumulator -= NetworkIntervalMs;
                    if (Mode == GameSessionMode.Host) { AdvanceHost(input); }
                    else { SendInput(input); }
                    if (Mode == GameSessionMode.Idle) { return; }
                }
            }
            catch (Exception exception) { Fail("联机中止 · " + exception.Message); }
        }

        private void Receive(GameDatagram datagram, GamePacket packet)
        {
            if (Mode == GameSessionMode.Host && packet.Message == GameMessage.Hello)
            {
                if (_peerAddress == null)
                { _peerAddress = datagram.PeerAddress; _peerPort = datagram.PeerPort; _token = packet.Token; }
                if (!FromPeer(datagram) || packet.Token != _token) { return; }
                _lastReceive = _clock; Send(GameMessage.Start, _seed); return;
            }
            if (!FromPeer(datagram) || packet.Token != _token) { return; }
            if (Mode == GameSessionMode.Join && packet.Message == GameMessage.Start)
            {
                if (_session != 0 && packet.Session != _session) { return; }
                if (_session == 0)
                {
                    _session = packet.Session; _seed = packet.Tick;
                    Engine = new GameEngine(_seed, true); Connected = Running = true;
                    Status = "双人合作 · 橙色战机 · 已连接";
                }
                _lastReceive = _clock; Send(GameMessage.Ready); return;
            }
            if (packet.Session != _session) { return; }
            if (packet.Message == GameMessage.Bye) { Fail("队友已离开 · 可以重新出击"); return; }
            if (Mode == GameSessionMode.Host && packet.Message == GameMessage.Ready)
            {
                if (!Connected)
                { Engine = new GameEngine(_seed, true); Connected = Running = true; Status = "双人合作 · 蓝色战机 · 已连接"; }
                _lastReceive = _clock; return;
            }
            if (!Connected) { return; }
            if (Mode == GameSessionMode.Host && packet.Message == GameMessage.Input)
            {
                if (packet.Tick > Engine.Tick || packet.Tick < _ack) { return; }
                _ack = packet.Tick; _lastReceive = _clock;
                if (!_hasInputSequence || unchecked((int)(packet.Sequence - _lastInputSequence)) > 0)
                {
                    _lastInputSequence = packet.Sequence; _hasInputSequence = true;
                    _remoteInput = packet.Input; _lastRemoteInput = _clock;
                }
            }
            else if (Mode == GameSessionMode.Join && packet.Message == GameMessage.Frames)
            {
                if (packet.Tick > Engine.Tick + 1) { return; }
                _lastReceive = _clock;
                for (var index = 0; index < packet.Count; ++index)
                {
                    if (packet.Tick + (uint)index <= Engine.Tick) { continue; }
                    Engine.Step(packet.Inputs[index * 2], packet.Inputs[index * 2 + 1]);
                }
                if (Engine.GameOver) { Running = false; Status = "任务结束 · 得分 " + Engine.Score + " · 可以重新出击"; }
            }
        }

        private void AdvanceHost(byte input)
        {
            if (!Engine.GameOver)
            {
                if (Engine.Tick - _ack + 3 >= HistoryTicks)
                { Fail("连接落后超过恢复窗口 · 请重新创建房间"); return; }
                var remote = _clock - _lastRemoteInput > 300 ? (byte)0 : _remoteInput;
                for (var i = 0; i < 3 && !Engine.GameOver; ++i)
                {
                    Engine.Step(input, remote);
                    var slot = (int)(Engine.Tick % HistoryTicks) * 2;
                    _history[slot] = input; _history[slot + 1] = remote;
                }
                if (Engine.GameOver) { Running = false; Status = "任务结束 · 得分 " + Engine.Score + " · 可以重新出击"; }
            }
            var pending = Engine.Tick - _ack;
            // A duplicate final tick keeps the session alive without advancing the peer beyond GameOver.
            if (pending == 0) { Send(GameMessage.Frames, Engine.Tick, 1, new byte[] { 0, 0 }); return; }
            var count = (byte)Math.Min((uint)GameWire.MaxBatchTicks, pending);
            var inputs = new byte[count * 2];
            for (var i = 0; i < count; ++i)
            {
                var slot = (int)((_ack + 1 + (uint)i) % HistoryTicks) * 2;
                inputs[i * 2] = _history[slot]; inputs[i * 2 + 1] = _history[slot + 1];
            }
            Send(GameMessage.Frames, _ack + 1, count, inputs);
        }
        private void SendInput(byte input)
        {
            var packet = new GamePacket { Message = GameMessage.Input, Session = _session, Token = _token,
                Tick = Engine.Tick, Sequence = ++_inputSequence, Input = input };
            SendPacket(packet);
        }
        private void Send(GameMessage message, uint tick = 0, byte count = 0, byte[] inputs = null)
        { SendPacket(new GamePacket { Message = message, Session = message == GameMessage.Hello ? 0 : _session,
            Token = _token, Tick = tick, Count = count, Inputs = inputs ?? new byte[0] }); }
        private void SendPacket(GamePacket packet)
        {
            var bytes = GameWire.Encode(packet);
            _link.Send(_peerAddress, _peerPort, bytes); BytesSent += bytes.Length;
        }
        private bool FromPeer(GameDatagram datagram)
        { return datagram.PeerAddress == _peerAddress && datagram.PeerPort == _peerPort; }

        public void Stop()
        {
            if (_link != null && _peerAddress != null && _session != 0)
            { try { Send(GameMessage.Bye); } catch { /* Closing must still release the adapter. */ } }
            CloseLink(); Mode = GameSessionMode.Idle; Connected = Running = false; Status = "已停止 · 可以重新出击";
        }
        private void Fail(string status) { Stop(); Status = status; }
        private void Reset()
        {
            ThrowIfDisposed(); Stop(); Engine = null;
            _session = _token = _seed = _ack = _inputSequence = _lastInputSequence = 0;
            _peerAddress = null; _peerPort = 0; _remoteInput = 0; _hasInputSequence = false;
            _clock = _lastReceive = _lastRemoteInput = 0;
            _networkAccumulator = _soloAccumulator = _handshakeAccumulator = 0;
            BytesSent = BytesReceived = 0; Array.Clear(_history, 0, _history.Length);
        }
        private void Open(int port)
        {
            _link = _factory.CreateLink();
            try { _link.Open(port); } catch { CloseLink(); throw; }
        }
        private void CloseLink() { if (_link != null) { _link.Dispose(); _link = null; } }
        private static uint NewId() { var value = BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0); return value == 0 ? 1 : value; }
        private void ThrowIfDisposed() { if (_disposed) { throw new ObjectDisposedException(nameof(GameSession)); } }
        public void Dispose() { if (_disposed) { return; } Stop(); _disposed = true; }
    }
}
