using Yolk.Cli;
using Yolk.Core;

namespace Yolk.Cli.Tests;

/// <summary>Collects what the renderer wrote, so output can be diffed.</summary>
internal sealed class OutputSpy
{
    public string Out { get; private set; } = "";
    public string Err { get; private set; } = "";

    /// <summary>
    /// stdout and stderr in emission order — what a user redirecting 2>&amp;1
    /// sees, and how the Mac goldens were captured.
    /// </summary>
    public string Combined { get; private set; } = "";

    // Normalised so a CRLF checkout of the source compares equal.
    public void WriteOut(string s)
    {
        Out += s.ReplaceLineEndings("\n") + "\n";
        Combined += s.ReplaceLineEndings("\n") + "\n";
    }

    public void WriteErr(string s)
    {
        Err += s.ReplaceLineEndings("\n") + "\n";
        Combined += s.ReplaceLineEndings("\n") + "\n";
    }
}

/// <summary>A port of the Mac's ConsoleRendererTests.</summary>
public class ConsoleRendererTests
{
    private const int PinnedPid = 4242;
    private static readonly DateTime PinnedNow = new(2026, 8, 17, 14, 13, 20, DateTimeKind.Local);
    private const string FixedTip = "yolk --help lists every option.";

    private static (ConsoleRenderer Renderer, OutputSpy Spy) Make(
        bool verbose = false, bool invokedBare = false, string? tip = FixedTip, Func<DateTime>? now = null)
    {
        var spy = new OutputSpy();
        var renderer = new ConsoleRenderer(
            verbose, invokedBare, PinnedPid, now ?? (() => PinnedNow),
            spy.WriteOut, spy.WriteErr, tipPicker: () => tip);
        return (renderer, spy);
    }

    private static string Stamp(DateTime date) => ConsoleRenderer.Format(date, includingDate: false);

    // ── Startup banner parity with the Mac goldens ─────────────────────────

    [Fact]
    public void ABareInvocationReproducesTheBannerWithItsTip()
    {
        var (renderer, spy) = Make(invokedBare: true);
        renderer.Handle(new YolkEvent.Started(new YolkConfig()));
        renderer.RenderStartupTail();
        Assert.Equal(MacGoldens.Expected("cli-banner-bare.txt", PinnedPid), spy.Out);
        Assert.Empty(spy.Err);
    }

    [Fact]
    public void ABareInvocationWithNoTipAvailablePrintsNoTipBlock()
    {
        var (renderer, spy) = Make(invokedBare: true, tip: null);
        renderer.Handle(new YolkEvent.Started(new YolkConfig()));
        renderer.RenderStartupTail();
        Assert.Equal(MacGoldens.Expected("cli-banner-notip.txt", PinnedPid), spy.Out);
    }

    [Fact]
    public void AFlaggedInvocationEmitsNoTipEvenWhenOneIsAvailable()
    {
        var (renderer, spy) = Make(invokedBare: false);
        renderer.Handle(new YolkEvent.Started(new YolkConfig(timeout: 5)));
        renderer.RenderStartupTail();
        Assert.Equal(MacGoldens.Expected("cli-banner-flagged.txt", PinnedPid, PinnedNow.AddSeconds(5)), spy.Out);
    }

    [Fact]
    public void TheBannerReportsTheConfiguredThresholdAndIntervalAsWholeSeconds()
    {
        var (renderer, spy) = Make();
        renderer.Handle(new YolkEvent.Started(new YolkConfig(interval: 5, threshold: 300)));
        Assert.Contains("simulating activity when idle ≥ 300s (checking every 5s)", spy.Out);
    }

    [Fact]
    public void ADayPlusTimeoutIncludesTheDateInTheAutoExitLine()
    {
        var (renderer, spy) = Make();
        renderer.Handle(new YolkEvent.Started(new YolkConfig(timeout: 86400)));
        var expected = ConsoleRenderer.Format(PinnedNow.AddSeconds(86400), includingDate: true);
        Assert.Contains($"  • auto-exit at {expected}", spy.Out);
    }

    [Fact]
    public void ASubDayTimeoutShowsOnlyTheTimeOfDay()
    {
        var (renderer, spy) = Make();
        renderer.Handle(new YolkEvent.Started(new YolkConfig(timeout: 3600)));
        Assert.Contains($"  • auto-exit at {Stamp(PinnedNow.AddSeconds(3600))}\n", spy.Out);
    }

    [Fact]
    public void NoTimeoutMeansNoAutoExitLine()
    {
        var (renderer, spy) = Make();
        renderer.Handle(new YolkEvent.Started(new YolkConfig()));
        Assert.DoesNotContain("auto-exit", spy.Out);
    }

    // ── Verbose tick logging ───────────────────────────────────────────────

    [Fact]
    public void VerboseLogsABelowThresholdCheckAsRealActivity()
    {
        var (renderer, spy) = Make(verbose: true);
        renderer.Handle(new YolkEvent.Started(new YolkConfig()));
        renderer.Handle(new YolkEvent.Checked(59.7));
        Assert.EndsWith($"[{Stamp(PinnedNow)}] idle 59s — real activity, nothing to do\n", spy.Out);
    }

    [Fact]
    public void QuietModeLogsNoPerTickLines()
    {
        var (renderer, spy) = Make(verbose: false);
        renderer.Handle(new YolkEvent.Started(new YolkConfig()));
        var afterBanner = spy.Out;
        renderer.Handle(new YolkEvent.Checked(12));
        renderer.Handle(new YolkEvent.Checked(90));
        renderer.Handle(new YolkEvent.ActivitySimulated(1));
        renderer.Handle(new YolkEvent.Paused(PauseReason.ScreenLocked));
        renderer.Handle(new YolkEvent.Resumed());
        Assert.Equal(afterBanner, spy.Out);
    }

    [Fact]
    public void TheSimulatedActivityLineReusesTheIdleValueThatTriggeredIt()
    {
        var (renderer, spy) = Make(verbose: true);
        renderer.Handle(new YolkEvent.Started(new YolkConfig()));
        renderer.Handle(new YolkEvent.Checked(61.2));
        renderer.Handle(new YolkEvent.ActivitySimulated(7));
        Assert.EndsWith($"[{Stamp(PinnedNow)}] idle 61s — simulated activity (#7)\n", spy.Out);
    }

    [Fact]
    public void AnAtOrAboveThresholdCheckLogsNothingOnItsOwn()
    {
        var (renderer, spy) = Make(verbose: true);
        renderer.Handle(new YolkEvent.Started(new YolkConfig()));
        var afterBanner = spy.Out;
        renderer.Handle(new YolkEvent.Checked(60));
        Assert.Equal(afterBanner, spy.Out);
    }

    [Theory]
    [InlineData(PauseReason.ScreenLocked)]
    [InlineData(PauseReason.SessionSwitchedOut)]
    [InlineData(PauseReason.SessionUnknown)]
    public void VerboseReportsAPauseWithTheFrozenWording(PauseReason reason)
    {
        var (renderer, spy) = Make(verbose: true);
        renderer.Handle(new YolkEvent.Started(new YolkConfig()));
        renderer.Handle(new YolkEvent.Paused(reason));
        Assert.EndsWith($"[{Stamp(PinnedNow)}] screen locked or session inactive — paused\n", spy.Out);
    }

    [Fact]
    public void ResumingPrintsNothing()
    {
        var (renderer, spy) = Make(verbose: true);
        renderer.Handle(new YolkEvent.Started(new YolkConfig()));
        var afterBanner = spy.Out;
        renderer.Handle(new YolkEvent.Resumed());
        Assert.Equal(afterBanner, spy.Out);
    }

    // ── Warnings, always on stderr ─────────────────────────────────────────

    [Fact]
    public void IneffectiveInputIsReportedOnStderr()
    {
        var (renderer, spy) = Make();
        renderer.Handle(new YolkEvent.PermissionMissing());
        Assert.StartsWith("⚠ Simulated activity had no effect — Slack may still mark you away.\n", spy.Err);
        Assert.Contains("administrator", spy.Err);
        Assert.Empty(spy.Out);
    }

    [Fact]
    public void UnpostableInputIsReportedOnStderr()
    {
        var (renderer, spy) = Make();
        renderer.Handle(new YolkEvent.PostFailed());
        Assert.StartsWith("⚠ Could not post a synthetic input event — Slack may mark you away.", spy.Err);
        Assert.Empty(spy.Out);
    }

    [Fact]
    public void TheDisplaySleepWarningStatesBothTheSleepDelayAndYolksWorstCase()
    {
        var (renderer, spy) = Make();
        renderer.Handle(new YolkEvent.Started(new YolkConfig(interval: 30, threshold: 60)));
        renderer.Handle(new YolkEvent.DisplaySleepTooSoon(120));
        Assert.Equal("""
            ⚠ Your display sleeps after 2 min — sooner than yolk would act
              with these settings (up to 95s of idle). Once the display sleeps
              and locks, yolk pauses and Slack will mark you away. Lower --threshold/
              --interval, or raise "Turn off my screen after" in Settings → System → Power.

            """.ReplaceLineEndings("\n"), spy.Err);
    }

    [Fact]
    public void WarningsAreNotGatedOnVerbose()
    {
        var (renderer, spy) = Make(verbose: false);
        renderer.Handle(new YolkEvent.PermissionMissing());
        renderer.Handle(new YolkEvent.PostFailed());
        Assert.NotEmpty(spy.Err);
    }

    // ── Shutdown summary ───────────────────────────────────────────────────

    [Fact]
    public void TheTimeoutSummaryReportsElapsedMinutesAndTheActivityCount()
    {
        var clock = PinnedNow;
        var (renderer, spy) = Make(now: () => clock);
        renderer.Handle(new YolkEvent.Started(new YolkConfig(timeout: 3600)));
        renderer.Handle(new YolkEvent.ActivitySimulated(1));
        renderer.Handle(new YolkEvent.ActivitySimulated(2));
        clock = PinnedNow.AddSeconds(3600);
        renderer.Handle(new YolkEvent.Stopped(StopReason.TimeoutReached));
        Assert.EndsWith(
            "\nyolk: timeout reached — ran 60 min, simulated activity 2 times. Sleep settings restored.\n",
            spy.Out);
    }

    [Fact]
    public void AUserStopReadsStoppedUnlessASignalRenamedIt()
    {
        var (renderer, spy) = Make();
        renderer.Handle(new YolkEvent.Started(new YolkConfig()));
        renderer.Handle(new YolkEvent.Stopped(StopReason.UserRequested));
        Assert.EndsWith(
            "\nyolk: stopped — ran 0 min, simulated activity 0 times. Sleep settings restored.\n", spy.Out);
    }

    [Theory]
    [InlineData("terminated")]
    [InlineData("terminal closed")]
    public void SignalHandlersSupplyTheirOwnWording(string reason)
    {
        var (renderer, spy) = Make();
        renderer.Handle(new YolkEvent.Started(new YolkConfig()));
        renderer.UserStopReason = reason;
        renderer.Handle(new YolkEvent.Stopped(StopReason.UserRequested));
        Assert.EndsWith(
            $"\nyolk: {reason} — ran 0 min, simulated activity 0 times. Sleep settings restored.\n", spy.Out);
    }

    [Fact]
    public void ASingleSimulatedEventIsNotPluralised()
    {
        var (renderer, spy) = Make();
        renderer.Handle(new YolkEvent.Started(new YolkConfig()));
        renderer.Handle(new YolkEvent.ActivitySimulated(1));
        renderer.Handle(new YolkEvent.Stopped(StopReason.UserRequested));
        Assert.Contains("simulated activity 1 time.", spy.Out);
    }

    [Fact]
    public void ElapsedMinutesTruncateRatherThanRound()
    {
        var clock = PinnedNow;
        var (renderer, spy) = Make(now: () => clock);
        renderer.Handle(new YolkEvent.Started(new YolkConfig()));
        clock = PinnedNow.AddSeconds(119);
        renderer.Handle(new YolkEvent.Stopped(StopReason.UserRequested));
        Assert.Contains("ran 1 min,", spy.Out);
    }

    /// <summary>
    /// The display-sleep warning lands between the banner and the tip, as on
    /// the Mac — the order anyone redirecting 2>&amp;1 sees.
    /// </summary>
    [Fact]
    public void TheDisplaySleepWarningLandsBetweenTheBannerAndTheTip()
    {
        var (renderer, spy) = Make(invokedBare: true);
        renderer.Handle(new YolkEvent.Started(new YolkConfig(interval: 30, threshold: 60)));
        renderer.Handle(new YolkEvent.DisplaySleepTooSoon(120));
        renderer.RenderStartupTail();

        var bannerEnd = spy.Combined.IndexOf("yolk pauses while locked", StringComparison.Ordinal);
        var warning = spy.Combined.IndexOf("⚠ Your display sleeps after 2 min", StringComparison.Ordinal);
        var tip = spy.Combined.IndexOf("💡 Did you know?", StringComparison.Ordinal);
        var hint = spy.Combined.IndexOf("Press Ctrl-C to stop.", StringComparison.Ordinal);
        Assert.True(bannerEnd >= 0 && bannerEnd < warning && warning < tip && tip < hint);
    }

    [Fact]
    public void EveryTipMatchesTheMacOnesApartFromTheLockShortcut()
    {
        Assert.Equal(6, ConsoleRenderer.Tips.Length);
        Assert.Contains(ConsoleRenderer.Tips, t => t.Contains("Win+L", StringComparison.Ordinal));
        Assert.DoesNotContain(ConsoleRenderer.Tips, t => t.Contains("Cmd", StringComparison.Ordinal));
    }
}
