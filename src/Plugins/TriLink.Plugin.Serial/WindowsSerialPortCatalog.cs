using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

[assembly: InternalsVisibleTo("TriLink.Core.Tests")]

namespace TriLink.MinClient.Serial
{
    internal sealed class SerialCandidate
    {
        private static readonly Regex PortPattern = new Regex(
            @"\ACOM[1-9][0-9]{0,4}\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public string PortName { get; set; }
        public string Name { get; set; }
        public string PnpDeviceId { get; set; }
        public string Manufacturer { get; set; }

        public bool IsValidPort { get { return PortPattern.IsMatch(PortName ?? string.Empty); } }
        public bool IsProtocolCandidate
        {
            get
            {
                return IsValidPort && (Contains(Name, "TriLink")
                    || Contains(Manufacturer, "TriLink") || Contains(PnpDeviceId, "VID_303A&"));
            }
        }
        public bool IsProgrammingBridge
        {
            get
            {
                return Contains(PnpDeviceId, "VID_1A86&PID_7523")
                    || Contains(PnpDeviceId, "VID_1A86&PID_55D3");
            }
        }

        private static bool Contains(string text, string value)
        {
            return (text ?? string.Empty).IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    // Read-only, present-device enumeration. No WMI provider, subprocess, or COM opening.
    // API contracts: Microsoft Learn, SetupDiGetClassDevsW / SetupDiOpenDevRegKey.
    internal static class WindowsSerialPortCatalog
    {
        private static readonly IntPtr InvalidHandle = new IntPtr(-1);
        private const uint DigcfPresent = 2;
        private const int ErrorNoMoreItems = 259;

        public static List<SerialCandidate> Enumerate()
        {
            var portClass = new Guid("4d36e978-e325-11ce-bfc1-08002be10318");
            var devices = SetupDiGetClassDevsW(ref portClass, null, IntPtr.Zero, DigcfPresent);
            if (devices == InvalidHandle || devices == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                var result = new List<SerialCandidate>();
                for (uint index = 0; ; index++)
                {
                    var info = new DeviceInfo { Size = (uint)Marshal.SizeOf(typeof(DeviceInfo)) };
                    if (!SetupDiEnumDeviceInfo(devices, index, ref info))
                    {
                        var error = Marshal.GetLastWin32Error();
                        if (error == ErrorNoMoreItems) { break; }
                        throw new Win32Exception(error);
                    }

                    var portName = ReadPortName(devices, ref info);
                    var candidate = new SerialCandidate { PortName = portName };
                    if (!candidate.IsValidPort) { continue; }
                    candidate.Name = ReadTextProperty(devices, ref info, 12); // SPDRP_FRIENDLYNAME
                    if (string.IsNullOrEmpty(candidate.Name))
                    {
                        candidate.Name = ReadTextProperty(devices, ref info, 0); // SPDRP_DEVICEDESC
                    }
                    candidate.Manufacturer = ReadTextProperty(devices, ref info, 11); // SPDRP_MFG
                    var instanceId = new StringBuilder(512);
                    uint required;
                    candidate.PnpDeviceId = SetupDiGetDeviceInstanceIdW(
                        devices, ref info, instanceId, (uint)instanceId.Capacity, out required)
                        ? instanceId.ToString() : ReadTextProperty(devices, ref info, 1);
                    result.Add(candidate);
                }
                return result;
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(devices);
            }
        }

        private static string ReadPortName(IntPtr devices, ref DeviceInfo info)
        {
            // DICS_FLAG_GLOBAL, DIREG_DEV, KEY_QUERY_VALUE: never request write/admin access.
            var handle = SetupDiOpenDevRegKey(devices, ref info, 1, 0, 1, 1);
            if (handle == InvalidHandle || handle == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                if (error == 2 || error == 1167) { return string.Empty; } // unplugged/missing key
                throw new Win32Exception(error);
            }
            using (var safeHandle = new SafeRegistryHandle(handle, true))
            using (var key = RegistryKey.FromHandle(safeHandle))
            {
                return key.GetValue("PortName") as string ?? string.Empty;
            }
        }

        private static string ReadTextProperty(IntPtr devices, ref DeviceInfo info, uint property)
        {
            var buffer = new byte[2048];
            uint type;
            uint required;
            if (!SetupDiGetDeviceRegistryPropertyW(
                devices, ref info, property, out type, buffer, (uint)buffer.Length, out required))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == 13 || error == 1167 || error == 1168) { return string.Empty; }
                if (error != 122 || required > 8192) { throw new Win32Exception(error); }
                buffer = new byte[required];
                if (!SetupDiGetDeviceRegistryPropertyW(
                    devices, ref info, property, out type, buffer, (uint)buffer.Length, out required))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            if ((type != 1 && type != 7) || required > buffer.Length || (required & 1) != 0)
            {
                return string.Empty;
            }
            return Encoding.Unicode.GetString(buffer, 0, (int)required).TrimEnd('\0');
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DeviceInfo
        {
            public uint Size;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevsW(
            ref Guid classGuid, string enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr devices, uint index, ref DeviceInfo info);
        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiOpenDevRegKey(
            IntPtr devices, ref DeviceInfo info, uint scope, uint profile, uint keyType, uint access);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiGetDeviceRegistryPropertyW(
            IntPtr devices, ref DeviceInfo info, uint property, out uint type,
            [Out] byte[] buffer, uint size, out uint required);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiGetDeviceInstanceIdW(
            IntPtr devices, ref DeviceInfo info, StringBuilder id, uint size, out uint required);
        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr devices);
    }
}
