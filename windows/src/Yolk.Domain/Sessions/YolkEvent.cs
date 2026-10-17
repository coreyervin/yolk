using Yolk.Domain.Configuration;

namespace Yolk.Domain.Sessions;

public enum PauseReason
{
    ScreenLocked,
    SessionSwitchedOut,
    SessionUnknown,
}

public enum StopReason
{
    UserRequested,
    TimeoutReached,
}

/// <summary>
/// What the engine reports. Frontends decide how to present each one; the
/// engine emits the same events whether a CLI or the tray app is listening.
/// </summary>
public abstract record YolkEvent
{
    private YolkEvent() { }

    public sealed record Started(YolkConfig Config) : YolkEvent;

    /// <summary>
    /// Emitted every tick that isn't paused. <paramref name="NudgeAt"/> is the
    /// idle time the next nudge is waiting for.
    /// </summary>
    public sealed record Checked(double Idle, double NudgeAt) : YolkEvent;

    public sealed record Paused(PauseReason Reason) : YolkEvent;

    public sealed record Resumed : YolkEvent;

    public sealed record ActivitySimulated(int Total) : YolkEvent;

    /// <summary>
    /// Input was posted but the idle timer didn't reset. At most once per session.
    /// </summary>
    public sealed record PermissionMissing : YolkEvent;

    /// <summary>The input could not be posted at all. At most once per session.</summary>
    public sealed record PostFailed : YolkEvent;

    public sealed record DisplaySleepTooSoon(double DisplaySleep) : YolkEvent;

    public sealed record Stopped(StopReason Reason) : YolkEvent;
}
