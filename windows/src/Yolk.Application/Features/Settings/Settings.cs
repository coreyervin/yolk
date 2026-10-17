using Yolk.Application.Abstractions;
using Yolk.Application.Features.KeepAwake;
using Yolk.Domain.Configuration;

namespace Yolk.Application.Features.Settings;

/// <summary>The tray app's settings, validated.</summary>
public sealed record SettingsDto(double Interval, double Threshold, StopAfter StopAfter)
{
    /// <summary>Both values are validated on the way in, so this cannot throw.</summary>
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
/// not. Null leaves a value unchanged; out-of-range values are clamped.
/// </summary>
public sealed record UpdateSettings(double? Interval = null, double? Threshold = null, StopAfter? StopAfter = null);

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
            Valid(stored.Interval, YolkConfig.IsValidInterval, YolkConfig.DefaultInterval),
            Valid(stored.Threshold, YolkConfig.IsValidThreshold, YolkConfig.DefaultThreshold),
            stored.StopAfter ?? StopAfter.Never);
    }

    private static double Valid(double? value, Func<double, bool> isValid, double fallback) =>
        value is { } v && isValid(v) ? v : fallback;
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
        var updated = new SettingsDto(
            Clamp(command.Interval, current.Interval, YolkConfig.MinInterval, YolkConfig.MaxInterval),
            Clamp(command.Threshold, current.Threshold, YolkConfig.MinThreshold, YolkConfig.MaxThreshold),
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
    private static double Clamp(double? requested, double current, double min, double max) =>
        requested is { } value && !double.IsNaN(value) ? Math.Clamp(value, min, max) : current;
}
