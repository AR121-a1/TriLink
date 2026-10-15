using System;
using TriLink.Plugins.Thunder;

namespace TriLink.Thunder.Tests
{
    internal static class WireChecks
    {
        public static void Run(Action<bool, string> check)
        {
            var packet = new GamePacket { Message = GameMessage.Frames, Session = 0x01020304, Token = 99,
                Tick = 17, Count = 3, PlayerCount = 2, Inputs = new byte[] { 1, 16, 2, 8, 0, 0 } };
            var bytes = GameWire.Encode(packet); GamePacket decoded;
            check(bytes.Length == 30 && bytes[4] == 4 && bytes[7] == 1, "compact little-endian three-tick packet is 30 bytes");
            check(GameWire.TryDecode(bytes, out decoded) && decoded.Tick == 17 && decoded.Inputs[3] == 8,
                "wire preserves input order and session identity");
            foreach (var offset in new[] { 0, 2, 3, 20, 22, 23, 24 })
            {
                var invalid = (byte[])bytes.Clone(); invalid[offset] = 255;
                check(!GameWire.TryDecode(invalid, out decoded), "malformed field rejected at " + offset);
            }
            foreach (var size in new[] { 0, 23, 29, 31, 129 })
            { check(!GameWire.TryDecode(new byte[size], out decoded), "invalid packet size rejected: " + size); }
            packet.PlayerCount = 6; packet.Count = 16; packet.Inputs = new byte[96];
            check(GameWire.Encode(packet).Length == 120 && GameWire.MaxPacketBytes <= 128, "six-player recovery batch fits the 128-byte adapter budget");
            packet.Tick = uint.MaxValue;
            var rejected = false; try { GameWire.Encode(packet); } catch (ArgumentException) { rejected = true; }
            check(rejected, "tick range cannot overflow in a batch");
            for (var players = 2; players <= GameEngine.MaxPlayers; ++players)
            {
                packet.Tick = 1; packet.PlayerCount = (byte)players; packet.Count = 3; packet.Inputs = new byte[players * 3];
                bytes = GameWire.Encode(packet);
                check(bytes.Length == 24 + 3 * players && GameWire.TryDecode(bytes, out decoded) && decoded.PlayerCount == players,
                    "per-tick multi-player payload is exact for " + players + " players");
                var legacy = (byte[])bytes.Clone(); legacy[2] = 1;
                check(!GameWire.TryDecode(legacy, out decoded), "legacy v1 rules are rejected for " + players + " players");
            }
            foreach (var message in new[] { GameMessage.Start, GameMessage.Ready, GameMessage.Input, GameMessage.Bye })
            {
                var control = new GamePacket { Message = message, Session = 3, Token = 7, PlayerCount = 6, PlayerIndex = 5 };
                check(GameWire.TryDecode(GameWire.Encode(control), out decoded) && decoded.PlayerIndex == 5,
                    "highest valid guest seat survives " + message);
                foreach (var invalid in new byte[] { 0, 6, 255 })
                {
                    bytes = GameWire.Encode(control); bytes[23] = invalid;
                    check(GameWire.TryDecode(bytes, out decoded) == (message == GameMessage.Bye && invalid == 0),
                        "seat boundary is validated for " + message + ": " + invalid);
                }
                foreach (var invalid in new byte[] { 0, 1, 7, 255 })
                {
                    bytes = GameWire.Encode(control); bytes[22] = invalid;
                    check(!GameWire.TryDecode(bytes, out decoded), "participant boundary is validated for " + message + ": " + invalid);
                }
            }
            bytes = GameWire.Encode(new GamePacket { Message = GameMessage.Hello, Token = 2 }); bytes[22] = 2;
            check(!GameWire.TryDecode(bytes, out decoded), "Hello cannot claim a preassigned player count");
            var frame = new GamePacket { Message = GameMessage.Frames, Session = 5, Token = 9,
                Tick = 1, Count = 3, PlayerCount = 6, Inputs = new byte[18] };
            bytes = GameWire.Encode(frame);
            foreach (var length in new[] { bytes.Length - 1, bytes.Length + 1 })
            {
                var invalid = new byte[length]; Buffer.BlockCopy(bytes, 0, invalid, 0, Math.Min(length, bytes.Length));
                check(!GameWire.TryDecode(invalid, out decoded), "a valid frame header rejects a body length mismatch: " + length);
            }
            foreach (var offset in new[] { 4, 8, 12 })
            {
                var invalid = (byte[])bytes.Clone(); Array.Clear(invalid, offset, 4);
                check(!GameWire.TryDecode(invalid, out decoded), "a valid frame rejects a zero identity/tick field at " + offset);
            }
            foreach (var offset in new[] { 16, 21, 23 })
            {
                var invalid = (byte[])bytes.Clone(); invalid[offset] = 1;
                check(!GameWire.TryDecode(invalid, out decoded), "a valid frame rejects a reserved sequence/input/seat at " + offset);
            }
            foreach (var count in new byte[] { 0, 17 })
            { var invalid = (byte[])bytes.Clone(); invalid[20] = count; check(!GameWire.TryDecode(invalid, out decoded), "invalid frame batch count rejected: " + count); }
            foreach (var players in new byte[] { 0, 1, 7 })
            { var invalid = (byte[])bytes.Clone(); invalid[22] = players; check(!GameWire.TryDecode(invalid, out decoded), "invalid frame participant count rejected: " + players); }
            frame.Tick = uint.MaxValue; frame.Count = 1; frame.Inputs = new byte[6];
            check(GameWire.TryDecode(GameWire.Encode(frame), out decoded), "a single final uint tick is valid");
            frame.Tick = uint.MaxValue - 2; frame.Count = 3; frame.Inputs = new byte[18];
            check(GameWire.TryDecode(GameWire.Encode(frame), out decoded), "a recovery batch ending exactly at uint.MaxValue is valid");
            frame.Tick++; rejected = false; try { GameWire.Encode(frame); } catch (ArgumentException) { rejected = true; }
            check(rejected, "a recovery batch ending one tick beyond uint.MaxValue is rejected");
        }
    }
}
