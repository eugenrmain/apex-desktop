using Apex.Services;
namespace Apex.Platform;
public sealed record BoostCleanupPreview(List<CleanItem> Files, List<string> Issues, long BinCount, long BinBytes);
public static class BoostCleanup
{
    public static async Task<BoostCleanupPreview> ScanAsync(IReadOnlySet<string> categories, IReadOnlySet<string> closing, IProgress<string>? progress, CancellationToken token)
    {
        var files = new List<CleanItem>();
        var issues = new List<string>();
        async Task Scan(string category, string root, Func<Entry, bool> eligible, string? process = null)
        {
            token.ThrowIfCancellationRequested();
            if (files.Count >= 1000)
                return;
            if (!Directory.Exists(root))
                return;
            if (process is not null && WindowsSystemClean.Running(process) && !closing.Contains(process))
            {
                issues.Add(category + ": " + process + " is still running.");
                return;
            }
            try
            {
                var result = await FileToolkit.ScanAsync(root, true, progress, token);
                issues.AddRange(result.Issues.Take(100));
                files.AddRange(result.Files.Where(eligible).Take(Math.Max(0, 1000 - files.Count)).Select(f => new CleanItem(new(f, category), category, root, process)));
                if (files.Count >= 1000)
                    issues.Add("Review is limited to 1,000 files; run cleanup again for more.");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { issues.Add(category + ": " + e.Message); }
        }
        if (categories.Contains("Temporary files"))
            await Scan("System junk", Path.GetTempPath(), f => GameBooster.SafeTemporaryFile(f, Path.GetTempPath(), DateTime.UtcNow));
        foreach (bool browser in new[] { false, true })
            if (categories.Contains(browser ? "Browser cache" : "Application junk"))
            {
                try
                {
                    foreach (var root in WindowsSystemClean.Caches(browser))
                        await Scan(browser ? "Browser cache" : "Application junk", root.Path, _ => true, root.Process);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { issues.Add(e.Message); }
            }
        if (categories.Contains("Game junk") && files.Count < 1000)
        {
            var games = await Task.Run(WindowsGameDiscovery.Discover, token);
            issues.AddRange(games.Issues.Take(100));
            foreach (string root in games.Games.SelectMany(g => g.CleanupFolders).Distinct(StringComparer.OrdinalIgnoreCase))
                await Scan("Game junk", root, SystemCleanRules.GameJunk);
        }
        var bin = categories.Contains("Recycle Bin") ? WindowsSystemClean.RecycleBin() : default;
        return new(files.DistinctBy(f => f.Candidate.File.Path, StringComparer.OrdinalIgnoreCase).ToList(), issues.Take(200).ToList(), bin.Count, bin.Bytes);
    }
}
