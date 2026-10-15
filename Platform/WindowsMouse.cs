using System.Runtime.InteropServices;
using System.Text.Json;
namespace Apex.Platform;
public sealed record MouseParameters(int Threshold1, int Threshold2, int Acceleration);
public static class WindowsMouse
{
    public static string Read()
    {
        var values = new int[3];
        Check(Parameter(3, 0, values, 0));
        return JsonSerializer.Serialize(new MouseParameters(values[0], values[1], values[2]));
    }
    public static string WithoutAcceleration(string original) => JsonSerializer.Serialize(Parse(original) with { Acceleration = 0 });
    public static MouseParameters Parse(string value)
    {
        var mouse = JsonSerializer.Deserialize<MouseParameters>(value) ?? throw new IOException("Invalid mouse settings.");
        if (mouse.Threshold1 < 0 || mouse.Threshold2 < 0 || mouse.Acceleration is < 0 or > 2)
            throw new IOException("Invalid mouse settings.");
        return mouse;
    }
    public static void Write(string value)
    {
        var mouse = Parse(value);
        Check(Parameter(4, 0, [mouse.Threshold1, mouse.Threshold2, mouse.Acceleration], 3));
    }
    private static void Check(bool ok)
    {
        if (!ok)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool Parameter(uint action, uint param, [In, Out] int[] value, uint flags);
}
