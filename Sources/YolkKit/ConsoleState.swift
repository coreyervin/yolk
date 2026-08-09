import CoreGraphics
import Foundation

/// Whether this login session is ours, on the console, and unlocked.
///
/// Only `.active` permits simulating activity. Every other case pauses — this
/// is the fail-safe that stops yolk posting synthetic input into a locked
/// screen or somebody else's fast-user-switched session.
public enum ConsoleState: Equatable, Sendable {
    case active
    case locked
    case switchedOut
    case unknown

    /// Why simulation should pause, or nil when it may proceed.
    ///
    /// Every "should I pause?" decision routes through here, so a case added
    /// later cannot silently start posting events into the wrong session.
    public var pauseReason: PauseReason? {
        switch self {
        case .active: nil
        case .locked: .screenLocked
        case .switchedOut: .sessionSwitchedOut
        case .unknown: .sessionUnknown
        }
    }

    /// True for every state except `.active`.
    public var pausesSimulation: Bool { pauseReason != nil }
}

/// Keys read out of `CGSessionCopyCurrentDictionary()`.
enum SessionKey {
    static let onConsole = kCGSessionOnConsoleKey as String
    static let screenLocked = "CGSSessionScreenIsLocked"
}

extension ConsoleState {
    /// Derives state from a session dictionary. A nil dictionary means the
    /// session was unreadable, which must fail safe to `.unknown` rather than
    /// being optimistically treated as active.
    static func from(session: [String: Any]?) -> ConsoleState {
        guard let session else { return .unknown }
        // Absent keys default to false, so a partially-readable dictionary
        // degrades to .switchedOut rather than .active.
        let locked = (session[SessionKey.screenLocked] as? NSNumber)?.boolValue ?? false
        let onConsole = (session[SessionKey.onConsole] as? NSNumber)?.boolValue ?? false
        if locked { return .locked }
        return onConsole ? .active : .switchedOut
    }
}
