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

internal static class Program
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

    private static int Main()
    {
        try
        {
            CacheRefresh(); BusinessIdentityFailure();
            foreach (bool dispose in new[] { false, true })
            {
                StopDuringEnumeration(dispose); StopDuringHandshake(dispose); StopQueuedScan(dispose); StopDuringOpen(dispose);
            }
            foreach (string stage in new[] { "enumeration", "read", "queue" }) RapidResume(stage);
            DisposeQueuedBusiness(); FailureLimitAndResume(); DemoPluginStart();
            Console.WriteLine("PASS serial lifecycle checks=" + _checks + " (fake inventory/COM only; no GUI or hardware)");
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
        public uint Capabilities { get; set; } = 64;
        public int PortCount { get; set; } = 1;
        public bool FailOpen { get; set; }
        public bool FailRead { get; set; }
        public bool NoHelloReply { get; set; }
        public Action<int> BeforeEnumeration { get; set; }
        public Action<int> BeforeRead { get; set; }
        public Action<int> BeforeOpen { get; set; }
        public int Opens, Enumerations, Hellos, BusinessWrites, Closes;
        public List<SerialCandidate> Enumerate()
        {
            int call = Interlocked.Increment(ref Enumerations); BeforeEnumeration?.Invoke(call);
            return Enumerable.Range(0, PortCount).Select(i => new SerialCandidate {
                PortName = "COM" + (99 + i), PnpDeviceId = PnpDeviceId, Name = "TriLink fake"
            }).ToList();
        }
        public ISerialConnection OpenPort(string port, int timeout, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
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
                }
                else if (fields[1] == "SEARCH") reply = "TRILINK/1 END " + fields[2] + "\n";
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
