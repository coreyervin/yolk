using Yolk.Domain.Configuration;
using Yolk.Domain.Sessions;
using Yolk.TestSupport;

namespace Yolk.Application.Tests.Features.ActivityCheck;

/// <summary>
/// One tick of the check loop, end to end. A port of the Mac's YolkSession
/// tick tests; the two engines must agree.
/// </summary>
public class CheckActivityTests
{
    private static TestHost Running(double idle = 0, double threshold = 60, double? timeout = null,
        ConsoleState console = ConsoleState.Active)
    {
        var host = new TestHost();
        host.System.Idle = idle;
        host.System.Console = console;
        // A fixed threshold, so these tests know exactly when a nudge is due;
        // the random range has its own tests below.
        host.Start(new YolkConfig(threshold: ThresholdRange.Fixed(threshold), timeout: timeout));
        return host;
    }

    // ── Simulation ─────────────────────────────────────────────────────────

    [Fact]
    public void DoesNotSimulateWhileIdleIsBelowTheThreshold()
    {
        using var host = Running(idle: 59, threshold: 60);

        host.Tick();

        Assert.Equal(0, host.System.PostCount);
        Assert.Contains(new YolkEvent.Checked(59, NudgeAt: 60), host.Events.Events);
        Assert.Equal(0, host.Status().ActivityCount);
    }

    [Fact]
    public void SimulatesOnceIdleReachesTheThreshold()
    {
        using var host = Running(idle: 60, threshold: 60);

        host.Tick();

        Assert.Equal(1, host.System.PostCount);
        Assert.Contains(new YolkEvent.ActivitySimulated(1), host.Events.Events);
        Assert.Equal(1, host.Status().ActivityCount);
    }

    [Fact]
    public void CountsActivityOnlyWhenTheIdleTimerActuallyReset()
    {
        using var host = Running(idle: 90);
        host.System.IdleAfterPost = 90; // input posted, but idle never moved

        host.Tick();

        Assert.Equal(1, host.System.PostCount);
        Assert.Equal(0, host.Status().ActivityCount);
        Assert.Contains(new YolkEvent.PermissionMissing(), host.Events.Events);
    }

    [Fact]
    public void WarnsAboutIneffectiveInputOnlyOncePerSession()
    {
        using var host = Running(idle: 90);
        host.System.IdleAfterPost = 90;

        for (var i = 0; i < 5; i++)
        {
            host.Tick();
        }

        Assert.Equal(5, host.System.PostCount);
        Assert.Equal(1, host.Events.Count(new YolkEvent.PermissionMissing()));
    }

    [Fact]
    public void WarnsAboutUnpostableInputOnlyOncePerSession()
    {
        using var host = Running(idle: 90);
        host.System.PostSucceeds = false;

        for (var i = 0; i < 5; i++)
        {
            host.Tick();
        }

        Assert.Equal(1, host.Events.Count(new YolkEvent.PostFailed()));
        Assert.Equal(0, host.Status().ActivityCount);
    }

    [Fact]
    public void TheOneShotWarningsResetForANewSession()
    {
        using var host = Running(idle: 90);
        host.System.IdleAfterPost = 90;
        host.Tick();
        host.Stop();

        host.Start();
        host.Tick();

        Assert.Equal(2, host.Events.Count(new YolkEvent.PermissionMissing()));
    }

    [Fact]
    public void ATickWhileStoppedDoesNothing()
    {
        using var host = new TestHost();
        host.System.Idle = 90;

        host.Tick();

        Assert.Equal(0, host.System.PostCount);
        Assert.Empty(host.Events.Events);
    }

    // ── Pausing ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ConsoleState.Locked, PauseReason.ScreenLocked)]
    [InlineData(ConsoleState.SwitchedOut, PauseReason.SessionSwitchedOut)]
    [InlineData(ConsoleState.Unknown, PauseReason.SessionUnknown)]
    public void PausesInsteadOfPostingWhenTheSessionIsntOursAndUnlocked(ConsoleState state, PauseReason reason)
    {
        using var host = Running(idle: 90, console: state);

        host.Tick();

        Assert.Equal(0, host.System.PostCount);
        Assert.Contains(new YolkEvent.Paused(reason), host.Events.Events);
        Assert.Equal(reason, host.Status().PauseReason);
        Assert.DoesNotContain(host.Events.Events, e => e is YolkEvent.Checked);
    }

    [Fact]
    public void AnnouncesAPauseOnceNotOnEveryTick()
    {
        using var host = Running(idle: 90, console: ConsoleState.Locked);

        for (var i = 0; i < 5; i++)
        {
            host.Tick();
        }

        Assert.Equal(1, host.Events.Count(new YolkEvent.Paused(PauseReason.ScreenLocked)));
    }

    [Fact]
    public void ResumesExactlyOnceWhenTheConsoleBecomesActiveAgain()
    {
        using var host = Running(idle: 90, console: ConsoleState.Locked);
        host.Tick();
        host.Tick();

        host.System.Console = ConsoleState.Active;
        host.Tick();
        host.Tick();

        Assert.Equal(1, host.Events.Count(new YolkEvent.Resumed()));
        Assert.Null(host.Status().PauseReason);
        Assert.Equal(2, host.System.PostCount);
    }

    // ── Timeout ────────────────────────────────────────────────────────────

    [Fact]
    public void StopsWhenTheMonotonicDeadlineIsReached()
    {
        using var host = Running(timeout: 3600);

        host.System.Monotonic = 3599;
        host.Tick();
        Assert.True(host.Status().IsActive);

        host.System.Monotonic = 3600;
        host.Tick();

        Assert.False(host.Status().IsActive);
        Assert.Single(host.System.Released);
        Assert.Equal(new YolkEvent.Stopped(StopReason.TimeoutReached), host.Events.Events[^1]);
    }

    [Fact]
    public void TheDeadlineSurvivesASystemSleepThatJumpsTheMonotonicClock()
    {
        using var host = Running(timeout: 3600);

        // The machine slept for hours; the timers never fired.
        host.System.Monotonic = 50_000;
        host.Tick();

        Assert.False(host.Status().IsActive);
        Assert.Equal(new YolkEvent.Stopped(StopReason.TimeoutReached), host.Events.Events[^1]);
    }

    [Fact]
    public void ANullTimeoutNeverExpires()
    {
        using var host = Running(timeout: null);

        host.System.Monotonic = 1_000_000;
        host.Tick();

        Assert.True(host.Status().IsActive);
    }

    // ── Random nudge timing ────────────────────────────────────────────────

    [Fact]
    public void IdleBelowTheRandomPointDoesntNudgeEvenPastTheRangesBottom()
    {
        using var host = new TestHost();
        host.System.RandomUnit = 0.5; // 30–60 → 45
        host.Start();

        host.System.Idle = 40;
        host.Tick();

        Assert.Equal(0, host.System.PostCount);
        Assert.Contains(new YolkEvent.Checked(40, NudgeAt: 45), host.Events.Events);
    }

    [Fact]
    public void ANewRandomPointIsChosenAfterEveryNudge()
    {
        using var host = new TestHost();
        host.System.RandomUnit = 0; // first point 30
        host.Start();

        host.System.Idle = 30;
        host.System.RandomUnit = 0.5; // next point 45
        host.Tick();
        Assert.Equal(1, host.System.PostCount);

        host.System.Idle = 40;
        host.Tick();
        Assert.Equal(1, host.System.PostCount);
        Assert.Contains(new YolkEvent.Checked(40, NudgeAt: 45), host.Events.Events);
    }

    /// <summary>
    /// The point of the feature: each check is timed to land when idle
    /// reaches the random point, so nudges fall anywhere in the range rather
    /// than on the next fixed tick.
    /// </summary>
    [Fact]
    public void TheCheckTimerReschedulesItselfForTheNextNudge()
    {
        using var host = new TestHost();
        host.System.RandomUnit = 0.5; // 30–60 → 45
        host.System.Idle = 40;
        host.Start();

        Assert.Equal(TimeSpan.FromSeconds(1), Assert.Single(host.Scheduler.Live).DueTime);
        Assert.Single(host.Scheduler.Live).Fire();

        Assert.Equal(TimeSpan.FromSeconds(5), Assert.Single(host.Scheduler.Live).DueTime);
    }

    [Fact]
    public void AfterANudgeTheNextCheckIsTimedFromZeroIdle()
    {
        using var host = new TestHost();
        host.System.RandomUnit = 0; // 30
        host.System.Idle = 30;
        host.Start(new YolkConfig(interval: 120));
        host.System.RandomUnit = 0.5; // next point 45

        Assert.Single(host.Scheduler.Live).Fire();

        Assert.Equal(1, host.System.PostCount);
        Assert.Equal(TimeSpan.FromSeconds(45), Assert.Single(host.Scheduler.Live).DueTime);
    }

    [Fact]
    public void ChecksAreNeverMoreThanTheIntervalApart()
    {
        using var host = new TestHost();
        host.System.Idle = 0;
        host.Start(new YolkConfig(interval: 10));

        Assert.Single(host.Scheduler.Live).Fire();

        Assert.Equal(TimeSpan.FromSeconds(10), Assert.Single(host.Scheduler.Live).DueTime);
    }

    [Fact]
    public void APausedSessionChecksBackAfterTheInterval()
    {
        using var host = new TestHost();
        host.System.Console = ConsoleState.Locked;
        host.Start(new YolkConfig(interval: 20));

        Assert.Single(host.Scheduler.Live).Fire();

        Assert.Equal(TimeSpan.FromSeconds(20), Assert.Single(host.Scheduler.Live).DueTime);
    }
}
