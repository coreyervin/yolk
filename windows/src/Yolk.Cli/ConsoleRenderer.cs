using System.Globalization;
using Yolk.Domain;
using Yolk.Domain.Configuration;
using Yolk.Domain.Sessions;

namespace Yolk.Cli;

/// <summary>
/// Turns <see cref="YolkEvent"/>s into the CLI's stdout/stderr output.
/// </summary>
/// <remarks>
/// A port of the Mac CLI's renderer: the same lines in the same order, with
/// only platform-specific wording changed. All presentation lives here —
/// verbose gating, timestamps, the startup tip, and the shutdown summary.
/// </remarks>
internal sealed class ConsoleRenderer
{
    /// <summary>
    /// Shown at startup on a bare invocation, to advertise options that are
    /// easy to miss. Any flag suppresses them.
    /// </summary>
    public static readonly string[] Tips =
    [
        "yolk -t 8h stops on its own after 8 hours. Also takes 90m, 45s, or 1d.",
        "yolk -v logs every idle check and simulated event, if you want to watch it work.",
        "yolk --threshold 20-40 nudges sooner, if Slack or Teams still slips to away.",
        "yolk -i 15 checks more often, for a tighter idle window.",
        "Lock with Win+L when you step away — yolk pauses, so Slack shows you away honestly.",
        "yolk --help lists every option.",
    ];

    /// <summary>
    /// The word the shutdown summary uses for a user-requested stop. Signal
    /// handlers overwrite it, since <see cref="StopReason.UserRequested"/>
    /// cannot tell Ctrl-C from a closed console window.
    /// </summary>
    public string UserStopReason { get; set; } = "stopped";

    private readonly bool _verbose;
    private readonly bool _invokedBare;
    private readonly int _pid;
    private readonly Func<DateTime> _now;
    private readonly Func<string?> _tipPicker;
    private readonly Action<string> _out;
    private readonly Action<string> _err;

    private YolkConfig _config = YolkConfig.Default;
    private DateTime? _startedAt;
    private int _activityCount;
    // The idle reading from the most recent check, reused by the simulated
    // line — re-reading after posting would report ~0.
    private double _lastCheckedIdle;

    public ConsoleRenderer(
        bool verbose,
        bool invokedBare,
        int pid,
        Func<DateTime> now,
        Action<string> @out,
        Action<string> err,
        Func<string?>? tipPicker = null)
    {
        _verbose = verbose;
        _invokedBare = invokedBare;
        _pid = pid;
        _now = now;
        _out = @out;
        _err = err;
        _tipPicker = tipPicker ?? (() => Tips[Random.Shared.Next(Tips.Length)]);
    }

    public void Handle(YolkEvent e)
    {
        switch (e)
        {
            case YolkEvent.Started started:
                _config = started.Config;
                _startedAt = _now();
                RenderBanner(started.Config);
                break;
            case YolkEvent.Checked checkedEvent:
                _lastCheckedIdle = checkedEvent.Idle;
                // At or above the target the engine goes on to simulate, and
                // the simulated line reports it — logging here would double up.
                if (checkedEvent.Idle < checkedEvent.NudgeAt)
                {
                    VerboseLog($"idle {(int)checkedEvent.Idle}s — next nudge at {(int)checkedEvent.NudgeAt}s idle");
                }
                break;
            case YolkEvent.ActivitySimulated simulated:
                _activityCount = simulated.Total;
                VerboseLog($"idle {(int)_lastCheckedIdle}s — simulated activity (#{simulated.Total})");
                break;
            case YolkEvent.Paused:
                // Reason-agnostic on purpose, matching the Mac CLI.
                VerboseLog("screen locked or session inactive — paused");
                break;
            case YolkEvent.Resumed:
                // Nothing, as on the Mac: the next `idle Ns` line is the signal.
                break;
            case YolkEvent.PermissionMissing:
                _err("""
                    ⚠ Simulated activity had no effect — Slack may still mark you away.
                      This usually means an app running as administrator has focus —
                      Windows won't deliver simulated input to it. Switch to a non-admin
                      window, or run yolk as administrator too.
                    """);
                break;
            case YolkEvent.PostFailed:
                _err("⚠ Could not post a synthetic input event — Slack may mark you away. "
                    + "Is yolk running in a desktop session?");
                break;
            case YolkEvent.DisplaySleepTooSoon tooSoon:
                var worstCase = _config.Threshold.Max + 5;
                _err($"""
                    ⚠ Your display sleeps after {(int)(tooSoon.DisplaySleep / 60)} min — sooner than yolk would act
                      with these settings (up to {(int)worstCase}s of idle). Once the display sleeps
                      and locks, yolk pauses and Slack will mark you away. Lower --threshold,
                      or raise "Turn off my screen after" in Settings → System → Power.
                    """);
                break;
            case YolkEvent.Stopped stopped:
                RenderSummary(stopped.Reason);
                break;
        }
    }

    /// <summary>
    /// The tail of the startup output: the tip, then the Ctrl-C hint. Split
    /// from <see cref="YolkEvent.Started"/> because it must land after the
    /// display-sleep warning, which the engine emits right after starting.
    /// </summary>
    public void RenderStartupTail()
    {
        if (_invokedBare && _tipPicker() is { } tip)
        {
            _out($"\n  💡 Did you know? {tip}\n");
        }
        _out("Press Ctrl-C to stop.");
    }

    private void RenderBanner(YolkConfig config)
    {
        _out($"""
            yolk v{YolkVersion.Current} (pid {_pid})
              ✓ system sleep prevented while running
              ✓ simulating activity {NudgeDescription(config.Threshold)} (checking at least every {(int)config.Interval}s)
              • lock your screen (Win+L) when you walk away — yolk pauses while locked
            """);
        if (config.Timeout is { } timeout)
        {
            // Display only. The real deadline is monotonic and lives in the engine.
            var estimatedEnd = _now().AddSeconds(timeout);
            // For day-plus timeouts a bare time of day reads as "today".
            _out($"  • auto-exit at {Format(estimatedEnd, includingDate: timeout >= 86400)}");
        }
    }

    /// <summary>"at a random 30–60s of idle", or "when idle ≥ 45s" for a fixed point.</summary>
    internal static string NudgeDescription(ThresholdRange threshold) =>
        threshold.IsFixed
            ? $"when idle ≥ {(int)threshold.Min}s"
            : $"at a random {(int)threshold.Min}–{(int)threshold.Max}s of idle";

    private void RenderSummary(StopReason reason)
    {
        var word = reason == StopReason.TimeoutReached ? "timeout reached" : UserStopReason;
        var elapsed = _startedAt is { } startedAt ? (_now() - startedAt).TotalSeconds : 0;
        var minutes = (int)(elapsed / 60);
        var plural = _activityCount == 1 ? "" : "s";
        _out($"\nyolk: {word} — ran {minutes} min, simulated activity {_activityCount} time{plural}. "
            + "Sleep settings restored.");
    }

    private void VerboseLog(string message)
    {
        if (_verbose)
        {
            _out($"[{Format(_now(), includingDate: false)}] {message}");
        }
    }

    internal static string Format(DateTime date, bool includingDate) =>
        date.ToString(includingDate ? "yyyy-MM-dd HH:mm:ss" : "HH:mm:ss", CultureInfo.InvariantCulture);
}
