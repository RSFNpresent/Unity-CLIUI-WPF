using System.Text.RegularExpressions;

namespace unity_cli_ui.Services;

public static partial class UnityVersionPolicy
{
    public static bool Matches(string? first, string? second)
    {
        if (TryExtract(first, out var firstVersion) && TryExtract(second, out var secondVersion))
        {
            return string.Equals(firstVersion, secondVersion, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(first?.Trim(), second?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string? value) =>
        TryExtract(value, out var version) ? version : value?.Trim() ?? string.Empty;

    public static bool TryExtract(string? value, out string version)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            version = string.Empty;
            return false;
        }

        var match = UnityVersionPattern().Match(value);
        version = match.Success ? match.Groups["version"].Value : string.Empty;
        return match.Success;
    }

    [GeneratedRegex(
        @"(?<![0-9A-Za-z])(?<version>\d+\.\d+\.\d+[abfp]\d+(?:[a-z]\d+)?)(?![0-9A-Za-z])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnityVersionPattern();
}
