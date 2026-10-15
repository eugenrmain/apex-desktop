using System.Text.Json;
namespace Apex.Services;
public sealed class Preferences
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool SolidBackground
    {
        get; set;
    }
    public bool CompletionSounds { get; set; } = true;
    public Dictionary<string, string> ExtensionGroups { get; set; } = new();
    public bool VerifyContents
    {
        get; set;
    }
    public bool IncludeSubdirectories
    {
        get; set;
    }
    public int CleanupAgeDays { get; set; } = 90;
    private static string PathName => Path.Combine(AppDataPaths.Root, "preferences.json");
    public static Preferences Load()
    {
        try
        {
            var preferences = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(PathName)) ?? new();
            preferences.ExtensionGroups ??= new();
            preferences.CleanupAgeDays = Math.Clamp(preferences.CleanupAgeDays, 1, 36500);
            return preferences;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        File.WriteAllText(PathName, JsonSerializer.Serialize(this));
    }
}
