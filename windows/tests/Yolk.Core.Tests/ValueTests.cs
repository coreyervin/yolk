using System.Text.RegularExpressions;
using Yolk.Core;
using Yolk.TestSupport;

namespace Yolk.Core.Tests;

public class YolkConfigTests
{
    [Fact]
    public void DefaultsMatchTheCliDocumentedDefaults()
    {
        var config = new YolkConfig();
        Assert.Equal(30, config.Interval);
        Assert.Equal(60, config.Threshold);
        Assert.Null(config.Timeout);
    }

    [Theory]
    [InlineData(5.0)]
    [InlineData(120.0)]
    public void AcceptsIntervalAtBothRangeBoundaries(double interval) =>
        Assert.Equal(interval, new YolkConfig(interval: interval).Interval);

    [Theory]
    [InlineData(5.0)]
    [InlineData(300.0)]
    public void AcceptsThresholdAtBothRangeBoundaries(double threshold) =>
        Assert.Equal(threshold, new YolkConfig(threshold: threshold).Threshold);

    [Theory]
    [InlineData(4.9)]
    [InlineData(121.0)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void RejectsIntervalOutsideTheRange(double interval)
    {
        var error = Assert.Throws<ConfigException>(() => new YolkConfig(interval: interval));
        Assert.Equal(ConfigError.IntervalOutOfRange, error.Error);
    }

    [Theory]
    [InlineData(4.9)]
    [InlineData(301.0)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void RejectsThresholdOutsideTheRange(double threshold)
    {
        var error = Assert.Throws<ConfigException>(() => new YolkConfig(threshold: threshold));
        Assert.Equal(ConfigError.ThresholdOutOfRange, error.Error);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NaN)]
    public void RejectsANonPositiveOrNonFiniteTimeout(double timeout)
    {
        var error = Assert.Throws<ConfigException>(() => new YolkConfig(timeout: timeout));
        Assert.Equal(ConfigError.InvalidTimeout, error.Error);
    }

    [Fact]
    public void AcceptsANullTimeoutAsRunForever() => Assert.Null(new YolkConfig(timeout: null).Timeout);
}

public class DurationParserTests
{
    [Theory]
    [InlineData("8h", 28_800.0)]
    [InlineData("90m", 5_400.0)]
    [InlineData("45s", 45.0)]
    [InlineData("1d", 86_400.0)]
    [InlineData("300", 300.0)]
    [InlineData("1.5h", 5_400.0)]
    public void ParsesSuffixedAndBareDurationsIntoSeconds(string input, double expected) =>
        Assert.Equal(expected, DurationParser.Parse(input));

    [Fact]
    public void SuffixMatchingIsCaseInsensitive() => Assert.Equal(28_800, DurationParser.Parse("8H"));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("-2h")]
    [InlineData("abc")]
    [InlineData("8x")]
    [InlineData("h")]
    [InlineData("inf")]
    [InlineData("nan")]
    [InlineData(" 5")]
    [InlineData("1,000")]
    public void RejectsUnparseableNonPositiveAndNonFiniteInput(string input) =>
        Assert.Null(DurationParser.Parse(input));
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

public class PowerAssertionTests
{
    [Fact]
    public void CreatesAnAssertionWithTheGivenReason()
    {
        var system = new FakeSystem();
        using var assertion = new PowerAssertion("keeping the PC awake", system);
        Assert.Equal(["keeping the PC awake"], system.Created);
    }

    [Fact]
    public void ReleasesTheAssertionItCreatedOnDispose()
    {
        var system = new FakeSystem();
        var assertion = new PowerAssertion("test", system);
        Assert.Empty(system.Released);

        assertion.Dispose();

        Assert.Equal([new AssertionHandle(1)], system.Released);
    }

    [Fact]
    public void RepeatedDisposeReachesTheSystemOnlyOnce()
    {
        var system = new FakeSystem();
        var assertion = new PowerAssertion("test", system);

        assertion.Dispose();
        assertion.Dispose();
        assertion.Dispose();

        Assert.Single(system.Released);
    }

    [Fact]
    public void PropagatesACreationFailureInsteadOfPretendingToHoldOne()
    {
        var system = new FakeSystem { AssertionFailure = new AssertionCreationException(87) };

        var error = Assert.Throws<AssertionCreationException>(() => new PowerAssertion("test", system));

        Assert.Equal(87, error.Code);
        Assert.Empty(system.Released);
    }
}

public partial class VersionTests
{
    /// <summary>
    /// The Mac's Version.swift is the source of truth; the Windows build must
    /// report the same version or a release would ship two different numbers.
    /// </summary>
    [Fact]
    public void MatchesTheMacVersion()
    {
        var swift = RepoFiles.Read("Sources", "YolkKit", "Version.swift");
        var match = SwiftVersion().Match(swift);
        Assert.True(match.Success, "could not read the version out of Version.swift");
        Assert.Equal(match.Groups[1].Value, YolkVersion.Current);
    }

    [GeneratedRegex(@"public static let version = ""([^""]+)""")]
    private static partial Regex SwiftVersion();
}
