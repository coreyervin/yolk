using Yolk.Cli;
using Yolk.TestSupport;

namespace Yolk.Cli.Tests;

/// <summary>
/// The Mac CLI's goldens, translated to Windows wording.
/// </summary>
/// <remarks>
/// The Windows CLI is checked against the Mac goldens, not against copies of
/// its own. <see cref="PlatformWording"/> is the complete list of what may
/// differ between the two; any other difference fails a test. To change the
/// shared output, change a golden. To word something differently on Windows,
/// add it here.
/// </remarks>
internal static class MacGoldens
{
    public static readonly (string Mac, string Windows)[] PlatformWording =
    [
        ("keep your Mac awake and Slack active", "keep your PC awake and Slack active"),
        ("(Ctrl-Cmd-Q)", "(Win+L)"),
        ("""
          • Needs Accessibility permission for your terminal (yolk will prompt
            on first run) to post synthetic input events.
        """,
        """
          • Needs no special permission. Simulated input may not register while
            an app running as administrator has focus.
        """),
        ("Closing the lid still sleeps the Mac (same as caffeinate -i).",
         "Closing the lid follows your Windows power settings."),
    ];

    /// <summary>
    /// A golden in Windows wording, with the machine-dependent tokens filled in.
    /// </summary>
    public static string Expected(string name, int pid = 0, DateTime? autoExit = null)
    {
        var text = RepoFiles.Read("Goldens", name);
        foreach (var (mac, windows) in PlatformWording)
        {
            var from = mac.ReplaceLineEndings("\n");
            Assert.True(text.Contains(from, StringComparison.Ordinal) || !IsUsedBy(name, from),
                $"the Mac wording \"{from}\" no longer appears in {name}; update PlatformWording");
            text = text.Replace(from, windows.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        }
        text = text.Replace("%PID%", pid.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        if (autoExit is { } end)
        {
            text = text.Replace("%AUTOEXIT%", ConsoleRenderer.Format(end, includingDate: false), StringComparison.Ordinal);
        }
        return text;
    }

    // Which goldens each substitution is expected in, so a reworded golden
    // fails loudly instead of silently skipping its translation.
    private static bool IsUsedBy(string golden, string macText) => golden switch
    {
        "cli-help.txt" => true,
        "cli-version.txt" => false,
        _ => macText == "(Ctrl-Cmd-Q)",
    };
}
