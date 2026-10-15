using System.Text.Json;

namespace Apex.Services;
public enum PlanActionKind
{
    PowerPlan, Dns, StartupRun, StartupShortcut, Priority, Manual, VisualEffect, Mouse, Display
}
public sealed record PlanAction(PlanActionKind Kind, string Target, string Value, string Label, long ProcessStarted = 0);
public sealed record PerformancePlan(Guid Id, string Name, List<PlanAction> Actions, string Notes, int Version = 1);
public sealed record AppliedChange(Guid Id, Guid PlanId, PlanAction Action, string Before, string After, string State, string? Error = null);
public sealed record DnsChoice(string IPv4, string IPv6);
public interface IPerformanceProvider
{
    Task<string> ReadAsync(PlanAction action, CancellationToken token);
    Task WriteAsync(PlanAction action, string value, string expected, CancellationToken token);
}
public sealed class PerformancePlanStore
{
    public string DirectoryPath
    {
        get;
    }
    public PerformancePlanStore(string? directory = null) => DirectoryPath = directory ?? Path.Combine(AppDataPaths.Root, "performance-v1");
    public List<PerformancePlan> Plans { get; private set; } = [];
    public List<AppliedChange> Changes { get; private set; } = [];
    public void Load()
    {
        var path = Path.Combine(DirectoryPath, "plans.json");
        if (!File.Exists(path))
            return;
        var data = JsonSerializer.Deserialize<Saved>(File.ReadAllText(path)) ?? throw new IOException("Performance records could not be read.");
        if (data.Version != 1)
            throw new IOException("Unsupported performance record version.");
        Plans = data.Plans;
        Changes = data.Changes;
    }
    private FileStream Lock()
    {
        Directory.CreateDirectory(DirectoryPath);
        return new FileStream(Path.Combine(DirectoryPath, "plans.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    public void SavePlan(PerformancePlan plan)
    {
        using var gate = Lock();
        Load();
        Plans.RemoveAll(p => p.Id == plan.Id);
        Plans.Add(plan);
        Persist();
    }
    public void RemovePlan(Guid id)
    {
        using var gate = Lock();
        Load();
        Plans.RemoveAll(p => p.Id == id);
        Persist();
    }
    private sealed record Saved(int Version, List<PerformancePlan> Plans, List<AppliedChange> Changes);
    private void Persist()
    {
        Directory.CreateDirectory(DirectoryPath);
        string path = Path.Combine(DirectoryPath, "plans.json"), temp = path + ".tmp";
        using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(file, new Saved(1, Plans, Changes));
            file.Flush(true);
        }
        File.Move(temp, path, true);
    }
    private void Update(AppliedChange change)
    {
        int i = Changes.FindIndex(c => c.Id == change.Id);
        if (i < 0)
            Changes.Add(change);
        else
            Changes[i] = change;
        Persist();
    }
    public async Task<TaskOutcome> ApplyAsync(PerformancePlan plan, IPerformanceProvider provider, CancellationToken token)
    {
        using var gate = Lock();
        Load();
        if (plan.Actions.GroupBy(a => (a.Kind, a.Target)).Any(g => g.Count() > 1))
            throw new IOException("A plan cannot change the same setting twice.");
        int done = 0, skipped = 0, unchanged = 0;
        foreach (var action in plan.Actions.Where(a => a.Kind != PlanActionKind.Manual))
        {
            token.ThrowIfCancellationRequested();
            string before;
            try
            {
                before = await provider.ReadAsync(action, token);
            }
            catch (Exception e) when (action.Kind == PlanActionKind.Priority && e is not OperationCanceledException) { skipped++; continue; }
            var existing = Changes.LastOrDefault(c => c.Action.Kind == action.Kind && c.Action.Target == action.Target && c.State is "Applied" or "Pending" or "Conflict" && (action.Kind != PlanActionKind.Priority || c.Action.ProcessStarted == action.ProcessStarted));
            if (existing is not null)
            {
                if (existing.State == "Applied" && before == existing.After && action.Value == existing.After)
                {
                    unchanged++;
                    continue;
                }
                return new(TaskOutcomeKind.Partial, $"{done} settings applied · stopped at {action.Label}. Restore or resolve its existing change first.");
            }
            if (before == action.Value)
            {
                unchanged++;
                continue;
            }
            var change = new AppliedChange(Guid.NewGuid(), plan.Id, action, before, action.Value, "Pending");
            Update(change);
            try
            {
                await provider.WriteAsync(action, action.Value, before, token);
                if (await provider.ReadAsync(action, CancellationToken.None) != action.Value)
                    throw new IOException("The setting did not retain the requested value.");
                Update(change with
                {
                    State = "Applied"
                });
                done++;
            }
            catch (Exception e)
            {
                // Retain the intent: a write can partially succeed before an error.
                Update(change with
                {
                    State = "Pending",
                    Error = e.Message
                });
                if (action.Kind == PlanActionKind.Priority && e is not OperationCanceledException)
                {
                    try
                    {
                        await provider.ReadAsync(action, CancellationToken.None);
                    }
                    catch { skipped++; continue; }
                }
                return new(TaskOutcomeKind.Partial, $"Applied {done} changes; stopped at {action.Label}: {e.Message}. Review Restore.");
            }
        }
        return new(skipped > 0 ? TaskOutcomeKind.Partial : TaskOutcomeKind.Success, $"{done} settings applied · {unchanged} already set · {plan.Actions.Count(a => a.Kind == PlanActionKind.Manual)} steps to finish" + (skipped > 0 ? $" · {skipped} unavailable processes skipped" : ""));
    }
    public async Task<TaskOutcome> RestoreAsync(IPerformanceProvider provider, CancellationToken token)
    {
        using var gate = Lock();
        Load();
        int restored = 0, conflicts = 0;
        foreach (var change in Changes.Where(c => c.State is "Applied" or "Pending" or "Conflict").Reverse().ToArray())
        {
            token.ThrowIfCancellationRequested();
            try
            {
                string current = await provider.ReadAsync(change.Action, token);
                if (current == change.Before)
                {
                    Update(change with
                    {
                        State = "Restored",
                        Error = null
                    });
                    restored++;
                    continue;
                }
                if (current != change.After)
                {
                    Update(change with
                    {
                        State = "Conflict",
                        Error = "Changed outside Apex; left untouched."
                    });
                    conflicts++;
                    continue;
                }
                await provider.WriteAsync(change.Action, change.Before, current, token);
                if (await provider.ReadAsync(change.Action, CancellationToken.None) != change.Before)
                    throw new IOException("Restore verification failed.");
                Update(change with
                {
                    State = "Restored",
                    Error = null
                });
                restored++;
            }
            catch (Exception e) { Update(change with { State = "Conflict", Error = e.Message }); conflicts++; }
        }
        return new(conflicts > 0 ? TaskOutcomeKind.Partial : TaskOutcomeKind.Success, $"Restored {restored} changes; {conflicts} conflicts left untouched.");
    }
}
