using Yolk.Core;
using Yolk.TestSupport;

namespace Yolk.Core.Tests;

/// <summary>A port of the Mac's YolkSessionTests; the two engines must agree.</summary>
public class YolkSessionTests
{
    private static (YolkSession Session, EventCollector Log, ManualScheduler Scheduler) Make(
        FakeSystem system, double interval = 30, double threshold = 60, double? timeout = null)
    {
        var scheduler = new ManualScheduler();
        var session = new YolkSession(new YolkConfig(interval, threshold, timeout), system, scheduler);
        var log = new EventCollector();
        log.Attach(session);
        return (session, log, scheduler);
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────

    [Fact]
    public void StartEntersRunningHoldsAnAssertionAndAnnouncesItself()
    {
        var system = new FakeSystem();
        var (session, log, _) = Make(system);

        session.Start();

        Assert.True(session.IsRunning);
        Assert.Single(system.Created);
        Assert.Equal(new YolkEvent.Started(YolkConfig.Default), log.Events[0]);
    }

    [Fact]
    public void StopReturnsToStoppedReleasesTheAssertionAndReportsWhy()
    {
        var system = new FakeSystem();
        var (session, log, _) = Make(system);
        session.Start();

        session.Stop();

        Assert.False(session.IsRunning);
        Assert.Single(system.Released);
        Assert.Equal(new YolkEvent.Stopped(StopReason.UserRequested), log.Events[^1]);
    }

    [Fact]
    public void DisposeReleasesTheAssertionWithoutEmitting()
    {
        var system = new FakeSystem();
        var (session, log, _) = Make(system);
        session.Start();
        var before = log.Events.Count;

        session.Dispose();

        Assert.Single(system.Released);
        Assert.Equal(before, log.Events.Count);
    }

    [Fact]
    public void AFailedAssertionLeavesTheSessionStopped()
    {
        var system = new FakeSystem { AssertionFailure = new AssertionCreationException(5) };
        var (session, log, scheduler) = Make(system);

        var error = Assert.Throws<AssertionCreationException>(session.Start);

        Assert.Equal(5, error.Code);
        Assert.False(session.IsRunning);
        Assert.Empty(log.Events);
        Assert.Empty(scheduler.Entries);
    }

    [Fact]
    public void StartingTwiceIsIdempotent()
    {
        var system = new FakeSystem();
        var (session, _, _) = Make(system);

        session.Start();
        session.Start();

        Assert.Single(system.Created);
    }

    // ── Simulation ─────────────────────────────────────────────────────────

    [Fact]
    public void DoesNotSimulateWhileIdleIsBelowTheThreshold()
    {
        var system = new FakeSystem { Idle = 59 };
        var (session, log, _) = Make(system, threshold: 60);
        session.Start();

        session.Tick();

        Assert.Equal(0, system.PostCount);
        Assert.Contains(new YolkEvent.Checked(59), log.Events);
        Assert.Equal(0, session.Run!.ActivityCount);
    }

    [Fact]
    public void SimulatesOnceIdleReachesTheThreshold()
    {
        var system = new FakeSystem { Idle = 60 };
        var (session, log, _) = Make(system, threshold: 60);
        session.Start();

        session.Tick();

        Assert.Equal(1, system.PostCount);
        Assert.Contains(new YolkEvent.ActivitySimulated(1), log.Events);
        Assert.Equal(1, session.Run!.ActivityCount);
    }

    [Fact]
    public void CountsActivityOnlyWhenTheIdleTimerActuallyReset()
    {
        // Input posted, but idle never moved.
        var system = new FakeSystem { Idle = 90, IdleAfterPost = 90 };
        var (session, log, _) = Make(system);
        session.Start();

        session.Tick();

        Assert.Equal(1, system.PostCount);
        Assert.Equal(0, session.Run!.ActivityCount);
        Assert.Contains(new YolkEvent.PermissionMissing(), log.Events);
        Assert.DoesNotContain(new YolkEvent.ActivitySimulated(1), log.Events);
    }

    [Fact]
    public void WarnsAboutIneffectiveInputOnlyOncePerSession()
    {
        var system = new FakeSystem { Idle = 90, IdleAfterPost = 90 };
        var (session, log, _) = Make(system);
        session.Start();

        for (var i = 0; i < 5; i++)
        {
            session.Tick();
        }

        Assert.Equal(5, system.PostCount);
        Assert.Equal(1, log.Count(new YolkEvent.PermissionMissing()));
    }

    [Fact]
    public void WarnsAboutUnpostableInputOnlyOncePerSession()
    {
        var system = new FakeSystem { Idle = 90, PostSucceeds = false };
        var (session, log, _) = Make(system);
        session.Start();

        for (var i = 0; i < 5; i++)
        {
            session.Tick();
        }

        Assert.Equal(1, log.Count(new YolkEvent.PostFailed()));
        Assert.Equal(0, session.Run!.ActivityCount);
    }

    [Fact]
    public void TheOneShotWarningsResetForANewSession()
    {
        var system = new FakeSystem { Idle = 90, IdleAfterPost = 90 };
        var (session, log, _) = Make(system);
        session.Start();
        session.Tick();
        session.Stop();

        session.Start();
        session.Tick();

        Assert.Equal(2, log.Count(new YolkEvent.PermissionMissing()));
    }

    // ── Pausing ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ConsoleState.Locked, PauseReason.ScreenLocked)]
    [InlineData(ConsoleState.SwitchedOut, PauseReason.SessionSwitchedOut)]
    [InlineData(ConsoleState.Unknown, PauseReason.SessionUnknown)]
    public void PausesInsteadOfPostingWhenTheSessionIsntOursAndUnlocked(ConsoleState state, PauseReason reason)
    {
        var system = new FakeSystem { Idle = 90, Console = state };
        var (session, log, _) = Make(system);
        session.Start();

        session.Tick();

        Assert.Equal(0, system.PostCount);
        Assert.Contains(new YolkEvent.Paused(reason), log.Events);
        Assert.Equal(reason, session.Run!.PausedBecause);
    }

    [Fact]
    public void AnnouncesAPauseOnceNotOnEveryTick()
    {
        var system = new FakeSystem { Idle = 90, Console = ConsoleState.Locked };
        var (session, log, _) = Make(system);
        session.Start();

        for (var i = 0; i < 5; i++)
        {
            session.Tick();
        }

        Assert.Equal(1, log.Count(new YolkEvent.Paused(PauseReason.ScreenLocked)));
    }

    [Fact]
    public void ResumesExactlyOnceWhenTheConsoleBecomesActiveAgain()
    {
        var system = new FakeSystem { Idle = 90, Console = ConsoleState.Locked };
        var (session, log, _) = Make(system);
        session.Start();
        session.Tick();
        session.Tick();

        system.Console = ConsoleState.Active;
        session.Tick();
        session.Tick();

        Assert.Equal(1, log.Count(new YolkEvent.Resumed()));
        Assert.Null(session.Run!.PausedBecause);
        Assert.Equal(2, system.PostCount);
    }

    // ── Timeout ────────────────────────────────────────────────────────────

    [Fact]
    public void StopsWhenTheMonotonicDeadlineIsReached()
    {
        var system = new FakeSystem();
        var (session, log, _) = Make(system, timeout: 3600);
        session.Start();

        system.Monotonic = 3599;
        session.Tick();
        Assert.True(session.IsRunning);

        system.Monotonic = 3600;
        session.Tick();

        Assert.False(session.IsRunning);
        Assert.Equal(new YolkEvent.Stopped(StopReason.TimeoutReached), log.Events[^1]);
    }

    [Fact]
    public void TheDeadlineSurvivesASystemSleepThatJumpsTheMonotonicClock()
    {
        var system = new FakeSystem();
        var (session, log, _) = Make(system, timeout: 3600);
        session.Start();

        // Machine slept for hours; the timers never fired.
        system.Monotonic = 50_000;
        session.Tick();

        Assert.False(session.IsRunning);
        Assert.Equal(new YolkEvent.Stopped(StopReason.TimeoutReached), log.Events[^1]);
    }

    [Fact]
    public void ANullTimeoutNeverExpires()
    {
        var system = new FakeSystem();
        var (session, _, _) = Make(system, timeout: null);
        session.Start();

        system.Monotonic = 1_000_000;
        session.Tick();

        Assert.True(session.IsRunning);
    }

    [Fact]
    public void TheDeadlineTimerStopsTheSessionAsATimeout()
    {
        var system = new FakeSystem();
        var (session, log, scheduler) = Make(system, timeout: 60);
        session.Start();

        var deadline = Assert.Single(scheduler.Live, e => e.Period is null);
        Assert.Equal(TimeSpan.FromSeconds(60), deadline.DueTime);
        deadline.Fire();

        Assert.False(session.IsRunning);
        Assert.Equal(new YolkEvent.Stopped(StopReason.TimeoutReached), log.Events[^1]);
    }

    // ── Timers ─────────────────────────────────────────────────────────────

    [Fact]
    public void StartSchedulesATickerAtTheConfiguredInterval()
    {
        var system = new FakeSystem();
        var (session, _, scheduler) = Make(system, interval: 15);

        session.Start();

        var ticker = Assert.Single(scheduler.Live);
        Assert.Equal(TimeSpan.FromSeconds(1), ticker.DueTime);
        Assert.Equal(TimeSpan.FromSeconds(15), ticker.Period);
    }

    [Fact]
    public void StopCancelsEveryTimer()
    {
        var system = new FakeSystem();
        var (session, _, scheduler) = Make(system, timeout: 60);
        session.Start();

        session.Stop();

        Assert.Empty(scheduler.Live);
    }

    // ── Reconfigure ────────────────────────────────────────────────────────

    [Fact]
    public void ReconfigurePreservesStartedAtAndActivityCountOnALiveSession()
    {
        var system = new FakeSystem { Idle = 90 };
        var (session, _, _) = Make(system);
        session.Start();
        session.Tick();
        var before = session.Run!;

        session.Reconfigure(new YolkConfig(interval: 15, threshold: 45));

        var after = session.Run!;
        Assert.Equal(before.StartedAt, after.StartedAt);
        Assert.Equal(before.ActivityCount, after.ActivityCount);
        Assert.Equal(15, session.Config.Interval);
        Assert.Equal(45, session.Config.Threshold);
    }

    [Fact]
    public void AChangedIntervalReschedulesTheTicker()
    {
        var system = new FakeSystem();
        var (session, _, scheduler) = Make(system);
        session.Start();

        session.Reconfigure(new YolkConfig(interval: 10));

        var ticker = Assert.Single(scheduler.Live);
        Assert.Equal(TimeSpan.FromSeconds(10), ticker.Period);
    }

    [Fact]
    public void AChangedTimeoutRestartsTheCountdownFromNow()
    {
        var system = new FakeSystem();
        var (session, _, _) = Make(system, timeout: 3600);
        session.Start();

        system.Monotonic = 1800;
        session.Reconfigure(new YolkConfig(timeout: 7200));

        // The original deadline was 3600; it must no longer apply.
        system.Monotonic = 3601;
        session.Tick();
        Assert.True(session.IsRunning);

        // The new deadline is 1800 + 7200.
        system.Monotonic = 9000;
        session.Tick();
        Assert.False(session.IsRunning);
    }

    [Fact]
    public void ReconfiguringToNoTimeoutCancelsTheDeadlineAndKeepsRunning()
    {
        var system = new FakeSystem();
        var (session, _, scheduler) = Make(system, timeout: 60);
        session.Start();

        session.Reconfigure(new YolkConfig(timeout: null));

        system.Monotonic = 100_000;
        session.Tick();
        Assert.True(session.IsRunning);
        Assert.DoesNotContain(scheduler.Live, e => e.Period is null);
    }

    [Fact]
    public void ReconfigureWhileStoppedJustStoresTheConfig()
    {
        var system = new FakeSystem();
        var (session, _, scheduler) = Make(system);

        session.Reconfigure(new YolkConfig(interval: 90));

        Assert.False(session.IsRunning);
        Assert.Equal(90, session.Config.Interval);
        Assert.Empty(scheduler.Entries);
    }

    // ── Display sleep ──────────────────────────────────────────────────────

    [Fact]
    public void WarnsAtStartWhenTheDisplayWouldSleepBeforeYolkCouldAct()
    {
        // threshold 60 + interval 30 + 5 exceeds this.
        var system = new FakeSystem { DisplaySleep = 60 };
        var (session, log, _) = Make(system);

        session.Start();

        Assert.Equal(1, log.Count(new YolkEvent.DisplaySleepTooSoon(60)));
    }

    [Fact]
    public void StaysQuietWhenDisplaySleepIsComfortablyLongerThanTheCheckWindow()
    {
        var system = new FakeSystem { DisplaySleep = 1800 };
        var (session, log, _) = Make(system);

        session.Start();

        Assert.DoesNotContain(log.Events, e => e is YolkEvent.DisplaySleepTooSoon);
    }
}
