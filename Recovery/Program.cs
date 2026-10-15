using System.Text;
using System.Text.Json;
using Apex.Platform;
using Apex.Services;

namespace Apex.Recovery;

public sealed record RecoveryRequest(Entry File, string Action);
internal static class Program
{
    // Only one inspected file and three explicit operations; no directory recursion,
    // process termination, ownership changes, command execution, or repair APIs.
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2 || args[0] != "--request" || args[1].Length > 32768) return 87;
            var request = JsonSerializer.Deserialize<RecoveryRequest>(Encoding.UTF8.GetString(Convert.FromBase64String(args[1]))) ?? throw new IOException("Missing request.");
            if (!Path.IsPathFullyQualified(request.File.Path)) return 87;
            FileToolkit.ValidateMutation(request.File.Path); FileToolkit.Recheck(request.File);
            switch (request.Action)
            {
                case "Recycle": WindowsActions.Delete(request.File, false, 0); break;
                case "Permanent": WindowsActions.Delete(request.File, true, 0); break;
                case "ReadOnly": WindowsActions.ClearReadOnly(request.File); break;
                default: return 87;
            }
            return 0;
        }
        catch (UnauthorizedAccessException) { return 5; }
        catch (Exception e) { return (e.HResult & 0xffff) is int code && code != 0 ? code : 1; }
    }
}
