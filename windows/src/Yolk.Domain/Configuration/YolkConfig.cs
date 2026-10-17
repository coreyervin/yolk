namespace Yolk.Domain.Configuration;

/// <summary>
/// Validated settings shared by the CLI's argument parser and the tray app's
/// settings. Bounds live here and nowhere else so the two cannot drift.
/// </summary>
public sealed record YolkConfig
{
    public const double MinInterval = 5;
    public const double MaxInterval = 120;

    /// <summary>
    /// Capped at 300s deliberately: above it Slack's ~10 minute away timer
    /// becomes reachable and yolk silently stops doing its job.
    /// </summary>
    public const double MinThreshold = 5;
    public const double MaxThreshold = 300;

    /// <summary>The CLI's documented defaults, shared with the tray app.</summary>
    public const double DefaultInterval = 30;
    public const double DefaultThreshold = 60;

    /// <summary>The documented defaults, which are in range by construction.</summary>
    public static readonly YolkConfig Default = new();

    /// <summary>Seconds between idle checks.</summary>
    public double Interval { get; }

    /// <summary>Idle seconds before simulating activity.</summary>
    public double Threshold { get; }

    /// <summary>Seconds until the session stops itself; null runs indefinitely.</summary>
    public double? Timeout { get; }

    public YolkConfig(
        double interval = DefaultInterval,
        double threshold = DefaultThreshold,
        double? timeout = null)
    {
        if (!IsValidInterval(interval))
        {
            throw new ConfigException(ConfigError.IntervalOutOfRange, interval);
        }
        if (!IsValidThreshold(threshold))
        {
            throw new ConfigException(ConfigError.ThresholdOutOfRange, threshold);
        }
        if (timeout is { } t && (!double.IsFinite(t) || t <= 0))
        {
            throw new ConfigException(ConfigError.InvalidTimeout, t);
        }
        Interval = interval;
        Threshold = threshold;
        Timeout = timeout;
    }

    // Written as range checks rather than Math.Clamp comparisons so NaN fails
    // both bounds and is rejected.
    public static bool IsValidInterval(double value) =>
        value >= MinInterval && value <= MaxInterval;

    public static bool IsValidThreshold(double value) =>
        value >= MinThreshold && value <= MaxThreshold;
}

public enum ConfigError
{
    IntervalOutOfRange,
    ThresholdOutOfRange,
    InvalidTimeout,
}

public sealed class ConfigException(ConfigError error, double value)
    : ArgumentException($"{error}: {value}")
{
    public ConfigError Error { get; } = error;
    public double Value { get; } = value;
}
