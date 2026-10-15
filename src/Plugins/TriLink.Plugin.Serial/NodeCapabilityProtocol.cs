using System;
using System.Globalization;
using System.Linq;
using TriLink.Core;

namespace TriLink.MinClient.Serial
{
    internal static class NodeCapabilityProtocol
    {
        // CAPS is an exact, bounded ASCII contract. Unknown contract versions fail closed.
        internal static bool TryParse(string line, string nonce, string nodeId, out NodeCapabilities result)
        {
            result = null;
            if (string.IsNullOrEmpty(line) || line.Length > 160 || string.IsNullOrEmpty(nonce)
                || line.Any(c => c < 32 || c > 126)) return false;
            var fields = line.Split(' ');
            uint boot, body, neighbors, members, modules;
            Version firmware;
            if (fields.Length != 10 || fields[0] != "TRILINK/3" || fields[1] != "CAPS"
                || fields[2] != nonce || fields[3] != "1"
                || fields[4].Split('.').Length != 3
                || fields[4].Any(c => (c < '0' || c > '9') && c != '.')
                || !Version.TryParse(fields[4], out firmware)
                || firmware.Major > 65535 || firmware.Minor > 65535 || firmware.Build > 65535
                || !Hex(fields[5], 8, out boot) || boot == 0
                || !Hex(fields[6], 2, out body) || body == 0 || body > 132
                || !Hex(fields[7], 2, out neighbors) || neighbors > 6
                || !Hex(fields[8], 2, out members) || members == 0 || members > 6
                || !Hex(fields[9], 2, out modules)) return false;
            result = new NodeCapabilities(nodeId, false, 1, fields[4], boot,
                (int)body, (int)neighbors, (int)members, modules);
            return true;
        }

        private static bool Hex(string value, int width, out uint parsed)
        {
            parsed = 0;
            return value.Length == width && uint.TryParse(value,
                NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out parsed);
        }
    }
}
