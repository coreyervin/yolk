import Foundation
import Testing

@testable import YolkKit
@testable import yolk

@Suite("ArgumentParser")
struct ArgumentParserTests {
    @Test("no arguments yields the documented defaults, flagged as a bare invocation")
    func defaults() throws {
        let outcome = try ArgumentParser.parse([])
        guard case .run(let invocation) = outcome else {
            Issue.record("expected .run, got \(outcome)")
            return
        }
        #expect(invocation.config.interval == 30)
        #expect(invocation.config.threshold == 60)
        #expect(invocation.config.timeout == nil)
        #expect(invocation.verbose == false)
        #expect(invocation.invokedBare == true)
    }

    @Test("both spellings of every flag are accepted", arguments: [
        (["-i", "45"], 45.0, 60.0, TimeInterval?.none, false),
        (["--interval", "45"], 45.0, 60.0, TimeInterval?.none, false),
        (["--threshold", "90"], 30.0, 90.0, TimeInterval?.none, false),
        (["-t", "8h"], 30.0, 60.0, TimeInterval?.some(28800), false),
        (["--timeout", "90m"], 30.0, 60.0, TimeInterval?.some(5400), false),
        (["-v"], 30.0, 60.0, TimeInterval?.none, true),
        (["--verbose"], 30.0, 60.0, TimeInterval?.none, true),
    ])
    func flagSpellings(
        args: [String], interval: TimeInterval, threshold: TimeInterval,
        timeout: TimeInterval?, verbose: Bool
    ) throws {
        guard case .run(let invocation) = try ArgumentParser.parse(args) else {
            Issue.record("expected .run for \(args)")
            return
        }
        #expect(invocation.config.interval == interval)
        #expect(invocation.config.threshold == threshold)
        #expect(invocation.config.timeout == timeout)
        #expect(invocation.verbose == verbose)
    }

    @Test("any flag suppresses the bare-invocation tip")
    func flaggedIsNotBare() throws {
        guard case .run(let invocation) = try ArgumentParser.parse(["-v"]) else {
            Issue.record("expected .run")
            return
        }
        #expect(invocation.invokedBare == false)
    }

    @Test("help and version short-circuit the run", arguments: [
        (["-h"], ParseOutcome.help),
        (["--help"], ParseOutcome.help),
        (["--version"], ParseOutcome.version),
    ])
    func shortCircuits(args: [String], expected: ParseOutcome) throws {
        #expect(try ArgumentParser.parse(args) == expected)
    }

    @Test("flags combine")
    func combined() throws {
        guard case .run(let invocation) =
            try ArgumentParser.parse(["-i", "15", "--threshold", "30", "-t", "45s", "-v"])
        else {
            Issue.record("expected .run")
            return
        }
        #expect(invocation.config.interval == 15)
        #expect(invocation.config.threshold == 30)
        #expect(invocation.config.timeout == 45)
        #expect(invocation.verbose == true)
    }

    // MARK: - Usage errors

    /// The exact strings the CLI has always printed. Frozen: these appear in
    /// muscle memory and in the README's troubleshooting notes.
    /// Hoisted to an explicitly-typed constant: Swift 6.4's type checker times
    /// out trying to infer this many tuple literals inline ("unable to
    /// type-check this expression in reasonable time").
    static let rejectionCases: [(args: [String], message: String)] = [
        (["-i", "4"], "--interval must be 5–120 seconds"),
        (["-i", "121"], "--interval must be 5–120 seconds"),
        (["-i", "abc"], "--interval must be 5–120 seconds"),
        (["--interval", "0"], "--interval must be 5–120 seconds"),
        (["--threshold", "4"],
         "--threshold must be 5–300 seconds (above 300 Slack could mark you away)"),
        (["--threshold", "301"],
         "--threshold must be 5–300 seconds (above 300 Slack could mark you away)"),
        (["--threshold", "nope"],
         "--threshold must be 5–300 seconds (above 300 Slack could mark you away)"),
        (["-t", "abc"], "invalid duration 'abc' — use e.g. 8h, 90m, 45s, or seconds"),
        (["-t", "0"], "invalid duration '0' — use e.g. 8h, 90m, 45s, or seconds"),
        (["-t", "-5m"], "invalid duration '-5m' — use e.g. 8h, 90m, 45s, or seconds"),
        (["-i"], "-i requires a value"),
        (["--interval"], "--interval requires a value"),
        (["--threshold"], "--threshold requires a value"),
        (["-t"], "-t requires a value"),
        (["--nope"], "unknown option '--nope'"),
        (["extra"], "unknown option 'extra'"),
    ]

    @Test("rejections carry the frozen message", arguments: rejectionCases)
    func rejections(args: [String], message: String) {
        #expect(throws: ArgumentError(message: message)) {
            try ArgumentParser.parse(args)
        }
    }

    @Test("range boundaries are inclusive", arguments: [
        (["-i", "5"], 5.0), (["-i", "120"], 120.0),
    ])
    func intervalBoundaries(args: [String], expected: TimeInterval) throws {
        guard case .run(let invocation) = try ArgumentParser.parse(args) else {
            Issue.record("expected .run")
            return
        }
        #expect(invocation.config.interval == expected)
    }

    @Test("threshold boundaries are inclusive", arguments: [
        (["--threshold", "5"], 5.0), (["--threshold", "300"], 300.0),
    ])
    func thresholdBoundaries(args: [String], expected: TimeInterval) throws {
        guard case .run(let invocation) = try ArgumentParser.parse(args) else {
            Issue.record("expected .run")
            return
        }
        #expect(invocation.config.threshold == expected)
    }

    /// The original validated each flag as it parsed it, so the leftmost bad
    /// flag is the one reported. Collecting values and validating afterwards
    /// would silently reorder these two messages.
    @Test("the leftmost invalid flag is the one reported")
    func leftmostErrorWins() {
        #expect(
            throws: ArgumentError(
                message:
                    "--threshold must be 5–300 seconds (above 300 Slack could mark you away)")
        ) {
            try ArgumentParser.parse(["--threshold", "999", "-i", "999"])
        }
        #expect(throws: ArgumentError(message: "--interval must be 5–120 seconds")) {
            try ArgumentParser.parse(["-i", "999", "--threshold", "999"])
        }
    }

    @Test("help wins over a later invalid flag, as it always has")
    func helpShortCircuitsBeforeLaterErrors() throws {
        #expect(try ArgumentParser.parse(["--help", "--nope"]) == .help)
    }

    @Test("an invalid flag before help still fails")
    func errorBeforeHelpStillFails() {
        #expect(throws: ArgumentError(message: "unknown option '--nope'")) {
            try ArgumentParser.parse(["--nope", "--help"])
        }
    }
}
