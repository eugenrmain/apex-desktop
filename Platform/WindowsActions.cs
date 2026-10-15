using System.Diagnostics;
using System.Runtime.InteropServices;
using Apex.Services;

namespace Apex.Platform;

public sealed record AppTask(int Id, long Started, string Name, string Executable, bool CanClose)
{
    public override string ToString() => $"{Name} · PID {Id}" + (CanClose ? "" : " · protected");
}
public static class WindowsActions
{
    public static void Delete(Entry file, bool permanent, nint owner)
    {
        FileToolkit.ValidateMutation(file.Path);
        FileToolkit.Recheck(file);
        if (permanent)
        {
            File.Delete(file.Path);
            return;
        }
        object? operationObject = null;
        IShellItem? item = null;
        try
        {
            operationObject = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"))!);
            var operation = (IFileOperation)operationObject!;
            operation.SetOwnerWindow(owner);
            operation.SetOperationFlags(0x00080000 | 0x00100000 | 0x20000000 | 0x0004 | 0x0010 | 0x0400 | 0x2000);
            var guid = typeof(IShellItem).GUID;
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(file.Path, 0, ref guid, out item));
            var sink = new RecycleSink(file);
            operation.DeleteItem(item, sink);
            operation.PerformOperations();
            operation.GetAnyOperationsAborted(out bool aborted);
            if (sink.Error < 0)
                Marshal.ThrowExceptionForHR(sink.Error);
            if (aborted || File.Exists(file.Path))
                throw new IOException("Windows could not recycle the file. It may be locked, inaccessible, or on a volume without a Recycle Bin. No permanent-delete fallback was used.");
        }
        finally
        {
            if (item is not null)
                Marshal.ReleaseComObject(item);
            if (operationObject is not null)
                Marshal.ReleaseComObject(operationObject);
        }
    }
    public static void ClearReadOnly(Entry entry)
    {
        FileToolkit.ValidateMutation(entry.Path);
        FileToolkit.Recheck(entry);
        File.SetAttributes(entry.Path, File.GetAttributes(entry.Path) & ~FileAttributes.ReadOnly);
    }
    public static List<AppTask> Tasks() => Process.GetProcesses().Select(p =>
    {
        using (p)
        {
            try
            {
                return p.MainWindowHandle != 0 ? Describe(p) : null;
            }
            catch { return null; }
        }
    }).OfType<AppTask>().OrderBy(p => p.Name).ToList();
    public static AppTask Describe(Process process)
    {
        var executable = process.MainModule?.FileName ?? "";
        bool allowed = executable.Length > 0 && process.Id != Environment.ProcessId && process.SessionId == Process.GetCurrentProcess().SessionId
            && !FileToolkit.IsWithin(executable, Environment.GetFolderPath(Environment.SpecialFolder.Windows))
            && IsProcessCritical(process.Handle, out bool critical) && !critical;
        return new(process.Id, process.StartTime.ToUniversalTime().Ticks, process.ProcessName, executable, allowed);
    }
    public static async Task<bool> CloseAsync(AppTask selected, bool force)
    {
        using var process = Process.GetProcessById(selected.Id);
        var current = Describe(process);
        if (!current.CanClose || current.Started != selected.Started || current.Executable != selected.Executable)
            throw new IOException("The selected process changed or is protected. Refresh the task list.");
        if (force)
            process.Kill(false);
        else if (!process.CloseMainWindow())
            return false;
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            return true;
        }
        catch (TimeoutException) { return false; }
    }
    public static List<AppTask> Locks(Entry file)
    {
        FileToolkit.Recheck(file);
        uint session;
        var key = Guid.NewGuid().ToString("N");
        int error = RmStartSession(out session, 0, key);
        if (error != 0)
            throw new IOException("Lock diagnostics failed: " + error);
        try
        {
            error = RmRegisterResources(session, 1, [file.Path], 0, null, 0, null);
            if (error != 0)
                throw new IOException("Lock diagnostics failed: " + error);
            uint needed = 0, count = 0, reason = 0;
            for (int retry = 0; retry < 4; retry++)
            {
                var buffer = count == 0 ? null : new RmProcessInfo[count];
                error = RmGetList(session, out needed, ref count, buffer, ref reason);
                if (error == 234)
                {
                    count = needed;
                    continue;
                }
                if (error != 0)
                    throw new IOException("Lock diagnostics failed: " + error);
                var result = new List<AppTask>();
                foreach (var info in (buffer ?? []).Take((int)count))
                {
                    try
                    {
                        using var process = Process.GetProcessById(info.Process.Id);
                        var task = Describe(process);
                        long started = ((long)info.Process.Start.High << 32) | (uint)info.Process.Start.Low;
                        if (process.StartTime.ToUniversalTime().ToFileTimeUtc() != started)
                            continue;
                        result.Add(task with
                        {
                            CanClose = task.CanClose && info.Type is not 3 and not 1000
                        });
                    }
                    catch { result.Add(new(info.Process.Id, 0, info.Name, "", false)); }
                }
                return result;
            }
            throw new IOException("Locking apps changed during inspection. Try again.");
        }
        finally { RmEndSession(session); }
    }
    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsProcessCritical(nint process, [MarshalAs(UnmanagedType.Bool)] out bool critical);
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeTime
    {
        public int Low; public int High;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct UniqueProcess
    {
        public int Id; public NativeTime Start;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RmProcessInfo
    {
        public UniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Service;
        public int Type; public uint Status; public uint Session; [MarshalAs(UnmanagedType.Bool)] public bool Restartable;
    }
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern int RmStartSession(out uint handle, uint flags, string key);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern int RmRegisterResources(uint handle, uint files, string[] names, uint apps, UniqueProcess[]? processes, uint services, string[]? serviceNames);
    [DllImport("rstrtmgr.dll")] private static extern int RmGetList(uint handle, out uint needed, ref uint count, [In, Out] RmProcessInfo[]? apps, ref uint reasons);
    [DllImport("rstrtmgr.dll")] private static extern int RmEndSession(uint handle);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] private static extern int SHCreateItemFromParsingName(string path, nint context, ref Guid iid, out IShellItem item);

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItem
    {
    }
    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        void Advise(IFileOperationProgressSink sink, out uint cookie); void Unadvise(uint cookie);
        void SetOperationFlags(uint flags); void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message); void SetProgressDialog(nint dialog); void SetProperties(nint properties); void SetOwnerWindow(nint owner);
        void ApplyPropertiesToItem(IShellItem item); void ApplyPropertiesToItems(nint items);
        void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink? sink); void RenameItems(nint items, [MarshalAs(UnmanagedType.LPWStr)] string name);
        void MoveItem(IShellItem item, IShellItem folder, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink? sink); void MoveItems(nint items, IShellItem folder);
        void CopyItem(IShellItem item, IShellItem folder, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink? sink); void CopyItems(nint items, IShellItem folder);
        void DeleteItem(IShellItem item, IFileOperationProgressSink? sink); void DeleteItems(nint items);
        void NewItem(IShellItem folder, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string template, IFileOperationProgressSink? sink);
        void PerformOperations(); void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }
    [Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), ComVisible(true)]
    public interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations(); [PreserveSig] int FinishOperations(int result);
        [PreserveSig] int PreRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem? created);
        [PreserveSig] int PreMoveItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostMoveItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem? created);
        [PreserveSig] int PreCopyItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostCopyItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem? created);
        [PreserveSig] int PreDeleteItem(uint flags, IShellItem item); [PreserveSig] int PostDeleteItem(uint flags, IShellItem item, int result, IShellItem? created);
        [PreserveSig] int PreNewItem(uint flags, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostNewItem(uint flags, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string template, uint attributes, int result, IShellItem? created);
        [PreserveSig] int UpdateProgress(uint total, uint completed); [PreserveSig] int ResetTimer(); [PreserveSig] int PauseTimer(); [PreserveSig] int ResumeTimer();
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class RecycleSink(Entry file) : IFileOperationProgressSink
    {
        public int Error
        {
            get; private set;
        }
        public int PreDeleteItem(uint flags, IShellItem item)
        {
            // Refuse shell's permanent-delete fallback when recycling is unavailable.
            if ((flags & 0x80) == 0)
                return Error = unchecked((int)0x80004005);
            try
            {
                FileToolkit.ValidateMutation(file.Path);
                FileToolkit.Recheck(file);
                return 0;
            }
            catch { return Error = unchecked((int)0x80004005); }
        }
        public int PostDeleteItem(uint f, IShellItem i, int r, IShellItem? c)
        {
            if (r < 0)
                Error = r;
            return 0;
        }
        public int StartOperations() => 0; public int FinishOperations(int r)
        {
            if (r < 0)
                Error = r;
            return 0;
        }
        public int PreRenameItem(uint f, IShellItem i, string n) => 0; public int PostRenameItem(uint f, IShellItem i, string n, int r, IShellItem? c) => 0;
        public int PreMoveItem(uint f, IShellItem i, IShellItem d, string n) => 0; public int PostMoveItem(uint f, IShellItem i, IShellItem d, string n, int r, IShellItem? c) => 0;
        public int PreCopyItem(uint f, IShellItem i, IShellItem d, string n) => 0; public int PostCopyItem(uint f, IShellItem i, IShellItem d, string n, int r, IShellItem? c) => 0;
        public int PreNewItem(uint f, IShellItem d, string n) => 0; public int PostNewItem(uint f, IShellItem d, string n, string t, uint a, int r, IShellItem? c) => 0;
        public int UpdateProgress(uint t, uint c) => 0; public int ResetTimer() => 0; public int PauseTimer() => 0; public int ResumeTimer() => 0;
    }
}
