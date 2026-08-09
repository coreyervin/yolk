import Foundation

public enum PauseReason: Equatable, Sendable {
    case screenLocked
    case sessionSwitchedOut
    case sessionUnknown
}

public enum StopReason: Equatable, Sendable {
    case userRequested
    case timeoutReached
}

public enum YolkEvent: Equatable, Sendable {
    case started(YolkConfig)
    /// Emitted every tick that isn't paused; frontends decide whether to show it.
    case checked(idle: TimeInterval)
    case paused(PauseReason)
    case resumed
    case activitySimulated(total: Int)
    /// An event was posted but the idle timer didn't reset. At most once per session.
    case permissionMissing
    /// The event could not be constructed at all. At most once per session.
    case postFailed
    case displaySleepTooSoon(TimeInterval)
    case stopped(StopReason)
}

/// The engine. Owns the timer, holds the power assertion while running, and
/// emits events. Throws rather than calling `exit()` — only the CLI exits.
@MainActor
public final class YolkSession {
    public struct Run: Equatable, Sendable {
        public let startedAt: Date
        /// Display only — "auto-exit at 17:30". Never consulted to decide
        /// whether to stop; that is the monotonic deadline's job alone.
        public let estimatedEnd: Date?
        public var activityCount: Int
        public var pausedBecause: PauseReason?
    }

    public enum State: Equatable, Sendable {
        case stopped
        case running(Run)
    }

    public private(set) var state: State = .stopped
    public private(set) var config: YolkConfig
    public var onEvent: ((YolkEvent) -> Void)?

    /// Owns the dispatch sources so they are cancelled when the session is
    /// deallocated. Releasing an active dispatch source without cancelling it
    /// traps in libdispatch, and a `@MainActor` class's deinit cannot safely
    /// touch isolated state — so lifetime lives here instead.
    private final class TimerBox: @unchecked Sendable {
        private var sources: [DispatchSourceTimer] = []

        func adopt(_ source: DispatchSourceTimer) { sources.append(source) }

        func cancelAll() {
            sources.forEach { $0.cancel() }
            sources.removeAll()
        }

        deinit { sources.forEach { $0.cancel() } }
    }

    private let environment: SystemEnvironment
    private let timers = TimerBox()
    private var assertion: PowerAssertion?
    private var monotonicDeadline: TimeInterval?
    private var warnedPermissionMissing = false
    private var warnedPostFailed = false

    public init(config: YolkConfig, environment: SystemEnvironment = .live) {
        self.config = config
        self.environment = environment
    }

    public func start() throws {
        guard case .stopped = state else { return }
        warnedPermissionMissing = false
        warnedPostFailed = false

        // Throws before any state changes, so a failed assertion leaves the
        // session cleanly stopped rather than half-started.
        assertion = try PowerAssertion(
            reason: "yolk: keeping Mac awake while agents run", environment: environment)

        let startedAt = environment.now()
        monotonicDeadline = config.timeout.map { environment.monotonicNow() + $0 }
        state = .running(
            Run(
                startedAt: startedAt,
                estimatedEnd: config.timeout.map { startedAt.addingTimeInterval($0) },
                activityCount: 0,
                pausedBecause: nil))

        emit(.started(config))

        // Worst case yolk acts after threshold + interval + timer leeway of
        // idle. If the display sleeps sooner, the screen locks first and yolk
        // pauses forever — say so up front rather than fail silently.
        if let displaySleep = environment.displaySleepSeconds(),
           config.threshold + config.interval + 5 >= displaySleep {
            emit(.displaySleepTooSoon(displaySleep))
        }

        scheduleTimers()
    }

    public func stop() {
        stop(reason: .userRequested)
    }

    private func stop(reason: StopReason) {
        guard case .running = state else { return }
        timers.cancelAll()
        assertion?.release()
        assertion = nil
        monotonicDeadline = nil
        state = .stopped
        emit(.stopped(reason))
    }

    private func emit(_ event: YolkEvent) {
        onEvent?(event)
    }

    private func scheduleTimers() {
        timers.cancelAll()

        let ticker = DispatchSource.makeTimerSource(queue: .main)
        ticker.schedule(deadline: .now() + 1, repeating: config.interval, leeway: .seconds(5))
        ticker.setEventHandler { [weak self] in
            MainActor.assumeIsolated { self?.tick() }
        }
        ticker.resume()
        timers.adopt(ticker)

        // One-shot so the timeout fires on time rather than up to an interval
        // late. DispatchTime pauses during system sleep, so the monotonic check
        // in tick() remains the sleep-safe backstop.
        if let timeout = config.timeout {
            let deadline = DispatchSource.makeTimerSource(queue: .main)
            deadline.schedule(deadline: .now() + timeout, leeway: .seconds(1))
            deadline.setEventHandler { [weak self] in
                MainActor.assumeIsolated { self?.stop(reason: .timeoutReached) }
            }
            deadline.resume()
            timers.adopt(deadline)
        }
    }

    /// Applies new settings to a live session without disturbing `startedAt`
    /// or `activityCount`. A changed `timeout` restarts the countdown from now.
    /// When stopped, the config is simply stored.
    public func reconfigure(_ newConfig: YolkConfig) {
        let timeoutChanged = newConfig.timeout != config.timeout
        let intervalChanged = newConfig.interval != config.interval
        config = newConfig

        guard case .running(let run) = state else { return }

        if timeoutChanged {
            monotonicDeadline = newConfig.timeout.map { environment.monotonicNow() + $0 }
            state = .running(
                Run(
                    startedAt: run.startedAt,
                    estimatedEnd: newConfig.timeout.map {
                        environment.now().addingTimeInterval($0)
                    },
                    activityCount: run.activityCount,
                    pausedBecause: run.pausedBecause))
        }
        if timeoutChanged || intervalChanged {
            scheduleTimers()
        }
    }

    /// One iteration of the check loop. Internal so tests drive it directly
    /// instead of waiting on real timers.
    func tick() {
        guard case .running(var run) = state else { return }

        if let monotonicDeadline, environment.monotonicNow() >= monotonicDeadline {
            stop(reason: .timeoutReached)
            return
        }

        // Fail safe: only an active, unlocked, on-console session gets events.
        if let reason = environment.consoleState().pauseReason {
            guard run.pausedBecause != reason else { return }
            run.pausedBecause = reason
            state = .running(run)
            emit(.paused(reason))
            return
        }
        if run.pausedBecause != nil {
            run.pausedBecause = nil
            state = .running(run)
            emit(.resumed)
        }

        let idle = environment.idleSeconds()
        emit(.checked(idle: idle))
        guard idle >= config.threshold else { return }

        guard environment.postActivity() else {
            if !warnedPostFailed {
                warnedPostFailed = true
                emit(.postFailed)
            }
            return
        }

        // Posting is asynchronous; settle, then confirm the idle timer actually
        // reset — it won't without the Accessibility grant. Concurrent real
        // input can false-pass this, which is acceptable: real input means
        // presence, and a missing grant is caught on the next idle tick.
        environment.waitForEventDelivery()
        if environment.idleSeconds() < 1.0 {
            run.activityCount += 1
            state = .running(run)
            emit(.activitySimulated(total: run.activityCount))
        } else if !warnedPermissionMissing {
            warnedPermissionMissing = true
            emit(.permissionMissing)
        }
    }
}
