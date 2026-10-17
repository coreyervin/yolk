using Yolk.Domain.Configuration;
using Yolk.Infrastructure.Persistence;

namespace Yolk.Infrastructure.Tests;

public class SettingNamesTests
{
    /// <summary>The same names the Mac app stores, so the two read alike.</summary>
    [Theory]
    [InlineData(StopAfter.Never, "never")]
    [InlineData(StopAfter.Minutes30, "minutes30")]
    [InlineData(StopAfter.Hour1, "hour1")]
    [InlineData(StopAfter.Hours4, "hours4")]
    [InlineData(StopAfter.Hours8, "hours8")]
    public void StopAfterIsStoredByItsMacName(StopAfter option, string name)
    {
        Assert.Equal(name, SettingNames.Name(option));
        Assert.Equal(option, SettingNames.ParseStopAfter(name));
    }

    [Theory]
    [InlineData("fortnight")]
    [InlineData("")]
    [InlineData(null)]
    public void AnUnrecognisedNameReadsAsMissing(string? name) =>
        Assert.Null(SettingNames.ParseStopAfter(name));
}
