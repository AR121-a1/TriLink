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
                CatchUp(host, new[] { guest }, 15);
                guest.Update(0, 0);
                var recovered = host.Mode == GameSessionMode.Host && guest.Mode == GameSessionMode.Join
                    && host.Connected && guest.Connected && Hash(host.Engine) == Hash(guest.Engine);
                if (!recovered) { Console.Error.WriteLine("RECOVERY " + Trace(host) + " | " + Trace(guest)); }
                check(recovered, "bounded resend recovers loss, duplicate and reordered packets without divergent ticks");
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
            VerifyWaitingForPlayers(check);
            foreach (var players in new[] { 3, 4, 6 }) { VerifyMultiplePlayers(check, players); }
            VerifyPeerIsolationAndRecovery(check);
            VerifySequenceAndNeutralTimeout(check);
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
                            Tick = before + 1, Count = 1, PlayerCount = 2, Inputs = new byte[] { 16, 16 } }) });
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

        private static void VerifyWaitingForPlayers(Action<bool, string> check)
        {
            var network = new TestNetwork();
            using (var host = new GameSession(network))
            using (var first = new GameSession(network))
            using (var last = new GameSession(network))
            {
                host.Host(15001, 11, 3); first.Join("test", 15001, 15002);
                check(first.PlayerCount == 0 && first.LocalPlayerIndex == -1 && !first.Connected, "an unassigned join cannot guess its seat or participant count");
                Pump(host, first, 80);
                check(host.Engine == null && !host.Running && host.ReadyPlayerCount == 2 && host.PlayerCount == 3,
                    "a three-player host does not simulate before every guest is ready");
                check(first.Connected && !first.Running && first.Engine.Tick == 0 && first.LocalPlayerIndex == 1,
                    "assigned guests remain at tick zero while a third participant is absent");
                check(host.Mode == GameSessionMode.Host && first.Mode == GameSessionMode.Join,
                    "Start/Ready heartbeats preserve a waiting guest beyond five seconds");
                last.Join("test", 15001, 15003); var guests = new[] { first, last }; PumpMany(host, guests, 10);
                check(host.Running && first.Running && last.Running && host.ReadyPlayerCount == 3,
                    "the final Ready releases the complete room to simulation");
                var engine = first.Engine; var hash = Hash(engine); var start = network.StartFor(15002);
                foreach (var changedField in new[] { "seed", "count", "seat", "none" })
                {
                    var replay = new GamePacket { Message = GameMessage.Start, Session = start.Session, Token = start.Token,
                        Tick = changedField == "seed" ? start.Tick + 1 : start.Tick,
                        PlayerCount = changedField == "count" ? (byte)2 : start.PlayerCount,
                        PlayerIndex = changedField == "seat" ? (byte)2 : start.PlayerIndex };
                    network.Inject(15002, new GameDatagram { PeerAddress = "test", PeerPort = 15001, Payload = GameWire.Encode(replay) });
                    first.Update(0, 0);
                    check(first.Engine == engine && Hash(first.Engine) == hash && first.PlayerCount == 3 && first.LocalPlayerIndex == 1,
                        "duplicate or contradictory Start cannot reset a live assigned world: " + changedField);
                }
                first.Stop(); host.Update(0, 0); last.Update(0, 0);
                check(network.OpenLinks == 0, "leaving a three-player room propagates termination to every peer");
            }
            check(Hash(new GameEngine(1U, true)) != Hash(new GameEngine(2U, true)), "world hashes include the seed before any random spawn");
            check(Hash(new GameEngine(1U, 2)) != Hash(new GameEngine(1U, 3)), "world hashes include the declared player count");
            foreach (var players in new[] { 1, 7 })
            {
                using (var invalid = new GameSession(network))
                {
                    var rejected = false; try { invalid.Host(15001, 1, players); } catch (ArgumentOutOfRangeException) { rejected = true; }
                    check(rejected && invalid.LocalPort == 0 && network.OpenLinks == 0, "invalid host size opens no adapter: " + players);
                }
            }
        }

        private static void VerifyMultiplePlayers(Action<bool, string> check, int players)
        {
            var network = new TestNetwork(); var guests = new GameSession[players - 1];
            using (var host = new GameSession(network))
            {
                try
                {
                    var port = 16000 + players * 20; host.Host(port, 200U + (uint)players, players);
                    for (var i = 0; i < guests.Length; ++i) { guests[i] = new GameSession(network); guests[i].Join("test", port, port + i + 1); }
                    PumpMany(host, guests, 20);
                    var seats = new bool[GameEngine.MaxPlayers]; seats[0] = true;
                    foreach (var guest in guests)
                    {
                        check(guest.Connected && guest.Running && guest.PlayerCount == players && !seats[guest.LocalPlayerIndex],
                            players + "-player handshake assigns one unique active seat: " + guest.LocalPlayerIndex);
                        seats[guest.LocalPlayerIndex] = true;
                        check(Hash(host.Engine) == Hash(guest.Engine), players + "-player worlds agree with distinct per-seat input");
                    }
                    check(host.Engine.Players[0].X != host.Engine.Players[1].X, players + "-player local and remote controls do not collapse into one input");
                    // A prime drop interval avoids permanently targeting one seat in a 10-packet six-player cycle.
                    network.DropEvery = 13; network.DuplicateEvery = 7; network.ReorderEvery = 3;
                    PumpMany(host, guests, 100);
                    network.DropEvery = network.DuplicateEvery = network.ReorderEvery = 0; CatchUp(host, guests, 20);
                    foreach (var guest in guests) { check(Hash(host.Engine) == Hash(guest.Engine), players + "-player bounded replay recovers per-peer loss/reordering"); }
                    var outsider = network.CreateLink(); outsider.Open(port + 10);
                    outsider.Send("test", port, GameWire.Encode(new GamePacket { Message = GameMessage.Hello, Token = 55 })); host.Update(0, 0);
                    check(host.Connected && host.PlayerCount == players && host.ReadyPlayerCount == players, players + "-player live room rejects mid-game admission");
                    outsider.Dispose(); guests[guests.Length - 1].Stop(); host.Update(0, 0);
                    foreach (var guest in guests) { guest.Update(0, 0); }
                    check(network.OpenLinks == 0, players + "-player exit releases all adapters");
                    check(host.ReadyPlayerCount == 0 && guests[0].ReadyPlayerCount == 0,
                        players + "-player stopped sessions no longer report ready participants");
                }
                finally { foreach (var guest in guests) { if (guest != null) { guest.Dispose(); } } }
            }
        }

        private static void VerifyPeerIsolationAndRecovery(Action<bool, string> check)
        {
            var network = new TestNetwork();
            using (var host = new GameSession(network))
            using (var first = new GameSession(network))
            using (var last = new GameSession(network))
            {
                host.Host(18001, 33, 3); first.Join("test", 18001, 18002); last.Join("test", 18001, 18003);
                var guests = new[] { first, last }; PumpMany(host, guests, 5);
                var start = network.StartFor(18002); var beforeY = host.Engine.Players[1].Y;
                foreach (var wrongField in new[] { "seat", "count" })
                {
                    network.Inject(18001, new GameDatagram { PeerAddress = "test", PeerPort = 18002, Payload = GameWire.Encode(new GamePacket {
                        Message = GameMessage.Input, Session = start.Session, Token = start.Token, Tick = host.Engine.Tick, Sequence = 999,
                        Input = GameEngine.InputDown, PlayerCount = wrongField == "count" ? (byte)2 : (byte)3,
                        PlayerIndex = wrongField == "seat" ? (byte)2 : (byte)1 }) });
                }
                host.Update(0, 100);
                check(host.Engine.Players[1].Y == beforeY, "a valid endpoint/token cannot impersonate another seat or participant count");
                network.DropFramesPort = 18003; PumpMany(host, guests, 10);
                check(first.Engine.Tick == host.Engine.Tick && last.Engine.Tick < first.Engine.Tick,
                    "one slow peer does not reuse or block the fast peer's independent ACK");
                network.DropFramesPort = 0; CatchUp(host, guests, 5);
                check(Hash(host.Engine) == Hash(first.Engine) && Hash(host.Engine) == Hash(last.Engine), "the isolated slow peer catches up without skipped ticks");
                network.DropFramesPort = 18003; network.DropToPort = 18003; PumpMany(host, guests, 45);
                check(host.Mode == GameSessionMode.Idle && host.Status.Contains("恢复窗口"), "the slowest peer bounds the shared fixed history window");
                first.Update(0, 0); last.Update(0, 6000);
                check(network.OpenLinks == 0, "a dropped termination is eventually closed by the guest silence deadline");
            }
            var waitingNetwork = new TestNetwork();
            using (var host = new GameSession(waitingNetwork))
            using (var guest = new GameSession(waitingNetwork))
            {
                host.Host(18101, 2, 3); guest.Join("test", 18101, 18102); Pump(host, guest, 5); host.Update(0, 0);
                waitingNetwork.SilentFromPort = 18102; host.Update(0, 6000); guest.Update(0, 0);
                check(host.Mode == GameSessionMode.Idle && guest.Mode == GameSessionMode.Idle && waitingNetwork.OpenLinks == 0,
                    "a waiting participant's actual silence ends the incomplete room and releases ports");
            }
        }

        private static void VerifySequenceAndNeutralTimeout(Action<bool, string> check)
        {
            var network = new TestNetwork();
            using (var host = new GameSession(network))
            using (var guest = new GameSession(network))
            {
                host.Host(19001, 7); guest.Join("test", 19001, 19002);
                host.Update(0, 0); guest.Update(0, 0); host.Update(0, 0);
                var start = network.StartFor(19002); var initialX = host.Engine.Players[1].X;
                InjectInput(network, 19001, 19002, start, 0, uint.MaxValue, GameEngine.InputRight); host.Update(0, 100);
                check(host.Engine.Players[1].X == initialX + 9, "the first sequence can occupy the upper unsigned boundary");
                InjectInput(network, 19001, 19002, start, host.Engine.Tick, 0, GameEngine.InputLeft); host.Update(0, 100);
                check(host.Engine.Players[1].X == initialX, "sequence wrap from uint.MaxValue to zero advances input state");
                var initialY = host.Engine.Players[1].Y;
                InjectInput(network, 19001, 19002, start, host.Engine.Tick, uint.MaxValue, GameEngine.InputDown); host.Update(0, 100);
                check(host.Engine.Players[1].Y == initialY, "a reordered prior sequence cannot replace newer controls even with a current ACK");
                InjectInput(network, 19001, 19002, start, host.Engine.Tick, 1, GameEngine.InputDown); host.Update(0, 100);
                check(host.Engine.Players[1].Y == initialY + 9, "the next valid sequence controls its own seat");
                var y = host.Engine.Players[1].Y; host.Update(0, 400);
                check(host.Engine.Players[1].Y == y && host.Connected, "stale remote controls become neutral while simulation catch-up remains bounded");
                InjectInput(network, 19001, 19002, start, uint.MaxValue, 2, GameEngine.InputDown); host.Update(0, 100);
                check(host.Engine.Players[1].Y == y && host.Connected, "a future ACK cannot move controls or escape the host's committed tick range");
            }
        }
        private static void InjectInput(TestNetwork network, int hostPort, int guestPort, GamePacket start, uint tick, uint sequence, byte input)
        {
            network.Inject(hostPort, new GameDatagram { PeerAddress = "test", PeerPort = guestPort, Payload = GameWire.Encode(new GamePacket {
                Message = GameMessage.Input, Session = start.Session, Token = start.Token, Tick = tick, Sequence = sequence, Input = input,
                PlayerCount = start.PlayerCount, PlayerIndex = start.PlayerIndex }) });
        }
        private static void PumpMany(GameSession host, GameSession[] guests, int iterations)
        {
            for (var i = 0; i < iterations && host.Mode != GameSessionMode.Idle; ++i)
            {
                for (var player = 0; player < guests.Length; ++player) { guests[player].Update((byte)(1 << ((player + 1) % 5)), 100); }
                host.Update(GameEngine.InputLeft, 100);
                foreach (var guest in guests) { guest.Update(0, 0); }
            }
        }
        private static void CatchUp(GameSession host, GameSession[] guests, int iterations)
        {
            for (var i = 0; i < iterations; ++i)
            {
                foreach (var guest in guests) { guest.Update(0, 100); }
                // Keep the 10 Hz host sender scheduled: zero elapsed cannot retransmit a lost final batch.
                host.Update(0, 100); foreach (var guest in guests) { guest.Update(0, 0); }
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
            Add(ref value, (int)engine.Seed); Add(ref value, engine.PlayerCount); Add(ref value, engine.Cooperative ? 1 : 0);
            Add(ref value, (int)engine.Tick); Add(ref value, engine.Score); Add(ref value, engine.Wave); Add(ref value, engine.GameOver ? 1 : 0);
            foreach (var player in engine.Players)
            { Add(ref value, player.Active ? 1 : 0); Add(ref value, player.X); Add(ref value, player.Y); Add(ref value, player.Lives); Add(ref value, player.InvulnerableTicks); }
            foreach (var entities in new[] { engine.Enemies, engine.Bullets, engine.EnemyBullets, engine.Explosions })
            { foreach (var entity in entities) { Add(ref value, entity.Active ? 1 : 0); Add(ref value, entity.X); Add(ref value, entity.Y);
                Add(ref value, entity.Vx); Add(ref value, entity.Vy); Add(ref value, entity.Health); Add(ref value, entity.Kind); Add(ref value, entity.Age); } }
            return value;
        }
        private static void Add(ref ulong hash, int value) { unchecked { hash ^= (uint)value; hash *= 1099511628211UL; } }
        private static string Trace(GameSession session)
        {
            var peers = (Array)typeof(GameSession).GetField("_peers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(session);
            string acks = string.Empty;
            foreach (var peer in peers) { acks += peer.GetType().GetField("Ack").GetValue(peer) + ","; }
            return session.Mode + " connected=" + session.Connected + " running=" + session.Running + " tick=" + session.Engine.Tick
                + " hash=" + Hash(session.Engine) + " ack=[" + acks + "] status=" + session.Status;
        }

        // Fault injection stays in the test assembly. Production receive work and histories are bounded.
        private sealed class TestNetwork : IGameLinkFactory
        {
            private readonly Dictionary<int, Link> _links = new Dictionary<int, Link>();
            private int _sends;
            public int DropEvery, DuplicateEvery, ReorderEvery;
            public int DropFramesPort, SilentFromPort, DropToPort;
            private readonly Dictionary<int, GamePacket> _starts = new Dictionary<int, GamePacket>();
            public GameMessage DropMessage;
            public int DropMessageCount;
            public uint Session, Token;
            public bool Silent;
            public int OpenLinks { get { return _links.Count; } }
            public IGameLink CreateLink() { return new Link(this); }
            public GamePacket StartFor(int port) { return _starts[port]; }
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
                    { _owner.Session = packet.Session; _owner.Token = packet.Token; _owner._starts[port] = packet; }
                    if (payload[3] == (byte)_owner.DropMessage && _owner.DropMessageCount > 0)
                    { --_owner.DropMessageCount; return; }
                    if (_owner.Silent || _owner.SilentFromPort == LocalPort || _owner.DropToPort == port
                        || (_owner.DropFramesPort == port && payload[3] == (byte)GameMessage.Frames)
                        || !_owner._links.TryGetValue(port, out receiver)
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
