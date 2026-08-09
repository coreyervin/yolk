import Foundation

/// RAII wrapper over the IOKit power assertion that stops idle sleep.
///
/// Release is tied to object lifetime rather than to a single shutdown path,
/// because the app starts and stops sessions repeatedly without quitting.
public final class PowerAssertion {
    private let environment: SystemEnvironment
    private var handle: AssertionHandle?

    public init(reason: String, environment: SystemEnvironment) throws {
        self.environment = environment
        self.handle = try environment.createAssertion(reason)
    }

    /// Releases the assertion. Safe to call more than once; only the first
    /// call reaches the system.
    public func release() {
        guard let handle else { return }
        // Cleared before the call so a re-entrant release can't double-free.
        self.handle = nil
        environment.releaseAssertion(handle)
    }

    deinit {
        release()
    }
}
