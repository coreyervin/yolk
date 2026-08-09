import Foundation

/// Parsing for the CLI's `-t/--timeout` durations and the app's timer presets.
public enum DurationParser {
    /// Parses `"8h"`, `"90m"`, `"45s"`, `"1d"`, or bare seconds into a
    /// `TimeInterval`. Returns nil for anything non-positive or unparseable.
    public static func parse(_ s: String) -> TimeInterval? {
        let lower = s.lowercased()
        let multipliers: [Character: Double] = ["s": 1, "m": 60, "h": 3600, "d": 86400]
        var numPart = lower
        var mult = 1.0
        if let last = lower.last, let m = multipliers[last] {
            numPart = String(lower.dropLast())
            mult = m
        }
        guard let n = Double(numPart), n.isFinite, n > 0 else { return nil }
        return n * mult
    }
}
