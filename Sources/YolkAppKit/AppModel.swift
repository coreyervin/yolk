import Foundation
import Observation
import YolkKit

/// Observable wrapper over `YolkSession` for the menu bar app.
///
/// Owns everything the UI needs and nothing it doesn't: settings and their
/// persistence, the session's observable state, and the two pieces of system
/// state the Settings pane reports. The views read this and call these
/// methods; they hold no logic of their own.
@MainActor
@Observable
public final class AppModel {
    /// The "Stop after…" menu. Raw values are persisted, so renaming a case
    /// silently resets the stored preference — add cases, don't rename them.
    public enum StopAfter: String, CaseIterable, Sendable, Equatable {
        case never
        case minutes30
        case hour1
        case hours4
        case hours8

        public var timeout: TimeInterval? {
            switch self {
            case .never: nil
            case .minutes30: 1800
            case .hour1: 3600
            case .hours4: 14400
            case .hours8: 28800
            }
        }

        public var title: String {
            switch self {
            case .never: "Never"
            case .minutes30: "30 minutes"
            case .hour1: "1 hour"
            case .hours4: "4 hours"
            case .hours8: "8 hours"
            }
        }
    }

    private enum Key {
        static let interval = "interval"
        static let threshold = "threshold"
        static let stopAfter = "stopAfter"
    }

    // MARK: Session state

    public private(set) var isActive = false
    /// nil while nudging; set while the session is paused. A paused session is
    /// still active — it resumes on its own when the screen unlocks.
    public private(set) var pauseReason: PauseReason?
    public private(set) var activityCount = 0
    public private(set) var startedAt: Date?
    /// Display only, for "auto-exit at 17:30". The real deadline is monotonic
    /// and lives in the engine.
    public private(set) var estimatedEnd: Date?

    // MARK: System state

    public private(set) var hasAccessibilityPermission = false
    /// The display-sleep delay, but only when it beats yolk to the punch —
    /// otherwise nil. Same condition the CLI warns about.
    public private(set) var displaySleepWarning: TimeInterval?

    // MARK: Settings

    // These three use explicit access/withMutation over @ObservationIgnored
    // storage rather than stored properties with `didSet`. Under @Observable a
    // `didSet` that assigns to its own property recurses forever and crashes
    // with a stack overflow — the macro rewrites the stored property into a
    // computed one, so the assignment re-enters the setter. Verified.
    @ObservationIgnored private var storedInterval: TimeInterval = YolkConfig.defaultInterval
    @ObservationIgnored private var storedThreshold: TimeInterval = YolkConfig.defaultThreshold
    @ObservationIgnored private var storedStopAfter: StopAfter = .never

    public var interval: TimeInterval {
        get {
            access(keyPath: \.interval)
            return storedInterval
        }
        set {
            let clamped = Self.clamp(newValue, to: YolkConfig.intervalRange)
            guard clamped != storedInterval else { return }
            withMutation(keyPath: \.interval) { storedInterval = clamped }
            store.writeDouble(Key.interval, clamped)
            applyConfig()
        }
    }

    public var threshold: TimeInterval {
        get {
            access(keyPath: \.threshold)
            return storedThreshold
        }
        set {
            let clamped = Self.clamp(newValue, to: YolkConfig.thresholdRange)
            guard clamped != storedThreshold else { return }
            withMutation(keyPath: \.threshold) { storedThreshold = clamped }
            store.writeDouble(Key.threshold, clamped)
            applyConfig()
        }
    }

    public var stopAfter: StopAfter {
        get {
            access(keyPath: \.stopAfter)
            return storedStopAfter
        }
        set {
            guard newValue != storedStopAfter else { return }
            withMutation(keyPath: \.stopAfter) { storedStopAfter = newValue }
            store.writeString(Key.stopAfter, newValue.rawValue)
            applyConfig()
        }
    }

    private let session: YolkSession
    private let store: SettingsStore
    private let environment: SystemEnvironment

    public init(environment: SystemEnvironment = .live, defaults: SettingsStore = .standard()) {
        self.environment = environment
        self.store = defaults

        // Stored values are validated rather than trusted: preferences can be
        // hand-edited, or written by a build with different bounds.
        storedInterval = Self.validate(
            defaults.readDouble(Key.interval), in: YolkConfig.intervalRange,
            fallback: YolkConfig.defaultInterval)
        storedThreshold = Self.validate(
            defaults.readDouble(Key.threshold), in: YolkConfig.thresholdRange,
            fallback: YolkConfig.defaultThreshold)
        storedStopAfter =
            defaults.readString(Key.stopAfter).flatMap(StopAfter.init(rawValue:)) ?? .never

        // Starts idle: opening the app makes Yolk available, it does not begin
        // a session.
        session = YolkSession(config: YolkConfig.fallback, environment: environment)
        session.reconfigure(makeConfig())
        session.onEvent = { [weak self] event in
            MainActor.assumeIsolated { self?.handle(event) }
        }
        refreshSystemState()
    }

    // MARK: - Intent

    public func setActive(_ active: Bool) {
        guard active != isActive else { return }
        if active {
            do {
                try session.start()
            } catch {
                // Without an assertion there is nothing to show as active; the
                // menu stays idle rather than lying about it.
                isActive = false
            }
        } else {
            session.stop()
        }
    }

    public func toggle() {
        setActive(!isActive)
    }

    /// Re-reads the system state the Settings pane displays. Cheap enough on
    /// demand, and `pmset` should not be spawned on every view refresh.
    public func refreshSystemState() {
        hasAccessibilityPermission = environment.hasPostPermission()
        displaySleepWarning = environment.displaySleepSeconds().flatMap {
            storedThreshold + storedInterval + 5 >= $0 ? $0 : nil
        }
    }

    public func requestAccessibilityPermission() {
        environment.requestPostPermission()
        refreshSystemState()
    }

    /// "2h 14m", or nil while idle.
    public func uptimeDescription(asOf date: Date) -> String? {
        guard let startedAt else { return nil }
        let elapsed = max(0, date.timeIntervalSince(startedAt))
        let minutes = Int(elapsed / 60)
        return minutes < 60 ? "\(minutes)m" : "\(minutes / 60)h \(minutes % 60)m"
    }

    // MARK: - Internals

    private func handle(_ event: YolkEvent) {
        switch event {
        case .started:
            isActive = true
            activityCount = 0
            pauseReason = nil
            if case .running(let run) = session.state {
                startedAt = run.startedAt
                estimatedEnd = run.estimatedEnd
            }
        case .activitySimulated(let total):
            activityCount = total
        case .paused(let reason):
            pauseReason = reason
        case .resumed:
            pauseReason = nil
        case .stopped:
            isActive = false
            pauseReason = nil
            activityCount = 0
            startedAt = nil
            estimatedEnd = nil
        case .checked, .permissionMissing, .postFailed, .displaySleepTooSoon:
            // Nothing observable in the UI — the menu shows state, not a log.
            break
        }
    }

    private func makeConfig() -> YolkConfig {
        // Both settings are clamped on the way in, so this cannot throw.
        (try? YolkConfig(
            interval: storedInterval, threshold: storedThreshold,
            timeout: storedStopAfter.timeout))
            ?? YolkConfig.fallback
    }

    /// Applies the current settings to the session in place, keeping
    /// `startedAt` and the nudge count intact, and refreshes the warning that
    /// depends on them.
    private func applyConfig() {
        session.reconfigure(makeConfig())
        if case .running(let run) = session.state {
            estimatedEnd = run.estimatedEnd
        }
        refreshSystemState()
    }

    private static func clamp(
        _ value: TimeInterval, to range: ClosedRange<TimeInterval>
    ) -> TimeInterval {
        min(max(value, range.lowerBound), range.upperBound)
    }

    private static func validate(
        _ stored: Double?, in range: ClosedRange<TimeInterval>, fallback: TimeInterval
    ) -> TimeInterval {
        guard let stored, stored.isFinite, range.contains(stored) else { return fallback }
        return stored
    }
}

extension AppModel {
    /// Drives one check-loop iteration without waiting on a real timer.
    /// Test-only seam: the app is driven by the session's own dispatch timer.
    func tickForTesting() {
        session.tick()
    }
}
