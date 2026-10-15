using Apex.Services;
using Microsoft.Win32;
namespace Apex.Platform;
public static class WindowsGameDiscovery
{
    public static DiscoveredGames Discover()
    {
        var games = new List<InstalledGame>();
        var issues = new List<string>();
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Attempt(Action read)
        {
            try
            {
                read();
            }
            catch (Exception e) { issues.Add(e.Message); }
        }
        string Read(string file)
        {
            FileToolkit.NoLinks(file);
            if (new FileInfo(file).Length > 2_000_000)
                throw new IOException("Launcher manifest exceeds the metadata limit.");
            return File.ReadAllText(file);
        }
        var steamRoots = new List<string> { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam") };
        Attempt(() => { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"); if (key?.GetValue("SteamPath") is string path) steamRoots.Add(path); });
        Attempt(() => { using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"); if (key?.GetValue("InstallPath") is string path) steamRoots.Add(path); });
        foreach (string root in steamRoots.Distinct(StringComparer.OrdinalIgnoreCase))
            Attempt(() => { if (!Directory.Exists(root)) return; FileToolkit.NoLinks(root); libraries.Add(root); string file = Path.Combine(root, "steamapps", "libraryfolders.vdf"); if (File.Exists(file)) foreach (var pair in GameManifest.Entries(Read(file)).Where(p => p.Key == "path" || int.TryParse(p.Key, out _)).Take(32)) if (Path.IsPathFullyQualified(pair.Value) && Directory.Exists(Path.Combine(pair.Value, "steamapps"))) libraries.Add(pair.Value); });
        foreach (string library in libraries.Take(32))
            Attempt(() => { string apps = Path.Combine(library, "steamapps"); if (!Directory.Exists(apps)) return; FileToolkit.NoLinks(apps); foreach (var manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf").Take(512)) Attempt(() => games.Add(GameManifest.Steam(Read(manifest), library))); });
        string epic = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        Attempt(() => { if (!Directory.Exists(epic)) return; FileToolkit.NoLinks(epic); foreach (var file in Directory.EnumerateFiles(epic, "*.item").Take(512)) Attempt(() => games.Add(GameManifest.Epic(Read(file)))); });
        var available = new List<InstalledGame>();
        foreach (var game in games.DistinctBy(g => g.Folder, StringComparer.OrdinalIgnoreCase))
            Attempt(() =>
        {
            if (!Directory.Exists(game.Folder))
                return;
            FileToolkit.NoLinks(game.Folder);
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Attempt(() => { FileToolkit.ValidateMutation(game.Folder); roots.Add(game.Folder); });
            foreach (string leaf in new[] { game.Name, Path.GetFileName(game.Folder), game.LaunchFolder }.Distinct(StringComparer.OrdinalIgnoreCase).Where(n => n.Length > 0 && n.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && n is not "." and not ".."))
                foreach (string suffix in new[] { "Logs", "Saved/Logs", "Saved/Crashes", "Temp" })
                {
                    string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), leaf, suffix.Replace('/', Path.DirectorySeparatorChar));
                    if (Directory.Exists(path))
                        Attempt(() => { FileToolkit.ValidateMutation(path); roots.Add(path); });
                }
            available.Add(game with
            {
                CleanupFolders = roots.ToList()
            });
        });
        return new(available.OrderBy(g => g.Name).ToList(), issues.Distinct().ToList());
    }
}
