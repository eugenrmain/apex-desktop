using Apex.Platform;
namespace Apex.Services;
public sealed record CleanItem(CleanupCandidate Candidate, string Category, string Root, string? ProcessName = null);
public sealed record CleanScan(List<CleanItem> Items, List<string> Issues);
public static class SystemCleanRules
{
    public static bool GameJunk(Entry file) => file.ModifiedUtc < DateTime.UtcNow.AddDays(-1) && new[] { ".tmp", ".log", ".dmp" }.Contains(Path.GetExtension(file.Path), StringComparer.OrdinalIgnoreCase) && !file.Path.Contains("shader", StringComparison.OrdinalIgnoreCase);
    public static bool Backup(Entry file) => new[] { ".bak", ".old" }.Contains(Path.GetExtension(file.Path), StringComparer.OrdinalIgnoreCase);
    public static bool CacheFile(string path, string root) => FileToolkit.IsWithin(path, root) && new[] { "Cache", "Cache_Data", "cache2" }.Contains(Path.GetFileName(Path.TrimEndingDirectorySeparator(root)), StringComparer.OrdinalIgnoreCase);
}
