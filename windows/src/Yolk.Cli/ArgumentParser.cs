using Yolk.Core;

namespace Yolk.Cli;

/// <summary>What the command line asked yolk to do.</summary>
internal abstract record ParseOutcome
{
    private ParseOutcome() { }

    public sealed record Run(Invocation Invocation) : ParseOutcome;

    public sealed record Help : ParseOutcome;

    public sealed record Version : ParseOutcome;
}

/// <summary>
/// A validated invocation. <c>Verbose</c> and <c>InvokedBare</c> are
/// presentation concerns and deliberately live outside <see cref="YolkConfig"/>.
/// </summary>
internal sealed record Invocation(YolkConfig Config, bool Verbose, bool InvokedBare);

/// <summary>A usage error, carrying the exact message the Mac CLI prints.</summary>
internal sealed class ArgumentError(string message) : Exception(message);

/// <summary>
/// Hand-rolled on purpose: four flags do not justify a dependency. A port of
/// the Mac CLI's parser — same flags, same messages, same precedence.
/// </summary>
internal static class ArgumentParser
{
    public static ParseOutcome Parse(IReadOnlyList<string> args)
    {
        var interval = YolkConfig.DefaultInterval;
        var threshold = YolkConfig.DefaultThreshold;
        double? timeout = null;
        var verbose = false;

        var index = 0;
        // Consumes the next argument as `flag`'s value.
        string ValueFor(string flag)
        {
            index++;
            if (index >= args.Count)
            {
                throw new ArgumentError($"{flag} requires a value");
            }
            return args[index];
        }

        while (index < args.Count)
        {
            var arg = args[index];
            switch (arg)
            {
                case "-i" or "--interval":
                    // Validated as each flag is read, so the leftmost bad flag
                    // is the one reported — matching the Mac CLI.
                    if (NumberParser.Parse(ValueFor(arg)) is not { } i || !YolkConfig.IsValidInterval(i))
                    {
                        throw new ArgumentError("--interval must be 5–120 seconds");
                    }
                    interval = i;
                    break;
                case "--threshold":
                    if (NumberParser.Parse(ValueFor(arg)) is not { } t || !YolkConfig.IsValidThreshold(t))
                    {
                        throw new ArgumentError(
                            "--threshold must be 5–300 seconds (above 300 Slack could mark you away)");
                    }
                    threshold = t;
                    break;
                case "-t" or "--timeout":
                    var raw = ValueFor(arg);
                    timeout = DurationParser.Parse(raw)
                        ?? throw new ArgumentError(
                            $"invalid duration '{raw}' — use e.g. 8h, 90m, 45s, or seconds");
                    break;
                case "-v" or "--verbose":
                    verbose = true;
                    break;
                case "-h" or "--help":
                    return new ParseOutcome.Help();
                case "--version":
                    return new ParseOutcome.Version();
                default:
                    throw new ArgumentError($"unknown option '{arg}'");
            }
            index++;
        }

        // Every value above is already in range, so this cannot throw — but
        // YolkConfig stays the only thing entitled to say so.
        var config = new YolkConfig(interval, threshold, timeout);
        return new ParseOutcome.Run(new Invocation(config, verbose, InvokedBare: args.Count == 0));
    }
}
