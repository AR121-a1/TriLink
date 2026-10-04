using System;
using System.Collections.Generic;
using TriLink.Plugin;
using TriLink.Plugins.Thunder;

namespace TriLink.Thunder.Tests
{
    internal static class SessionChecks
    {
        public static void Run(Action<bool, string> check)
        {
            var network = new TestNetwork();
            using (var host = new GameSession(network))
            using (var guest = new GameSession(network))
            {
                host.Host(10001, 12345); guest.Join("test", 10001, 10002);
                Pump(host, guest, 20);
                check(host.Connected && guest.Connected, "join handshake starts two independent game engines");
                check(host.Engine != guest.Engine && host.Engine.Players[1].Active, "cooperative worlds are independent and both pilots active");
                guest.Update(0, 0); host.Update(0, 0);
                check(Hash(host.Engine) == Hash(guest.Engine), "normal input stream produces identical full world state");
                network.DropEvery = 5; network.DuplicateEvery = 7; network.ReorderEvery = 3;
                Pump(host, guest, 120);
                network.DropEvery = network.DuplicateEvery = network.ReorderEvery = 0;
                for (var i = 0; i < 15; ++i) { guest.Update(0, 100); host.Update(0, 0); }
                guest.Update(0, 0);
                check(Hash(host.Engine) == Hash(guest.Engine), "bounded resend recovers loss, duplicate and reordered packets without divergent ticks");
                check(host.BytesSent < 13000 && guest.BytesSent < 8000, "normal and impaired run stays within a small datagram budget");
                var tick = host.Engine.Tick;
                var outsider = network.CreateLink(); outsider.Open(10003);
                outsider.Send("test", 10001, GameWire.Encode(new GamePacket { Message = GameMessage.Hello, Token = 9 }));
                host.Update(0, 0);
                check(host.Connected && host.Engine.Tick == tick, "a third player cannot replace the locked session peer");
                outsider.Dispose();
                guest.Stop(); host.Update(0, 0);
                check(!host.Connected && host.Mode == GameSessionMode.Idle && network.OpenLinks == 0, "leaving closes both links through the peer notification");
            }
            using (var solo = new GameSession(network))
            {
                solo.StartSolo(7); solo.Update(2, 100);
                check(solo.Engine.Tick == 3 && network.OpenLinks == 0, "solo advances fixed ticks without creating a network socket");
            }
            VerifyTimeout(check);
            VerifyWrongSession(check);
            VerifyHandshakeAndFinalRecovery(check);
            using (var invalidJoin = new GameSession(new TriLink.Plugins.GameLink.UdpGameLinkFactory()))
            {
                var rejected = false;
                try { invalidJoin.Join("localhost", 47830, 0); } catch (ArgumentException) { rejected = true; }
                check(rejected && invalidJoin.LocalPort == 0 && invalidJoin.Mode == GameSessionMode.Idle,
                    "address validation failure releases the newly opened adapter");
            }
        }

        private static void VerifyTimeout(Action<bool, string> check)
        {
            var network = new TestNetwork();
            using (var host = new GameSession(network))
            using (var guest = new GameSession(network))
            {
                host.Host(11001, 2); guest.Join("test", 11001, 11002); Pump(host, guest, 5);
                network.Silent = true;
                host.Update(0, 6000); guest.Update(0, 6000);
                check(host.Mode == GameSessionMode.Idle && guest.Mode == GameSessionMode.Idle && network.OpenLinks == 0,
                    "actual elapsed silence terminates instead of advancing an unsynchronized game");
            }
        }
        private static void VerifyWrongSession(Action<bool, string> check)
        {
            var network = new TestNetwork();
            using (var host = new GameSession(network))
            using (var guest = new GameSession(network))
            {
                host.Host(12001, 3); guest.Join("test", 12001, 12002); Pump(host, guest, 5);
                var before = guest.Engine.Tick;
                for (var invalidPart = 0; invalidPart < 3; ++invalidPart)
                {
                    network.Inject(12002, new GameDatagram { PeerAddress = "test", PeerPort = invalidPart == 2 ? 12003 : 12001,
                        Payload = GameWire.Encode(new GamePacket { Message = GameMessage.Frames,
                            Session = invalidPart == 0 ? (network.Session == 1 ? 2U : 1U) : network.Session,
                            Token = invalidPart == 1 ? (network.Token == 1 ? 2U : 1U) : network.Token,
                            Tick = before + 1, Count = 1, Inputs = new byte[] { 16, 16 } }) });
                    guest.Update(0, 0);
                    check(guest.Engine.Tick == before, "session, token and endpoint filtering are independently enforced: " + invalidPart);
                }
                network.Silent = true;
                for (var i = 0; i < 45 && host.Mode != GameSessionMode.Idle; ++i) { host.Update(0, 100); }
                check(host.Mode == GameSessionMode.Idle && host.Status.Contains("恢复窗口"), "fixed input history exhaustion ends the session rather than dropping committed ticks");
            }
        }

        private static void VerifyHandshakeAndFinalRecovery(Action<bool, string> check)
        {
            foreach (var message in new[] { GameMessage.Hello, GameMessage.Start, GameMessage.Ready })
            {
                var network = new TestNetwork { DropMessage = message, DropMessageCount = 1 };
                using (var host = new GameSession(network))
                using (var guest = new GameSession(network))
                {
                    host.Host(13001, 7); guest.Join("test", 13001, 13002); Pump(host, guest, 25);
                    check(host.Connected && guest.Connected, "handshake recovers a lost " + message);
                    guest.Update(0, 0);
                    check(Hash(host.Engine) == Hash(guest.Engine), "handshake retry does not reset the live world: " + message);
                }
            }
            var finalNetwork = new TestNetwork();
            using (var host = new GameSession(finalNetwork))
            using (var guest = new GameSession(finalNetwork))
            {
                host.Host(14001, 9); guest.Join("test", 14001, 14002); Pump(host, guest, 5);
                host.Update(0, 0);
                for (var player = 0; player < 2; ++player)
                {
                    host.Engine.Players[player].Active = guest.Engine.Players[player].Active = false;
                    host.Engine.Players[player].Lives = guest.Engine.Players[player].Lives = 0;
                }
                finalNetwork.DropMessage = GameMessage.Frames; finalNetwork.DropMessageCount = 1;
                Pump(host, guest, 65);
                check(host.Engine.GameOver && guest.Engine.GameOver && Hash(host.Engine) == Hash(guest.Engine),
                    "lost final tick is resent and both worlds stop at the same GameOver");
                check(host.Connected && guest.Connected && !host.Running && !guest.Running,
                    "finished network sessions continue acknowledgments beyond the silence timeout");
                host.Update(0, 0);
                finalNetwork.DropMessage = GameMessage.Bye; finalNetwork.DropMessageCount = 1;
                guest.Stop(); host.Update(0, 6000);
                check(host.Mode == GameSessionMode.Idle && finalNetwork.OpenLinks == 0,
                    "lost leave notification is resolved by bounded silence timeout");
            }
        }

        private static void Pump(GameSession host, GameSession guest, int iterations)
        {
            for (var i = 0; i < iterations; ++i)
            {
                guest.Update((byte)(i % 12 < 6 ? 1 : 2), 100);
                host.Update((byte)(i % 10 < 5 ? 4 : 8), 100);
                guest.Update(0, 0);
            }
        }
        internal static ulong Hash(GameEngine engine)
        {
            ulong value = 14695981039346656037UL;
            Add(ref value, (int)engine.Tick); Add(ref value, engine.Score); Add(ref value, engine.Wave); Add(ref value, engine.GameOver ? 1 : 0);
            foreach (var player in engine.Players)
            { Add(ref value, player.Active ? 1 : 0); Add(ref value, player.X); Add(ref value, player.Y); Add(ref value, player.Lives); Add(ref value, player.InvulnerableTicks); }
            foreach (var entities in new[] { engine.Enemies, engine.Bullets, engine.EnemyBullets, engine.Explosions })
            { foreach (var entity in entities) { Add(ref value, entity.Active ? 1 : 0); Add(ref value, entity.X); Add(ref value, entity.Y);
                Add(ref value, entity.Vx); Add(ref value, entity.Vy); Add(ref value, entity.Health); Add(ref value, entity.Kind); Add(ref value, entity.Age); } }
            return value;
        }
        private static void Add(ref ulong hash, int value) { unchecked { hash ^= (uint)value; hash *= 1099511628211UL; } }

        // Fault injection stays in the test assembly. Production receive work and histories are bounded.
        private sealed class TestNetwork : IGameLinkFactory
        {
            private readonly Dictionary<int, Link> _links = new Dictionary<int, Link>();
            private int _sends;
            public int DropEvery, DuplicateEvery, ReorderEvery;
            public GameMessage DropMessage;
            public int DropMessageCount;
            public uint Session, Token;
            public bool Silent;
            public int OpenLinks { get { return _links.Count; } }
            public IGameLink CreateLink() { return new Link(this); }
            public void Inject(int port, GameDatagram datagram) { _links[port].Queue.Add(datagram); }
            private sealed class Link : IGameLink
            {
                private readonly TestNetwork _owner;
                internal readonly List<GameDatagram> Queue = new List<GameDatagram>();
                public Link(TestNetwork owner) { _owner = owner; }
                public int LocalPort { get; private set; }
                public bool IsOpen { get { return LocalPort != 0; } }
                public void Open(int port) { LocalPort = port; _owner._links.Add(port, this); }
                public string NormalizePeerAddress(string address) { return address; }
                public bool TryReceive(out GameDatagram datagram)
                { datagram = null; if (Queue.Count == 0) { return false; } datagram = Queue[0]; Queue.RemoveAt(0); return true; }
                public void Send(string address, int port, byte[] payload)
                {
                    ++_owner._sends; Link receiver;
                    GamePacket packet;
                    if (GameWire.TryDecode(payload, out packet) && packet.Message == GameMessage.Start)
                    { _owner.Session = packet.Session; _owner.Token = packet.Token; }
                    if (payload[3] == (byte)_owner.DropMessage && _owner.DropMessageCount > 0)
                    { --_owner.DropMessageCount; return; }
                    if (_owner.Silent || !_owner._links.TryGetValue(port, out receiver)
                        || (_owner.DropEvery != 0 && _owner._sends % _owner.DropEvery == 0)) { return; }
                    var datagram = new GameDatagram { PeerAddress = "test", PeerPort = LocalPort, Payload = (byte[])payload.Clone() };
                    if (_owner.ReorderEvery != 0 && _owner._sends % _owner.ReorderEvery == 0) { receiver.Queue.Insert(0, datagram); }
                    else { receiver.Queue.Add(datagram); }
                    if (_owner.DuplicateEvery != 0 && _owner._sends % _owner.DuplicateEvery == 0) { receiver.Queue.Add(datagram); }
                }
                public void Dispose() { if (IsOpen) { _owner._links.Remove(LocalPort); LocalPort = 0; } Queue.Clear(); }
            }
        }
    }
}
