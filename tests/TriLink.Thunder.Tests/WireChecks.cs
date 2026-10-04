using System;
using TriLink.Plugins.Thunder;

namespace TriLink.Thunder.Tests
{
    internal static class WireChecks
    {
        public static void Run(Action<bool, string> check)
        {
            var packet = new GamePacket { Message = GameMessage.Frames, Session = 0x01020304, Token = 99,
                Tick = 17, Count = 3, Inputs = new byte[] { 1, 16, 2, 8, 0, 0 } };
            var bytes = GameWire.Encode(packet); GamePacket decoded;
            check(bytes.Length == 30 && bytes[4] == 4 && bytes[7] == 1, "compact little-endian three-tick packet is 30 bytes");
            check(GameWire.TryDecode(bytes, out decoded) && decoded.Tick == 17 && decoded.Inputs[3] == 8,
                "wire preserves input order and session identity");
            foreach (var offset in new[] { 0, 2, 3, 20, 22, 24 })
            {
                var invalid = (byte[])bytes.Clone(); invalid[offset] = 255;
                check(!GameWire.TryDecode(invalid, out decoded), "malformed field rejected at " + offset);
            }
            foreach (var size in new[] { 0, 23, 29, 31, 129 })
            { check(!GameWire.TryDecode(new byte[size], out decoded), "invalid packet size rejected: " + size); }
            packet.Count = 32; packet.Inputs = new byte[64];
            check(GameWire.Encode(packet).Length == 88 && GameWire.MaxPacketBytes <= 128, "largest recovery batch fits future 128-byte adapter budget");
            packet.Tick = uint.MaxValue;
            var rejected = false; try { GameWire.Encode(packet); } catch (ArgumentException) { rejected = true; }
            check(rejected, "tick range cannot overflow in a batch");
        }
    }
}
