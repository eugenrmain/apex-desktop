namespace Apex.Services;
public sealed record NetworkProcess(int Id, long Started);
public sealed record NetworkRate(int Id, long Started, double BytesPerSecond);
public sealed record NetworkScanRequest(string Token, List<NetworkProcess> Processes);
public sealed record NetworkScanReply(string Token, List<NetworkRate> Rates, string? Error, int EventsLost = 0);
public static class NetworkUsage
{
    public static Dictionary<(int, long), double> Match(NetworkScanReply reply, IEnumerable<NetworkProcess> processes)
    {
        if (reply.Error is not null || reply.EventsLost > 0)
            throw new IOException(reply.Error ?? "Network events were lost; try another scan.");
        var allowed = processes.Select(p => (p.Id, p.Started)).ToHashSet();
        var result = new Dictionary<(int, long), double>();
        foreach (var rate in reply.Rates)
            if (allowed.Contains((rate.Id, rate.Started)) && double.IsFinite(rate.BytesPerSecond) && rate.BytesPerSecond >= 0)
                result[(rate.Id, rate.Started)] = rate.BytesPerSecond;
        return result;
    }
}
