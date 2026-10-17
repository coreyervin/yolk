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
        var session = new KeepAwakeSession(new YolkConfig(threshold: threshold, timeout: timeout));
        session.Start(Now, monotonicNow: 0);
        return session;
    }

    [Fact]
    public void StartingAnnouncesTheConfigAndBeginsARun()
    {
        var session = new KeepAwakeSession(YolkConfig.Default);

        var started = session.Start(Now, 0);

        Assert.Equal(new YolkEvent.Started(YolkConfig.Default), started);
        Assert.Equal(new RunState(Now, null, 0, null), session.Run);
    }

    [Fact]
    public void StartingTwiceDoesNothingTheSecondTime()
    {
        var session = Running();
        Assert.Null(session.Start(Now.AddHours(1), 3600));
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
        Assert.Equal(new YolkEvent.ActivitySimulated(1), session.RecordNudge(0.2));
        Assert.Equal(new YolkEvent.ActivitySimulated(2), session.RecordNudge(0.1));
        Assert.Equal(2, session.Run!.ActivityCount);
    }

    [Fact]
    public void AnIneffectiveNudgeWarnsOncePerRun()
    {
        var session = Running();
        Assert.Equal(new YolkEvent.PermissionMissing(), session.RecordNudge(90));
        Assert.Null(session.RecordNudge(90));
        Assert.Equal(0, session.Run!.ActivityCount);

        session.Stop(StopReason.UserRequested);
        session.Start(Now, 0);
        Assert.Equal(new YolkEvent.PermissionMissing(), session.RecordNudge(90));
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
        session.RecordNudge(0.2);

        var change = session.Reconfigure(new YolkConfig(timeout: 7200), Now.AddMinutes(30), monotonicNow: 1800);

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
        Assert.False(session.Reconfigure(new YolkConfig(threshold: 45), Now, 0).TimersChanged);
    }

    [Theory]
    [InlineData(60.0, true)]   // 60 + 30 + 5 = 95 ≥ 60
    [InlineData(95.0, true)]
    [InlineData(96.0, false)]
    [InlineData(null, false)]
    public void WarnsWhenTheDisplayWouldSleepBeforeYolkCouldAct(double? displaySleep, bool warns) =>
        Assert.Equal(warns, Running().CheckDisplaySleep(displaySleep) is not null);
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
