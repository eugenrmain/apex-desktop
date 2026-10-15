using System.Diagnostics;
using System.Security.Cryptography;
using Apex.Services;
namespace Apex.Platform;
public sealed class WindowsGameBooster(GameBoostStore store, nint owner) : IGameBoostProvider, IReviewedCleanupProvider
{
    public static bool HasGameGraphics(IEnumerable<string> modules, string processName)
    {
        var list = modules.ToArray();
        if (list.Any(n => new[] { "unityplayer.dll", "unrealengine", "gameassembly.dll", "easyanticheat", "battleye", "nvngx_dlss" }.Any(m => n.Contains(m, StringComparison.OrdinalIgnoreCase))))
            return true;
        bool rendering = list.Any(n => new[] { "d3d9.dll", "d3d11.dll", "d3d12.dll", "opengl32.dll", "vulkan-1.dll", "dinput8.dll" }.Contains(n, StringComparer.OrdinalIgnoreCase));
        string[] ordinaryApps = ["chrome", "msedge", "firefox", "discord", "slack", "teams", "msteams", "spotify", "notepad", "code", "winword", "excel", "powerpnt", "obs64", "photoshop", "illustrator"];
        return rendering && !ordinaryApps.Contains(processName, StringComparer.OrdinalIgnoreCase);
    }
    public static ExecutableIdentity Identify(string path)
    {
        FileToolkit.NoLinks(path);
        using var stream = File.OpenRead(path);
        return new(Path.GetFullPath(path), Convert.ToHexString(SHA256.HashData(stream)));
    }
    public string? Protection(AppTask app)
    {
        if (!app.CanClose || app.Id == Environment.ProcessId || app.Executable.Length == 0)
            return "System or inaccessible process";
        if (store.Profile.Protected.Contains(app.Executable, StringComparer.OrdinalIgnoreCase))
            return "Protected by you";
        string name = app.Name.ToLowerInvariant();
        string[] protectedNames = ["apex", "ember", "steam", "epicgameslauncher", "riotclient", "battle.net", "eadesktop", "origin", "ubisoft", "goggalaxy", "gamingservices", "gamebar", "xbox", "presentmon", "anticheat", "easyanti", "battleye", "beservice", "vgc", "vanguard", "msmpeng", "securityhealth", "defender", "avast", "avg", "kaspersky", "bitdefender", "norton", "mcafee", "eset", "malwarebytes", "audiodg"];
        if (protectedNames.Any(n => name.Contains(n)))
            return "Game dependency, launcher, security software or Apex";
        try
        {
            var info = FileVersionInfo.GetVersionInfo(app.Executable);
            string description = (info.ProductName + " " + info.FileDescription).ToLowerInvariant();
            if (description.Contains("game") || description.Contains("anti-cheat") || description.Contains("antivirus"))
                return "Game or security software";
            using var process = Process.GetProcessById(app.Id);
            if (HasGameGraphics(process.Modules.Cast<ProcessModule>().Select(m => m.ModuleName), app.Name))
                return "Game, game dependency or unrecognized graphics app";
        }
        catch { return "Cannot verify game/process protection"; }
        return null;
    }
    public async Task<bool> CloseAsync(BoostAppSelection selected, bool force, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var executable = FileToolkit.Inspect(selected.Identity.Path);
        var identity = await Task.Run(() => Identify(selected.Identity.Path), token);
        if (identity != selected.Identity || !store.Profile.Approved.Contains(identity))
            throw new IOException("Executable changed or approval was removed; review it again.");
        var instances = selected.Instances ?? [selected.App];
        foreach (var instance in instances)
        {
            token.ThrowIfCancellationRequested();
            Process process;
            try
            {
                process = Process.GetProcessById(instance.Id);
            }
            catch (ArgumentException) { continue; }
            using (process)
            {
                if (process.HasExited)
                    continue;
                var live = WindowsActions.Describe(process);
                if (live.Started != instance.Started || !live.Executable.Equals(instance.Executable, StringComparison.OrdinalIgnoreCase) || !live.Executable.Equals(selected.Identity.Path, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Process identity changed; refresh apps before closing.");
                string? reason = await Task.Run(() => Protection(live), token);
                if (reason is not null)
                    throw new IOException(reason);
                FileToolkit.Recheck(executable);
                if (identity != selected.Identity || !store.Profile.Approved.Contains(identity))
                    throw new IOException("Executable changed or approval was removed; review it again.");
                token.ThrowIfCancellationRequested();
                // Close windows normally. A tray-only app is never silently killed.
                if (force)
                    process.Kill(false);
                else if (process.MainWindowHandle != 0)
                    process.CloseMainWindow();
            }
        }
        for (int attempt = 0; attempt < 20; attempt++)
        {
            token.ThrowIfCancellationRequested();
            if (!RunningExecutable(selected.Identity.Path, instances))
                return true;
            await Task.Delay(250, token);
        }
        return false;
    }
    private static bool RunningExecutable(string path, List<AppTask> instances)
    {
        foreach (var instance in instances)
        {
            try
            {
                using var known = Process.GetProcessById(instance.Id);
                if (!known.HasExited && known.StartTime.ToUniversalTime().Ticks == instance.Started)
                    return true;
            }
            catch (ArgumentException) { }
            catch { return true; }
        }
        foreach (var process in Process.GetProcesses())
            using (process)
            {
                try
                {
                    if (process.MainModule?.FileName is string live && live.Equals(path, StringComparison.OrdinalIgnoreCase) && process.SessionId == Process.GetCurrentProcess().SessionId)
                        return true;
                }
                catch { }
            }
        return false;
    }
    public async Task RecycleAsync(Entry file, CancellationToken token)
    {
        if (!GameBooster.SafeTemporaryFile(file, Path.GetTempPath(), DateTime.UtcNow))
            throw new IOException("File no longer qualifies as a safe temporary-file candidate.");
        token.ThrowIfCancellationRequested();
        await Task.Run(() => WindowsActions.Delete(file, false, owner), token);
    }
    public Task EmptyRecycleBinAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.Run(() => WindowsSystemClean.EmptyBin(owner), token);
    }
    public async Task RecycleReviewedAsync(CleanItem item, CancellationToken token)
    {
        if (item.Category is not ("System junk" or "Application junk" or "Browser cache" or "Game junk"))
            throw new IOException("Unsupported gaming cleanup category.");
        token.ThrowIfCancellationRequested();
        WindowsSystemClean.Recheck(item);
        await Task.Run(() => WindowsActions.Delete(item.Candidate.File, false, owner), token);
    }
    public static void Reopen(BoostAppSelection selected)
    {
        if (Identify(selected.Identity.Path) != selected.Identity)
            throw new IOException("Executable changed since closure; relaunch it yourself.");
        // Executable only: never replay untrusted command lines or pretend to recover documents.
        Process.Start(new ProcessStartInfo(selected.Identity.Path) { UseShellExecute = true });
    }
}
