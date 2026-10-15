using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using TriLink.Core;
using TriLink.MinClient.Serial;

internal static partial class Program
{
    private static void NcmTransportContracts()
    {
        Check(NcmHttpClient.RemainingMilliseconds(3499)==1,"HTTP deadline retains only the remaining single millisecond");
        Reject<TimeoutException>(()=>NcmHttpClient.RemainingMilliseconds(3500),"HTTP final response at exact deadline is rejected");
        Reject<TimeoutException>(()=>NcmHttpClient.RemainingMilliseconds(long.MaxValue),"HTTP elapsed time cannot wrap through an Int32 cast and reopen a deadline");
        Check(new TriLinkDevice().TransportKind == NodeTransportKind.UsbSerial, "old plugins default to CDC metadata without an ABI-breaking field replacement");
        Check(WindowsNcmCatalog.IsTriLinkPnp(@"USB\VID_303A&PID_4011&MI_02\instance"), "NCM hardware whitelist matches frozen TriLink interface");
        foreach (var invalid in new[] { @"USB\VID_303A&PID_4011&MI_00\instance", @"USB\VID_303A&PID_9999&MI_02\instance", @"ROOT\TriLink NCM", @"USB\VID_1A86&PID_55D3\instance", "" })
            Check(!WindowsNcmCatalog.IsTriLinkPnp(invalid), "NCM rejects unrelated driver metadata: " + invalid);
        Check(WindowsNcmCatalog.IsUsbLocalAddress(IPAddress.Parse("192.168.240.2"), IPAddress.Parse("255.255.255.0")), "NCM accepts only configured USB /24 host addresses");
        Check(WindowsNcmCatalog.IsUsbLocalAddress(IPAddress.Parse("192.168.240.3"), IPAddress.Parse("255.255.255.0")), "NCM accepts the firmware's second bounded DHCP lease");
        foreach (var invalid in new[] { "192.168.240.1", "192.168.240.0", "192.168.240.4", "192.168.240.254", "192.168.240.255", "192.168.1.2", "127.0.0.1", "::1" })
            Check(!WindowsNcmCatalog.IsUsbLocalAddress(IPAddress.Parse(invalid), IPAddress.Parse("255.255.255.0")), "NCM rejects unsafe host source: " + invalid);
        Check(!WindowsNcmCatalog.IsUsbLocalAddress(IPAddress.Parse("192.168.240.2"), IPAddress.Parse("255.255.0.0")), "NCM refuses an overlapping broad subnet");

        var request = Encoding.ASCII.GetString(NcmHttpClient.BuildRequest(null, null));
        Check(request.StartsWith("GET /api/v1/session HTTP/1.1\r\nHost: 192.168.240.1\r\n") && !request.Contains("X-TriLink-Token"), "session bootstrap has fixed address/path and no retained credentials");
        foreach (var command in new[] { "ROOM", "RGB", "RGBENABLE" })
        {
            request = Encoding.ASCII.GetString(NcmHttpClient.BuildRequest(new string('a', 32), "TRILINK/3 " + command + " n 1", 12500));
            Check(request.Contains("X-TriLink-Confirm: execute\r\nX-TriLink-Deadline: 12500\r\n") && request.EndsWith("\n"), command + " sends explicit execution consent, expiry and exact single-line payload");
        }
        request = Encoding.ASCII.GetString(NcmHttpClient.BuildRequest(new string('a', 32), "TRILINK/3 ROOMGET n 00"));
        Check(!request.Contains("X-TriLink-Confirm") && request.Contains("Content-Length: 23\r\n"), "read request has exact Content-Length and no mutation consent");
        Reject<ArgumentException>(() => NcmHttpClient.BuildRequest("bad\r\nheader", "TRILINK/1 HELLO n"), "NCM session token cannot inject headers");
        Reject<ArgumentException>(() => NcmHttpClient.BuildRequest(new string('a', 32), "TRILINK/3 RGBENABLE n 1"), "mutation without device-monotonic deadline fails before I/O");
        foreach (var invalid in new[] { "TRILINK/1 HELLO n\nTRILINK/3 RGB n x", "中文", new string('x', 383), "" })
            Reject<ArgumentException>(() => NcmHttpClient.BuildRequest(new string('a', 32), invalid), "NCM refuses multiline/non-ASCII/oversized/empty request");
        var header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2048\r\nConnection: close\r\n\r\n");
        Check(NcmHttpClient.FindHeaderEnd(header, header.Length) == header.Length && NcmHttpClient.ParseHeader(header, header.Length) == 2048, "HTTP response accepts bounded explicit length only");
        foreach (var invalid in new[] {
            "HTTP/1.1 302 Found\r\nContent-Length: 0\r\n\r\n",
            "HTTP/1.1 401 Unauthorized\r\nContent-Length: 0\r\n\r\n",
            "HTTP/1.1 200 OK\r\n\r\n",
            "HTTP/1.1 200 OK\r\nContent-Length: 1\r\nContent-Length: 1\r\n\r\n",
            "HTTP/1.1 200 OK\r\nContent-Length: 2049\r\n\r\n",
            "HTTP/1.1 200 OK\r\nContent-Length: -1\r\n\r\n",
            "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nTransfer-Encoding: chunked\r\n\r\n",
            "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nContent-Encoding: gzip\r\n\r\n",
            "HTTP/1.1 200 OK\r\n Content-Length: 0\r\n\r\n",
            "HTTP/1.1 200 OK\r\nContent-Length:\t0\r\n\r\n" })
        {
            header = Encoding.ASCII.GetBytes(invalid);
            if (invalid.StartsWith("HTTP/1.1 200 OK"))
                Reject<InvalidDataException>(() => NcmHttpClient.ParseHeader(header, header.Length), "HTTP fails closed on length/encoding/header violations");
            else Reject<IOException>(() => NcmHttpClient.ParseHeader(header, header.Length), "HTTP refuses non-success and redirect status");
        }
        NcmConnectionContracts();
        NcmCompositeContracts();
        NcmSearchBinding();
    }

    private static void NcmConnectionContracts()
    {
        var catalog = new FakeNcmCatalog(); var http = new FakeNcmHttp();
        var io = new NcmIo(catalog, http); string endpoint = catalog.Endpoint.EndpointName;
        Reject<IOException>(() => io.OpenPort(endpoint, 10, CancellationToken.None), "unenumerated NCM endpoint cannot cause any HTTP access");
        Check(http.Requests == 0, "NCM guard rejects before bootstrap");
        Check(io.Enumerate().Single().IsProtocolCandidate, "validated NCM enumeration becomes a protocol candidate");
        using (var connection = io.OpenPort(endpoint, 10, CancellationToken.None))
        {
            connection.WriteLine("TRILINK/1 HELLO nonce");
            var reply = ReadNcm(connection);
            UsbDeviceIdentity identity;
            Check(UsbControlProtocol.TryParseDevice(reply, "nonce", out identity) && identity.NodeId == http.NodeId, "NCM reuses the existing nonce/MAC HELLO parser");
            int before = http.Requests;
            catalog.Endpoint = FakeNcmCatalog.NewEndpoint("192.168.240.3");
            Reject<IOException>(() => connection.WriteLine("TRILINK/3 RGBENABLE n 1"), "NCM source address replacement invalidates an open connection");
            Check(http.Requests == before, "changed NCM binding sends no request and never bootstraps/retries");
        }
        Reject<IOException>(() => io.OpenPort(endpoint, 10, CancellationToken.None), "route replacement after inventory blocks open before HTTP");
        foreach (var invalid in new[] {
            "{}", "{\"protocol\":2,\"token\":\"" + new string('a', 32) + "\",\"firmware\":\"0.16.0\",\"endpoint\":\"/api/v1/exchange\"}",
            FakeNcmHttp.SessionJson.Replace("/api/v1/exchange", "http://outside.invalid/"),
            FakeNcmHttp.SessionJson.Replace(new string('a', 32), new string('A', 32)),
            FakeNcmHttp.SessionJson.Replace("10000", "18446744073709551616"),
            FakeNcmHttp.SessionJson.Replace("10000", "-1"),
            FakeNcmHttp.SessionJson + FakeNcmHttp.SessionJson, new string('x', 513) })
        {
            catalog = new FakeNcmCatalog(); http = new FakeNcmHttp { Session = invalid }; io = new NcmIo(catalog, http); io.Enumerate();
            Reject<InvalidDataException>(() => io.OpenPort(endpoint, 10, CancellationToken.None), "NCM session schema/token/version/path fail closed");
        }
        catalog = new FakeNcmCatalog(); http = new FakeNcmHttp(); io = new NcmIo(catalog, http); io.Enumerate();
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            Reject<OperationCanceledException>(() => io.OpenPort(endpoint, 10, cancellation.Token), "canceled NCM open performs no bootstrap");
            Check(http.Requests == 0, "cancellation is checked before HTTP I/O");
        }
        using (var connection = io.OpenPort(endpoint, 10, CancellationToken.None))
        {
            http.OverrideReply = new string('x', 2049);
            Reject<InvalidDataException>(() => connection.WriteLine("TRILINK/1 HELLO nonce"), "NCM bounds fake and real response bodies equally");
            http.OverrideReply = "bad\0reply\n";
            Reject<InvalidDataException>(() => connection.WriteLine("TRILINK/1 HELLO nonce"), "NCM rejects control byte reply");
        }
    }

    private static string ReadNcm(ISerialConnection connection)
    {
        var line = new StringBuilder();
        for (int i = 0; i < 2048; ++i) { int ch = connection.ReadChar(); if (ch == '\n') return line.ToString(); line.Append((char)ch); }
        throw new InvalidDataException("fixture oversized");
    }

    private static void NcmCompositeContracts()
    {
        var serial = new FakeIo(); var catalog = new FakeNcmCatalog(); var http = new FakeNcmHttp();
        using (var watcher = new TriLinkSerialWatcher(new CompositeSerialIo(serial, new NcmIo(catalog, http))))
        {
            watcher.ResumePolling(); Idle(watcher); watcher.PausePolling();
            Check(watcher.Devices.Count == 1 && watcher.Devices[0].TransportKind == NodeTransportKind.UsbSerial, "same MAC CDC plus NCM publishes one CDC-first logical device");
            int requests = http.Requests;
            serial.FailOpen = true;
            Reject<IOException>(() => watcher.ExecuteAsync("COM99", "RGBENABLE", "1").GetAwaiter().GetResult(), "failed CDC write is surfaced, not hidden by HTTP fallback");
            Check(http.Requests == requests && http.Mutations == 0, "CDC failure never replays an intent through NCM");
            serial.PortCount = 0;
            watcher.ResumePolling(); Idle(watcher); watcher.PausePolling();
            var device = watcher.Devices.Single();
            Check(device.TransportKind == NodeTransportKind.UsbNcm && device.ManagementUrl == NcmEndpoint.ManagementUrl && device.PortName == catalog.Endpoint.EndpointName,
                "later discovery can explicitly expose a validated NCM endpoint with separate display metadata");
            watcher.ExecuteAsync(device.PortName, "RGBENABLE", "1").GetAwaiter().GetResult();
            Check(http.Mutations == 1 && serial.BusinessWrites == 0, "new user operation on NCM is executed exactly once via shared owner");
            var capabilities = watcher.DescribeAsync(device.PortName).GetAwaiter().GetResult();
            Check(capabilities.FirmwareVersion == "0.16.0" && capabilities.BootId == 0x1234ABCD, "NCM shares CAPS/boot validation instead of inventing network capabilities");
            Check(watcher.SearchNearbyAsync(device.PortName).GetAwaiter().GetResult().Count == 0, "NCM SEARCH requires matching END and reuses peer parser");
            Check(http.LastMutationDeadline >= 12500 && http.LastMutationDeadline < 17500,
                "NCM mutation deadline is derived from server uptime plus bounded elapsed time");
            http.FailMutation = true;
            int before = http.Mutations;
            Reject<IOException>(() => watcher.ExecuteAsync(device.PortName, "RGBENABLE", "0").GetAwaiter().GetResult(), "NCM result-unknown failure remains visible");
            Check(http.Mutations == before + 1, "NCM unknown result is never retried even on same transport");
            serial.PortCount = 1; serial.FailOpen = false;
            watcher.ResumePolling(); Idle(watcher); watcher.PausePolling();
            Check(watcher.Devices.Count == 1 && watcher.Devices[0].PortName == "COM99", "CDC reappearance replaces NCM selection without duplicate node sessions");
            int bootstrap = http.Requests;
            watcher.ResumePolling(); Idle(watcher); Scan(watcher); watcher.PausePolling();
            Check(http.Requests == bootstrap + 2, "unchanged shadow NCM is not repeatedly probed by periodic scans");
        }
        serial = new FakeIo { PortCount = 0 }; catalog = new FakeNcmCatalog { Fail = true }; http = new FakeNcmHttp();
        using (var watcher = new TriLinkSerialWatcher(new CompositeSerialIo(serial, new NcmIo(catalog, http))))
        {
            watcher.ResumePolling(); Idle(watcher);
            for (int i = 1; i < 5; ++i) Scan(watcher);
            Check(!watcher.IsPolling && watcher.ConsecutiveFailures == 5 && http.Requests == 0, "NCM inventory failure obeys existing five-failure circuit breaker");
            watcher.RequestScan(); Idle(watcher);
            Check(http.Requests == 0, "paused composite discovery cannot be bypassed by scan request");
            catalog.Fail = false;
            watcher.ResumePolling(); Idle(watcher); watcher.PausePolling();
            Check(watcher.Devices.Count == 1 && watcher.ConsecutiveFailures == 0, "manual resume safely recovers NCM inventory");
        }
        serial = new FakeIo(); catalog = new FakeNcmCatalog { Fail = true }; http = new FakeNcmHttp();
        using (var watcher = new TriLinkSerialWatcher(new CompositeSerialIo(serial, new NcmIo(catalog, http))))
        {
            watcher.ResumePolling(); Idle(watcher);
            for (int i = 1; i < 5; ++i) Scan(watcher);
            Check(watcher.IsPolling && watcher.ConsecutiveFailures == 0 && watcher.Devices.Single().PortName == "COM99",
                "optional NCM enumeration failure cannot disable a healthy CDC primary");
            int enumerations = catalog.Enumerations;
            Scan(watcher);
            Check(catalog.Enumerations == enumerations, "five optional failures pause only NCM inventory attempts");
            watcher.ExecuteAsync("COM99", "RGBENABLE", "1").GetAwaiter().GetResult();
            Check(serial.BusinessWrites == 1 && http.Mutations == 0, "CDC business remains operable while NCM discovery is paused");
            catalog.Fail = false;
            watcher.ResumePolling(); Idle(watcher); watcher.PausePolling();
            Check(catalog.Enumerations > enumerations && http.Requests == 2 && watcher.Devices.Count == 1,
                "manual resume resets optional circuit breaker without publishing a duplicate node");
        }
        serial = new FakeIo { PortCount = 0 }; catalog = new FakeNcmCatalog(); http = new FakeNcmHttp();
        using (var watcher = new TriLinkSerialWatcher(new CompositeSerialIo(serial, new NcmIo(catalog, http))))
        {
            var entered = new ManualResetEventSlim();
            http.BeforeSession = cancellation => { entered.Set(); cancellation.WaitHandle.WaitOne(2500); cancellation.ThrowIfCancellationRequested(); };
            watcher.ResumePolling();
            if (!entered.Wait(2500)) throw new TimeoutException("NCM fake request not started");
            watcher.PausePolling(); Idle(watcher);
            Check(watcher.Devices.Count == 0 && watcher.ConsecutiveFailures == 0 && http.Mutations == 0, "pause cancels NCM bootstrap without stale device event or failure increment");
        }
    }

    private static void NcmSearchBinding()
    {
        var serial = new FakeIo { PortCount = 0 }; var catalog = new FakeNcmCatalog(); var http = new FakeNcmHttp();
        using (var watcher = new TriLinkSerialWatcher(new CompositeSerialIo(serial, new NcmIo(catalog, http))))
        {
            watcher.ResumePolling(); Idle(watcher); watcher.PausePolling();
            http.NodeId = "20:00:00:00:00:02";
            Reject<InvalidOperationException>(() => watcher.SearchNearbyAsync(catalog.Endpoint.EndpointName).GetAwaiter().GetResult(),
                "NCM SEARCH freshly validates selected node before obtaining peers");
            Check(http.Searches == 0 && watcher.Devices.Count == 0,
                "changed NCM HELLO invalidates old identity and transmits no SEARCH");
        }
        serial = new FakeIo();
        using (var watcher = new TriLinkSerialWatcher(serial))
        {
            watcher.ResumePolling(); Idle(watcher); watcher.PausePolling();
            var owner = (SemaphoreSlim)Private(watcher, "_ioOwner"); owner.Wait();
            System.Threading.Tasks.Task<IReadOnlyList<TriLinkPeer>> task;
            try
            {
                task = watcher.SearchNearbyAsync("COM99");
                lock (Private(watcher, "_sync"))
                {
                    var cache = (Dictionary<string, TriLinkDevice>)Private(watcher, "_recognized");
                    cache["COM99"] = new TriLinkDevice { PortName = "COM99", NodeId = "20:00:00:00:00:02", PnpDeviceId = "new", Capabilities = 64 };
                }
            }
            finally { owner.Release(); }
            Reject<InvalidOperationException>(() => task.GetAwaiter().GetResult(), "queued SEARCH never silently retargets a replacement cached device");
            Check(serial.Opens == 1 && watcher.Devices.Single().NodeId == "20:00:00:00:00:02", "rejected SEARCH neither opens nor removes replacement cache");
        }
    }

    private sealed class FakeNcmCatalog : INcmCatalog
    {
        public NcmEndpoint Endpoint = NewEndpoint("192.168.240.2");
        public bool Fail;
        public int Enumerations;
        public static NcmEndpoint NewEndpoint(string address)
        {
            return new NcmEndpoint { EndpointName = "NCM:11111111-2222-3333-4444-555555555555", InterfaceIndex = 17,
                PnpDeviceId = @"USB\VID_303A&PID_4011&MI_02\fake", LocalAddress = IPAddress.Parse(address) };
        }
        public List<NcmEndpoint> Enumerate()
        {
            ++Enumerations;
            if (Fail) throw new IOException("fake inventory unavailable");
            return Endpoint == null ? new List<NcmEndpoint>() : new List<NcmEndpoint> { Endpoint };
        }
    }
    private sealed class FakeNcmHttp : INcmHttpClient
    {
        public const string SessionJson = "{\"protocol\":1,\"token\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"firmware\":\"0.16.0\",\"endpoint\":\"/api/v1/exchange\",\"uptime_ms\":10000}";
        public string NodeId = "10:00:00:00:00:01", Session = SessionJson, OverrideReply;
        public int Requests, Mutations, Searches;
        public ulong LastMutationDeadline;
        public bool FailMutation;
        public Action<CancellationToken> BeforeSession;
        public string Request(NcmEndpoint endpoint, string token, string line, ulong? deadline, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            ++Requests;
            if (line == null) { BeforeSession?.Invoke(cancellation); return Session; }
            if (OverrideReply != null) return OverrideReply;
            var fields = line.Split(' ');
            if (fields[1] == "HELLO") return "TRILINK/1 DEVICE " + fields[2] + " " + NodeId + " dGVzdA== 000000E3\n";
            if (fields[1] == "SEARCH") { ++Searches; return "TRILINK/1 END " + fields[2] + "\n"; }
            if (fields[1] == "CAPS") return "TRILINK/3 CAPS " + fields[2] + " 1 0.16.0 1234ABCD 84 06 06 01\n";
            ++Mutations;
            LastMutationDeadline = deadline ?? 0;
            if (FailMutation) throw new IOException("fake result unknown");
            return "TRILINK/3 OK " + fields[2] + " " + fields[1] + "\n";
        }
    }
}
