using Yolk.Application.Abstractions;
using Yolk.Application.Features.KeepAwake;
using Yolk.Domain.Configuration;

namespace Yolk.Application.Features.Settings;

/// <summary>The tray app's settings, validated.</summary>
public sealed record SettingsDto(double Interval, ThresholdRange Threshold, StopAfter StopAfter)
{
    /// <summary>Every value is validated on the way in, so this cannot throw.</summary>
    public YolkConfig ToConfig() => new(Interval, Threshold, StopAfter.Timeout());
}

/// <summary>Read the stored settings.</summary>
public sealed record GetSettings;

/// <summary>
/// Apply the stored settings to the session. The tray app sends this once at
/// launch, so the session runs with what the user chose last time.
/// </summary>
public sealed record ApplyStoredSettings;

/// <summary>
/// Change some settings: store them and apply them to the session, running or
/// not. Null leaves a value unchanged; out-of-range values are clamped. Moving
/// one end of the threshold range past the other carries the other along, so
/// the range never inverts.
/// </summary>
public sealed record UpdateSettings(
    double? Interval = null,
    double? ThresholdMin = null,
    double? ThresholdMax = null,
    StopAfter? StopAfter = null);

/// <summary>
/// Reads the store, falling back to the documented defaults for anything
/// missing or out of range — the registry can be hand-edited, or written by a
/// build with different bounds.
/// </summary>
internal sealed class SettingsReader(ISettingsRepository repository)
{
    public SettingsDto Read()
    {
        var stored = repository.Load();
        return new SettingsDto(
            stored.Interval is { } i && YolkConfig.IsValidInterval(i) ? i : YolkConfig.DefaultInterval,
            Threshold(stored),
            stored.StopAfter ?? StopAfter.Never);
    }

    /// <summary>
    /// The stored range; or the pre-range single value, as a fixed point; or
    /// the default. A range that is out of bounds or inverted is thrown away
    /// whole rather than half-trusted.
    /// </summary>
    private static ThresholdRange Threshold(StoredSettings stored)
    {
        if (stored.ThresholdMin is { } low && stored.ThresholdMax is { } high
            && YolkConfig.IsValidThreshold(low) && YolkConfig.IsValidThreshold(high) && low <= high)
        {
            return new ThresholdRange(low, high);
        }
        if (stored.LegacyThreshold is { } legacy && YolkConfig.IsValidThreshold(legacy))
        {
            return ThresholdRange.Fixed(legacy);
        }
        return ThresholdRange.Default;
    }
}

internal sealed class GetSettingsHandler(SettingsReader reader) : IQueryHandler<GetSettings, SettingsDto>
{
    public SettingsDto Handle(GetSettings query) => reader.Read();
}

internal sealed class ApplyStoredSettingsHandler(
    SettingsReader reader, ICommandHandler<ReconfigureKeepAwake> reconfigure)
    : ICommandHandler<ApplyStoredSettings>
{
    public void Handle(ApplyStoredSettings command) =>
        reconfigure.Handle(new ReconfigureKeepAwake(reader.Read().ToConfig()));
}

internal sealed class UpdateSettingsHandler(
    SettingsReader reader, ISettingsRepository repository, ICommandHandler<ReconfigureKeepAwake> reconfigure)
    : ICommandHandler<UpdateSettings, SettingsDto>
{
    public SettingsDto Handle(UpdateSettings command)
    {
        var current = reader.Read();
        var (low, high) = (current.Threshold.Min, current.Threshold.Max);
        if (Clamp(command.ThresholdMin, YolkConfig.MinThreshold, YolkConfig.MaxThreshold) is { } newLow)
        {
            (low, high) = (newLow, Math.Max(high, newLow));
        }
        if (Clamp(command.ThresholdMax, YolkConfig.MinThreshold, YolkConfig.MaxThreshold) is { } newHigh)
        {
            (low, high) = (Math.Min(low, newHigh), newHigh);
        }
        var updated = new SettingsDto(
            Clamp(command.Interval, YolkConfig.MinInterval, YolkConfig.MaxInterval) ?? current.Interval,
            new ThresholdRange(low, high),
            command.StopAfter ?? current.StopAfter);

        if (updated == current)
        {
            return current;
        }
        repository.Save(updated.Interval, updated.Threshold, updated.StopAfter);
        reconfigure.Handle(new ReconfigureKeepAwake(updated.ToConfig()));
        return updated;
    }

    // NaN is treated as "no change" rather than clamped, since it has no place
    // in the range at all.
    private static double? Clamp(double? requested, double min, double max) =>
        requested is { } value && !double.IsNaN(value) ? Math.Clamp(value, min, max) : null;
}
