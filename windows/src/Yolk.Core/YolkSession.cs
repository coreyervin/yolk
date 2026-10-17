namespace Yolk.Core;

/// <summary>
/// The engine. Owns the timers, holds the power request while running, and
/// emits events. Throws rather than exiting — only the CLI exits.
/// </summary>
/// <remarks>
/// Single-threaded by contract: every call, and every scheduler callback,
/// happens on the thread that owns the session. A port of the Mac's
/// <c>YolkSession</c>; the two must behave identically.
/// </remarks>
public sealed class YolkSession : IDisposable
{
    public sealed record RunState(
        DateTime StartedAt,
        // Display only — "auto-exit at 17:30". Never consulted to decide
        // whether to stop; that is the monotonic deadline's job alone.
        DateTime? EstimatedEnd,
        int ActivityCount,
        PauseReason? PausedBecause);

    internal const string AssertionReason = "yolk: keeping the PC awake while agents run";

    private readonly ISystemEnvironment _environment;
    private readonly IScheduler _scheduler;
    private readonly List<IDisposable> _timers = [];
    private PowerAssertion? _assertion;
    private double? _monotonicDeadline;
    private bool _warnedPermissionMissing;
    private bool _warnedPostFailed;

    public YolkSession(YolkConfig config, ISystemEnvironment environment, IScheduler scheduler)
    {
        Config = config;
        _environment = environment;
        _scheduler = scheduler;
    }

    /// <summary>The current run, or null while stopped.</summary>
    public RunState? Run { get; private set; }

    public bool IsRunning => Run is not null;

    public YolkConfig Config { get; private set; }

    public event Action<YolkEvent>? Emitted;

    /// <exception cref="AssertionCreationException">
    /// The power request was refused. The session stays stopped.
    /// </exception>
    public void Start()
    {
        if (IsRunning)
        {
            return;
        }
        _warnedPermissionMissing = false;
        _warnedPostFailed = false;

        // Throws before any state changes, so a failed request leaves the
        // session cleanly stopped rather than half-started.
        _assertion = new PowerAssertion(AssertionReason, _environment);

        var startedAt = _environment.Now();
        _monotonicDeadline = Config.Timeout is { } timeout ? _environment.MonotonicNow() + timeout : null;
        Run = new RunState(
            StartedAt: startedAt,
            EstimatedEnd: Config.Timeout is { } t ? startedAt.AddSeconds(t) : null,
            ActivityCount: 0,
            PausedBecause: null);

        Emit(new YolkEvent.Started(Config));

        // Worst case yolk acts after threshold + interval + timer slack of
        // idle. If the display sleeps sooner, the screen may lock first and
        // yolk pauses forever — say so up front rather than fail silently.
        if (_environment.DisplaySleepSeconds() is { } displaySleep
            && Config.Threshold + Config.Interval + 5 >= displaySleep)
        {
            Emit(new YolkEvent.DisplaySleepTooSoon(displaySleep));
        }

        ScheduleTimers();
    }

    public void Stop() => Stop(StopReason.UserRequested);

    private void Stop(StopReason reason)
    {
        if (!IsRunning)
        {
            return;
        }
        CancelTimers();
        _assertion?.Dispose();
        _assertion = null;
        _monotonicDeadline = null;
        Run = null;
        Emit(new YolkEvent.Stopped(reason));
    }

    /// <summary>
    /// Applies new settings to a live session without disturbing
    /// <see cref="RunState.StartedAt"/> or the activity count. A changed
    /// timeout restarts the countdown from now. When stopped, the config is
    /// simply stored.
    /// </summary>
    public void Reconfigure(YolkConfig newConfig)
    {
        var timeoutChanged = newConfig.Timeout != Config.Timeout;
        var intervalChanged = newConfig.Interval != Config.Interval;
        Config = newConfig;

        if (Run is not { } run)
        {
            return;
        }

        if (timeoutChanged)
        {
            _monotonicDeadline = newConfig.Timeout is { } timeout
                ? _environment.MonotonicNow() + timeout
                : null;
            Run = run with
            {
                EstimatedEnd = newConfig.Timeout is { } t ? _environment.Now().AddSeconds(t) : null,
            };
        }
        if (timeoutChanged || intervalChanged)
        {
            ScheduleTimers();
        }
    }

    /// <summary>Releases the power request without emitting anything.</summary>
    public void Dispose()
    {
        CancelTimers();
        _assertion?.Dispose();
        _assertion = null;
        _monotonicDeadline = null;
        Run = null;
    }

    /// <summary>
    /// One iteration of the check loop. Internal so tests and AppModel's tests
    /// can drive it directly instead of waiting on real timers.
    /// </summary>
    internal void Tick()
    {
        if (Run is not { } run)
        {
            return;
        }

        if (_monotonicDeadline is { } deadline && _environment.MonotonicNow() >= deadline)
        {
            Stop(StopReason.TimeoutReached);
            return;
        }

        // Fail safe: only an active, unlocked, connected session gets input.
        if (_environment.GetConsoleState().ToPauseReason() is { } reason)
        {
            if (run.PausedBecause == reason)
            {
                return;
            }
            Run = run with { PausedBecause = reason };
            Emit(new YolkEvent.Paused(reason));
            return;
        }
        if (run.PausedBecause is not null)
        {
            run = run with { PausedBecause = null };
            Run = run;
            Emit(new YolkEvent.Resumed());
        }

        var idle = _environment.IdleSeconds();
        Emit(new YolkEvent.Checked(idle));
        if (idle < Config.Threshold)
        {
            return;
        }

        if (!_environment.PostActivity())
        {
            if (!_warnedPostFailed)
            {
                _warnedPostFailed = true;
                Emit(new YolkEvent.PostFailed());
            }
            return;
        }

        // Settle, then confirm the idle timer actually reset. Concurrent real
        // input can false-pass this, which is acceptable: real input means
        // presence, and a failure is caught on the next idle tick.
        _environment.WaitForEventDelivery();
        if (_environment.IdleSeconds() < 1.0)
        {
            run = run with { ActivityCount = run.ActivityCount + 1 };
            Run = run;
            Emit(new YolkEvent.ActivitySimulated(run.ActivityCount));
        }
        else if (!_warnedPermissionMissing)
        {
            _warnedPermissionMissing = true;
            Emit(new YolkEvent.PermissionMissing());
        }
    }

    private void Emit(YolkEvent e) => Emitted?.Invoke(e);

    private void ScheduleTimers()
    {
        CancelTimers();

        _timers.Add(_scheduler.Schedule(
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(Config.Interval), Tick));

        // One-shot so the timeout fires on time rather than up to an interval
        // late. Timers may not count through system sleep, so the monotonic
        // check in Tick() remains the sleep-safe backstop.
        if (Config.Timeout is { } timeout)
        {
            _timers.Add(_scheduler.Schedule(
                TimeSpan.FromSeconds(timeout), null, () => Stop(StopReason.TimeoutReached)));
        }
    }

    private void CancelTimers()
    {
        foreach (var timer in _timers)
        {
            timer.Dispose();
        }
        _timers.Clear();
    }
}
