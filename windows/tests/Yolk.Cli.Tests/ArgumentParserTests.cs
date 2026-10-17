using Yolk.Cli;

namespace Yolk.Cli.Tests;

/// <summary>A port of the Mac's ArgumentParserTests — same flags, same messages.</summary>
public class ArgumentParserTests
{
    private static Invocation Run(params string[] args) =>
        Assert.IsType<ParseOutcome.Run>(ArgumentParser.Parse(args)).Invocation;

    [Fact]
    public void NoArgumentsYieldsTheDocumentedDefaultsFlaggedAsABareInvocation()
    {
        var invocation = Run();
        Assert.Equal(30, invocation.Config.Interval);
        Assert.Equal(60, invocation.Config.Threshold);
        Assert.Null(invocation.Config.Timeout);
        Assert.False(invocation.Verbose);
        Assert.True(invocation.InvokedBare);
    }

    public static TheoryData<string[], double, double, double?, bool> Spellings => new()
    {
        { ["-i", "45"], 45, 60, null, false },
        { ["--interval", "45"], 45, 60, null, false },
        { ["--threshold", "90"], 30, 90, null, false },
        { ["-t", "8h"], 30, 60, 28800, false },
        { ["--timeout", "90m"], 30, 60, 5400, false },
        { ["-v"], 30, 60, null, true },
        { ["--verbose"], 30, 60, null, true },
    };

    [Theory]
    [MemberData(nameof(Spellings))]
    public void BothSpellingsOfEveryFlagAreAccepted(
        string[] args, double interval, double threshold, double? timeout, bool verbose)
    {
        var invocation = Run(args);
        Assert.Equal(interval, invocation.Config.Interval);
        Assert.Equal(threshold, invocation.Config.Threshold);
        Assert.Equal(timeout, invocation.Config.Timeout);
        Assert.Equal(verbose, invocation.Verbose);
    }

    [Fact]
    public void AnyFlagSuppressesTheBareInvocationTip() => Assert.False(Run("-v").InvokedBare);

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void HelpShortCircuitsTheRun(string flag) =>
        Assert.IsType<ParseOutcome.Help>(ArgumentParser.Parse([flag]));

    [Fact]
    public void VersionShortCircuitsTheRun() =>
        Assert.IsType<ParseOutcome.Version>(ArgumentParser.Parse(["--version"]));

    [Fact]
    public void FlagsCombine()
    {
        var invocation = Run("-i", "15", "--threshold", "30", "-t", "45s", "-v");
        Assert.Equal(15, invocation.Config.Interval);
        Assert.Equal(30, invocation.Config.Threshold);
        Assert.Equal(45, invocation.Config.Timeout);
        Assert.True(invocation.Verbose);
    }

    // ── Usage errors ───────────────────────────────────────────────────────

    private const string IntervalMessage = "--interval must be 5–120 seconds";
    private const string ThresholdMessage =
        "--threshold must be 5–300 seconds (above 300 Slack could mark you away)";

    /// <summary>The exact strings the Mac CLI prints. Frozen.</summary>
    public static TheoryData<string[], string> Rejections => new()
    {
        { ["-i", "4"], IntervalMessage },
        { ["-i", "121"], IntervalMessage },
        { ["-i", "abc"], IntervalMessage },
        { ["--interval", "0"], IntervalMessage },
        { ["--threshold", "4"], ThresholdMessage },
        { ["--threshold", "301"], ThresholdMessage },
        { ["--threshold", "nope"], ThresholdMessage },
        { ["-t", "abc"], "invalid duration 'abc' — use e.g. 8h, 90m, 45s, or seconds" },
        { ["-t", "0"], "invalid duration '0' — use e.g. 8h, 90m, 45s, or seconds" },
        { ["-t", "-5m"], "invalid duration '-5m' — use e.g. 8h, 90m, 45s, or seconds" },
        { ["-i"], "-i requires a value" },
        { ["--interval"], "--interval requires a value" },
        { ["--threshold"], "--threshold requires a value" },
        { ["-t"], "-t requires a value" },
        { ["--nope"], "unknown option '--nope'" },
        { ["extra"], "unknown option 'extra'" },
    };

    [Theory]
    [MemberData(nameof(Rejections))]
    public void RejectionsCarryTheFrozenMessage(string[] args, string message)
    {
        var error = Assert.Throws<ArgumentError>(() => ArgumentParser.Parse(args));
        Assert.Equal(message, error.Message);
    }

    [Theory]
    [InlineData("5", 5)]
    [InlineData("120", 120)]
    public void IntervalBoundariesAreInclusive(string value, double expected) =>
        Assert.Equal(expected, Run("-i", value).Config.Interval);

    [Theory]
    [InlineData("5", 5)]
    [InlineData("300", 300)]
    public void ThresholdBoundariesAreInclusive(string value, double expected) =>
        Assert.Equal(expected, Run("--threshold", value).Config.Threshold);

    /// <summary>
    /// Each flag is validated as it is read, so the leftmost bad flag is the
    /// one reported.
    /// </summary>
    [Fact]
    public void TheLeftmostInvalidFlagIsTheOneReported()
    {
        Assert.Equal(ThresholdMessage,
            Assert.Throws<ArgumentError>(() => ArgumentParser.Parse(["--threshold", "999", "-i", "999"])).Message);
        Assert.Equal(IntervalMessage,
            Assert.Throws<ArgumentError>(() => ArgumentParser.Parse(["-i", "999", "--threshold", "999"])).Message);
    }

    [Fact]
    public void HelpWinsOverALaterInvalidFlag() =>
        Assert.IsType<ParseOutcome.Help>(ArgumentParser.Parse(["--help", "--nope"]));

    [Fact]
    public void AnInvalidFlagBeforeHelpStillFails() =>
        Assert.Equal("unknown option '--nope'",
            Assert.Throws<ArgumentError>(() => ArgumentParser.Parse(["--nope", "--help"])).Message);
}
