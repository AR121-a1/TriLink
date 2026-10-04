using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Threading.Tasks;
using TriLink.Core;
using TriLink.Plugin;
using TriLink.PluginHost;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length != 1)
            {
                throw new ArgumentException("Usage: TriLink.Desktop.Tests.exe <runtime-directory>");
            }
            var release = Path.GetFullPath(args[0]);
            var environment = new HostEnvironment(release, new[] { "--demo", "--safe-mode" }, "desktop");
            using (var runtime = PluginRuntime.LoadFromProfile(
                Path.Combine(release, "plugins"),
                Path.Combine(release, "profiles", "desktop.profile.json"),
                environment,
                _ => { }))
            {
                runtime.StartAll();
                var shell = runtime.Services.GetRequired<IDesktopShell>();
                VerifyModeGuards(runtime, shell, release);
                using (var form = shell.CreateMainWindow())
                {
                    // Access the real tray menu without interacting with the user's taskbar.
                    var tray = (NotifyIcon)form.GetType()
                        .GetField("_trayIcon", BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(form);
                    Exception failure = null;
                    form.Shown += (_, __) => form.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            Require(form.Visible && tray.Visible, "startup exposes form and tray");
                            Field<Button>(form, "_modulesButton").PerformClick();
                            var managerWindow = Field<Form>(form, "_modulesWindow");
                            var moduleGrid = Field<DataGridView>(managerWindow, "_grid");
                            moduleGrid.CurrentCell = moduleGrid.Rows.Cast<DataGridViewRow>()
                                .Single(row => ((ModuleInfo)row.Tag).Id == "trilink.text-tools").Cells[0];
                            Field<Button>(managerWindow, "_open").PerformClick();
                            var featureWindow = managerWindow.OwnedForms.Single();
                            form.Close();
                            Require(!form.IsDisposed && !form.Visible && tray.Visible,
                                "X hides form and keeps tray alive");
                            Require(managerWindow.IsDisposed || !managerWindow.Visible,
                                "main X leaves no visible orphan manager window");
                            Require(featureWindow.IsDisposed || !featureWindow.Visible,
                                "main X also hides nested feature window");

                            tray.ContextMenuStrip.Items.OfType<ToolStripMenuItem>()
                                .Single(item => item.Text == "打开 TriLink").PerformClick();
                            Require(form.Visible && form.ShowInTaskbar,
                                "tray Open restores form to taskbar");
                            Field<Button>(form, "_modulesButton").PerformClick();
                            managerWindow = Field<Form>(form, "_modulesWindow");
                            Require(managerWindow.Visible, "module manager can reopen after tray restore");
                            Require(featureWindow.Visible, "tray restore preserves open feature window");

                            var shutdown = new FormClosingEventArgs(CloseReason.WindowsShutDown, false);
                            form.GetType().GetMethod("OnFormClosing",
                                BindingFlags.Instance | BindingFlags.NonPublic)
                                .Invoke(form, new object[] { shutdown });
                            Require(!shutdown.Cancel && form.Visible, "Windows shutdown is not canceled");

                            form.Close();
                            Require(!form.Visible && !form.IsDisposed, "X can hide again after restore");
                            tray.ContextMenuStrip.Items.OfType<ToolStripMenuItem>()
                                .Single(item => item.Text == "退出").PerformClick();
                            Require(form.IsDisposed && !tray.Visible,
                                "tray Exit closes hidden form and disposes icon");
                            Require(managerWindow.IsDisposed, "tray exit disposes owned manager window");
                            Require(featureWindow.IsDisposed, "tray exit disposes nested feature window");
                        }
                        catch (Exception exception)
                        {
                            failure = exception;
                            if (!form.IsDisposed)
                            {
                                shell.ExitApplication(form);
                            }
                        }
                    }));
                    Application.Run(form);
                    if (failure != null)
                    {
                        throw new InvalidOperationException("Desktop lifecycle regression failed.", failure);
                    }
                }
            }

            Console.WriteLine("PASS desktop lifecycle (automated form test, not interactive desktop acceptance)");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void Require(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException(description);
        }

        Console.WriteLine("PASS " + description);
    }

    private static T Field<T>(Form form, string name)
    {
        return (T)form.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
    }

    private static void VerifyModeGuards(PluginRuntime runtime, IDesktopShell shell, string release)
    {
        var fake = new FakeDiscovery();
        Type formType;
        using (var reference = shell.CreateMainWindow()) { formType = reference.GetType(); }
        var ui = Path.Combine(Path.GetDirectoryName(release), "tests", Path.GetFileName(release), "ui");
        Directory.CreateDirectory(ui);
        using (var form = (Form)Activator.CreateInstance(formType, new object[] {
            false, false, runtime.Services.GetRequired<IRoomNetwork>(), fake,
            runtime.Services.GetRequired<ISimulationControl>(),
            runtime.Services.GetRequired<IPluginCatalog>(), "desktop",
            runtime.Services.GetRequired<IModuleManagementService>(), runtime.Services.GetRequired<IModuleFeatureRegistry>() }))
        {
            try
            {
                form.Show();
                Application.DoEvents();
                var search = Field<Button>(form, "_searchButton");
                var simulate = Field<Button>(form, "_simulateButton");
                var choices = Field<ComboBox>(form, "_currentNodeCombo");
                var nearby = Field<DataGridView>(form, "_nearbyGrid");
                Require(choices.Items.Count == 0 && !search.Enabled,
                    "normal startup does not select fake A or enable hardware search without a device");
                Require(Field<Button>(form, "_pollingButton").Text == "暂停轮询",
                    "initial polling caption reflects already-running service");
                var background = Field<Button>(form, "_backgroundButton");
                Require(background.Right <= background.Parent.ClientSize.Width,
                    "hardware waiting text does not hide toolbar actions");
                search.PerformClick();
                Require(fake.SearchCalls == 0 && !Field<TextBox>(form, "_eventLog").Text.Contains("模拟搜索完成"),
                    "no-device search cannot silently fall back to simulation");
                shell.SaveScreenshot(form, Path.Combine(ui, "ui-hardware-empty.png"));
                var modulesButton = Field<Button>(form, "_modulesButton");
                Require(modulesButton.Enabled && modulesButton.Visible && modulesButton.Right <= modulesButton.Parent.ClientSize.Width,
                    "explicit module entry visible without hardware");
                modulesButton.PerformClick();
                Application.DoEvents();
                var modulesWindow = Field<Form>(form, "_modulesWindow");
                Require(modulesWindow != null && modulesWindow.Visible && modulesWindow.Owner == form,
                    "main button opens owned module manager");
                modulesWindow.Close();
                Require(modulesWindow.IsDisposed && form.Visible && !form.IsDisposed,
                    "closing module manager does not hide or exit main window");
                var normalSize = form.Size;
                form.Size = form.MinimumSize;
                Application.DoEvents();
                Require(modulesButton.Right <= modulesButton.Parent.ClientSize.Width,
                    "module entry remains within minimum-width toolbar");
                shell.SaveScreenshot(form, Path.Combine(ui, "ui-hardware-minimum.png"));
                form.Size = normalSize;

                simulate.PerformClick();
                Require(choices.Items.Count == 3 && search.Enabled, "simulation requires explicit activation");
                search.PerformClick();
                Require(fake.SearchCalls == 0 && nearby.Rows.Count == 2, "simulation never calls the hardware adapter");
                shell.SaveScreenshot(form, Path.Combine(ui, "ui-explicit-simulation.png"));
                simulate.PerformClick();
                Require(choices.Items.Count == 0 && !search.Enabled, "exit simulation returns to hardware waiting state");

                var device = new TriLinkDevice { NodeId = "80:B5:4E:00:00:01", DisplayName = "TEST LOCAL",
                    PortName = "COM99", PnpDeviceId = @"USB\VID_303A&PID_1001\TEST" };
                fake.Arrive(device);
                Require(choices.Items.Count == 1 && search.Enabled
                    && !Field<Button>(form, "_createRoomButton").Enabled,
                    "recognized local device enables real search; hardware Room uses its own module");
                fake.Peers = new[] { new TriLinkPeer { NodeId = "80:B5:4E:00:00:02", DisplayName = "TEST REMOTE", Rssi = -42 } };
                search.PerformClick();
                Require(fake.SearchCalls == 1 && nearby.Rows.Count == 1 && choices.Items.Count == 1,
                    "real peers are displayed without becoming fake local computers");
                fake.Peers = new TriLinkPeer[0];
                search.PerformClick();
                Require(fake.SearchCalls == 2 && nearby.Rows.Count == 0, "completed empty search clears stale real peers");
                fake.FailSearch = true;
                search.PerformClick();
                Require(Field<TextBox>(form, "_eventLog").Text.Contains("搜索失败：TEST TIMEOUT"),
                    "adapter timeout is displayed as failure, not zero-peer success");
                fake.Remove(device);
                Require(!search.Enabled && nearby.Rows.Count == 0, "disconnect disables search and clears real results");

                fake.Trip();
                Require(Field<Button>(form, "_pollingButton").Text == "启动轮询", "paused polling presents a manual restart");
                shell.SaveScreenshot(form, Path.Combine(ui, "ui-polling-paused.png"));
                Field<Button>(form, "_pollingButton").PerformClick();
                Require(fake.IsPolling && fake.ConsecutiveFailures == 0, "manual resume clears failure count");
            }
            finally { shell.ExitApplication(form); }
        }
        Console.WriteLine("PASS mode guards with fake discovery; not physical USB verification");
    }

    private sealed class FakeDiscovery : IDeviceDiscoveryService
    {
        public event EventHandler<TriLinkDeviceEventArgs> DeviceArrived;
        public event EventHandler<TriLinkDeviceEventArgs> DeviceRemoved;
        public event EventHandler<string> Status;
        public event EventHandler PollingStateChanged;
        public IReadOnlyList<TriLinkDevice> Devices { get { return new TriLinkDevice[0]; } }
        public bool IsPolling { get; private set; } = true;
        public int ConsecutiveFailures { get; private set; }
        public int FailureLimit { get { return 5; } }
        public int SearchCalls { get; private set; }
        public bool FailSearch { get; set; }
        public IReadOnlyList<TriLinkPeer> Peers { get; set; } = new TriLinkPeer[0];
        public void Arrive(TriLinkDevice device) { DeviceArrived?.Invoke(this, new TriLinkDeviceEventArgs(device)); }
        public void Remove(TriLinkDevice device) { DeviceRemoved?.Invoke(this, new TriLinkDeviceEventArgs(device)); }
        public void Start() { ResumePolling(); }
        public void ResumePolling() { IsPolling = true; ConsecutiveFailures = 0; PollingStateChanged?.Invoke(this, EventArgs.Empty); }
        public void PausePolling() { IsPolling = false; PollingStateChanged?.Invoke(this, EventArgs.Empty); }
        public void Trip()
        {
            ConsecutiveFailures = 5;
            PausePolling();
            Status?.Invoke(this, "[测试夹具] 串口轮询已自动暂停：连续 5 次失败；点击启动轮询重试。");
        }
        public void RequestScan() { }
        public Task<IReadOnlyList<TriLinkPeer>> SearchNearbyAsync(string port)
        {
            SearchCalls++;
            return FailSearch ? Task.FromException<IReadOnlyList<TriLinkPeer>>(new TimeoutException("TEST TIMEOUT"))
                : Task.FromResult(Peers);
        }
        public void Dispose() { }
    }
}
