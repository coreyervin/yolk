namespace Yolk.Domain.Configuration;

/// <summary>The "Stop after…" presets the tray app offers.</summary>
public enum StopAfter
{
    Never,
    Minutes30,
    Hour1,
    Hours4,
    Hours8,
}

public static class StopAfterExtensions
{
    /// <summary>The preset's duration in seconds, or null for <see cref="StopAfter.Never"/>.</summary>
    public static double? Timeout(this StopAfter option) => option switch
    {
        StopAfter.Minutes30 => 1800,
        StopAfter.Hour1 => 3600,
        StopAfter.Hours4 => 14400,
        StopAfter.Hours8 => 28800,
        _ => null,
    };

    public static string Title(this StopAfter option) => option switch
    {
        StopAfter.Minutes30 => "30 minutes",
        StopAfter.Hour1 => "1 hour",
        StopAfter.Hours4 => "4 hours",
        StopAfter.Hours8 => "8 hours",
        _ => "Never",
    };
}
