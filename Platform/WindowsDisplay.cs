using System.Runtime.InteropServices;
using System.Text.Json;
namespace Apex.Platform;
public sealed record DisplayMode(string Identity, uint Width, uint Height, uint Bits, uint Orientation, int X, int Y, uint FixedOutput, uint Flags, uint Hertz);
public sealed record DisplayChoice(string Target, string Name, string Current, string? Highest, string? Reason);
public static class WindowsDisplay
{
    public static bool NativeLayoutsValid => Marshal.SizeOf<Mode>() == 220 && Marshal.SizeOf<Device>() == 840 && Marshal.SizeOf<PathInfo>() == 72 && Marshal.SizeOf<ConfigMode>() == 64 && Marshal.SizeOf<SourceName>() == 84 && Marshal.SizeOf<AdvancedColor>() == 32;
    public static List<DisplayChoice> Choices()
    {
        var result = new List<DisplayChoice>();
        for (uint i = 0; i < 64; i++)
        {
            var device = new Device { Size = 840 };
            if (!EnumDisplayDevicesW(null, i, ref device, 0))
                break;
            if ((device.Flags & 1) == 0 || (device.Flags & 8) != 0)
                continue;
            try
            {
                var current = Current(device.Name);
                string identity = Identity(device.Name);
                var mode = Snapshot(current, identity);
                var candidates = Modes(device.Name).Select(m => Snapshot(m, identity)).Where(m => SameConfiguration(mode, m)).ToList();
                var highest = Highest(mode, candidates);
                var trial = WithFrequency(current, highest.Hertz);
                if (ChangeDisplaySettingsExW(device.Name, ref trial, 0, 2, 0) != 0)
                    throw new IOException("Windows rejected the highest reported mode; use Advanced display settings.");
                string name = device.Description;
                result.Add(new(device.Name, name + " · " + device.Name, JsonSerializer.Serialize(mode), JsonSerializer.Serialize(highest), null));
            }
            catch (Exception e) { result.Add(new(device.Name, device.Description, "Unavailable", null, e.Message)); }
        }
        return result;
    }
    public static DisplayMode Highest(DisplayMode current, IEnumerable<DisplayMode> modes) => modes.Where(m => SameConfiguration(current, m) && m.Hertz > 1).Append(current).OrderByDescending(m => m.Hertz).First();
    private static bool SameConfiguration(DisplayMode a, DisplayMode b) => a with { Hertz = 0 } == b with { Hertz = 0 };
    public static string Read(string target) => JsonSerializer.Serialize(Snapshot(Current(target), Identity(target)));
    public static void Write(string target, string value, string expected)
    {
        if (Read(target) != expected)
            throw new IOException("The monitor or display configuration changed; refresh the preview.");
        var requested = JsonSerializer.Deserialize<DisplayMode>(value) ?? throw new IOException("Invalid display mode.");
        var current = Current(target);
        var original = Snapshot(current, Identity(target));
        if (!SameConfiguration(original, requested) || requested.Hertz <= 1 || !Modes(target).Any(m => Snapshot(m, original.Identity) == requested))
            throw new IOException("Only a supported refresh rate at the unchanged resolution and configuration is allowed.");
        var mode = WithFrequency(current, requested.Hertz);
        if (ChangeDisplaySettingsExW(target, ref mode, 0, 2, 0) != 0)
            throw new IOException("The display driver rejected this mode.");
        int result = ChangeDisplaySettingsExW(target, ref mode, 0, 1, 0);
        if (result != 0)
            throw new IOException("Display change failed (" + result + "). No restart was requested.");
    }
    private static Mode WithFrequency(Mode mode, uint hz)
    {
        mode.Frequency = hz;
        mode.Fields |= 0x400000;
        return mode;
    }
    private static Mode Current(string target)
    {
        if (!target.StartsWith(@"\\.\DISPLAY", StringComparison.Ordinal) || target.Length > 32)
            throw new IOException("Invalid display target.");
        var mode = new Mode { Size = 220 };
        if (!EnumDisplaySettingsExW(target, -1, ref mode, 0))
            throw new IOException("Monitor is no longer connected.");
        return mode;
    }
    private static IEnumerable<Mode> Modes(string target)
    {
        var modes = new List<Mode>();
        for (int i = 0; i < 8192; i++)
        {
            var mode = new Mode { Size = 220 };
            if (!EnumDisplaySettingsExW(target, i, ref mode, 0))
                break;
            modes.Add(mode);
        }
        return modes;
    }
    private static DisplayMode Snapshot(Mode m, string identity) => new(identity, m.Width, m.Height, m.Bits, m.Orientation, m.X, m.Y, m.FixedOutput, m.Flags, m.Frequency);
    private static string Identity(string target)
    {
        int error = GetDisplayConfigBufferSizes(2, out uint pathCount, out uint modeCount);
        if (error != 0)
            throw new IOException("Display topology cannot be verified.");
        var paths = new PathInfo[pathCount];
        var modes = new ConfigMode[modeCount];
        error = QueryDisplayConfig(2, ref pathCount, paths, ref modeCount, modes, 0);
        if (error != 0)
            throw new IOException("Display topology changed; refresh first.");
        var matches = new List<PathInfo>();
        foreach (var path in paths.Take((int)pathCount))
        {
            var source = new SourceName { Header = new Header { Type = 1, Size = 84, Adapter = path.Source.Adapter, Id = path.Source.Id } };
            if (SourceInfo(ref source) != 0)
                throw new IOException("Display identity is unavailable.");
            if (source.Name == target)
                matches.Add(path);
        }
        if (matches.Count != 1)
            throw new IOException("Mirrored/unsupported display topology: use Windows Advanced display settings.");
        var match = matches[0];
        var color = new AdvancedColor { Header = new Header { Type = 9, Size = 32, Adapter = match.Target.Adapter, Id = match.Target.Id } };
        if (ColorInfo(ref color) != 0 || (color.Flags & 2) != 0)
            throw new IOException("HDR/advanced color cannot be safely preserved by this mode API; use Windows Advanced display settings.");
        return $"{match.Target.Adapter.Low}:{match.Target.Adapter.High}:{match.Target.Id}:{color.Flags}:{color.Encoding}:{color.Bits}";
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint Low; public int High;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Rational
    {
        public uint Numerator, Denominator;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Source
    {
        public Luid Adapter; public uint Id, ModeIndex, Flags;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Target
    {
        public Luid Adapter; public uint Id, ModeIndex, Technology, Rotation, Scaling; public Rational Refresh; public uint Scanline; public int Available; public uint Flags;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct PathInfo
    {
        public Source Source; public Target Target; public uint Flags;
    }
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct ConfigMode
    {
        [FieldOffset(0)] public uint Type;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Header
    {
        public uint Type, Size; public Luid Adapter; public uint Id;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SourceName
    {
        public Header Header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct AdvancedColor
    {
        public Header Header; public uint Flags, Encoding, Bits;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Device
    {
        public uint Size; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Mode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
        public ushort Spec, Driver, Size, Extra; public uint Fields;
        public int X, Y; public uint Orientation, FixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Form;
        public ushort LogPixels; public uint Bits, Width, Height, Flags, Frequency, IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplayDevicesW(string? device, uint index, ref Device output, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplaySettingsExW(string device, int index, ref Mode mode, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettingsExW(string device, ref Mode mode, nint hwnd, uint flags, nint data);
    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint paths, [In, Out] PathInfo[] path, ref uint modes, [In, Out] ConfigMode[] mode, nint topology);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int SourceInfo(ref SourceName source);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int ColorInfo(ref AdvancedColor source);
}
