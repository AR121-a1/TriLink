using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TriLink.Plugins.GameLink;
using TriLink.Plugins.Thunder;

namespace TriLink.Thunder.Tests
{
    public static class NetworkProcessChecks
    {
        public static int RunPeer(string[] args)
        {
            int hostPort, players = 2;
            if (args == null || (args.Length != 3 && args.Length != 4) || args[0] != "--udp-peer"
                || (args[1] != "host" && args[1] != "guest")
                || !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out hostPort)
                || hostPort < 1 || hostPort > ushort.MaxValue
                || (args.Length == 4 && (!int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out players)
                    || players < 2 || players > GameEngine.MaxPlayers)))
            { Console.Error.WriteLine("Expected --udp-peer host|guest <hostPort> [2..6 players]."); return 2; }
            bool hosting = args[1] == "host";
            try
            {
                using (var session = new GameSession(new UdpGameLinkFactory()))
                {
                    if (hosting) { session.Host(hostPort, 13579U, players); }
                    else { session.Join("127.0.0.1", hostPort, 0); }
                    Console.WriteLine("READY " + session.LocalPort.ToString(CultureInfo.InvariantCulture)); Console.Out.Flush();
                    // The parent releases everyone only after collecting every WORLD line.
                    // A guest must not send Bye while another guest is still applying final ticks.
                    // Console.In's synchronized TextReader can implement ReadLineAsync synchronously
                    // on .NET Framework. Isolate this test-only barrier read from the simulation loop.
                    var stopCommand = Task.Run(() => Console.In.ReadLine()); var elapsed = Stopwatch.StartNew();
                    long previous = 0; bool reported = false;
                    while (elapsed.ElapsedMilliseconds < 12000)
                    {
                        if (stopCommand.IsCompleted)
                        { return reported && stopCommand.GetAwaiter().GetResult() == "STOP" ? 0 : 6; }
                        long now = elapsed.ElapsedMilliseconds;
                        int delta = (int)Math.Min(GameSession.NetworkIntervalMs, now - previous); previous = now;
                        if (session.Mode != GameSessionMode.Idle) { session.Update(0, hosting && reported ? 0 : delta); }
                        if (!reported && session.Engine != null && session.Engine.Tick >= 90)
                        {
                            Console.WriteLine("WORLD " + session.Engine.Tick.ToString(CultureInfo.InvariantCulture)
                                + " " + SessionChecks.Hash(session.Engine).ToString(CultureInfo.InvariantCulture)
                                + " " + session.PlayerCount.ToString(CultureInfo.InvariantCulture)
                                + " " + session.LocalPlayerIndex.ToString(CultureInfo.InvariantCulture));
                            Console.Out.Flush(); reported = true;
                        }
                        if (!reported && session.Mode == GameSessionMode.Idle)
                        { Console.Error.WriteLine("UDP peer stopped early: " + session.Status); return 3; }
                        Thread.Sleep(5);
                    }
                    Console.Error.WriteLine("UDP peer exceeded twelve seconds: " + session.Status); return 4;
                }
            }
            catch (Exception exception) { Console.Error.WriteLine(exception); return 5; }
        }

        public static void Run(Action<bool, string> check)
        {
            if (check == null) { throw new ArgumentNullException(nameof(check)); }
            foreach (var players in new[] { 2, 3, 6 }) { RunGroup(check, players); }
        }
        private static void RunGroup(Action<bool, string> check, int players)
        {
            int port;
            using (var reservation = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                reservation.ExclusiveAddressUse = true; reservation.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                port = ((IPEndPoint)reservation.LocalEndPoint).Port;
            }
            string executable = Assembly.GetExecutingAssembly().Location;
            var children = new Process[players]; var outputs = new Task<string>[players]; var errors = new Task<string>[players];
            var deadline = Stopwatch.StartNew();
            try
            {
                for (var i = 0; i < players; ++i)
                {
                    children[i] = Start(executable, i == 0 ? "host" : "guest", port, players);
                    outputs[i] = ReadWorldAsync(children[i].StandardOutput);
                    errors[i] = children[i].StandardError.ReadToEndAsync();
                }
                bool published = Task.WaitAll(outputs, Math.Max(0, 11000 - (int)deadline.ElapsedMilliseconds));
                check(published, players + " independent UDP peers publish worlds within eleven seconds");
                if (!published) { return; }
                var ticks = new uint[players]; var hashes = new ulong[players]; var seats = new bool[players];
                bool valid = true, equalTicks = true, equalHashes = true;
                for (var i = 0; i < players; ++i)
                {
                    int count, seat;
                    bool parsed = TryWorld(outputs[i].GetAwaiter().GetResult(), out ticks[i], out hashes[i], out count, out seat);
                    bool assigned = parsed && count == players && seat >= 0 && seat < players && !seats[seat];
                    valid &= assigned; if (assigned) { seats[seat] = true; }
                    equalTicks &= parsed && ticks[i] >= 90 && ticks[i] == ticks[0]; equalHashes &= parsed && hashes[i] == hashes[0];
                }
                check(valid && seats[0], players + " UDP processes receive distinct seats and the complete participant count");
                check(equalTicks, players + " real UDP worlds commit the same tick after ninety ticks");
                check(equalHashes, players + " independent real UDP engines have identical full world hashes");
                // Release the test barrier only when every world has been captured.
                foreach (var child in children)
                { if (!child.HasExited) { child.StandardInput.WriteLine("STOP"); child.StandardInput.Flush(); } }
                bool finished = true, successful = true; string detail = string.Empty;
                for (var i = 0; i < players; ++i)
                {
                    bool exited = children[i].WaitForExit(Remaining(deadline)); finished &= exited;
                    if (exited)
                    { successful &= children[i].ExitCode == 0; detail += errors[i].GetAwaiter().GetResult() + outputs[i].GetAwaiter().GetResult(); }
                    else { successful = false; }
                }
                check(finished, players + " UDP processes release their links within the fifteen-second group deadline");
                check(successful, players + " real UDP processes exit successfully" + (successful ? string.Empty : ": " + Clip(detail)));
            }
            finally { foreach (var child in children) { CloseChild(child); } }
        }
        private static async Task<string> ReadWorldAsync(StreamReader reader)
        {
            string line, output = string.Empty;
            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
            { output += line + Environment.NewLine; if (line.StartsWith("WORLD ", StringComparison.Ordinal)) { break; } }
            return output;
        }
        private static Process Start(string executable, string mode, int port, int players)
        {
            var process = new Process { StartInfo = new ProcessStartInfo {
                FileName = executable, Arguments = "--udp-peer " + mode + " " + port.ToString(CultureInfo.InvariantCulture)
                    + " " + players.ToString(CultureInfo.InvariantCulture), WorkingDirectory = Path.GetDirectoryName(executable),
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                RedirectStandardError = true, RedirectStandardInput = true } };
            try { if (!process.Start()) { throw new InvalidOperationException("Could not start the UDP test peer."); } return process; }
            catch { process.Dispose(); throw; }
        }
        private static int Remaining(Stopwatch elapsed) { return (int)Math.Max(0, 15000 - elapsed.ElapsedMilliseconds); }
        private static bool TryWorld(string output, out uint tick, out ulong hash, out int players, out int seat)
        {
            tick = 0; hash = 0; players = 0; seat = -1;
            using (var reader = new StringReader(output))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (!line.StartsWith("WORLD ", StringComparison.Ordinal)) { continue; }
                    string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    return parts.Length == 5 && uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out tick)
                        && ulong.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out hash)
                        && int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out players)
                        && int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out seat);
                }
            }
            return false;
        }
        private static string Clip(string text) { return text.Length <= 1000 ? text : text.Substring(0, 1000); }
        private static void CloseChild(Process process)
        {
            if (process == null) { return; }
            try { if (!process.HasExited) { process.Kill(); process.WaitForExit(1000); } }
            catch (InvalidOperationException) { /* It exited during cleanup. */ }
            catch (System.ComponentModel.Win32Exception) { /* It exited during Kill. */ }
            finally { process.Dispose(); }
        }
    }
}
