using System;
using System.Collections.Generic;
using TriLink.Plugin;
using TriLink.Plugins.Thunder;

namespace TriLink.Thunder.Tests
{
    // A future USB provider must fail closed just like the UDP provider.
    internal static class FailureChecks
    {
        public static void Run(Action<bool, string> check)
        {
            var factory = new FaultFactory { FailCreate = true };
            using (var session = new GameSession(factory))
            {
                check(Throws(() => session.Host(17001, 3)) && session.Mode == GameSessionMode.Idle,
                    "factory failure cannot leave a game marked open");
            }
            factory = new FaultFactory { FailOpen = true };
            using (var session = new GameSession(factory))
            {
                check(Throws(() => session.Host(17001, 3)) && factory.Disposals == 1
                    && session.Mode == GameSessionMode.Idle && session.LocalPort == 0,
                    "failed adapter open is disposed and leaves an idle game");
            }
            factory = new FaultFactory { FailSend = true };
            using (var session = new GameSession(factory))
            {
                check(Throws(() => session.Join("test", 17001, 17002)) && factory.Disposals == 1
                    && session.Mode == GameSessionMode.Idle && session.LocalPort == 0,
                    "initial handshake send failure releases the joining adapter");
            }
            factory = new FaultFactory();
            using (var session = new GameSession(factory))
            {
                session.Host(17001, 3);
                factory.FailReceive = true;
                session.Update(0, 100);
                check(session.Mode == GameSessionMode.Idle && !session.Connected && factory.Disposals == 1,
                    "receive failure closes the host instead of leaving a live timer owner");
            }
            factory = new FaultFactory();
            using (var session = new GameSession(factory))
            {
                session.Host(17001, 3);
                factory.Link.Incoming.Enqueue(new GameDatagram { PeerAddress = "test", PeerPort = 17002,
                    Payload = GameWire.Encode(new GamePacket { Message = GameMessage.Hello, Token = 9 }) });
                factory.FailSend = true;
                session.Update(0, 100);
                check(session.Mode == GameSessionMode.Idle && !session.Connected && factory.Disposals == 1,
                    "reply failure during admission terminates and releases the room");
                session.Stop();
                check(factory.Disposals == 1, "repeated stop does not dispose a failed adapter twice");
            }
            factory = new FaultFactory();
            using (var session = new GameSession(factory))
            {
                session.Join("test", 17001, 17002);
                factory.Link.Incoming.Enqueue(new GameDatagram { PeerAddress = "test", PeerPort = 17001,
                    Payload = GameWire.Encode(new GamePacket { Message = GameMessage.Start, Session = 7,
                        Token = factory.LastSent.Token, Tick = 3, PlayerCount = 2, PlayerIndex = 1 }) });
                session.Update(0, 0);
                factory.FailSend = true;
                session.Stop();
                check(session.Mode == GameSessionMode.Idle && factory.Disposals == 1 && session.LocalPort == 0,
                    "best-effort departure failure still releases its adapter");
            }
            factory = new FaultFactory();
            var disposed = new GameSession(factory);
            disposed.Dispose(); disposed.Dispose();
            check(Throws(() => disposed.Host(17001, 3)) && Throws(() => disposed.StartSolo(3))
                && Throws(() => disposed.Update(0, 100)) && factory.Link == null,
                "disposed games cannot restart or create a new I/O owner");
        }

        private static bool Throws(Action action)
        { try { action(); return false; } catch (InvalidOperationException) { return true; } }

        private sealed class FaultFactory : IGameLinkFactory
        {
            public bool FailCreate, FailOpen, FailSend, FailReceive;
            public int Disposals;
            public FaultLink Link;
            public GamePacket LastSent;
            public IGameLink CreateLink()
            {
                if (FailCreate) { throw new InvalidOperationException("fake factory failure"); }
                Link = new FaultLink(this); return Link;
            }

            public sealed class FaultLink : IGameLink
            {
                private readonly FaultFactory _owner;
                private bool _disposed;
                public readonly Queue<GameDatagram> Incoming = new Queue<GameDatagram>();
                public FaultLink(FaultFactory owner) { _owner = owner; }
                public bool IsOpen { get; private set; }
                public int LocalPort { get; private set; }
                public string NormalizePeerAddress(string value) { return value; }
                public void Open(int port)
                {
                    if (_owner.FailOpen) { throw new InvalidOperationException("fake open failure"); }
                    IsOpen = true; LocalPort = port;
                }
                public bool TryReceive(out GameDatagram datagram)
                {
                    if (_owner.FailReceive) { throw new InvalidOperationException("fake receive failure"); }
                    datagram = Incoming.Count == 0 ? null : Incoming.Dequeue(); return datagram != null;
                }
                public void Send(string address, int port, byte[] payload)
                {
                    if (_owner.FailSend) { throw new InvalidOperationException("fake send failure"); }
                    GamePacket decoded;
                    if (!GameWire.TryDecode(payload, out decoded)) { throw new InvalidOperationException("invalid outgoing packet"); }
                    _owner.LastSent = decoded;
                }
                public void Dispose()
                {
                    if (_disposed) { return; }
                    _disposed = true; _owner.Disposals++; IsOpen = false; LocalPort = 0; Incoming.Clear();
                }
            }
        }
    }
}
