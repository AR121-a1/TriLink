using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TriLink.Core;
using TriLink.MinClient.Serial;
using TriLink.Plugin;
using TriLink.Plugins.Serial;

internal static partial class Program
{
    private static int _checks;
    private static void Check(bool condition, string description)
    {
        ++_checks;
        if (!condition) throw new Exception(description);
        Console.WriteLine("PASS " + description);
    }

    private static void WaitFor(Func<bool> condition, string description)
    {
        if (!SpinWait.SpinUntil(condition, 2500)) throw new TimeoutException(description);
    }

    private static object Private(TriLinkSerialWatcher watcher, string name)
    {
        return typeof(TriLinkSerialWatcher).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(watcher);
    }

    private static void Idle(TriLinkSerialWatcher watcher)
    {
        WaitFor(() => { lock (Private(watcher, "_sync")) return Private(watcher, "_activeScanSession") == null; }, "scan completes");
    }

    private static void Scan(TriLinkSerialWatcher watcher) { watcher.RequestScan(); Idle(watcher); }

    private static void Reject<T>(Action action, string description) where T : Exception
    {
        bool rejected = false;
        try { action(); } catch (T) { rejected = true; }
        Check(rejected, description);
    }

    private static void CacheRefresh()
    {
        var io = new FakeIo { Capabilities = 3, DisplayName = "old" };
        using (var watcher = new TriLinkSerialWatcher(io))
        {
            int arrivals = 0, removals = 0;
            watcher.DeviceArrived += (_, __) => ++arrivals;
            watcher.DeviceRemoved += (_, __) => ++removals;
            watcher.ResumePolling(); Idle(watcher);
            Check(watcher.Devices.Single().Capabilities == 3, "initial HELLO retains old firmware capability");
            for (int i = 0; i < 3; ++i) Scan(watcher);
            Check(io.Opens == 1 && io.Hellos == 1 && arrivals == 1, "normal scans reuse an unchanged identified COM without repeated HELLO");
            watcher.PausePolling();
            io.Capabilities = 64; io.DisplayName = "upgraded";
            watcher.ResumePolling(); Idle(watcher);
            Check(watcher.Devices.Single().Capabilities == 64 && watcher.Devices.Single().DisplayName == "upgraded",
                "manual resume refreshes capability and name on the same COM and MAC");
            Check(io.Opens == 2 && arrivals == 2 && removals == 0, "capability upgrade publishes one changed-device arrival");
            watcher.ExecuteAsync("COM99", "ROOMGET", "00").GetAwaiter().GetResult();
            Check(io.BusinessWrites == 1, "upgraded device now passes the fresh identity guard and receives Room query");
            int opens = io.Opens; Scan(watcher);
            Check(io.Opens == opens, "automatic scanning remains quiet after manual refresh");
            io.PnpDeviceId = "USB\\VID_303A&PID_1001\\NEW"; io.NodeId = "20:00:00:00:00:02";
            Scan(watcher);
            Check(watcher.Devices.Single().NodeId == io.NodeId && watcher.Devices.Single().PnpDeviceId == io.PnpDeviceId,
                "changed PnP identity revalidates a reused COM during automatic scanning");
            Check(arrivals == 3 && removals == 1, "replacement removes the old node before publishing the new node");
            io.FailOpen = true;
            watcher.ResumePolling(); Idle(watcher);
            Check(watcher.Devices.Count == 0 && removals == 2, "failed resumed identity check invalidates the cached device");
        }
    }

    private static void BusinessIdentityFailure()
    {
        foreach (string failure in new[] { "changed-mac", "lost-capability", "open", "read", "timeout" })
        {
            var io = new FakeIo();
            using (var watcher = new TriLinkSerialWatcher(io))
            {
                int removals = 0;
                watcher.DeviceRemoved += (_, __) => ++removals;
                watcher.ResumePolling(); Idle(watcher);
                if (failure == "changed-mac") io.NodeId = "20:00:00:00:00:02";
                if (failure == "lost-capability") io.Capabilities = 3;
                if (failure == "open") io.FailOpen = true;
                if (failure == "read") io.FailRead = true;
                if (failure == "timeout") io.NoHelloReply = true;
                bool rejected = false;
                var elapsed = Stopwatch.StartNew();
                try { watcher.ExecuteAsync("COM99", "ROOMGET", "00").GetAwaiter().GetResult(); }
                catch (InvalidOperationException) { rejected = true; }
                catch (IOException) { rejected = true; }
                catch (TimeoutException) { rejected = true; }
                Check(rejected && io.BusinessWrites == 0, failure + " prevents business transmission");
                Check(watcher.Devices.Count == 0 && removals == 1, failure + " invalidates stale identity cache once");
                Check(elapsed.ElapsedMilliseconds < 2300, failure + " identity failure remains bounded");
            }
        }
    }

    private static void StopDuringEnumeration(bool dispose)
    {
        var io = new FakeIo();
        var gate = new Gate();
        io.BeforeEnumeration = call => { if (call == 1) gate.Block(); };
        using (var watcher = new TriLinkSerialWatcher(io))
        {
            int arrivals = 0, statuses = 0;
            watcher.DeviceArrived += (_, __) => ++arrivals;
            watcher.Status += (_, __) => ++statuses;
            watcher.ResumePolling(); gate.WaitEntered();
            var elapsed = Stopwatch.StartNew();
            if (dispose) watcher.Dispose(); else watcher.PausePolling();
            Check(!watcher.IsPolling && elapsed.ElapsedMilliseconds < 1000,
                (dispose ? "Dispose" : "pause") + " returns without waiting for blocked enumeration");
            gate.Release.Set(); Idle(watcher);
            Check(io.Opens == 0 && arrivals == 0 && watcher.Devices.Count == 0 && statuses == 0,
                (dispose ? "Dispose" : "pause") + " cancels enumeration result before new I/O, cache, or events");
            watcher.RequestScan(); Idle(watcher);
            Check(io.Enumerations == 1, "external scan request cannot bypass " + (dispose ? "Dispose" : "pause"));
        }
    }

    private static void StopDuringHandshake(bool dispose)
    {
        var io = new FakeIo { PortCount = 2 };
        var gate = new Gate();
        io.BeforeRead = connection => { if (connection == 1) gate.Block(); };
        using (var watcher = new TriLinkSerialWatcher(io))
        {
            int arrivals = 0, statuses = 0;
            watcher.DeviceArrived += (_, __) => ++arrivals;
            watcher.Status += (_, __) => ++statuses;
            watcher.ResumePolling(); gate.WaitEntered();
            int statusBefore = statuses;
            var elapsed = Stopwatch.StartNew();
            if (dispose) watcher.Dispose(); else watcher.PausePolling();
            Check(elapsed.ElapsedMilliseconds < 1000, (dispose ? "Dispose" : "pause") + " does not wait for current serial read");
            gate.Release.Set(); Idle(watcher);
            Check(io.Opens == 1 && io.Hellos == 1 && io.Closes == 1,
                (dispose ? "Dispose" : "pause") + " closes current read and never starts next candidate");
            Check(arrivals == 0 && watcher.Devices.Count == 0 && statuses == statusBefore,
                (dispose ? "Dispose" : "pause") + " suppresses handshake cache and events after cancellation");
        }
    }

    private static void StopQueuedScan(bool dispose)
    {
        var io = new FakeIo();
        using (var watcher = new TriLinkSerialWatcher(io))
        {
            var owner = (SemaphoreSlim)Private(watcher, "_ioOwner");
            owner.Wait();
            try
            {
                watcher.ResumePolling();
                Check(Private(watcher, "_activeScanSession") != null, "resumed scan queues behind existing I/O");
                if (dispose) watcher.Dispose(); else watcher.PausePolling();
                Idle(watcher);
                Check(io.Enumerations == 0 && io.Opens == 0,
                    (dispose ? "Dispose" : "pause") + " cancels queued scan before enumeration and open");
            }
            finally { owner.Release(); }
        }
    }

    private static void StopDuringOpen(bool dispose)
    {
        var io = new FakeIo { PortCount = 2 };
        var gate = new Gate();
        io.BeforeOpen = connection => { if (connection == 1) gate.Block(); };
        using (var watcher = new TriLinkSerialWatcher(io))
        {
            int arrivals = 0;
            watcher.DeviceArrived += (_, __) => ++arrivals;
            watcher.ResumePolling(); gate.WaitEntered();
            if (dispose) watcher.Dispose(); else watcher.PausePolling();
            gate.Release.Set(); Idle(watcher);
            Check(io.Opens == 1 && io.Hellos == 0 && io.Closes == 1,
                (dispose ? "Dispose" : "pause") + " after an admitted open closes it before HELLO and never opens next candidate");
            Check(watcher.Devices.Count == 0 && arrivals == 0, "canceled open cannot commit cache or events");
        }
    }

    private static void FailureLimitAndResume()
    {
        var io = new FakeIo();
        io.BeforeEnumeration = _ => { throw new IOException("fixture enumeration failure"); };
        using (var watcher = new TriLinkSerialWatcher(io))
        {
            watcher.ResumePolling(); Idle(watcher);
            for (int i = 0; i < 3; ++i) Scan(watcher);
            Check(watcher.IsPolling && watcher.ConsecutiveFailures == 4, "four consecutive enumeration failures retain polling");
            Scan(watcher);
            Check(!watcher.IsPolling && watcher.ConsecutiveFailures == 5, "fifth enumeration failure stops polling");
            int calls = io.Enumerations; watcher.RequestScan(); Idle(watcher);
            Check(io.Enumerations == calls, "external scan cannot bypass failure limiter");
            io.BeforeEnumeration = null;
            watcher.ResumePolling(); Idle(watcher);
            Check(watcher.IsPolling && watcher.ConsecutiveFailures == 0 && watcher.Devices.Count == 1,
                "manual resume clears limiter and immediately revalidates hardware");
        }
    }

    private static void RecognitionDiagnostics()
    {
        foreach (string failure in new[] { "busy", "unavailable", "read", "timeout", "invalid-mac", "invalid-capability", "not-ready", "declined", "wrong-nonce", "oversized" })
        {
            var io = new FakeIo();
            string expected;
            switch (failure)
            {
                case "busy": io.OpenException = new UnauthorizedAccessException("private-driver-detail"); expected = "端口忙或访问受限"; break;
                case "unavailable": io.OpenException = new InvalidOperationException("private-driver-detail"); expected = "端口当前不可用"; break;
                case "read": io.FailRead = true; expected = "端口读写失败或设备已断开"; break;
                case "timeout": io.NoHelloReply = true; expected = "HELLO 握手超时"; break;
                case "invalid-mac": io.NodeId = "11:00:00:00:00:01"; expected = "身份回应无效"; break;
                case "invalid-capability": io.HelloResponse = nonce => "TRILINK/1 DEVICE " + nonce + " 10:00:00:00:00:01 dGVzdA== invalid\n"; expected = "身份回应无效"; break;
                case "not-ready": io.HelloResponse = nonce => "TRILINK/1 ERROR " + nonce + " NOT_READY\n"; expected = "固件尚未就绪"; break;
                case "declined": io.HelloResponse = nonce => "TRILINK/1 ERROR " + nonce + " private-driver-detail\n"; expected = "固件拒绝 HELLO"; break;
                case "wrong-nonce": io.HelloResponse = _ => "TRILINK/1 DEVICE stale 10:00:00:00:00:01 dGVzdA== 00000040\n"; expected = "只收到无关或旧回应"; break;
                default: io.HelloResponse = _ => new string('X', 512) + "\n"; expected = "超过安全长度上限"; break;
            }
            using (var watcher = new TriLinkSerialWatcher(io))
            {
                var statuses = new List<string>();
                watcher.Status += (_, message) => statuses.Add(message);
                watcher.ResumePolling(); Idle(watcher);
                Check(watcher.Devices.Count == 0 && statuses.Any(message => message.StartsWith("COM99 · ") && message.Contains(expected)),
                    failure + " reports the final HELLO diagnosis without creating a device");
                Check(statuses.All(message => !message.Contains("private-driver-detail"))
                    && statuses.Any(message => message.Contains("候选不代表设备已连接")),
                    failure + " does not leak driver or untrusted reply text and distinguishes candidates from connections");
                Scan(watcher);
                Check(statuses.Count(message => message.StartsWith("COM99 · ") && message.Contains(expected)) == 1,
                    failure + " repeats polling without repeating the same per-port diagnosis");
                Check(watcher.ConsecutiveFailures == 0 && watcher.IsPolling,
                    failure + " is an unrecognized candidate, not a global enumeration failure");
                io.OpenException = null; io.FailRead = false; io.NoHelloReply = false;
                io.NodeId = "10:00:00:00:00:01"; io.HelloResponse = null;
                watcher.ResumePolling(); Idle(watcher);
                Check(watcher.Devices.Count == 1 && statuses.Count(message => message.StartsWith("COM99 · ") && message.Contains("已识别 TriLink")) == 1,
                    failure + " recovery updates the final diagnostic and identifies the real-capability device");
            }
        }
    }

    private static void RecognitionScopeAndRemoval()
    {
        var io = new FakeIo { Capabilities = 3 };
        using (var watcher = new TriLinkSerialWatcher(io))
        {
            var statuses = new List<string>();
            watcher.Status += (_, message) => statuses.Add(message);
            watcher.ResumePolling(); Idle(watcher);
            Check(watcher.Devices.Count == 1 && statuses.Any(message => message.Contains("未声明真实 Room/RGB 能力")),
                "identified legacy firmware is explicitly distinguished from Room-capable firmware");
            watcher.ResumePolling(); Idle(watcher);
            Check(statuses.Count(message => message.StartsWith("COM99 · ")) == 1,
                "same successful HELLO diagnostic remains quiet on manual refresh");
            io.PnpDeviceId = "USB\\VID_303A&PID_1001\\REATTACHED"; Scan(watcher);
            Check(statuses.Count(message => message.StartsWith("COM99 · ")) == 2,
                "same COM with a new PnP identity is a new diagnostic subject");
            io.PortCount = 0; Scan(watcher);
            io.PortCount = 1; Scan(watcher);
            Check(statuses.Count(message => message.StartsWith("COM99 · ")) == 3,
                "a disconnected and reattached port republishes its diagnostic once");
        }
        io = new FakeIo { PnpDeviceId = "USB\\VID_1A86&PID_55D3\\CH343", CandidateName = "USB Serial" };
        using (var watcher = new TriLinkSerialWatcher(io))
        {
            var statuses = new List<string>();
            watcher.Status += (_, message) => statuses.Add(message);
            watcher.ResumePolling(); Idle(watcher);
            Check(io.Opens == 0 && watcher.Devices.Count == 0 && statuses.Any(message => message.Contains("检测到烧录口 COM99")),
                "CH343 alone is diagnosed as a programming bridge without probing or widening VID candidates");
            Scan(watcher);
            Check(statuses.Count == 1, "unchanged programming-bridge-only inventory does not spam polling logs");
        }
        io = new FakeIo { HelloResponse = nonce => "TRILINK/1 DEVICE stale 10:00:00:00:00:01 dGVzdA== 00000040\n"
            + "TRILINK/1 DEVICE " + nonce + " 10:00:00:00:00:01 dGVzdA== 00000040\n" };
        using (var watcher = new TriLinkSerialWatcher(io))
        {
            var statuses = new List<string>();
            watcher.Status += (_, message) => statuses.Add(message);
            watcher.ResumePolling(); Idle(watcher);
            Check(watcher.Devices.Count == 1 && statuses.Count(message => message.StartsWith("COM99 · ")) == 1
                && statuses.Last().Contains("已识别 TriLink"),
                "a stale nonce line followed by the current valid identity succeeds without publishing an interim failure");
        }
    }

    private static void StopDuringFailedHandshake(bool dispose)
    {
        var io = new FakeIo { FailRead = true };
        var gate = new Gate();
        io.BeforeRead = _ => gate.Block();
        using (var watcher = new TriLinkSerialWatcher(io))
        {
            var statuses = new List<string>();
            watcher.Status += (_, message) => statuses.Add(message);
            watcher.ResumePolling(); gate.WaitEntered();
            if (dispose) watcher.Dispose(); else watcher.PausePolling();
            gate.Release.Set(); Idle(watcher);
            Check(statuses.All(message => !message.StartsWith("COM99 · ")) && watcher.Devices.Count == 0,
                (dispose ? "Dispose" : "pause") + " suppresses even a failed-handshake diagnosis from a retired scan generation");
            Check(watcher.ConsecutiveFailures == 0 && io.Closes == 1,
                "canceled failed handshake closes its fake COM without incrementing the limiter");
        }
    }

    private static void DemoPluginStart()
    {
        var context = new DemoContext();
        var plugin = new SerialPlugin();
        plugin.Configure(context);
        try
        {
            plugin.Start();
            Check(!context.Discovery.IsPolling, "demo plugin start never enables automatic real hardware scanning");
            Check(context.Commands != null && ReferenceEquals(context.Commands, context.Discovery),
                "demo still provides discovery and commands for later explicit user operation");
        }
        finally { context.Cleanup(); }
    }

    private static void RapidResume(string stage)
    {
        var io = new FakeIo { Capabilities = 3, DisplayName = "old" };
        var gate = new Gate();
        if (stage == "enumeration") io.BeforeEnumeration = call => { if (call == 1) gate.Block(); };
        if (stage == "read") io.BeforeRead = connection => { if (connection == 1) gate.Block(); };
        using (var watcher = new TriLinkSerialWatcher(io))
        {
            var nodes = new List<string>();
            watcher.DeviceArrived += (_, args) => nodes.Add(args.Device.NodeId);
            var owner = (SemaphoreSlim)Private(watcher, "_ioOwner");
            if (stage == "queue") owner.Wait();
            watcher.ResumePolling();
            if (stage != "queue") gate.WaitEntered();
            watcher.PausePolling();
            io.NodeId = "20:00:00:00:00:02"; io.Capabilities = 64; io.DisplayName = "new";
            watcher.ResumePolling();
            if (stage == "queue") owner.Release(); else gate.Release.Set();
            WaitFor(() => watcher.Devices.Count == 1, "fresh resumed scan publishes new device");
            Idle(watcher);
            Check(watcher.Devices.Single().NodeId == io.NodeId && watcher.Devices.Single().Capabilities == 64,
                "rapid resume at " + stage + " commits only the new generation");
            Check(nodes.SequenceEqual(new[] { io.NodeId }), "old " + stage + " generation cannot publish an arrival after resume");
            Check(io.Opens == (stage == "read" ? 2 : 1), "new " + stage + " generation retries once without duplicate old I/O");
            Check(watcher.ConsecutiveFailures == 0, "cancellation at " + stage + " does not count as an I/O failure");
        }
    }

    private static void DisposeQueuedBusiness()
    {
        var io = new FakeIo();
        using (var watcher = new TriLinkSerialWatcher(io))
        {
            var owner = (SemaphoreSlim)Private(watcher, "_ioOwner"); owner.Wait();
            var search = watcher.SearchNearbyAsync("COM99");
            var command = watcher.ExecuteAsync("COM99", "ROOMGET", "00");
            watcher.Dispose();
            owner.Release();
            Reject<OperationCanceledException>(() => search.GetAwaiter().GetResult(), "Dispose cancels queued peer search");
            Reject<OperationCanceledException>(() => command.GetAwaiter().GetResult(), "Dispose cancels queued business command");
            Check(io.Opens == 0, "disposed queued operations never open serial I/O");
        }
    }

    private static void QueuedBusinessBinding()
    {
        foreach (string changed in new[] { "node", "pnp", "capabilities" })
        {
            var io = new FakeIo();
            using (var watcher = new TriLinkSerialWatcher(io))
            {
                watcher.ResumePolling(); Idle(watcher); watcher.PausePolling();
                int opens = io.Opens;
                var owner = (SemaphoreSlim)Private(watcher, "_ioOwner"); owner.Wait();
                Task<string> pending;
                TriLinkDevice replacement;
                try
                {
                    pending = watcher.ExecuteAsync("COM99", "RGBENABLE", "1");
                    lock (Private(watcher, "_sync"))
                    {
                        var cache = (Dictionary<string, TriLinkDevice>)Private(watcher, "_recognized");
                        var original = cache["COM99"];
                        replacement = new TriLinkDevice { PortName = "COM99",
                            NodeId = changed == "node" ? "20:00:00:00:00:02" : original.NodeId,
                            PnpDeviceId = changed == "pnp" ? "replacement" : original.PnpDeviceId,
                            Capabilities = changed == "capabilities" ? original.Capabilities | 128U : original.Capabilities };
                        cache["COM99"] = replacement;
                    }
                }
                finally { owner.Release(); }
                Reject<InvalidOperationException>(() => pending.GetAwaiter().GetResult(),
                    "queued business rejects changed " + changed + " binding before I/O");
                Check(io.Opens == opens && io.BusinessWrites == 0, "changed " + changed + " binding never opens or writes replacement");
                var retained = watcher.Devices.Single();
                Check(retained.NodeId == replacement.NodeId && retained.PnpDeviceId == replacement.PnpDeviceId
                    && retained.Capabilities == replacement.Capabilities, "binding rejection preserves fresh " + changed + " cache");
            }
        }
    }

    private static void NodeCapabilityDescriptors()
    {
        const string wire = "TRILINK/3 CAPS abc 1 0.15.0 1234ABCD 84 06 06 01";
        NodeCapabilities value;
        Check(NodeCapabilityProtocol.TryParse(wire, "abc", "10:00:00:00:00:01", out value)
            && !value.IsLegacy && value.ContractVersion == 1 && value.FirmwareVersion == "0.15.0"
            && value.BootId == 0x1234ABCD && value.MaxRouteBody == 132 && value.MaxNeighbors == 6
            && value.MaxRoomMembers == 6 && value.SupportsRgb, "CAPS decodes exact firmware module budget contract");
        foreach (string invalid in new[] {
            wire.Replace("CAPS", "OK"), wire.Replace("abc", "stale"), wire.Replace(" 1 ", " 2 "),
            wire.Replace("0.15.0", "0.15"), wire.Replace("0.15.0", "0.15.0.1"),
            wire.Replace("0.15.0", "0.15.x"), wire.Replace("0.15.0", "999999.15.0"),
            wire.Replace("1234ABCD", "00000000"), wire.Replace("1234ABCD", "1234ABC"),
            wire.Replace("1234ABCD", "1234ABCG"), wire.Replace("84", "85"), wire.Replace("84", "00"),
            wire.Replace("06 06", "07 06"), wire.Replace("06 06", "06 00"), wire.Replace("06 06", "06 07"),
            wire + " extra", wire + "\t", wire.Replace(" CAPS ", "  CAPS "), new string('X', 161) })
        {
            Check(!NodeCapabilityProtocol.TryParse(invalid, "abc", "node", out value),
                "CAPS rejects malformed, stale or unsupported descriptor: " + invalid.Replace("\t", "<TAB>"));
        }
        Check(NodeCapabilityProtocol.TryParse(wire.Replace(" 01", " 81"), "abc", "node", out value)
            && value.ModuleMask == 0x81 && value.SupportsRgb,
            "unknown module bits are retained without inventing a new implemented feature");
        var io = new FakeIo();
        using (var watcher = new TriLinkSerialWatcher(io))
        {
            watcher.ResumePolling(); Idle(watcher); watcher.PausePolling();
            int opens = io.Opens;
            var legacy = watcher.DescribeAsync("COM99").GetAwaiter().GetResult();
            Check(legacy.IsLegacy && legacy.SupportsRgb && legacy.ContractVersion == 0
                && legacy.FirmwareVersion == null && legacy.BootId == 0 && legacy.MaxRouteBody == 0,
                "legacy HELLO reports only known capabilities and leaves unknown descriptor fields empty");
            Check(io.Opens == opens && io.CapsReads == 0, "legacy node is never probed with unsupported CAPS");
            Reject<InvalidOperationException>(() => watcher.DescribeAsync("COM98").GetAwaiter().GetResult(),
                "CAPS requires a recognized port");
        }
        io = new FakeIo { Capabilities = NodeCapabilities.DiscoveryCapability };
        using (var watcher = new TriLinkSerialWatcher(io))
        {
            watcher.ResumePolling(); Idle(watcher); watcher.PausePolling();
            int hellos = io.Hellos;
            var current = watcher.DescribeAsync("COM99").GetAwaiter().GetResult();
            Check(!current.IsLegacy && current.NodeId == io.NodeId && current.BootId == 0x1234ABCD
                && current.FirmwareVersion == "0.15.0", "module service reads typed capabilities through real adapter code with fake I/O");
            Check(io.CapsReads == 1 && io.Hellos == hellos + 1 && io.BusinessWrites == 0,
                "CAPS shares owner and revalidates identity without requiring unrelated Room capability");
            Reject<ArgumentException>(() => watcher.ExecuteAsync("COM99", "CAPS", "unexpected"),
                "CAPS rejects arguments before I/O");
            io.CapsResponse = nonce => "TRILINK/3 CAPS " + nonce + " 2 0.15.0 1234ABCD 84 06 06 01\n";
            Reject<FormatException>(() => watcher.DescribeAsync("COM99").GetAwaiter().GetResult(),
                "invalid CAPS cannot become usable module state");
            foreach (string corruption in new[] { "tab", "non-ascii", "padding" })
            {
                io.CapsResponse = nonce => (corruption == "non-ascii" ? "\u00A0" : "")
                    + "TRILINK/3 CAPS " + nonce + " 1 0.15.0 1234ABCD 84 06 06 01"
                    + (corruption == "tab" ? "\t" : corruption == "padding" ? new string(' ', 170) : "") + "\n";
                Reject<FormatException>(() => watcher.DescribeAsync("COM99").GetAwaiter().GetResult(),
                    "serial layer preserves and rejects CAPS " + corruption + " instead of trimming it valid");
            }
            io.CapsResponse = nonce => "\r\nTRILINK/3 CAPS " + nonce + " 1 0.15.0 1234ABCD 84 06 06 01\r\n";
            Check(watcher.DescribeAsync("COM99").GetAwaiter().GetResult().BootId == 0x1234ABCD,
                "CAPS accepts normal blank prefix and CRLF framing without stripping payload whitespace");
            io.CapsResponse = nonce => "TRILINK/3 CAPS stale 1 0.15.0 00000001 84 06 06 01\n"
                + "TRILINK/3 CAPS " + nonce + " 1 0.15.0 1234ABCD 84 06 06 01\n";
            Check(watcher.DescribeAsync("COM99").GetAwaiter().GetResult().BootId == 0x1234ABCD,
                "CAPS skips stale nonce and accepts only the current reply");
            io.CapsResponse = nonce => "TRILINK/3 CAPS stale 1 0.15.0 00000001 84 06 06 01\n";
            Reject<TimeoutException>(() => watcher.DescribeAsync("COM99").GetAwaiter().GetResult(),
                "CAPS stale nonce alone cannot satisfy the live request");
            io.Capabilities = 64;
            int reads = io.CapsReads;
            Reject<InvalidOperationException>(() => watcher.DescribeAsync("COM99").GetAwaiter().GetResult(),
                "fresh HELLO downgrade blocks stale CAPS request");
            Check(io.CapsReads == reads && watcher.Devices.Count == 0,
                "lost module capability invalidates identity and does not transmit CAPS");
        }
        io = new FakeIo { Capabilities = NodeCapabilities.DiscoveryCapability };
        using (var watcher = new TriLinkSerialWatcher(io))
        {
            watcher.ResumePolling(); Idle(watcher); watcher.PausePolling();
            var owner = (SemaphoreSlim)Private(watcher, "_ioOwner");
            owner.Wait();
            Task<NodeCapabilities> pending;
            try
            {
                pending = watcher.DescribeAsync("COM99");
                lock (Private(watcher, "_sync"))
                {
                    var cache = (Dictionary<string, TriLinkDevice>)Private(watcher, "_recognized");
                    cache["COM99"] = new TriLinkDevice { PortName = "COM99", NodeId = "20:00:00:00:00:02",
                        PnpDeviceId = "replacement", Capabilities = NodeCapabilities.DiscoveryCapability };
                }
            }
            finally { owner.Release(); }
            Reject<InvalidOperationException>(() => pending.GetAwaiter().GetResult(),
                "queued CAPS remains bound to original node when COM cache is replaced");
            Check(io.CapsReads == 0 && watcher.Devices.Single().NodeId == "20:00:00:00:00:02",
                "old CAPS neither transmits nor invalidates the replacement node cache");
        }
    }

    private static int Main(string[] args)
    {
        try
        {
            CacheRefresh(); BusinessIdentityFailure();
            foreach (bool dispose in new[] { false, true })
            {
                StopDuringEnumeration(dispose); StopDuringHandshake(dispose); StopQueuedScan(dispose); StopDuringOpen(dispose); StopDuringFailedHandshake(dispose);
            }
            foreach (string stage in new[] { "enumeration", "read", "queue" }) RapidResume(stage);
            DisposeQueuedBusiness(); FailureLimitAndResume(); RecognitionDiagnostics(); RecognitionScopeAndRemoval(); DemoPluginStart();
            NodeCapabilityDescriptors();
            QueuedBusinessBinding();
            NcmTransportContracts();
            if (args.Length != 0)
            {
                if ((args.Length != 2 && args.Length != 4) || args[0] != "--caps-fixture"
                    || (args.Length == 4 && args[2] != "--caps-version"))
                    throw new ArgumentException("Expected --caps-fixture <path> [--caps-version <version>]");
                var capsVersion = args.Length == 4 ? args[3] : "0.15.0";
                var file = new FileInfo(args[1]);
                if (file.Length > 4096) throw new ArgumentException("CAPS fixture is oversized");
                string fixture = File.ReadAllText(file.FullName, Encoding.ASCII).Trim();
                NodeCapabilities description;
                Check(NodeCapabilityProtocol.TryParse(fixture, "cap", "10:00:00:00:00:01", out description)
                    && description.FirmwareVersion == capsVersion && description.BootId == 300
                    && description.MaxRouteBody == 132 && description.ModuleMask == 1,
                    "cross-language CAPS generated by firmware bridge is accepted by PC parser");
            }
            Console.WriteLine("PASS serial lifecycle checks=" + _checks + " (fake inventory/COM/HTTP only; no GUI, network or hardware)");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }

    private sealed class Gate
    {
        private readonly ManualResetEventSlim _entered = new ManualResetEventSlim();
        public readonly ManualResetEventSlim Release = new ManualResetEventSlim();
        public void Block() { _entered.Set(); if (!Release.Wait(2500)) throw new TimeoutException("fixture gate"); }
        public void WaitEntered() { if (!_entered.Wait(2500)) throw new TimeoutException("fixture not reached"); }
    }

    private sealed class FakeIo : ISerialIo
    {
        public string NodeId { get; set; } = "10:00:00:00:00:01";
        public string PnpDeviceId { get; set; } = "USB\\VID_303A&PID_1001\\OLD";
        public string DisplayName { get; set; } = "test";
        public string CandidateName { get; set; } = "TriLink fake";
        public uint Capabilities { get; set; } = 64;
        public int PortCount { get; set; } = 1;
        public bool FailOpen { get; set; }
        public bool FailRead { get; set; }
        public bool NoHelloReply { get; set; }
        public Exception OpenException { get; set; }
        public Func<string, string> HelloResponse { get; set; }
        public Func<string, string> CapsResponse { get; set; }
        public Action<int> BeforeEnumeration { get; set; }
        public Action<int> BeforeRead { get; set; }
        public Action<int> BeforeOpen { get; set; }
        public int Opens, Enumerations, Hellos, BusinessWrites, Closes, CapsReads;
        public List<SerialCandidate> Enumerate()
        {
            int call = Interlocked.Increment(ref Enumerations); BeforeEnumeration?.Invoke(call);
            return Enumerable.Range(0, PortCount).Select(i => new SerialCandidate {
                PortName = "COM" + (99 + i), PnpDeviceId = PnpDeviceId, Name = CandidateName
            }).ToList();
        }
        public ISerialConnection OpenPort(string port, int timeout, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (OpenException != null) throw OpenException;
            if (FailOpen) throw new IOException("fixture open failed");
            int connection = Interlocked.Increment(ref Opens);
            var result = new FakeConnection(this, connection);
            BeforeOpen?.Invoke(connection);
            return result;
        }

        private sealed class FakeConnection : ISerialConnection
        {
            private readonly FakeIo _io;
            private readonly int _connection;
            private readonly string _node, _name;
            private readonly uint _capabilities;
            private readonly Queue<char> _input = new Queue<char>();
            private bool _readStarted;
            public FakeConnection(FakeIo io, int connection)
            { _io = io; _connection = connection; _node = io.NodeId; _name = io.DisplayName; _capabilities = io.Capabilities; }
            public void DiscardInBuffer() { _input.Clear(); }
            public void DiscardOutBuffer() { }
            public void WriteLine(string line)
            {
                var fields = line.Split(' ');
                string reply;
                if (fields[1] == "HELLO")
                {
                    Interlocked.Increment(ref _io.Hellos);
                    if (_io.NoHelloReply) return;
                    reply = "TRILINK/1 DEVICE " + fields[2] + " " + _node + " "
                        + Convert.ToBase64String(Encoding.UTF8.GetBytes(_name)) + " " + _capabilities.ToString("X8") + "\n";
                    if (_io.HelloResponse != null) reply = _io.HelloResponse(fields[2]);
                }
                else if (fields[1] == "SEARCH") reply = "TRILINK/1 END " + fields[2] + "\n";
                else if (fields[1] == "CAPS")
                {
                    Interlocked.Increment(ref _io.CapsReads);
                    reply = _io.CapsResponse != null ? _io.CapsResponse(fields[2])
                        : "TRILINK/3 CAPS " + fields[2] + " 1 0.15.0 1234ABCD 84 06 06 01\n";
                }
                else
                {
                    Interlocked.Increment(ref _io.BusinessWrites);
                    reply = "TRILINK/3 OK " + fields[2] + " " + fields[1] + "\n";
                }
                foreach (char value in reply) _input.Enqueue(value);
            }
            public int ReadChar()
            {
                if (!_readStarted) { _readStarted = true; _io.BeforeRead?.Invoke(_connection); }
                if (_io.FailRead) throw new IOException("fixture read failed");
                if (_input.Count == 0) { Thread.Sleep(1); throw new TimeoutException("fixture no reply"); }
                return _input.Dequeue();
            }
            public void Dispose() { Interlocked.Increment(ref _io.Closes); }
        }
    }

    private sealed class DemoContext : IPluginContext, IHostEnvironment
    {
        public IDeviceDiscoveryService Discovery { get; private set; }
        public IHardwareCommandService Commands { get; private set; }
        public Action Cleanup { get; private set; }
        public string PluginId { get { return "trilink.serial"; } }
        public IHostEnvironment Environment { get { return this; } }
        public string BaseDirectory { get { return AppDomain.CurrentDomain.BaseDirectory; } }
        public IReadOnlyList<string> Arguments { get { return new[] { "--demo" }; } }
        public bool DemoMode { get { return true; } }
        public string ProfileName { get { return "desktop"; } }
        public TService GetRequired<TService>() where TService : class { throw new InvalidOperationException(); }
        public void Provide<TService>(TService service) where TService : class
        {
            if (service is IDeviceDiscoveryService) Discovery = (IDeviceDiscoveryService)service;
            if (service is IHardwareCommandService) Commands = (IHardwareCommandService)service;
        }
        public void Defer(Action cleanup) { Cleanup = cleanup; }
        public void Log(string message) { }
    }
}
