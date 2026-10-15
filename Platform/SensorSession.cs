using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using Apex.Services;

namespace Apex.Platform;
public sealed class SensorSession : IDisposable
{
    private CancellationTokenSource? lifetime;
    private Process? helper;
    private NamedPipeServerStream? pipe;
    private StreamWriter? control;
    private bool paused;
    private DateTime lastReceived;
    private SensorPacket? latest;
    public SensorPacket? Latest => latest is not null && !paused && DateTime.UtcNow - lastReceived > TimeSpan.FromSeconds(5) ? new([], "Sensor readings are stale; re-enable monitoring.") : latest;
    public bool Enabled => lifetime is not null;
    public async Task StartAsync()
    {
        if (Enabled)
            Dispose();
        lifetime = new();
        paused = false;
        string name = "Apex.Sensor." + Guid.NewGuid().ToString("N"), nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        pipe = new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            helper = Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Apex.Sensors.exe")) { UseShellExecute = true, Verb = "runas", Arguments = "--pipe " + name + " --token " + nonce, WindowStyle = ProcessWindowStyle.Hidden }) ?? throw new IOException("Sensor helper did not start.");
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            connect.CancelAfter(TimeSpan.FromSeconds(20));
            await pipe.WaitForConnectionAsync(connect.Token);
            control = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            _ = ReadAsync(pipe, nonce, lifetime.Token);
        }
        catch { Dispose(); throw; }
    }
    private async Task ReadAsync(NamedPipeServerStream channel, string nonce, CancellationToken token)
    {
        try
        {
            using var reader = new StreamReader(channel, leaveOpen: true);
            while (!token.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(token);
                if (line is null)
                    break;
                if (line.Length > 1_000_000)
                    throw new IOException("Invalid sensor response.");
                var envelope = JsonSerializer.Deserialize<Envelope>(line) ?? throw new IOException("Missing sensor response.");
                if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(envelope.Token), System.Text.Encoding.ASCII.GetBytes(nonce)))
                    throw new IOException("Invalid sensor session.");
                latest = envelope.Packet;
                lastReceived = DateTime.UtcNow;
            }
            if (!token.IsCancellationRequested)
                latest = new([], "Sensor helper disconnected; enable monitoring again.");
        }
        catch (Exception e) { if (!token.IsCancellationRequested) latest = new([], e.Message); }
    }
    private sealed record Envelope(string Token, SensorPacket Packet);
    public void SetPaused(bool value)
    {
        if (paused == value || control is null)
            return;
        paused = value;
        try
        {
            control.WriteLine(value ? "Pause" : "Resume");
        }
        catch { }
    }
    public void Dispose()
    {
        lifetime?.Cancel();
        lifetime?.Dispose();
        lifetime = null;
        control?.Dispose();
        control = null;
        pipe?.Dispose();
        pipe = null;
        try
        {
            if (helper is { HasExited: false })
                helper.Kill();
        }
        catch { }
        helper?.Dispose();
        helper = null;
        latest = null;
    }
}
