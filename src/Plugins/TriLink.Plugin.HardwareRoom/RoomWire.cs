using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TriLink.Plugins.HardwareRoom
{
    public sealed class RoomWire
    {
        public byte[] Bytes { get; private set; }
        public byte Kind { get { return Bytes[0]; } }
        public int Count { get { return Bytes[2]; } }
        public bool Active { get { return (Bytes[130] & 1) != 0; } }
        public bool Synchronized { get { return (Bytes[130] & 2) != 0; } }
        public bool Waiting { get { return (Bytes[130] & 4) != 0; } }
        public bool RgbEnabled { get { return (Bytes[130] & 8) != 0; } }
        public uint Room { get { return Number(18); } }
        public uint Incarnation { get { return Number(22); } }
        public uint Revision { get { return Number(26); } }
        public uint Term { get { return Number(30); } }
        public string Source { get { return Hex(Bytes.Skip(4).Take(6).ToArray()); } }
        public string Leader { get { return Count == 0 ? "" : Members[0]; } }
        public string Name { get { int end = Array.IndexOf(Bytes, (byte)0, 100, 24);
            return new UTF8Encoding(false, true).GetString(Bytes, 100, end - 100); } }
        public string[] Members { get { return Enumerable.Range(0, Count)
            .Select(i => Hex(Bytes.Skip(40 + i * 10).Take(6).ToArray())).ToArray(); } }
        public RoomWire(byte[] bytes)
        {
            if (bytes == null || bytes.Length != 132 || bytes[2] > 6 || bytes[1] > 5
                || bytes[3] > 1 || bytes[123] != 0 || bytes[131] != 0)
                throw new InvalidDataException("Room 数据格式错误。");
            Bytes = (byte[])bytes.Clone();
            for (int i = 40 + Count * 10; i < 100; i++)
                if (Bytes[i] != 0) throw new InvalidDataException("Room 未使用成员区必须为零。");
            var members = Members;
            if (members.Distinct().Count() != members.Length || members.Any(m => !ValidMac(m)))
                throw new InvalidDataException("Room 成员重复或身份无效。");
            for (int i = 0; i < Count; i++)
                if (Number(46 + i * 10) == 0) throw new InvalidDataException("Room 成员启动标识无效。");
            var ignored = Name;
        }
        public uint Number(int offset) { return (uint)Bytes[offset] | (uint)Bytes[offset+1]<<8
            | (uint)Bytes[offset+2]<<16 | (uint)Bytes[offset+3]<<24; }
        public static bool ValidMac(string mac)
        {
            byte[] bytes; try { bytes = Unhex(mac); } catch (Exception) { return false; }
            return bytes.Length == 6 && (bytes[0] & 1) == 0 && bytes.Any(b => b != 0);
        }
        public static byte[] Unhex(string hex)
        {
            if (hex == null || hex.Length % 2 != 0 || hex.Any(c => !Uri.IsHexDigit(c)))
                throw new InvalidDataException("非法十六进制数据。");
            return Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(i*2,2),16)).ToArray();
        }
        public static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", ""); }
        public static RoomWire ParseReply(string reply, int page)
        {
            var fields = reply.Split(' ');
            if (fields.Length == 3 && fields[0] == "TRILINK/3" && fields[1] == "EMPTY") return null;
            if (fields.Length != 5 || fields[0] != "TRILINK/3" || fields[1] != "ROOMSTATE"
                || fields[3] != page.ToString("X2")) throw new InvalidDataException("Room 页码/回复类型不匹配。");
            return new RoomWire(Unhex(fields[4]));
        }
        public static string Command(byte kind, RoomWire state, string target = null, string name = null)
        {
            byte[] bytes = state == null ? new byte[132] : (byte[])state.Bytes.Clone();
            bytes[0] = kind; bytes[130] = 0;
            Array.Clear(bytes, 124, 6);
            if (target != null) {
                target = target.Replace(":", "");
                if (!ValidMac(target)) throw new ArgumentException("请选择有效目标 MAC。");
                Array.Copy(Unhex(target), 0, bytes, 124, 6);
            }
            if (name != null) {
                var encoded = new UTF8Encoding(false, true).GetBytes(name);
                if (encoded.Length == 0 || encoded.Length > 23 || name.Any(char.IsControl))
                    throw new ArgumentException("Room 名限 1–23 个 UTF-8 字节，不含控制字符。");
                Array.Clear(bytes, 100, 24); Array.Copy(encoded, 0, bytes, 100, encoded.Length);
            }
            return Hex(bytes);
        }
        public override string ToString() { return Kind==3 ? "申请者 " + Source + " · " + Name
            : Kind==4 ? "邀请者 " + Source + " · " + Name : Name + " · " + Count + "/6 · " + Room.ToString("X8") + " · " + Leader; }
    }
}
