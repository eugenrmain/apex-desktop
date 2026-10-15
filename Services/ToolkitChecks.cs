using Apex.Platform;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Apex.Services;

public static class ToolkitChecks
{
    public static async Task<List<string>> RunAsync(string root)
    {
        Directory.CreateDirectory(root);
        var checks = new List<string>();
        void Assert(bool value, string name)
        {
            if (!value)
                throw new InvalidOperationException(name);
            checks.Add(name);
        }
        string a = Path.Combine(root, "left"), b = Path.Combine(root, "right"), dest = Path.Combine(root, "sorted");
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);
        Directory.CreateDirectory(dest);
        await File.WriteAllTextAsync(Path.Combine(a, "日本語-å.png"), "same");
        await File.WriteAllTextAsync(Path.Combine(b, "日本語-å.png"), "same");
        await File.WriteAllTextAsync(Path.Combine(a, "changed.pdf"), "aaaa");
        await File.WriteAllTextAsync(Path.Combine(b, "changed.pdf"), "bbbb");
        File.SetLastWriteTimeUtc(Path.Combine(b, "changed.pdf"), File.GetLastWriteTimeUtc(Path.Combine(a, "changed.pdf")));
        await File.WriteAllTextAsync(Path.Combine(a, "left-only.txt"), "left");
        await File.WriteAllTextAsync(Path.Combine(b, "right-only.txt"), "right");
        string sub = Path.Combine(a, "nested");
        Directory.CreateDirectory(sub);
        await File.WriteAllTextAsync(Path.Combine(sub, "nested.png"), "nested");
        var aa = await FileToolkit.ScanAsync(a, true, null, default);
        var bb = await FileToolkit.ScanAsync(b, true, null, default);
        Assert(aa.Files.Count == 4 && aa.Files.Sum(f => f.Size) == 18, "Recursive sizes and Unicode paths");
        var shallow = await FileToolkit.ScanAsync(a, false, null, default);
        Assert(shallow.Files.Count == 3, "Immediate children default");
        var metadata = await FileToolkit.CompareAsync(aa.Files, bb.Files, false, null, default);
        Assert(metadata.Single(r => r.Path == "changed.pdf").State == "Matching metadata", "Metadata does not assert content equality");
        var hashed = await FileToolkit.CompareAsync(aa.Files, bb.Files, true, null, default);
        Assert(hashed.Single(r => r.Path == "changed.pdf").State == "Different" && hashed.Single(r => r.Path == "日本語-å.png").State == "Identical", "SHA-256 comparison");
        Assert(hashed.Any(r => r.State == "Left only") && hashed.Any(r => r.State == "Right only"), "Both comparison directions");
        var deniedDirectory = Directory.CreateDirectory(Path.Combine(a, "inaccessible"));
        var originalAcl = deniedDirectory.GetAccessControl();
        try
        {
            var deniedAcl = deniedDirectory.GetAccessControl();
            deniedAcl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny));
            deniedDirectory.SetAccessControl(deniedAcl);
            var inaccessible = await FileToolkit.ScanAsync(a, true, null, default);
            Assert(inaccessible.Issues.Any(i => i.Contains("inaccessible")), "Inaccessible directory reported");
        }
        finally { originalAcl.SetSecurityDescriptorSddlForm(originalAcl.GetSecurityDescriptorSddlForm(AccessControlSections.Access), AccessControlSections.Access); deniedDirectory.SetAccessControl(originalAcl); }
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            bool canceled = false;
            try
            {
                await FileToolkit.ScanAsync(a, true, null, cancellation.Token);
            }
            catch (OperationCanceledException) { canceled = true; }
            Assert(canceled, "Canceled scan");
        }
        string link = Path.Combine(a, "loop");
        try
        {
            Directory.CreateSymbolicLink(link, a);
            var linked = await FileToolkit.ScanAsync(a, true, null, default);
            Assert(linked.SkippedLinks == 1 && linked.Files.Count == 4, "Directory-link loop skipped");
        }
        catch (Exception e) when (e is UnauthorizedAccessException || e is IOException && (e.HResult & 0xffff) == 1314)
        {
            // A junction does not need symbolic-link privilege and exercises the same link guard.
            var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "/c", "mklink", "/J", link, a })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
                throw new IOException("Cannot create disposable junction fixture: " + await process.StandardError.ReadToEndAsync());
            var linked = await FileToolkit.ScanAsync(a, true, null, default);
            Assert(linked.SkippedLinks == 1 && linked.Files.Count == 4, "Directory-junction loop skipped");
        }
        string longDirectory = Path.Combine(a, new string('x', 100), new string('y', 100), new string('z', 80));
        Directory.CreateDirectory(longDirectory);
        await File.WriteAllTextAsync(Path.Combine(longDirectory, "long.txt"), "long");
        var longScan = await FileToolkit.ScanAsync(a, true, null, default);
        Assert(longScan.Files.Any(e => e.Path.Length > 260), "Long paths scanned");
        await File.WriteAllTextAsync(Path.Combine(dest, "changed.pdf"), "existing");
        var plan = FileToolkit.Plan(aa, [new("png,pdf", dest)]);
        Assert(plan.Single(p => p.Source.RelativePath == "changed.pdf").State == "Skip collision", "Existing destination collision skipped");
        var journal = new FileToolkit(Path.Combine(root, "journal.json"));
        var moved = await journal.MoveAsync(plan, null, default);
        Assert(moved.Completed == 2, "Files moved without overwriting");
        var changedPlan = FileToolkit.Plan(aa, [new("txt", dest)]);
        var stalePlan = changedPlan.Single(p => p.Source.RelativePath == "left-only.txt");
        await File.WriteAllTextAsync(stalePlan.Source.Path, "changed before moving");
        var rejectedMove = await journal.MoveAsync([stalePlan], null, default);
        Assert(rejectedMove.Completed == 0 && rejectedMove.Issues.Count == 1, "Move rejects changed source");
        string batch = Path.Combine(root, "batch"), batchDestination = Path.Combine(root, "batch-destination");
        Directory.CreateDirectory(batch);
        Directory.CreateDirectory(batchDestination);
        foreach (var name in new[] { "one.bin", "two.bin", "three.bin" })
            await File.WriteAllTextAsync(Path.Combine(batch, name), name);
        var batchPlans = FileToolkit.Plan(await FileToolkit.ScanAsync(batch, false, null, default), [new("bin", batchDestination)]).OrderBy(p => p.Source.Path).ToArray();
        await File.WriteAllTextAsync(batchPlans[1].Source.Path, "changed after planning");
        var batchJournal = new FileToolkit(Path.Combine(root, "batch-journal.json"));
        var partial = await batchJournal.MoveAsync(batchPlans, null, default);
        Assert(partial.Completed == 2 && partial.Issues.Count == 1, "Partial batch reports failures and keeps completed moves");
        var restoredBatch = await new FileToolkit(batchJournal.JournalPath).UndoAsync(null, default);
        Assert(restoredBatch.Completed == 2, "Undo history survives service restart");
        var cancelPlans = FileToolkit.Plan(await FileToolkit.ScanAsync(batch, false, null, default), [new("bin", batchDestination)]);
        using (var cancelMoves = new CancellationTokenSource())
        {
            var interrupted = await batchJournal.MoveAsync(cancelPlans, new InlineProgress(_ => cancelMoves.Cancel()), cancelMoves.Token);
            Assert(interrupted.Completed == 1 && interrupted.Issues.Count == 1, "Move cancellation retains undo for completed work");
            var restored = await batchJournal.UndoAsync(null, default);
            Assert(restored.Completed == 1, "Canceled batch can be undone");
        }
        await File.WriteAllTextAsync(Path.Combine(a, "日本語-å.png"), "replacement");
        var undo = await journal.UndoAsync(null, default);
        Assert(undo.Completed == 1 && undo.Issues.Count == 1, "Undo skips occupied original path");
        File.Delete(Path.Combine(a, "日本語-å.png"));
        await File.WriteAllTextAsync(Path.Combine(dest, "日本語-å.png"), "edited");
        undo = await journal.UndoAsync(null, default);
        Assert(undo.Completed == 0 && undo.Issues.Count == 1, "Undo skips edited destination");
        await File.WriteAllTextAsync(Path.Combine(a, "duplicate-a.bin"), "duplicate");
        await File.WriteAllTextAsync(Path.Combine(a, "duplicate-b.bin"), "duplicate");
        var dup = await FileToolkit.DuplicatesAsync(await FileToolkit.ScanAsync(a, false, null, default), null, default);
        Assert(dup.Count == 1 && dup[0].File.RelativePath == "duplicate-b.bin", "Duplicate detection keeps one original");
        var stale = FileToolkit.Inspect(Path.Combine(a, "left-only.txt"));
        await File.WriteAllTextAsync(stale.Path, "changed after selection");
        bool rejected = false;
        try
        {
            FileToolkit.Recheck(stale);
        }
        catch (IOException) { rejected = true; }
        Assert(rejected, "Stale target rejected");
        using (var locked = new FileStream(Path.Combine(a, "duplicate-a.bin"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            bool denied = false;
            try
            {
                await FileToolkit.HashAsync(FileToolkit.Inspect(locked.Name), default);
            }
            catch (IOException) { denied = true; }
            Assert(denied, "Locked-file hash reports failure");
            var lockers = WindowsActions.Locks(FileToolkit.Inspect(locked.Name));
            Assert(lockers.Any(p => p.Id == Environment.ProcessId && !p.CanClose), "Lock diagnostics and self-process protection");
        }
        bool protectedTarget = false;
        try
        {
            FileToolkit.ValidateMutation(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        }
        catch (IOException) { protectedTarget = true; }
        Assert(protectedTarget, "System directory mutation blocked");
        var readOnly = FileToolkit.Inspect(Path.Combine(a, "duplicate-a.bin"));
        File.SetAttributes(readOnly.Path, File.GetAttributes(readOnly.Path) | FileAttributes.ReadOnly);
        WindowsActions.ClearReadOnly(readOnly);
        Assert(!File.GetAttributes(readOnly.Path).HasFlag(FileAttributes.ReadOnly), "Selected read-only attribute cleared");
        string recyclePath = Path.Combine(root, "recycle-fixture.txt");
        await File.WriteAllTextAsync(recyclePath, "disposable fixture");
        WindowsActions.Delete(FileToolkit.Inspect(recyclePath), false, 0);
        Assert(!File.Exists(recyclePath), "Disposable fixture recycled through Windows");
        string permanentPath = Path.Combine(root, "permanent-fixture.txt");
        await File.WriteAllTextAsync(permanentPath, "disposable fixture");
        WindowsActions.Delete(FileToolkit.Inspect(permanentPath), true, 0);
        Assert(!File.Exists(permanentPath), "Disposable fixture permanently deleted");
        var helper = Path.Combine(AppContext.BaseDirectory, "Apex.Recovery.exe");
        async Task<int> Helper(params string[] args)
        {
            var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in args)
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        Assert(await Helper("--request", "invalid") != 0, "Recovery helper rejects malformed request");
        var guarded = FileToolkit.Inspect(Path.Combine(a, "duplicate-b.bin"));
        var request = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            File = guarded with
            {
                Size = guarded.Size + 1
            },
            Action = "Permanent"
        })));
        Assert(await Helper("--request", request) != 0 && File.Exists(guarded.Path), "Recovery helper rechecks target before deletion");
        bool selfProtected = false;
        try
        {
            await WindowsActions.CloseAsync(new(Environment.ProcessId, 0, "fixture", "", true), true);
        }
        catch (IOException) { selfProtected = true; }
        Assert(selfProtected, "Force closure rejects Apex itself");
        return checks;
    }
    private sealed class InlineProgress(Action<string> callback) : IProgress<string>
    {
        public void Report(string value) => callback(value);
    }
}
