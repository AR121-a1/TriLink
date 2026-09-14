using System;
using System.IO;
using System.Linq;
using TriLink.Core;
using TriLink.Plugin;
using TriLink.PluginHost;

namespace TriLink.Core.Tests
{
    internal static class Program
    {
        private const string NodeA = "10:00:00:00:00:01";
        private const string NodeB = "20:00:00:00:00:02";
        private const string NodeC = "30:00:00:00:00:03";

        private static int _checks;
        private static int _failures;

        private static int Main()
        {
            TestWaitingRoomAndLeaderApproval();
            TestInvitationFromNonLeaderAndSuccession();
            TestDirectJoinThroughAnyRoomMember();
            TestReplicaIsolation();
            TestUsbRecognitionProtocol();
            TestPollingFailureLimiter();
            TestPluginDependencyGraph();
            TestPluginServices();
            TestPluginProfile();

            Console.WriteLine(
                "TriLink.Core tests: checks={0} failures={1}",
                _checks,
                _failures);
            return _failures == 0 ? 0 : 1;
        }

        private static void TestWaitingRoomAndLeaderApproval()
        {
            var network = SimulatedNetwork.CreateThreeNodeDemo(true);
            var created = network.CreateRoom(NodeA, "A Room");
            Check(created.Success, "creator can create room");

            var room = network.GetRoomForNode(NodeA);
            Check(room != null, "waiting room exists with one creator");
            Check(
                room.Snapshot.Lifecycle == RoomLifecycle.WaitingForFirstPeer,
                "one-person new room is waiting, not dissolved");
            Check(room.Snapshot.Members.Count == 1, "waiting room has creator");
            Check(room.IsLeader(NodeA), "creator is initial leader");

            var requested = network.RequestJoin(NodeB, NodeA);
            Check(requested.Success, "B can request to join A");
            Check(network.GetRoomForNode(NodeB) == null, "request does not auto-admit B");
            Check(room.Snapshot.PendingJoinRequests.Count == 1, "request is replicated");

            var unauthorized = network.ApproveJoin(NodeB, requested.RequestId);
            Check(!unauthorized.Success, "non-member cannot approve request");

            var approved = network.ApproveJoin(NodeA, requested.RequestId);
            Check(approved.Success, "leader approves B");
            Check(network.GetRoomForNode(NodeB) == room, "B joins the same room");
            Check(room.Snapshot.Lifecycle == RoomLifecycle.Formed, "two members form room");
            Check(room.Replicas.Count == 2, "both members hold replicas");
            Check(room.ReplicasAgree(), "two replicas agree");
        }

        private static void TestInvitationFromNonLeaderAndSuccession()
        {
            var network = SimulatedNetwork.CreateThreeNodeDemo(true);
            Check(network.CreateRoom(NodeA, "Shared Room").Success, "A creates room");
            var requestB = network.RequestJoin(NodeB, NodeA);
            Check(network.ApproveJoin(NodeA, requestB.RequestId).Success, "A admits B");

            var invitation = network.Invite(NodeB, NodeC);
            Check(invitation.Success, "non-leader B can invite C");
            var pendingInvitation = network.GetPendingInvitations(NodeC).Single();
            var accepted = network.AcceptInvitation(NodeC, pendingInvitation.InvitationId);
            Check(accepted.Success, "C accepts B invitation");

            var room = network.GetRoomForNode(NodeA);
            var requestC = room.Snapshot.PendingJoinRequests.Single();
            Check(
                string.Equals(requestC.RequestedByNodeId, NodeB, StringComparison.Ordinal),
                "request records B as inviter");
            Check(!network.ApproveJoin(NodeB, requestC.RequestId).Success, "B cannot admit C");
            Check(network.ApproveJoin(NodeA, requestC.RequestId).Success, "leader A admits C");
            Check(room.Snapshot.Members.Count == 3, "room reaches three members");
            Check(room.Replicas.Count == 3 && room.ReplicasAgree(), "three replicas agree");

            var left = network.LeaveRoom(NodeA);
            Check(left.Success && !left.RoomDissolved, "leader exit keeps two-member room");
            room = network.GetRoomForNode(NodeB);
            Check(room != null, "room persists after original owner exits");
            Check(room.IsLeader(NodeB), "earliest remaining member inherits leader");
            Check(room.Snapshot.Term == 2, "leader succession advances term");
            Check(room.Replicas.Count == 2 && room.ReplicasAgree(), "remaining replicas agree");

            Check(!network.Kick(NodeC, NodeB).Success, "ordinary member cannot kick leader");
            var kicked = network.Kick(NodeB, NodeC);
            Check(kicked.Success && kicked.RoomDissolved, "formed room dissolves at one member");
            Check(network.GetRoomForNode(NodeB) == null, "last member is released on dissolve");
            Check(network.GetRoomForNode(NodeC) == null, "kicked member has no room");
        }

        private static void TestDirectJoinThroughAnyRoomMember()
        {
            var network = SimulatedNetwork.CreateThreeNodeDemo(true);
            Check(network.CreateRoom(NodeA, "Join Target").Success, "room creation succeeds");
            var requestB = network.RequestJoin(NodeB, NodeA);
            Check(network.ApproveJoin(NodeA, requestB.RequestId).Success, "B joins room");

            var requestC = network.RequestJoin(NodeC, NodeB);
            Check(requestC.Success, "C can find room through ordinary member B");
            Check(network.ApproveJoin(NodeA, requestC.RequestId).Success, "leader admits direct request");

            var room = network.GetRoomForNode(NodeC);
            Check(room != null && room.Snapshot.Members.Count == 3, "C joins shared room");

            Check(network.LeaveRoom(NodeC).Success, "C can leave");
            Check(network.LeaveRoom(NodeB).RoomDissolved, "two to one transition dissolves room");

            var waiting = network.CreateRoom(NodeC, "Fresh Waiting Room");
            Check(waiting.Success, "released C can create a new waiting room");
            Check(
                network.GetRoomForNode(NodeC).Snapshot.Lifecycle
                    == RoomLifecycle.WaitingForFirstPeer,
                "fresh one-person room remains discoverable");
            Check(network.LeaveRoom(NodeC).RoomDissolved, "creator leaving waiting room dissolves it");
        }

        private static void TestReplicaIsolation()
        {
            var room = RoomSession.Create(NodeA, "A", "Replica Room", "R-TEST");
            var request = room.RequestJoin(NodeB, "B", NodeB);
            Check(room.ApproveJoin(NodeA, request.RequestId).Success, "second replica joins");

            var replicaA = room.Replicas[NodeA];
            var replicaB = room.Replicas[NodeB];
            Check(!ReferenceEquals(replicaA.Snapshot, replicaB.Snapshot), "replicas are deep copies");
            Check(
                replicaA.Snapshot.HasEquivalentState(replicaB.Snapshot),
                "independent copies have equivalent state");
            Check(replicaA.EventLog.Count == replicaB.EventLog.Count, "event logs match");
        }

        private static void TestUsbRecognitionProtocol()
        {
            const string nonce = "7fa02c11";
            var encodedName = UsbControlProtocol.EncodeToken("电脑 A / S3-A");
            Check(
                UsbControlProtocol.EncodeHello(nonce) == "TRILINK/1 HELLO " + nonce,
                "HELLO encoding is stable");
            Check(
                UsbControlProtocol.EncodeSearch(nonce) == "TRILINK/1 SEARCH " + nonce,
                "SEARCH encoding is stable");

            UsbDeviceIdentity identity;
            var deviceLine = "TRILINK/1 DEVICE "
                + nonce
                + " "
                + NodeA
                + " "
                + encodedName
                + " 00000003";
            Check(
                UsbControlProtocol.TryParseDevice(deviceLine, nonce, out identity),
                "valid DEVICE response is recognized");
            Check(identity != null && identity.NodeId == NodeA, "device MAC is normalized");
            Check(identity != null && identity.DisplayName == "电脑 A / S3-A", "name decodes");
            Check(identity != null && identity.Capabilities == 3U, "capabilities decode");
            Check(
                !UsbControlProtocol.TryParseDevice(deviceLine, "wrong", out identity),
                "wrong nonce is rejected");
            Check(
                !UsbControlProtocol.TryParseDevice(
                    "TRILINK/1 DEVICE " + nonce + " FF:FF:FF:FF:FF:FF " + encodedName + " 1",
                    nonce,
                    out identity),
                "multicast MAC is rejected");

            UsbPeerAdvertisement peer;
            var roomName = UsbControlProtocol.EncodeToken("A Room");
            Check(
                UsbControlProtocol.TryParsePeer(
                    "TRILINK/1 PEER "
                    + NodeB
                    + " "
                    + UsbControlProtocol.EncodeToken("电脑 B")
                    + " -43 R-01 "
                    + roomName
                    + " "
                    + NodeA
                    + " 1",
                    out peer),
                "room PEER response parses");
            Check(peer != null && peer.RoomId == "R-01", "peer room id parses");
            Check(peer != null && peer.LeaderNodeId == NodeA, "peer leader parses");
            Check(peer != null && peer.IsOnline && peer.Rssi == -43, "peer link state parses");
            Check(
                UsbControlProtocol.TryParsePeer(
                    "TRILINK/1 PEER "
                    + NodeC
                    + " "
                    + UsbControlProtocol.EncodeToken("电脑 C")
                    + " -51 - - - 1",
                    out peer),
                "roomless PEER response parses");
            Check(peer != null && peer.RoomId == null, "roomless peer remains roomless");
            Check(
                !UsbControlProtocol.TryParsePeer(
                    "TRILINK/1 PEER "
                    + NodeC
                    + " "
                    + UsbControlProtocol.EncodeToken("电脑 C")
                    + " -200 - - - 1",
                    out peer),
                "invalid RSSI is rejected");
            Check(
                UsbControlProtocol.IsSearchEnd("TRILINK/1 END " + nonce, nonce),
                "matching search terminator is accepted");
            Check(
                !UsbControlProtocol.IsSearchEnd("TRILINK/1 END other", nonce),
                "wrong terminator nonce is rejected");
        }

        private static void TestPollingFailureLimiter()
        {
            var limiter = new ConsecutiveFailureLimiter(3);
            Check(limiter.FailureLimit == 3, "polling failure limit is retained");
            Check(!limiter.IsTripped, "polling limiter starts closed");
            Check(!limiter.RecordFailure(), "first polling failure does not trip");
            Check(limiter.ConsecutiveFailures == 1, "first failure increments counter");
            Check(!limiter.RecordFailure(), "second polling failure does not trip");
            Check(limiter.RecordFailure(), "failure limit trips polling limiter");
            Check(
                limiter.IsTripped && limiter.ConsecutiveFailures == 3,
                "tripped polling counter is capped at its limit");
            Check(limiter.RecordFailure(), "tripped limiter remains tripped");
            Check(limiter.ConsecutiveFailures == 3, "extra failures cannot overflow counter");
            Check(limiter.RecordSuccess(), "success reports recovery after failures");
            Check(
                !limiter.IsTripped && limiter.ConsecutiveFailures == 0,
                "success resets polling limiter");
            Check(!limiter.RecordSuccess(), "clean success does not report recovery");
        }

        private static void TestPluginDependencyGraph()
        {
            var rooms = Manifest("trilink.rooms");
            rooms.providesServices = new[] { PluginServiceIds.RoomNetwork };
            var serial = Manifest("trilink.serial");
            serial.providesServices = new[] { PluginServiceIds.DeviceDiscovery };
            var simulation = Manifest("trilink.simulation");
            simulation.requiresServices = new[] { PluginServiceIds.RoomNetwork };
            simulation.providesServices = new[] { PluginServiceIds.SimulationControl };
            var desktop = Manifest("trilink.desktop");
            desktop.requiresServices = new[]
            {
                PluginServiceIds.RoomNetwork,
                PluginServiceIds.DeviceDiscovery,
                PluginServiceIds.SimulationControl,
                PluginServiceIds.PluginCatalog,
            };
            desktop.providesServices = new[] { PluginServiceIds.DesktopShell };
            var ordered = PluginDependencyGraph.Order(
                new[] { desktop, simulation, serial, rooms });

            Check(ordered.Count == 4, "all enabled plugins enter dependency order");
            Check(
                Array.IndexOf(ordered.Select(item => item.id).ToArray(), "trilink.rooms")
                    < Array.IndexOf(ordered.Select(item => item.id).ToArray(), "trilink.simulation"),
                "room provider precedes simulation consumer");
            Check(
                string.Equals(ordered.Last().id, "trilink.desktop", StringComparison.Ordinal),
                "desktop shell starts after all required services");

            var missingService = Manifest("trilink.service-consumer");
            missingService.requiresServices = new[] { "trilink.unavailable" };
            Check(
                Throws<InvalidDataException>(
                    () => PluginDependencyGraph.Order(new[] { missingService })),
                "missing required service is rejected");

            var duplicateProviderA = Manifest("trilink.provider-a");
            var duplicateProviderB = Manifest("trilink.provider-b");
            duplicateProviderA.providesServices = new[] { "trilink.shared-service" };
            duplicateProviderB.providesServices = new[] { "trilink.shared-service" };
            Check(
                Throws<InvalidDataException>(
                    () => PluginDependencyGraph.Order(
                        new[] { duplicateProviderA, duplicateProviderB })),
                "multiple providers for one service are rejected");

            Check(
                Throws<InvalidDataException>(
                    () => PluginDependencyGraph.Order(
                        new[] { Manifest("trilink.consumer", "trilink.missing") })),
                "missing plugin dependency is rejected");
            Check(
                Throws<InvalidDataException>(
                    () => PluginDependencyGraph.Order(
                        new[]
                        {
                            Manifest("trilink.a", "trilink.b"),
                            Manifest("trilink.b", "trilink.a"),
                        })),
                "plugin dependency cycle is rejected");
        }

        private static void TestPluginServices()
        {
            var services = new PluginServices();
            services.Register<string>("room-service");
            Check(
                services.GetRequired<string>() == "room-service",
                "plugin service resolves by contract type");

            object missing;
            Check(!services.TryGet<object>(out missing), "unknown plugin service is absent");
            Check(
                Throws<InvalidOperationException>(
                    () => services.Register<string>("replacement")),
                "duplicate service provider is rejected");
        }

        private static void TestPluginProfile()
        {
            var profile = new PluginProfile
            {
                schemaVersion = 1,
                name = "desktop",
                plugins = new[] { "trilink.rooms", "trilink.desktop" },
            };
            profile.Validate();
            Check(profile.PluginIds.Count == 2, "plugin profile accepts distinct plugin ids");

            var duplicate = new PluginProfile
            {
                schemaVersion = 1,
                name = "desktop",
                plugins = new[] { "trilink.rooms", "trilink.rooms" },
            };
            Check(
                Throws<InvalidDataException>(duplicate.Validate),
                "plugin profile rejects duplicate plugin ids");

            var environment = new HostEnvironment(".", new[] { "--demo" }, "lab");
            Check(
                environment.DemoMode && environment.ProfileName == "lab",
                "host environment exposes profile and mode to plugins");
        }

        private static PluginManifest Manifest(string id, params string[] dependencies)
        {
            return new PluginManifest
            {
                schemaVersion = 1,
                id = id,
                displayName = id,
                version = "1.0.0",
                hostApi = "1.0",
                enabled = true,
                entryAssembly = id + ".dll",
                entryType = "Tests.Plugin",
                dependencies = dependencies,
                requiresServices = new string[0],
                providesServices = new string[0],
                capabilities = new[] { "test" },
            };
        }

        private static bool Throws<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (TException)
            {
                return true;
            }
        }

        private static void Check(bool condition, string description)
        {
            _checks++;
            if (condition)
            {
                return;
            }

            _failures++;
            Console.Error.WriteLine("FAIL: " + description);
        }
    }
}
