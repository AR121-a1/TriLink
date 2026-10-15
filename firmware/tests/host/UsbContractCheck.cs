using System;
using System.Collections.Generic;
using TriLink.Core;

internal static class UsbContractCheck
{
    private static int Main()
    {
        int devices = 0, ends = 0;
        var peers = new HashSet<string>();
        string line;
        while ((line = Console.ReadLine()) != null)
        {
            if (String.IsNullOrWhiteSpace(line)) continue;
            UsbDeviceIdentity device;
            UsbPeerAdvertisement peer;
            if (UsbControlProtocol.TryParseDevice(line, "1234abcd", out device))
            {
                if (device.NodeId != "10:00:00:00:00:01" || device.DisplayName != "TriLink-100000000001"
                    || device.Capabilities != 3U) return 1;
                ++devices;
            }
            else if (UsbControlProtocol.TryParsePeer(line, out peer))
            {
                if (!peers.Add(peer.NodeId) || peer.RoomId != null || !peer.IsOnline
                    || peer.DisplayName != "TriLink-" + peer.NodeId.Replace(":", "")) return 2;
            }
            else if (UsbControlProtocol.IsSearchEnd(line, "1234abcd")) ++ends;
            else return 3;
        }
        if (devices != 1 || peers.Count != 6 || ends != 1) return 4;
        Console.WriteLine("PASS firmware C -> client v0.13 C# protocol: DEVICE=1 PEER=6 END=1");
        return 0;
    }
}
