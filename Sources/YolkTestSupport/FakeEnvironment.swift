import Foundation

import YolkKit

/// Records assertion create/release traffic so lifetime can be asserted on
/// without touching real IOKit.
public final class AssertionRecorder: @unchecked Sendable {
    private let lock = NSLock()
    private var _created: [String] = []
    private var _released: [AssertionHandle] = []
    private var _nextHandle: UInt32 = 1

    /// When set, `create` throws this instead of vending a handle.
    public init() {}

    public var failure: AssertionError?

    public var created: [String] { lock.withLock { _created } }
    public var released: [AssertionHandle] { lock.withLock { _released } }

    public func create(reason: String) throws -> AssertionHandle {
        if let failure { throw failure }
        return lock.withLock {
            _created.append(reason)
            let handle = AssertionHandle(rawValue: _nextHandle)
            _nextHandle += 1
            return handle
        }
    }

    public func release(_ handle: AssertionHandle) {
        lock.withLock { _released.append(handle) }
    }
}

/// A mutable stand-in for the whole machine. Tests set properties directly and
/// then drive `YolkSession.tick()` by hand — no real timers, no real sleeping.
public final class FakeSystem: @unchecked Sendable {
    public init() {}

    public var idle: TimeInterval = 0
    public var console: ConsoleState = .active
    public var postSucceeds = true
    /// What the verification re-read returns after a successful post. Below
    /// 1.0 means "the event landed"; above means the grant is missing.
    public var idleAfterPost: TimeInterval = 0.2
    public var monotonic: TimeInterval = 0
    public var displaySleep: TimeInterval?
    public var wallClock = Date(timeIntervalSince1970: 1_000_000)

    public private(set) var postCount = 0
    private var pendingVerification = false
    public let recorder = AssertionRecorder()

    public var environment: SystemEnvironment {
        SystemEnvironment(
            idleSeconds: { [self] in
                // The session reads idle once to decide, then again after
                // posting to confirm the timer actually reset.
                if pendingVerification {
                    pendingVerification = false
                    return idleAfterPost
                }
                return idle
            },
            consoleState: { [self] in console },
            postActivity: { [self] in
                guard postSucceeds else { return false }
                postCount += 1
                pendingVerification = true
                return true
            },
            hasPostPermission: { true },
            requestPostPermission: {},
            displaySleepSeconds: { [self] in displaySleep },
            monotonicNow: { [self] in monotonic },
            now: { [self] in wallClock },
            createAssertion: { [self] in try recorder.create(reason: $0) },
            releaseAssertion: { [self] in recorder.release($0) },
            waitForEventDelivery: {}
        )
    }
}

@MainActor
public final class EventCollector {
    public init() {}

    public var events: [YolkEvent] = []

    public func count(of event: YolkEvent) -> Int {
        events.filter { $0 == event }.count
    }
}

extension SystemEnvironment {
    /// A fully-faked environment. Every closure has an inert default so a test
    /// overrides only what it cares about.
    public static func fake(
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
            releaseAssertion: { recorder.release($0) },
            waitForEventDelivery: {}
        )
    }
}
