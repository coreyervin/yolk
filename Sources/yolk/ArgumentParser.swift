import Foundation
import YolkKit

/// What the command line asked yolk to do.
enum ParseOutcome: Equatable {
    case run(Invocation)
    case help
    case version
}

/// A validated invocation. `verbose` and `invokedBare` are presentation
/// concerns and deliberately live outside `YolkConfig`, which the app shares.
struct Invocation: Equatable {
    var config: YolkConfig
    var verbose: Bool
    var invokedBare: Bool
}

/// A usage error, carrying the exact message the CLI has always printed.
struct ArgumentError: Error, Equatable {
    let message: String
}

/// Hand-rolled on purpose: four flags do not justify a dependency.
enum ArgumentParser {
    static func parse(_ args: [String]) throws -> ParseOutcome {
        var interval = YolkConfig.defaultInterval
        var threshold = YolkConfig.defaultThreshold
        var timeout: TimeInterval?
        var verbose = false

        var index = 0
        // Consumes the next argument as `flag`'s value.
        func value(for flag: String) throws -> String {
            index += 1
            guard index < args.count else {
                throw ArgumentError(message: "\(flag) requires a value")
            }
            return args[index]
        }

        while index < args.count {
            let arg = args[index]
            switch arg {
            case "-i", "--interval":
                // Validated here, against YolkConfig's range rather than a
                // literal, so the bounds still live in exactly one place while
                // the message and the argument-order precedence stay frozen.
                guard let n = Double(try value(for: arg)),
                      YolkConfig.intervalRange.contains(n)
                else {
                    throw ArgumentError(message: "--interval must be 5–120 seconds")
                }
                interval = n
            case "--threshold":
                threshold = try parseThreshold(try value(for: arg))
            case "-t", "--timeout":
                let raw = try value(for: arg)
                guard let duration = DurationParser.parse(raw) else {
                    throw ArgumentError(
                        message: "invalid duration '\(raw)' — use e.g. 8h, 90m, 45s, or seconds")
                }
                timeout = duration
            case "-v", "--verbose":
                verbose = true
            case "-h", "--help":
                return .help
            case "--version":
                return .version
            default:
                throw ArgumentError(message: "unknown option '\(arg)'")
            }
            index += 1
        }

        // Every value above is already in range, so this cannot throw — but
        // YolkConfig stays the only thing entitled to say so.
        let config = try YolkConfig(interval: interval, threshold: threshold, timeout: timeout)
        return .run(Invocation(config: config, verbose: verbose, invokedBare: args.isEmpty))
    }

    /// `45` for a fixed point, or `30-60` for a random point in that range.
    /// Both ends are checked against YolkConfig's bounds before the range is
    /// built, since an inverted ClosedRange traps rather than throwing.
    static func parseThreshold(_ raw: String) throws -> ClosedRange<TimeInterval> {
        let outOfRange = ArgumentError(
            message: "--threshold must be 5–300 seconds (above 300 Slack could mark you away)")
        let parts = raw.split(separator: "-", omittingEmptySubsequences: false)
        let values = parts.compactMap { Double($0) }
        guard (1...2).contains(parts.count), values.count == parts.count,
              values.allSatisfy({ YolkConfig.thresholdRange.contains($0) }),
              let low = values.first, let high = values.last
        else { throw outOfRange }
        guard low <= high else {
            throw ArgumentError(message: "--threshold range must run from low to high, e.g. 30-60")
        }
        return low...high
    }
}
