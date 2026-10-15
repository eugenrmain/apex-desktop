using Apex.Platform;
using System.Text.Json;

namespace Apex.Services;
public static class ReleaseChecks
{
    public static async Task<List<string>> RunAsync(string root)
    {
        Directory.CreateDirectory(root);
        var checks = new List<string>();
        void Assert(bool ok, string title)
        {
            if (!ok)
                throw new IOException(title);
            checks.Add(title);
        }
        string migrationRoot = Path.Combine(root, "migration");
        string legacyRoot = Path.Combine(migrationRoot, "EmberDesktop");
        Directory.CreateDirectory(Path.Combine(legacyRoot, "performance-v1"));
        File.WriteAllText(Path.Combine(legacyRoot, "preferences.json"), "legacy preferences");
        File.WriteAllText(Path.Combine(legacyRoot, "performance-v1", "restore.json"), "original restoration values");
        string migrated = AppDataPaths.Migrate(migrationRoot);
        Assert(Path.GetFileName(migrated) == "ApexDesktop" && File.ReadAllText(Path.Combine(migrated, "preferences.json")) == "legacy preferences", "Legacy preferences imported into ApexDesktop");
        Assert(File.ReadAllText(Path.Combine(migrated, "performance-v1", "restore.json")) == "original restoration values", "Nested restoration records preserved during migration");
        Assert(File.ReadAllText(Path.Combine(legacyRoot, "preferences.json")) == "legacy preferences", "Legacy data retained as a recovery copy");
        File.WriteAllText(Path.Combine(migrated, "preferences.json"), "new Apex preferences");
        AppDataPaths.Migrate(migrationRoot);
        Assert(File.ReadAllText(Path.Combine(migrated, "preferences.json")) == "new Apex preferences", "Repeated migration never replaces newer Apex data");
        File.Delete(Path.Combine(migrated, ".legacy-imported-v1"));
        AppDataPaths.Migrate(migrationRoot);
        Assert(File.ReadAllText(Path.Combine(migrated, "preferences.json")) == "new Apex preferences", "Interrupted import preserves existing destination records");
        string file = Path.Combine(root, "日本語, sample.png"), video = Path.Combine(root, "clip.mp4");
        await File.WriteAllTextAsync(file, "original image fixture");
        await File.WriteAllTextAsync(video, "original video fixture");
        Assert(SelectionValidation.Validate([file], true).Length == 1, "Unicode file selection");
        Assert(SelectionValidation.Validate([file, video], true, true, true).Length == 2, "Mixed media drops accepted");
        try
        {
            SelectionValidation.Validate([file, video], true);
            throw new Exception("Multiple drop accepted");
        }
        catch (IOException) { checks.Add("Multiple-item single-selection drops rejected"); }
        try
        {
            SelectionValidation.Validate([file], false);
            throw new Exception("File accepted as folder");
        }
        catch (IOException) { checks.Add("File/folder mismatch rejected"); }
        try
        {
            SelectionValidation.Validate([Path.Combine(root, "missing.png")], true);
            throw new Exception("Missing path accepted");
        }
        catch (IOException) { checks.Add("Missing/cloud-unavailable paths rejected"); }
        var options = new CompressionOptions(false, 128, 80, 0, false, false);
        var queue = new[] { new CompressionQueueItem(file, options), new CompressionQueueItem(video, options with { Video = true }) };
        int simultaneous = 0, peak = 0;
        var outcome = await CompressionBatch.RunAsync(queue, async (item, token) => { peak = Math.Max(peak, ++simultaneous); await Task.Delay(1, token); simultaneous--; if (item.Options.Video) throw new IOException("Unsupported codec"); return new(item.Source + ".copy", 20, 10, "Fixture"); }, _ => { }, default);
        Assert(outcome.Kind == TaskOutcomeKind.Partial && queue[0].State == QueueState.Completed && queue[1].State == QueueState.Failed && peak == 1, "Sequential batch retains successful outputs through codec failures");
        Assert(outcome.Outputs?.Count == 1 && await File.ReadAllTextAsync(file) == "original image fixture", "Batch output references and original preservation");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var canceled = new[] { new CompressionQueueItem(file, options), new CompressionQueueItem(video, options) };
        int invoked = 0;
        var stopped = await CompressionBatch.RunAsync(canceled, (_, _) => { invoked++; return Task.FromResult(new CompressionResult("x", 1, 1, "")); }, _ => { }, cancel.Token);
        Assert(stopped.Kind == TaskOutcomeKind.Canceled && invoked == 0 && canceled.All(i => i.State == QueueState.Canceled), "Cancellation prevents later queued work");
        Assert(queue[0].Options.MaximumEdge == 128 && queue[1].Options.Video, "Per-item settings snapshots remain independent");
        var fields = RecordingCsv.Split("\"game, test.exe\",10,\"quoted \"\"text\"\"\"");
        Assert(fields[0] == "game, test.exe" && fields[2] == "quoted \"text\"", "Quoted CSV parsing");
        var metrics = RecordingCsv.Parse(new StringReader("Application,ProcessID,SwapChainAddress,TimeInSeconds,MsBetweenPresents,MsBetweenDisplayChange\n\"game, test.exe\",42,0x1,0.01,10,10\ngame,42,0x1,0.02,10,10\ngame,42,0x1,0.03,10,10\ngame,42,0x1,0.04,10,10\nother,99,0x1,0.05,5,5\ngame,42,0x2,0.01,20,NA\ngame,42,0x2,0.03,20,NA\ngame,42,0x1,NA,5,5"), 42);
        var summaries = RecordingCsv.Summaries(metrics);
        Assert(summaries.Count == 3 && summaries.Single(s => s.SwapChain == "0x1" && s.Metric == "Rendered").AverageFps == 100, "FPS uses measured duration and filters PID/invalid data");
        Assert(summaries.Single(s => s.SwapChain == "0x1" && s.Metric == "Rendered").OnePercentLow == 100 && Math.Abs(summaries.Single(s => s.SwapChain == "0x2").AverageFps - 50) < 1e-9, "1% lows and swap-chain/metric separation");
        try
        {
            RecordingCsv.Parse(new StringReader("bad,columns\na,b"), 42);
            throw new Exception("Invalid header accepted");
        }
        catch (IOException) { checks.Add("Unsupported capture schema reported"); }
        var provider = new FakeProvider();
        var plans = new PerformancePlanStore(Path.Combine(root, "plans"));
        PlanAction power = new PlanAction(PlanActionKind.PowerPlan, "power", "new", "Power"), dns = new PlanAction(PlanActionKind.Dns, "adapter", "changed", "DNS");
        provider.Values[(power.Kind, power.Target)] = "original";
        provider.Values[(dns.Kind, dns.Target)] = "automatic";
        var plan = new PerformancePlan(Guid.NewGuid(), "Fixture", [power, dns], "manual notes");
        plans.SavePlan(plan);
        provider.FailTarget = "adapter";
        var applied = await plans.ApplyAsync(plan, provider, default);
        Assert(applied.Kind == TaskOutcomeKind.Partial && provider.Values[(power.Kind, power.Target)] == "new" && provider.Values[(dns.Kind, dns.Target)] == "automatic", "Denied elevation stops plan and retains completed changes");
        var restarted = new PerformancePlanStore(plans.DirectoryPath);
        restarted.Load();
        provider.FailTarget = null;
        var restored = await restarted.RestoreAsync(provider, default);
        Assert(restored.Kind == TaskOutcomeKind.Success && provider.Values[(power.Kind, power.Target)] == "original", "Journal survives restart and restores originals");
        await restarted.ApplyAsync(plan, provider, default);
        provider.Values[(power.Kind, power.Target)] = "external";
        var conflict = await restarted.RestoreAsync(provider, default);
        Assert(conflict.Kind == TaskOutcomeKind.Partial && provider.Values[(power.Kind, power.Target)] == "external", "External changes are left untouched on restore");
        var repeatStore = new PerformancePlanStore(Path.Combine(root, "repeat-plan"));
        var repeatProvider = new FakeProvider();
        repeatProvider.Values[(power.Kind, power.Target)] = "original";
        var repeatPlan = new PerformancePlan(Guid.NewGuid(), "Repeat fixture", [power], "");
        await repeatStore.ApplyAsync(repeatPlan, repeatProvider, default);
        int repeatWrites = repeatProvider.Writes;
        var repeated = await repeatStore.ApplyAsync(repeatPlan, repeatProvider, default);
        Assert(repeated.Kind == TaskOutcomeKind.Success && repeatProvider.Writes == repeatWrites && repeatStore.Changes.Count == 1, "Reapplying verified settings is a no-op and retains the original restoration record");
        repeatProvider.Values[(power.Kind, power.Target)] = "external";
        var changedRepeat = await repeatStore.ApplyAsync(repeatPlan, repeatProvider, default);
        Assert(changedRepeat.Kind == TaskOutcomeKind.Partial && repeatProvider.Values[(power.Kind, power.Target)] == "external", "Reapply protects settings changed outside Apex");
        repeatProvider.Values[(power.Kind, power.Target)] = "new";
        await repeatStore.RestoreAsync(repeatProvider, default);
        Assert(repeatProvider.Values[(power.Kind, power.Target)] == "original", "Repeated apply still restores the first original value");
        Assert(MetricHealth.Classify(45, true, false) == "Cool" && MetricHealth.Classify(88, true, false) == "High" && MetricHealth.Classify(92, false, true) == "High" && MetricHealth.Classify(null, true, false) == "Unavailable", "Thermal/load indicators distinguish cool, high and unavailable readings");
        Assert(ExtensionGroupValidation.Normalize("CS, .py, cs, .JS") == ".cs, .py, .js", "Custom extension groups normalize case and duplicates");
        foreach (string invalid in new[] { "", "../folder", ".cs;*.exe" })
        {
            try
            {
                ExtensionGroupValidation.Normalize(invalid);
                throw new Exception("Invalid group accepted");
            }
            catch (IOException) { checks.Add("Invalid custom extension group rejected: " + invalid); }
        }
        var preferenceFixture = new Preferences();
        preferenceFixture.ExtensionGroups["Scripts"] = ExtensionGroupValidation.Normalize(".ps1, .sh");
        var persistedPreferences = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(preferenceFixture))!;
        Assert(persistedPreferences.ExtensionGroups["Scripts"] == ".ps1, .sh", "Custom sorting groups survive preference serialization");
        var budgetRoot = Path.Combine(root, "bounded-scan");
        Directory.CreateDirectory(budgetRoot);
        for (int i = 0; i < 24; i++)
            await File.WriteAllTextAsync(Path.Combine(budgetRoot, i + ".txt"), "fixture");
        var budgetScan = await FileToolkit.ScanAsync(budgetRoot, true, null, default, 10);
        Assert(budgetScan.Files.Count == 10 && budgetScan.Issues.Any(i => i.Contains("Results are partial")), "Large scans stop at the entry budget with an explicit partial-results warning");
        using (var scanCancel = new CancellationTokenSource())
        {
            scanCancel.Cancel();
            try
            {
                await FileToolkit.ScanAsync(budgetRoot, true, null, scanCancel.Token);
                throw new Exception("Canceled scan ran");
            }
            catch (OperationCanceledException) { checks.Add("Cancellation stops scans before enumeration"); }
        }
        var manual = new PlanAction(PlanActionKind.Manual, "fps", "unverified", "Graphics");
        var manualPlan = new PerformancePlan(Guid.NewGuid(), "Manual", [manual], "");
        int before = provider.Writes;
        await restarted.ApplyAsync(manualPlan, provider, default);
        Assert(provider.Writes == before, "Manual steps never execute settings writes");
        Assert(WindowsPerformance.NormalizeDns("1.1.1.1 8.8.8.8", "2606:4700:4700::1111").IPv4 == "1.1.1.1,8.8.8.8", "DNS input normalization");
        try
        {
            WindowsPerformance.NormalizeDns("::1", "");
            throw new Exception("Invalid DNS accepted");
        }
        catch (IOException) { checks.Add("DNS family mismatch rejected"); }
        var store = new RecordingStore(Path.Combine(root, "recordings"));
        var record = new GameRecording(Guid.NewGuid(), "Fixture", "", 42, 123, "game.exe", DateTime.UtcNow, 60, false, "Stopped", summaries, [], plan);
        store.Save(record);
        Assert(store.List().Single().Id == record.Id && !store.List()[0].Complete, "Incomplete recordings persist accurately");
        string csv = Path.Combine(store.Folder(record.Id), "frames.csv");
        await File.WriteAllTextAsync(csv, "fixture");
        string export = Path.Combine(root, "export");
        Directory.CreateDirectory(export);
        store.Export(record, export);
        Assert(Directory.EnumerateFiles(export).Count() == 2, "Recording CSV/JSON export");
        try
        {
            store.Export(record, export);
            throw new Exception("Export overwrite allowed");
        }
        catch (IOException) { checks.Add("Recording export never overwrites files"); }
        var fakeIdentity = new PlanAction(PlanActionKind.Priority, "fixture-pid", "AboveNormal", "Fixture process", 123);
        var exited = new FakeProvider { FailReadTarget = "fixture-pid" };
        var exitedPlan = new PerformancePlan(Guid.NewGuid(), "Exited process", [fakeIdentity], "");
        Assert((await plans.ApplyAsync(exitedPlan, exited, default)).Kind == TaskOutcomeKind.Partial, "Exited/reused process is skipped without blocking other tweaks");
        Assert(exited.Writes == 0, "Missing target never receives priority writes");
        var disconnected = new SensorPacket([], "Sensor helper disconnected");
        Assert(disconnected.Readings.Count == 0 && disconnected.Error is not null, "Disconnected sensors retain a reason rather than zero readings");
        using (var monitor = new WindowsMonitoring())
        {
            await monitor.SampleAsync(default);
            await Task.Delay(1000);
            var sample = await monitor.SampleAsync(default);
            Assert(sample.MemoryTotal > 0 && sample.MemoryUsed >= 0 && sample.MemoryUsed <= sample.MemoryTotal && sample.Cpu is >= 0 and <= 100, "Read-only Windows CPU/RAM sampling returns valid units");
            Assert(sample.Processes.All(p => p.Cpu >= 0 && p.Cpu <= 100 && p.Started > 0), "Process monitoring includes identity and bounded CPU usage");
        }
        string moveSource = Path.Combine(root, "move-source"), moveOutput = Path.Combine(root, "move-output");
        Directory.CreateDirectory(moveSource);
        Directory.CreateDirectory(moveOutput);
        await File.WriteAllTextAsync(Path.Combine(moveSource, "one.txt"), "disposable move fixture");
        var moveToolkit = new FileToolkit(Path.Combine(root, "move-journal.json"));
        var moveScan = await FileToolkit.ScanAsync(moveSource, false, null, default);
        var moved = await moveToolkit.MoveAsync(FileToolkit.Plan(moveScan, [new SortRule(".txt", moveOutput)]), null, default);
        Assert(moved.Completed == 1 && moved.Outputs?.Count == 1 && File.Exists(moved.Outputs[0].Destination), "Organize completed results retain actual destinations");
        var draft = new PerformanceDraft();
        int writesBeforeDraft = provider.Writes;
        Assert(draft.Actions.Count == 0, "Custom preset starts with no selected actions");
        draft.Toggle(dns, true);
        draft.SelectBuiltIn("Performance", new Dictionary<Guid, string> { [PerformanceDraft.HighPerformanceScheme] = "High performance" });
        Assert(draft.Actions[0].Kind == PlanActionKind.PowerPlan && draft.Actions.Count(a => a.Kind == PlanActionKind.VisualEffect) == WindowsVisualEffects.Supported.Count && provider.Writes == writesBeforeDraft, "Performance stages broader appearance and gaming steps without applying them");
        draft.SelectCustom();
        Assert(draft.Actions.Single() == dns, "Custom selections survive switching presets");
        draft.SelectBuiltIn("Performance", new Dictionary<Guid, string> { [PerformanceDraft.HighPerformanceScheme] = "High performance" });
        draft.Toggle(manual, true);
        Assert(draft.Selected == "Custom" && draft.Actions.Count > WindowsVisualEffects.Supported.Count && draft.Actions.Any(a => a.Value == PerformanceDraft.HighPerformanceScheme.ToString()), "Editing a built-in preset keeps its power change in a Custom draft");
        var previousDraft = draft.Snapshot();
        try
        {
            draft.SelectBuiltIn("Performance", new Dictionary<Guid, string>());
            throw new Exception("Missing scheme substituted");
        }
        catch (IOException) { checks.Add("Unavailable built-in power scheme is rejected without substitution"); }
        Assert(draft.Actions.SequenceEqual(previousDraft.Actions), "Unavailable presets preserve the pending draft");
        draft.Load(plan);
        Assert(draft.Snapshot().Id == plan.Id && draft.Actions.SequenceEqual(plan.Actions), "Legacy saved plans load as Custom presets without migration");
        draft.Clear();
        Assert(draft.Actions.Count == 0 && provider.Writes == writesBeforeDraft, "Clearing selections never applies settings");
        var percentage = options with
        {
            Method = CompressionMethod.TargetPercent,
            TargetPercent = 60,
            PreserveDimensions = true
        };
        Assert(percentage.ResolveTarget(101) == 60 && percentage.ResolveTarget(1000) == 600, "Percentage caps are calculated independently per source with rounding down");
        Assert((percentage with
        {
            Method = CompressionMethod.Quality,
            TargetBytes = 100000
        }).ResolveTarget(1000) == 0, "Quality mode ignores stale size targets");
        try
        {
            (percentage with
            {
                TargetPercent = 0
            }).ResolveTarget(1000);
            throw new Exception("Invalid percent accepted");
        }
        catch (IOException) { checks.Add("Percentage target bounds validated"); }
        var effect = new PlanAction(PlanActionKind.VisualEffect, "ClientAnimation", "Disabled", "Control animations");
        var visuals = new PerformancePlanStore(Path.Combine(root, "visuals"));
        var effectsProvider = new FakeProvider();
        effectsProvider.Values[(effect.Kind, effect.Target)] = "Enabled";
        await visuals.ApplyAsync(new(Guid.NewGuid(), "Visual fixture", [effect], ""), effectsProvider, default);
        await visuals.RestoreAsync(effectsProvider, default);
        Assert(effectsProvider.Values[(effect.Kind, effect.Target)] == "Enabled", "Visual effects use the same original-value restoration journal");
        await visuals.ApplyAsync(new(Guid.NewGuid(), "Visual fixture", [effect], ""), effectsProvider, default);
        effectsProvider.Values[(effect.Kind, effect.Target)] = "external";
        Assert((await visuals.RestoreAsync(effectsProvider, default)).Kind == TaskOutcomeKind.Partial && effectsProvider.Values[(effect.Kind, effect.Target)] == "external", "Visual-effect restoration protects external changes");
        var firstFile = Path.Combine(root, "compare-left.txt");
        var secondFile = Path.Combine(root, "compare-right.txt");
        await File.WriteAllTextAsync(firstFile, "alpha");
        await File.WriteAllTextAsync(secondFile, "bravo");
        var diff = await FileToolkit.CompareAsync([FileToolkit.Inspect(firstFile) with { RelativePath = "Selected files" }], [FileToolkit.Inspect(secondFile) with { RelativePath = "Selected files" }], true, null, default);
        Assert(diff.Count == 1 && diff[0].State != "Identical", "Two individual files can be compared with verified contents");
        var invalidTemperatures = new SensorPacket([new("CPU", "CPU Package", "Cpu", 0, "C"), new("CPU", "CPU Tctl", "Cpu", double.NaN, "C")], null);
        Assert(SensorValidation.Temperature(invalidTemperatures, "Cpu") is null, "Invalid zero/NaN CPU temperatures are unavailable rather than a false zero");
        invalidTemperatures.Readings.Add(new("CPU", "CPU Package", "Cpu", 61, "C"));
        invalidTemperatures.Readings.Add(new("CPU", "Distance to limit", "Cpu", 100, "C"));
        Assert(SensorValidation.Temperature(invalidTemperatures, "Cpu") == 61, "CPU package temperature excludes distance/limit sensors");
        Assert(SensorValidation.Temperature(new([new("GPU", "GPU Memory", "GpuNvidia", 90, "C")], null), "Gpu") is null, "GPU memory temperature cannot masquerade as core temperature");
        var smbios = new byte[8 + 35 + 3 + 4 + 2];
        smbios[8] = 17;
        smbios[9] = 35;
        BitConverter.GetBytes((ushort)32767).CopyTo(smbios, 20);
        BitConverter.GetBytes(32768u).CopyTo(smbios, 36);
        smbios[26] = 0x22;
        BitConverter.GetBytes((ushort)6000).CopyTo(smbios, 40);
        smbios[31] = 1;
        smbios[43] = (byte)'X';
        smbios[44] = 0;
        smbios[45] = 0;
        smbios[46] = 127;
        smbios[47] = 4;
        var memoryIdentity = RamHardware.Parse(smbios);
        Assert(memoryIdentity.InstalledBytes == 32L * 1073741824 && memoryIdentity.Name.Contains("DDR5") && memoryIdentity.Name.Contains("6000"), "RAM identity decodes installed capacity, type, speed and manufacturer");
        // All settings changes above use FakeProvider. No actual process, driver, power or DNS change occurs.
        checks.AddRange(await GameBoosterChecks.RunAsync(Path.Combine(root, "booster")));
        var checklist = new ManualChecklist(Path.Combine(root, "manual-checklist"));
        var checklistPlan = new PerformancePlan(Guid.NewGuid(), "Manual fixture", [new(PlanActionKind.Manual, "GameMode", "Manual; unverified", "Review Game Mode")], "");
        checklist.Stage(checklistPlan);
        Assert(checklist.Steps.Single().Status == "Pending", "Manual-only application produces pending instructions");
        checklist.Mark(checklist.Steps[0].Id, "Done");
        var reload = new ManualChecklist(Path.Combine(root, "manual-checklist"));
        reload.Load();
        Assert(reload.Steps.Single().Status == "Done", "User-marked manual status survives restart without claiming verification");
        reload.Mark(reload.Steps[0].Id, "Skipped");
        reload.Load();
        Assert(reload.Steps.Single().Status == "Skipped", "Manual step can be skipped persistently");
        var oldFixture = FileToolkit.Inspect(file) with
        {
            ModifiedUtc = DateTime.UtcNow.AddDays(-3)
        };
        Assert(!SystemCleanRules.GameJunk(oldFixture with
        {
            Path = Path.Combine(root, "save.dat")
        }) && !SystemCleanRules.GameJunk(oldFixture with
        {
            Path = Path.Combine(root, "shaders", "x.tmp")
        }), "Game cleanup excludes saves and shader data");
        Assert(SystemCleanRules.GameJunk(oldFixture with
        {
            Path = Path.Combine(root, "crash.dmp")
        }) && SystemCleanRules.Backup(oldFixture with
        {
            Path = Path.Combine(root, "backup.bak")
        }), "Game and backup cleanup use limited file types");
        Assert(!SystemCleanRules.CacheFile(Path.Combine(root, "Cookies"), Path.Combine(root, "Cache_Data")) && !SystemCleanRules.CacheFile(Path.Combine(root, "History"), root) && SystemCleanRules.CacheFile(Path.Combine(root, "Cache_Data", "item"), Path.Combine(root, "Cache_Data")), "Cache-only policy excludes browser history and cookies");
        var steam = GameManifest.Steam("\"appid\" \"123\" \"name\" \"Fixture game\" \"installdir\" \"Fixture\"", root);
        Assert(steam.Name == "Fixture game" && steam.Folder == Path.Combine(root, "steamapps", "common", "Fixture"), "Steam discovery resolves game identities from bounded launcher metadata");
        Assert(GameManifest.Entries("\"0\" { \"path\" \"D:\\\\Steam\" } \"1\" { \"path\" \"E:\\\\Steam\" }").Where(p => p.Key == "path").Select(p => p.Value).SequenceEqual(new[] { @"D:\Steam", @"E:\Steam" }), "Multiple Steam libraries retain every path and decode escaped separators");
        try
        {
            GameManifest.Steam("\"appid\" \"123\" \"installdir\" \"../outside\"", root);
            throw new Exception("Unsafe Steam path accepted");
        }
        catch (IOException) { checks.Add("Steam install directory traversal rejected"); }
        var epic = GameManifest.Epic(System.Text.Json.JsonSerializer.Serialize(new
        {
            DisplayName = "Epic fixture",
            InstallLocation = Path.Combine(root, "Epic fixture"),
            LaunchExecutable = "FixtureGame/Binaries/game.exe"
        }));
        Assert(epic.Name == "Epic fixture" && epic.LaunchFolder == "FixtureGame", "Epic discovery separates install identity and user-data folder hints");
        try
        {
            GameManifest.Epic(System.Text.Json.JsonSerializer.Serialize(new
            {
                DisplayName = "Incomplete",
                InstallLocation = root,
                bIsIncompleteInstall = true
            }));
            throw new Exception("Incomplete install accepted");
        }
        catch (IOException) { checks.Add("Incomplete Epic installs are excluded"); }
        try
        {
            GameManifest.Epic("{\"DisplayName\":\"Invalid\",\"InstallLocation\":\"relative\"}");
            throw new Exception("Relative install accepted");
        }
        catch (IOException) { checks.Add("Relative game install locations rejected"); }
        var sortingRoot = Path.Combine(root, "sorting-presets");
        Directory.CreateDirectory(sortingRoot);
        foreach (string leaf in new[] { "photo.JPG", "clip.MP4", "notes.pdf" })
            await File.WriteAllTextAsync(Path.Combine(sortingRoot, leaf), "fixture");
        foreach (string category in SortingPresets.Items.Keys)
            Directory.CreateDirectory(Path.Combine(root, "sorted", category));
        var sortingScan = await FileToolkit.ScanAsync(sortingRoot, true, null, default);
        var sortingPlan = FileToolkit.Plan(sortingScan, SortingPresets.Items.Select(p => new SortRule(p.Value, Path.Combine(root, "sorted", p.Key))).ToList());
        Assert(sortingPlan.Count == 3 && sortingPlan.All(p => p.State == "Ready") && sortingPlan.Any(p => p.Destination.Contains("Pictures")) && sortingPlan.Any(p => p.Destination.Contains("Videos")) && sortingPlan.Any(p => p.Destination.Contains("Documents")), "Sorting presets preview picture/document/video moves with case-insensitive extensions without moving originals");
        return checks;
    }
    private sealed class FakeProvider : IPerformanceProvider
    {
        public Dictionary<(PlanActionKind, string), string> Values { get; } = []; public string? FailTarget; public string? FailReadTarget; public int Writes;
        public Task<string> ReadAsync(PlanAction a, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (a.Target == FailReadTarget)
                throw new IOException("Fixture process exited or PID reused");
            return Task.FromResult(Values.GetValueOrDefault((a.Kind, a.Target), "original"));
        }
        public Task WriteAsync(PlanAction a, string value, string expected, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (a.Target == FailTarget)
                throw new UnauthorizedAccessException("Fixture refusal");
            if (Values.GetValueOrDefault((a.Kind, a.Target), "original") != expected)
                throw new IOException("Fixture conflict");
            Values[(a.Kind, a.Target)] = value;
            Writes++;
            return Task.CompletedTask;
        }
    }
}
