using Apex.Platform;
namespace Apex.Services;
public static class GameBoosterChecks
{
    public static async Task<List<string>> RunAsync(string root)
    {
        Directory.CreateDirectory(root);
        var checks = new List<string>();
        void Assert(bool ok, string message)
        {
            if (!ok)
                throw new IOException(message);
            checks.Add(message);
        }
        var file = Path.Combine(root, "approved.exe");
        await File.WriteAllTextAsync(file, "disposable executable identity fixture");
        var identity = WindowsGameBooster.Identify(file);
        var profile = new GameBoostStore(Path.Combine(root, "profile"));
        profile.Load();
        Assert(profile.Profile.Approved.Count == 0, "Game Booster starts with no approvals");
        profile.Approve(identity, true);
        var reload = new GameBoostStore(profile.DirectoryPath);
        reload.Load();
        Assert(reload.Profile.Approved.Single() == identity, "Approvals persist by path and SHA-256, not PID/name");
        profile.Protect(file, true);
        Assert(profile.Profile.Approved.Count == 0 && profile.Profile.Protected.Single() == file, "User protection removes closure approval");
        profile.Protect(file, false);
        profile.Approve(identity, true);
        await File.WriteAllTextAsync(file, "changed executable");
        Assert(WindowsGameBooster.Identify(file) != identity, "Executable replacement invalidates its identity");
        var first = new BoostAppSelection(new(11, 100, "approved", file, true), identity);
        var refused = first with
        {
            App = first.App with
            {
                Id = 12,
                Name = "refused"
            }
        };
        var exited = first with
        {
            App = first.App with
            {
                Id = 13,
                Name = "exited"
            }
        };
        var protectedApp = first with
        {
            App = first.App with
            {
                Id = 14,
                Name = "game",
                CanClose = false
            }
        };
        var fake = new FakeBoost();
        var results = await GameBooster.PrepareAsync([first, refused, exited, protectedApp], [], fake, profile.Record, null, default);
        Assert(results.Actions.Count(a => a.State == "Completed") == 1 && results.Actions.Count(a => a.State == "Skipped") == 3 && results.Outcome.Kind == TaskOutcomeKind.Partial, "Refused, protected and reused processes are skipped with partial outcome");
        Assert(fake.Forced == 0 && fake.Recycled == 0, "Preparation never silently forces closure or cleanup");
        reload.Load();
        Assert(reload.LastResult?.Actions.Count == 4, "Per-action preparation results persist independently of settings records");
        var old = DateTime.UtcNow.AddDays(-3);
        var temporary = new Entry(Path.Combine(root, "old.tmp"), "old.tmp", 10, old);
        Assert(GameBooster.SafeTemporaryFile(temporary, root, DateTime.UtcNow), "Old ordinary temporary files qualify");
        foreach (string cache in new[] { "ShaderCache", "Chrome", "Steam", "BrowserCache", "D3DCache", "NVIDIA" })
            Assert(!GameBooster.SafeTemporaryFile(temporary with
            {
                Path = Path.Combine(root, cache, "old.tmp")
            }, root, DateTime.UtcNow), cache + " excluded from booster cleanup");
        Assert(!GameBooster.SafeTemporaryFile(temporary with
        {
            ModifiedUtc = DateTime.UtcNow
        }, root, DateTime.UtcNow) && !GameBooster.SafeTemporaryFile(temporary with
        {
            Path = Path.Combine(Path.GetDirectoryName(root)!, "outside.tmp")
        }, root, DateTime.UtcNow), "Recent and out-of-root files are excluded");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        fake = new();
        results = await GameBooster.PrepareAsync([first], [temporary], fake, _ => { }, null, canceled.Token);
        Assert(fake.Closed == 0 && fake.Recycled == 0 && results.Actions.All(a => a.State == "Canceled") && results.Outcome.Kind == TaskOutcomeKind.Canceled, "Pre-cancellation performs no closure or cleanup");
        using var mid = new CancellationTokenSource();
        fake = new()
        {
            AfterClose = () => mid.Cancel()
        };
        results = await GameBooster.PrepareAsync([first, refused], [temporary], fake, _ => { }, null, mid.Token);
        Assert(results.Actions[0].State == "Completed" && results.Actions.Skip(1).All(a => a.State == "Canceled") && fake.Recycled == 0, "Mid-preparation cancellation retains completed closure and stops later actions");
        fake = new()
        {
            FailRecycle = true
        };
        results = await GameBooster.PrepareAsync([first], [temporary], fake, _ => { }, null, default);
        Assert(results.Outcome.Kind == TaskOutcomeKind.Partial && results.Actions[0].State == "Completed" && results.Actions[1].State == "Failed", "Cleanup errors retain completed app results");
        string display = "before";
        int writes = 0;
        void Write(string value, string expected)
        {
            if (display != expected)
                throw new IOException("Conflict");
            display = value;
            writes++;
        }
        bool kept = await DisplayChangeGuard.ApplyAsync(() => display, Write, "before", "after", () => Task.FromResult(false), TimeSpan.FromMilliseconds(50));
        Assert(!kept && display == "before" && writes == 2, "Declined display confirmation rolls back through fake provider");
        kept = await DisplayChangeGuard.ApplyAsync(() => display, Write, "before", "after", () => new TaskCompletionSource<bool>().Task, TimeSpan.FromMilliseconds(10));
        Assert(!kept && display == "before", "Unresponsive display confirmation rolls back independently");
        kept = await DisplayChangeGuard.ApplyAsync(() => display, Write, "before", "after", () => Task.FromResult(true), TimeSpan.FromMilliseconds(50));
        Assert(kept && display == "after", "Confirmed display mode is retained");
        display = "before";
        try
        {
            await DisplayChangeGuard.ApplyAsync(() => display, Write, "before", "after", () => { display = "external"; return Task.FromResult(false); }, TimeSpan.FromMilliseconds(50));
            throw new Exception("Conflict ignored");
        }
        catch (IOException) { Assert(display == "external", "Display rollback preserves external changes"); }
        var mouse = System.Text.Json.JsonSerializer.Serialize(new MouseParameters(6, 10, 2));
        Assert(WindowsMouse.Parse(WindowsMouse.WithoutAcceleration(mouse)) == new MouseParameters(6, 10, 0), "Mouse acceleration change preserves original thresholds");
        var original = new DisplayMode("fixture-monitor", 1920, 1080, 32, 0, 0, 0, 0, 0, 60);
        Assert(WindowsDisplay.NativeLayoutsValid, "Display native structures match documented Windows layouts");
        Assert(WindowsGameBooster.HasGameGraphics(["D3D12.dll"], "unknown-game") && WindowsGameBooster.HasGameGraphics(["UnityPlayer.dll"], "chrome") && !WindowsGameBooster.HasGameGraphics(["d3d11.dll"], "chrome"), "Games and unknown rendering processes stay protected; ordinary browser rendering alone is not a game");
        Assert(WindowsDisplay.Highest(original, [original with { Hertz = 144 }, original with { Width = 1280, Hertz = 240 }, original with { Orientation = 1, Hertz = 240 }]).Hertz == 144, "Highest refresh rate preserves resolution and orientation");
        Assert(WindowsVisualEffects.Supported.Keys.All(k => WindowsVisualEffects.PresetValue(k) == (k == "FontSmoothing" ? "Enabled" : "Disabled")), "Performance appearance retains font smoothing and disables supported other effects");
        var draft = new PerformanceDraft();
        draft.SelectBuiltIn("Performance", new Dictionary<Guid, string> { [PerformanceDraft.HighPerformanceScheme] = "High performance" }, mouse, [new("fixture-display", "Fixture", System.Text.Json.JsonSerializer.Serialize(original), System.Text.Json.JsonSerializer.Serialize(original with { Hertz = 144 }), null)]);
        Assert(draft.Actions.Any(a => a.Kind == PlanActionKind.Mouse) && draft.Actions.Any(a => a.Kind == PlanActionKind.Display) && !draft.Actions.Any(a => a.Kind is PlanActionKind.Dns or PlanActionKind.StartupRun or PlanActionKind.Priority), "Performance includes mouse/display while target-dependent settings remain explicit");
        var grouped = BoostApps.Group([new(first.App, 1, 100, null), new(first.App with { Id = 22 }, 2, 200, null), new(first.App with { Id = 23, Executable = Path.Combine(root, "another.exe") }, 0, 50, null)]);
        Assert(grouped.Count == 2 && grouped[0].Instances.Count == 2 && grouped[0].Memory == 300 && grouped[0].Cpu == 3, "App rows combine process usage by executable path, never just process name");
        Assert(BoostApps.Group([new(first.App, 0, 1, null), new(protectedApp.App, 0, 1, "Protected game")]).Single().Protection == "Protected game", "One protected instance protects the whole grouped app");
        var selection = first with
        {
            Instances = [first.App, first.App with { Id = 22 }]
        };
        fake = new();
        results = await GameBooster.PrepareAsync([selection], [], fake, profile.Record, null, default);
        Assert(results.Actions.Count == 1 && fake.Closed == 2 && results.Actions.Single().State == "Completed", "Grouped approvals close all reviewed instances and produce one app result");
        reload.Load();
        Assert(reload.LastResult!.Actions.Single().App!.Instances!.Count == 2, "Grouped process identities survive result persistence for explicit force-close review");
        fake = new();
        results = await GameBooster.PrepareAsync([refused with { Instances = [refused.App] }], [], fake, _ => { }, null, default);
        Assert(results.Actions.Single().State == "Skipped" && results.Actions.Single().Detail.Contains("tray") && fake.Forced == 0, "Tray/refused closure is accurately reported with manual quit/force guidance");
        var resources = BoostApps.Group([new(first.App, 1, 100, null, 40, 1000), new(first.App with { Id = 22 }, 2, 200, null, 70, 2000)]).Single();
        Assert(resources.Gpu == 100 && resources.NetworkBytesPerSecond == 3000, "Resource grouping caps GPU percentage and sums network throughput by executable");
        Assert(BoostApps.Group([new(first.App, 0, 1, null)]).Single().NetworkBytesPerSecond is null, "Unmeasured network is unavailable rather than false zero");
        Assert(WindowsNetworkTrace.LayoutsValid, "Network trace native layouts match the x64 Windows SDK");
        var matched = NetworkUsage.Match(new("fixture", [new(11, 100, 2048), new(11, 999, 9000), new(12, 100, double.NaN), new(13, 100, -1)], null), [new(11, 100), new(12, 100), new(13, 100)]);
        Assert(matched.Count == 1 && matched[(11, 100)] == 2048, "Network results recheck process start identity and reject invalid rates");
        try
        {
            NetworkUsage.Match(new("fixture", [], null, 1), []);
            throw new Exception("Lost events accepted");
        }
        catch (IOException) { checks.Add("Lost network events are not shown as accurate usage"); }
        var fakeClean = new FakeCleanup();
        int persists = 0;
        var reviewed = new CleanItem(new(temporary, "fixture"), "System junk", root);
        fake = new();
        var combined = await ReviewedPreparation.RunAsync([first], [reviewed], true, fake, fakeClean, _ => persists++, null, default);
        Assert(fakeClean.Order.SequenceEqual(new[] { "empty", "recycle" }) && combined.Outcome.Message.Contains("1 apps closed") && combined.Outcome.Message.Contains("1 files cleaned"), "Reviewed preparation empties old Bin before recycling new files and reports app/file totals");
        using (var cleanCanceled = new CancellationTokenSource())
        {
            cleanCanceled.Cancel();
            fakeClean = new();
            combined = await ReviewedPreparation.RunAsync([], [reviewed], true, new FakeBoost(), fakeClean, _ => { }, null, cleanCanceled.Token);
            Assert(fakeClean.Order.Count == 0 && combined.Outcome.Kind == TaskOutcomeKind.Canceled, "Canceled combined preparation does not empty Bin or recycle files");
        }
        fakeClean = new()
        {
            Fail = true
        };
        combined = await ReviewedPreparation.RunAsync([], [reviewed], false, new FakeBoost(), fakeClean, _ => { }, null, default);
        Assert(combined.Outcome.Kind == TaskOutcomeKind.Partial && combined.Actions.Single().State == "Failed", "Combined cleanup failures remain partial with accurate results");
        fakeClean = new();
        persists = 0;
        var many = Enumerable.Range(0, 300).Select(i => reviewed with { Candidate = reviewed.Candidate with { File = temporary with { Path = Path.Combine(root, i + ".tmp") } } }).ToArray();
        await ReviewedPreparation.RunAsync([], many, false, new FakeBoost(), fakeClean, _ => persists++, null, default);
        Assert(persists == 2 && fakeClean.Order.Count == 300, "Cache cleanup batches journal updates instead of rewriting the entire journal for each file");
        return checks;
    }
    private sealed class FakeCleanup : IReviewedCleanupProvider
    {
        public List<string> Order = []; public bool Fail;
        public Task EmptyRecycleBinAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Order.Add("empty");
            return Task.CompletedTask;
        }
        public Task RecycleReviewedAsync(CleanItem file, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Fail)
                throw new IOException("Fixture failure");
            Order.Add("recycle");
            return Task.CompletedTask;
        }
    }
    private sealed class FakeBoost : IGameBoostProvider
    {
        public int Closed, Recycled, Forced; public bool FailRecycle; public Action? AfterClose;
        public Task<bool> CloseAsync(BoostAppSelection app, bool force, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (force)
                Forced++;
            if (!app.App.CanClose)
                throw new IOException("Protected game");
            if (app.App.Id == 13)
                throw new IOException("PID reused");
            if (app.App.Id == 12)
                return Task.FromResult(false);
            Closed += app.Instances?.Count ?? 1;
            AfterClose?.Invoke();
            return Task.FromResult(true);
        }
        public Task RecycleAsync(Entry file, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (FailRecycle)
                throw new IOException("Locked file");
            Recycled++;
            return Task.CompletedTask;
        }
    }
}
