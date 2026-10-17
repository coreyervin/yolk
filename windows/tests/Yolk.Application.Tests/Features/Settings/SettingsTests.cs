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
        Assert.Equal(new ThresholdRange(30, 60), settings.Threshold);
        Assert.Equal(StopAfter.Never, settings.StopAfter);
    }

    [Fact]
    public void SettingsSurviveARelaunch()
    {
        var repository = new FakeSettingsRepository();
        using (var first = new TestHost())
        {
            first.UpdateSettings(interval: 15, thresholdMin: 90, thresholdMax: 120, stopAfter: StopAfter.Hours4);
            repository.Stored = first.Settings.Stored;
        }

        using var second = new TestHost();
        second.Settings.Stored = repository.Stored;
        var settings = second.GetSettings();

        Assert.Equal(15, settings.Interval);
        Assert.Equal(new ThresholdRange(90, 120), settings.Threshold);
        Assert.Equal(StopAfter.Hours4, settings.StopAfter);
    }

    [Theory]
    [InlineData(4.0)]
    [InlineData(500.0)]
    [InlineData(double.NaN)]
    public void AnOutOfRangeStoredIntervalFallsBackToTheDefault(double interval)
    {
        using var host = new TestHost();
        host.Settings.Stored = new StoredSettings(interval, null, null, null, null);
        Assert.Equal(30, host.GetSettings().Interval);
    }

    [Theory]
    [InlineData(60.0, 30.0)]   // inverted
    [InlineData(1.0, 60.0)]    // bottom out of bounds
    [InlineData(30.0, 900.0)]  // top out of bounds
    [InlineData(double.NaN, 60.0)]
    public void ABadStoredRangeFallsBackToTheDefault(double low, double high)
    {
        using var host = new TestHost();
        host.Settings.Stored = new StoredSettings(null, low, high, null, null);
        Assert.Equal(new ThresholdRange(30, 60), host.GetSettings().Threshold);
    }

    /// <summary>
    /// Before thresholds were ranges, one number was stored. An upgrade keeps
    /// it, as a fixed point.
    /// </summary>
    [Fact]
    public void APreRangeStoredThresholdCarriesOverAsAFixedPoint()
    {
        using var host = new TestHost();
        host.Settings.Stored = new StoredSettings(null, null, null, LegacyThreshold: 90, null);
        Assert.Equal(ThresholdRange.Fixed(90), host.GetSettings().Threshold);
    }

    [Fact]
    public void AStoredRangeWinsOverAPreRangeValue()
    {
        using var host = new TestHost();
        host.Settings.Stored = new StoredSettings(null, 20, 40, LegacyThreshold: 90, null);
        Assert.Equal(new ThresholdRange(20, 40), host.GetSettings().Threshold);
    }

    [Fact]
    public void MovingOneEndOfTheRangePastTheOtherCarriesItAlong()
    {
        using var host = new TestHost();
        Assert.Equal(ThresholdRange.Fixed(90), host.UpdateSettings(thresholdMin: 90).Threshold);
        Assert.Equal(ThresholdRange.Fixed(20), host.UpdateSettings(thresholdMax: 20).Threshold);
    }

    [Fact]
    public void AnUnrecognisedStoredStopAfterFallsBackToNever()
    {
        using var host = new TestHost();
        host.Settings.Stored = new StoredSettings(null, null, null, null, StopAfter: null);
        Assert.Equal(StopAfter.Never, host.GetSettings().StopAfter);
    }

    [Fact]
    public void ApplyingStoredSettingsConfiguresTheNextSession()
    {
        using var host = new TestHost();
        host.Settings.Stored = new StoredSettings(15, 20, 45, null, StopAfter.Hour1);

        host.ApplyStoredSettings();
        host.Start();

        Assert.Equal(new YolkEvent.Started(new YolkConfig(15, new ThresholdRange(20, 45), 3600)), host.Events.Events[0]);
    }

    [Fact]
    public void AnOutOfRangeChangeIsClampedIntoRange()
    {
        using var host = new TestHost();
        var settings = host.UpdateSettings(interval: 1, thresholdMin: 0, thresholdMax: 9999);
        Assert.Equal(5, settings.Interval);
        Assert.Equal(new ThresholdRange(5, 300), settings.Threshold);
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
        Assert.Single(host.Scheduler.Live).Fire();
        Assert.Equal(TimeSpan.FromSeconds(15), Assert.Single(host.Scheduler.Live).DueTime);
    }

    [Fact]
    public void ChangingThresholdMidSessionTakesEffectImmediately()
    {
        using var host = new TestHost();
        host.UpdateSettings(thresholdMin: 60);
        host.System.Idle = 45;
        host.Start();
        host.Tick();
        Assert.Equal(0, host.Status().ActivityCount);

        host.UpdateSettings(thresholdMax: 30);
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
