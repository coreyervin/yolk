using Yolk.Core;

namespace Yolk.Platform.Windows;

/// <summary>
/// Turns raw Win32 values into the engine's terms. Pure functions with no
/// P/Invoke, so the decisions are unit-tested on any OS; Win32Environment
/// only fetches the numbers.
/// </summary>
internal static class Win32Interpretation
{
    // WTS_CONNECTSTATE_CLASS.WTSActive
    internal const int WtsActive = 0;

    // WTSINFOEX_LEVEL1.SessionFlags
    internal const int WtsSessionStateLock = 0;
    internal const int WtsSessionStateUnlock = 1;

    /// <summary>
    /// Idle seconds from <c>GetTickCount</c> and <c>LASTINPUTINFO.dwTime</c>.
    /// Both are 32-bit millisecond counters that wrap every ~49.7 days; the
    /// unchecked subtraction stays correct across the wrap.
    /// </summary>
    internal static double IdleSeconds(uint tickCountNow, uint lastInputTick) =>
        unchecked(tickCountNow - lastInputTick) / 1000.0;

    /// <summary>
    /// Session state from <c>WTSQuerySessionInformation(WTSSessionInfoEx)</c>.
    /// A null reading means the query failed, which must fail safe to
    /// <see cref="ConsoleState.Unknown"/> rather than be treated as active.
    /// </summary>
    /// <remarks>
    /// Lock is checked first, matching the Mac. "Switched out" means the
    /// session is not connected — fast user switching or a dropped Remote
    /// Desktop connection. A connected Remote Desktop session counts as
    /// active, since someone is using it.
    /// </remarks>
    internal static ConsoleState ConsoleState(int? connectState, int? sessionFlags)
    {
        if (connectState is not { } state || sessionFlags is not { } flags)
        {
            return Core.ConsoleState.Unknown;
        }
        if (flags == WtsSessionStateLock)
        {
            return Core.ConsoleState.Locked;
        }
        if (state != WtsActive)
        {
            return Core.ConsoleState.SwitchedOut;
        }
        // Anything but an explicit unlock (including WTS_SESSIONSTATE_UNKNOWN)
        // pauses rather than guesses.
        return flags == WtsSessionStateUnlock ? Core.ConsoleState.Active : Core.ConsoleState.Unknown;
    }

    /// <summary>
    /// The display-off timeout from the active power scheme, in seconds.
    /// Zero means "never turn off the display", which yolk reports as null.
    /// </summary>
    internal static double? DisplaySleepSeconds(uint? timeout) =>
        timeout is { } seconds and > 0 ? seconds : null;
}
