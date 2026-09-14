using System;
using System.Globalization;
using System.Text;

namespace TriLink.Core
{
    public sealed class UsbDeviceIdentity
    {
        public string NodeId { get; set; }

        public string DisplayName { get; set; }

        public uint Capabilities { get; set; }
    }

    public sealed class UsbPeerAdvertisement
    {
        public string NodeId { get; set; }

        public string DisplayName { get; set; }

        public int Rssi { get; set; }

        public string RoomId { get; set; }

        public string RoomName { get; set; }

        public string LeaderNodeId { get; set; }

        public bool IsOnline { get; set; }
    }

    public static class UsbControlProtocol
    {
        public const string Prefix = "TRILINK/1";

        public static string EncodeHello(string nonce)
        {
            ValidateNonce(nonce);
            return Prefix + " HELLO " + nonce;
        }

        public static string EncodeSearch(string nonce)
        {
            ValidateNonce(nonce);
            return Prefix + " SEARCH " + nonce;
        }

        public static bool IsSearchEnd(string line, string expectedNonce)
        {
            return !string.IsNullOrWhiteSpace(line)
                && string.Equals(
                    line.Trim(),
                    Prefix + " END " + expectedNonce,
                    StringComparison.Ordinal);
        }

        public static bool TryParseDevice(
            string line,
            string expectedNonce,
            out UsbDeviceIdentity identity)
        {
            identity = null;
            var parts = Split(line);
            uint capabilities;
            string normalizedNodeId;
            string displayName;
            if (parts == null
                || parts.Length != 6
                || !string.Equals(parts[0], Prefix, StringComparison.Ordinal)
                || !string.Equals(parts[1], "DEVICE", StringComparison.Ordinal)
                || !string.Equals(parts[2], expectedNonce, StringComparison.Ordinal)
                || !TryNormalizeMac(parts[3], out normalizedNodeId)
                || !TryDecodeToken(parts[4], out displayName)
                || string.IsNullOrWhiteSpace(displayName)
                || !uint.TryParse(
                    parts[5],
                    NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture,
                    out capabilities))
            {
                return false;
            }

            identity = new UsbDeviceIdentity
            {
                NodeId = normalizedNodeId,
                DisplayName = displayName,
                Capabilities = capabilities,
            };
            return true;
        }

        public static bool TryParsePeer(string line, out UsbPeerAdvertisement peer)
        {
            peer = null;
            var parts = Split(line);
            int rssi;
            string nodeId;
            string displayName;
            string leaderNodeId = null;
            string roomName = null;
            bool online;
            if (parts == null
                || parts.Length != 9
                || !string.Equals(parts[0], Prefix, StringComparison.Ordinal)
                || !string.Equals(parts[1], "PEER", StringComparison.Ordinal)
                || !TryNormalizeMac(parts[2], out nodeId)
                || !TryDecodeToken(parts[3], out displayName)
                || !int.TryParse(
                    parts[4],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out rssi)
                || rssi < -127
                || rssi > 0
                || !TryParseOnline(parts[8], out online))
            {
                return false;
            }

            var hasRoom = !string.Equals(parts[5], "-", StringComparison.Ordinal);
            if (hasRoom)
            {
                if (string.IsNullOrWhiteSpace(parts[5])
                    || !TryDecodeToken(parts[6], out roomName)
                    || !TryNormalizeMac(parts[7], out leaderNodeId))
                {
                    return false;
                }
            }
            else if (!string.Equals(parts[6], "-", StringComparison.Ordinal)
                || !string.Equals(parts[7], "-", StringComparison.Ordinal))
            {
                return false;
            }

            peer = new UsbPeerAdvertisement
            {
                NodeId = nodeId,
                DisplayName = displayName,
                Rssi = rssi,
                RoomId = hasRoom ? parts[5] : null,
                RoomName = roomName,
                LeaderNodeId = leaderNodeId,
                IsOnline = online,
            };
            return true;
        }

        public static string EncodeToken(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }

        private static string[] Split(string line)
        {
            return string.IsNullOrWhiteSpace(line)
                ? null
                : line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static bool TryDecodeToken(string value, out string decoded)
        {
            decoded = null;
            try
            {
                decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value));
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static bool TryNormalizeMac(string value, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var parts = value.Split(':');
            if (parts.Length != 6)
            {
                return false;
            }

            var bytes = new byte[6];
            for (var index = 0; index < parts.Length; index++)
            {
                if (parts[index].Length != 2
                    || !byte.TryParse(
                        parts[index],
                        NumberStyles.AllowHexSpecifier,
                        CultureInfo.InvariantCulture,
                        out bytes[index]))
                {
                    return false;
                }
            }

            if ((bytes[0] & 0x01) != 0)
            {
                return false;
            }

            var aggregate = 0;
            for (var index = 0; index < bytes.Length; index++)
            {
                aggregate |= bytes[index];
            }

            if (aggregate == 0)
            {
                return false;
            }

            normalized = string.Join(
                ":",
                Array.ConvertAll(bytes, item => item.ToString("X2", CultureInfo.InvariantCulture)));
            return true;
        }

        private static bool TryParseOnline(string value, out bool online)
        {
            if (string.Equals(value, "1", StringComparison.Ordinal))
            {
                online = true;
                return true;
            }

            if (string.Equals(value, "0", StringComparison.Ordinal))
            {
                online = false;
                return true;
            }

            online = false;
            return false;
        }

        private static void ValidateNonce(string nonce)
        {
            if (string.IsNullOrWhiteSpace(nonce)
                || nonce.Length > 32
                || nonce.IndexOf(' ') >= 0
                || nonce.IndexOf('\r') >= 0
                || nonce.IndexOf('\n') >= 0)
            {
                throw new ArgumentException("Nonce must be 1-32 non-space characters.", nameof(nonce));
            }
        }
    }
}
