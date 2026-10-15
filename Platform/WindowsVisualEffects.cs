using System.Runtime.InteropServices;
namespace Apex.Platform;
public static class WindowsVisualEffects
{
    public static readonly IReadOnlyDictionary<string, (uint Get, uint Set, string Label)> Supported = new Dictionary<string, (uint, uint, string)>
    {
        ["ClientAnimation"] = (0x1042, 0x1043, "Animate controls and elements inside windows"),
        ["WindowAnimation"] = (0x0048, 0x0049, "Animate windows when minimizing and maximizing"),
        ["MenuAnimation"] = (0x1002, 0x1003, "Fade or slide menus into view"),
        ["ComboAnimation"] = (0x1004, 0x1005, "Slide open combo boxes"),
        ["TooltipAnimation"] = (0x1016, 0x1017, "Fade or slide ToolTips into view"),
        ["MenuFade"] = (0x1012, 0x1013, "Menu fade transition"),
        ["SelectionFade"] = (0x1014, 0x1015, "Fade out menu items after clicking"),
        ["CursorShadow"] = (0x101A, 0x101B, "Show shadows under mouse pointer"),
        ["DropShadow"] = (0x1024, 0x1025, "Show shadows under windows (supported legacy windows)"),
        ["DragFullWindows"] = (0x0026, 0x0025, "Show window contents while dragging"),
        ["FontSmoothing"] = (0x004A, 0x004B, "Smooth edges of screen fonts"),
        ["ListSmooth"] = (0x1006, 0x1007, "Smooth-scroll list boxes")
    };
    public static readonly string[] GuidedDisabled = ["Animations in the taskbar", "Enable Peek", "Save taskbar thumbnail previews", "Show shadows under windows (remaining Windows surfaces)", "Show translucent selection rectangle", "Use drop shadows for icon labels on the desktop"];
    public static string PresetValue(string target) => target == "FontSmoothing" ? "Enabled" : "Disabled";
    public static string Read(string target)
    {
        if (!Supported.TryGetValue(target, out var item))
            throw new IOException("Unsupported visual effect.");
        if (target == "WindowAnimation")
        {
            var animation = new Animation { Size = 8 };
            Check(AnimationParameter(item.Get, 8, ref animation, 0));
            return animation.MinAnimate != 0 ? "Enabled" : "Disabled";
        }
        if (!GetParameter(item.Get, 0, out int enabled, 0))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return enabled != 0 ? "Enabled" : "Disabled";
    }
    public static void Write(string target, string value)
    {
        if (!Supported.TryGetValue(target, out var item) || value is not ("Enabled" or "Disabled"))
            throw new IOException("Unsupported visual effect or value.");
        bool enabled = value == "Enabled";
        if (target == "WindowAnimation")
        {
            var animation = new Animation { Size = 8, MinAnimate = enabled ? 1 : 0 };
            Check(AnimationParameter(item.Set, 8, ref animation, 3));
        }
        else if (target is "DragFullWindows" or "FontSmoothing")
            Check(SetParameter(item.Set, enabled ? 1u : 0u, 0, 3));
        else
            Check(SetParameter(item.Set, 0, enabled ? (nint)1 : 0, 3));
        // Preserve existing font smoothing type, contrast, orientation and ClearType preference.
    }
    private static void Check(bool ok)
    {
        if (!ok)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Animation
    {
        public uint Size; public int MinAnimate;
    }
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool AnimationParameter(uint action, uint param, ref Animation value, uint flags);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetParameter(uint action, uint param, out int value, uint flags);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetParameter(uint action, uint param, nint value, uint flags);
}
