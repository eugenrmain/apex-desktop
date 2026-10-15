namespace Apex.Services;

public static class AppDataPaths
{
    private static readonly Lazy<string> root = new(() =>
    {
        // Bounded verification can isolate every default store from the user's records.
        string? isolated = Environment.GetEnvironmentVariable("APX_DATA_ROOT");
        if (!string.IsNullOrWhiteSpace(isolated))
            return Path.GetFullPath(isolated);
        return Migrate(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    });
    public static string Root => root.Value;

    public static string Migrate(string localData)
    {
        string target = Path.Combine(localData, "ApexDesktop"), legacy = Path.Combine(localData, "EmberDesktop");
        using var gate = new Mutex(false, "Local\\APX.Desktop.DataMigration");
        bool held = false;
        try
        {
            try
            {
                held = gate.WaitOne(TimeSpan.FromSeconds(30));
            }
            catch (AbandonedMutexException) { held = true; }
            if (!held)
                throw new IOException("Apex data migration is busy. Close the other instance and retry.");
            string marker = Path.Combine(target, ".legacy-imported-v1");
            if (File.Exists(marker))
                return target;
            if (!Directory.Exists(legacy))
            {
                Directory.CreateDirectory(target);
                return target;
            }
            // Preserve the legacy folder as a recovery copy. Never overwrite newer Apex data.
            CopyMissing(legacy, target);
            File.WriteAllText(marker, "Imported legacy data without replacing existing Apex records. The original application data folder is preserved.");
            return target;
        }
        finally { if (held) gate.ReleaseMutex(); }
    }

    private static void CopyMissing(string source, string destination)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Linked application data cannot be migrated automatically.");
        if (Directory.Exists(destination) && (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Linked application data cannot be migrated automatically.");
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked application data cannot be migrated automatically.");
            string copy = Path.Combine(destination, Path.GetFileName(file));
            if (File.Exists(copy))
                continue;
            string temporary = copy + ".import-" + Guid.NewGuid().ToString("N");
            try
            {
                File.Copy(file, temporary);
                File.Move(temporary, copy, false);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        foreach (string directory in Directory.EnumerateDirectories(source))
            CopyMissing(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
