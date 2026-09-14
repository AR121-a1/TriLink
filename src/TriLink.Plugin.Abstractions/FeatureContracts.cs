using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TriLink.Plugin;

namespace TriLink.Core
{
    public interface IRoomSession
    {
        string RoomId { get; }

        RoomSnapshot Snapshot { get; }

        IReadOnlyDictionary<string, RoomReplica> Replicas { get; }

        bool IsMember(string nodeId);

        bool IsLeader(string nodeId);

        bool ReplicasAgree();
    }

    [PluginService(PluginServiceIds.RoomNetwork)]
    public interface IRoomNetwork
    {
        event EventHandler Changed;

        event EventHandler<NetworkNoticeEventArgs> Notice;

        IReadOnlyList<NodeInfo> Nodes { get; }

        void AddOrUpdateNode(NodeInfo node);

        void SetNodeOnline(string nodeId, bool online);

        void SetAllSimulatedOnline(bool online);

        OperationResult CreateRoom(string creatorNodeId, string roomName);

        OperationResult RequestJoin(string requesterNodeId, string targetNodeId);

        OperationResult Invite(string inviterNodeId, string targetNodeId);

        OperationResult AcceptInvitation(string targetNodeId, string invitationId);

        OperationResult DeclineInvitation(string targetNodeId, string invitationId);

        OperationResult ApproveJoin(string leaderNodeId, string requestId);

        OperationResult RejectJoin(string leaderNodeId, string requestId);

        OperationResult LeaveRoom(string nodeId);

        OperationResult Kick(string leaderNodeId, string targetNodeId);

        IRoomSession GetRoomForNode(string nodeId);

        IReadOnlyList<RoomInvitation> GetPendingInvitations(string targetNodeId);

        IReadOnlyList<NearbyNodeView> SearchNearby(string localNodeId);
    }

    public sealed class NetworkNoticeEventArgs : EventArgs
    {
        public NetworkNoticeEventArgs(string targetNodeId, string title, string message)
        {
            TargetNodeId = targetNodeId;
            Title = title;
            Message = message;
        }

        public string TargetNodeId { get; private set; }

        public string Title { get; private set; }

        public string Message { get; private set; }
    }

    public sealed class TriLinkDevice
    {
        public string PortName { get; set; }

        public string NodeId { get; set; }

        public string DisplayName { get; set; }

        public uint Capabilities { get; set; }

        public string PnpDeviceId { get; set; }
    }

    public sealed class TriLinkPeer
    {
        public string NodeId { get; set; }

        public string DisplayName { get; set; }

        public int Rssi { get; set; }

        public string RoomId { get; set; }

        public string RoomName { get; set; }

        public string LeaderNodeId { get; set; }
    }

    public sealed class TriLinkDeviceEventArgs : EventArgs
    {
        public TriLinkDeviceEventArgs(TriLinkDevice device)
        {
            Device = device;
        }

        public TriLinkDevice Device { get; private set; }
    }

    [PluginService(PluginServiceIds.DeviceDiscovery)]
    public interface IDeviceDiscoveryService : IDisposable
    {
        event EventHandler<TriLinkDeviceEventArgs> DeviceArrived;

        event EventHandler<TriLinkDeviceEventArgs> DeviceRemoved;

        event EventHandler<string> Status;

        event EventHandler PollingStateChanged;

        IReadOnlyList<TriLinkDevice> Devices { get; }

        bool IsPolling { get; }

        int ConsecutiveFailures { get; }

        int FailureLimit { get; }

        void Start();

        void ResumePolling();

        void PausePolling();

        void RequestScan();

        Task<IReadOnlyList<TriLinkPeer>> SearchNearbyAsync(string portName);
    }

    [PluginService(PluginServiceIds.SimulationControl)]
    public interface ISimulationControl
    {
        void SetAllOnline(bool online);
    }
}
