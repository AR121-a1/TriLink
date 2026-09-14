using System;
using System.Collections.Generic;
using System.Linq;

namespace TriLink.Core
{
    public sealed class SimulatedNetwork : IRoomNetwork
    {
        private readonly Dictionary<string, NodeInfo> _nodes =
            new Dictionary<string, NodeInfo>(StringComparer.Ordinal);
        private readonly Dictionary<string, RoomSession> _rooms =
            new Dictionary<string, RoomSession>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _nodeRooms =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<RoomInvitation> _invitations =
            new List<RoomInvitation>();

        public event EventHandler Changed;

        public event EventHandler<NetworkNoticeEventArgs> Notice;

        public IReadOnlyList<NodeInfo> Nodes
        {
            get
            {
                return _nodes.Values
                    .OrderBy(node => node.DisplayName, StringComparer.CurrentCulture)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public static SimulatedNetwork CreateThreeNodeDemo(bool allOnline)
        {
            var network = new SimulatedNetwork();
            network.AddOrUpdateNode(
                new NodeInfo("10:00:00:00:00:01", "电脑 A / S3-A", true)
                {
                    IsOnline = true,
                    Rssi = -36,
                });
            network.AddOrUpdateNode(
                new NodeInfo("20:00:00:00:00:02", "电脑 B / S3-B", true)
                {
                    IsOnline = allOnline,
                    Rssi = -43,
                });
            network.AddOrUpdateNode(
                new NodeInfo("30:00:00:00:00:03", "电脑 C / S3-C", true)
                {
                    IsOnline = allOnline,
                    Rssi = -51,
                });
            return network;
        }

        public void AddOrUpdateNode(NodeInfo node)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            NodeInfo existing;
            if (_nodes.TryGetValue(node.NodeId, out existing))
            {
                existing.DisplayName = node.DisplayName;
                existing.IsOnline = node.IsOnline;
                existing.Transport = node.Transport;
                existing.PortName = node.PortName;
                existing.Rssi = node.Rssi;
            }
            else
            {
                _nodes.Add(node.NodeId, node);
            }

            RaiseChanged();
        }

        public void SetNodeOnline(string nodeId, bool online)
        {
            var node = RequireNode(nodeId);
            node.IsOnline = online;
            RaiseChanged();
        }

        public void SetAllSimulatedOnline(bool online)
        {
            foreach (var node in _nodes.Values.Where(item => item.IsSimulated))
            {
                node.IsOnline = online;
            }

            RaiseChanged();
        }

        public OperationResult CreateRoom(string creatorNodeId, string roomName)
        {
            var creator = RequireOnlineNode(creatorNodeId);
            if (GetRoomForNode(creatorNodeId) != null)
            {
                return OperationResult.Fail("当前节点已经在房间中。");
            }

            var room = RoomSession.Create(
                creatorNodeId,
                creator.DisplayName,
                roomName);
            _rooms.Add(room.RoomId, room);
            _nodeRooms[creatorNodeId] = room.RoomId;
            RaiseChanged();
            var result = OperationResult.Ok("已创建等待房间；首次成员加入后成为正式房间。");
            result.RoomId = room.RoomId;
            result.Snapshot = room.Snapshot;
            return result;
        }

        public OperationResult RequestJoin(string requesterNodeId, string targetNodeId)
        {
            var requester = RequireOnlineNode(requesterNodeId);
            RequireOnlineNode(targetNodeId);

            if (GetRoomForNode(requesterNodeId) != null)
            {
                return OperationResult.Fail("请先退出当前房间，再申请加入其他房间。");
            }

            var targetRoom = GetRoomForNode(targetNodeId);
            if (targetRoom == null)
            {
                return OperationResult.Fail("目标节点没有可加入的房间。");
            }

            var result = targetRoom.RequestJoin(
                requesterNodeId,
                requester.DisplayName,
                requesterNodeId);
            if (result.Success)
            {
                var leader = targetRoom.Snapshot.LeaderNodeId;
                RaiseNotice(
                    leader,
                    "新的加入申请",
                    requester.DisplayName + " 请求加入 " + targetRoom.Snapshot.RoomName);
                RaiseChanged();
            }

            return result;
        }

        public OperationResult Invite(string inviterNodeId, string targetNodeId)
        {
            var inviter = RequireOnlineNode(inviterNodeId);
            var target = RequireOnlineNode(targetNodeId);
            var room = GetRoomForNode(inviterNodeId);
            if (room == null)
            {
                return OperationResult.Fail("请先创建或加入房间，再邀请附近设备。");
            }

            if (GetRoomForNode(targetNodeId) != null)
            {
                return OperationResult.Fail("目标节点已经在一个房间中。");
            }

            var duplicate = _invitations.Any(
                invitation => invitation.Status == InvitationStatus.Pending
                    && string.Equals(
                        invitation.RoomId,
                        room.RoomId,
                        StringComparison.Ordinal)
                    && string.Equals(
                        invitation.TargetNodeId,
                        targetNodeId,
                        StringComparison.Ordinal));
            if (duplicate)
            {
                return OperationResult.Fail("该房间已经向目标节点发出邀请。");
            }

            var invitation = new RoomInvitation(
                "I-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant(),
                room.RoomId,
                room.Snapshot.RoomName,
                inviterNodeId,
                inviter.DisplayName,
                targetNodeId);
            _invitations.Add(invitation);
            RaiseNotice(
                targetNodeId,
                "收到房间邀请",
                inviter.DisplayName + " 邀请你加入 " + room.Snapshot.RoomName);
            RaiseChanged();
            var result = OperationResult.Ok("已向 " + target.DisplayName + " 发送邀请。");
            result.RoomId = room.RoomId;
            result.Snapshot = room.Snapshot;
            return result;
        }

        public OperationResult AcceptInvitation(string targetNodeId, string invitationId)
        {
            var target = RequireOnlineNode(targetNodeId);
            var invitation = FindPendingInvitation(targetNodeId, invitationId);
            if (invitation == null)
            {
                return OperationResult.Fail("邀请不存在或已经处理。");
            }

            if (GetRoomForNode(targetNodeId) != null)
            {
                return OperationResult.Fail("当前节点已经在房间中。");
            }

            RoomSession room;
            if (!_rooms.TryGetValue(invitation.RoomId, out room))
            {
                invitation.Status = InvitationStatus.Declined;
                RaiseChanged();
                return OperationResult.Fail("邀请对应的房间已不存在。");
            }

            var result = room.RequestJoin(
                targetNodeId,
                target.DisplayName,
                invitation.FromNodeId);
            if (!result.Success)
            {
                return result;
            }

            invitation.Status = InvitationStatus.Accepted;
            RaiseNotice(
                room.Snapshot.LeaderNodeId,
                "邀请已接受",
                target.DisplayName + " 已接受邀请，等待你同意进入。");
            RaiseChanged();
            var accepted = OperationResult.Ok("已接受邀请，等待当前 leader 同意。");
            accepted.RoomId = room.RoomId;
            accepted.RequestId = result.RequestId;
            accepted.Snapshot = room.Snapshot;
            return accepted;
        }

        public OperationResult DeclineInvitation(string targetNodeId, string invitationId)
        {
            RequireNode(targetNodeId);
            var invitation = FindPendingInvitation(targetNodeId, invitationId);
            if (invitation == null)
            {
                return OperationResult.Fail("邀请不存在或已经处理。");
            }

            invitation.Status = InvitationStatus.Declined;
            RaiseNotice(
                invitation.FromNodeId,
                "邀请被拒绝",
                RequireNode(targetNodeId).DisplayName + " 拒绝了房间邀请。");
            RaiseChanged();
            return OperationResult.Ok("已拒绝邀请。");
        }

        public OperationResult ApproveJoin(string leaderNodeId, string requestId)
        {
            var room = GetRoomForNode(leaderNodeId);
            if (room == null)
            {
                return OperationResult.Fail("当前节点不在房间中。");
            }

            var request = room.Snapshot.PendingJoinRequests.FirstOrDefault(
                item => string.Equals(item.RequestId, requestId, StringComparison.Ordinal));
            if (request == null)
            {
                return OperationResult.Fail("加入申请不存在或已经处理。");
            }

            if (GetRoomForNode(request.CandidateNodeId) != null)
            {
                return OperationResult.Fail("申请节点已经加入了其他房间。");
            }

            var result = room.ApproveJoin(leaderNodeId, requestId);
            if (result.Success)
            {
                _nodeRooms[request.CandidateNodeId] = room.RoomId;
                RaiseNotice(
                    request.CandidateNodeId,
                    "加入成功",
                    "已加入 " + room.Snapshot.RoomName);
                RaiseChanged();
            }

            return result;
        }

        public OperationResult RejectJoin(string leaderNodeId, string requestId)
        {
            var room = GetRoomForNode(leaderNodeId);
            if (room == null)
            {
                return OperationResult.Fail("当前节点不在房间中。");
            }

            var request = room.Snapshot.PendingJoinRequests.FirstOrDefault(
                item => string.Equals(item.RequestId, requestId, StringComparison.Ordinal));
            if (request == null)
            {
                return OperationResult.Fail("加入申请不存在或已经处理。");
            }

            var result = room.RejectJoin(leaderNodeId, requestId);
            if (result.Success)
            {
                RaiseNotice(
                    request.CandidateNodeId,
                    "加入申请被拒绝",
                    room.Snapshot.RoomName + " 的 leader 拒绝了申请。");
                RaiseChanged();
            }

            return result;
        }

        public OperationResult LeaveRoom(string nodeId)
        {
            var room = GetRoomForNode(nodeId);
            if (room == null)
            {
                return OperationResult.Fail("当前节点不在房间中。");
            }

            var formerMembers = room.Snapshot.Members.Select(member => member.NodeId).ToList();
            var result = room.Leave(nodeId);
            if (!result.Success)
            {
                return result;
            }

            _nodeRooms.Remove(nodeId);
            CompleteRoomMutation(room, formerMembers, result);
            RaiseChanged();
            return result;
        }

        public OperationResult Kick(string leaderNodeId, string targetNodeId)
        {
            var room = GetRoomForNode(leaderNodeId);
            if (room == null)
            {
                return OperationResult.Fail("当前节点不在房间中。");
            }

            var formerMembers = room.Snapshot.Members.Select(member => member.NodeId).ToList();
            var result = room.Kick(leaderNodeId, targetNodeId);
            if (!result.Success)
            {
                return result;
            }

            _nodeRooms.Remove(targetNodeId);
            RaiseNotice(targetNodeId, "已被移出房间", result.Message);
            CompleteRoomMutation(room, formerMembers, result);
            RaiseChanged();
            return result;
        }

        public RoomSession GetRoomForNode(string nodeId)
        {
            string roomId;
            RoomSession room;
            return _nodeRooms.TryGetValue(nodeId, out roomId)
                && _rooms.TryGetValue(roomId, out room)
                ? room
                : null;
        }

        IRoomSession IRoomNetwork.GetRoomForNode(string nodeId)
        {
            return GetRoomForNode(nodeId);
        }

        public IReadOnlyList<RoomInvitation> GetPendingInvitations(string targetNodeId)
        {
            return _invitations
                .Where(invitation => invitation.Status == InvitationStatus.Pending
                    && string.Equals(
                        invitation.TargetNodeId,
                        targetNodeId,
                        StringComparison.Ordinal))
                .OrderBy(invitation => invitation.CreatedUtc)
                .ToList()
                .AsReadOnly();
        }

        public IReadOnlyList<NearbyNodeView> SearchNearby(string localNodeId)
        {
            RequireNode(localNodeId);
            var localRoom = GetRoomForNode(localNodeId);
            var results = new List<NearbyNodeView>();

            foreach (var node in _nodes.Values
                .Where(item => item.IsOnline
                    && !string.Equals(item.NodeId, localNodeId, StringComparison.Ordinal))
                .OrderByDescending(item => item.Rssi))
            {
                var targetRoom = GetRoomForNode(node.NodeId);
                var sameRoom = localRoom != null
                    && targetRoom != null
                    && string.Equals(localRoom.RoomId, targetRoom.RoomId, StringComparison.Ordinal);
                var hasPendingInvitation = localRoom != null
                    && _invitations.Any(invitation =>
                        invitation.Status == InvitationStatus.Pending
                        && string.Equals(
                            invitation.RoomId,
                            localRoom.RoomId,
                            StringComparison.Ordinal)
                        && string.Equals(
                            invitation.TargetNodeId,
                            node.NodeId,
                            StringComparison.Ordinal));

                results.Add(
                    new NearbyNodeView
                    {
                        Node = node,
                        RoomId = targetRoom == null ? null : targetRoom.RoomId,
                        RoomName = targetRoom == null ? null : targetRoom.Snapshot.RoomName,
                        LeaderNodeId = targetRoom == null
                            ? null
                            : targetRoom.Snapshot.LeaderNodeId,
                        CanRequestJoin = localRoom == null && targetRoom != null,
                        CanInvite = localRoom != null
                            && targetRoom == null
                            && !sameRoom
                            && !hasPendingInvitation,
                    });
            }

            return results.AsReadOnly();
        }

        private void CompleteRoomMutation(
            RoomSession room,
            IEnumerable<string> formerMembers,
            OperationResult result)
        {
            if (!result.RoomDissolved)
            {
                return;
            }

            foreach (var memberNodeId in formerMembers)
            {
                _nodeRooms.Remove(memberNodeId);
                RaiseNotice(memberNodeId, "房间已解散", "成员数降为一，房间状态已清除。");
            }

            _rooms.Remove(room.RoomId);
            _invitations.RemoveAll(
                invitation => string.Equals(
                    invitation.RoomId,
                    room.RoomId,
                    StringComparison.Ordinal));
        }

        private RoomInvitation FindPendingInvitation(string targetNodeId, string invitationId)
        {
            return _invitations.FirstOrDefault(
                invitation => invitation.Status == InvitationStatus.Pending
                    && string.Equals(
                        invitation.TargetNodeId,
                        targetNodeId,
                        StringComparison.Ordinal)
                    && string.Equals(
                        invitation.InvitationId,
                        invitationId,
                        StringComparison.Ordinal));
        }

        private NodeInfo RequireOnlineNode(string nodeId)
        {
            var node = RequireNode(nodeId);
            if (!node.IsOnline)
            {
                throw new InvalidOperationException("节点当前离线：" + node.DisplayName);
            }

            return node;
        }

        private NodeInfo RequireNode(string nodeId)
        {
            NodeInfo node;
            if (string.IsNullOrWhiteSpace(nodeId) || !_nodes.TryGetValue(nodeId, out node))
            {
                throw new ArgumentException("Unknown node: " + nodeId, nameof(nodeId));
            }

            return node;
        }

        private void RaiseChanged()
        {
            var handler = Changed;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void RaiseNotice(string targetNodeId, string title, string message)
        {
            var handler = Notice;
            if (handler != null)
            {
                handler(this, new NetworkNoticeEventArgs(targetNodeId, title, message));
            }
        }
    }
}
