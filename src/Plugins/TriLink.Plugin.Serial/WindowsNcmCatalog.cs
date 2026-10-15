using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TriLink.MinClient.Serial
{
    // Read only: never install drivers, change addresses/routes or scan a subnet.
    internal sealed class WindowsNcmCatalog : INcmCatalog
    {
        private const string NetClass = @"SYSTEM\CurrentControlSet\Control\Network\{4d36e972-e325-11ce-bfc1-08002be10318}";
        internal static bool IsTriLinkPnp(string pnp)
        {
            return (pnp ?? "").StartsWith(@"USB\VID_303A&PID_4011&MI_02\", StringComparison.OrdinalIgnoreCase);
        }
        internal static bool IsUsbLocalAddress(IPAddress address, IPAddress mask)
        {
            if (address == null || address.AddressFamily != AddressFamily.InterNetwork
                || mask == null || !mask.Equals(IPAddress.Parse("255.255.255.0"))) return false;
            var bytes = address.GetAddressBytes();
            return bytes[0] == 192 && bytes[1] == 168 && bytes[2] == 240 && (bytes[3] == 2 || bytes[3] == 3);
        }
        public List<NcmEndpoint> Enumerate()
        {
            var result = new List<NcmEndpoint>();
            uint routeIndex;
            if (GetBestInterface(BitConverter.ToUInt32(NcmEndpoint.DeviceAddress.GetAddressBytes(), 0), out routeIndex) != 0)
                return result;
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up || !adapter.Supports(NetworkInterfaceComponent.IPv4)) continue;
                Guid guid;
                if (!Guid.TryParse(adapter.Id, out guid) || guid == Guid.Empty) continue;
                string pnp;
                using (var key = Registry.LocalMachine.OpenSubKey(NetClass + "\\{" + guid.ToString("D") + "}\\Connection", false))
                    pnp = key == null ? null : key.GetValue("PnpInstanceID") as string;
                if (!IsTriLinkPnp(pnp)) continue;
                var properties = adapter.GetIPProperties();
                var ipv4 = properties.GetIPv4Properties();
                if (ipv4 == null || (uint)ipv4.Index != routeIndex) continue;
                var addresses = properties.UnicastAddresses.Where(address => IsUsbLocalAddress(address.Address, address.IPv4Mask)).ToList();
                if (addresses.Count != 1) continue; // An ambiguous USB address is not guessed.
                result.Add(new NcmEndpoint { EndpointName = "NCM:" + guid.ToString("D").ToUpperInvariant(),
                    PnpDeviceId = pnp, InterfaceIndex = routeIndex, LocalAddress = addresses[0].Address });
            }
            return result.Count == 1 ? result : new List<NcmEndpoint>();
        }
        [DllImport("iphlpapi.dll")]
        private static extern uint GetBestInterface(uint destinationAddress, out uint bestInterfaceIndex);
    }
}
