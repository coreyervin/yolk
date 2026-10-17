using Yolk.Domain.Configuration;

namespace Yolk.Domain.Sessions;

/// <summary>
/// The keep-awake session: Yolk's rules, with no I/O.
/// </summary>
/// <remarks>
/// The aggregate never reads the clock, the idle timer, or the screen lock
/// itself. The application layer takes those readings and passes them in;
/// the session decides what they mean and answers with the domain event to
/// publish, if any. The rules match the Mac's <c>YolkSession</c>.
/// </remarks>
public sealed class KeepAwakeSession(YolkConfig config)
{
    /// <summary>
    /// The idle reading, after a nudge, below which the nudge counts as landed.
    /// </summary>
    public const double NudgeLandedBelowSeconds = 1.0;

    /// <summary>
    /// Timer slack allowed for when deciding whether the display could sleep
    /// before Yolk gets a chance to act.
    /// </summary>
    public const double TimerSlackSeconds = 5;

    private double? _monotonicDeadline;
    private bool _warnedNoEffect;
    private bool _warnedPostFailed;

    public YolkConfig Config { get; private set; } = config;

    /// <summary>The current run, or null while stopped.</summary>
    public RunState? Run { get; private set; }

    public bool IsRunning => Run is not null;

    /// <summary>Begins a run. Returns null if one is already under way.</summary>
    public YolkEvent.Started? Start(DateTime now, double monotonicNow)
    {
        if (IsRunning)
        {
            return null;
        }
        _warnedNoEffect = false;
        _warnedPostFailed = false;
        _monotonicDeadline = Config.Timeout is { } timeout ? monotonicNow + timeout : null;
        Run = new RunState(
            StartedAt: now,
            EstimatedEnd: Config.Timeout is { } t ? now.AddSeconds(t) : null,
            ActivityCount: 0,
            PausedBecause: null);
        return new YolkEvent.Started(Config);
    }

    /// <summary>Ends the run. Returns null if there was none.</summary>
    public YolkEvent.Stopped? Stop(StopReason reason)
    {
        if (!IsRunning)
        {
            return null;
        }
        Run = null;
        _monotonicDeadline = null;
        return new YolkEvent.Stopped(reason);
    }

    /// <summary>
    /// Whether the display would sleep before Yolk could act. If it does, the
    /// screen may lock first and Yolk pauses for good — better said up front.
    /// </summary>
    public YolkEvent.DisplaySleepTooSoon? CheckDisplaySleep(double? displaySleepSeconds) =>
        DisplaySleepBeatsYolk(Config, displaySleepSeconds)
            ? new YolkEvent.DisplaySleepTooSoon(displaySleepSeconds!.Value)
            : null;

    /// <summary>The same check, for a config that isn't running yet.</summary>
    public static bool DisplaySleepBeatsYolk(YolkConfig config, double? displaySleepSeconds) =>
        displaySleepSeconds is { } sleep && config.Threshold + config.Interval + TimerSlackSeconds >= sleep;

    /// <summary>
    /// Applies new settings without disturbing the start time or activity
    /// count. A changed timeout restarts the countdown from now.
    /// </summary>
    public Reconfiguration Reconfigure(YolkConfig newConfig, DateTime now, double monotonicNow)
    {
        var change = new Reconfiguration(
            TimeoutChanged: newConfig.Timeout != Config.Timeout,
            IntervalChanged: newConfig.Interval != Config.Interval);
        Config = newConfig;

        if (Run is { } run && change.TimeoutChanged)
        {
            _monotonicDeadline = newConfig.Timeout is { } timeout ? monotonicNow + timeout : null;
            Run = run with { EstimatedEnd = newConfig.Timeout is { } t ? now.AddSeconds(t) : null };
        }
        return change;
    }

    /// <summary>
    /// Whether the timeout has passed. Judged on a clock that counts through
    /// system sleep, so a machine that slept past its deadline still stops.
    /// </summary>
    public bool IsPastDeadline(double monotonicNow) =>
        _monotonicDeadline is { } deadline && monotonicNow >= deadline;

    /// <summary>
    /// Takes in the session's lock state. Only an active, unlocked, connected
    /// session gets input; anything else pauses. Returns Paused or Resumed on
    /// a change, and null otherwise — a pause is announced once, not per tick.
    /// </summary>
    public YolkEvent? ObserveConsole(ConsoleState state)
    {
        if (Run is not { } run)
        {
            return null;
        }
        if (state.ToPauseReason() is { } reason)
        {
            if (run.PausedBecause == reason)
            {
                return null;
            }
            Run = run with { PausedBecause = reason };
            return new YolkEvent.Paused(reason);
        }
        if (run.PausedBecause is null)
        {
            return null;
        }
        Run = run with { PausedBecause = null };
        return new YolkEvent.Resumed();
    }

    public bool IsPaused => Run?.PausedBecause is not null;

    /// <summary>Whether this much idle time calls for a nudge.</summary>
    public bool ShouldNudge(double idleSeconds) =>
        IsRunning && !IsPaused && idleSeconds >= Config.Threshold;

    /// <summary>
    /// The nudge could not be posted at all. Reported once per run.
    /// </summary>
    public YolkEvent.PostFailed? RecordPostFailed()
    {
        if (!IsRunning || _warnedPostFailed)
        {
            return null;
        }
        _warnedPostFailed = true;
        return new YolkEvent.PostFailed();
    }

    /// <summary>
    /// The nudge was posted; <paramref name="idleAfterSeconds"/> is the idle
    /// timer re-read afterwards. Concurrent real input can make a failed
    /// nudge look landed, which is fine: real input means someone is there.
    /// </summary>
    public YolkEvent? RecordNudge(double idleAfterSeconds)
    {
        if (Run is not { } run)
        {
            return null;
        }
        if (idleAfterSeconds < NudgeLandedBelowSeconds)
        {
            Run = run with { ActivityCount = run.ActivityCount + 1 };
            return new YolkEvent.ActivitySimulated(run.ActivityCount + 1);
        }
        if (_warnedNoEffect)
        {
            return null;
        }
        _warnedNoEffect = true;
        return new YolkEvent.PermissionMissing();
    }
}

public sealed record RunState(
    DateTime StartedAt,
    // Display only — "auto-exit at 17:30". Never consulted to decide whether
    // to stop; that is the monotonic deadline's job alone.
    DateTime? EstimatedEnd,
    int ActivityCount,
    PauseReason? PausedBecause);

/// <summary>What a reconfiguration changed, so the caller knows to reschedule.</summary>
public readonly record struct Reconfiguration(bool TimeoutChanged, bool IntervalChanged)
{
    public bool TimersChanged => TimeoutChanged || IntervalChanged;
}
