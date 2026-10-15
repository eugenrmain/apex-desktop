using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using Apex.Services;

namespace Apex.Platform;
public sealed class GameCapture(IMonitoringService monitoring, RecordingStore store) : IRecordingService
{
    public bool IsRecording
    {
        get; private set;
    }
    public bool RequestElevation
    {
        get; set;
    }
    public async Task<GameRecording> RecordAsync(ProcessUsage selected, string name, string notes, int seconds, PerformancePlan? plan, Action<string> progress, CancellationToken token)
    {
        if (IsRecording)
            throw new IOException("A recording is already running.");
        if (!selected.CanClose || seconds is < 1 or > 900)
            throw new IOException("Choose a non-system game and a duration of 1-900 seconds.");
        using var target = Process.GetProcessById(selected.Id);
        if (target.StartTime.ToUniversalTime().Ticks != selected.Started)
            throw new IOException("The selected game changed.");
        IsRecording = true;
        var samples = new List<MonitorSample>();
        Guid id = Guid.NewGuid();
        DateTime started = DateTime.UtcNow;
        string? issue = null;
        bool complete = false;
        string pipeName = "Apex.Capture." + id.ToString("N"), nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        Process? helper = null;
        store.Save(new(id, string.IsNullOrWhiteSpace(name) ? "Recording" : name.Trim(), notes, selected.Id, selected.Started, selected.Executable, started, seconds, false, "Interrupted or not finalized", [], [], plan));
        try
        {
            for (int i = 3; i > 0; i--)
            {
                progress("Recording starts in " + i);
                await Task.Delay(1000, token);
            }
            started = DateTime.UtcNow;
            // ETW access varies by account; only explicit capture starts this elevated helper.
            helper = Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Apex.Capture.exe")) { UseShellExecute = true, Verb = RequestElevation ? "runas" : "", WindowStyle = ProcessWindowStyle.Hidden, Arguments = "--pipe " + pipeName + " --token " + nonce }) ?? throw new IOException("Capture helper did not start.");
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(token);
            connect.CancelAfter(TimeSpan.FromSeconds(25));
            await pipe.WaitForConnectionAsync(connect.Token);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await writer.WriteLineAsync(JsonSerializer.Serialize(new
            {
                Token = nonce,
                Id = id.ToString("N"),
                Pid = selected.Id,
                Started = selected.Started,
                Executable = selected.Executable,
                Seconds = seconds
            }));
            string liveFps = "";
            var reply = reader.ReadLineAsync();
            bool stopSent = false;
            var began = Stopwatch.StartNew();
            while (true)
            {
                if (token.IsCancellationRequested && !stopSent)
                {
                    await writer.WriteLineAsync("Stop");
                    stopSent = true;
                }
                if (reply.IsCompleted)
                {
                    string? line = await reply;
                    if (line is null)
                    {
                        issue = "Capture helper disconnected.";
                        break;
                    }
                    var envelope = JsonSerializer.Deserialize<Reply>(line) ?? throw new IOException("Invalid capture reply.");
                    if (envelope.Token != nonce)
                        throw new IOException("Invalid capture session.");
                    if (envelope.Kind == "Progress")
                    {
                        liveFps = envelope.Error ?? "";
                        progress("Recording " + Math.Min(seconds, (int)began.Elapsed.TotalSeconds) + " / " + seconds + "s - " + envelope.Error);
                        reply = reader.ReadLineAsync();
                        continue;
                    }
                    if (envelope.Kind != "Started")
                    {
                        issue = envelope.Error;
                        complete = envelope.Kind == "Finished" && issue is null && !stopSent;
                        break;
                    }
                    started = DateTime.UtcNow;
                    began.Restart();
                    reply = reader.ReadLineAsync();
                }
                if (began.Elapsed.TotalSeconds > seconds + 30)
                {
                    await writer.WriteLineAsync("Stop");
                    issue = "Capture timed out.";
                    break;
                }
                string csv = Path.Combine(store.Folder(id), "frames.csv");
                if (File.Exists(csv) && new FileInfo(csv).Length > 250_000_000)
                {
                    await writer.WriteLineAsync("Stop");
                    issue = "Capture reached the 250 MB limit.";
                    break;
                }
                var sample = await monitoring.SampleAsync(CancellationToken.None);
                samples.Add(sample with
                {
                    Processes = sample.Processes.Where(p => p.Id == selected.Id).ToList()
                });
                progress($"Recording {Math.Min(seconds, (int)began.Elapsed.TotalSeconds)} / {seconds}s" + (liveFps.Length == 0 ? "" : " - " + liveFps));
                await Task.WhenAny(reply, Task.Delay(1000));
            }
            if (!helper.HasExited)
                await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
        }
        catch (OperationCanceledException) { issue = "Canceled before capture started."; }
        catch (Exception e) { issue = e.Message; }
        finally { helper?.Dispose(); IsRecording = false; }
        if (token.IsCancellationRequested)
        {
            complete = false;
            issue ??= "Stopped by user.";
        }
        List<FrameSummary> frames = [];
        string file = Path.Combine(store.Folder(id), "frames.csv");
        if (File.Exists(file))
        {
            try
            {
                using var csv = new StreamReader(file);
                frames = await Task.Run(() => RecordingCsv.Summaries(RecordingCsv.Parse(csv, selected.Id)));
            }
            catch (Exception e) { issue = "Frame data could not be read: " + e.Message; complete = false; }
        }
        if (frames.Count == 0)
        {
            complete = false;
            issue ??= "No frames were captured. Check game/API support and capture permissions.";
        }
        if (samples.Count < Math.Max(1, seconds - 3))
        {
            complete = false;
            issue ??= "The game ended before the requested duration.";
        }
        var record = new GameRecording(id, string.IsNullOrWhiteSpace(name) ? "Recording" : name.Trim(), notes, selected.Id, selected.Started, selected.Executable, started, seconds, complete, issue, frames, samples, plan);
        store.Save(record);
        return record;
    }
    private sealed record Reply(string Token, string Kind, string? Error);
}
