using System;
using System.Collections.Generic;
using System.Linq;

namespace TriLink.Core
{
    public enum RoomLifecycle
    {
        WaitingForFirstPeer = 0,
        Formed = 1,
        Dissolved = 2,
    }

    public enum InvitationStatus
    {
        Pending = 0,
        Accepted = 1,
        Declined = 2,
    }

    public sealed class NodeInfo
    {
        public NodeInfo(string nodeId, string displayName, bool simulated)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node id is required.", nameof(nodeId));
            }

            NodeId = nodeId;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? nodeId : displayName;
            IsSimulated = simulated;
            IsOnline = true;
            Transport = simulated ? "SIM" : "USB CDC";
        }

        public string NodeId { get; private set; }

        public string DisplayName { get; set; }

        public bool IsOnline { get; set; }

        public bool IsSimulated { get; private set; }

        public string Transport { get; set; }

        public string PortName { get; set; }

        public int Rssi { get; set; }
    }

    public sealed class RoomMember
    {
        public RoomMember(string nodeId, string displayName, long joinOrder)
        {
            NodeId = nodeId;
            DisplayName = displayName;
            JoinOrder = joinOrder;
        }

        public string NodeId { get; private set; }

        public string DisplayName { get; private set; }

        public long JoinOrder { get; private set; }

        public RoomMember Clone()
        {
            return new RoomMember(NodeId, DisplayName, JoinOrder);
        }
    }

    public sealed class JoinRequest
    {
        public JoinRequest(
            string requestId,
            string candidateNodeId,
            string candidateName,
            string requestedByNodeId,
            DateTime createdUtc)
        {
            RequestId = requestId;
            CandidateNodeId = candidateNodeId;
            CandidateName = candidateName;
            RequestedByNodeId = requestedByNodeId;
            CreatedUtc = createdUtc;
        }

        public string RequestId { get; private set; }

        public string CandidateNodeId { get; private set; }

        public string CandidateName { get; private set; }

        public string RequestedByNodeId { get; private set; }

        public DateTime CreatedUtc { get; private set; }

        public JoinRequest Clone()
        {
            return new JoinRequest(
                RequestId,
                CandidateNodeId,
                CandidateName,
                RequestedByNodeId,
                CreatedUtc);
        }
    }

    public sealed class RoomEvent
    {
        public RoomEvent(
            long revision,
            long term,
            string kind,
            string actorNodeId,
            string targetNodeId,
            DateTime createdUtc)
        {
            Revision = revision;
            Term = term;
            Kind = kind;
            ActorNodeId = actorNodeId;
            TargetNodeId = targetNodeId;
            CreatedUtc = createdUtc;
        }

        public long Revision { get; private set; }

        public long Term { get; private set; }

        public string Kind { get; private set; }

        public string ActorNodeId { get; private set; }

        public string TargetNodeId { get; private set; }

        public DateTime CreatedUtc { get; private set; }

        public RoomEvent Clone()
        {
            return new RoomEvent(
                Revision,
                Term,
                Kind,
                ActorNodeId,
                TargetNodeId,
                CreatedUtc);
        }
    }

    public sealed class RoomSnapshot
    {
        public RoomSnapshot(string roomId, string roomName)
        {
            RoomId = roomId;
            RoomName = roomName;
            Members = new List<RoomMember>();
            PendingJoinRequests = new List<JoinRequest>();
        }

        public string RoomId { get; set; }

        public string RoomName { get; set; }

        public long Revision { get; set; }

        public long Term { get; set; }

        public RoomLifecycle Lifecycle { get; set; }

        public string LeaderNodeId { get; set; }

        public List<RoomMember> Members { get; private set; }

        public List<JoinRequest> PendingJoinRequests { get; private set; }

        public RoomSnapshot Clone()
        {
            var clone = new RoomSnapshot(RoomId, RoomName)
            {
                Revision = Revision,
                Term = Term,
                Lifecycle = Lifecycle,
                LeaderNodeId = LeaderNodeId,
            };

            clone.Members.AddRange(Members.Select(member => member.Clone()));
            clone.PendingJoinRequests.AddRange(
                PendingJoinRequests.Select(request => request.Clone()));
            return clone;
        }

        public bool HasEquivalentState(RoomSnapshot other)
        {
            if (other == null
                || !string.Equals(RoomId, other.RoomId, StringComparison.Ordinal)
                || !string.Equals(RoomName, other.RoomName, StringComparison.Ordinal)
                || Revision != other.Revision
                || Term != other.Term
                || Lifecycle != other.Lifecycle
                || !string.Equals(LeaderNodeId, other.LeaderNodeId, StringComparison.Ordinal)
                || Members.Count != other.Members.Count
                || PendingJoinRequests.Count != other.PendingJoinRequests.Count)
            {
                return false;
            }

            for (var index = 0; index < Members.Count; index++)
            {
                var left = Members[index];
                var right = other.Members[index];
                if (!string.Equals(left.NodeId, right.NodeId, StringComparison.Ordinal)
                    || !string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal)
                    || left.JoinOrder != right.JoinOrder)
                {
                    return false;
                }
            }

            for (var index = 0; index < PendingJoinRequests.Count; index++)
            {
                var left = PendingJoinRequests[index];
                var right = other.PendingJoinRequests[index];
                if (!string.Equals(left.RequestId, right.RequestId, StringComparison.Ordinal)
                    || !string.Equals(
                        left.CandidateNodeId,
                        right.CandidateNodeId,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        left.RequestedByNodeId,
                        right.RequestedByNodeId,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }

    public sealed class RoomReplica
    {
        private readonly List<RoomEvent> _eventLog = new List<RoomEvent>();

        public RoomReplica(string ownerNodeId)
        {
            OwnerNodeId = ownerNodeId;
        }

        public string OwnerNodeId { get; private set; }

        public RoomSnapshot Snapshot { get; private set; }

        public IReadOnlyList<RoomEvent> EventLog
        {
            get { return _eventLog.AsReadOnly(); }
        }

        internal void Replace(RoomSnapshot snapshot, IEnumerable<RoomEvent> events)
        {
            Snapshot = snapshot.Clone();
            _eventLog.Clear();
            _eventLog.AddRange(events.Select(item => item.Clone()));
        }
    }

    public sealed class RoomInvitation
    {
        public RoomInvitation(
            string invitationId,
            string roomId,
            string roomName,
            string fromNodeId,
            string fromDisplayName,
            string targetNodeId)
        {
            InvitationId = invitationId;
            RoomId = roomId;
            RoomName = roomName;
            FromNodeId = fromNodeId;
            FromDisplayName = fromDisplayName;
            TargetNodeId = targetNodeId;
            Status = InvitationStatus.Pending;
            CreatedUtc = DateTime.UtcNow;
        }

        public string InvitationId { get; private set; }

        public string RoomId { get; private set; }

        public string RoomName { get; private set; }

        public string FromNodeId { get; private set; }

        public string FromDisplayName { get; private set; }

        public string TargetNodeId { get; private set; }

        public InvitationStatus Status { get; set; }

        public DateTime CreatedUtc { get; private set; }
    }

    public sealed class NearbyNodeView
    {
        public NodeInfo Node { get; set; }

        public string RoomId { get; set; }

        public string RoomName { get; set; }

        public string LeaderNodeId { get; set; }

        public bool CanRequestJoin { get; set; }

        public bool CanInvite { get; set; }
    }

    public sealed class OperationResult
    {
        private OperationResult(bool success, string message)
        {
            Success = success;
            Message = message;
        }

        public bool Success { get; private set; }

        public string Message { get; private set; }

        public bool RoomDissolved { get; set; }

        public string RoomId { get; set; }

        public string RequestId { get; set; }

        public RoomSnapshot Snapshot { get; set; }

        public static OperationResult Ok(string message)
        {
            return new OperationResult(true, message);
        }

        public static OperationResult Fail(string message)
        {
            return new OperationResult(false, message);
        }
    }
}
