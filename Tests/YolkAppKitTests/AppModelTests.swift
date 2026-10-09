import Foundation
import Testing
import YolkKit
import YolkTestSupport

@testable import YolkAppKit

@MainActor
@Suite("AppModel")
struct AppModelTests {
    static func make(
        _ fake: FakeSystem = FakeSystem(), store: SettingsStore = .ephemeral()
    ) -> AppModel {
        // Always a fake login-item service: the default is `.live`, which would
        // reach real SMAppService and read this machine's actual registration.
        AppModel(
            environment: fake.environment, defaults: store,
            loginItems: FakeLoginItems().service)
    }

    // MARK: - Lifecycle

    /// "Launch at Login" means "have Yolk available at login", not "be awake
    /// from boot" — opening the app must not start nudging on its own.
    @Test("the app starts idle")
    func startsIdle() {
        let fake = FakeSystem()
        let model = Self.make(fake)
        #expect(model.isActive == false)
        #expect(fake.recorder.created.isEmpty)
    }

    @Test("activating holds a power assertion")
    func activatingHoldsAssertion() {
        let fake = FakeSystem()
        let model = Self.make(fake)
        model.setActive(true)
        #expect(model.isActive)
        #expect(fake.recorder.created.count == 1)
    }

    @Test("deactivating releases the assertion")
    func deactivatingReleasesAssertion() {
        let fake = FakeSystem()
        let model = Self.make(fake)
        model.setActive(true)
        model.setActive(false)
        #expect(model.isActive == false)
        #expect(fake.recorder.released.count == 1)
    }

    @Test("activating twice is idempotent")
    func activatingTwiceIsIdempotent() {
        let fake = FakeSystem()
        let model = Self.make(fake)
        model.setActive(true)
        model.setActive(true)
        #expect(fake.recorder.created.count == 1)
    }

    @Test("toggle flips whichever way it is currently pointing")
    func toggleFlips() {
        let model = Self.make()
        model.toggle()
        #expect(model.isActive)
        model.toggle()
        #expect(model.isActive == false)
    }

    /// A power assertion that outlives the session would keep the Mac awake
    /// with nothing on screen saying so.
    @Test("a timeout that fires returns the app to idle")
    func timeoutReturnsToIdle() {
        let fake = FakeSystem()
        let model = Self.make(fake)
        model.stopAfter = .minutes30
        model.setActive(true)
        fake.monotonic += 1800
        model.tickForTesting()
        #expect(model.isActive == false)
        #expect(fake.recorder.released.count == 1)
    }

    // MARK: - Observable state

    @Test("the nudge count follows the session")
    func nudgeCountFollowsSession() {
        let fake = FakeSystem()
        let model = Self.make(fake)
        model.setActive(true)
        fake.idle = 120
        model.tickForTesting()
        model.tickForTesting()
        #expect(model.activityCount == 2)
    }

    @Test("locking the screen surfaces a pause reason")
    func lockingSurfacesPauseReason() {
        let fake = FakeSystem()
        let model = Self.make(fake)
        model.setActive(true)
        fake.console = .locked
        model.tickForTesting()
        #expect(model.pauseReason == .screenLocked)
        #expect(model.isActive, "a paused session is still active, just not nudging")
    }

    @Test("unlocking clears the pause reason")
    func unlockingClearsPauseReason() {
        let fake = FakeSystem()
        let model = Self.make(fake)
        model.setActive(true)
        fake.console = .locked
        model.tickForTesting()
        fake.console = .active
        model.tickForTesting()
        #expect(model.pauseReason == nil)
    }

    @Test("going idle clears the session's statistics")
    func deactivatingClearsStatistics() {
        let fake = FakeSystem()
        let model = Self.make(fake)
        model.setActive(true)
        fake.idle = 120
        model.tickForTesting()
        model.setActive(false)
        #expect(model.activityCount == 0)
        #expect(model.startedAt == nil)
        #expect(model.pauseReason == nil)
    }

    @Test("uptime reads as hours and minutes", arguments: [
        (0.0, "0m"), (59.0, "0m"), (60.0, "1m"), (3599.0, "59m"),
        (3600.0, "1h 0m"), (8040.0, "2h 14m"), (86_400.0, "24h 0m"),
    ])
    func uptimeDescription(elapsed: TimeInterval, expected: String) {
        let fake = FakeSystem()
        let model = Self.make(fake)
        model.setActive(true)
        let started = try! #require(model.startedAt)
        #expect(model.uptimeDescription(asOf: started.addingTimeInterval(elapsed)) == expected)
    }

    @Test("uptime is nil while idle")
    func uptimeNilWhileIdle() {
        let model = Self.make()
        #expect(model.uptimeDescription(asOf: Date()) == nil)
    }

    // MARK: - Settings persistence

    @Test("an empty store yields the documented defaults")
    func defaultsFromEmptyStore() {
        let model = Self.make()
        #expect(model.interval == 30)
        #expect(model.threshold == 60)
        #expect(model.stopAfter == .never)
    }

    @Test("settings survive a relaunch")
    func settingsPersist() {
        let store = SettingsStore.ephemeral()
        let first = Self.make(FakeSystem(), store: store)
        first.interval = 15
        first.threshold = 90
        first.stopAfter = .hours4

        let second = Self.make(FakeSystem(), store: store)
        #expect(second.interval == 15)
        #expect(second.threshold == 90)
        #expect(second.stopAfter == .hours4)
    }

    /// Preferences can be edited by hand, or written by an older build with
    /// different bounds. Out-of-range values must not make the app unusable.
    @Test("out-of-range stored settings fall back to the defaults", arguments: [
        (0.0, 30.0), (-5.0, 30.0), (1000.0, 30.0),
    ])
    func corruptStoredIntervalFallsBack(stored: Double, expected: Double) {
        let store = SettingsStore.ephemeral()
        store.writeDouble("interval", stored)
        #expect(Self.make(FakeSystem(), store: store).interval == expected)
    }

    @Test("an unrecognised stored stop-after falls back to never")
    func corruptStoredStopAfterFallsBack() {
        let store = SettingsStore.ephemeral()
        store.writeString("stopAfter", "fortnight")
        #expect(Self.make(FakeSystem(), store: store).stopAfter == .never)
    }

    @Test("assigning an out-of-range setting clamps it into range")
    func assignmentClamps() {
        let model = Self.make()
        model.interval = 9999
        #expect(model.interval == YolkConfig.intervalRange.upperBound)
        model.threshold = 0
        #expect(model.threshold == YolkConfig.thresholdRange.lowerBound)
    }

    // MARK: - Live reconfiguration

    /// The settings controls sit next to a live status readout, so resetting
    /// the statistics on every keystroke would be visibly wrong.
    @Test("changing interval mid-session preserves the statistics")
    func reconfigurePreservesStatistics() {
        let fake = FakeSystem()
        let model = Self.make(fake)
        model.setActive(true)
        fake.idle = 120
        model.tickForTesting()
        let startedAt = model.startedAt

        model.interval = 15

        #expect(model.isActive)
        #expect(model.activityCount == 1)
        #expect(model.startedAt == startedAt)
        #expect(fake.recorder.released.isEmpty, "the assertion must not be cycled")
    }

    @Test("changing threshold mid-session takes effect immediately")
    func thresholdAppliesImmediately() {
        let fake = FakeSystem()
        let model = Self.make(fake)
        model.setActive(true)
        fake.idle = 45
        model.tickForTesting()
        #expect(model.activityCount == 0, "45s is below the default 60s threshold")

        model.threshold = 30
        model.tickForTesting()
        #expect(model.activityCount == 1)
    }

    @Test("changing settings while idle just stores them")
    func idleReconfigureOnlyStores() {
        let fake = FakeSystem()
        let model = Self.make(fake)
        model.interval = 15
        #expect(model.isActive == false)
        #expect(fake.recorder.created.isEmpty)
    }

    // MARK: - Stop after

    @Test("stop-after maps to the documented durations", arguments: [
        (AppModel.StopAfter.never, TimeInterval?.none),
        (.minutes30, .some(1800)),
        (.hour1, .some(3600)),
        (.hours4, .some(14400)),
        (.hours8, .some(28800)),
    ])
    func stopAfterDurations(option: AppModel.StopAfter, timeout: TimeInterval?) {
        #expect(option.timeout == timeout)
    }

    @Test("every stop-after option is offered, never first")
    func stopAfterMenuOrder() {
        #expect(AppModel.StopAfter.allCases == [.never, .minutes30, .hour1, .hours4, .hours8])
        #expect(AppModel.StopAfter.never.title == "Never")
        #expect(AppModel.StopAfter.minutes30.title == "30 minutes")
        #expect(AppModel.StopAfter.hour1.title == "1 hour")
    }

    /// Picking "1 hour" two hours into a session means one more hour, not
    /// immediate expiry.
    @Test("changing stop-after mid-session restarts the countdown from now")
    func stopAfterRestartsCountdown() {
        let fake = FakeSystem()
        let model = Self.make(fake)
        model.setActive(true)

        fake.monotonic += 7200  // two hours in
        model.stopAfter = .hour1
        model.tickForTesting()
        #expect(model.isActive, "must not expire instantly")

        fake.monotonic += 3600  // one hour after the change
        model.tickForTesting()
        #expect(model.isActive == false)
    }

    @Test("selecting Never mid-session cancels the deadline and keeps running")
    func neverCancelsDeadline() {
        let fake = FakeSystem()
        let model = Self.make(fake)
        model.stopAfter = .minutes30
        model.setActive(true)

        model.stopAfter = .never
        fake.monotonic += 86_400
        model.tickForTesting()
        #expect(model.isActive)
    }

    // MARK: - System state

    @Test("the Accessibility grant is reported from the system")
    func accessibilityGrantReported() {
        let granted = AppModel(
            environment: .fake(hasPostPermission: { true }), defaults: .ephemeral(),
            loginItems: FakeLoginItems().service)
        let denied = AppModel(
            environment: .fake(hasPostPermission: { false }), defaults: .ephemeral(),
            loginItems: FakeLoginItems().service)
        #expect(granted.hasAccessibilityPermission)
        #expect(denied.hasAccessibilityPermission == false)
    }

    /// Shown inline in Settings when the display would sleep before yolk could
    /// ever act — the same condition the CLI warns about.
    @Test("the display-sleep warning appears only when sleep beats yolk to it")
    func displaySleepWarning() {
        let fake = FakeSystem()
        fake.displaySleep = 120  // 60 + 30 + 5 = 95 < 120, so no warning
        let model = Self.make(fake)
        #expect(model.displaySleepWarning == nil)

        fake.displaySleep = 90  // 95 >= 90
        model.refreshSystemState()
        #expect(model.displaySleepWarning == 90)
    }

    @Test("no display-sleep setting means no warning")
    func noDisplaySleepNoWarning() {
        let fake = FakeSystem()
        fake.displaySleep = nil
        #expect(Self.make(fake).displaySleepWarning == nil)
    }

    @Test("tightening the settings clears the display-sleep warning")
    func tighteningClearsWarning() {
        let fake = FakeSystem()
        fake.displaySleep = 90
        let model = Self.make(fake)
        #expect(model.displaySleepWarning == 90)
        model.threshold = 30
        model.interval = 10  // 30 + 10 + 5 = 45 < 90
        #expect(model.displaySleepWarning == nil)
    }
}

/// Stands in for `SMAppService.mainApp`, which cannot be exercised in a test.
final class FakeLoginItems: @unchecked Sendable {
    var enabled = false
    var inApplications = true
    var registerError: Error?
    var unregisterError: Error?
    private(set) var registerCalls = 0
    private(set) var unregisterCalls = 0

    struct Failure: Error {}

    var service: LoginItemService {
        LoginItemService(
            isEnabled: { [self] in enabled },
            register: { [self] in
                registerCalls += 1
                if let registerError { throw registerError }
                enabled = true
            },
            unregister: { [self] in
                unregisterCalls += 1
                if let unregisterError { throw unregisterError }
                enabled = false
            },
            isInApplicationsFolder: { [self] in inApplications })
    }
}

@MainActor
@Suite("AppModel — status presentation")
struct AppModelStatusTests {
    static func make(_ fake: FakeSystem = FakeSystem()) -> AppModel {
        AppModel(
            environment: fake.environment, defaults: .ephemeral(),
            loginItems: FakeLoginItems().service)
    }

    @Test("an idle Yolk says so and offers no detail")
    func idleStatus() {
        let model = Self.make()
        #expect(model.statusTitle == "Yolk is idle")
        #expect(model.statusDetail(asOf: Date()) == nil)
    }

    @Test("an active Yolk reports uptime and nudge count")
    func activeStatus() {
        let fake = FakeSystem()
        let model = Self.make(fake)
        model.setActive(true)
        fake.idle = 120
        model.tickForTesting()
        let started = try! #require(model.startedAt)
        #expect(model.statusTitle == "Yolk is active")
        #expect(
            model.statusDetail(asOf: started.addingTimeInterval(8040))
                == "Awake 2h 14m · 1 nudge")
    }

    @Test("the nudge count is pluralised", arguments: [
        (0, "no nudges"), (1, "1 nudge"), (2, "2 nudges"), (47, "47 nudges"),
    ])
    func nudgePluralisation(count: Int, phrase: String) {
        let fake = FakeSystem()
        let model = Self.make(fake)
        model.setActive(true)
        fake.idle = 120
        for _ in 0..<count { model.tickForTesting() }
        let started = try! #require(model.startedAt)
        #expect(model.statusDetail(asOf: started)?.hasSuffix("· \(phrase)") == true)
    }

    /// The menu says *why* it is paused; "not working" with no reason reads as
    /// a bug rather than as the deliberate walked-away behaviour.
    @Test("a pause names its reason", arguments: [
        (ConsoleState.locked, "Paused — screen locked"),
        (.switchedOut, "Paused — another user is using this Mac"),
        (.unknown, "Paused — session state unavailable"),
    ])
    func pausedStatus(console: ConsoleState, title: String) {
        let fake = FakeSystem()
        let model = Self.make(fake)
        model.setActive(true)
        fake.console = console
        model.tickForTesting()
        #expect(model.statusTitle == title)
    }

    @Test("a paused Yolk still reports its uptime")
    func pausedKeepsDetail() {
        let fake = FakeSystem()
        let model = Self.make(fake)
        model.setActive(true)
        fake.console = .locked
        model.tickForTesting()
        #expect(model.statusDetail(asOf: Date()) != nil)
    }

    /// A typo here fails silently — the deep link simply does nothing.
    @Test("the Accessibility deep link points at the right pane")
    func accessibilityDeepLink() {
        #expect(
            AppModel.accessibilitySettingsURL?.absoluteString
                == "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")
    }
}

@MainActor
@Suite("AppModel — launch at login")
struct AppModelLoginItemTests {
    static func make(_ items: FakeLoginItems) -> AppModel {
        AppModel(
            environment: FakeSystem().environment, defaults: .ephemeral(),
            loginItems: items.service)
    }

    @Test("the current registration state is read from the system, not stored")
    func readsSystemState() {
        let items = FakeLoginItems()
        items.enabled = true
        #expect(Self.make(items).launchAtLogin)
    }

    @Test("enabling registers the app")
    func enablingRegisters() {
        let items = FakeLoginItems()
        let model = Self.make(items)
        model.launchAtLogin = true
        #expect(items.registerCalls == 1)
        #expect(model.launchAtLogin)
        #expect(model.launchAtLoginProblem == nil)
    }

    @Test("disabling unregisters the app")
    func disablingUnregisters() {
        let items = FakeLoginItems()
        items.enabled = true
        let model = Self.make(items)
        model.launchAtLogin = false
        #expect(items.unregisterCalls == 1)
        #expect(model.launchAtLogin == false)
    }

    /// Registration genuinely fails outside /Applications. Silently reverting
    /// the toggle looks like a bug; the pane has to say why.
    @Test("running outside /Applications is detected up front, not on failure")
    func outsideApplicationsIsDetected() {
        let items = FakeLoginItems()
        items.inApplications = false
        let model = Self.make(items)
        #expect(model.canEnableLaunchAtLogin == false)
        #expect(model.launchAtLoginProblem != nil)
        #expect(model.launchAtLoginProblem?.contains("/Applications") == true)
    }

    @Test("being in /Applications reports no problem")
    func insideApplicationsIsFine() {
        let items = FakeLoginItems()
        items.inApplications = true
        let model = Self.make(items)
        #expect(model.canEnableLaunchAtLogin)
        #expect(model.launchAtLoginProblem == nil)
    }

    @Test("enabling from outside /Applications is refused without calling the system")
    func refusesOutsideApplications() {
        let items = FakeLoginItems()
        items.inApplications = false
        let model = Self.make(items)
        model.launchAtLogin = true
        #expect(items.registerCalls == 0)
        #expect(model.launchAtLogin == false)
    }

    @Test("a registration failure reverts the toggle and explains itself")
    func registrationFailureReverts() {
        let items = FakeLoginItems()
        items.registerError = FakeLoginItems.Failure()
        let model = Self.make(items)
        model.launchAtLogin = true
        #expect(items.registerCalls == 1)
        #expect(model.launchAtLogin == false, "must not claim to be enabled")
        #expect(model.launchAtLoginProblem != nil)
    }

    @Test("an unregistration failure leaves the toggle on and explains itself")
    func unregistrationFailureReverts() {
        let items = FakeLoginItems()
        items.enabled = true
        items.unregisterError = FakeLoginItems.Failure()
        let model = Self.make(items)
        model.launchAtLogin = false
        #expect(model.launchAtLogin, "must not claim to be disabled")
        #expect(model.launchAtLoginProblem != nil)
    }

    @Test("a later success clears an earlier problem")
    func successClearsProblem() {
        let items = FakeLoginItems()
        items.registerError = FakeLoginItems.Failure()
        let model = Self.make(items)
        model.launchAtLogin = true
        #expect(model.launchAtLoginProblem != nil)

        items.registerError = nil
        model.launchAtLogin = true
        #expect(model.launchAtLogin)
        #expect(model.launchAtLoginProblem == nil)
    }
}
