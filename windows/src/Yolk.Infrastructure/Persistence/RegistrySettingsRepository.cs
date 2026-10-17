using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Yolk.Application.Abstractions;
using Yolk.Domain.Configuration;

namespace Yolk.Infrastructure.Persistence;

/// <summary>
/// Settings under <c>HKEY_CURRENT_USER\Software\Yolk</c>, one value per
/// setting. Numbers are stored as invariant-culture strings so a hand edit in
/// regedit reads naturally.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class RegistrySettingsRepository : ISettingsRepository
{
    private const string KeyPath = @"Software\Yolk";

    public StoredSettings Load()
    {
        using var settings = Registry.CurrentUser.OpenSubKey(KeyPath);
        return new StoredSettings(
            ReadDouble(settings, SettingNames.Interval),
            ReadDouble(settings, SettingNames.ThresholdMin),
            ReadDouble(settings, SettingNames.ThresholdMax),
            ReadDouble(settings, SettingNames.LegacyThreshold),
            SettingNames.ParseStopAfter(settings?.GetValue(SettingNames.StopAfter) as string));
    }

    public void Save(double interval, ThresholdRange threshold, StopAfter stopAfter)
    {
        using var settings = Registry.CurrentUser.CreateSubKey(KeyPath);
        WriteDouble(settings, SettingNames.Interval, interval);
        WriteDouble(settings, SettingNames.ThresholdMin, threshold.Min);
        WriteDouble(settings, SettingNames.ThresholdMax, threshold.Max);
        settings.SetValue(SettingNames.StopAfter, SettingNames.Name(stopAfter), RegistryValueKind.String);
    }

    private static void WriteDouble(RegistryKey key, string name, double value) =>
        key.SetValue(name, value.ToString(CultureInfo.InvariantCulture), RegistryValueKind.String);

    private static double? ReadDouble(RegistryKey? key, string name) =>
        key?.GetValue(name) is string text
        && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}

/// <summary>
/// The stored names — the same keys and values the Mac app writes to its
/// preferences. Renaming a StopAfter value silently resets that preference,
/// so add values, don't rename them.
/// </summary>
internal static class SettingNames
{
    public const string Interval = "interval";
    public const string ThresholdMin = "thresholdMin";
    public const string ThresholdMax = "thresholdMax";
    /// <summary>Before thresholds became ranges. Read, never written.</summary>
    public const string LegacyThreshold = "threshold";
    public const string StopAfter = "stopAfter";

    public static string Name(Domain.Configuration.StopAfter option) => option switch
    {
        Domain.Configuration.StopAfter.Minutes30 => "minutes30",
        Domain.Configuration.StopAfter.Hour1 => "hour1",
        Domain.Configuration.StopAfter.Hours4 => "hours4",
        Domain.Configuration.StopAfter.Hours8 => "hours8",
        _ => "never",
    };

    /// <summary>Null for a missing or unrecognised name.</summary>
    public static Domain.Configuration.StopAfter? ParseStopAfter(string? name) =>
        Enum.GetValues<Domain.Configuration.StopAfter>()
            .Cast<Domain.Configuration.StopAfter?>()
            .FirstOrDefault(option => Name(option!.Value) == name);
}
