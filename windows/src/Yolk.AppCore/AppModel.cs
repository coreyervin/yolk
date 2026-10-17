using System.ComponentModel;
using System.Runtime.CompilerServices;
using Yolk.Core;

namespace Yolk.AppCore;

/// <summary>
/// Observable wrapper over <see cref="YolkSession"/> for the tray app — the
/// counterpart of the Mac's <c>AppModel</c>.
/// </summary>
/// <remarks>
/// Owns everything the UI needs and nothing it doesn't: settings and their
/// persistence, the session's state, and the system state the settings
/// window reports. The WinForms code reads this and calls these methods; it
/// holds no logic of its own. Single-threaded, like the session.
/// </remarks>
public sealed class AppModel : INotifyPropertyChanged, IDisposable
{
    private static class Key
    {
        public const string Interval = "interval";
        public const string Threshold = "threshold";
        public const string StopAfter = "stopAfter";
    }

    private readonly YolkSession _session;
    private readonly ISystemEnvironment _environment;
    private readonly ISettingsStore _store;
    private readonly ILoginItemService _loginItems;

    private bool _isActive;
    private PauseReason? _pauseReason;
    private int _activityCount;
    private DateTime? _startedAt;
    private DateTime? _estimatedEnd;
    private double? _displaySleepWarning;
    private bool _canEnableLaunchAtLogin = true;
    private string? _launchAtLoginProblem;
    private bool _launchAtLogin;
    private double _interval;
    private double _threshold;
    private StopAfter _stopAfter;

    public AppModel(
        ISystemEnvironment environment,
        IScheduler scheduler,
        ISettingsStore store,
        ILoginItemService loginItems)
    {
        _environment = environment;
        _store = store;
        _loginItems = loginItems;

        // Stored values are validated rather than trusted: the registry can be
        // hand-edited, or written by a build with different bounds.
        _interval = Validate(store.ReadDouble(Key.Interval), YolkConfig.IsValidInterval, YolkConfig.DefaultInterval);
        _threshold = Validate(store.ReadDouble(Key.Threshold), YolkConfig.IsValidThreshold, YolkConfig.DefaultThreshold);
        _stopAfter = StopAfterExtensions.FromStorageKey(store.ReadString(Key.StopAfter)) ?? StopAfter.Never;

        // Starts idle: opening the app makes Yolk available, it does not begin
        // a session.
        _session = new YolkSession(MakeConfig(), environment, scheduler);
        _session.Emitted += Handle;
        RefreshSystemState();
        RefreshLoginItemState();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // ── Session state ──────────────────────────────────────────────────────

    public bool IsActive { get => _isActive; private set => Set(ref _isActive, value); }

    /// <summary>
    /// Null while nudging; set while paused. A paused session is still active
    /// — it resumes on its own when the screen unlocks.
    /// </summary>
    public PauseReason? PauseReason { get => _pauseReason; private set => Set(ref _pauseReason, value); }

    public int ActivityCount { get => _activityCount; private set => Set(ref _activityCount, value); }

    public DateTime? StartedAt { get => _startedAt; private set => Set(ref _startedAt, value); }

    /// <summary>
    /// Display only, for "Stops at 17:30". The real deadline is monotonic and
    /// lives in the engine.
    /// </summary>
    public DateTime? EstimatedEnd { get => _estimatedEnd; private set => Set(ref _estimatedEnd, value); }

    // ── System state ───────────────────────────────────────────────────────

    /// <summary>
    /// The display-off delay, but only when it beats yolk to the punch —
    /// otherwise null. Same condition the CLI warns about.
    /// </summary>
    public double? DisplaySleepWarning { get => _displaySleepWarning; private set => Set(ref _displaySleepWarning, value); }

    public bool CanEnableLaunchAtLogin { get => _canEnableLaunchAtLogin; private set => Set(ref _canEnableLaunchAtLogin, value); }

    /// <summary>Why Launch at sign-in is unavailable or did not take, in plain language.</summary>
    public string? LaunchAtLoginProblem { get => _launchAtLoginProblem; private set => Set(ref _launchAtLoginProblem, value); }

    /// <summary>
    /// Reflects the real registration state, never a cached preference — the
    /// user can remove the startup entry in Task Manager behind the app's back.
    /// </summary>
    public bool LaunchAtLogin
    {
        get => _launchAtLogin;
        set
        {
            if (value == _launchAtLogin && LaunchAtLoginProblem is null)
            {
                return;
            }
            if (!CanEnableLaunchAtLogin)
            {
                // Refused up front rather than attempted and silently reverted.
                Set(ref _launchAtLogin, false);
                return;
            }
            try
            {
                if (value)
                {
                    _loginItems.Register();
                }
                else
                {
                    _loginItems.Unregister();
                }
                LaunchAtLoginProblem = null;
                Set(ref _launchAtLogin, value);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Report the truth: the system state did not change.
                LaunchAtLoginProblem = value
                    ? $"Yolk could not be added to your startup apps. {error.Message}"
                    : $"Yolk could not be removed from your startup apps. {error.Message}";
                Set(ref _launchAtLogin, _loginItems.IsEnabled());
            }
        }
    }

    // ── Settings ───────────────────────────────────────────────────────────

    public double Interval
    {
        get => _interval;
        set
        {
            if (double.IsNaN(value))
            {
                return;
            }
            var clamped = Math.Clamp(value, YolkConfig.MinInterval, YolkConfig.MaxInterval);
            if (!Set(ref _interval, clamped, nameof(Interval)))
            {
                return;
            }
            _store.WriteDouble(Key.Interval, clamped);
            ApplyConfig();
        }
    }

    public double Threshold
    {
        get => _threshold;
        set
        {
            if (double.IsNaN(value))
            {
                return;
            }
            var clamped = Math.Clamp(value, YolkConfig.MinThreshold, YolkConfig.MaxThreshold);
            if (!Set(ref _threshold, clamped, nameof(Threshold)))
            {
                return;
            }
            _store.WriteDouble(Key.Threshold, clamped);
            ApplyConfig();
        }
    }

    public StopAfter StopAfter
    {
        get => _stopAfter;
        set
        {
            if (!Set(ref _stopAfter, value, nameof(StopAfter)))
            {
                return;
            }
            _store.WriteString(Key.StopAfter, value.StorageKey());
            ApplyConfig();
        }
    }

    // ── Intent ─────────────────────────────────────────────────────────────

    public void SetActive(bool active)
    {
        if (active == IsActive)
        {
            return;
        }
        if (active)
        {
            try
            {
                _session.Start();
            }
            catch (AssertionCreationException)
            {
                // Without a power request there is nothing to show as active;
                // the menu stays idle rather than lying about it.
                IsActive = false;
            }
        }
        else
        {
            _session.Stop();
        }
    }

    public void Toggle() => SetActive(!IsActive);

    /// <summary>
    /// Re-reads the system state the settings window displays. Cheap enough to
    /// call on demand, which is how the app keeps it current.
    /// </summary>
    public void RefreshSystemState()
    {
        DisplaySleepWarning = _environment.DisplaySleepSeconds() is { } sleep
            && _threshold + _interval + 5 >= sleep
            ? sleep
            : null;
    }

    /// <summary>Re-reads whether Yolk is a startup app and whether it could be one.</summary>
    public void RefreshLoginItemState()
    {
        CanEnableLaunchAtLogin = _loginItems.CanRegister();
        Set(ref _launchAtLogin, CanEnableLaunchAtLogin && _loginItems.IsEnabled(), nameof(LaunchAtLogin));
        LaunchAtLoginProblem = CanEnableLaunchAtLogin
            ? null
            : "Launch at sign-in isn't available for this copy of Yolk.";
    }

    /// <summary>The menu's first line.</summary>
    public string StatusTitle => !IsActive
        ? "Yolk is idle"
        : PauseReason switch
        {
            null => "Yolk is active",
            Core.PauseReason.ScreenLocked => "Paused — screen locked",
            Core.PauseReason.SessionSwitchedOut => "Paused — another user is using this PC",
            _ => "Paused — session state unavailable",
        };

    /// <summary>
    /// The menu's second line — "Awake 2h 14m · 47 nudges", or null while
    /// idle. Still shown while paused: the session is running, just not nudging.
    /// </summary>
    public string? StatusDetail(DateTime asOf)
    {
        if (UptimeDescription(asOf) is not { } uptime)
        {
            return null;
        }
        var nudges = ActivityCount switch
        {
            0 => "no nudges",
            1 => "1 nudge",
            _ => $"{ActivityCount} nudges",
        };
        return $"Awake {uptime} · {nudges}";
    }

    /// <summary>"2h 14m", or null while idle.</summary>
    public string? UptimeDescription(DateTime asOf)
    {
        if (StartedAt is not { } startedAt)
        {
            return null;
        }
        var minutes = (int)(Math.Max(0, (asOf - startedAt).TotalSeconds) / 60);
        return minutes < 60 ? $"{minutes}m" : $"{minutes / 60}h {minutes % 60}m";
    }

    public void Dispose()
    {
        _session.Emitted -= Handle;
        _session.Dispose();
    }

    /// <summary>
    /// Drives one check-loop iteration without waiting on a real timer.
    /// Test-only seam: the app is driven by the session's own timers.
    /// </summary>
    internal void TickForTesting() => _session.Tick();

    // ── Internals ──────────────────────────────────────────────────────────

    private void Handle(YolkEvent e)
    {
        switch (e)
        {
            case YolkEvent.Started:
                IsActive = true;
                ActivityCount = 0;
                PauseReason = null;
                StartedAt = _session.Run?.StartedAt;
                EstimatedEnd = _session.Run?.EstimatedEnd;
                break;
            case YolkEvent.ActivitySimulated simulated:
                ActivityCount = simulated.Total;
                break;
            case YolkEvent.Paused paused:
                PauseReason = paused.Reason;
                break;
            case YolkEvent.Resumed:
                PauseReason = null;
                break;
            case YolkEvent.Stopped:
                IsActive = false;
                PauseReason = null;
                ActivityCount = 0;
                StartedAt = null;
                EstimatedEnd = null;
                break;
            // Checked, PermissionMissing, PostFailed, DisplaySleepTooSoon:
            // nothing observable — the menu shows state, not a log.
        }
    }

    // Both settings are clamped on the way in, so this cannot throw.
    private YolkConfig MakeConfig() => new(_interval, _threshold, _stopAfter.Timeout());

    /// <summary>
    /// Applies the current settings to the session in place, keeping the start
    /// time and nudge count, and refreshes the warning that depends on them.
    /// </summary>
    private void ApplyConfig()
    {
        _session.Reconfigure(MakeConfig());
        if (_session.Run is { } run)
        {
            EstimatedEnd = run.EstimatedEnd;
        }
        RefreshSystemState();
    }

    private static double Validate(double? stored, Func<double, bool> isValid, double fallback) =>
        stored is { } value && isValid(value) ? value : fallback;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
