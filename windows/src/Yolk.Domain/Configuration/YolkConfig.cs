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

    /// <summary>The documented defaults, which are in range by construction.</summary>
    public static readonly YolkConfig Default = new();

    /// <summary>
    /// The most seconds between idle checks. Checks are otherwise timed to
    /// land when idle reaches the next nudge point.
    /// </summary>
    public double Interval { get; }

    /// <summary>
    /// Idle seconds before simulating activity: a random point in this range,
    /// chosen afresh for every nudge. A single value is a range of one.
    /// </summary>
    public ThresholdRange Threshold { get; }

    /// <summary>Seconds until the session stops itself; null runs indefinitely.</summary>
    public double? Timeout { get; }

    public YolkConfig(
        double interval = DefaultInterval,
        ThresholdRange? threshold = null,
        double? timeout = null)
    {
        var range = threshold ?? ThresholdRange.Default;
        if (!IsValidInterval(interval))
        {
            throw new ConfigException(ConfigError.IntervalOutOfRange, interval);
        }
        foreach (var bound in new[] { range.Min, range.Max })
        {
            if (!IsValidThreshold(bound))
            {
                throw new ConfigException(ConfigError.ThresholdOutOfRange, bound);
            }
        }
        if (range.Min > range.Max)
        {
            throw new ConfigException(ConfigError.ThresholdInverted, range.Min);
        }
        if (timeout is { } t && (!double.IsFinite(t) || t <= 0))
        {
            throw new ConfigException(ConfigError.InvalidTimeout, t);
        }
        Interval = interval;
        Threshold = range;
        Timeout = timeout;
    }

    // Written as range checks rather than Math.Clamp comparisons so NaN fails
    // both bounds and is rejected.
    public static bool IsValidInterval(double value) =>
        value >= MinInterval && value <= MaxInterval;

    public static bool IsValidThreshold(double value) =>
        value >= MinThreshold && value <= MaxThreshold;
}

/// <summary>
/// The idle time each nudge waits for, as a range: every nudge picks a random
/// point in it, so the nudges never land on a regular, noticeable beat.
/// </summary>
public readonly record struct ThresholdRange(double Min, double Max)
{
    /// <summary>A random point between 30 and 60 seconds of idle.</summary>
    public static readonly ThresholdRange Default = new(30, 60);

    /// <summary>The same point every time.</summary>
    public static ThresholdRange Fixed(double seconds) => new(seconds, seconds);

    public bool IsFixed => Min == Max;

    /// <summary>The point <paramref name="unit"/> (0 to 1) of the way through the range.</summary>
    public double PointAt(double unit) => Min + (unit * (Max - Min));
}

public enum ConfigError
{
    IntervalOutOfRange,
    ThresholdOutOfRange,
    ThresholdInverted,
    InvalidTimeout,
}

public sealed class ConfigException(ConfigError error, double value)
    : ArgumentException($"{error}: {value}")
{
    public ConfigError Error { get; } = error;
    public double Value { get; } = value;
}
