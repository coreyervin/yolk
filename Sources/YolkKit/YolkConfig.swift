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
    public static let defaultThreshold: TimeInterval = 60

    /// Seconds between idle checks.
    public var interval: TimeInterval
    /// Idle seconds before simulating activity.
    public var threshold: TimeInterval
    /// nil runs indefinitely.
    public var timeout: TimeInterval?

    public init(
        interval: TimeInterval = Self.defaultInterval,
        threshold: TimeInterval = Self.defaultThreshold,
        timeout: TimeInterval? = nil
    ) throws {
        guard Self.intervalRange.contains(interval) else {
            throw ConfigError.intervalOutOfRange(interval)
        }
        guard Self.thresholdRange.contains(threshold) else {
            throw ConfigError.thresholdOutOfRange(threshold)
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
