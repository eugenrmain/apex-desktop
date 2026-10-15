using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;

if (args.Length != 4 || args[0] != "--pipe" || !args[1].StartsWith("Apex.Capture.", StringComparison.Ordinal) || args[2] != "--token" || args[3].Length != 64 || !args[3].All(Uri.IsHexDigit)) return;
using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous);
try
{
    await pipe.ConnectAsync(15000);
    using var input = new StreamReader(pipe, leaveOpen: true); using var output = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
    string? requestLine = await input.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
    var request = JsonSerializer.Deserialize<CaptureRequest>(requestLine ?? "") ?? throw new IOException("Missing request.");
    if (request.Token != args[3] || request.Seconds is < 1 or > 900 || !Guid.TryParseExact(request.Id, "N", out _)) throw new IOException("Invalid capture request.");
    using var target = Process.GetProcessById(request.Pid);
    if (target.StartTime.ToUniversalTime().Ticks != request.Started || target.SessionId != Process.GetCurrentProcess().SessionId) throw new IOException("The selected process changed.");
    string path = target.MainModule?.FileName ?? throw new IOException("Cannot identify the game.");
    if (!path.Equals(request.Executable, StringComparison.OrdinalIgnoreCase) || path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Cannot capture this process.");
    string directory = Path.Combine(Apex.Services.AppDataPaths.Root, "recordings-v1", request.Id);
    for (string? ancestor = directory; ancestor is not null; ancestor = Path.GetDirectoryName(ancestor))
        if (Directory.Exists(ancestor) && (File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked capture directory.");
    Directory.CreateDirectory(directory);
    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked capture directory.");
    string csv = Path.Combine(directory, "frames.csv");
    string executable = Path.Combine(AppContext.BaseDirectory, "Vendor", "PresentMon.exe");
    if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(executable))).ToLowerInvariant() != "b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af") throw new IOException("Capture engine checksum mismatch.");
    string session = "Apex." + request.Id;
    var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (string argument in new[] { "--process_id", request.Pid.ToString(), "--output_stdout", "--v1_metrics", "--session_name", session, "--timed", request.Seconds.ToString(), "--terminate_after_timed", "--terminate_on_proc_exit", "--no_console_stats" }) start.ArgumentList.Add(argument);
    using var capture = Process.Start(start) ?? throw new IOException("Capture did not start.");
    try
    {
    await output.WriteLineAsync(JsonSerializer.Serialize(new { Token = args[3], Kind = "Started", Error = (string?)null }));
    var errorTask = capture.StandardError.ReadToEndAsync();
    using var file = new FileStream(csv, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
    async Task CopyFrames()
    {
        using var raw = new StreamWriter(file, leaveOpen: true);
        string[]? headers = null; int timeColumn = -1, frameColumn = -1, chainColumn = -1;
        var chains = new Dictionary<string, Queue<(double Time, double Ms)>>();
        var clock = Stopwatch.StartNew();
        for (string? line; (line = await capture.StandardOutput.ReadLineAsync()) is not null;)
        {
            await raw.WriteLineAsync(line);
            var row = SplitCsv(line);
            if (headers is null) { headers = row; timeColumn = Array.FindIndex(row,c => c is "TimeInSeconds" or "CPUStartTime"); frameColumn = Array.FindIndex(row,c => c == "MsBetweenPresents"); chainColumn = Array.FindIndex(row,c => c == "SwapChainAddress"); }
            else if (row.Length == headers.Length && timeColumn >= 0 && frameColumn >= 0 && chainColumn >= 0 && double.TryParse(row[timeColumn],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double t) && double.TryParse(row[frameColumn],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double ms) && ms > 0 && double.IsFinite(ms))
            {
                string key = row[chainColumn]; if (!chains.TryGetValue(key,out var frames)) { frames = new(); chains[key] = frames; }
                frames.Enqueue((t,ms)); while (frames.Count > 1 && frames.Peek().Time < t - 2) frames.Dequeue();
            }
            if (clock.Elapsed.TotalSeconds >= 1)
            {
                await raw.FlushAsync();
                if (file.Length > 250_000_000) throw new IOException("Capture exceeded the 250 MB limit.");
                var series = chains.OrderByDescending(c=>c.Value.Count).FirstOrDefault();
                if (series.Value is { Count: > 1 }) await output.WriteLineAsync(JsonSerializer.Serialize(new { Token = args[3], Kind = "Progress", Error = $"Rendered FPS: {1000 / series.Value.Average(f=>f.Ms):0.#} / stream {series.Key}" }));
                clock.Restart();
            }
        }
        await raw.FlushAsync();
    }
    var copy = CopyFrames();

    var stop = input.ReadLineAsync();
    var wait = capture.WaitForExitAsync();
    var winner = await Task.WhenAny(wait, stop, copy);
    bool canceled = winner == stop || (winner == copy && !capture.HasExited);
    if (canceled && !capture.HasExited) capture.Kill();
    await wait; Exception? copyError = null; try { await copy; } catch (Exception e) { copyError = e; } await file.FlushAsync();
    if (canceled)
    {
        var cleanup = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in new[] { "--session_name", session, "--terminate_existing_session" }) cleanup.ArgumentList.Add(argument);
        using var clean = Process.Start(cleanup); if (clean is not null) await clean.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }
    string errors = await errorTask;
    await output.WriteLineAsync(JsonSerializer.Serialize(new { Token = args[3], Kind = canceled ? "Stopped" : "Finished", Error = copyError is not null ? copyError.Message : capture.ExitCode == 0 || canceled ? null : errors.Length > 2000 ? errors[..2000] : errors }));
    }
    finally
    {
        if (!capture.HasExited)
        {
            try { capture.Kill(); await capture.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
            try
            {
                var cleanup = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
                foreach (string argument in new[] { "--session_name", session, "--terminate_existing_session" }) cleanup.ArgumentList.Add(argument);
                using var ownSession = Process.Start(cleanup); if (ownSession is not null) await ownSession.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            } catch { }
        }
    }
}
catch (Exception e)
{
    try { using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true }; await writer.WriteLineAsync(JsonSerializer.Serialize(new { Token = args[3], Kind = "Error", Error = e.Message })); } catch { }
    Environment.ExitCode = 1;
}
static string[] SplitCsv(string line)
{
    var fields = new List<string>(); var field = new System.Text.StringBuilder(); bool quote = false;
    for (int i = 0; i < line.Length; i++)
    {
        char c = line[i]; if (c == '"') { if (quote && i + 1 < line.Length && line[i+1] == '"') { field.Append('"');i++; } else quote = !quote; }
        else if (c == ',' && !quote) { fields.Add(field.ToString());field.Clear(); } else field.Append(c);
    }
    fields.Add(field.ToString()); return fields.ToArray();
}
record CaptureRequest(string Token, string Id, int Pid, long Started, string Executable, int Seconds);
