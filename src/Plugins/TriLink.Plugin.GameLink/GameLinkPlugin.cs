using System;
using TriLink.Plugin;

namespace TriLink.Plugins.GameLink
{
    public sealed class GameLinkPlugin : ITriLinkPlugin
    {
        public void Configure(IPluginContext context)
        {
            if (context == null) { throw new ArgumentNullException(nameof(context)); }
            context.Provide<IGameLinkFactory>(new UdpGameLinkFactory());
            context.Log("IPv4 局域网游戏传输已注册；仅在游戏会话打开后绑定端口。");
        }

        public void Start() { }

        public void Stop() { }
    }
}
