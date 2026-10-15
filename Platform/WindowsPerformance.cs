using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Apex.Services;

namespace Apex.Platform;
public sealed record PowerScheme(Guid Id, string Name)
{
    public override string ToString() => Name;
}
public sealed record StartupEntry(string Target, string Label, string Command, PlanActionKind Kind)
{
    public override string ToString() => Label;
}
public sealed class WindowsPerformance : IPerformanceProvider
{
    public static List<PowerScheme> PowerSchemes()
    {
        var list = new List<PowerScheme>();
        for (uint i = 0; i < 100; i++)
        {
            uint size = 16;
            byte[] bytes = new byte[16];
            uint error = PowerEnumerate(0, 0, 0, 16, i, bytes, ref size);
            if (error == 259)
                break;
            if (error != 0)
                throw new System.ComponentModel.Win32Exception((int)error);
            var id = new Guid(bytes);
            uint nameSize = 0;
            PowerReadFriendlyName(0, ref id, 0, 0, null, ref nameSize);
            if (nameSize is 0 or > 8192)
                continue;
            byte[] name = new byte[nameSize];
            if (PowerReadFriendlyName(0, ref id, 0, 0, name, ref nameSize) == 0)
                list.Add(new(id, Encoding.Unicode.GetString(name).TrimEnd('\0')));
        }
        return list;
    }
    public static List<StartupEntry> StartupEntries()
    {
        var list = new List<StartupEntry>();
        using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (run is not null)
            foreach (var name in run.GetValueNames())
            {
                if (run.GetValueKind(name) is not (RegistryValueKind.String or RegistryValueKind.ExpandString))
                    continue;
                string command = run.GetValue(name, "", RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? "";
                list.Add(new(name, name + " (registered at sign-in)", command, PlanActionKind.StartupRun));
            }
        string folder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        if (Directory.Exists(folder))
            foreach (var path in Directory.EnumerateFiles(folder, "*.lnk"))
                list.Add(new(path, Path.GetFileName(path), path, PlanActionKind.StartupShortcut));
        return list;
    }
    public static List<NetworkInterface> Adapters() => NetworkInterface.GetAllNetworkInterfaces().Where(n => n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 && Guid.TryParse(n.Id, out _)).ToList();
    public Task<string> ReadAsync(PlanAction action, CancellationToken token) => Task.Run(() => Read(action), token);
    public Task WriteAsync(PlanAction action, string value, string expected, CancellationToken token) => Task.Run(() => Write(action, value, expected), token);
    public static string Read(PlanAction action)
    {
        switch (action.Kind)
        {
            case PlanActionKind.PowerPlan:
                Check(PowerGetActiveScheme(0, out var pointer));
                try
                {
                    return Marshal.PtrToStructure<Guid>(pointer).ToString();
                }
                finally { LocalFree(pointer); }
            case PlanActionKind.Priority:
                using (var process = SelectedProcess(action))
                    return process.PriorityClass.ToString();
            case PlanActionKind.StartupRun:
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (key is null || !key.GetValueNames().Contains(action.Target, StringComparer.OrdinalIgnoreCase))
                        return "Disabled";
                    var kind = key.GetValueKind(action.Target);
                    if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString))
                        throw new IOException("Unsupported startup value.");
                    return JsonSerializer.Serialize(new StartupValue(key.GetValue(action.Target, "", RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? "", (int)kind));
                }
            case PlanActionKind.StartupShortcut:
                ValidateShortcut(action.Target);
                if (!File.Exists(action.Target))
                    return "Disabled";
                return Convert.ToBase64String(File.ReadAllBytes(action.Target));
            case PlanActionKind.VisualEffect:
                return WindowsVisualEffects.Read(action.Target);
            case PlanActionKind.Mouse:
                if (action.Target != "Pointer")
                    throw new IOException("Unsupported mouse target.");
                return WindowsMouse.Read();
            case PlanActionKind.Display:
                return WindowsDisplay.Read(action.Target);
            case PlanActionKind.Dns:
                return JsonSerializer.Serialize(ReadDns(action.Target));
            default:
                throw new IOException("This step is manual.");
        }
    }
    public static void Write(PlanAction action, string value, string expected)
    {
        if (Read(action) != expected)
            throw new IOException("The setting changed after preview; refresh before applying.");
        switch (action.Kind)
        {
            case PlanActionKind.PowerPlan:
                if (!Guid.TryParse(value, out var scheme) || !PowerSchemes().Any(p => p.Id == scheme))
                    throw new IOException("Select an existing power plan.");
                Check(PowerSetActiveScheme(0, ref scheme));
                break;
            case PlanActionKind.Priority:
                if (!Enum.TryParse<ProcessPriorityClass>(value, out var priority) || priority is not (ProcessPriorityClass.Idle or ProcessPriorityClass.BelowNormal or ProcessPriorityClass.Normal or ProcessPriorityClass.AboveNormal))
                    throw new IOException("Unsupported process priority.");
                using (var process = SelectedProcess(action))
                    process.PriorityClass = priority;
                break;
            case PlanActionKind.StartupRun:
                if (action.Target.Length is 0 or > 256 || action.Target.Contains('\0'))
                    throw new IOException("Invalid startup item.");
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (value == "Disabled")
                        key.DeleteValue(action.Target, false);
                    else
                    {
                        var item = JsonSerializer.Deserialize<StartupValue>(value) ?? throw new IOException("Invalid startup record.");
                        if (item.Kind is not (1 or 2) || item.Value.Length > 32768)
                            throw new IOException("Invalid startup record.");
                        key.SetValue(action.Target, item.Value, (RegistryValueKind)item.Kind);
                    }
                }
                break;
            case PlanActionKind.StartupShortcut:
                ValidateShortcut(action.Target);
                if (value == "Disabled")
                    File.Delete(action.Target);
                else
                {
                    var bytes = Convert.FromBase64String(value);
                    if (bytes.Length > 1_000_000)
                        throw new IOException("Invalid startup shortcut.");
                    using var file = new FileStream(action.Target, FileMode.CreateNew);
                    file.Write(bytes);
                }
                break;
            case PlanActionKind.VisualEffect:
                WindowsVisualEffects.Write(action.Target, value);
                break;
            case PlanActionKind.Mouse:
                WindowsMouse.Write(value);
                break;
            case PlanActionKind.Display:
                WindowsDisplay.Write(action.Target, value, expected);
                break;
            case PlanActionKind.Dns:
                var choice = JsonSerializer.Deserialize<DnsChoice>(value) ?? throw new IOException("Invalid DNS selection.");
                ValidateDns(choice);
                var before = ReadDns(action.Target);
                var id = Guid.Parse(action.Target);
                try
                {
                    SetDns(id, choice.IPv4, false);
                    SetDns(id, choice.IPv6, true);
                }
                catch { try { SetDns(id, before.IPv4, false); SetDns(id, before.IPv6, true); } catch { } throw; }
                break;
            default:
                throw new IOException("This step must be changed manually.");
        }
    }
    public static DnsChoice NormalizeDns(string ipv4, string ipv6)
    {
        string Normalize(string input, AddressFamily family)
        {
            var addresses = input.Split([' ', ',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (addresses.Length > 8)
                throw new IOException("Use at most eight DNS servers per address family.");
            return string.Join(",", addresses.Select(s => IPAddress.TryParse(s, out var ip) && ip.AddressFamily == family && !IPAddress.IsLoopback(ip) && !ip.Equals(IPAddress.Any) && !ip.Equals(IPAddress.IPv6Any) ? ip.ToString() : throw new IOException("Enter valid DNS server addresses in the correct IPv4/IPv6 field.")));
        }
        return new(Normalize(ipv4, AddressFamily.InterNetwork), Normalize(ipv6, AddressFamily.InterNetworkV6));
    }
    private static void ValidateDns(DnsChoice choice)
    {
        if (NormalizeDns(choice.IPv4, choice.IPv6) != choice)
            throw new IOException("DNS addresses must be normalized.");
    }
    public static DnsChoice ReadDns(string target)
    {
        if (!Guid.TryParse(target, out var id))
            throw new IOException("Choose a network adapter.");
        var nic = Adapters().FirstOrDefault(n => Guid.TryParse(n.Id, out var candidate) && candidate == id) ?? throw new IOException("The selected adapter is unavailable.");
        if (nic.Description.Contains("VPN", StringComparison.OrdinalIgnoreCase) || nic.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) || nic.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Managed, VPN, and virtual adapters are not modified.");
        using var policy = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows NT\DNSClient");
        using var doh = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters\Interfaces\{" + id + @"}\DohInterfaceSettings");
        using var interfaceDoh = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters\{" + id + @"}\DohInterfaceSettings");
        if (policy is { ValueCount: > 0 } || policy is { SubKeyCount: > 0 } || doh is { SubKeyCount: > 0 } || doh is { ValueCount: > 0 } || interfaceDoh is { SubKeyCount: > 0 } || interfaceDoh is { ValueCount: > 0 })
            throw new IOException("Managed/encrypted DNS configuration cannot be faithfully restored; use Windows Settings.");
        var settings = new DnsSettings3 { Version = 3 };
        Check(GetInterfaceDnsSettings(id, ref settings));
        try
        {
            if (settings.ServerPropertiesCount != 0 || settings.ProfileServerPropertiesCount != 0)
                throw new IOException("Encrypted DNS configuration cannot be faithfully restored; use Windows Settings.");
            if (settings.ProfileNameServer != 0 && !string.IsNullOrEmpty(Marshal.PtrToStringUni(settings.ProfileNameServer)))
                throw new IOException("Profile-specific DNS is not modified.");
        }
        finally { FreeInterfaceDnsSettings(ref settings); }
        string Static(string service)
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + service + @"\Parameters\Interfaces\{" + id + "}");
            return key?.GetValue("NameServer")?.ToString() ?? "";
        }
        return NormalizeDns(Static("Tcpip"), Static("Tcpip6"));
    }
    private static void SetDns(Guid id, string addresses, bool ipv6)
    {
        var memory = Marshal.StringToHGlobalUni(addresses);
        try
        {
            var settings = new DnsSettings { Version = 1, Flags = 2UL | (ipv6 ? 1UL : 0), NameServer = memory };
            Check(SetInterfaceDnsSettings(id, ref settings));
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    private static Process SelectedProcess(PlanAction action)
    {
        if (!int.TryParse(action.Target, out int id))
            throw new IOException("Invalid process.");
        var process = Process.GetProcessById(id);
        try
        {
            var current = WindowsActions.Describe(process);
            if (!current.CanClose || current.Started != action.ProcessStarted)
                throw new IOException("The process changed or is protected.");
            return process;
        }
        catch { process.Dispose(); throw; }
    }
    private static void ValidateShortcut(string path)
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase) || !Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose a current-user Startup shortcut.");
        FileToolkit.NoLinks(path);
        if (File.Exists(path) && new FileInfo(path).Length > 1_000_000)
            throw new IOException("Startup shortcut exceeds the supported size.");
    }
    private sealed record StartupValue(string Value, int Kind);
    private static void Check(uint code)
    {
        if (code != 0)
            throw new System.ComponentModel.Win32Exception((int)code);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct DnsSettings
    {
        public uint Version; public ulong Flags; public nint Domain, NameServer, SearchList; public uint RegistrationEnabled, RegisterAdapterName, EnableLLMNR, QueryAdapterName; public nint ProfileNameServer;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct DnsSettings3
    {
        public uint Version; public ulong Flags; public nint Domain, NameServer, SearchList; public uint RegistrationEnabled, RegisterAdapterName, EnableLLMNR, QueryAdapterName; public nint ProfileNameServer; public uint DisableUnconstrainedQueries; public nint SupplementalSearchList; public uint ServerPropertiesCount; public nint ServerProperties; public uint ProfileServerPropertiesCount; public nint ProfileServerProperties;
    }
    [DllImport("iphlpapi.dll")] private static extern uint GetInterfaceDnsSettings(Guid id, ref DnsSettings3 settings);
    [DllImport("iphlpapi.dll")] private static extern void FreeInterfaceDnsSettings(ref DnsSettings3 settings);
    [DllImport("iphlpapi.dll")] private static extern uint SetInterfaceDnsSettings(Guid id, ref DnsSettings settings);
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(nint root, out nint scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(nint root, ref Guid scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerEnumerate(nint root, nint scheme, nint subgroup, uint access, uint index, byte[] buffer, ref uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(nint root, ref Guid scheme, nint subgroup, nint setting, byte[]? buffer, ref uint size);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
}
