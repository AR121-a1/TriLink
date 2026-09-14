using System;
using System.Linq;
using System.Windows.Forms;
using TriLink.Core;
using TriLink.MinClient;
using TriLink.Plugin;

namespace TriLink.Plugins.Desktop
{
    public sealed class DesktopPlugin : ITriLinkPlugin, IDesktopShell
    {
        private IRoomNetwork _network;
        private IDeviceDiscoveryService _deviceDiscovery;
        private ISimulationControl _simulation;
        private IPluginCatalog _catalog;
        private bool _demoMode;
        private bool _showPluginsOnStart;
        private string _profileName;

        public void Configure(IPluginContext context)
        {
            _network = context.GetRequired<IRoomNetwork>();
            _deviceDiscovery = context.GetRequired<IDeviceDiscoveryService>();
            _simulation = context.GetRequired<ISimulationControl>();
            _catalog = context.GetRequired<IPluginCatalog>();
            _demoMode = context.Environment.DemoMode;
            _profileName = context.Environment.ProfileName;
            _showPluginsOnStart = context.Environment.Arguments.Any(
                argument => string.Equals(
                    argument,
                    "--plugins-view",
                    StringComparison.OrdinalIgnoreCase));
            context.Provide<IDesktopShell>(this);
            context.Log("WinForms 桌面壳已注册。");
        }

        public void Start()
        {
        }

        public void Stop()
        {
        }

        public Form CreateMainWindow()
        {
            return new MainForm(
                _demoMode,
                _showPluginsOnStart,
                _network,
                _deviceDiscovery,
                _simulation,
                _catalog,
                _profileName);
        }

        public void SaveScreenshot(Form form, string path)
        {
            var window = RequireMainForm(form);
            window.SaveScreenshot(path);
        }

        public void ExitApplication(Form form)
        {
            var window = RequireMainForm(form);
            window.ExitApplication();
        }

        private static MainForm RequireMainForm(Form form)
        {
            var window = form as MainForm;
            if (window == null)
            {
                throw new ArgumentException("Unexpected desktop form.", nameof(form));
            }

            return window;
        }
    }
}
