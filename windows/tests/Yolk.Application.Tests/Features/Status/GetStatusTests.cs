using Yolk.Domain.Configuration;
using Yolk.Domain.Sessions;
using Yolk.TestSupport;

namespace Yolk.Application.Tests.Features.Status;

/// <summary>What the tray icon and menu show.</summary>
public class GetStatusTests
{
    [Fact]
    public void AnIdleYolkSaysSoAndOffersNoDetail()
    {
        using var host = new TestHost();
        var status = host.Status();
        Assert.False(status.IsActive);
        Assert.Equal("Yolk is idle", status.Title);
        Assert.Null(status.Detail);
        Assert.Null(status.StartedAt);
    }

    [Fact]
    public void TheAppStartsIdleWithoutHoldingThePCAwake()
    {
        using var host = new TestHost();
        host.ApplyStoredSettings();
        Assert.False(host.Status().IsActive);
        Assert.Empty(host.System.Acquired);
    }

    [Fact]
    public void AnActiveYolkReportsUptimeAndNudgeCount()
    {
        using var host = new TestHost();
        host.System.Idle = 90;
        host.Start();
        host.Tick();

        var status = host.Status(host.System.WallClock.AddMinutes(134));

        Assert.Equal("Yolk is active", status.Title);
        Assert.Equal("Awake 2h 14m · 1 nudge", status.Detail);
    }

    [Theory]
    [InlineData(0, "Awake 0m")]
    [InlineData(59, "Awake 0m")]
    [InlineData(60 * 59, "Awake 59m")]
    [InlineData(60 * 60, "Awake 1h 0m")]
    [InlineData(60 * 134, "Awake 2h 14m")]
    public void UptimeReadsAsHoursAndMinutes(int seconds, string expected)
    {
        using var host = new TestHost();
        host.Start();
        Assert.StartsWith(expected + " ·", host.Status(host.System.WallClock.AddSeconds(seconds)).Detail);
    }

    [Theory]
    [InlineData(0, "no nudges")]
    [InlineData(1, "1 nudge")]
    [InlineData(3, "3 nudges")]
    public void TheNudgeCountIsPluralised(int ticks, string expected)
    {
        using var host = new TestHost();
        host.System.Idle = 90;
        host.Start();
        for (var i = 0; i < ticks; i++)
        {
            host.Tick();
        }
        Assert.EndsWith(expected, host.Status().Detail);
    }

    [Theory]
    [InlineData(ConsoleState.Locked, "Paused — screen locked")]
    [InlineData(ConsoleState.SwitchedOut, "Paused — another user is using this PC")]
    [InlineData(ConsoleState.Unknown, "Paused — session state unavailable")]
    public void APauseNamesItsReasonAndStillReportsUptime(ConsoleState state, string expected)
    {
        using var host = new TestHost();
        host.System.Console = state;
        host.Start();
        host.Tick();

        var status = host.Status(host.System.WallClock.AddMinutes(5));

        Assert.True(status.IsActive);
        Assert.Equal(expected, status.Title);
        Assert.NotNull(status.Detail);
    }

    [Fact]
    public void GoingIdleClearsTheSessionStatistics()
    {
        using var host = new TestHost();
        host.System.Idle = 90;
        host.Start(new YolkConfig(timeout: 600));
        host.Tick();

        host.Stop();

        var status = host.Status();
        Assert.Equal(0, status.ActivityCount);
        Assert.Null(status.StartedAt);
        Assert.Null(status.EstimatedEnd);
        Assert.Null(status.PauseReason);
    }

    [Fact]
    public void TheDisplaySleepWarningAppearsOnlyWhenSleepBeatsYolkToIt()
    {
        using var host = new TestHost();
        host.System.DisplaySleep = 60;
        Assert.Equal(60, host.Status().DisplaySleepWarning);

        host.System.DisplaySleep = 1800;
        Assert.Null(host.Status().DisplaySleepWarning);

        host.System.DisplaySleep = null;
        Assert.Null(host.Status().DisplaySleepWarning);
    }

    [Fact]
    public void TighteningTheSettingsClearsTheDisplaySleepWarning()
    {
        using var host = new TestHost();
        host.System.DisplaySleep = 60; // top of 30–60, + 5 = 65 ≥ 60, so the defaults warn
        Assert.NotNull(host.Status().DisplaySleepWarning);

        host.UpdateSettings(thresholdMax: 50); // 55 < 60

        Assert.Null(host.Status().DisplaySleepWarning);
    }
}
