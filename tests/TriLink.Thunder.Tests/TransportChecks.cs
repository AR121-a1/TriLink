using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using TriLink.Plugin;
using TriLink.Plugins.GameLink;

internal static class TransportChecks
{
    public static void Run(Action<bool, string> check)
    {
        var context = new Context();
        var plugin = new GameLinkPlugin();
        plugin.Configure(context);
        plugin.Start();
        plugin.Stop();
        check(context.Factory != null, "game link plugin provides its factory without dependencies");
        check(PluginServiceContract.GetName<IGameLinkFactory>() == "trilink.game-link",
            "game transport has a stable service identity");
        check(GameDatagram.MaxPayloadLength == 128, "game transport payload ceiling is 128 bytes");

        using (var first = context.Factory.CreateLink())
        using (var second = context.Factory.CreateLink())
        {
            GameDatagram datagram;
            check(!first.IsOpen && first.LocalPort == 0 && !first.TryReceive(out datagram),
                "factory creates a closed link with no receive work");
            check(first.NormalizePeerAddress("127.1") == "127.0.0.1" && !first.IsOpen,
                "peer normalization returns the receive address format without opening a socket");
            check(Throws<ArgumentException>(() => first.NormalizePeerAddress("localhost"))
                && Throws<ArgumentException>(() => first.NormalizePeerAddress("::1"))
                && Throws<ArgumentException>(() => first.NormalizePeerAddress(null)) && !first.IsOpen,
                "invalid peer addresses are rejected before a session opens");
            check(Throws<InvalidOperationException>(() => first.Send("127.0.0.1", 1, new byte[1])),
                "sending requires an explicitly opened game session");
            check(Throws<ArgumentOutOfRangeException>(() => first.Open(-1))
                && Throws<ArgumentOutOfRangeException>(() => first.Open(65536)),
                "local port validation precedes binding");
            first.Open(0);
            second.Open(0);
            check(first.IsOpen && second.IsOpen && first.LocalPort > 0 && second.LocalPort > 0
                && first.LocalPort != second.LocalPort, "two local sessions bind distinct ephemeral ports");
            check(!first.TryReceive(out datagram), "empty receive is nonblocking");
            check(Throws<InvalidOperationException>(() => first.Open(0)),
                "an open link cannot silently replace its socket");

            var payload = Enumerable.Range(0, 128).Select(value => (byte)value).ToArray();
            first.Send("127.0.0.1", second.LocalPort, payload);
            var received = Receive(second);
            check(received.Payload.SequenceEqual(payload) && received.PeerAddress == "127.0.0.1"
                && received.PeerPort == first.LocalPort, "loopback preserves all 128 bytes and sender endpoint");
            second.Send(received.PeerAddress, received.PeerPort, new byte[] { 7, 8, 9 });
            check(Receive(first).Payload.SequenceEqual(new byte[] { 7, 8, 9 }),
                "two independently opened game links exchange a reply");
            first.Send("127.0.0.1", second.LocalPort, new byte[] { 99 });
            check(Receive(second).Payload.SequenceEqual(new byte[] { 99 })
                && received.Payload.SequenceEqual(payload), "received payload owns its buffer across later packets");
            first.Send("127.0.0.1", second.LocalPort, new byte[0]);
            check(Receive(second).Payload.Length == 0, "empty transport payload is preserved for protocol validation");

            check(Throws<ArgumentException>(() => first.Send("localhost", second.LocalPort, payload))
                && Throws<ArgumentException>(() => first.Send("::1", second.LocalPort, payload)),
                "DNS names and IPv6 addresses are rejected");
            check(Throws<ArgumentOutOfRangeException>(() => first.Send("127.0.0.1", 0, payload))
                && Throws<ArgumentOutOfRangeException>(() => first.Send("127.0.0.1", 65536, payload)),
                "destination ports must be 1 through 65535");
            check(Throws<ArgumentNullException>(() => first.Send("127.0.0.1", second.LocalPort, null))
                && Throws<ArgumentException>(() => first.Send("127.0.0.1", second.LocalPort, new byte[129])),
                "null and oversized outgoing payloads are rejected");

            using (var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                sender.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                var target = new IPEndPoint(IPAddress.Loopback, second.LocalPort);
                sender.SendTo(new byte[129], target);
                sender.SendTo(new byte[] { 12 }, target);
                check(Receive(second).Payload.SequenceEqual(new byte[] { 12 }),
                    "129 byte marker datagram is dropped without truncating it into game input");
                sender.SendTo(new byte[4096], target);
                sender.SendTo(new byte[] { 13 }, target);
                check(Receive(second).Payload.SequenceEqual(new byte[] { 13 }),
                    "large UDP datagram is discarded and a later valid packet remains readable");
                for (int i = 0; i < 8; ++i) { sender.SendTo(new byte[129], target); }
                sender.SendTo(new byte[] { 14 }, target);
                check(!second.TryReceive(out datagram), "one receive call stops after eight invalid packets");
                check(Receive(second).Payload.SequenceEqual(new byte[] { 14 }),
                    "invalid packet budget leaves the subsequent valid packet for the next call");
            }

            using (var occupied = context.Factory.CreateLink())
            {
                check(Throws<SocketException>(() => occupied.Open(first.LocalPort)) && !occupied.IsOpen,
                    "a failed bind does not keep an open transport socket");
                occupied.Open(0);
                check(occupied.IsOpen, "a link remains usable after a failed bind");
            }
            int releasedPort = first.LocalPort;
            first.Dispose();
            first.Dispose();
            check(!first.IsOpen && first.LocalPort == 0 && !first.TryReceive(out datagram),
                "dispose is repeatable and stops receive work");
            check(Throws<ObjectDisposedException>(() => first.Open(0))
                && Throws<ObjectDisposedException>(() => first.Send("127.0.0.1", second.LocalPort, payload)),
                "disposed sessions cannot reopen or send");
            using (var replacement = context.Factory.CreateLink())
            {
                replacement.Open(releasedPort);
                second.Send("127.0.0.1", releasedPort, new byte[] { 15 });
                check(Receive(replacement).Payload.SequenceEqual(new byte[] { 15 }),
                    "disposing releases the bound port for a new session with no background owner");
            }
        }
    }

    private static GameDatagram Receive(IGameLink link)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.ElapsedMilliseconds < 2000)
        {
            GameDatagram datagram;
            if (link.TryReceive(out datagram)) { return datagram; }
            Thread.Sleep(1);
        }
        throw new TimeoutException("Local UDP test did not receive a datagram within two seconds.");
    }

    private static bool Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return true; }
        return false;
    }

    private sealed class Context : IPluginContext
    {
        public IGameLinkFactory Factory { get; private set; }
        public string PluginId { get { return "trilink.game-link"; } }
        public IHostEnvironment Environment { get { return null; } }
        public TService GetRequired<TService>() where TService : class
        {
            throw new InvalidOperationException("The transport provider must not resolve other services.");
        }
        public void Provide<TService>(TService service) where TService : class
        {
            Factory = service as IGameLinkFactory;
        }
        public void Defer(Action cleanup) { }
        public void Log(string message) { }
    }
}
