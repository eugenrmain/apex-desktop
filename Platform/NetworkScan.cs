using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using Apex.Services;
namespace Apex.Platform;
public static class NetworkScan
{
    public static async Task<Dictionary<(int, long), double>> RunAsync(List<NetworkProcess> processes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (processes.Count > 10000)
            throw new IOException("Too many processes for one network sample.");
        string name = "Apex.Network." + Guid.NewGuid().ToString("N"), nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var helper = Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Apex.Network.exe")) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, Arguments = "--pipe " + name + " --token " + nonce }) ?? throw new IOException("Network scan did not start.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await pipe.WaitForConnectionAsync(timeout.Token);
        var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(new NetworkScanRequest(nonce, processes)));
        // Cancel the wait, not the pipe read: the helper must still be able to
        // acknowledge that its trace session has stopped before this pipe closes.
        var response = reader.ReadLineAsync();
        try
        {
            string? line = await response.WaitAsync(timeout.Token);
            if (line is null || line.Length > 2_000_000)
                throw new IOException("Network scan disconnected.");
            var reply = JsonSerializer.Deserialize<NetworkScanReply>(line) ?? throw new IOException("Invalid network reply.");
            if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(reply.Token), System.Text.Encoding.ASCII.GetBytes(nonce)))
                throw new IOException("Invalid network session.");
            return NetworkUsage.Match(reply, processes);
        }
        finally
        {
            try
            {
                await writer.WriteLineAsync("Stop");
            }
            catch { }
            try
            {
                await response.WaitAsync(TimeSpan.FromSeconds(8));
            }
            catch { }
            try
            {
                await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
            }
            catch { }
            try
            {
                writer.Dispose();
            }
            catch (IOException) { }
        }
    }
}
