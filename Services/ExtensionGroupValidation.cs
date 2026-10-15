using System.Text.RegularExpressions;
namespace Apex.Services;
public static class ExtensionGroupValidation
{
    public static string Normalize(string value)
    {
        var extensions = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant()).Distinct().ToArray();
        if (extensions.Length is < 1 or > 64 || extensions.Any(e => !Regex.IsMatch(e, @"^\.[a-z0-9]{1,16}$")))
            throw new IOException("Enter comma-separated extensions, such as .cs, .py, .js.");
        return string.Join(", ", extensions);
    }
}
