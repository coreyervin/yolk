using System.Text.RegularExpressions;
using Yolk.Domain.Configuration;
using Yolk.TestSupport;

namespace Yolk.Domain.Tests.Configuration;

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

public class StopAfterTests
{
    [Theory]
    [InlineData(StopAfter.Never, null)]
    [InlineData(StopAfter.Minutes30, 1800.0)]
    [InlineData(StopAfter.Hour1, 3600.0)]
    [InlineData(StopAfter.Hours4, 14400.0)]
    [InlineData(StopAfter.Hours8, 28800.0)]
    public void StopAfterMapsToTheDocumentedDurations(StopAfter option, double? seconds) =>
        Assert.Equal(seconds, option.Timeout());

    [Fact]
    public void EveryOptionIsOfferedNeverFirst()
    {
        var options = Enum.GetValues<StopAfter>();
        Assert.Equal(5, options.Length);
        Assert.Equal(StopAfter.Never, options[0]);
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
