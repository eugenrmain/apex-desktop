using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using Apex.Services;
namespace Apex.Platform;
public sealed class SettingsProvider : IPerformanceProvider
{
    public Func<Task<bool>>? ConfirmDisplay
    {
        get; init;
    }
    private readonly WindowsPerformance native = new();
    public Task<string> ReadAsync(PlanAction action, CancellationToken token) => native.ReadAsync(action, token);
    public async Task WriteAsync(PlanAction action, string value, string expected, CancellationToken token)
    {
        if (action.Kind == PlanActionKind.Display)
        {
            if (ConfirmDisplay is null)
                throw new IOException("Display changes need a timed Keep confirmation.");
            await ChangeDisplayAsync(action, value, expected, token);
            return;
        }
        try
        {
            await native.WriteAsync(action, value, expected, token);
        }
        catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 5 && action.Kind is PlanActionKind.Dns or PlanActionKind.PowerPlan)
        {
            string name = "Apex.Settings." + Guid.NewGuid().ToString("N"), nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var helper = Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Apex.Settings.exe")) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, Arguments = "--pipe " + name + " --token " + nonce }) ?? throw new IOException("Settings helper did not start.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await pipe.WaitForConnectionAsync(timeout.Token);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(new
            {
                Token = nonce,
                Action = action,
                Value = value,
                Expected = expected
            }));
            var reply = JsonSerializer.Deserialize<Reply>(await reader.ReadLineAsync(timeout.Token) ?? "") ?? throw new IOException("Invalid settings response.");
            if (reply.Token != nonce)
                throw new IOException("Invalid settings session.");
            if (reply.Error is not null)
                throw new IOException(reply.Error);
        }
    }
    private async Task ChangeDisplayAsync(PlanAction action, string value, string expected, CancellationToken token)
    {
        string name = "Apex.Settings." + Guid.NewGuid().ToString("N"), nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var helper = Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Apex.Settings.exe")) { UseShellExecute = false, CreateNoWindow = true, Arguments = "--pipe " + name + " --token " + nonce }) ?? throw new IOException("Display rollback helper could not start.");
        using var connect = CancellationTokenSource.CreateLinkedTokenSource(token);
        connect.CancelAfter(TimeSpan.FromSeconds(15));
        await pipe.WaitForConnectionAsync(connect.Token);
        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(new
        {
            Token = nonce,
            Action = action,
            Value = value,
            Expected = expected
        }));
        var first = JsonSerializer.Deserialize<DisplayReply>(await reader.ReadLineAsync(connect.Token) ?? "") ?? throw new IOException("Invalid display response.");
        if (first.Token != nonce || !first.Confirm || first.Error is not null)
            throw new IOException(first.Error ?? "Display helper refused the change.");
        bool keep = false;
        try
        {
            if (!token.IsCancellationRequested)
                keep = await ConfirmDisplay!();
        }
        finally { if (pipe.IsConnected) await writer.WriteLineAsync(JsonSerializer.Serialize(new { Token = nonce, Keep = keep && !token.IsCancellationRequested })); }
        using var finish = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var result = JsonSerializer.Deserialize<DisplayReply>(await reader.ReadLineAsync(finish.Token) ?? "") ?? throw new IOException("Display helper disconnected; review restoration records.");
        if (result.Token != nonce || result.Error is not null)
            throw new IOException(result.Error ?? "Invalid display session.");
    }
    private sealed record DisplayReply(string Token, string? Error, bool Confirm = false);
    private sealed record Reply(string Token, string? Error);
}
