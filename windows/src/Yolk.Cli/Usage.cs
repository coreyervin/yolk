using Yolk.Core;

namespace Yolk.Cli;

/// <summary>
/// The CLI's help and version output. The Mac CLI's text with only the
/// platform-specific wording changed; UsageTests checks it against the shared
/// goldens so the two can't drift apart.
/// </summary>
internal static class Usage
{
    public static readonly string VersionLine = $"yolk v{YolkVersion.Current}";

    public static readonly string Text = $"""
        yolk v{YolkVersion.Current} — keep your PC awake and Slack active

        usage: yolk [options]

        options:
          -i, --interval <sec>    seconds between idle checks (default 30, 5–120)
              --threshold <sec>   idle seconds before simulating activity
                                  (default 60, 5–300; capped so Slack's ~10 min
                                  away timer can never be reached)
          -t, --timeout <dur>     exit automatically after a duration: 8h, 90m,
                                  45s, 1d, or plain seconds (default: run until Ctrl-C)
          -v, --verbose           log every check and simulated event
          -h, --help              show this help
              --version           show version

        notes:
          • Needs no special permission. Simulated input may not register while
            an app running as administrator has focus.
          • Lock your screen (Win+L) when you walk away — yolk pauses while
            locked, so Slack correctly shows you away and the display can sleep.
          • Closing the lid follows your Windows power settings.
        """;
}
