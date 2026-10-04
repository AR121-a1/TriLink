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
                Description = "像素战机小游戏：单人练习，或两人联机合作迎战。"
            }, () => new ThunderView(links));
            context.Defer(lease.Dispose);
        }

        public void Start() { }
        public void Stop() { }
    }
}
