using System.Text.Json;
using System.Text.RegularExpressions;
namespace Apex.Services;
public sealed record InstalledGame(string Name, string Launcher, string Folder, string LaunchFolder = "")
{
    public List<string> CleanupFolders { get; init; } = [];
    public override string ToString() => Name + " · " + Launcher;
}
public sealed record DiscoveredGames(List<InstalledGame> Games, List<string> Issues);
public static class GameManifest
{
    public static IEnumerable<KeyValuePair<string, string>> Entries(string text)
    {
        if (text.Length > 2_000_000)
            throw new IOException("Launcher metadata too large.");
        foreach (Match pair in Regex.Matches(text, "\"(?<key>[^\"]+)\"\\s*\"(?<value>(?:\\\\.|[^\"\\\\])*)\"", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            yield return new(pair.Groups["key"].Value, pair.Groups["value"].Value.Replace("\\\\", "\\").Replace("\\\"", "\""));
    }
    public static Dictionary<string, string> Pairs(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in Entries(text))
            result.TryAdd(pair.Key, pair.Value);
        return result;
    }
    public static InstalledGame Steam(string text, string library)
    {
        var pairs = Pairs(text);
        if (!pairs.TryGetValue("appid", out var id) || !uint.TryParse(id, out _) || !pairs.TryGetValue("installdir", out var leaf) || leaf.Length == 0 || leaf is "." or ".." || leaf.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new IOException("Invalid Steam game manifest.");
        string common = Path.GetFullPath(Path.Combine(library, "steamapps", "common")), folder = Path.GetFullPath(Path.Combine(common, leaf));
        if (!FileToolkit.IsWithin(folder, common))
            throw new IOException("Game folder escaped its library.");
        return new(pairs.GetValueOrDefault("name", leaf), "Steam", folder);
    }
    public static InstalledGame Epic(string text)
    {
        if (text.Length > 2_000_000)
            throw new IOException("Launcher metadata too large.");
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        if (root.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.ValueKind == JsonValueKind.True)
            throw new IOException("Incomplete game install.");
        string folder = root.GetProperty("InstallLocation").GetString() ?? "";
        if (!Path.IsPathFullyQualified(folder) || Path.TrimEndingDirectorySeparator(folder) == Path.GetPathRoot(folder))
            throw new IOException("Invalid game install location.");
        string name = root.GetProperty("DisplayName").GetString() ?? Path.GetFileName(folder);
        string launch = root.TryGetProperty("LaunchExecutable", out var executable) ? executable.GetString() ?? "" : "";
        return new(name, "Epic", Path.GetFullPath(folder), launch.Split('/', '\\')[0]);
    }
}
