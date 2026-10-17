namespace Yolk.Domain.Sessions;

/// <summary>
/// Whether this login session is ours, connected, and unlocked.
/// </summary>
/// <remarks>
/// Only <see cref="Active"/> permits simulating activity. Every other case
/// pauses — the fail-safe that stops yolk posting synthetic input into a
/// locked screen or a disconnected session.
/// </remarks>
public enum ConsoleState
{
    Active,
    Locked,
    SwitchedOut,
    Unknown,
}

public static class ConsoleStateExtensions
{
    /// <summary>
    /// Why simulation should pause, or null when it may proceed. Every "should
    /// I pause?" decision routes through here, so a case added later cannot
    /// silently start posting input into the wrong session.
    /// </summary>
    public static PauseReason? ToPauseReason(this ConsoleState state) => state switch
    {
        ConsoleState.Active => null,
        ConsoleState.Locked => PauseReason.ScreenLocked,
        ConsoleState.SwitchedOut => PauseReason.SessionSwitchedOut,
        _ => PauseReason.SessionUnknown,
    };

    public static bool PausesSimulation(this ConsoleState state) => state.ToPauseReason() is not null;
}
