namespace Apex.Services;
public sealed class PerformanceDraft
{
    public static readonly Guid HighPerformanceScheme = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public string Selected { get; private set; } = "Custom";
    public List<PlanAction> Custom { get; private set; } = [];
    private List<PlanAction> builtIn = [];
    public Guid CustomId { get; private set; } = Guid.NewGuid();
    public string CustomName { get; private set; } = "Custom";
    public string Notes { get; set; } = "";
    public IReadOnlyList<PlanAction> Actions => Selected == "Custom" ? Custom : builtIn;
    public void SelectBuiltIn(string name, IReadOnlyDictionary<Guid, string> available, string? mouseParameters = null, IReadOnlyList<Apex.Platform.DisplayChoice>? displays = null)
    {
        Guid id = name == "Performance" ? HighPerformanceScheme : throw new IOException("Unknown preset.");
        if (!available.TryGetValue(id, out string? label))
            throw new IOException(name + " power scheme is not installed. Open Windows Power settings to manage existing schemes.");
        builtIn = [new(PlanActionKind.PowerPlan, "Active", id.ToString(), "Power: " + label)];
        foreach (var effect in Apex.Platform.WindowsVisualEffects.Supported)
            builtIn.Add(new(PlanActionKind.VisualEffect, effect.Key, Apex.Platform.WindowsVisualEffects.PresetValue(effect.Key), "Appearance: " + effect.Value.Label));
        if (mouseParameters is not null)
            builtIn.Add(new(PlanActionKind.Mouse, "Pointer", Apex.Platform.WindowsMouse.WithoutAcceleration(mouseParameters), "Input: disable Windows pointer acceleration"));
        if (displays is not null)
            foreach (var display in displays)
            {
                if (display.Highest is not null)
                    builtIn.Add(new(PlanActionKind.Display, display.Target, display.Highest, "Display: highest supported refresh rate / " + display.Name));
                else
                    builtIn.Add(new(PlanActionKind.Manual, "Display:" + display.Target, "Unavailable: " + display.Reason, "Manual: refresh rate / " + display.Name + " - " + display.Reason));
            }
        builtIn.Add(new(PlanActionKind.Manual, "AppearanceGuided", "Enable thumbnails instead of icons; disable: " + string.Join("; ", Apex.Platform.WindowsVisualEffects.GuidedDisabled), "Manual: finish appearance in Windows Performance Options"));
        foreach (var item in new[] { ("Graphics", "Review per-game graphics preference"), ("GameMode", "Review Game Mode"), ("Windowed", "Review optimizations for windowed games"), ("Captures", "Review and disable unwanted background game recording"), ("VSync", "Review in-game VSync"), ("FrameCap", "Review game frame cap"), ("Latency", "Review supported in-game latency option") })
            builtIn.Add(new(PlanActionKind.Manual, item.Item1, "Manual; unverified", item.Item2));
        Selected = name;
    }
    public void SelectCustom() => Selected = "Custom";
    public void Toggle(PlanAction action, bool enabled)
    {
        if (Selected != "Custom")
        {
            Custom = builtIn.ToList();
            CustomId = Guid.NewGuid();
            CustomName = "Custom";
            Selected = "Custom";
        }
        Custom.RemoveAll(a => a.Kind == action.Kind && a.Target.Equals(action.Target, StringComparison.OrdinalIgnoreCase));
        if (enabled)
            Custom.Add(action);
    }
    public void Clear()
    {
        Selected = "Custom";
        Custom.Clear();
        Notes = "";
    }
    public void Load(PerformancePlan preset)
    {
        Custom = preset.Actions.ToList();
        CustomId = preset.Id;
        CustomName = preset.Name;
        Notes = preset.Notes;
        Selected = "Custom";
    }
    public PerformancePlan Snapshot(string? name = null) => new(Selected == "Custom" ? CustomId : Guid.NewGuid(), name ?? (Selected == "Custom" ? CustomName : Selected), Actions.ToList(), Notes);
}
