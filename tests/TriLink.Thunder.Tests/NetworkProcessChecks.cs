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
            int hostPort;
            if (args == null || args.Length != 3 || args[0] != "--udp-peer"
                || (args[1] != "host" && args[1] != "guest")
                || !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out hostPort)
                || hostPort < 1 || hostPort > ushort.MaxValue)
            {
                Console.Error.WriteLine("Expected --udp-peer host|guest <hostPort>.");
                return 2;
            }

            bool hosting = args[1] == "host";
            try
            {
                using (var session = new GameSession(new UdpGameLinkFactory()))
                {
                    if (hosting) session.Host(hostPort, 13579U);
                    else session.Join("127.0.0.1", hostPort, 0);
                    Console.WriteLine("READY " + session.LocalPort.ToString(CultureInfo.InvariantCulture));
                    Console.Out.Flush();
                    var elapsed = Stopwatch.StartNew();
                    long previous = 0;
                    bool reported = false;
                    while (elapsed.ElapsedMilliseconds < 8000)
                    {
                        long now = elapsed.ElapsedMilliseconds;
                        // One network batch per call avoids overshooting the final tick
                        // after a scheduler stall. Wall time still controls the deadline.
                        int delta = (int)Math.Min(GameSession.NetworkIntervalMs, now - previous);
                        previous = now;
                        session.Update(0, hosting && reported ? 0 : delta);
                        if (!reported && session.Engine != null && session.Engine.Tick >= 90)
                        {
                            Console.WriteLine("WORLD " + session.Engine.Tick.ToString(CultureInfo.InvariantCulture)
                                + " " + SessionChecks.Hash(session.Engine).ToString(CultureInfo.InvariantCulture));
                            Console.Out.Flush();
                            reported = true;
                            // Disposal sends Bye; the host continues polling its socket
                            // without committing additional simulation ticks.
                            if (!hosting) return 0;
                        }
                        if (session.Mode == GameSessionMode.Idle)
                        {
                            if (hosting && reported) return 0;
                            Console.Error.WriteLine("UDP peer stopped early: " + session.Status);
                            return 3;
                        }
                        Thread.Sleep(5);
                    }
                    Console.Error.WriteLine("UDP peer exceeded eight seconds: " + session.Status);
                    return 4;
                }
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 5;
            }
        }

        public static void Run(Action<bool, string> check)
        {
            if (check == null) throw new ArgumentNullException(nameof(check));
            int port;
            using (var reservation = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                reservation.ExclusiveAddressUse = true;
                reservation.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                port = ((IPEndPoint)reservation.LocalEndPoint).Port;
            }
            string executable = Assembly.GetExecutingAssembly().Location;
            Process host = null, guest = null;
            try
            {
                var deadline = Stopwatch.StartNew();
                host = Start(executable, "host", port);
                Task<string> hostOutput = host.StandardOutput.ReadToEndAsync();
                Task<string> hostError = host.StandardError.ReadToEndAsync();
                guest = Start(executable, "guest", port);
                Task<string> guestOutput = guest.StandardOutput.ReadToEndAsync();
                Task<string> guestError = guest.StandardError.ReadToEndAsync();
                bool hostFinished = host.WaitForExit(Remaining(deadline));
                bool guestFinished = guest.WaitForExit(Remaining(deadline));
                check(hostFinished && guestFinished, "independent UDP game processes finish within ten seconds");
                if (!hostFinished || !guestFinished) return;

                string hostText = hostOutput.GetAwaiter().GetResult();
                string guestText = guestOutput.GetAwaiter().GetResult();
                string errors = hostError.GetAwaiter().GetResult() + guestError.GetAwaiter().GetResult();
                bool successful = host.ExitCode == 0 && guest.ExitCode == 0;
                check(successful, "real UDP host and guest exit successfully"
                    + (successful ? string.Empty : ": " + Clip(errors + hostText + guestText)));

                uint hostTick, guestTick;
                ulong hostHash, guestHash;
                bool hostWorld = TryWorld(hostText, out hostTick, out hostHash);
                bool guestWorld = TryWorld(guestText, out guestTick, out guestHash);
                check(hostWorld && guestWorld, "both independent processes publish committed world hashes");
                check(hostWorld && guestWorld && hostTick >= 90 && hostTick == guestTick,
                    "real UDP sessions commit the same final tick after at least ninety ticks");
                check(hostWorld && guestWorld && hostHash == guestHash,
                    "independent real UDP game engines have identical full world state");
            }
            finally
            {
                CloseChild(guest);
                CloseChild(host);
            }
        }

        private static Process Start(string executable, string mode, int port)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "--udp-peer " + mode + " " + port.ToString(CultureInfo.InvariantCulture),
                    WorkingDirectory = Path.GetDirectoryName(executable),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            try
            {
                if (!process.Start()) throw new InvalidOperationException("Could not start the UDP test peer.");
                return process;
            }
            catch { process.Dispose(); throw; }
        }

        private static int Remaining(Stopwatch elapsed)
        { return (int)Math.Max(0, 10000 - elapsed.ElapsedMilliseconds); }

        private static bool TryWorld(string output, out uint tick, out ulong hash)
        {
            tick = 0;
            hash = 0;
            using (var reader = new StringReader(output))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (!line.StartsWith("WORLD ", StringComparison.Ordinal)) continue;
                    string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    return parts.Length == 3
                        && uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out tick)
                        && ulong.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out hash);
                }
            }
            return false;
        }

        private static string Clip(string text) { return text.Length <= 1000 ? text : text.Substring(0, 1000); }

        private static void CloseChild(Process process)
        {
            if (process == null) return;
            try
            {
                if (!process.HasExited)
                {
                    // Only Process objects started above are eligible for termination.
                    process.Kill();
                    process.WaitForExit(1000);
                }
            }
            catch (InvalidOperationException) { /* It exited during cleanup. */ }
            catch (System.ComponentModel.Win32Exception) { /* It exited during Kill. */ }
            finally { process.Dispose(); }
        }
    }
}
