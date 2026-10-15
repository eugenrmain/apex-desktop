namespace Apex.Services;
public sealed record SensorReading(string Hardware, string Name, string Kind, double? Value, string Unit);
public sealed record SensorPacket(List<SensorReading> Readings, string? Error);
public sealed record ProcessUsage(int Id, long Started, string Name, string Executable, double Cpu, long Memory, bool CanClose);
public sealed record MonitorSample(DateTime TimeUtc, double? Cpu, long MemoryUsed, long MemoryTotal, double? Gpu, double? DiskRead, double? DiskWrite, double? NetworkRead, double? NetworkWrite, List<ProcessUsage> Processes, SensorPacket? Sensors, string? Error);
public interface IMonitoringService : IDisposable
{
    Task<MonitorSample> SampleAsync(CancellationToken token);
}
public sealed record FrameMetric(string SwapChain, string Metric, double TimeSeconds, double Milliseconds);
public sealed record FrameSummary(string SwapChain, string Metric, int Frames, double DurationSeconds, double AverageFps, double OnePercentLow, double P50, double P95, double P99);
public sealed record GameRecording(Guid Id, string Name, string Notes, int ProcessId, long ProcessStarted, string Executable, DateTime StartedUtc, double RequestedSeconds, bool Complete, string? Issue, List<FrameSummary> Frames, List<MonitorSample> Samples, PerformancePlan? Plan, int Version = 1);
public interface IRecordingService
{
    bool IsRecording
    {
        get;
    }
    Task<GameRecording> RecordAsync(ProcessUsage process, string name, string notes, int seconds, PerformancePlan? plan, Action<string> progress, CancellationToken token);
}
