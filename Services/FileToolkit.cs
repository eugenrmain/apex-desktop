using System.Security.Cryptography;
using System.Text.Json;

namespace Apex.Services;

public sealed record Entry(string Path, string RelativePath, long Size, DateTime ModifiedUtc);
public sealed record ScanResult(string Root, List<Entry> Files, List<string> Issues, int SkippedLinks, List<string> Directories);
public sealed record Difference(string Path, string State, string Detail);
public sealed record SortRule(string Extensions, string Destination);
public sealed record MovePlan(Entry Source, string Destination, string State);
public sealed record MoveRecord(string Source, string Destination, long Size, DateTime ModifiedUtc, string Hash);
public sealed record OperationResult(int Completed, List<string> Issues, List<MoveRecord>? Outputs = null);
public sealed record CleanupCandidate(Entry File, string Reason, Entry? Keep = null);

public sealed class FileToolkit
{
    public string JournalPath
    {
        get;
    }
    public FileToolkit(string? journalPath = null) => JournalPath = journalPath ?? Path.Combine(AppDataPaths.Root, "moves.json");
    public static bool IsWithin(string path, string root) => Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFullPath(path), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase);
    public static void NoLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Directory links and linked files are not supported for this operation: " + current);
    }
    public static void ValidateMutation(string path)
    {
        path = Path.GetFullPath(path);
        if (string.Equals(Path.TrimEndingDirectorySeparator(path), Path.TrimEndingDirectorySeparator(Path.GetPathRoot(path)!), StringComparison.OrdinalIgnoreCase))
            throw new IOException("A drive root cannot be modified.");
        foreach (var special in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonApplicationData })
        {
            var protectedPath = Environment.GetFolderPath(special);
            if (protectedPath.Length > 0 && IsWithin(path, protectedPath))
                throw new IOException("System and application directories are protected.");
        }
        NoLinks(path);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.System) != 0)
            throw new IOException("System files are protected.");
    }
    public static void Recheck(Entry entry)
    {
        NoLinks(entry.Path);
        var current = new FileInfo(entry.Path);
        if (!current.Exists || current.Length != entry.Size || current.LastWriteTimeUtc != entry.ModifiedUtc)
            throw new IOException("File changed since it was selected; refresh first: " + entry.Path);
    }
    public static Entry Inspect(string path)
    {
        path = Path.GetFullPath(path);
        NoLinks(path);
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new FileNotFoundException("File not found.", path);
        return new(path, info.Name, info.Length, info.LastWriteTimeUtc);
    }
    public static Task<ScanResult> ScanAsync(string root, bool recursive, IProgress<string>? progress, CancellationToken token, int maximumEntries = 100_000) => Task.Run(() =>
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        NoLinks(root);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException("Choose an existing directory.");
        if (maximumEntries is < 1 or > 100_000)
            throw new IOException("Invalid scan limit.");
        var result = new ScanResult(root, [], [], 0, []);
        int omittedIssues = 0;
        void Issue(string message)
        {
            if (result.Issues.Count < 1000)
                result.Issues.Add(message);
            else
                omittedIssues++;
        }
        int links = 0, visited = 0;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var folder = pending.Pop();
            try
            {
                NoLinks(folder);
                foreach (var path in Directory.EnumerateFileSystemEntries(folder))
                {
                    token.ThrowIfCancellationRequested();
                    if (result.Files.Count + result.Directories.Count >= maximumEntries)
                    {
                        result.Issues.Add($"Scan limit reached ({maximumEntries:N0} entries). Results are partial; choose a smaller folder.");
                        return result with
                        {
                            SkippedLinks = links
                        };
                    }
                    try
                    {
                        var attributes = File.GetAttributes(path);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            links++;
                            continue;
                        }
                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            result.Directories.Add(path);
                            if (recursive)
                                pending.Push(path);
                            continue;
                        }
                        var file = new FileInfo(path);
                        result.Files.Add(new(file.FullName, Path.GetRelativePath(root, path), file.Length, file.LastWriteTimeUtc));
                        if (++visited % 100 == 0)
                            progress?.Report($"{visited:N0} files · {folder}");
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Issue(path + ": " + e.Message); }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Issue(folder + ": " + e.Message); }
        }
        if (omittedIssues > 0)
            result.Issues.Add($"{omittedIssues:N0} additional scan errors omitted.");
        return result with
        {
            SkippedLinks = links
        };
    }, token);
    public static async Task<string> HashAsync(Entry entry, CancellationToken token)
    {
        Recheck(entry);
        await using var stream = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
        Recheck(entry);
        return hash;
    }
    public static async Task<List<Difference>> CompareAsync(IEnumerable<Entry> left, IEnumerable<Entry> right, bool hash, IProgress<string>? progress, CancellationToken token)
    {
        var a = left.ToDictionary(e => e.RelativePath, StringComparer.OrdinalIgnoreCase);
        var b = right.ToDictionary(e => e.RelativePath, StringComparer.OrdinalIgnoreCase);
        var rows = new List<Difference>();
        foreach (var path in a.Keys.Union(b.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            progress?.Report(path);
            if (!a.TryGetValue(path, out var x))
            {
                rows.Add(new(path, "Right only", "Not present on the left"));
                continue;
            }
            if (!b.TryGetValue(path, out var y))
            {
                rows.Add(new(path, "Left only", "Not present on the right"));
                continue;
            }
            Recheck(x);
            Recheck(y);
            if (x.Size != y.Size)
                rows.Add(new(path, "Different", $"Contents differ · File 1: {Size(x.Size)} · File 2: {Size(y.Size)}"));
            else if (hash)
            {
                var equal = await HashAsync(x, token) == await HashAsync(y, token);
                rows.Add(new(path, equal ? "Identical" : "Different", equal ? "SHA-256 verified" : "Contents differ"));
            }
            else
                rows.Add(new(path, x.ModifiedUtc == y.ModifiedUtc ? "Matching metadata" : "Potentially changed", "Equal size · contents not verified"));
        }
        return rows;
    }
    public static List<MovePlan> Plan(ScanResult scan, IEnumerable<SortRule> rules)
    {
        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Extensions) || string.IsNullOrWhiteSpace(rule.Destination))
                throw new IOException("Each rule needs extensions and a destination directory.");
            var destination = Path.GetFullPath(rule.Destination);
            if (!Directory.Exists(destination))
                throw new IOException("Choose an existing destination directory.");
            ValidateMutation(destination);
            foreach (var ext in rule.Extensions.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (!mappings.TryAdd("." + ext.TrimStart('.').ToLowerInvariant(), destination))
                    throw new IOException("An extension appears in more than one rule: " + ext);
        }
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var plans = new List<MovePlan>();
        foreach (var file in scan.Files)
        {
            if (!mappings.TryGetValue(Path.GetExtension(file.Path), out var folder))
                continue;
            var destination = Path.Combine(folder, Path.GetFileName(file.Path));
            var state = string.Equals(file.Path, destination, StringComparison.OrdinalIgnoreCase) ? "Already in destination" : File.Exists(destination) || Directory.Exists(destination) || !reserved.Add(destination) ? "Skip collision" : "Ready";
            plans.Add(new(file, destination, state));
        }
        return plans;
    }
    private List<MoveRecord> ReadJournal() => File.Exists(JournalPath) ? JsonSerializer.Deserialize<List<MoveRecord>>(File.ReadAllText(JournalPath)) ?? [] : [];
    private void SaveJournal(List<MoveRecord> records)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(JournalPath)!);
        var staging = JournalPath + ".new";
        File.WriteAllText(staging, JsonSerializer.Serialize(records));
        File.Move(staging, JournalPath, true);
    }
    public bool HasUndo => File.Exists(JournalPath);
    private FileStream LockJournal()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(JournalPath)!);
        return new FileStream(JournalPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    public async Task<OperationResult> MoveAsync(IEnumerable<MovePlan> plans, IProgress<string>? progress, CancellationToken token)
    {
        var outputs = new List<MoveRecord>();
        using var journalLock = LockJournal();
        var records = ReadJournal();
        var issues = new List<string>();
        int completed = 0;
        foreach (var plan in plans.Where(p => p.State == "Ready"))
        {
            if (token.IsCancellationRequested)
            {
                issues.Add("Canceled; completed moves remain available for undo.");
                break;
            }
            try
            {
                ValidateMutation(plan.Source.Path);
                ValidateMutation(plan.Destination);
                Recheck(plan.Source);
                if (File.Exists(plan.Destination) || Directory.Exists(plan.Destination))
                    throw new IOException("Destination now exists; skipped.");
                // Hash before moving so undo can detect contents changed at the destination.
                string hash = await HashAsync(plan.Source, token);
                var record = new MoveRecord(plan.Source.Path, plan.Destination, plan.Source.Size, plan.Source.ModifiedUtc, hash);
                records.Add(record);
                SaveJournal(records); // Write-ahead record survives interruption.
                Recheck(plan.Source);
                File.Move(plan.Source.Path, plan.Destination, false);
                outputs.Add(record);
                completed++;
                progress?.Report($"Moved {completed:N0} · {plan.Source.RelativePath}");
            }
            catch (OperationCanceledException) { issues.Add("Canceled; completed moves remain available for undo."); break; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { issues.Add(plan.Source.Path + ": " + e.Message); }
        }
        return new(completed, issues, outputs);
    }
    public async Task<OperationResult> UndoAsync(IProgress<string>? progress, CancellationToken token)
    {
        using var journalLock = LockJournal();
        var records = ReadJournal();
        var issues = new List<string>();
        int completed = 0;
        foreach (var record in records.ToArray().Reverse())
        {
            if (token.IsCancellationRequested)
            {
                issues.Add("Undo canceled.");
                break;
            }
            try
            {
                ValidateMutation(record.Source);
                ValidateMutation(record.Destination);
                if (File.Exists(record.Source) && !File.Exists(record.Destination))
                {
                    records.Remove(record);
                    SaveJournal(records);
                    continue;
                }
                if (File.Exists(record.Source) || Directory.Exists(record.Source))
                    throw new IOException("Original path is occupied; undo skipped.");
                var entry = Inspect(record.Destination);
                if (entry.Size != record.Size || entry.ModifiedUtc != record.ModifiedUtc || await HashAsync(entry, token) != record.Hash)
                    throw new IOException("Moved file has changed; undo skipped.");
                if (!Directory.Exists(Path.GetDirectoryName(record.Source)))
                    throw new IOException("Original directory no longer exists.");
                Recheck(entry);
                File.Move(record.Destination, record.Source, false);
                records.Remove(record);
                SaveJournal(records);
                completed++;
                progress?.Report($"Restored {completed:N0} files");
            }
            catch (OperationCanceledException) { issues.Add("Undo canceled."); break; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { issues.Add(record.Destination + ": " + e.Message); }
        }
        return new(completed, issues);
    }
    public static async Task<List<CleanupCandidate>> DuplicatesAsync(ScanResult scan, IProgress<string>? progress, CancellationToken token)
    {
        var result = new List<CleanupCandidate>();
        foreach (var group in scan.Files.GroupBy(e => e.Size).Where(g => g.Count() > 1))
        {
            var hashes = new Dictionary<string, Entry>();
            foreach (var entry in group.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                progress?.Report("Checking duplicate: " + entry.RelativePath);
                string hash = await HashAsync(entry, token);
                if (hashes.TryGetValue(hash, out var keep))
                    result.Add(new(entry, "Duplicate · keep " + keep.Path, keep));
                else
                    hashes[hash] = entry;
            }
        }
        return result;
    }
    public static string Size(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.##} GB" : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0.##} MB" : bytes >= 1024 ? $"{bytes / 1024d:0.##} KB" : $"{bytes:N0} B";
}
