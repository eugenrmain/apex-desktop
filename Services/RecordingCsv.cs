using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Apex.Services;
public static class RecordingCsv
{
    public static string[] Split(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        bool quote = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (quote && i + 1 < line.Length && line[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else
                    quote = !quote;
            }
            else if (c == ',' && !quote)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
                field.Append(c);
        }
        if (quote)
            throw new IOException("Incomplete quoted CSV row.");
        fields.Add(field.ToString());
        return fields.ToArray();
    }
    public static List<FrameMetric> Parse(TextReader reader, int processId)
    {
        var frames = new List<FrameMetric>();
        var headerLine = reader.ReadLine();
        if (headerLine is null)
            return frames;
        var headers = Split(headerLine.TrimStart('\uFEFF'));
        int Column(params string[] names) => Array.FindIndex(headers, h => names.Any(n => n.Equals(h, StringComparison.OrdinalIgnoreCase)));
        int pid = Column("ProcessID"), chain = Column("SwapChainAddress"), time = Column("TimeInSeconds", "CPUStartTime"), rendered = Column("MsBetweenPresents", "FrameTime"), displayed = Column("MsBetweenDisplayChange", "DisplayedTime");
        if (pid < 0 || chain < 0 || time < 0 || rendered < 0)
            throw new IOException("Unsupported PresentMon CSV columns.");
        for (string? line; (line = reader.ReadLine()) is not null;)
        {
            var row = Split(line);
            if (row.Length != headers.Length)
                continue;
            if (!int.TryParse(row[pid], out int id) || id != processId)
                continue;
            if (!double.TryParse(row[time], NumberStyles.Float, CultureInfo.InvariantCulture, out double t) || !double.IsFinite(t) || t < 0)
                continue;
            void Add(int col, string metric)
            {
                if (col >= 0 && double.TryParse(row[col], NumberStyles.Float, CultureInfo.InvariantCulture, out double ms) && double.IsFinite(ms) && ms > 0 && ms < 60_000)
                    frames.Add(new(row[chain], metric, t, ms));
            }
            Add(rendered, "Rendered");
            Add(displayed, "Displayed");
            if (frames.Count > 2_000_000)
                throw new IOException("Capture exceeds the bounded frame limit.");
        }
        return frames;
    }
    public static List<FrameSummary> Summaries(IEnumerable<FrameMetric> frames)
    {
        var result = new List<FrameSummary>();
        foreach (var group in frames.GroupBy(f => (f.SwapChain, f.Metric)))
        {
            var ordered = group.OrderBy(f => f.TimeSeconds).ToArray();
            if (ordered.Length < 2)
                continue;
            var values = ordered.Select(f => f.Milliseconds).Order().ToArray();
            // Include the first frame's measured interval in the duration.
            double duration = ordered[^1].TimeSeconds - ordered[0].TimeSeconds + ordered[0].Milliseconds / 1000;
            if (duration <= 0)
                continue;
            int slow = Math.Max(1, (int)Math.Ceiling(values.Length * .01));
            double P(double percentile) => values[Math.Clamp((int)Math.Ceiling(percentile * values.Length) - 1, 0, values.Length - 1)];
            result.Add(new(group.Key.SwapChain, group.Key.Metric, values.Length, duration, values.Length / duration, 1000 / values.TakeLast(slow).Average(), P(.5), P(.95), P(.99)));
        }
        return result;
    }
}
public sealed class RecordingStore
{
    public string DirectoryPath
    {
        get;
    }
    public RecordingStore(string? root = null) => DirectoryPath = root ?? Path.Combine(AppDataPaths.Root, "recordings-v1");
    public string Folder(Guid id) => Path.Combine(DirectoryPath, id.ToString("N"));
    public void Save(GameRecording recording)
    {
        Directory.CreateDirectory(Folder(recording.Id));
        string path = Path.Combine(Folder(recording.Id), "recording.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(recording));
        File.Move(path + ".tmp", path, true);
    }
    public List<GameRecording> List()
    {
        if (!Directory.Exists(DirectoryPath))
            return [];
        var result = new List<GameRecording>();
        foreach (var dir in Directory.EnumerateDirectories(DirectoryPath))
        {
            if (!Guid.TryParseExact(Path.GetFileName(dir), "N", out var id))
                continue;
            try
            {
                var record = JsonSerializer.Deserialize<GameRecording>(File.ReadAllText(Path.Combine(dir, "recording.json")));
                if (record?.Id == id && record.Version == 1)
                    result.Add(record);
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return result.OrderByDescending(r => r.StartedUtc).ToList();
    }
    public void Remove(Guid id)
    {
        string directory = Folder(id);
        if (!Directory.Exists(directory))
            return;
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Linked recording folders cannot be removed.");
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked recording files cannot be removed.");
            File.Delete(path);
        }
        Directory.Delete(directory, false);
    }
    public void Export(GameRecording record, string destination)
    {
        if (!Directory.Exists(destination))
            throw new DirectoryNotFoundException("Choose an existing export folder.");
        FileToolkit.ValidateMutation(destination);
        string stem = "Apex-" + record.Id.ToString("N");
        string json = Path.Combine(destination, stem + ".json"), csv = Path.Combine(destination, stem + ".csv");
        using (var output = new FileStream(json, FileMode.CreateNew))
            JsonSerializer.Serialize(output, record);
        try
        {
            File.Copy(Path.Combine(Folder(record.Id), "frames.csv"), csv, false);
        }
        catch { File.Delete(json); throw; }
    }
}
