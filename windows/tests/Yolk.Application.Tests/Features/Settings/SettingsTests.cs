using Yolk.Application.Abstractions;
using Yolk.Domain.Configuration;
using Yolk.Domain.Sessions;
using Yolk.TestSupport;

namespace Yolk.Application.Tests.Features.Settings;

/// <summary>GetSettings, ApplyStoredSettings, and UpdateSettings.</summary>
public class SettingsTests
{
    [Fact]
    public void AnEmptyStoreYieldsTheDocumentedDefaults()
    {
        using var host = new TestHost();
        var settings = host.GetSettings();
        Assert.Equal(30, settings.Interval);
        Assert.Equal(60, settings.Threshold);
        Assert.Equal(StopAfter.Never, settings.StopAfter);
    }

    [Fact]
    public void SettingsSurviveARelaunch()
    {
        var repository = new FakeSettingsRepository();
        using (var first = new TestHost())
        {
            first.UpdateSettings(interval: 15, threshold: 120, stopAfter: StopAfter.Hours4);
            repository.Stored = first.Settings.Stored;
        }

        using var second = new TestHost();
        second.Settings.Stored = repository.Stored;
        var settings = second.GetSettings();

        Assert.Equal(15, settings.Interval);
        Assert.Equal(120, settings.Threshold);
        Assert.Equal(StopAfter.Hours4, settings.StopAfter);
    }

    [Theory]
    [InlineData(4.0, 60.0)]
    [InlineData(500.0, 60.0)]
    [InlineData(30.0, 1.0)]
    [InlineData(30.0, 301.0)]
    [InlineData(double.NaN, 60.0)]
    public void OutOfRangeStoredSettingsFallBackToTheDefaults(double interval, double threshold)
    {
        using var host = new TestHost();
        host.Settings.Stored = new StoredSettings(interval, threshold, null);

        var settings = host.GetSettings();

        Assert.True(YolkConfig.IsValidInterval(settings.Interval));
        Assert.True(YolkConfig.IsValidThreshold(settings.Threshold));
        Assert.Equal(YolkConfig.IsValidInterval(interval) ? interval : 30, settings.Interval);
        Assert.Equal(YolkConfig.IsValidThreshold(threshold) ? threshold : 60, settings.Threshold);
    }

    [Fact]
    public void AnUnrecognisedStoredStopAfterFallsBackToNever()
    {
        using var host = new TestHost();
        host.Settings.Stored = new StoredSettings(null, null, StopAfter: null);
        Assert.Equal(StopAfter.Never, host.GetSettings().StopAfter);
    }

    [Fact]
    public void ApplyingStoredSettingsConfiguresTheNextSession()
    {
        using var host = new TestHost();
        host.Settings.Stored = new StoredSettings(15, 45, StopAfter.Hour1);

        host.ApplyStoredSettings();
        host.Start();

        Assert.Equal(new YolkEvent.Started(new YolkConfig(15, 45, 3600)), host.Events.Events[0]);
    }

    [Fact]
    public void AnOutOfRangeChangeIsClampedIntoRange()
    {
        using var host = new TestHost();
        var settings = host.UpdateSettings(interval: 1, threshold: 9999);
        Assert.Equal(5, settings.Interval);
        Assert.Equal(300, settings.Threshold);
    }

    [Fact]
    public void ANaNChangeIsIgnored()
    {
        using var host = new TestHost();
        Assert.Equal(30, host.UpdateSettings(interval: double.NaN).Interval);
    }

    [Fact]
    public void ANoOpChangeIsNotSaved()
    {
        using var host = new TestHost();
        host.UpdateSettings(interval: 30);
        Assert.Equal(0, host.Settings.SaveCount);
    }

    [Fact]
    public void ChangingIntervalMidSessionPreservesTheStatistics()
    {
        using var host = new TestHost();
        host.System.Idle = 90;
        host.Start();
        host.Tick();
        var startedAt = host.Status().StartedAt;

        host.UpdateSettings(interval: 15);

        var status = host.Status();
        Assert.True(status.IsActive);
        Assert.Equal(1, status.ActivityCount);
        Assert.Equal(startedAt, status.StartedAt);
        Assert.Equal(TimeSpan.FromSeconds(15), Assert.Single(host.Scheduler.Live).Period);
    }

    [Fact]
    public void ChangingThresholdMidSessionTakesEffectImmediately()
    {
        using var host = new TestHost();
        host.System.Idle = 45;
        host.Start();
        host.Tick();
        Assert.Equal(0, host.Status().ActivityCount);

        host.UpdateSettings(threshold: 30);
        host.Tick();

        Assert.Equal(1, host.Status().ActivityCount);
    }

    [Fact]
    public void ChangingStopAfterMidSessionRestartsTheCountdownFromNow()
    {
        using var host = new TestHost();
        host.Start();

        host.System.WallClock = host.System.WallClock.AddHours(2);
        host.UpdateSettings(stopAfter: StopAfter.Hour1);

        Assert.Equal(host.System.WallClock.AddHours(1), host.Status().EstimatedEnd);
    }

    [Fact]
    public void SelectingNeverMidSessionCancelsTheDeadlineAndKeepsRunning()
    {
        using var host = new TestHost();
        host.UpdateSettings(stopAfter: StopAfter.Minutes30);
        host.Start();

        host.UpdateSettings(stopAfter: StopAfter.Never);
        host.System.Monotonic = 100_000;
        host.Tick();

        Assert.True(host.Status().IsActive);
        Assert.Null(host.Status().EstimatedEnd);
    }

    [Fact]
    public void AStopAfterThatFiresReturnsToIdle()
    {
        using var host = new TestHost();
        host.UpdateSettings(stopAfter: StopAfter.Minutes30);
        host.Start();

        host.System.Monotonic = 1800;
        host.Tick();

        Assert.False(host.Status().IsActive);
    }
}
