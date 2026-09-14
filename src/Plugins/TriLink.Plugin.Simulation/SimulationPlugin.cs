using TriLink.Core;
using TriLink.Plugin;

namespace TriLink.Plugins.Simulation
{
    public sealed class SimulationPlugin : ITriLinkPlugin, ISimulationControl
    {
        private IRoomNetwork _network;
        private bool _allOnline;

        public void Configure(IPluginContext context)
        {
            _network = context.GetRequired<IRoomNetwork>();
            _allOnline = context.Environment.DemoMode;
            context.Provide<ISimulationControl>(this);
            context.Log("三节点模拟数据源已配置。");
        }

        public void Start()
        {
            _network.AddOrUpdateNode(
                new NodeInfo("10:00:00:00:00:01", "电脑 A / S3-A", true)
                {
                    IsOnline = true,
                    Rssi = -36,
                });
            _network.AddOrUpdateNode(
                new NodeInfo("20:00:00:00:00:02", "电脑 B / S3-B", true)
                {
                    IsOnline = _allOnline,
                    Rssi = -43,
                });
            _network.AddOrUpdateNode(
                new NodeInfo("30:00:00:00:00:03", "电脑 C / S3-C", true)
                {
                    IsOnline = _allOnline,
                    Rssi = -51,
                });
        }

        public void Stop()
        {
        }

        public void SetAllOnline(bool online)
        {
            _network.SetAllSimulatedOnline(online);
        }
    }
}
