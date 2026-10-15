namespace Apex.Services;
public static class CompressionBatch
{
    public static async Task<TaskOutcome> RunAsync(IEnumerable<CompressionQueueItem> queue, Func<CompressionQueueItem, CancellationToken, Task<CompressionResult>> compress, Action<CompressionQueueItem> changed, CancellationToken token)
    {
        var ready = queue.Where(i => i.State == QueueState.Ready).ToArray();
        if (ready.Length == 0)
            throw new IOException("Add files to the queue first.");
        int done = 0, failed = 0, canceled = 0;
        foreach (var item in ready)
        {
            if (token.IsCancellationRequested)
            {
                item.State = QueueState.Canceled;
                item.Detail = "Canceled before processing";
                canceled++;
                changed(item);
                continue;
            }
            item.State = QueueState.Working;
            item.Detail = "Starting";
            changed(item);
            try
            {
                item.Output = await compress(item, token);
                item.State = QueueState.Completed;
                item.Detail = "Copy saved";
                done++;
            }
            catch (OperationCanceledException) { item.State = QueueState.Canceled; item.Detail = "Canceled; original retained"; canceled++; }
            catch (Exception e) { item.State = QueueState.Failed; item.Detail = e.Message; failed++; }
            changed(item);
        }
        return new(canceled > 0 ? TaskOutcomeKind.Canceled : failed > 0 ? (done > 0 ? TaskOutcomeKind.Partial : TaskOutcomeKind.Error) : TaskOutcomeKind.Success,
            $"Batch finished: {done} saved, {failed} failed, {canceled} canceled", ready.Where(i => i.Output is not null).Select(i => new OutputReference(i.Output!.Output, Path.GetFileName(i.Output.Output))).ToArray());
    }
}
