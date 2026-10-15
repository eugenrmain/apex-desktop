using System.Text.Json;
using Apex.Platform;
namespace Apex.Services;
public sealed record ExecutableIdentity(string Path, string Sha256);
public sealed record GameBoostProfile(List<ExecutableIdentity> Approved, List<string> Protected, int Version = 1);
public sealed record BoostCandidate(AppTask App, double Cpu, long Memory, string? Protection, double? Gpu = null, double? NetworkBytesPerSecond = null);
public sealed record BoostAppSelection(AppTask App, ExecutableIdentity Identity, List<AppTask>? Instances = null);
public sealed record BoostAppGroup(string Name, string Executable, double Cpu, long Memory, List<AppTask> Instances, string? Protection, double? Gpu = null, double? NetworkBytesPerSecond = null);
public static class BoostApps
{
    public static bool Recommended(string name) => new[] { "chrome", "msedge", "firefox", "opera", "brave", "discord", "teams", "msteams", "slack", "spotify", "vlc" }.Contains(name, StringComparer.OrdinalIgnoreCase);
    public static List<BoostAppGroup> Group(IEnumerable<BoostCandidate> candidates) => candidates.Where(c => c.App.Executable.Length > 0).GroupBy(c => c.App.Executable, StringComparer.OrdinalIgnoreCase).Select(g => new BoostAppGroup(g.First().App.Name, g.Key, g.Sum(c => c.Cpu), g.Sum(c => c.Memory), g.Select(c => c.App).ToList(), g.Select(c => c.Protection).FirstOrDefault(p => p is not null), g.Any(c => c.Gpu.HasValue) ? Math.Clamp(g.Sum(c => c.Gpu ?? 0), 0, 100) : null, g.Any(c => c.NetworkBytesPerSecond.HasValue) ? g.Sum(c => c.NetworkBytesPerSecond ?? 0) : null)).OrderByDescending(g => g.Memory).ToList();
}
public sealed record BoostActionResult(string Kind, string Target, string State, string Detail, BoostAppSelection? App = null, int Count = 1);
public sealed record GameBoostResult(Guid Id, DateTime TimeUtc, List<BoostActionResult> Actions)
{
    public TaskOutcome Outcome
    {
        get
        {
            int apps = Actions.Where(a => a.Kind == "App" && a.State == "Completed").Sum(a => a.Count), files = Actions.Where(a => a.Kind is not ("App" or "Recycle Bin" or "Scan" or "Clipboard") && a.State == "Completed").Sum(a => a.Count);
            string message = $"{apps} apps closed · {files} files cleaned";
            if (Actions.Any(a => a.Kind == "Recycle Bin" && a.State == "Completed"))
                message += " · Recycle Bin emptied";
            foreach (string state in new[] { "Skipped", "Failed", "Canceled" })
            {
                int count = Actions.Where(a => a.State == state).Sum(a => a.Count);
                if (count > 0)
                    message += $" · {count} {state.ToLowerInvariant()}";
            }
            return new(Actions.Any(a => a.State == "Canceled") ? TaskOutcomeKind.Canceled : Actions.Any(a => a.State != "Completed") ? TaskOutcomeKind.Partial : TaskOutcomeKind.Success, message);
        }
    }
}
public interface IGameBoostProvider
{
    Task<bool> CloseAsync(BoostAppSelection app, bool force, CancellationToken token);
    Task RecycleAsync(Entry file, CancellationToken token);
}
public sealed class GameBoostStore
{
    public string DirectoryPath
    {
        get;
    }
    public GameBoostProfile Profile { get; private set; } = new([], []);
    public GameBoostResult? LastResult
    {
        get; private set;
    }
    public GameBoostStore(string? directory = null) => DirectoryPath = directory ?? Path.Combine(AppDataPaths.Root, "game-booster-v1");
    public void Load()
    {
        var path = Path.Combine(DirectoryPath, "profile.json");
        if (File.Exists(path))
            Profile = JsonSerializer.Deserialize<GameBoostProfile>(File.ReadAllText(path)) ?? throw new IOException("Game Booster profile cannot be read.");
        if (Profile.Version != 1)
            throw new IOException("Unsupported Game Booster profile version.");
        path = Path.Combine(DirectoryPath, "last-result.json");
        if (File.Exists(path))
            LastResult = JsonSerializer.Deserialize<GameBoostResult>(File.ReadAllText(path));
    }
    private void Save<T>(string name, T value)
    {
        Directory.CreateDirectory(DirectoryPath);
        var path = Path.Combine(DirectoryPath, name);
        var temporary = path + ".tmp";
        using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(file, value);
            file.Flush(true);
        }
        File.Move(temporary, path, true);
    }
    public void Approve(ExecutableIdentity identity, bool enabled)
    {
        var approved = Profile.Approved.Where(a => !a.Path.Equals(identity.Path, StringComparison.OrdinalIgnoreCase)).ToList();
        if (enabled)
            approved.Add(identity);
        var updated = Profile with
        {
            Approved = approved
        };
        Save("profile.json", updated);
        Profile = updated;
    }
    public void Protect(string path, bool enabled)
    {
        var items = Profile.Protected.Where(p => !p.Equals(path, StringComparison.OrdinalIgnoreCase)).ToList();
        if (enabled)
            items.Add(path);
        var updated = Profile with
        {
            Protected = items,
            Approved = Profile.Approved.Where(a => !enabled || !a.Path.Equals(path, StringComparison.OrdinalIgnoreCase)).ToList()
        };
        Save("profile.json", updated);
        Profile = updated;
    }
    public void Record(GameBoostResult result)
    {
        Save("last-result.json", result);
        LastResult = result;
    }
}
public static class GameBooster
{
    public static bool SafeTemporaryFile(Entry file, string root, DateTime now)
    {
        if (!FileToolkit.IsWithin(file.Path, root) || file.ModifiedUtc >= now.AddDays(-1))
            return false;
        var segments = Path.GetRelativePath(root, file.Path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !segments.Any(p => new[] { "cache", "shader", "chrome", "edge", "firefox", "browser", "discord", "steam", "epic", "riot", "nvidia", "amd", "d3d" }.Any(word => p.Contains(word, StringComparison.OrdinalIgnoreCase)));
    }
    public static async Task<GameBoostResult> PrepareAsync(IReadOnlyList<BoostAppSelection> apps, IReadOnlyList<Entry> files, IGameBoostProvider provider, Action<GameBoostResult> persist, IProgress<string>? progress, CancellationToken token)
    {
        var result = new GameBoostResult(Guid.NewGuid(), DateTime.UtcNow, []);
        foreach (var app in apps)
        {
            result.Actions.Add(new("App", app.App.Name, "Pending", "Close request prepared; completion not yet recorded.", app));
            persist(result);
            string state, detail;
            progress?.Report("Requesting close: " + app.App.Name);
            try
            {
                token.ThrowIfCancellationRequested();
                bool closed = await provider.CloseAsync(app, false, token);
                state = closed ? "Completed" : "Skipped";
                detail = closed ? "App closed" : "Still running. Quit it from the tray, respond to a save prompt, or confirm Force close below.";
            }
            catch (OperationCanceledException) { state = "Canceled"; detail = "No further close request sent."; }
            catch (Exception e) { state = "Skipped"; detail = e.Message; }
            result.Actions[^1] = new("App", app.App.Name, state, detail, app);
            persist(result);
        }
        foreach (var file in files)
        {
            result.Actions.Add(new("Temporary file", file.Path, "Pending", "Recycle prepared; completion not yet recorded."));
            persist(result);
            string state, detail;
            progress?.Report("Recycling: " + file.RelativePath);
            try
            {
                token.ThrowIfCancellationRequested();
                await provider.RecycleAsync(file, token);
                state = "Completed";
                detail = "Recycled; restore through Windows Recycle Bin.";
            }
            catch (OperationCanceledException) { state = "Canceled"; detail = "Not recycled."; }
            catch (Exception e) { state = "Failed"; detail = e.Message; }
            result.Actions[^1] = new("Temporary file", file.Path, state, detail);
            persist(result);
        }
        return result;
    }
}
