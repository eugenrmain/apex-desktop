using System.IO.Pipes;
using System.Text.Json;
using Apex.Platform;
using Apex.Services;
if (args.Length != 4 || args[0] != "--pipe" || !args[1].StartsWith("Apex.Settings.", StringComparison.Ordinal) || args[2] != "--token" || args[3].Length != 64 || !args[3].All(Uri.IsHexDigit)) return;
try
{
    using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous);
    await pipe.ConnectAsync(15000); using var reader = new StreamReader(pipe, leaveOpen: true); using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
    string? line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
    if (line is null || line.Length > 32768) throw new IOException("Invalid settings request.");
    var request = JsonSerializer.Deserialize<Request>(line) ?? throw new IOException("Missing request.");
    if (request.Token != args[3] || request.Action.Kind is not (PlanActionKind.PowerPlan or PlanActionKind.Dns or PlanActionKind.Display)) throw new IOException("Action is not allowed in the settings helper.");
    if(request.Action.Kind==PlanActionKind.Display)
    {
        bool keep=await DisplayChangeGuard.ApplyAsync(()=>WindowsDisplay.Read(request.Action.Target),(value,expected)=>WindowsDisplay.Write(request.Action.Target,value,expected),request.Expected,request.Value,async()=>
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(new {Token=args[3],Error=(string?)null,Confirm=true}));
            string? decision=await reader.ReadLineAsync();
            var approval=JsonSerializer.Deserialize<Approval>(decision ?? "");
            return approval?.Token==args[3] && approval.Keep;
        },TimeSpan.FromSeconds(18));
        await writer.WriteLineAsync(JsonSerializer.Serialize(new {Token=args[3],Error=keep?(string?)null:"Display change reverted; it was not confirmed."})); return;
    }
    try { WindowsPerformance.Write(request.Action, request.Value, request.Expected); await writer.WriteLineAsync(JsonSerializer.Serialize(new { Token = args[3], Error = (string?)null })); }
    catch (Exception e) { await writer.WriteLineAsync(JsonSerializer.Serialize(new { Token = args[3], Error = e.Message })); }
}
catch { Environment.ExitCode = 1; }
record Request(string Token, PlanAction Action, string Value, string Expected);
record Approval(string Token,bool Keep);
