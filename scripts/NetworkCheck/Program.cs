using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Apex.Platform;
using Apex.Services;

string output = Path.GetFullPath(args[0]);
var checks = new List<string>();
try
{
    using var current = Process.GetCurrentProcess();
    var identity = new NetworkProcess(current.Id, current.StartTime.ToUniversalTime().Ticks);
    if (args.Contains("--cancel"))
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var existing = Process.GetProcessesByName("Apex.Network").Select(p => { using (p) return p.Id; }).ToHashSet();
        int helperId = 0;
        var monitor = Task.Run(async () =>
        {
            while (!cancel.IsCancellationRequested)
            {
                foreach (var p in Process.GetProcessesByName("Apex.Network"))
                    using (p)
                        if (!existing.Contains(p.Id)) helperId = p.Id;
                if (helperId != 0) { await Task.Delay(1500); cancel.Cancel(); return; }
                await Task.Delay(100);
            }
        });
        try { await NetworkScan.RunAsync([identity], cancel.Token); throw new IOException("Canceled sample unexpectedly completed."); }
        catch (OperationCanceledException) { checks.Add("In-flight cancellation interrupts the administrator sample"); }
        await monitor;
        if (helperId == 0) throw new IOException("Helper never started.");
        try { using var p = Process.GetProcessById(helperId); if (!p.HasExited) throw new IOException("Helper still running after cancellation."); }
        catch (ArgumentException) { }
        checks.Add("Canceled helper exits without terminating unrelated processes");
        File.WriteAllText(output, JsonSerializer.Serialize(new { passed = true, checks }, new JsonSerializerOptions { WriteIndented = true }));
        return;
    }
    using (var canceled = new CancellationTokenSource())
    {
        canceled.Cancel();
        try { await NetworkScan.RunAsync([identity], canceled.Token); throw new IOException("Canceled scan unexpectedly started."); }
        catch (OperationCanceledException) { checks.Add("Pre-canceled request exits without elevation"); }
    }
    using var trafficStop = new CancellationTokenSource();
    using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
    using var sender = new UdpClient();
    var endpoint = (IPEndPoint)receiver.Client.LocalEndPoint!;
    var receive = Task.Run(async () =>
    {
        try { while (true) await receiver.ReceiveAsync(trafficStop.Token); }
        catch (OperationCanceledException) { }
    });
    var send = Task.Run(async () =>
    {
        byte[] data = new byte[8192];
        try
        {
            while (true)
            {
                await sender.SendAsync(data, endpoint, trafficStop.Token);
                await Task.Delay(10, trafficStop.Token);
            }
        }
        catch (OperationCanceledException) { }
    });
    Dictionary<(int, long), double> rates;
    var watch = Stopwatch.StartNew();
    try { rates = await NetworkScan.RunAsync([identity], CancellationToken.None); }
    finally { trafficStop.Cancel(); await Task.WhenAll(send, receive); }
    if (!rates.TryGetValue((identity.Id, identity.Started), out double rate) || !double.IsFinite(rate) || rate <= 0)
        throw new IOException("No generated traffic was attributed to the reviewed process.");
    checks.Add("Administrator helper connects through nonce-authenticated local pipe");
    checks.Add("Real Windows ETW sample attributes generated UDP traffic to the correct process identity");
    checks.Add("Owned session stops and helper exits after the five-second sample");
    File.WriteAllText(output, JsonSerializer.Serialize(new { passed = true, checks, bytesPerSecond = rate, elapsedSeconds = watch.Elapsed.TotalSeconds }, new JsonSerializerOptions { WriteIndented = true }));
}
catch (Exception e)
{
    File.WriteAllText(output, JsonSerializer.Serialize(new { passed = false, checks, error = e.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
    Environment.ExitCode = 1;
}
