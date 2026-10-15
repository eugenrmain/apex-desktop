using System.Diagnostics;
using System.Runtime.InteropServices;
using Apex.Services;
namespace Apex.Platform;
// Uses an owned, time-bounded Windows kernel session. Only PID and byte count are read;
// addresses, ports and packet contents never leave the native event buffer.
public static class WindowsNetworkTrace
{
    public static bool LayoutsValid => Marshal.SizeOf<Properties>() == 120 && Marshal.SizeOf<LogFile>() == 448;
    public static async Task<(List<NetworkRate> Rates, int Lost)> CaptureAsync(IReadOnlyList<NetworkProcess> requested, CancellationToken token)
    {
        if (!LayoutsValid || requested.Count > 10000)
            throw new IOException("Unsupported network trace layout or process count.");
        EnableProfilePrivilege();
        string name = "APX.Network." + Guid.NewGuid().ToString("N");
        var identities = requested.DistinctBy(p => p.Id).Where(Alive).ToDictionary(p => p.Id, p => p.Started);
        var bytes = identities.Keys.ToDictionary(p => p, _ => 0L);
        var tcp = Guid.Parse("9a280ac0-c8e0-11d1-84e2-00c04fb998a2");
        var udp = Guid.Parse("bf3a50c5-a9c9-4988-a005-2df0b7c80f80");
        EventCallback callback = record =>
        {
            try
            {
                var provider = Marshal.PtrToStructure<Guid>(record + 24);
                int opcode = Marshal.ReadByte(record, 45);
                if ((provider != tcp && provider != udp) || opcode is not (10 or 11 or 26 or 27) || (ushort)Marshal.ReadInt16(record, 86) < 8)
                    return;
                nint data = Marshal.ReadIntPtr(record, 96);
                if (data == 0)
                    return;
                int pid = Marshal.ReadInt32(data);
                uint size = unchecked((uint)Marshal.ReadInt32(data, 4));
                if (bytes.TryGetValue(pid, out long total))
                    bytes[pid] = checked(total + size);
            }
            catch { /* Malformed or unrequested events do not contribute to usage. */ }
        };
        int propertySize = Marshal.SizeOf<Properties>();
        nint properties = Marshal.AllocHGlobal(propertySize + (name.Length + 1) * 2);
        ulong session = 0, trace = ulong.MaxValue;
        Task? processing = null;
        GCHandle callbackRoot = GCHandle.Alloc(callback);
        for (int i = 0; i < propertySize + (name.Length + 1) * 2; i++)
            Marshal.WriteByte(properties, i, 0);
        var settings = new Properties { Wnode = new() { BufferSize = (uint)(propertySize + (name.Length + 1) * 2), Guid = Guid.NewGuid(), ClientContext = 1, Flags = 0x20000 }, BufferSize = 64, MinimumBuffers = 2, MaximumBuffers = 16, LogFileMode = 0x02000100, FlushTimer = 1, EnableFlags = 0x10000, LoggerNameOffset = (uint)propertySize };
        Marshal.StructureToPtr(settings, properties, false);
        Marshal.Copy((name + '\0').ToCharArray(), 0, properties + propertySize, name.Length + 1);
        try
        {
            uint error = StartTrace(out session, name, properties);
            if (error != 0)
                throw new IOException("Network scan could not start: " + new System.ComponentModel.Win32Exception((int)error).Message);
            var log = new LogFile { LoggerName = name, ProcessTraceMode = 0x10000100, EventRecordCallback = Marshal.GetFunctionPointerForDelegate(callback) };
            trace = OpenTrace(ref log);
            if (trace == ulong.MaxValue)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            ulong consumer = trace;
            var watch = Stopwatch.StartNew();
            processing = Task.Run(() => { uint code = ProcessTrace([consumer], 1, 0, 0); if (code is not (0 or 1223)) throw new IOException("Network trace ended: " + code); });
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);
            }
            finally { watch.Stop(); uint stopped = ControlTrace(session, name, properties, 1); if (stopped == 0 || stopped == 4201) session = 0; else throw new IOException("Could not stop the owned network session: " + stopped); }
            await processing.WaitAsync(TimeSpan.FromSeconds(5));
            token.ThrowIfCancellationRequested();
            var final = Marshal.PtrToStructure<Properties>(properties);
            return (identities.Where(p => Alive(new(p.Key, p.Value))).Select(p => new NetworkRate(p.Key, p.Value, bytes[p.Key] / Math.Max(.001, watch.Elapsed.TotalSeconds))).ToList(), (int)(final.EventsLost + final.RealTimeBuffersLost));
        }
        finally
        {
            if (session != 0)
                ControlTrace(session, name, properties, 1);
            if (trace != ulong.MaxValue)
                CloseTrace(trace);
            if (processing is not null)
            {
                try
                {
                    await processing.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch { }
            }
            // CloseTrace can return before ProcessTrace finishes. Keep the native callback
            // rooted until that worker has actually exited, even after a stop timeout.
            if (processing is not null && !processing.IsCompleted)
                _ = processing.ContinueWith(_ => callbackRoot.Free(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            else
                callbackRoot.Free();
            Marshal.FreeHGlobal(properties);
        }
    }
    private static bool Alive(NetworkProcess identity)
    {
        try
        {
            using var p = Process.GetProcessById(identity.Id);
            return !p.HasExited && p.StartTime.ToUniversalTime().Ticks == identity.Started;
        }
        catch { return false; }
    }
    private static void EnableProfilePrivilege()
    {
        if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 0x28, out nint handle))
            throw new System.ComponentModel.Win32Exception();
        try
        {
            if (!LookupPrivilegeValue(null, "SeSystemProfilePrivilege", out var luid))
                throw new System.ComponentModel.Win32Exception();
            var state = new Privileges { Count = 1, Low = luid.Low, High = luid.High, Attributes = 2 };
            if (!AdjustTokenPrivileges(handle, false, ref state, 0, 0, 0) || Marshal.GetLastWin32Error() != 0)
                throw new IOException("Administrator permission is required for network usage.");
        }
        finally { CloseHandle(handle); }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Wnode
    {
        public uint BufferSize, ProviderId; public ulong HistoricalContext; public long TimeStamp; public Guid Guid; public uint ClientContext, Flags;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Properties
    {
        public Wnode Wnode; public uint BufferSize, MinimumBuffers, MaximumBuffers, MaximumFileSize, LogFileMode, FlushTimer, EnableFlags; public int AgeLimit; public uint NumberOfBuffers, FreeBuffers, EventsLost, BuffersWritten, LogBuffersLost, RealTimeBuffersLost; public nint LoggerThreadId; public uint LogFileNameOffset, LoggerNameOffset;
    }
    [StructLayout(LayoutKind.Explicit, Size = 448, CharSet = CharSet.Unicode)]
    private struct LogFile
    {
        [FieldOffset(0)] public nint LogFileName; [FieldOffset(8)][MarshalAs(UnmanagedType.LPWStr)] public string? LoggerName; [FieldOffset(28)] public uint ProcessTraceMode; [FieldOffset(424)] public nint EventRecordCallback;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint Low; public int High;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Privileges
    {
        public uint Count, Low; public int High; public uint Attributes;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void EventCallback(nint record);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "StartTraceW")] private static extern uint StartTrace(out ulong session, string name, nint properties);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "ControlTraceW")] private static extern uint ControlTrace(ulong session, string name, nint properties, uint control);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "OpenTraceW", SetLastError = true)] private static extern ulong OpenTrace(ref LogFile file);
    [DllImport("advapi32.dll")] private static extern uint ProcessTrace(ulong[] handles, uint count, nint start, nint end);
    [DllImport("advapi32.dll")] private static extern uint CloseTrace(ulong handle);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool LookupPrivilegeValue(string? system, string name, out Luid luid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool AdjustTokenPrivileges(nint token, bool disable, ref Privileges state, uint length, nint previous, nint returned);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
}
