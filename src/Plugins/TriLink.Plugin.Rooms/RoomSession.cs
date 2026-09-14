using System;
using System.Collections.Generic;
using System.Linq;

namespace TriLink.Core
{
    public sealed class RoomSession : IRoomSession
    {
        private readonly List<RoomEvent> _events = new List<RoomEvent>();
        private readonly Dictionary<string, RoomReplica> _replicas =
            new Dictionary<string, RoomReplica>(StringComparer.Ordinal);
        private readonly RoomSnapshot _state;
        private long _nextJoinOrder;

        private RoomSession(
            string roomId,
            string roomName,
            string creatorNodeId,
            string creatorName)
        {
            _state = new RoomSnapshot(roomId, roomName)
            {
                Revision = 0,
                Term = 1,
                Lifecycle = RoomLifecycle.WaitingForFirstPeer,
                LeaderNodeId = creatorNodeId,
            };
            _nextJoinOrder = 1;
            _state.Members.Add(new RoomMember(creatorNodeId, creatorName, _nextJoinOrder));
            Commit("ROOM_CREATED", creatorNodeId, creatorNodeId);
        }

        public string RoomId
        {
            get { return _state.RoomId; }
        }

        public RoomSnapshot Snapshot
        {
            get { return _state.Clone(); }
        }

        public IReadOnlyDictionary<string, RoomReplica> Replicas
        {
            get { return _replicas; }
        }

        public static RoomSession Create(
            string creatorNodeId,
            string creatorName,
            string roomName,
            string fixedRoomId = null)
        {
            if (string.IsNullOrWhiteSpace(creatorNodeId))
            {
                throw new ArgumentException("Creator node id is required.", nameof(creatorNodeId));
            }

            var roomId = string.IsNullOrWhiteSpace(fixedRoomId)
                ? "R-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant()
                : fixedRoomId;
            var safeName = string.IsNullOrWhiteSpace(roomName)
                ? creatorName + " 的房间"
                : roomName.Trim();
            return new RoomSession(roomId, safeName, creatorNodeId, creatorName);
        }

        public bool IsMember(string nodeId)
        {
            return _state.Members.Any(
                member => string.Equals(member.NodeId, nodeId, StringComparison.Ordinal));
        }

        public bool IsLeader(string nodeId)
        {
            return _state.Lifecycle != RoomLifecycle.Dissolved
                && string.Equals(_state.LeaderNodeId, nodeId, StringComparison.Ordinal);
        }

        public OperationResult RequestJoin(
            string candidateNodeId,
            string candidateName,
            string requestedByNodeId)
        {
            if (_state.Lifecycle == RoomLifecycle.Dissolved)
            {
                return OperationResult.Fail("房间已经解散。");
            }

            if (string.IsNullOrWhiteSpace(candidateNodeId))
            {
                return OperationResult.Fail("申请节点无效。");
            }

            if (IsMember(candidateNodeId))
            {
                return OperationResult.Fail("该节点已经在房间中。");
            }

            if (_state.PendingJoinRequests.Any(
                request => string.Equals(
                    request.CandidateNodeId,
                    candidateNodeId,
                    StringComparison.Ordinal)))
            {
                return OperationResult.Fail("该节点已有待处理的加入申请。");
            }

            var request = new JoinRequest(
                "J-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant(),
                candidateNodeId,
                candidateName,
                requestedByNodeId,
                DateTime.UtcNow);
            _state.PendingJoinRequests.Add(request);
            Commit("JOIN_REQUESTED", candidateNodeId, _state.LeaderNodeId);

            var result = OperationResult.Ok("加入申请已发送，等待 leader 同意。");
            result.RoomId = RoomId;
            result.RequestId = request.RequestId;
            result.Snapshot = Snapshot;
            return result;
        }

        public OperationResult ApproveJoin(string actorNodeId, string requestId)
        {
            if (!IsLeader(actorNodeId))
            {
                return OperationResult.Fail("只有当前 leader 可以同意加入。");
            }

            var request = FindRequest(requestId);
            if (request == null)
            {
                return OperationResult.Fail("加入申请不存在或已经处理。");
            }

            _state.PendingJoinRequests.Remove(request);
            _nextJoinOrder++;
            _state.Members.Add(
                new RoomMember(request.CandidateNodeId, request.CandidateName, _nextJoinOrder));
            if (_state.Members.Count >= 2)
            {
                _state.Lifecycle = RoomLifecycle.Formed;
            }

            Commit("JOIN_APPROVED", actorNodeId, request.CandidateNodeId);
            var result = OperationResult.Ok(request.CandidateName + " 已加入房间。");
            result.RoomId = RoomId;
            result.Snapshot = Snapshot;
            return result;
        }

        public OperationResult RejectJoin(string actorNodeId, string requestId)
        {
            if (!IsLeader(actorNodeId))
            {
                return OperationResult.Fail("只有当前 leader 可以拒绝加入。");
            }

            var request = FindRequest(requestId);
            if (request == null)
            {
                return OperationResult.Fail("加入申请不存在或已经处理。");
            }

            _state.PendingJoinRequests.Remove(request);
            Commit("JOIN_REJECTED", actorNodeId, request.CandidateNodeId);
            var result = OperationResult.Ok("已拒绝 " + request.CandidateName + " 的加入申请。");
            result.RoomId = RoomId;
            result.Snapshot = Snapshot;
            return result;
        }

        public OperationResult Leave(string nodeId)
        {
            if (!IsMember(nodeId))
            {
                return OperationResult.Fail("该节点不在房间中。");
            }

            return RemoveMember(nodeId, nodeId, "MEMBER_LEFT", false);
        }

        public OperationResult Kick(string actorNodeId, string targetNodeId)
        {
            if (!IsLeader(actorNodeId))
            {
                return OperationResult.Fail("只有当前 leader 可以踢出成员。");
            }

            if (string.Equals(actorNodeId, targetNodeId, StringComparison.Ordinal))
            {
                return OperationResult.Fail("leader 不能踢出自己，请使用退出房间。");
            }

            if (!IsMember(targetNodeId))
            {
                return OperationResult.Fail("目标节点不在房间中。");
            }

            return RemoveMember(actorNodeId, targetNodeId, "MEMBER_KICKED", true);
        }

        public bool ReplicasAgree()
        {
            if (_state.Lifecycle == RoomLifecycle.Dissolved || _replicas.Count == 0)
            {
                return true;
            }

            return _replicas.Values.All(
                replica => replica.Snapshot != null
                    && replica.Snapshot.HasEquivalentState(_state)
                    && replica.EventLog.Count == _events.Count);
        }

        private OperationResult RemoveMember(
            string actorNodeId,
            string targetNodeId,
            string eventKind,
            bool kicked)
        {
            var target = _state.Members.First(
                member => string.Equals(member.NodeId, targetNodeId, StringComparison.Ordinal));
            var targetWasLeader = string.Equals(
                _state.LeaderNodeId,
                targetNodeId,
                StringComparison.Ordinal);
            _state.Members.Remove(target);
            _replicas.Remove(targetNodeId);

            var mustDissolve = _state.Members.Count == 0
                || (_state.Lifecycle == RoomLifecycle.Formed && _state.Members.Count == 1);
            if (mustDissolve)
            {
                _state.Lifecycle = RoomLifecycle.Dissolved;
                _state.LeaderNodeId = null;
            }
            else if (targetWasLeader)
            {
                _state.LeaderNodeId = _state.Members
                    .OrderBy(member => member.JoinOrder)
                    .First()
                    .NodeId;
                _state.Term++;
            }

            _state.PendingJoinRequests.RemoveAll(
                request => string.Equals(
                    request.CandidateNodeId,
                    targetNodeId,
                    StringComparison.Ordinal));
            Commit(eventKind, actorNodeId, targetNodeId);

            var action = kicked ? "已被 leader 踢出。" : "已退出房间。";
            var result = OperationResult.Ok(target.DisplayName + action);
            result.RoomId = RoomId;
            result.RoomDissolved = mustDissolve;
            result.Snapshot = Snapshot;
            if (mustDissolve)
            {
                result = OperationResult.Ok(target.DisplayName + action + " 房间只剩一人，已解散。");
                result.RoomId = RoomId;
                result.RoomDissolved = true;
                result.Snapshot = Snapshot;
            }

            return result;
        }

        private JoinRequest FindRequest(string requestId)
        {
            return _state.PendingJoinRequests.FirstOrDefault(
                request => string.Equals(request.RequestId, requestId, StringComparison.Ordinal));
        }

        private void Commit(string eventKind, string actorNodeId, string targetNodeId)
        {
            _state.Revision++;
            _events.Add(
                new RoomEvent(
                    _state.Revision,
                    _state.Term,
                    eventKind,
                    actorNodeId,
                    targetNodeId,
                    DateTime.UtcNow));

            foreach (var member in _state.Members)
            {
                RoomReplica replica;
                if (!_replicas.TryGetValue(member.NodeId, out replica))
                {
                    replica = new RoomReplica(member.NodeId);
                    _replicas.Add(member.NodeId, replica);
                }

                replica.Replace(_state, _events);
            }
        }
    }
}
