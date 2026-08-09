import Foundation
import Testing

@testable import YolkKit

@Suite("YolkConfig")
struct YolkConfigTests {
    @Test("defaults match the CLI's documented defaults")
    func defaults() throws {
        let config = try YolkConfig()
        #expect(config.interval == 30)
        #expect(config.threshold == 60)
        #expect(config.timeout == nil)
    }

    @Test("accepts interval at both range boundaries", arguments: [5.0, 120.0])
    func acceptsIntervalBoundaries(interval: TimeInterval) throws {
        #expect(try YolkConfig(interval: interval).interval == interval)
    }

    @Test("accepts threshold at both range boundaries", arguments: [5.0, 300.0])
    func acceptsThresholdBoundaries(threshold: TimeInterval) throws {
        #expect(try YolkConfig(threshold: threshold).threshold == threshold)
    }

    @Test("rejects interval outside the range", arguments: [4.9, 121.0, 0.0, -1.0])
    func rejectsIntervalOutOfRange(interval: TimeInterval) {
        #expect(throws: ConfigError.intervalOutOfRange(interval)) {
            try YolkConfig(interval: interval)
        }
    }

    @Test("rejects threshold outside the range", arguments: [4.9, 301.0, 0.0, -1.0])
    func rejectsThresholdOutOfRange(threshold: TimeInterval) {
        #expect(throws: ConfigError.thresholdOutOfRange(threshold)) {
            try YolkConfig(threshold: threshold)
        }
    }

    @Test("rejects a non-positive or non-finite timeout", arguments: [0.0, -1.0, .infinity, .nan])
    func rejectsInvalidTimeout(timeout: TimeInterval) {
        #expect(throws: ConfigError.invalidTimeout) {
            try YolkConfig(timeout: timeout)
        }
    }

    @Test("accepts a nil timeout as run-forever")
    func acceptsNilTimeout() throws {
        #expect(try YolkConfig(timeout: nil).timeout == nil)
    }
}
