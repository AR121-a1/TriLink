using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using TriLink.Plugin;
using TriLink.PluginHost;

namespace TriLink.Thunder.Tests
{
    internal static class Program
    {
        private static int _checks;
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && args[0] == "--udp-peer") { return NetworkProcessChecks.RunPeer(args); }
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                EngineChecks.Run(Check); WireChecks.Run(Check); TransportChecks.Run(Check); SessionChecks.Run(Check);
                NetworkProcessChecks.Run(Check);
                if (args.Length == 2)
                {
                    var runtimeRoot = Path.GetFullPath(args[0]);
                    using (var runtime = PluginRuntime.LoadFromProfile(Path.Combine(runtimeRoot, "plugins"),
                        Path.Combine(runtimeRoot, "profiles", "desktop.profile.json"), new HostEnvironment(runtimeRoot, new[] { "--demo" })))
                    {
                        runtime.StartAll();
                        var features = runtime.Services.GetRequired<IModuleFeatureRegistry>();
                        Check(features.Features.Any(feature => feature.Id == "trilink.thunder.play"), "game feature registers through the plugin profile");
                        using (var view = features.CreateView("trilink.thunder.play"))
                        { Check(view.GetType().Name == "ThunderView", "game factory creates its independent view"); }
                    }
                    Directory.CreateDirectory(args[1]); ViewChecks.Run(Check, args[1]);
                }
                Console.WriteLine("PASS thunder checks=" + _checks + " (software and loopback only; no ESP32 hardware)");
                return 0;
            }
            catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        }
        private static void Check(bool result, string name)
        { ++_checks; if (!result) { throw new InvalidOperationException(name); } Console.WriteLine("PASS " + name); }
    }
}
