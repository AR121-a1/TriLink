using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using TriLink.Plugin;
using TriLink.PluginHost;

namespace TriLink.MinClient
{
    internal static class Program
    {
        private const string MutexName = "Local\\TriLink.MinClient.SingleInstance.v3";

        [STAThread]
        private static int Main(string[] args)
        {
            var screenshotPath = ReadOption(args, "--screenshot");
            var screenshotDelayMs = ReadIntOption(
                args,
                "--screenshot-delay-ms",
                900,
                100,
                60000);
            var mutexName = screenshotPath == null
                ? MutexName
                : MutexName + ".Screenshot." + Guid.NewGuid().ToString("N");

            bool createdNew;
            using (var mutex = new Mutex(true, mutexName, out createdNew))
            {
                if (!createdNew)
                {
                    ActivateExistingWindow();
                    return 0;
                }

                PluginRuntime runtime = null;
                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);

                    var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                    var profileName = ReadProfileName(args);
                    var environment = new HostEnvironment(baseDirectory, args, profileName);
                    runtime = PluginRuntime.LoadFromProfile(
                        Path.Combine(baseDirectory, "plugins"),
                        Path.Combine(
                            baseDirectory,
                            "profiles",
                            profileName + ".profile.json"),
                        environment,
                        message => TracePluginHost(baseDirectory, message));
                    runtime.StartAll();

                    var shell = runtime.Services.GetRequired<IDesktopShell>();
                    using (var form = shell.CreateMainWindow())
                    {
                        ConfigureScreenshot(
                            form,
                            shell,
                            screenshotPath,
                            screenshotDelayMs);
                        Application.Run(form);
                    }

                    GC.KeepAlive(mutex);
                    return 0;
                }
                catch (Exception exception)
                {
                    ReportStartupFailure(screenshotPath, exception);
                    return 2;
                }
                finally
                {
                    if (runtime != null)
                    {
                        runtime.Dispose();
                    }
                }
            }
        }

        private static void ConfigureScreenshot(
            Form form,
            IDesktopShell shell,
            string screenshotPath,
            int screenshotDelayMs)
        {
            if (screenshotPath == null)
            {
                return;
            }

            form.Shown += (_, __) =>
            {
                var timer = new System.Windows.Forms.Timer
                {
                    Interval = screenshotDelayMs,
                };
                timer.Tick += (sender, eventArgs) =>
                {
                    timer.Stop();
                    timer.Dispose();
                    var fullPath = Path.GetFullPath(screenshotPath);
                    var directory = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    shell.SaveScreenshot(form, fullPath);
                    shell.ExitApplication(form);
                };
                timer.Start();
            };
        }

        private static void ActivateExistingWindow()
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                NativeMethods.PostMessage(
                    NativeMethods.HwndBroadcast,
                    HostWindowMessages.ActivateMessage,
                    IntPtr.Zero,
                    IntPtr.Zero);
                Thread.Sleep(80);
            }
        }

        private static void ReportStartupFailure(string screenshotPath, Exception exception)
        {
            if (screenshotPath != null)
            {
                try
                {
                    File.WriteAllText(
                        Path.GetFullPath(screenshotPath) + ".error.txt",
                        exception.ToString());
                }
                catch
                {
                    // Preserve the original startup failure and exit code.
                }

                return;
            }

            MessageBox.Show(
                "插件平台启动失败：" + exception.Message,
                "TriLink",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        private static void TracePluginHost(string baseDirectory, string message)
        {
            var configuredPath = Environment.GetEnvironmentVariable("TRILINK_PLUGIN_LOG");
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                return;
            }

            try
            {
                var path = Path.IsPathRooted(configuredPath)
                    ? configuredPath
                    : Path.Combine(baseDirectory, configuredPath);
                File.AppendAllText(
                    Path.GetFullPath(path),
                    DateTime.Now.ToString("O") + " " + message + Environment.NewLine);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static string ReadOption(string[] args, string optionName)
        {
            for (var index = 0; index + 1 < args.Length; index++)
            {
                if (string.Equals(args[index], optionName, StringComparison.OrdinalIgnoreCase))
                {
                    return args[index + 1];
                }
            }

            return null;
        }

        private static string ReadProfileName(string[] args)
        {
            var profileName = ReadOption(args, "--profile") ?? "desktop";
            if (!Regex.IsMatch(
                profileName,
                @"^[a-z0-9]+(?:[.-][a-z0-9]+)*$",
                RegexOptions.CultureInvariant))
            {
                throw new ArgumentException("Invalid plugin profile name: " + profileName);
            }

            return profileName;
        }

        private static int ReadIntOption(
            string[] args,
            string optionName,
            int defaultValue,
            int minimum,
            int maximum)
        {
            var text = ReadOption(args, optionName);
            int value;
            if (!int.TryParse(text, out value))
            {
                return defaultValue;
            }

            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static class NativeMethods
        {
            internal static readonly IntPtr HwndBroadcast = new IntPtr(0xffff);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool PostMessage(
                IntPtr windowHandle,
                int message,
                IntPtr wordParameter,
                IntPtr longParameter);
        }
    }
}
