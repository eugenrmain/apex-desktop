namespace Apex.Services;

public enum QueueState
{
    Ready, Working, Completed, Failed, Canceled
}
public sealed class CompressionQueueItem(string source, CompressionOptions options)
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Source { get; } = source;
    public CompressionOptions Options { get; set; } = options;
    public string Destination { get; set; } = Path.GetDirectoryName(source)!;
    public QueueState State
    {
        get; set;
    }
    public string Detail { get; set; } = "Ready";
    public CompressionResult? Output
    {
        get; set;
    }
}

public static class SelectionValidation
{
    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic", ".heif" };
    private static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".m4v", ".avi", ".wmv", ".mkv", ".webm", ".mpeg", ".mpg", ".mts", ".m2ts" };
    public static bool IsVideo(string path) => Videos.Contains(Path.GetExtension(path));
    public static bool IsMedia(string path) => IsVideo(path) || Images.Contains(Path.GetExtension(path));
    public static string[] Validate(IEnumerable<string> paths, bool file, bool multiple = false, bool media = false)
    {
        var selected = paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (selected.Length == 0 || (!multiple && selected.Length != 1))
            throw new IOException(multiple ? "Choose at least one file." : "Drop one " + (file ? "file" : "folder") + " on this card.");
        foreach (var path in selected)
        {
            if (file ? !File.Exists(path) : !Directory.Exists(path))
                throw new IOException("Choose a local " + (file ? "file" : "folder") + ".");
            if (media && !IsMedia(path))
                throw new IOException("Unsupported media type: " + Path.GetFileName(path));
        }
        return selected;
    }
}
