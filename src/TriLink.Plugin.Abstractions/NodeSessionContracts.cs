using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TriLink.Plugin;

namespace TriLink.Core
{
    public enum NodeOperationKind
    {
        CreateRoom, LeaveRoom, RetryJoin, JoinRoom, AcceptInvitation, ApproveJoin,
        RejectJoin, KickMember, InviteMember, EnableRgb, DisableRgb, LocalRgbPulse, MemberRgbPulse
    }
    public enum NodeSubmissionStatus { Queued, Duplicate, QueueFull, InvalidTarget, InvalidState, Unsupported, Disposed }
    public enum NodePollingState { NotStarted, Running, ManuallyPaused, FailurePaused, DeviceChanged }

    // Values returned by the session are copies. Consumers never own authoritative state.
    public sealed class NodeRoomState
    {
        private readonly byte[] _record;
        public NodeRoomState(byte[] record, string name, string leader, uint room, uint incarnation,
            uint revision, uint term, bool active, bool synchronized, IReadOnlyList<string> members)
        {
            _record = (byte[])record.Clone(); Name = name; Leader = leader; RoomId = room;
            Incarnation = incarnation; Revision = revision; Term = term; Active = active;
            Synchronized = synchronized; Members = new List<string>(members).AsReadOnly();
        }
        public byte[] EncodedRecord { get { return (byte[])_record.Clone(); } }
        public string Name { get; private set; }
        public string Leader { get; private set; }
        public uint RoomId { get; private set; }
        public uint Incarnation { get; private set; }
        public uint Revision { get; private set; }
        public uint Term { get; private set; }
        public bool Active { get; private set; }
        public bool Synchronized { get; private set; }
        public IReadOnlyList<string> Members { get; private set; }
    }
    public sealed class NodeOperationSnapshot
    {
        public string RequestId { get; set; }
        public string Description { get; set; }
        public string Target { get; set; }
        public bool WriteAttempted { get; set; }
    }
    public sealed class NodeSessionSnapshot
    {
        public TriLinkDevice Device { get; set; }
        public int Generation { get; set; }
        public NodeRoomState Room { get; set; }
        public IReadOnlyList<NodeRoomState> NearbyRooms { get; set; }
        public IReadOnlyList<NodeRoomState> Requests { get; set; }
        public IReadOnlyList<NodeRoomState> Invitations { get; set; }
        public IReadOnlyDictionary<string, int> PeerRssi { get; set; }
        public IReadOnlyList<NodeOperationSnapshot> QueuedOperations { get; set; }
        public NodeOperationSnapshot ExecutingOperation { get; set; }
        public bool Busy { get; set; }
        public bool BackgroundReading { get; set; }
        public bool RgbPending { get; set; }
        public bool RgbTracking { get; set; }
        public string RgbOperationId { get; set; }
        public int ConsecutiveFailures { get; set; }
        public string InvalidReason { get; set; }
        public NodePollingState PollingState { get; set; }
        public IReadOnlyList<string> Log { get; set; }
        public NodeCapabilities Capabilities { get; set; }
    }
    public sealed class NodeOperationIntent
    {
        public NodeOperationKind Kind { get; set; }
        public string Description { get; set; }
        public string Name { get; set; }
        public string Target { get; set; }
        public int Generation { get; set; }
        public NodeRoomState Basis { get; set; }
        public NodeRoomState Record { get; set; }
        public bool LocalPermissionConfirmed { get; set; }
    }
    public sealed class NodeSubmissionResult
    {
        public NodeSubmissionStatus Status { get; set; }
        public string RequestId { get; set; }
        public string Message { get; set; }
    }
    [PluginService("trilink.node-session.v1")]
    public interface INodeSessionService
    {
        event EventHandler DevicesChanged;
        // Bind never changes an already-subscribed page's target. Stage A has one
        // active node binding; a different simultaneous target is explicitly refused.
        INodeSessionSubscription Bind(string nodeId, string portName);
        void PausePolling();
        void ResumePolling();
    }
    public interface INodeSessionSubscription : IDisposable
    {
        event EventHandler Changed;
        NodeSessionSnapshot Snapshot { get; }
        Task RefreshAsync();
        NodeSubmissionResult Submit(NodeOperationIntent intent);
        void SetVisible(bool visible, bool autoStart);
        void CancelPending();
    }
}
