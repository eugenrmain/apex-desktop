namespace Apex.Services;
public static class SensorValidation
{
    public static double? Temperature(SensorPacket? packet, string kind)
    {
        var readings = packet?.Readings.Where(r => r.Kind.StartsWith(kind, StringComparison.OrdinalIgnoreCase) && r.Unit == "C" && r.Value is > 0 and < 130 && !r.Name.Contains("Distance", StringComparison.OrdinalIgnoreCase) && !r.Name.Contains("Limit", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (readings is null || readings.Length == 0)
            return null;
        var preferred = readings.Where(r => kind == "Cpu" ? r.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) || r.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) : r.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) && !r.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (kind == "Gpu" && preferred.Length == 0)
            return null;
        return (preferred.Length > 0 ? preferred : readings).Max(r => r.Value);
    }
}
