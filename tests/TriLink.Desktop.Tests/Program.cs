using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
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
            var environment = new HostEnvironment(release, new[] { "--demo" }, "desktop");
            using (var runtime = PluginRuntime.LoadFromProfile(
                Path.Combine(release, "plugins"),
                Path.Combine(release, "profiles", "desktop.profile.json"),
                environment,
                _ => { }))
            {
                runtime.StartAll();
                var shell = runtime.Services.GetRequired<IDesktopShell>();
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
                            form.Close();
                            Require(!form.IsDisposed && !form.Visible && tray.Visible,
                                "X hides form and keeps tray alive");

                            tray.ContextMenuStrip.Items.OfType<ToolStripMenuItem>()
                                .Single(item => item.Text == "打开 TriLink").PerformClick();
                            Require(form.Visible && form.ShowInTaskbar,
                                "tray Open restores form to taskbar");

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
}
