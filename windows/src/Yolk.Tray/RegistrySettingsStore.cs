using System.Globalization;
using Microsoft.Win32;
using Yolk.AppCore;

namespace Yolk.Tray;

/// <summary>
/// Settings under <c>HKEY_CURRENT_USER\Software\Yolk</c>. Numbers are stored
/// as invariant-culture strings so a hand edit in regedit reads naturally.
/// </summary>
internal sealed class RegistrySettingsStore : ISettingsStore
{
    private const string KeyPath = @"Software\Yolk";

    public double? ReadDouble(string key) =>
        ReadString(key) is { } text
        && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    public void WriteDouble(string key, double? value) =>
        WriteString(key, value?.ToString(CultureInfo.InvariantCulture));

    public string? ReadString(string key)
    {
        using var settings = Registry.CurrentUser.OpenSubKey(KeyPath);
        return settings?.GetValue(key) as string;
    }

    public void WriteString(string key, string? value)
    {
        using var settings = Registry.CurrentUser.CreateSubKey(KeyPath);
        if (value is null)
        {
            settings.DeleteValue(key, throwOnMissingValue: false);
        }
        else
        {
            settings.SetValue(key, value, RegistryValueKind.String);
        }
    }
}
