using System.Runtime.InteropServices;
using System.Text;
namespace Apex.Platform;
public sealed record RamIdentity(string Name, long InstalledBytes);
public static class RamHardware
{
    public static readonly Lazy<RamIdentity> Identity = new(Read);
    private static RamIdentity Read()
    {
        try
        {
            uint size = GetSystemFirmwareTable(0x52534D42, 0, null, 0);
            if (size < 8 || size > 1048576)
                return new("RAM hardware details unavailable", 0);
            byte[] bytes = new byte[size];
            if (GetSystemFirmwareTable(0x52534D42, 0, bytes, size) != size)
                return new("RAM hardware details unavailable", 0);
            return Parse(bytes);
        }
        catch { return new("RAM hardware details unavailable", 0); }
    }
    public static RamIdentity Parse(byte[] bytes)
    {
        var names = new List<string>();
        long capacity = 0;
        for (int offset = 8; offset + 4 <= bytes.Length;)
        {
            int length = bytes[offset + 1];
            if (length < 4 || offset + length > bytes.Length)
                break;
            int end = offset + length;
            var strings = new List<string>();
            while (end + 1 < bytes.Length && !(bytes[end] == 0 && bytes[end + 1] == 0))
            {
                int first = end;
                while (end < bytes.Length && bytes[end] != 0)
                    end++;
                strings.Add(Encoding.ASCII.GetString(bytes, first, end - first).Trim());
                if (end + 1 < bytes.Length && bytes[end + 1] == 0)
                    break;
                end++;
            }
            string StringAt(int field)
            {
                int id = field < length ? bytes[offset + field] : 0;
                return id > 0 && id <= strings.Count ? strings[id - 1] : "";
            }
            if (bytes[offset] == 17 && length >= 23)
            {
                ushort raw = BitConverter.ToUInt16(bytes, offset + 12);
                long size = raw == 0x7fff && length >= 32 ? BitConverter.ToUInt32(bytes, offset + 28) * 1048576L : raw is 0 or 0xffff ? 0 : (raw & 0x7fff) * (raw >= 0x8000 ? 1024L : 1048576L);
                if (size > 0)
                {
                    capacity += size;
                    string type = bytes[offset + 18] switch
                    {
                        0x22 => "DDR5",
                        0x1a => "DDR4",
                        0x18 => "DDR3",
                        _ => "RAM"
                    };
                    int speed = length >= 34 ? BitConverter.ToUInt16(bytes, offset + 32) : 0;
                    if (speed == 0)
                        speed = BitConverter.ToUInt16(bytes, offset + 21);
                    names.Add(string.Join(" ", new[] { StringAt(23), StringAt(26), type, speed > 0 && speed != 65535 ? speed + " MT/s" : "" }.Where(s => s.Length > 0)));
                }
            }
            if (bytes[offset] == 127)
                break;
            // end currently points to the first terminator of the double NUL.
            offset = end + 2;
        }
        return new(names.Count > 0 ? string.Join(" / ", names.Distinct()) : "RAM hardware details unavailable", capacity);
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetSystemFirmwareTable(uint provider, uint id, [Out] byte[]? buffer, uint size);
}
