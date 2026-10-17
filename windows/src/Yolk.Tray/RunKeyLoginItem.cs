using Microsoft.Win32;
using Yolk.AppCore;

namespace Yolk.Tray;

/// <summary>
/// "Launch at sign-in" via the current user's Run key — the per-user startup
/// list Task Manager's Startup apps page shows. No admin rights needed.
/// </summary>
internal sealed class RunKeyLoginItem : ILoginItemService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Yolk";

    private static string? Command =>
        Environment.ProcessPath is { } path ? $"\"{path}\"" : null;

    /// <summary>
    /// Enabled only when the entry points at this copy. An entry left behind
    /// by a copy that has since moved would launch nothing, so it reads as off.
    /// </summary>
    public bool IsEnabled()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return run?.GetValue(ValueName) is string value
            && string.Equals(value, Command, StringComparison.OrdinalIgnoreCase);
    }

    public void Register()
    {
        var command = Command
            ?? throw new IOException("Yolk could not determine where it is installed.");
        using var run = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        run.SetValue(ValueName, command, RegistryValueKind.String);
    }

    public void Unregister()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        run?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public bool CanRegister() => Command is not null;
}
