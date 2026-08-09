import Foundation
import Testing

@testable import YolkKit

@MainActor
@Suite("YolkSession")
struct YolkSessionTests {
    private func makeSession(
        _ system: FakeSystem,
        interval: TimeInterval = 30,
        threshold: TimeInterval = 60,
        timeout: TimeInterval? = nil
    ) throws -> (YolkSession, EventCollector) {
        let session = YolkSession(
            config: try YolkConfig(interval: interval, threshold: threshold, timeout: timeout),
            environment: system.environment)
        let collector = EventCollector()
        session.onEvent = { collector.events.append($0) }
        return (session, collector)
    }

    private func runState(_ session: YolkSession) -> YolkSession.Run? {
        guard case .running(let run) = session.state else { return nil }
        return run
    }

    // MARK: - Lifecycle

    @Test("start enters running, holds an assertion, and announces itself")
    func startEntersRunning() throws {
        let system = FakeSystem()
        let (session, log) = try makeSession(system)

        try session.start()

        #expect(runState(session) != nil)
        #expect(system.recorder.created.count == 1)
        #expect(log.events.first == .started(try YolkConfig()))
    }

    @Test("stop returns to stopped, releases the assertion, and reports why")
    func stopReleases() throws {
        let system = FakeSystem()
        let (session, log) = try makeSession(system)
        try session.start()

        session.stop()

        #expect(session.state == .stopped)
        #expect(system.recorder.released.count == 1)
        #expect(log.events.last == .stopped(.userRequested))
    }

    @Test("the assertion is released when the session is deallocated")
    func releasesOnDealloc() throws {
        let system = FakeSystem()
        var session: YolkSession? = YolkSession(
            config: try YolkConfig(), environment: system.environment)
        try session?.start()
        #expect(system.recorder.released.isEmpty)

        session = nil

        #expect(system.recorder.released.count == 1)
    }

    // MARK: - Simulation

    @Test("does not simulate while idle is below the threshold")
    func noSimulationBelowThreshold() throws {
        let system = FakeSystem()
        system.idle = 59
        let (session, log) = try makeSession(system, threshold: 60)
        try session.start()

        session.tick()

        #expect(system.postCount == 0)
        #expect(log.events.contains(.checked(idle: 59)))
        #expect(runState(session)?.activityCount == 0)
    }

    @Test("simulates once idle reaches the threshold")
    func simulatesAtThreshold() throws {
        let system = FakeSystem()
        system.idle = 60
        let (session, log) = try makeSession(system, threshold: 60)
        try session.start()

        session.tick()

        #expect(system.postCount == 1)
        #expect(log.events.contains(.activitySimulated(total: 1)))
        #expect(runState(session)?.activityCount == 1)
    }

    @Test("counts activity only when the idle timer actually reset")
    func countsOnlyVerifiedActivity() throws {
        let system = FakeSystem()
        system.idle = 90
        system.idleAfterPost = 90  // event posted, but idle never moved
        let (session, log) = try makeSession(system)
        try session.start()

        session.tick()

        #expect(system.postCount == 1)
        #expect(runState(session)?.activityCount == 0)
        #expect(log.events.contains(.permissionMissing))
        #expect(!log.events.contains(.activitySimulated(total: 1)))
    }

    @Test("warns about a missing permission only once per session")
    func permissionWarningIsOneShot() throws {
        let system = FakeSystem()
        system.idle = 90
        system.idleAfterPost = 90
        let (session, log) = try makeSession(system)
        try session.start()

        for _ in 0..<5 { session.tick() }

        #expect(system.postCount == 5)
        #expect(log.count(of: .permissionMissing) == 1)
    }

    @Test("warns about an unconstructable event only once per session")
    func postFailureWarningIsOneShot() throws {
        let system = FakeSystem()
        system.idle = 90
        system.postSucceeds = false
        let (session, log) = try makeSession(system)
        try session.start()

        for _ in 0..<5 { session.tick() }

        #expect(log.count(of: .postFailed) == 1)
        #expect(runState(session)?.activityCount == 0)
    }

    // MARK: - Pausing

    @Test(
        "pauses instead of posting when the session isn't ours and unlocked",
        arguments: [
            (ConsoleState.locked, PauseReason.screenLocked),
            (ConsoleState.switchedOut, PauseReason.sessionSwitchedOut),
            (ConsoleState.unknown, PauseReason.sessionUnknown),
        ])
    func pausesOnNonActiveConsole(state: ConsoleState, reason: PauseReason) throws {
        let system = FakeSystem()
        system.idle = 90
        system.console = state
        let (session, log) = try makeSession(system)
        try session.start()

        session.tick()

        #expect(system.postCount == 0)
        #expect(log.events.contains(.paused(reason)))
        #expect(runState(session)?.pausedBecause == reason)
    }

    @Test("announces a pause once, not on every tick")
    func pauseIsAnnouncedOnce() throws {
        let system = FakeSystem()
        system.idle = 90
        system.console = .locked
        let (session, log) = try makeSession(system)
        try session.start()

        for _ in 0..<5 { session.tick() }

        #expect(log.count(of: .paused(.screenLocked)) == 1)
    }

    @Test("resumes exactly once when the console becomes active again")
    func resumesExactlyOnce() throws {
        let system = FakeSystem()
        system.idle = 90
        system.console = .locked
        let (session, log) = try makeSession(system)
        try session.start()
        session.tick()
        session.tick()

        system.console = .active
        session.tick()
        session.tick()

        #expect(log.count(of: .resumed) == 1)
        #expect(runState(session)?.pausedBecause == nil)
        #expect(system.postCount == 2)
    }

    // MARK: - Timeout

    @Test("stops when the monotonic deadline is reached")
    func stopsAtDeadline() throws {
        let system = FakeSystem()
        let (session, log) = try makeSession(system, timeout: 3_600)
        try session.start()

        system.monotonic = 3_599
        session.tick()
        #expect(session.state != .stopped)

        system.monotonic = 3_600
        session.tick()

        #expect(session.state == .stopped)
        #expect(log.events.last == .stopped(.timeoutReached))
    }

    @Test("the deadline survives a system sleep that jumps the monotonic clock")
    func deadlineSurvivesSystemSleep() throws {
        let system = FakeSystem()
        let (session, log) = try makeSession(system, timeout: 3_600)
        try session.start()

        // Machine slept for hours; dispatch time never advanced.
        system.monotonic = 50_000
        session.tick()

        #expect(session.state == .stopped)
        #expect(log.events.last == .stopped(.timeoutReached))
    }

    @Test("a nil timeout never expires")
    func nilTimeoutNeverExpires() throws {
        let system = FakeSystem()
        let (session, _) = try makeSession(system, timeout: nil)
        try session.start()

        system.monotonic = 1_000_000
        session.tick()

        #expect(session.state != .stopped)
    }

    // MARK: - Reconfigure

    @Test("reconfigure preserves startedAt and activityCount on a live session")
    func reconfigurePreservesRunState() throws {
        let system = FakeSystem()
        system.idle = 90
        let (session, _) = try makeSession(system)
        try session.start()
        session.tick()
        let before = try #require(runState(session))

        session.reconfigure(try YolkConfig(interval: 15, threshold: 45))

        let after = try #require(runState(session))
        #expect(after.startedAt == before.startedAt)
        #expect(after.activityCount == before.activityCount)
        #expect(session.config.interval == 15)
        #expect(session.config.threshold == 45)
    }

    @Test("a changed timeout restarts the countdown from now")
    func reconfigureRestartsCountdown() throws {
        let system = FakeSystem()
        let (session, _) = try makeSession(system, timeout: 3_600)
        try session.start()

        system.monotonic = 1_800
        session.reconfigure(try YolkConfig(timeout: 7_200))

        // Original deadline was 3600; it must no longer apply.
        system.monotonic = 3_601
        session.tick()
        #expect(session.state != .stopped)

        // New deadline is 1800 + 7200.
        system.monotonic = 9_000
        session.tick()
        #expect(session.state == .stopped)
    }

    @Test("reconfiguring to no timeout cancels the deadline and keeps running")
    func reconfigureToNilTimeoutCancelsDeadline() throws {
        let system = FakeSystem()
        let (session, _) = try makeSession(system, timeout: 60)
        try session.start()

        session.reconfigure(try YolkConfig(timeout: nil))

        system.monotonic = 100_000
        session.tick()

        #expect(session.state != .stopped)
    }

    @Test("reconfigure while stopped just stores the config")
    func reconfigureWhileStopped() throws {
        let system = FakeSystem()
        let (session, _) = try makeSession(system)

        session.reconfigure(try YolkConfig(interval: 90))

        #expect(session.state == .stopped)
        #expect(session.config.interval == 90)
    }

    // MARK: - Display sleep

    @Test("warns at start when the display would sleep before yolk could act")
    func warnsWhenDisplaySleepsTooSoon() throws {
        let system = FakeSystem()
        system.displaySleep = 60  // threshold 60 + interval 30 + 5 exceeds this
        let (session, log) = try makeSession(system)

        try session.start()

        #expect(log.count(of: .displaySleepTooSoon(60)) == 1)
    }

    @Test("stays quiet when display sleep is comfortably longer than the check window")
    func noWarningWhenDisplaySleepIsLong() throws {
        let system = FakeSystem()
        system.displaySleep = 1_800
        let (session, log) = try makeSession(system)

        try session.start()

        #expect(!log.events.contains { if case .displaySleepTooSoon = $0 { true } else { false } })
    }
}
