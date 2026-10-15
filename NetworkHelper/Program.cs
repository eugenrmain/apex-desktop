using System.IO.Pipes;
using System.Text.Json;
using Apex.Platform;
using Apex.Services;
if(args.Length!=4 || args[0]!="--pipe" || !args[1].StartsWith("Apex.Network.",StringComparison.Ordinal) || args[2]!="--token" || args[3].Length!=64 || !args[3].All(Uri.IsHexDigit))return;
try
{
    using var pipe=new NamedPipeClientStream(".",args[1],PipeDirection.InOut,PipeOptions.Asynchronous);await pipe.ConnectAsync(15000);
    using var reader=new StreamReader(pipe,leaveOpen:true);using var writer=new StreamWriter(pipe){AutoFlush=true};
    string? line=await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));if(line is null || line.Length>1_000_000)throw new IOException("Invalid network request.");
    var request=JsonSerializer.Deserialize<NetworkScanRequest>(line)??throw new IOException("Missing request.");if(request.Token!=args[3] || request.Processes.Count>10000)throw new IOException("Invalid network request.");
    using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(12));var disconnect=reader.ReadLineAsync();var capture=WindowsNetworkTrace.CaptureAsync(request.Processes,stop.Token);
    if(await Task.WhenAny(capture,disconnect)==disconnect)stop.Cancel();
    try{var result=await capture;await writer.WriteLineAsync(JsonSerializer.Serialize(new NetworkScanReply(args[3],result.Rates,null,result.Lost)));}
    catch(Exception e){if(pipe.IsConnected)await writer.WriteLineAsync(JsonSerializer.Serialize(new NetworkScanReply(args[3],[],e.Message)));}
}catch{Environment.ExitCode=1;}
