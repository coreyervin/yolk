import Foundation

/// Validated settings shared by the CLI's argument parser and the app's
/// settings UI. Bounds live here and nowhere else so the two cannot drift.
public struct YolkConfig: Equatable, Sendable {
    public static let intervalRange: ClosedRange<TimeInterval> = 5...120

    /// Capped at 300s deliberately: above it Slack's ~10 minute away timer
    /// becomes reachable and yolk silently stops doing its job.
    public static let thresholdRange: ClosedRange<TimeInterval> = 5...300

    /// The CLI's documented defaults, shared so the help text, the parser,
    /// and the app's settings cannot drift from each other.
    public static let defaultInterval: TimeInterval = 30
    /// A range rather than one number: each nudge waits for a random point in
    /// it, so the nudges never land on a regular, noticeable beat.
    public static let defaultThreshold: ClosedRange<TimeInterval> = 30...60

    /// The most seconds between idle checks. Checks are otherwise timed to
    /// land when idle reaches the next nudge point.
    public var interval: TimeInterval
    /// Idle seconds before simulating activity: a random point in this range,
    /// chosen afresh for every nudge. A single value is a range of one.
    public var threshold: ClosedRange<TimeInterval>
    /// nil runs indefinitely.
    public var timeout: TimeInterval?

    public init(
        interval: TimeInterval = Self.defaultInterval,
        threshold: ClosedRange<TimeInterval> = Self.defaultThreshold,
        timeout: TimeInterval? = nil
    ) throws {
        guard Self.intervalRange.contains(interval) else {
            throw ConfigError.intervalOutOfRange(interval)
        }
        for bound in [threshold.lowerBound, threshold.upperBound]
        where !Self.thresholdRange.contains(bound) {
            throw ConfigError.thresholdOutOfRange(bound)
        }
        if let timeout, !timeout.isFinite || timeout <= 0 {
            throw ConfigError.invalidTimeout
        }
        self.interval = interval
        self.threshold = threshold
        self.timeout = timeout
    }
}

extension YolkConfig {
    /// The documented defaults, which are in range by construction. Lets
    /// non-throwing contexts hold a valid config without a force-try.
    public static let fallback: YolkConfig = {
        // Cannot fail: both values come from the ranges declared above.
        try! YolkConfig()
    }()
}

public enum ConfigError: Error, Equatable {
    case intervalOutOfRange(TimeInterval)
    case thresholdOutOfRange(TimeInterval)
    case invalidTimeout
}
