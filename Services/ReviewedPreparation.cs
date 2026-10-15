namespace Apex.Services;
public interface IReviewedCleanupProvider
{
    Task EmptyRecycleBinAsync(CancellationToken token);
    Task RecycleReviewedAsync(CleanItem file, CancellationToken token);
}
public static class ReviewedPreparation
{
    public static async Task<GameBoostResult> RunAsync(IReadOnlyList<BoostAppSelection> apps, IReadOnlyList<CleanItem> files, bool emptyBin, IGameBoostProvider appsProvider, IReviewedCleanupProvider cleanupProvider, Action<GameBoostResult> persist, IProgress<string>? progress, CancellationToken token)
    {
        var result = await GameBooster.PrepareAsync(apps, [], appsProvider, persist, progress, token);
        async Task Action(string kind, string target, Func<Task> execute)
        {
            result.Actions.Add(new(kind, target, "Pending", "Awaiting completion"));
            persist(result);
            string state = "Completed", detail = "";
            try
            {
                token.ThrowIfCancellationRequested();
                await execute();
                detail = kind == "Recycle Bin" ? "Existing contents permanently emptied" : "Recycled; restore through Windows Recycle Bin";
            }
            catch (OperationCanceledException) { state = "Canceled"; detail = "Not completed"; }
            catch (Exception e) { state = "Failed"; detail = e.Message; }
            result.Actions[^1] = new(kind, target, state, detail);
            persist(result);
        }
        // Empty the reviewed old Bin before recycling newly removed files.
        if (emptyBin)
            await Action("Recycle Bin", "Recycle Bin", () => cleanupProvider.EmptyRecycleBinAsync(token));
        foreach (var category in files.GroupBy(f => f.Category))
        {
            var batch = category.ToArray();
            int slot = result.Actions.Count;
            result.Actions.Add(new(category.Key, category.Key, "Pending", "Cleanup started; an interrupted run may have recycled some files.", Count: batch.Length));
            persist(result);
            int done = 0, failed = 0, canceled = 0;
            var errors = new List<string>();
            foreach (var file in batch)
            {
                if (token.IsCancellationRequested)
                {
                    canceled = batch.Length - done - failed;
                    break;
                }
                progress?.Report("Cleaning: " + file.Candidate.File.RelativePath);
                try
                {
                    await cleanupProvider.RecycleReviewedAsync(file, token);
                    done++;
                }
                catch (OperationCanceledException) { canceled = batch.Length - done - failed; break; }
                catch (Exception e) { failed++; if (errors.Count < 12) errors.Add(Path.GetFileName(file.Candidate.File.Path) + ": " + e.Message); }
            }
            result.Actions.RemoveAt(slot);
            if (done > 0)
                result.Actions.Add(new(category.Key, category.Key, "Completed", $"{done} files recycled; restore through Windows Recycle Bin.", Count: done));
            if (failed > 0)
                result.Actions.Add(new(category.Key, category.Key, "Failed", string.Join("\n", errors), Count: failed));
            if (canceled > 0)
                result.Actions.Add(new(category.Key, category.Key, "Canceled", $"{canceled} files not processed.", Count: canceled));
            persist(result);
        }
        return result;
    }
}
