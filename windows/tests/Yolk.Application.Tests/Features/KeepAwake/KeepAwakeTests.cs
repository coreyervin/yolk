using Yolk.Application.Abstractions;
using Yolk.Application.Features.KeepAwake;
using Yolk.Domain.Configuration;
using Yolk.Domain.Sessions;
using Yolk.TestSupport;

namespace Yolk.Application.Tests.Features.KeepAwake;

/// <summary>Start, Stop, and Reconfigure — the session's lifecycle.</summary>
public class KeepAwakeTests
{
    // ── Start ──────────────────────────────────────────────────────────────

    [Fact]
    public void StartingHoldsAPowerRequestAndAnnouncesItself()
    {
        using var host = new TestHost();

        var result = host.Start();

        Assert.IsType<StartKeepAwakeResult.Started>(result);
        Assert.True(host.Status().IsActive);
        Assert.Equal(["yolk: keeping the PC awake while agents run"], host.System.Acquired);
        Assert.Equal(new YolkEvent.Started(YolkConfig.Default), host.Events.Events[0]);
    }

    [Fact]
    public void StartingWithAConfigRunsWithIt()
    {
        using var host = new TestHost();
        var config = new YolkConfig(interval: 15, threshold: ThresholdRange.Fixed(30), timeout: 600);

        host.Start(config);

        Assert.Equal(new YolkEvent.Started(config), host.Events.Events[0]);
    }

    [Fact]
    public void StartingTwiceIsIdempotent()
    {
        using var host = new TestHost();
        host.Start();

        Assert.IsType<StartKeepAwakeResult.AlreadyRunning>(host.Start());
        Assert.Single(host.System.Acquired);
    }

    [Fact]
    public void ARefusedPowerRequestLeavesEverythingStopped()
    {
        using var host = new TestHost();
        host.System.PowerRefusal = new PowerRequestRefusedException(5);

        var result = host.Start();

        Assert.Equal(new StartKeepAwakeResult.PowerRequestRefused(5), result);
        Assert.False(host.Status().IsActive);
        Assert.Empty(host.Events.Events);
        Assert.Empty(host.Scheduler.Entries);
    }

    [Fact]
    public void StartSchedulesTheFirstCheckASecondLater()
    {
        using var host = new TestHost();

        host.Start(new YolkConfig(interval: 15));

        var ticker = Assert.Single(host.Scheduler.Live);
        Assert.Equal(TimeSpan.FromSeconds(1), ticker.DueTime);
        Assert.Null(ticker.Period);
    }

    [Fact]
    public void TheTickerRunsTheActivityCheck()
    {
        using var host = new TestHost();
        host.System.Idle = 90;
        host.Start();

        Assert.Single(host.Scheduler.Live).Fire();

        Assert.Equal(1, host.System.PostCount);
    }

    [Fact]
    public void WarnsAtStartWhenTheDisplayWouldSleepBeforeYolkCouldAct()
    {
        using var host = new TestHost();
        host.System.DisplaySleep = 60; // threshold 60 + interval 30 + 5 exceeds this

        host.Start();

        Assert.Equal(1, host.Events.Count(new YolkEvent.DisplaySleepTooSoon(60)));
    }

    [Fact]
    public void StaysQuietWhenDisplaySleepIsComfortablyLonger()
    {
        using var host = new TestHost();
        host.System.DisplaySleep = 1800;

        host.Start();

        Assert.DoesNotContain(host.Events.Events, e => e is YolkEvent.DisplaySleepTooSoon);
    }

    // ── Stop ───────────────────────────────────────────────────────────────

    [Fact]
    public void StoppingReleasesThePowerRequestAndReportsWhy()
    {
        using var host = new TestHost();
        host.Start();

        host.Stop();

        Assert.False(host.Status().IsActive);
        Assert.Single(host.System.Released);
        Assert.Equal(new YolkEvent.Stopped(StopReason.UserRequested), host.Events.Events[^1]);
    }

    [Fact]
    public void StoppingCancelsEveryTimer()
    {
        using var host = new TestHost();
        host.Start(new YolkConfig(timeout: 60));

        host.Stop();

        Assert.Empty(host.Scheduler.Live);
    }

    [Fact]
    public void StoppingWhenIdleDoesNothing()
    {
        using var host = new TestHost();
        host.Stop();
        Assert.Empty(host.Events.Events);
        Assert.Empty(host.System.Released);
    }

    [Fact]
    public void TheDeadlineTimerStopsTheSessionAsATimeout()
    {
        using var host = new TestHost();
        host.Start(new YolkConfig(timeout: 60));

        var deadline = Assert.Single(host.Scheduler.Live, e => e.DueTime == TimeSpan.FromSeconds(60));
        deadline.Fire();

        Assert.False(host.Status().IsActive);
        Assert.Single(host.System.Released);
        Assert.Equal(new YolkEvent.Stopped(StopReason.TimeoutReached), host.Events.Events[^1]);
    }

    // ── Reconfigure ────────────────────────────────────────────────────────

    [Fact]
    public void ReconfiguringKeepsTheStartTimeAndNudgeCount()
    {
        using var host = new TestHost();
        host.System.Idle = 90;
        host.Start();
        host.Tick();
        var before = host.Status();

        host.Reconfigure(new YolkConfig(interval: 15, threshold: ThresholdRange.Fixed(45)));

        var after = host.Status();
        Assert.Equal(before.StartedAt, after.StartedAt);
        Assert.Equal(1, after.ActivityCount);
    }

    [Fact]
    public void AChangedIntervalTakesEffectFromTheNextCheck()
    {
        using var host = new TestHost();
        host.Start();

        host.Reconfigure(new YolkConfig(interval: 10));
        Assert.Single(host.Scheduler.Live).Fire();

        Assert.Equal(TimeSpan.FromSeconds(10), Assert.Single(host.Scheduler.Live).DueTime);
    }

    [Fact]
    public void AChangedTimeoutRestartsTheCountdownFromNow()
    {
        using var host = new TestHost();
        host.Start(new YolkConfig(timeout: 3600));

        host.System.Monotonic = 1800;
        host.Reconfigure(new YolkConfig(timeout: 7200));

        // The original deadline was 3600; it must no longer apply.
        host.System.Monotonic = 3601;
        host.Tick();
        Assert.True(host.Status().IsActive);

        // The new deadline is 1800 + 7200.
        host.System.Monotonic = 9000;
        host.Tick();
        Assert.False(host.Status().IsActive);
    }

    [Fact]
    public void ReconfiguringToNoTimeoutCancelsTheDeadlineAndKeepsRunning()
    {
        using var host = new TestHost();
        host.Start(new YolkConfig(timeout: 60));

        host.Reconfigure(new YolkConfig(timeout: null));
        host.System.Monotonic = 100_000;
        host.Tick();

        Assert.True(host.Status().IsActive);
        Assert.DoesNotContain(host.Scheduler.Live, e => e.DueTime == TimeSpan.FromSeconds(60));
        Assert.Null(host.Status().EstimatedEnd);
    }

    [Fact]
    public void ReconfiguringWhileStoppedJustKeepsTheSettingsForNextTime()
    {
        using var host = new TestHost();

        host.Reconfigure(new YolkConfig(interval: 90));
        Assert.Empty(host.Scheduler.Entries);

        host.Start();
        Assert.Equal(new YolkEvent.Started(new YolkConfig(interval: 90)), host.Events.Events[0]);
    }
}
