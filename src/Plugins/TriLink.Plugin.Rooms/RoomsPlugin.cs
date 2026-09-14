using TriLink.Core;
using TriLink.Plugin;

namespace TriLink.Plugins.Rooms
{
    public sealed class RoomsPlugin : ITriLinkPlugin
    {
        public void Configure(IPluginContext context)
        {
            context.Provide<IRoomNetwork>(new SimulatedNetwork());
            context.Log("Room 状态机服务已注册。");
        }

        public void Start()
        {
        }

        public void Stop()
        {
        }
    }
}
