using System;
using TriLink.Plugin;

namespace TriLink.Plugins.Thunder
{
    public enum GameSessionMode { Idle, Solo, Host, Join }

    // One host orders inputs; each PC runs the same engine. Fixed peer slots and
    // a 720-byte history ring bound recovery without scene state or worker threads.
    public sealed class GameSession : IDisposable
    {
        public const int HistoryTicks = 120;
        public const int NetworkIntervalMs = 100;
        public const int SilenceTimeoutMs = 5000;
        private sealed class PeerState
        {
            public string Address;
            public int Port;
            public uint Token, Ack, LastSequence;
            public byte Input;
            public bool Ready, HasSequence;
            public long LastReceive, LastInput;
            public void Clear()
            { Address = null; Port = 0; Token = Ack = LastSequence = 0; Input = 0;
                Ready = HasSequence = false; LastReceive = LastInput = 0; }
        }
        private readonly IGameLinkFactory _factory;
        private readonly byte[] _history = new byte[HistoryTicks * GameEngine.MaxPlayers];
        private readonly byte[] _tickInputs = new byte[GameEngine.MaxPlayers];
        private readonly PeerState[] _peers = new PeerState[GameEngine.MaxPlayers - 1];
        private IGameLink _link;
        private string _peerAddress;
        private int _peerPort;
        private uint _session, _token, _seed, _inputSequence;
        private bool _started, _disposed;
        private long _clock, _lastReceive;
        private int _networkAccumulator, _soloAccumulator, _handshakeAccumulator;

        public GameSession(IGameLinkFactory factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            for (var i = 0; i < _peers.Length; ++i) { _peers[i] = new PeerState(); }
            LocalPlayerIndex = -1; Status = "选择单人练习或多人联机";
        }
        public GameEngine Engine { get; private set; }
        public GameSessionMode Mode { get; private set; }
        public string Status { get; private set; }
        public bool Running { get; private set; }
        public bool Connected { get; private set; }
        public int PlayerCount { get; private set; }
        public int LocalPlayerIndex { get; private set; }
        public int ReadyPlayerCount
        {
            get
            {
                if (Mode == GameSessionMode.Idle) { return 0; }
                if (Mode == GameSessionMode.Host)
                { var count = 1; foreach (var peer in _peers) { if (peer.Ready) { ++count; } } return count; }
                if (Mode == GameSessionMode.Solo) { return 1; }
                return _started ? PlayerCount : Connected ? 1 : 0;
            }
        }
        public int LocalPort { get { return _link == null ? 0 : _link.LocalPort; } }
        public long BytesSent { get; private set; }
        public long BytesReceived { get; private set; }

        public void StartSolo(uint seed)
        {
            Reset(); Engine = new GameEngine(seed, false); Mode = GameSessionMode.Solo;
            PlayerCount = 1; LocalPlayerIndex = 0; Running = true; Status = "单人练习 · 玩家 1";
        }
        public void Host(int localPort, uint seed) { Host(localPort, seed, 2); }
        public void Host(int localPort, uint seed, int playerCount)
        {
            if (playerCount < 2 || playerCount > GameEngine.MaxPlayers) { throw new ArgumentOutOfRangeException(nameof(playerCount)); }
            Reset(); Open(localPort); _seed = seed; _session = NewId(); Mode = GameSessionMode.Host;
            PlayerCount = playerCount; LocalPlayerIndex = 0; UpdateWaitingStatus();
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
                SendJoin(GameMessage.Hello);
            }
            catch { CloseLink(); Mode = GameSessionMode.Idle; throw; }
            Status = "正在连接房主…";
        }

        public void Update(byte input, int elapsedMilliseconds)
        {
            ThrowIfDisposed();
            if (Mode == GameSessionMode.Idle) { return; }
            var elapsed = Math.Max(0, Math.Min(200, elapsedMilliseconds));
            _clock += Math.Max(0, elapsedMilliseconds); input &= GameEngine.InputMask;
            if (Mode == GameSessionMode.Solo)
            {
                _soloAccumulator += elapsed * GameEngine.TickRate;
                while (_soloAccumulator >= 1000) { Engine.Step(input, 0); _soloAccumulator -= 1000; }
                FinishIfGameOver(); return;
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
                if (Mode == GameSessionMode.Host)
                {
                    for (var i = 0; i < PlayerCount - 1; ++i)
                    {
                        if (_peers[i].Address != null && _clock - _peers[i].LastReceive > SilenceTimeoutMs)
                        { Fail("玩家 " + (i + 2) + " 连接已中断 · 请重新创建房间"); return; }
                    }
                }
                else if (Connected && _clock - _lastReceive > SilenceTimeoutMs)
                { Fail("房主连接已中断 · 请重新加入房间"); return; }
                else if (!Connected && _clock > 12000)
                { Fail("连接超时 · 请检查房主地址、端口和防火墙"); return; }
                if (!_started)
                {
                    _handshakeAccumulator += elapsed;
                    if (_handshakeAccumulator >= 500)
                    {
                        _handshakeAccumulator %= 500;
                        if (Mode == GameSessionMode.Join) { SendJoin(Connected ? GameMessage.Ready : GameMessage.Hello); }
                        else { for (var i = 0; i < PlayerCount - 1; ++i) { if (_peers[i].Address != null) { SendStart(i); } } }
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
            if (Mode == GameSessionMode.Host) { ReceiveHost(datagram, packet); return; }
            if (!FromHost(datagram) || packet.Token != _token) { return; }
            if (packet.Message == GameMessage.Start)
            {
                if (_session != 0 && (packet.Session != _session || packet.Tick != _seed
                    || packet.PlayerCount != PlayerCount || packet.PlayerIndex != LocalPlayerIndex)) { return; }
                if (_session == 0)
                {
                    _session = packet.Session; _seed = packet.Tick; PlayerCount = packet.PlayerCount;
                    LocalPlayerIndex = packet.PlayerIndex; Engine = new GameEngine(_seed, PlayerCount);
                    Connected = true; Running = false; UpdateWaitingStatus();
                }
                _lastReceive = _clock; SendJoin(GameMessage.Ready); return;
            }
            if (!Connected || packet.Session != _session || packet.PlayerCount != PlayerCount || packet.PlayerIndex != 0) { return; }
            if (packet.Message == GameMessage.Bye) { Fail("玩家已离开 · 本局结束，可以重新出击"); return; }
            if (packet.Message != GameMessage.Frames || packet.Tick > Engine.Tick + 1) { return; }
            _lastReceive = _clock;
            for (var index = 0; index < packet.Count; ++index)
            {
                if (packet.Tick + (uint)index <= Engine.Tick) { continue; }
                Engine.Step(packet.Inputs, index * PlayerCount);
            }
            if (!_started && Engine.Tick > 0)
            { _started = true; Running = true; Status = "多人合作 · 玩家 " + (LocalPlayerIndex + 1) + " / " + PlayerCount + " · 已连接"; }
            FinishIfGameOver();
        }

        private void ReceiveHost(GameDatagram datagram, GamePacket packet)
        {
            var peerIndex = FindPeer(datagram);
            if (packet.Message == GameMessage.Hello)
            {
                if (peerIndex < 0)
                {
                    if (_started) { return; }
                    for (var i = 0; i < PlayerCount - 1; ++i)
                    {
                        if (_peers[i].Address != null) { continue; }
                        peerIndex = i; _peers[i].Address = datagram.PeerAddress;
                        _peers[i].Port = datagram.PeerPort; _peers[i].Token = packet.Token; break;
                    }
                }
                if (peerIndex < 0 || _peers[peerIndex].Token != packet.Token) { return; }
                _peers[peerIndex].LastReceive = _clock; SendStart(peerIndex); UpdateWaitingStatus(); return;
            }
            if (peerIndex < 0) { return; }
            var peer = _peers[peerIndex];
            if (packet.Token != peer.Token || packet.Session != _session
                || packet.PlayerCount != PlayerCount || packet.PlayerIndex != peerIndex + 1) { return; }
            if (packet.Message == GameMessage.Bye) { Fail("玩家 " + (peerIndex + 2) + " 已离开 · 本局结束"); return; }
            if (packet.Message == GameMessage.Ready)
            {
                peer.Ready = true; peer.LastReceive = _clock;
                if (!_started && ReadyPlayerCount == PlayerCount)
                {
                    Engine = new GameEngine(_seed, PlayerCount); _started = Connected = Running = true;
                    Status = "多人合作 · 玩家 1 / " + PlayerCount + " · 已连接";
                }
                else { UpdateWaitingStatus(); }
                return;
            }
            if (!_started || packet.Message != GameMessage.Input || packet.Tick > Engine.Tick || packet.Tick < peer.Ack) { return; }
            peer.Ack = packet.Tick; peer.LastReceive = _clock;
            if (!peer.HasSequence || unchecked((int)(packet.Sequence - peer.LastSequence)) > 0)
            { peer.LastSequence = packet.Sequence; peer.HasSequence = true; peer.Input = packet.Input; peer.LastInput = _clock; }
        }

        private void AdvanceHost(byte input)
        {
            if (!Engine.GameOver)
            {
                for (var i = 0; i < PlayerCount - 1; ++i)
                {
                    if (Engine.Tick - _peers[i].Ack + 3 >= HistoryTicks)
                    { Fail("玩家 " + (i + 2) + " 落后超过恢复窗口 · 请重新创建房间"); return; }
                    _tickInputs[i + 1] = _clock - _peers[i].LastInput > 300 ? (byte)0 : _peers[i].Input;
                }
                _tickInputs[0] = input;
                for (var i = 0; i < 3 && !Engine.GameOver; ++i)
                {
                    Engine.Step(_tickInputs);
                    Buffer.BlockCopy(_tickInputs, 0, _history, (int)(Engine.Tick % HistoryTicks) * GameEngine.MaxPlayers, PlayerCount);
                }
                FinishIfGameOver();
            }
            for (var peerIndex = 0; peerIndex < PlayerCount - 1; ++peerIndex)
            {
                var peer = _peers[peerIndex]; var pending = Engine.Tick - peer.Ack;
                var count = (byte)(pending == 0 ? 1 : Math.Min((uint)GameWire.MaxBatchTicks, pending));
                var inputs = new byte[count * PlayerCount];
                if (pending != 0)
                {
                    for (var i = 0; i < count; ++i)
                    { Buffer.BlockCopy(_history, (int)((peer.Ack + 1 + (uint)i) % HistoryTicks) * GameEngine.MaxPlayers,
                        inputs, i * PlayerCount, PlayerCount); }
                }
                SendPeer(peerIndex, new GamePacket { Message = GameMessage.Frames, Session = _session,
                    Tick = pending == 0 ? Engine.Tick : peer.Ack + 1, Count = count, PlayerCount = (byte)PlayerCount, Inputs = inputs });
            }
        }
        private void SendInput(byte input) { SendJoin(GameMessage.Input, Engine.Tick, ++_inputSequence, input); }
        private void SendJoin(GameMessage message, uint tick = 0, uint sequence = 0, byte input = 0)
        {
            var hello = message == GameMessage.Hello;
            SendPacket(_peerAddress, _peerPort, new GamePacket { Message = message, Session = hello ? 0 : _session,
                Token = _token, Tick = tick, Sequence = sequence, Input = input,
                PlayerCount = hello ? (byte)0 : (byte)PlayerCount, PlayerIndex = hello ? (byte)0 : (byte)LocalPlayerIndex });
        }
        private void SendStart(int peerIndex)
        { SendPeer(peerIndex, new GamePacket { Message = GameMessage.Start, Session = _session, Tick = _seed,
            PlayerCount = (byte)PlayerCount, PlayerIndex = (byte)(peerIndex + 1) }); }
        private void SendPeer(int peerIndex, GamePacket packet)
        { var peer = _peers[peerIndex]; packet.Token = peer.Token; SendPacket(peer.Address, peer.Port, packet); }
        private void SendPacket(string address, int port, GamePacket packet)
        { var bytes = GameWire.Encode(packet); _link.Send(address, port, bytes); BytesSent += bytes.Length; }
        private bool FromHost(GameDatagram datagram)
        { return datagram.PeerAddress == _peerAddress && datagram.PeerPort == _peerPort; }
        private int FindPeer(GameDatagram datagram)
        {
            for (var i = 0; i < PlayerCount - 1; ++i)
            { if (_peers[i].Address == datagram.PeerAddress && _peers[i].Port == datagram.PeerPort) { return i; } }
            return -1;
        }
        private void UpdateWaitingStatus()
        {
            if (_started) { return; }
            Status = Mode == GameSessionMode.Host ? "等待队友 · 已就绪 " + ReadyPlayerCount + "/" + PlayerCount + " · 本机端口 " + LocalPort
                : "玩家 " + (LocalPlayerIndex + 1) + "/" + PlayerCount + " 已就绪 · 等待所有队友";
        }
        private void FinishIfGameOver()
        { if (Engine.GameOver) { Running = false; Status = "任务结束 · 得分 " + Engine.Score + " · 可以重新出击"; } }

        public void Stop()
        {
            if (_link != null && _session != 0)
            {
                if (Mode == GameSessionMode.Host)
                {
                    for (var i = 0; i < PlayerCount - 1; ++i)
                    {
                        if (_peers[i].Address == null) { continue; }
                        try { SendPeer(i, new GamePacket { Message = GameMessage.Bye, Session = _session, PlayerCount = (byte)PlayerCount }); }
                        catch { /* Each peer is best-effort; closing still releases the adapter. */ }
                    }
                }
                else if (Mode == GameSessionMode.Join)
                { try { SendJoin(GameMessage.Bye); } catch { /* Closing must still release the adapter. */ } }
            }
            CloseLink(); Mode = GameSessionMode.Idle; Connected = Running = false; Status = "已停止 · 可以重新出击";
        }
        private void Fail(string status) { Stop(); Status = status; }
        private void Reset()
        {
            ThrowIfDisposed(); Stop(); Engine = null; _session = _token = _seed = _inputSequence = 0;
            _peerAddress = null; _peerPort = 0; _started = false;
            _clock = _lastReceive = 0; PlayerCount = 0; LocalPlayerIndex = -1;
            _networkAccumulator = _soloAccumulator = _handshakeAccumulator = 0; BytesSent = BytesReceived = 0;
            foreach (var peer in _peers) { peer.Clear(); }
            Array.Clear(_history, 0, _history.Length); Array.Clear(_tickInputs, 0, _tickInputs.Length);
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
