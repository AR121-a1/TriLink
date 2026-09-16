using System;
using System.IO;
using System.Linq;
using TriLink.Plugin;
using TriLink.PluginHost;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var root = Path.GetFullPath(args[0]);
            var safe = bool.Parse(args[4]);
            var environment = new HostEnvironment(root, safe ? new[] { "--demo", "--safe-mode" } : new[] { "--demo" });
            using (var runtime = PluginRuntime.LoadFromProfile(Path.Combine(root, "plugins"),
                Path.Combine(root, "profiles", "desktop.profile.json"), environment))
            {
                runtime.StartAll();
                var descriptor = runtime.Plugins.Single(p => p.Id == args[1]);
                if ((descriptor.State == PluginState.Active) != bool.Parse(args[2])) { throw new Exception("Activation mismatch."); }
                if (bool.Parse(args[2]))
                {
                    var file = Directory.GetFiles(descriptor.SourceDirectory, "*.dll").Single();
                    var loaded = AppDomain.CurrentDomain.GetAssemblies().Single(assembly => assembly.GetName().Name == Path.GetFileNameWithoutExtension(file));
                    if (!string.Equals(loaded.Location, file, StringComparison.OrdinalIgnoreCase)) { throw new Exception("Wrong actual assembly location: " + loaded.Location); }
                }
                if (descriptor.SourceDirectory.Contains("module-data") != bool.Parse(args[3])) { throw new Exception("Package override mismatch."); }
            }
            Console.WriteLine("loaded path and lifecycle verified");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
}
