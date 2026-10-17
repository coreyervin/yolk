using Yolk.AppCore;
using Yolk.Core;
using Yolk.TestSupport;

namespace Yolk.AppCore.Tests;

internal sealed class FakeLoginItems : ILoginItemService
{
    public bool Enabled { get; set; }
    public bool Registrable { get; set; } = true;
    public Exception? RegisterFailure { get; set; }
    public Exception? UnregisterFailure { get; set; }
    public int RegisterCalls { get; private set; }
    public int UnregisterCalls { get; private set; }

    public bool IsEnabled() => Enabled;

    public void Register()
    {
        RegisterCalls++;
        if (RegisterFailure is { } failure)
        {
            throw failure;
        }
        Enabled = true;
    }

    public void Unregister()
    {
        UnregisterCalls++;
        if (UnregisterFailure is { } failure)
        {
            throw failure;
        }
        Enabled = false;
    }

    public bool CanRegister() => Registrable;
}

/// <summary>A port of the Mac's AppModelTests.</summary>
public class AppModelTests
{
    private static AppModel Make(
        FakeSystem? system = null, ISettingsStore? store = null, FakeLoginItems? loginItems = null) =>
        new(system ?? new FakeSystem(), new ManualScheduler(), store ?? new InMemorySettingsStore(),
            loginItems ?? new FakeLoginItems());

    // ── Lifecycle ──────────────────────────────────────────────────────────

    /// <summary>
    /// "Launch at sign-in" means "have Yolk available", not "be awake from
    /// boot" — opening the app must not start nudging on its own.
    /// </summary>
    [Fact]
    public void TheAppStartsIdle()
    {
        var system = new FakeSystem();
        var model = Make(system);
        Assert.False(model.IsActive);
        Assert.Empty(system.Created);
    }

    [Fact]
    public void ActivatingHoldsAPowerRequest()
    {
        var system = new FakeSystem();
        var model = Make(system);
        model.SetActive(true);
        Assert.True(model.IsActive);
        Assert.Single(system.Created);
    }

    [Fact]
    public void DeactivatingReleasesThePowerRequest()
    {
        var system = new FakeSystem();
        var model = Make(system);
        model.SetActive(true);
        model.SetActive(false);
        Assert.False(model.IsActive);
        Assert.Single(system.Released);
    }

    [Fact]
    public void ActivatingTwiceIsIdempotent()
    {
        var system = new FakeSystem();
        var model = Make(system);
        model.SetActive(true);
        model.SetActive(true);
        Assert.Single(system.Created);
    }

    [Fact]
    public void ARefusedPowerRequestLeavesTheAppIdle()
    {
        var system = new FakeSystem { AssertionFailure = new AssertionCreationException(5) };
        var model = Make(system);
        model.SetActive(true);
        Assert.False(model.IsActive);
    }

    [Fact]
    public void ToggleFlipsWhicheverWayItIsCurrentlyPointing()
    {
        var model = Make();
        model.Toggle();
        Assert.True(model.IsActive);
        model.Toggle();
        Assert.False(model.IsActive);
    }

    [Fact]
    public void ATimeoutThatFiresReturnsTheAppToIdle()
    {
        var system = new FakeSystem();
        var model = Make(system);
        model.StopAfter = StopAfter.Minutes30;
        model.SetActive(true);

        system.Monotonic = 1800;
        model.TickForTesting();

        Assert.False(model.IsActive);
    }

    [Fact]
    public void TheNudgeCountFollowsTheSession()
    {
        var system = new FakeSystem { Idle = 90 };
        var model = Make(system);
        model.SetActive(true);
        model.TickForTesting();
        model.TickForTesting();
        Assert.Equal(2, model.ActivityCount);
    }

    [Fact]
    public void LockingTheScreenSurfacesAPauseReason()
    {
        var system = new FakeSystem { Console = ConsoleState.Locked };
        var model = Make(system);
        model.SetActive(true);
        model.TickForTesting();
        Assert.Equal(PauseReason.ScreenLocked, model.PauseReason);
        Assert.True(model.IsActive);
    }

    [Fact]
    public void UnlockingClearsThePauseReason()
    {
        var system = new FakeSystem { Console = ConsoleState.Locked };
        var model = Make(system);
        model.SetActive(true);
        model.TickForTesting();

        system.Console = ConsoleState.Active;
        model.TickForTesting();

        Assert.Null(model.PauseReason);
    }

    [Fact]
    public void GoingIdleClearsTheSessionStatistics()
    {
        var system = new FakeSystem { Idle = 90 };
        var model = Make(system);
        model.SetActive(true);
        model.TickForTesting();

        model.SetActive(false);

        Assert.Equal(0, model.ActivityCount);
        Assert.Null(model.StartedAt);
        Assert.Null(model.EstimatedEnd);
        Assert.Null(model.PauseReason);
    }

    [Fact]
    public void StateChangesAreAnnounced()
    {
        var model = Make();
        var changed = new List<string?>();
        model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        model.SetActive(true);

        Assert.Contains(nameof(AppModel.IsActive), changed);
        Assert.Contains(nameof(AppModel.StartedAt), changed);
    }

    // ── Uptime ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "0m")]
    [InlineData(59, "0m")]
    [InlineData(60 * 59, "59m")]
    [InlineData(60 * 60, "1h 0m")]
    [InlineData(60 * 134, "2h 14m")]
    public void UptimeReadsAsHoursAndMinutes(int seconds, string expected)
    {
        var system = new FakeSystem();
        var model = Make(system);
        model.SetActive(true);
        Assert.Equal(expected, model.UptimeDescription(system.WallClock.AddSeconds(seconds)));
    }

    [Fact]
    public void UptimeIsNullWhileIdle() => Assert.Null(Make().UptimeDescription(DateTime.Now));

    // ── Settings ───────────────────────────────────────────────────────────

    [Fact]
    public void AnEmptyStoreYieldsTheDocumentedDefaults()
    {
        var model = Make();
        Assert.Equal(30, model.Interval);
        Assert.Equal(60, model.Threshold);
        Assert.Equal(StopAfter.Never, model.StopAfter);
    }

    [Fact]
    public void SettingsSurviveARelaunch()
    {
        var store = new InMemorySettingsStore();
        var first = Make(store: store);
        first.Interval = 15;
        first.Threshold = 120;
        first.StopAfter = StopAfter.Hours4;

        var second = Make(store: store);

        Assert.Equal(15, second.Interval);
        Assert.Equal(120, second.Threshold);
        Assert.Equal(StopAfter.Hours4, second.StopAfter);
    }

    /// <summary>The same strings the Mac app stores, so the two read alike.</summary>
    [Fact]
    public void StopAfterIsPersistedByItsMacName()
    {
        var store = new InMemorySettingsStore();
        Make(store: store).StopAfter = StopAfter.Hour1;
        Assert.Equal("hour1", store.ReadString("stopAfter"));
    }

    [Theory]
    [InlineData("interval", 4.0)]
    [InlineData("interval", 500.0)]
    [InlineData("threshold", 1.0)]
    [InlineData("threshold", 301.0)]
    [InlineData("interval", double.NaN)]
    public void OutOfRangeStoredSettingsFallBackToTheDefaults(string key, double value)
    {
        var store = new InMemorySettingsStore();
        store.WriteDouble(key, value);
        var model = Make(store: store);
        Assert.Equal(30, model.Interval);
        Assert.Equal(60, model.Threshold);
    }

    [Fact]
    public void AnUnrecognisedStoredStopAfterFallsBackToNever()
    {
        var store = new InMemorySettingsStore();
        store.WriteString("stopAfter", "fortnight");
        Assert.Equal(StopAfter.Never, Make(store: store).StopAfter);
    }

    [Fact]
    public void AssigningAnOutOfRangeSettingClampsItIntoRange()
    {
        var model = Make();
        model.Interval = 1;
        model.Threshold = 9999;
        Assert.Equal(5, model.Interval);
        Assert.Equal(300, model.Threshold);
    }

    [Fact]
    public void ChangingIntervalMidSessionPreservesTheStatistics()
    {
        var system = new FakeSystem { Idle = 90 };
        var model = Make(system);
        model.SetActive(true);
        model.TickForTesting();
        var startedAt = model.StartedAt;

        model.Interval = 15;

        Assert.True(model.IsActive);
        Assert.Equal(1, model.ActivityCount);
        Assert.Equal(startedAt, model.StartedAt);
    }

    [Fact]
    public void ChangingThresholdMidSessionTakesEffectImmediately()
    {
        var system = new FakeSystem { Idle = 45 };
        var model = Make(system);
        model.SetActive(true);
        model.TickForTesting();
        Assert.Equal(0, model.ActivityCount);

        model.Threshold = 30;
        model.TickForTesting();

        Assert.Equal(1, model.ActivityCount);
    }

    [Theory]
    [InlineData(StopAfter.Never, null)]
    [InlineData(StopAfter.Minutes30, 1800.0)]
    [InlineData(StopAfter.Hour1, 3600.0)]
    [InlineData(StopAfter.Hours4, 14400.0)]
    [InlineData(StopAfter.Hours8, 28800.0)]
    public void StopAfterMapsToTheDocumentedDurations(StopAfter option, double? seconds) =>
        Assert.Equal(seconds, option.Timeout());

    [Fact]
    public void EveryStopAfterOptionIsOfferedNeverFirst()
    {
        var options = Enum.GetValues<StopAfter>();
        Assert.Equal(5, options.Length);
        Assert.Equal(StopAfter.Never, options[0]);
    }

    [Fact]
    public void ChangingStopAfterMidSessionRestartsTheCountdownFromNow()
    {
        var system = new FakeSystem();
        var model = Make(system);
        model.SetActive(true);

        system.WallClock = system.WallClock.AddHours(2);
        model.StopAfter = StopAfter.Hour1;

        Assert.Equal(system.WallClock.AddHours(1), model.EstimatedEnd);
    }

    [Fact]
    public void SelectingNeverMidSessionCancelsTheDeadlineAndKeepsRunning()
    {
        var system = new FakeSystem();
        var model = Make(system);
        model.StopAfter = StopAfter.Minutes30;
        model.SetActive(true);

        model.StopAfter = StopAfter.Never;
        system.Monotonic = 100_000;
        model.TickForTesting();

        Assert.True(model.IsActive);
        Assert.Null(model.EstimatedEnd);
    }

    // ── System state ───────────────────────────────────────────────────────

    [Fact]
    public void TheDisplaySleepWarningAppearsOnlyWhenSleepBeatsYolkToIt()
    {
        Assert.Equal(60, Make(new FakeSystem { DisplaySleep = 60 }).DisplaySleepWarning);
        Assert.Null(Make(new FakeSystem { DisplaySleep = 1800 }).DisplaySleepWarning);
    }

    [Fact]
    public void NoDisplaySleepSettingMeansNoWarning() =>
        Assert.Null(Make(new FakeSystem { DisplaySleep = null }).DisplaySleepWarning);

    [Fact]
    public void TighteningTheSettingsClearsTheDisplaySleepWarning()
    {
        // 60 + 30 + 5 = 95 >= 90, so the defaults warn.
        var model = Make(new FakeSystem { DisplaySleep = 90 });
        Assert.NotNull(model.DisplaySleepWarning);

        model.Threshold = 30;

        Assert.Null(model.DisplaySleepWarning);
    }

    // ── Menu text ──────────────────────────────────────────────────────────

    [Fact]
    public void AnIdleYolkSaysSoAndOffersNoDetail()
    {
        var model = Make();
        Assert.Equal("Yolk is idle", model.StatusTitle);
        Assert.Null(model.StatusDetail(DateTime.Now));
    }

    [Fact]
    public void AnActiveYolkReportsUptimeAndNudgeCount()
    {
        var system = new FakeSystem { Idle = 90 };
        var model = Make(system);
        model.SetActive(true);
        model.TickForTesting();

        Assert.Equal("Yolk is active", model.StatusTitle);
        Assert.Equal("Awake 2h 14m · 1 nudge", model.StatusDetail(system.WallClock.AddMinutes(134)));
    }

    [Theory]
    [InlineData(0, "no nudges")]
    [InlineData(1, "1 nudge")]
    [InlineData(3, "3 nudges")]
    public void TheNudgeCountIsPluralised(int ticks, string expected)
    {
        var system = new FakeSystem { Idle = 90 };
        var model = Make(system);
        model.SetActive(true);
        for (var i = 0; i < ticks; i++)
        {
            model.TickForTesting();
        }
        Assert.EndsWith(expected, model.StatusDetail(system.WallClock));
    }

    [Theory]
    [InlineData(ConsoleState.Locked, "Paused — screen locked")]
    [InlineData(ConsoleState.SwitchedOut, "Paused — another user is using this PC")]
    [InlineData(ConsoleState.Unknown, "Paused — session state unavailable")]
    public void APauseNamesItsReason(ConsoleState state, string expected)
    {
        var system = new FakeSystem { Console = state };
        var model = Make(system);
        model.SetActive(true);
        model.TickForTesting();
        Assert.Equal(expected, model.StatusTitle);
    }

    [Fact]
    public void APausedYolkStillReportsItsUptime()
    {
        var system = new FakeSystem { Console = ConsoleState.Locked };
        var model = Make(system);
        model.SetActive(true);
        model.TickForTesting();
        Assert.NotNull(model.StatusDetail(system.WallClock.AddMinutes(5)));
    }

    // ── Launch at sign-in ──────────────────────────────────────────────────

    [Fact]
    public void TheCurrentRegistrationStateIsReadFromTheSystemNotStored()
    {
        Assert.True(Make(loginItems: new FakeLoginItems { Enabled = true }).LaunchAtLogin);
        Assert.False(Make(loginItems: new FakeLoginItems { Enabled = false }).LaunchAtLogin);
    }

    [Fact]
    public void EnablingRegistersTheApp()
    {
        var loginItems = new FakeLoginItems();
        var model = Make(loginItems: loginItems);
        model.LaunchAtLogin = true;
        Assert.Equal(1, loginItems.RegisterCalls);
        Assert.True(model.LaunchAtLogin);
    }

    [Fact]
    public void DisablingUnregistersTheApp()
    {
        var loginItems = new FakeLoginItems { Enabled = true };
        var model = Make(loginItems: loginItems);
        model.LaunchAtLogin = false;
        Assert.Equal(1, loginItems.UnregisterCalls);
        Assert.False(model.LaunchAtLogin);
    }

    [Fact]
    public void AnUnregistrableCopyIsDetectedUpFrontNotOnFailure()
    {
        var model = Make(loginItems: new FakeLoginItems { Registrable = false });
        Assert.False(model.CanEnableLaunchAtLogin);
        Assert.NotNull(model.LaunchAtLoginProblem);
    }

    [Fact]
    public void EnablingAnUnregistrableCopyIsRefusedWithoutCallingTheSystem()
    {
        var loginItems = new FakeLoginItems { Registrable = false };
        var model = Make(loginItems: loginItems);
        model.LaunchAtLogin = true;
        Assert.Equal(0, loginItems.RegisterCalls);
        Assert.False(model.LaunchAtLogin);
    }

    [Fact]
    public void ARegistrationFailureRevertsTheToggleAndExplainsItself()
    {
        var loginItems = new FakeLoginItems { RegisterFailure = new UnauthorizedAccessException("denied") };
        var model = Make(loginItems: loginItems);
        model.LaunchAtLogin = true;
        Assert.False(model.LaunchAtLogin);
        Assert.Contains("could not be added", model.LaunchAtLoginProblem);
    }

    [Fact]
    public void AnUnregistrationFailureLeavesTheToggleOnAndExplainsItself()
    {
        var loginItems = new FakeLoginItems { Enabled = true, UnregisterFailure = new IOException("busy") };
        var model = Make(loginItems: loginItems);
        model.LaunchAtLogin = false;
        Assert.True(model.LaunchAtLogin);
        Assert.Contains("could not be removed", model.LaunchAtLoginProblem);
    }

    [Fact]
    public void ALaterSuccessClearsAnEarlierProblem()
    {
        var loginItems = new FakeLoginItems { RegisterFailure = new IOException("busy") };
        var model = Make(loginItems: loginItems);
        model.LaunchAtLogin = true;
        Assert.NotNull(model.LaunchAtLoginProblem);

        loginItems.RegisterFailure = null;
        model.LaunchAtLogin = true;

        Assert.Null(model.LaunchAtLoginProblem);
        Assert.True(model.LaunchAtLogin);
    }
}
