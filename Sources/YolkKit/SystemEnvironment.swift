import CoreGraphics
import Foundation
import IOKit.pwr_mgt

/// Opaque wrapper over `IOPMAssertionID` so fakes can vend their own handles.
public struct AssertionHandle: Equatable, Sendable {
    public let rawValue: UInt32

    public init(rawValue: UInt32) {
        self.rawValue = rawValue
    }
}

public enum AssertionError: Error, Equatable {
    case creationFailed(Int32)
}

/// The single boundary between YolkKit and the operating system.
///
/// A struct of closures rather than a protocol: fewer types, trivial partial
/// overrides in tests, and no protocol-witness ceremony for ten functions.
public struct SystemEnvironment: Sendable {
    public var idleSeconds: @Sendable () -> TimeInterval
    public var consoleState: @Sendable () -> ConsoleState
    public var postActivity: @Sendable () -> Bool
    public var hasPostPermission: @Sendable () -> Bool
    public var requestPostPermission: @Sendable () -> Void
    public var displaySleepSeconds: @Sendable () -> TimeInterval?
    public var monotonicNow: @Sendable () -> TimeInterval
    public var now: @Sendable () -> Date
    public var createAssertion: @Sendable (String) throws -> AssertionHandle
    public var releaseAssertion: @Sendable (AssertionHandle) -> Void

    public init(
        idleSeconds: @escaping @Sendable () -> TimeInterval,
        consoleState: @escaping @Sendable () -> ConsoleState,
        postActivity: @escaping @Sendable () -> Bool,
        hasPostPermission: @escaping @Sendable () -> Bool,
        requestPostPermission: @escaping @Sendable () -> Void,
        displaySleepSeconds: @escaping @Sendable () -> TimeInterval?,
        monotonicNow: @escaping @Sendable () -> TimeInterval,
        now: @escaping @Sendable () -> Date,
        createAssertion: @escaping @Sendable (String) throws -> AssertionHandle,
        releaseAssertion: @escaping @Sendable (AssertionHandle) -> Void
    ) {
        self.idleSeconds = idleSeconds
        self.consoleState = consoleState
        self.postActivity = postActivity
        self.hasPostPermission = hasPostPermission
        self.requestPostPermission = requestPostPermission
        self.displaySleepSeconds = displaySleepSeconds
        self.monotonicNow = monotonicNow
        self.now = now
        self.createAssertion = createAssertion
        self.releaseAssertion = releaseAssertion
    }
}

// kCGAnyInputEventType isn't surfaced to Swift; it's defined as ((CGEventType)(~0)).
private let anyInputEventType = CGEventType(rawValue: ~0)!

extension SystemEnvironment {
    /// Real CoreGraphics, IOKit, and pmset. This wiring is deliberately free of
    /// logic — everything decidable lives in the pure helpers it calls, which
    /// are the parts under test.
    public static let live = SystemEnvironment(
        idleSeconds: {
            CGEventSource.secondsSinceLastEventType(
                .combinedSessionState, eventType: anyInputEventType)
        },
        consoleState: {
            ConsoleState.from(session: CGSessionCopyCurrentDictionary() as? [String: Any])
        },
        postActivity: {
            // A mouse-moved event at the cursor's existing position: invisible,
            // but it resets the idle timer Slack reads.
            guard let location = CGEvent(source: nil)?.location,
                  let move = CGEvent(
                      mouseEventSource: nil, mouseType: .mouseMoved,
                      mouseCursorPosition: location, mouseButton: .left)
            else { return false }
            move.post(tap: .cghidEventTap)
            return true
        },
        hasPostPermission: { CGPreflightPostEventAccess() },
        requestPostPermission: { CGRequestPostEventAccess() },
        displaySleepSeconds: { DisplaySleepProbe.readFromPmset() },
        monotonicNow: { Double(clock_gettime_nsec_np(CLOCK_MONOTONIC)) / 1_000_000_000 },
        now: { Date() },
        createAssertion: { reason in
            var id: IOPMAssertionID = 0
            let result = IOPMAssertionCreateWithName(
                "PreventUserIdleSystemSleep" as CFString,
                IOPMAssertionLevel(kIOPMAssertionLevelOn),
                reason as CFString,
                &id)
            guard result == kIOReturnSuccess else {
                throw AssertionError.creationFailed(result)
            }
            return AssertionHandle(rawValue: id)
        },
        releaseAssertion: { handle in
            IOPMAssertionRelease(IOPMAssertionID(handle.rawValue))
        }
    )
}
