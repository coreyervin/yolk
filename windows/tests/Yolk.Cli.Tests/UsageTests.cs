using Yolk.Cli;

namespace Yolk.Cli.Tests;

public class UsageTests
{
    /// <summary>
    /// --help is the most-seen output yolk has. It must be the Mac golden,
    /// byte for byte, apart from the listed platform wording.
    /// </summary>
    [Fact]
    public void TheHelpTextMatchesTheMacGolden() =>
        // The golden is a file and ends with a newline; WriteLine supplies it.
        Assert.Equal(MacGoldens.Expected("cli-help.txt"), Usage.Text.ReplaceLineEndings("\n") + "\n");

    [Fact]
    public void TheVersionLineMatchesTheMacGolden() =>
        Assert.Equal(MacGoldens.Expected("cli-version.txt"), Usage.VersionLine + "\n");
}
