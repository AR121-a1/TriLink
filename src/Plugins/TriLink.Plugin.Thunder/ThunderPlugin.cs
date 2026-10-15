using TriLink.Plugin;

namespace TriLink.Plugins.Thunder
{
    public sealed class ThunderPlugin : ITriLinkPlugin
    {
        public void Configure(IPluginContext context)
        {
            var links = context.GetRequired<IGameLinkFactory>();
            var lease = context.GetRequired<IModuleFeatureRegistry>().Register(new ModuleFeature
            {
                Id = "trilink.thunder.play",
                ModuleId = context.PluginId,
                Title = "雷霆战机",
                Scope = ModuleFeatureScope.DesktopDemo,
                Description = "PC 像素战机示例：单人练习或 2–6 人 UDP 合作；非板端任务包，不在 S3 内运行。"
            }, () => new ThunderView(links));
            context.Defer(lease.Dispose);
        }

        public void Start() { }
        public void Stop() { }
    }
}
