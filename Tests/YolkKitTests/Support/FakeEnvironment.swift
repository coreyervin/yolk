import Foundation

@testable import YolkKit

/// Records assertion create/release traffic so lifetime can be asserted on
/// without touching real IOKit.
final class AssertionRecorder: @unchecked Sendable {
    private let lock = NSLock()
    private var _created: [String] = []
    private var _released: [AssertionHandle] = []
    private var _nextHandle: UInt32 = 1

    /// When set, `create` throws this instead of vending a handle.
    var failure: AssertionError?

    var created: [String] { lock.withLock { _created } }
    var released: [AssertionHandle] { lock.withLock { _released } }

    func create(reason: String) throws -> AssertionHandle {
        if let failure { throw failure }
        return lock.withLock {
            _created.append(reason)
            let handle = AssertionHandle(rawValue: _nextHandle)
            _nextHandle += 1
            return handle
        }
    }

    func release(_ handle: AssertionHandle) {
        lock.withLock { _released.append(handle) }
    }
}

extension SystemEnvironment {
    /// A fully-faked environment. Every closure has an inert default so a test
    /// overrides only what it cares about.
    static func fake(
        idleSeconds: @escaping @Sendable () -> TimeInterval = { 0 },
        consoleState: @escaping @Sendable () -> ConsoleState = { .active },
        postActivity: @escaping @Sendable () -> Bool = { true },
        hasPostPermission: @escaping @Sendable () -> Bool = { true },
        requestPostPermission: @escaping @Sendable () -> Void = {},
        displaySleepSeconds: @escaping @Sendable () -> TimeInterval? = { nil },
        monotonicNow: @escaping @Sendable () -> TimeInterval = { 0 },
        now: @escaping @Sendable () -> Date = { Date(timeIntervalSince1970: 0) },
        recorder: AssertionRecorder = AssertionRecorder()
    ) -> SystemEnvironment {
        SystemEnvironment(
            idleSeconds: idleSeconds,
            consoleState: consoleState,
            postActivity: postActivity,
            hasPostPermission: hasPostPermission,
            requestPostPermission: requestPostPermission,
            displaySleepSeconds: displaySleepSeconds,
            monotonicNow: monotonicNow,
            now: now,
            createAssertion: { try recorder.create(reason: $0) },
            releaseAssertion: { recorder.release($0) }
        )
    }
}
