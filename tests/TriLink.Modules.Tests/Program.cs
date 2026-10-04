using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using TriLink.Plugin;
using TriLink.PluginHost;
using TriLink.Plugins.Modules;
using TriLink.Plugins.TextTools;

internal static class Program
{
    private static int _passed;
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var release = Path.GetFullPath(args[0]);
            var root = Path.Combine(Path.GetDirectoryName(release), "tests", Path.GetFileName(release), "fixtures", "modules-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            CopyDirectory(Path.Combine(release, "plugins"), Path.Combine(root, "plugins"));
            CopyDirectory(Path.Combine(release, "profiles"), Path.Combine(root, "profiles"));
            var environment = new HostEnvironment(root, new[] { "--demo" });
            var store = new ModuleStore(environment);
            var baseline = store.ReadSelection(false);
            store.Validate(baseline);
            Check(baseline.enabled.Length == 9, "nine built-in modules in baseline");
            var catalog = new Catalog { Plugins = store.Resolve(baseline).Select(m => new PluginDescriptor {
                Id = m.id, DisplayName = m.displayName, Version = m.version, State = PluginState.Active,
                SourceDirectory = m.SourceDirectory }).ToList() };
            var manager = new ModuleManagementService(store, catalog, false);
            Check(!manager.GetSnapshot().RestartRequired, "unchanged state does not require restart");
            manager.SetEnabled("trilink.text-tools", false);
            var optional = manager.GetSnapshot().Modules.Single(m => m.Id == "trilink.text-tools");
            Check(optional.Running && !optional.EnabledNextStart && optional.PendingChange,
                "disable is pending; running module remains active");
            Check(!store.ReadSelection(false).enabled.Contains("trilink.text-tools"), "desired state persisted");
            Probe(root, "trilink.text-tools", false, false);
            var selectionFile = Path.Combine(root, "module-data", "desktop.json");
            var before = File.ReadAllText(selectionFile);
            Reject(() => manager.SetEnabled("trilink.desktop", false), "desktop protected");
            Reject(() => manager.SetEnabled("trilink.modules", false), "manager protected");
            Reject(() => manager.SetEnabled("trilink.rooms", false), "required room dependency protected");
            Reject(() => manager.SetEnabled("missing.module", true), "unknown module rejected");
            Check(File.ReadAllText(selectionFile) == before, "rejected operations preserve persisted state");
            manager.CancelPendingChanges();
            Check(!manager.GetSnapshot().RestartRequired && store.ReadSelection(false).enabled.Contains("trilink.text-tools"), "cancel restores startup selection");

            var source = Path.Combine(root, "source-package");
            CopyDirectory(Path.Combine(root, "plugins", "trilink.text-tools"), source);
            var path = Path.Combine(source, "plugin.json");
            var original = File.ReadAllText(path);
            var manifest = Json.Deserialize<PluginManifest>(original);
            manifest.version = "1.0.1";
            File.WriteAllText(path, Json.Serialize(manifest));
            var preview = manager.PreviewPackage(path);
            Reject(() => manager.ImportPackage(path, "wrong-fingerprint"), "confirmation fingerprint enforced");
            Check(!manager.GetSnapshot().RestartRequired, "failed import leaves desired state unchanged");
            manager.ImportPackage(path, preview.Fingerprint);
            optional = manager.GetSnapshot().Modules.Single(m => m.Id == "trilink.text-tools");
            Check(optional.Version == "1.0.1" && optional.RunningVersion == "1.0.0" && optional.PendingChange,
                "update separates running version from next version");
            Check(optional.SourceDirectory.Contains("module-data")
                && File.ReadAllText(Path.Combine(root, "plugins", "trilink.text-tools", "plugin.json")) == original,
                "update stages beside original; never overwrites loaded DLL or manifest");
            Check(store.ReadSelection(false).packages.Count == 1, "import map persisted");
            Probe(root, "trilink.text-tools", true, true);
            manager.RestoreBuiltIns();
            Check(store.ReadSelection(false).packages.Count == 0 && !manager.GetSnapshot().RestartRequired,
                "restore removes package overrides without deleting retained package");

            manifest.id = "example.text-tools";
            File.WriteAllText(path, Json.Serialize(manifest));
            preview = manager.PreviewPackage(path);
            manager.ImportPackage(path, preview.Fingerprint);
            Check(!manager.GetSnapshot().Modules.Single(m => m.Id == manifest.id).EnabledNextStart,
                "new import disabled by default; no code activation on import");
            Reject(() => manager.SetEnabled(manifest.id, true), "duplicate enabled assembly identity rejected");
            manager.SetEnabled("trilink.text-tools", false);
            manager.SetEnabled(manifest.id, true);
            Probe(root, manifest.id, true, true);
            manager.CancelPendingChanges();

            manifest.id = "example.requires-missing";
            manifest.requiresServices = new[] { "example.missing-service" };
            File.WriteAllText(path, Json.Serialize(manifest));
            preview = manager.PreviewPackage(path);
            manager.ImportPackage(path, preview.Fingerprint);
            Reject(() => manager.SetEnabled(manifest.id, true), "missing service prevents activation");
            manager.CancelPendingChanges();

            File.WriteAllText(path, original);
            manifest = Json.Deserialize<PluginManifest>(original);
            manifest.sha256 = new string('0', 64);
            File.WriteAllText(path, Json.Serialize(manifest));
            Reject(() => manager.PreviewPackage(path), "bad hash rejected");
            manifest = Json.Deserialize<PluginManifest>(original);
            manifest.entryAssembly = "../escape.dll";
            File.WriteAllText(path, Json.Serialize(manifest));
            Reject(() => manager.PreviewPackage(path), "path traversal rejected");
            manifest.entryAssembly = "file:stream.dll";
            File.WriteAllText(path, Json.Serialize(manifest));
            Reject(() => manager.PreviewPackage(path), "alternate data stream rejected");
            manifest = Json.Deserialize<PluginManifest>(original);
            manifest.hostApi = "99.0";
            File.WriteAllText(path, Json.Serialize(manifest));
            Reject(() => manager.PreviewPackage(path), "incompatible host API rejected");
            File.WriteAllText(path, new string('x', 65537));
            Reject(() => manager.PreviewPackage(path), "oversize manifest bounded");
            File.WriteAllText(path, original);
            preview = manager.PreviewPackage(path);
            File.AppendAllText(path, " ");
            Reject(() => manager.ImportPackage(path, preview.Fingerprint), "source mutation after preview rejected");
            File.WriteAllText(path, original);
            var dll = Path.Combine(source, manifest.entryAssembly);
            using (var stream = new FileStream(dll, FileMode.Open, FileAccess.Write)) { stream.SetLength(16 * 1024 * 1024 + 1); }
            Reject(() => manager.PreviewPackage(path), "oversize DLL bounded before loading");

            File.WriteAllText(selectionFile, "{ broken");
            Reject(() => store.ReadSelection(false), "corrupt configuration fails closed");
            var safe = new ModuleManagementService(store, catalog, true);
            Check(safe.GetSnapshot().SafeMode && safe.GetSnapshot().Modules.Count == 9, "safe mode ignores corrupt overlay");
            Probe(root, "trilink.text-tools", true, false, true);
            Reject(() => safe.SetEnabled("trilink.text-tools", false), "safe mode only permits recovery");
            safe.RestoreBuiltIns();
            Check(safe.GetSnapshot().RestartRequired && store.ReadSelection(false).packages.Count == 0, "safe recovery saves valid baseline");

            VerifyFeatures();
            VerifyManagerUi(release, root, catalog);
            Console.WriteLine("PASS modules assertions=" + _passed + " (fixtures only; no release configuration changed)");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }

    private static void VerifyFeatures()
    {
        using (var registry = new ModuleFeatureRegistry())
        {
            var created = 0;
            var feature = new ModuleFeature { Id = "test.view", ModuleId = "test.module", Title = "Test" };
            var lease = registry.Register(feature, () => { created++; return new Panel(); });
            feature.Title = "modified";
            Check(created == 0 && registry.Features[0].Title == "Test", "feature registration lazy and defensive");
            Reject(() => registry.Register(feature, () => new Panel()), "duplicate feature rejected");
            using (var view = registry.CreateView("test.view")) { Check(created == 1, "feature created on demand"); }
            lease.Dispose(); lease.Dispose();
            Check(registry.Features.Count == 0, "owned registration cleanup idempotent");
            Reject(() => registry.CreateView("test.view"), "removed feature inaccessible");
        }
        Check(TextToolsView.Inspect("你好").Contains("UTF-8 字节数：6") && TextToolsView.Inspect("你好").Contains("5L2g5aW9"),
            "UTF-8 and Base64 preserve Chinese text");
        Check(TextToolsView.Inspect("").Contains("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"), "empty input SHA-256 correct");
        Check(TextToolsView.Inspect(new string('a', 16384)).Contains("16384"), "maximum text accepted");
        Reject(() => TextToolsView.Inspect(new string('a', 16385)), "text input upper bound enforced");
    }

    private static void VerifyManagerUi(string release, string root, IPluginCatalog catalog)
    {
        var ui = Path.Combine(Path.GetDirectoryName(release), "tests", Path.GetFileName(release), "ui");
        Directory.CreateDirectory(ui);
        var manager = new ModuleManagementService(new ModuleStore(new HostEnvironment(root, new string[0])), catalog, false);
        var type = Assembly.LoadFrom(Path.Combine(release, "plugins", "trilink.desktop", "TriLink.Plugin.Desktop.dll"))
            .GetType("TriLink.MinClient.ModulesForm", true);
        using (var features = new ModuleFeatureRegistry())
        using (var lease = features.Register(new ModuleFeature { Id = "trilink.text-tools.inspect", ModuleId = "trilink.text-tools", Title = "文本数据检查" }, () => new TextToolsView()))
        using (var form = (Form)Activator.CreateInstance(type, new object[] { manager, features }))
        {
            form.Show(); Application.DoEvents();
            var grid = Field<DataGridView>(form, "_grid");
            Check(grid.Rows.Count == 9, "management window lists all nine modules");
            Select(grid, "trilink.desktop");
            Check(!Field<Button>(form, "_toggle").Enabled, "UI prevents disabling foundation module");
            Select(grid, "trilink.text-tools");
            Check(Field<Button>(form, "_toggle").Enabled && Field<Button>(form, "_open").Enabled, "optional module has explicit actions");
            Shot(form, Path.Combine(ui, "ui-modules.png"));
            Field<Button>(form, "_open").PerformClick(); Application.DoEvents();
            var tool = form.OwnedForms.Single();
            tool.Controls.Find("TextInput", true).Single().Text = "你好，TriLink！";
            ((Button)tool.Controls.Find("Calculate", true).Single()).PerformClick();
            Check(tool.Controls.Find("TextOutput", true).Single().Text.Contains("SHA-256"), "module button opens operational text tool");
            Shot(tool, Path.Combine(ui, "ui-module-text-tools.png"));
            tool.Close();
            Check(tool.IsDisposed, "closing feature disposes its window");
            Field<Button>(form, "_toggle").PerformClick();
            Check(manager.GetSnapshot().RestartRequired && Field<Button>(form, "_open").Enabled, "pending disable keeps current feature available");
            Shot(form, Path.Combine(ui, "ui-modules-pending.png"));
            Field<Button>(form, "_cancel").PerformClick();
            Check(!manager.GetSnapshot().RestartRequired, "UI cancel restores startup configuration");
            form.Size = form.MinimumSize;
            Shot(form, Path.Combine(ui, "ui-modules-minimum.png"));
            form.Close();
            Check(form.IsDisposed, "manager X closes only the manager window");
        }
        using (var features = new ModuleFeatureRegistry())
        using (var form = (Form)Activator.CreateInstance(type, new object[] { new EmptyManager(), features }))
        {
            form.Show(); Application.DoEvents();
            Check(!Field<Button>(form, "_toggle").Enabled && !Field<Button>(form, "_open").Enabled, "empty state has no invalid module actions");
            Shot(form, Path.Combine(ui, "ui-modules-empty.png"));
            type.GetMethod("Apply", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form,
                new object[] { new Action(() => { throw new InvalidOperationException("测试：依赖服务缺失，配置未变更。"); }), "unused" });
            Check(Field<Label>(form, "_status").Text.Contains("依赖服务缺失"), "operation error shown inline");
            Shot(form, Path.Combine(ui, "ui-modules-error.png"));
            form.Close();
        }
    }
    private static void Probe(string root, string id, bool active, bool imported, bool safeMode = false)
    {
        var executable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TriLink.ModuleProbe.exe");
        var start = new ProcessStartInfo(executable, "\"" + root + "\" " + id + " " + active + " " + imported + " " + safeMode)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using (var process = Process.Start(start))
        {
            if (!process.WaitForExit(15000)) { process.Kill(); throw new TimeoutException("Module startup probe timed out."); }
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            Check(process.ExitCode == 0, "fresh-process startup: " + id + " active=" + active + " imported=" + imported + " safe=" + safeMode + " " + output.Trim());
        }
    }
    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source)) { File.Copy(file, Path.Combine(target, Path.GetFileName(file)), false); }
        foreach (var directory in Directory.GetDirectories(source)) { CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory))); }
    }
    private static void Select(DataGridView grid, string id)
    { grid.CurrentCell = grid.Rows.Cast<DataGridViewRow>().Single(row => ((ModuleInfo)row.Tag).Id == id).Cells[0]; }
    private static T Field<T>(Form form, string name)
    { return (T)form.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form); }
    private static void Shot(Form form, string path)
    { Application.DoEvents(); using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(path); } }
    private static void Check(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException(message); } _passed++; Console.WriteLine("PASS " + message); }
    private static void Reject(Action action, string message)
    { try { action(); } catch (Exception) { Check(true, message); return; } throw new InvalidOperationException("Expected rejection: " + message); }
    private sealed class Catalog : IPluginCatalog { public IReadOnlyList<PluginDescriptor> Plugins { get; set; } }
    private sealed class EmptyManager : IModuleManagementService
    {
        public ModuleSnapshot GetSnapshot() { return new ModuleSnapshot { Modules = new ModuleInfo[0], Notice = "无数据测试夹具" }; }
        public ModulePackagePreview PreviewPackage(string path) { throw new NotSupportedException(); }
        public void ImportPackage(string path, string fingerprint) { throw new NotSupportedException(); }
        public void SetEnabled(string id, bool enabled) { throw new NotSupportedException(); }
        public void CancelPendingChanges() { }
        public void RestoreBuiltIns() { }
    }
}
