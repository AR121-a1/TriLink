using System;

namespace TriLink.Plugins.Thunder
{
    public enum GameMessage : byte { Hello = 1, Start = 2, Ready = 3, Input = 4, Frames = 5, Bye = 6 }

    public sealed class GamePacket
    {
        public GameMessage Message;
        public uint Session, Token, Tick, Sequence;
        public byte Input, Count;
        public byte[] Inputs = new byte[0];
    }

    // Small application datagrams; the adapter supplies UDP today and can supply ESP32 later.
    // All multi-byte values are unsigned little-endian. No scene, assets or files go on the link.
    public static class GameWire
    {
        public const int HeaderBytes = 24;
        public const int MaxBatchTicks = 32;
        public const int MaxPacketBytes = HeaderBytes + MaxBatchTicks * 2;
        public const byte Version = 1;

        public static byte[] Encode(GamePacket packet)
        {
            if (!Valid(packet)) { throw new ArgumentException("Invalid game packet.", nameof(packet)); }
            var bytes = new byte[HeaderBytes + packet.Inputs.Length];
            bytes[0] = 0x54; bytes[1] = 0x46; bytes[2] = Version; bytes[3] = (byte)packet.Message;
            Write(bytes, 4, packet.Session); Write(bytes, 8, packet.Token);
            Write(bytes, 12, packet.Tick); Write(bytes, 16, packet.Sequence);
            bytes[20] = packet.Count; bytes[21] = packet.Input;
            Buffer.BlockCopy(packet.Inputs, 0, bytes, HeaderBytes, packet.Inputs.Length);
            return bytes;
        }

        public static bool TryDecode(byte[] bytes, out GamePacket packet)
        {
            packet = null;
            if (bytes == null || bytes.Length < HeaderBytes || bytes.Length > MaxPacketBytes
                || bytes[0] != 0x54 || bytes[1] != 0x46 || bytes[2] != Version
                || bytes[22] != 0 || bytes[23] != 0) { return false; }
            var candidate = new GamePacket { Message = (GameMessage)bytes[3], Session = Read(bytes, 4),
                Token = Read(bytes, 8), Tick = Read(bytes, 12), Sequence = Read(bytes, 16),
                Count = bytes[20], Input = bytes[21], Inputs = new byte[bytes.Length - HeaderBytes] };
            Buffer.BlockCopy(bytes, HeaderBytes, candidate.Inputs, 0, candidate.Inputs.Length);
            if (!Valid(candidate)) { return false; }
            packet = candidate; return true;
        }

        private static bool Valid(GamePacket packet)
        {
            if (packet == null || packet.Token == 0 || packet.Inputs == null) { return false; }
            if (packet.Message == GameMessage.Frames)
            {
                if (packet.Session == 0 || packet.Tick == 0 || packet.Sequence != 0 || packet.Input != 0
                    || packet.Count == 0 || packet.Count > MaxBatchTicks || packet.Inputs.Length != packet.Count * 2
                    || (ulong)packet.Tick + packet.Count - 1 > uint.MaxValue) { return false; }
                foreach (var input in packet.Inputs) { if ((input & ~31) != 0) { return false; } }
                return true;
            }
            if (packet.Count != 0 || packet.Inputs.Length != 0) { return false; }
            switch (packet.Message)
            {
                case GameMessage.Hello:
                    return packet.Session == 0 && packet.Tick == 0 && packet.Sequence == 0 && packet.Input == 0;
                case GameMessage.Start:
                    return packet.Session != 0 && packet.Sequence == 0 && packet.Input == 0;
                case GameMessage.Ready:
                    return packet.Session != 0 && packet.Tick == 0 && packet.Sequence == 0 && packet.Input == 0;
                case GameMessage.Input:
                    return packet.Session != 0 && (packet.Input & ~31) == 0;
                case GameMessage.Bye:
                    return packet.Session != 0 && packet.Tick == 0 && packet.Sequence == 0 && packet.Input == 0;
                default: return false;
            }
        }

        private static uint Read(byte[] bytes, int offset)
        { return (uint)bytes[offset] | (uint)bytes[offset + 1] << 8 | (uint)bytes[offset + 2] << 16 | (uint)bytes[offset + 3] << 24; }
        private static void Write(byte[] bytes, int offset, uint value)
        { for (var i = 0; i < 4; ++i) { bytes[offset + i] = (byte)(value >> (i * 8)); } }
    }
}
