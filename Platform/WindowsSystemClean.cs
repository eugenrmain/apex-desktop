using System.Diagnostics;
using System.Runtime.InteropServices;
using Apex.Services;
namespace Apex.Platform;
public static class WindowsSystemClean
{
    public sealed record CacheRoot(string Label, string Path, string Process);
    public static List<CacheRoot> Caches(bool browsers)
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var result = new List<CacheRoot>();
        if (browsers)
        {
            foreach (var browser in new[] { ("Chrome", @"Google\Chrome\User Data", "chrome"), ("Edge", @"Microsoft\Edge\User Data", "msedge") })
            {
                string root = System.IO.Path.Combine(local, browser.Item2);
                if (!Directory.Exists(root))
                    continue;
                FileToolkit.NoLinks(root);
                foreach (var profile in Directory.EnumerateDirectories(root).Where(p => System.IO.Path.GetFileName(p) == "Default" || System.IO.Path.GetFileName(p).StartsWith("Profile ", StringComparison.Ordinal)))
                    result.Add(new(browser.Item1, System.IO.Path.Combine(profile, "Cache", "Cache_Data"), browser.Item3));
            }
            string firefox = System.IO.Path.Combine(local, @"Mozilla\Firefox\Profiles");
            if (Directory.Exists(firefox))
            {
                FileToolkit.NoLinks(firefox);
                foreach (var profile in Directory.EnumerateDirectories(firefox))
                    result.Add(new("Firefox", System.IO.Path.Combine(profile, "cache2"), "firefox"));
            }
        }
        else
        {
            foreach (string baseFolder in new[] { local, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) })
                foreach (var app in new[] { ("Discord", "discord", "Discord"), ("Slack", "slack", "slack") })
                    result.Add(new(app.Item1, System.IO.Path.Combine(baseFolder, app.Item2, "Cache", "Cache_Data"), app.Item3));
        }
        return result.Where(r => Directory.Exists(r.Path)).ToList();
    }
    public static bool Running(string name)
    {
        var processes = Process.GetProcessesByName(name);
        try
        {
            return processes.Length > 0;
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    public static void Recheck(CleanItem item)
    {
        FileToolkit.NoLinks(item.Root);
        if (!FileToolkit.IsWithin(item.Candidate.File.Path, item.Root))
            throw new IOException("Cleanup target escaped its reviewed directory.");
        if (item.ProcessName is not null && (Running(item.ProcessName) || !SystemCleanRules.CacheFile(item.Candidate.File.Path, item.Root)))
            throw new IOException("App is running or cache scope changed; skipped.");
        if (item.Category == "Game junk" && !SystemCleanRules.GameJunk(item.Candidate.File))
            throw new IOException("Game file no longer qualifies as temporary/log/crash data.");
        if (item.Category == "Backups" && !SystemCleanRules.Backup(item.Candidate.File))
            throw new IOException("Only reviewed .bak and .old files qualify.");
        if (item.Category == "System junk" && !GameBooster.SafeTemporaryFile(item.Candidate.File, item.Root, DateTime.UtcNow))
            throw new IOException("Temporary-file scope changed; skipped.");
        FileToolkit.Recheck(item.Candidate.File);
    }
    public static (long Count, long Bytes) RecycleBin()
    {
        var info = new BinInfo { Size = (uint)Marshal.SizeOf<BinInfo>() };
        Marshal.ThrowExceptionForHR(SHQueryRecycleBinW(null, ref info));
        return (info.Count, info.Bytes);
    }
    public static void EmptyBin(nint owner) => Marshal.ThrowExceptionForHR(SHEmptyRecycleBinW(owner, null, 7));
    [StructLayout(LayoutKind.Sequential)]
    private struct BinInfo
    {
        public uint Size; public long Bytes, Count;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHQueryRecycleBinW(string? root, ref BinInfo info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHEmptyRecycleBinW(nint owner, string? root, uint flags);
}
