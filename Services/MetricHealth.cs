namespace Apex.Services;
public static class MetricHealth
{
    // Presentation ranges only, not device-specific safety limits or restart recommendations.
    public static string Classify(double? value, bool temperature, bool percentage)
    {
        if (value is not double v || !double.IsFinite(v))
            return "Unavailable";
        if (!temperature && !percentage)
            return "Normal";
        return v >= (temperature ? 85 : 90) ? "High" : v >= (temperature ? 70 : 70) ? "Elevated" : v < (temperature ? 50 : 30) ? "Cool" : "Normal";
    }
}
