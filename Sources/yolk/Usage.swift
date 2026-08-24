import YolkKit

/// The CLI's frozen help and version output.
///
/// Kept out of `YolkKit`: the app has no equivalent, and the numbers quoted
/// here are documentation of `YolkConfig`'s ranges rather than a second source
/// of them.
enum Usage {
    static let versionLine = "yolk v\(YolkKit.version)"

    static let text = """
        yolk v\(YolkKit.version) — keep your Mac awake and Slack active

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
          • Needs Accessibility permission for your terminal (yolk will prompt
            on first run) to post synthetic input events.
          • Lock your screen (Ctrl-Cmd-Q) when you walk away — yolk pauses while
            locked, so Slack correctly shows you away and the display can sleep.
          • Closing the lid still sleeps the Mac (same as caffeinate -i).
        """
}
