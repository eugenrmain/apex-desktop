using System.Text.Json;
namespace Apex.Services;
public sealed record ManualStep(Guid Id, Guid Batch, string Target, string Label, string Instructions, string Status = "Pending");
public sealed class ManualChecklist(string? directory = null)
{
    private readonly string folder = directory ?? Path.Combine(AppDataPaths.Root, "manual-steps-v1");
    public List<ManualStep> Steps { get; private set; } = [];
    public void Load()
    {
        var path = Path.Combine(folder, "steps.json");
        if (File.Exists(path))
            Steps = JsonSerializer.Deserialize<List<ManualStep>>(File.ReadAllText(path)) ?? throw new IOException("Manual checklist cannot be read.");
    }
    private void Persist()
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "steps.json");
        using (var stream = File.Create(path + ".tmp"))
        {
            JsonSerializer.Serialize(stream, Steps);
            stream.Flush(true);
        }
        File.Move(path + ".tmp", path, true);
    }
    public void Stage(PerformancePlan plan)
    {
        var added = plan.Actions.Where(a => a.Kind == PlanActionKind.Manual).Select(a => new ManualStep(Guid.NewGuid(), plan.Id, a.Target, a.Label, Instructions(a))).ToList();
        var previous = Steps;
        Steps = Steps.Where(s => s.Batch != plan.Id).Concat(added).ToList();
        try
        {
            Persist();
        }
        catch { Steps = previous; throw; }
    }
    public void Mark(Guid id, string status)
    {
        if (status is not ("Pending" or "Done" or "Skipped"))
            throw new IOException("Invalid checklist status.");
        int index = Steps.FindIndex(s => s.Id == id);
        if (index < 0)
            throw new IOException("Step no longer exists.");
        var previous = Steps[index];
        Steps[index] = previous with
        {
            Status = status
        };
        try
        {
            Persist();
        }
        catch { Steps[index] = previous; throw; }
    }
    public static string Instructions(PlanAction a) => a.Target switch
    {
        "Graphics" => "Open Windows Graphics settings. Add/select your game and review its graphics preference.",
        "GameMode" => "Open Windows Game Mode and choose whether to enable it.",
        "Windowed" => "In Windows Graphics settings, review optimizations for windowed games where supported.",
        "Captures" => "Open Windows Captures and turn off background recording if you do not use it.",
        "VSync" => "Review VSync inside your game's graphics settings. Choose it according to tearing, VRR and your frame cap.",
        "FrameCap" => "Set the frame cap in your game or supported graphics-driver controls.",
        "Latency" => "Review supported latency options inside your game. Options vary by game and hardware.",
        "GpuScheduling" => "Review hardware-accelerated GPU scheduling in Windows Graphics settings. It is hardware/driver dependent and may require a restart.",
        "AppearanceGuided" => "Open Windows Performance Options, choose Custom, keep Show thumbnails instead of icons and Smooth edges of screen fonts checked, and clear the other listed effects. Apex applies only its supported controls.",
        "Mouse" => "Review Windows mouse settings and your game's own input settings.",
        _ => a.Value is "Manual; unverified" or "Review" ? "Open the related settings and review this option yourself." : a.Value
    };
}
