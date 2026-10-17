using Yolk.Domain.Configuration;
using Yolk.Domain.Sessions;

namespace Yolk.Domain.Tests.Sessions;

/// <summary>
/// The session's rules on their own, with readings passed in by hand. The
/// Application tests cover the same rules end to end through the slices.
/// </summary>
public class KeepAwakeSessionTests
{
    private static readonly DateTime Now = new(2026, 10, 17, 9, 0, 0, DateTimeKind.Local);

    private static KeepAwakeSession Running(double threshold = 60, double? timeout = null)
    {
        var session = new KeepAwakeSession(new YolkConfig(threshold: ThresholdRange.Fixed(threshold), timeout: timeout));
        session.Start(Now, monotonicNow: 0, randomUnit: 0);
        return session;
    }

    [Fact]
    public void StartingAnnouncesTheConfigAndBeginsARun()
    {
        var session = new KeepAwakeSession(YolkConfig.Default);

        var started = session.Start(Now, 0, 0);

        Assert.Equal(new YolkEvent.Started(YolkConfig.Default), started);
        Assert.Equal(new RunState(Now, null, 0, null), session.Run);
    }

    [Fact]
    public void StartingTwiceDoesNothingTheSecondTime()
    {
        var session = Running();
        Assert.Null(session.Start(Now.AddHours(1), 3600, 0));
        Assert.Equal(Now, session.Run!.StartedAt);
    }

    [Fact]
    public void StoppingEndsTheRunAndSaysWhy()
    {
        var session = Running();
        Assert.Equal(new YolkEvent.Stopped(StopReason.TimeoutReached), session.Stop(StopReason.TimeoutReached));
        Assert.False(session.IsRunning);
        Assert.Null(session.Stop(StopReason.UserRequested));
    }

    [Fact]
    public void TheDeadlineIsJudgedOnTheMonotonicClock()
    {
        var session = Running(timeout: 3600);
        Assert.False(session.IsPastDeadline(3599));
        Assert.True(session.IsPastDeadline(3600));
        Assert.Equal(Now.AddHours(1), session.Run!.EstimatedEnd);
    }

    [Fact]
    public void NoTimeoutMeansNoDeadline() => Assert.False(Running().IsPastDeadline(double.MaxValue));

    [Fact]
    public void APauseIsAnnouncedOnceAndTheResumeOnce()
    {
        var session = Running();

        Assert.Equal(new YolkEvent.Paused(PauseReason.ScreenLocked), session.ObserveConsole(ConsoleState.Locked));
        Assert.Null(session.ObserveConsole(ConsoleState.Locked));
        Assert.True(session.IsPaused);

        Assert.Equal(new YolkEvent.Resumed(), session.ObserveConsole(ConsoleState.Active));
        Assert.Null(session.ObserveConsole(ConsoleState.Active));
        Assert.False(session.IsPaused);
    }

    [Fact]
    public void ADifferentPauseReasonIsAnnouncedAsANewPause()
    {
        var session = Running();
        session.ObserveConsole(ConsoleState.Locked);
        Assert.Equal(new YolkEvent.Paused(PauseReason.SessionSwitchedOut), session.ObserveConsole(ConsoleState.SwitchedOut));
    }

    [Theory]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(600, true)]
    public void NudgesOnceIdleReachesTheThreshold(double idle, bool expected) =>
        Assert.Equal(expected, Running(threshold: 60).ShouldNudge(idle));

    [Fact]
    public void NeverNudgesWhilePausedOrStopped()
    {
        var paused = Running();
        paused.ObserveConsole(ConsoleState.Locked);
        Assert.False(paused.ShouldNudge(600));
        Assert.False(new KeepAwakeSession(YolkConfig.Default).ShouldNudge(600));
    }

    [Fact]
    public void ALandedNudgeIsCounted()
    {
        var session = Running();
        Assert.Equal(new YolkEvent.ActivitySimulated(1), session.RecordNudge(0.2, 0));
        Assert.Equal(new YolkEvent.ActivitySimulated(2), session.RecordNudge(0.1, 0));
        Assert.Equal(2, session.Run!.ActivityCount);
    }

    [Fact]
    public void AnIneffectiveNudgeWarnsOncePerRun()
    {
        var session = Running();
        Assert.Equal(new YolkEvent.PermissionMissing(), session.RecordNudge(90, 0));
        Assert.Null(session.RecordNudge(90, 0));
        Assert.Equal(0, session.Run!.ActivityCount);

        session.Stop(StopReason.UserRequested);
        session.Start(Now, 0, 0);
        Assert.Equal(new YolkEvent.PermissionMissing(), session.RecordNudge(90, 0));
    }

    [Fact]
    public void AnUnpostableNudgeWarnsOncePerRun()
    {
        var session = Running();
        Assert.Equal(new YolkEvent.PostFailed(), session.RecordPostFailed());
        Assert.Null(session.RecordPostFailed());
    }

    [Fact]
    public void ReconfiguringKeepsTheRunButRestartsAChangedCountdown()
    {
        var session = Running(timeout: 3600);
        session.RecordNudge(0.2, 0);

        var change = session.Reconfigure(
            new YolkConfig(threshold: ThresholdRange.Fixed(60), timeout: 7200), Now.AddMinutes(30), monotonicNow: 1800, randomUnit: 0);

        Assert.True(change.TimeoutChanged);
        Assert.True(change.TimersChanged);
        Assert.Equal(Now, session.Run!.StartedAt);
        Assert.Equal(1, session.Run.ActivityCount);
        Assert.Equal(Now.AddMinutes(30).AddHours(2), session.Run.EstimatedEnd);
        Assert.False(session.IsPastDeadline(3601));
        Assert.True(session.IsPastDeadline(9000));
    }

    [Fact]
    public void AnUnchangedTimeoutAndIntervalNeedNoNewTimers()
    {
        var session = Running();
        Assert.False(session.Reconfigure(new YolkConfig(threshold: ThresholdRange.Fixed(60)), Now, 0, 0).TimersChanged);
    }

    // Checks are timed to the nudge, so the worst case is the top of the range
    // plus timer slack: 60 + 5 = 65.
    [Theory]
    [InlineData(60.0, true)]
    [InlineData(65.0, true)]
    [InlineData(66.0, false)]
    [InlineData(null, false)]
    public void WarnsWhenTheDisplayWouldSleepBeforeYolkCouldAct(double? displaySleep, bool warns) =>
        Assert.Equal(warns, Running().CheckDisplaySleep(displaySleep) is not null);

    // ── Random nudge timing ────────────────────────────────────────────────

    private static KeepAwakeSession RunningRange(double randomUnit, double interval = 30)
    {
        var session = new KeepAwakeSession(new YolkConfig(interval, new ThresholdRange(30, 60)));
        session.Start(Now, 0, randomUnit);
        return session;
    }

    [Fact]
    public void EachNudgeWaitsForARandomPointInTheRange()
    {
        var session = RunningRange(randomUnit: 0.5);
        Assert.Equal(45, session.NudgeTarget);
        Assert.False(session.ShouldNudge(40));
        Assert.True(session.ShouldNudge(45));
    }

    [Fact]
    public void ANewPointIsChosenAfterEveryNudge()
    {
        var session = RunningRange(randomUnit: 0);
        session.RecordNudge(0.2, randomUnit: 0.9);
        Assert.Equal(57, session.NudgeTarget, precision: 9);
    }

    [Theory]
    [InlineData(40.0, 5.0)]    // 45 - 40
    [InlineData(2.0, 30.0)]    // capped at the interval
    [InlineData(44.9, 0.5)]    // never sooner than half a second
    [InlineData(90.0, 0.5)]
    public void TheNextCheckIsTimedForWhenIdleReachesThePoint(double idle, double delay) =>
        Assert.Equal(delay, RunningRange(randomUnit: 0.5).DelayUntilNudge(idle), precision: 9);

    [Fact]
    public void ChangingTheRangeChoosesANewPointInIt()
    {
        var session = RunningRange(randomUnit: 0.5);
        var change = session.Reconfigure(new YolkConfig(threshold: new ThresholdRange(100, 200)), Now, 0, randomUnit: 0.5);
        Assert.True(change.TimersChanged);
        Assert.Equal(150, session.NudgeTarget);
    }
}

public class ConsoleStateTests
{
    [Fact]
    public void OnlyActivePermitsSimulation()
    {
        Assert.False(ConsoleState.Active.PausesSimulation());
        Assert.True(ConsoleState.Locked.PausesSimulation());
        Assert.True(ConsoleState.SwitchedOut.PausesSimulation());
        Assert.True(ConsoleState.Unknown.PausesSimulation());
    }

    [Theory]
    [InlineData(ConsoleState.Locked, PauseReason.ScreenLocked)]
    [InlineData(ConsoleState.SwitchedOut, PauseReason.SessionSwitchedOut)]
    [InlineData(ConsoleState.Unknown, PauseReason.SessionUnknown)]
    public void EachPausingStateNamesItsReason(ConsoleState state, PauseReason reason) =>
        Assert.Equal(reason, state.ToPauseReason());
}
