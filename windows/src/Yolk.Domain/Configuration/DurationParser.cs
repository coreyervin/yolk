using System.Globalization;

namespace Yolk.Domain.Configuration;

/// <summary>Parsing for the CLI's <c>-t/--timeout</c> durations.</summary>
public static class DurationParser
{
    /// <summary>
    /// Parses <c>"8h"</c>, <c>"90m"</c>, <c>"45s"</c>, <c>"1d"</c>, or bare
    /// seconds. Returns null for anything non-positive or unparseable.
    /// </summary>
    public static double? Parse(string s)
    {
        var lower = s.ToLowerInvariant();
        var numPart = lower;
        var multiplier = 1.0;
        if (lower.Length > 0 && Multiplier(lower[^1]) is { } m)
        {
            numPart = lower[..^1];
            multiplier = m;
        }
        if (NumberParser.Parse(numPart) is not { } n || !double.IsFinite(n) || n <= 0)
        {
            return null;
        }
        return n * multiplier;
    }

    private static double? Multiplier(char suffix) => suffix switch
    {
        's' => 1,
        'm' => 60,
        'h' => 3600,
        'd' => 86400,
        _ => null,
    };
}

/// <summary>
/// Number parsing that matches the Mac CLI's Swift <c>Double(String)</c>:
/// invariant culture, no surrounding whitespace, no thousands separators.
/// </summary>
public static class NumberParser
{
    private const NumberStyles Style =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

    public static double? Parse(string s) =>
        double.TryParse(s, Style, CultureInfo.InvariantCulture, out var value) ? value : null;
}
