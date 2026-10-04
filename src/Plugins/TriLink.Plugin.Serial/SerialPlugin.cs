using TriLink.Core;
using TriLink.MinClient.Serial;
using TriLink.Plugin;

namespace TriLink.Plugins.Serial
{
    public sealed class SerialPlugin : ITriLinkPlugin
    {
        private TriLinkSerialWatcher _watcher;
        private bool _demoMode;

        public void Configure(IPluginContext context)
        {
            _watcher = new TriLinkSerialWatcher();
            _demoMode = context.Environment.DemoMode;
            context.Defer(() => _watcher.Dispose());
            context.Provide<IDeviceDiscoveryService>(_watcher);
            context.Provide<IHardwareCommandService>(_watcher);
            context.Log("USB CDC 发现与受控轮询服务已注册。");
        }

        public void Start()
        {
            // Demo launch must not enumerate/open hardware behind the simulated UI.
            // An explicit later ResumePolling action still uses the real adapter.
            if (!_demoMode) _watcher.Start();
        }

        public void Stop()
        {
        }
    }
}
