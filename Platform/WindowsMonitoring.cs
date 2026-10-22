using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Apex.Services;

namespace Apex.Platform;
public sealed class WindowsMonitoring : IMonitoringService
{
    private ulong previousIdle, previousKernel, previousUser;
    private long previousStamp, previousDetailStamp;
    private readonly Dictionary<int, (long Started, double Cpu)> previousProcesses = [];
    private readonly Dictionary<string, (long Received, long Sent)> previousNetwork = [];
    private readonly PdhCounter diskRead = new("\\PhysicalDisk(_Total)\\Disk Read Bytes/sec");
    private readonly PdhCounter diskWrite = new("\\PhysicalDisk(_Total)\\Disk Write Bytes/sec");
    private readonly PdhCounter gpu = new("\\GPU Engine(*)\\Utilization Percentage");
    public SensorPacket? Sensors
    {
        get; set;
    }
    public static string CpuName
    {
        get
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return key?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "CPU";
        }
    }
    public static string GpuNames
    {
        get
        {
            var names = new List<string>();
            using var devices = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\VIDEO");
            if (devices is not null)
                foreach (var name in devices.GetValueNames())
                {
                    string? path = devices.GetValue(name)?.ToString();
                    const string prefix = @"\Registry\Machine\";
                    if (path?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) != true)
                        continue;
                    using var key = Registry.LocalMachine.OpenSubKey(path[prefix.Length..]);
                    if (key?.GetValue("DriverDesc") is string description)
                        names.Add(description);
                }
            return names.Count == 0 ? "GPU hardware name unavailable" : string.Join(" / ", names.Distinct());
        }
    }
    private readonly SemaphoreSlim sampleGate = new(1, 1);
    public Task<MonitorSample> SampleAsync(CancellationToken token) => SampleAsync(token, true);
    // Totals only (CPU, RAM, GPU) skip the per-process and network walks that dominate sampling cost.
    public async Task<MonitorSample> SampleAsync(CancellationToken token, bool detailed)
    {
        await sampleGate.WaitAsync(token);
        try
        {
            return await Task.Run(() => Sample(token, detailed), token);
        }
        finally { sampleGate.Release(); }
    }
    private MonitorSample Sample(CancellationToken token, bool detailed)
    {
        token.ThrowIfCancellationRequested();
        long stamp = Stopwatch.GetTimestamp();
        double? cpu = null;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            ulong total = kernel + user - previousKernel - previousUser;
            if (previousStamp > 0 && total > 0)
                cpu = Math.Clamp(100 * (1 - (idle - previousIdle) / (double)total), 0, 100);
            previousIdle = idle;
            previousKernel = kernel;
            previousUser = user;
        }
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref memory))
            throw new System.ComponentModel.Win32Exception();
        if (!detailed)
        {
            previousStamp = stamp;
            return new(DateTime.UtcNow, cpu, (long)(memory.TotalPhysical - memory.AvailablePhysical), (long)memory.TotalPhysical, gpu.GpuValue(), null, null, null, null, [], Sensors, null);
        }
        // Process and network deltas use their own clock because totals-only samples run in between.
        double elapsed = previousDetailStamp == 0 ? 0 : (stamp - previousDetailStamp) / (double)Stopwatch.Frequency;
        var processes = new List<ProcessUsage>();
        var live = new HashSet<int>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    long started = process.StartTime.ToUniversalTime().Ticks;
                    double ticks = process.TotalProcessorTime.TotalSeconds;
                    double load = 0;
                    if (elapsed > 0 && previousProcesses.TryGetValue(process.Id, out var prev) && prev.Started == started)
                        load = Math.Clamp((ticks - prev.Cpu) / elapsed / Environment.ProcessorCount * 100, 0, 100);
                    previousProcesses[process.Id] = (started, ticks);
                    live.Add(process.Id);
                    string path = "";
                    bool canClose = false;
                    try
                    {
                        var app = WindowsActions.Describe(process);
                        path = app.Executable;
                        canClose = app.CanClose;
                    }
                    catch { }
                    processes.Add(new(process.Id, started, process.ProcessName, path, load, process.WorkingSet64, canClose));
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or NotSupportedException) { }
            }
        }
        foreach (var id in previousProcesses.Keys.Where(id => !live.Contains(id)).ToArray())
            previousProcesses.Remove(id);
        double received = 0, sent = 0;
        bool available = false;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        {
            try
            {
                var stats = nic.GetIPStatistics();
                if (elapsed > 0 && previousNetwork.TryGetValue(nic.Id, out var last))
                {
                    received += Math.Max(0, stats.BytesReceived - last.Received) / elapsed;
                    sent += Math.Max(0, stats.BytesSent - last.Sent) / elapsed;
                    available = true;
                }
                previousNetwork[nic.Id] = (stats.BytesReceived, stats.BytesSent);
            }
            catch (NetworkInformationException) { }
        }
        previousStamp = previousDetailStamp = stamp;
        return new(DateTime.UtcNow, cpu, (long)(memory.TotalPhysical - memory.AvailablePhysical), (long)memory.TotalPhysical, gpu.GpuValue(), diskRead.Value(), diskWrite.Value(), available ? received : null, available ? sent : null, processes, Sensors, null);
    }
    public void Dispose()
    {
        diskRead.Dispose();
        diskWrite.Dispose();
        gpu.Dispose();
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load; public ulong TotalPhysical, AvailablePhysical, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, AvailableExtended;
    }
}
public sealed class PdhCounter : IDisposable
{
    private nint query, counter;
    private bool collected;
    public PdhCounter(string path)
    {
        if (PdhOpenQuery(null, 0, out query) != 0 || PdhAddEnglishCounter(query, path, 0, out counter) != 0)
            Dispose();
        if (query != 0)
            PdhCollectQueryData(query);
    }
    public double? Value()
    {
        if (query == 0 || PdhCollectQueryData(query) != 0)
            return null;
        if (!collected)
        {
            collected = true;
            return null;
        }
        if (PdhGetFormattedCounterValue(counter, 0x200, out _, out var value) != 0 || value.Status > 1 || !double.IsFinite(value.Value))
            return null;
        return Math.Max(0, value.Value);
    }
    public double? GpuValue()
    {
        if (query == 0 || PdhCollectQueryData(query) != 0)
            return null;
        if (!collected)
        {
            collected = true;
            return null;
        }
        uint bytes = 0, count = 0;
        var code = PdhGetFormattedCounterArray(counter, 0x200, ref bytes, out count, 0);
        if (code != 0x800007D2 || bytes == 0 || bytes > 16_000_000)
            return null;
        nint buffer = Marshal.AllocHGlobal((int)bytes);
        try
        {
            if (PdhGetFormattedCounterArray(counter, 0x200, ref bytes, out count, buffer) != 0)
                return null;
            var groups = new Dictionary<string, double>();
            for (int i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<CounterItem>(buffer + i * Marshal.SizeOf<CounterItem>());
                if (item.Value.Status > 1 || !double.IsFinite(item.Value.Value))
                    continue;
                string name = Marshal.PtrToStringUni(item.Name) ?? "";
                int start = name.IndexOf("luid_", StringComparison.Ordinal);
                if (start < 0)
                    continue;
                string engine = name[start..]; // Sum processes sharing a hardware engine; busiest engine is the device load.
                groups[engine] = groups.GetValueOrDefault(engine) + Math.Max(0, item.Value.Value);
            }
            return groups.Count == 0 ? null : Math.Clamp(groups.Values.Max(), 0, 100);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    public Dictionary<int, double>? GpuProcesses()
    {
        if (query == 0 || PdhCollectQueryData(query) != 0)
            return null;
        if (!collected)
        {
            collected = true;
            return null;
        }
        uint bytes = 0, count = 0;
        var code = PdhGetFormattedCounterArray(counter, 0x200, ref bytes, out count, 0);
        if (code != 0x800007D2 || bytes == 0 || bytes > 16_000_000)
            return null;
        nint buffer = Marshal.AllocHGlobal((int)bytes);
        try
        {
            if (PdhGetFormattedCounterArray(counter, 0x200, ref bytes, out count, buffer) != 0)
                return null;
            var values = new Dictionary<int, double>();
            for (int i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<CounterItem>(buffer + i * Marshal.SizeOf<CounterItem>());
                if (item.Value.Status > 1 || !double.IsFinite(item.Value.Value))
                    continue;
                string name = Marshal.PtrToStringUni(item.Name) ?? "";
                if (!name.StartsWith("pid_", StringComparison.Ordinal))
                    continue;
                int end = name.IndexOf('_', 4);
                if (end < 0 || !int.TryParse(name.AsSpan(4, end - 4), out int pid))
                    continue;
                values[pid] = Math.Max(values.GetValueOrDefault(pid), Math.Clamp(item.Value.Value, 0, 100));
            }
            return values;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    public void Dispose()
    {
        if (query != 0)
            PdhCloseQuery(query);
        query = 0;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct CounterValue
    {
        public uint Status; public double Value;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct CounterItem
    {
        public nint Name; public CounterValue Value;
    }
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQuery(string? source, nuint user, out nint query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhAddEnglishCounterW")] private static extern uint PdhAddEnglishCounter(nint query, string path, nuint user, out nint counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(nint query);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(nint counter, uint format, out uint type, out CounterValue value);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhGetFormattedCounterArrayW")] private static extern uint PdhGetFormattedCounterArray(nint counter, uint format, ref uint bytes, out uint count, nint buffer);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(nint query);
}
