using TriLink.Core;
using TriLink.Plugin;

namespace TriLink.Plugins.HardwareRoom
{
    public sealed class HardwareRoomPlugin : ITriLinkPlugin
    {
        public void Configure(IPluginContext context)
        {
            var commands = context.GetRequired<IHardwareCommandService>();
            var discovery = context.GetRequired<IDeviceDiscoveryService>();
            var sessions = new NodeSessionService(commands, discovery, context.GetRequired<INodeCapabilityService>());
            context.Provide<INodeSessionService>(sessions);
            context.Defer(sessions.Dispose);
            var lease = context.GetRequired<IModuleFeatureRegistry>().Register(new ModuleFeature {
                Id = "trilink.hardware-room.control", ModuleId = context.PluginId,
                Scope = ModuleFeatureScope.DeviceControl,
                Title = "真实 Room / RGB", Description = "通过 S3 管理真实成员副本与 RGB 执行回执；不使用模拟网络。" },
                () => new HardwareRoomView(sessions, discovery));
            context.Defer(lease.Dispose);
        }
        public void Start() { }
        public void Stop() { }
    }
}
