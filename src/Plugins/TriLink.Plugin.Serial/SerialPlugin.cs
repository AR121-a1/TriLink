using TriLink.Core;
using TriLink.MinClient.Serial;
using TriLink.Plugin;

namespace TriLink.Plugins.Serial
{
    public sealed class SerialPlugin : ITriLinkPlugin
    {
        private TriLinkSerialWatcher _watcher;

        public void Configure(IPluginContext context)
        {
            _watcher = new TriLinkSerialWatcher();
            context.Defer(() => _watcher.Dispose());
            context.Provide<IDeviceDiscoveryService>(_watcher);
            context.Log("USB CDC 发现与受控轮询服务已注册。");
        }

        public void Start()
        {
            _watcher.Start();
        }

        public void Stop()
        {
        }
    }
}
