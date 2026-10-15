using System.Threading.Tasks;
using TriLink.Plugin;

namespace TriLink.Core
{
    // Independent service version: Host API 1.0 alone does not imply this service exists.
    [PluginService("trilink.node-capabilities.v1")]
    public interface INodeCapabilityService
    {
        Task<NodeCapabilities> DescribeAsync(string portName);
    }

    public sealed class NodeCapabilities
    {
        public const uint DiscoveryCapability = 1U << 7;
        public const uint RgbModule = 1U;

        public NodeCapabilities(string nodeId, bool isLegacy, int contractVersion,
            string firmwareVersion, uint bootId, int maxRouteBody, int maxNeighbors,
            int maxRoomMembers, uint moduleMask)
        {
            NodeId = nodeId;
            IsLegacy = isLegacy;
            ContractVersion = contractVersion;
            FirmwareVersion = firmwareVersion;
            BootId = bootId;
            MaxRouteBody = maxRouteBody;
            MaxNeighbors = maxNeighbors;
            MaxRoomMembers = maxRoomMembers;
            ModuleMask = moduleMask;
        }

        public string NodeId { get; }
        // Legacy is a cached HELLO capability observation, not a fresh CAPS response.
        // Its unknown budget, version and boot fields are zero/null, never guessed.
        public bool IsLegacy { get; }
        public int ContractVersion { get; }
        public string FirmwareVersion { get; }
        public uint BootId { get; }
        public int MaxRouteBody { get; }
        public int MaxNeighbors { get; }
        public int MaxRoomMembers { get; }
        public uint ModuleMask { get; }
        public bool SupportsRgb { get { return (ModuleMask & RgbModule) != 0; } }
    }
}
