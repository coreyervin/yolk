import Foundation
import Testing

@testable import YolkKit
@testable import yolk

/// Collects what the renderer wrote, so parity can be diffed rather than eyeballed.
final class OutputSpy {
    var out = ""
    var err = ""

    /// stdout and stderr as one stream, in emission order — this is what a
    /// user redirecting `2>&1` sees, and how the goldens were captured.
    var combined = ""

    func writeOut(_ s: String) {
        out += s + "\n"
        combined += s + "\n"
    }

    func writeErr(_ s: String) {
        err += s + "\n"
        combined += s + "\n"
    }
}


/// Golden-file plumbing, deliberately outside any actor so every suite can use it.
enum GoldenFile {
    /// Repo root, found relative to this file — the Goldens live outside any
    /// SwiftPM target so they can be diffed against the real binary too.
    static var directory: URL {
        URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()  // YolkCLITests
            .deletingLastPathComponent()  // Tests
            .deletingLastPathComponent()  // repo root
            .appendingPathComponent("Goldens")
    }

    static func read(_ name: String) throws -> String {
        try String(contentsOf: directory.appendingPathComponent(name), encoding: .utf8)
    }

    /// Loads a golden and substitutes the machine-dependent tokens, so the
    /// expectation is timezone- and pid-independent.
    static func expected(_ name: String, pid: Int32, autoExit: Date? = nil) throws -> String {
        var text = try read(name)
        text = text.replacingOccurrences(of: "%PID%", with: String(pid))
        if let autoExit {
            text = text.replacingOccurrences(
                of: "%AUTOEXIT%", with: timeString(autoExit, includingDate: false))
        }
        return text
    }

    /// Mirrors the frozen CLI's formatter: POSIX locale, machine-local zone.
    static func timeString(_ date: Date, includingDate: Bool) -> String {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = includingDate ? "yyyy-MM-dd HH:mm:ss" : "HH:mm:ss"
        return f.string(from: date)
    }
}

@MainActor
@Suite("ConsoleRenderer")
struct ConsoleRendererTests {
    // Pinned so nothing in the rendered output depends on the machine.
    static let pinnedPID: Int32 = 4242
    static let pinnedNow = Date(timeIntervalSince1970: 1_787_000_000)
    static let fixedTip = "yolk --help lists every option."

    static func expected(_ name: String, autoExit: Date? = nil) throws -> String {
        try GoldenFile.expected(name, pid: pinnedPID, autoExit: autoExit)
    }

    static func makeRenderer(
        verbose: Bool = false,
        invokedBare: Bool = false,
        tip: String? = fixedTip,
        now: @escaping () -> Date = { pinnedNow }
    ) -> (ConsoleRenderer, OutputSpy) {
        let spy = OutputSpy()
        let renderer = ConsoleRenderer(
            verbose: verbose,
            invokedBare: invokedBare,
            pid: pinnedPID,
            now: now,
            tipPicker: { tip },
            out: spy.writeOut,
            err: spy.writeErr)
        return (renderer, spy)
    }

    // MARK: - Startup banner parity

    @Test("a bare invocation reproduces the banner with its tip")
    func bareBanner() throws {
        let (renderer, spy) = Self.makeRenderer(invokedBare: true)
        renderer.handle(.started(try YolkConfig()))
        renderer.renderStartupTail()
        #expect(spy.out == (try Self.expected("cli-banner-bare.txt")))
        #expect(spy.err.isEmpty)
    }

    @Test("a bare invocation with no tip available prints no tip block")
    func bannerWithoutTip() throws {
        let (renderer, spy) = Self.makeRenderer(invokedBare: true, tip: nil)
        renderer.handle(.started(try YolkConfig()))
        renderer.renderStartupTail()
        #expect(spy.out == (try Self.expected("cli-banner-notip.txt")))
    }

    @Test("a flagged invocation emits no tip even when one is available")
    func flaggedBannerSuppressesTip() throws {
        let (renderer, spy) = Self.makeRenderer(invokedBare: false)
        renderer.handle(.started(try YolkConfig(timeout: 5)))
        renderer.renderStartupTail()
        let expected = try Self.expected(
            "cli-banner-flagged.txt", autoExit: Self.pinnedNow.addingTimeInterval(5))
        #expect(spy.out == expected)
    }

    @Test("the banner reports the configured threshold and interval as whole seconds")
    func bannerReflectsConfig() throws {
        let (renderer, spy) = Self.makeRenderer()
        renderer.handle(.started(try YolkConfig(interval: 5, threshold: 300)))
        #expect(spy.out.contains("simulating activity when idle ≥ 300s (checking every 5s)"))
    }

    /// A bare time of day reads as "today", which is wrong for a multi-day run.
    @Test("a day-plus timeout includes the date in the auto-exit line")
    func dayPlusTimeoutShowsDate() throws {
        let (renderer, spy) = Self.makeRenderer()
        renderer.handle(.started(try YolkConfig(timeout: 86400)))
        let expected = GoldenFile.timeString(
            Self.pinnedNow.addingTimeInterval(86400), includingDate: true)
        #expect(spy.out.contains("  • auto-exit at \(expected)"))
    }

    @Test("a sub-day timeout shows only the time of day")
    func subDayTimeoutOmitsDate() throws {
        let (renderer, spy) = Self.makeRenderer()
        renderer.handle(.started(try YolkConfig(timeout: 3600)))
        let expected = GoldenFile.timeString(
            Self.pinnedNow.addingTimeInterval(3600), includingDate: false)
        #expect(spy.out.contains("  • auto-exit at \(expected)"))
        #expect(!spy.out.contains("auto-exit at 20"))
    }

    @Test("no timeout means no auto-exit line")
    func noTimeoutNoAutoExitLine() throws {
        let (renderer, spy) = Self.makeRenderer()
        renderer.handle(.started(try YolkConfig()))
        #expect(!spy.out.contains("auto-exit"))
    }

    // MARK: - Verbose tick logging

    @Test("verbose logs a below-threshold check as real activity")
    func verboseBelowThreshold() throws {
        let (renderer, spy) = Self.makeRenderer(verbose: true)
        renderer.handle(.started(try YolkConfig()))
        renderer.handle(.checked(idle: 59.7))
        let stamp = GoldenFile.timeString(Self.pinnedNow, includingDate: false)
        #expect(spy.out.hasSuffix("[\(stamp)] idle 59s — real activity, nothing to do\n"))
    }

    @Test("quiet mode logs no per-tick lines")
    func quietLogsNoTicks() throws {
        let (renderer, spy) = Self.makeRenderer(verbose: false)
        renderer.handle(.started(try YolkConfig()))
        let afterBanner = spy.out
        renderer.handle(.checked(idle: 12))
        renderer.handle(.checked(idle: 90))
        renderer.handle(.activitySimulated(total: 1))
        renderer.handle(.paused(.screenLocked))
        renderer.handle(.resumed)
        #expect(spy.out == afterBanner)
    }

    /// The idle figure on the simulated line is the one from the check that
    /// triggered it, not a fresh reading — after posting, idle is ~0.
    @Test("the simulated-activity line reuses the idle value that triggered it")
    func simulatedLineReusesTriggeringIdle() throws {
        let (renderer, spy) = Self.makeRenderer(verbose: true)
        renderer.handle(.started(try YolkConfig()))
        renderer.handle(.checked(idle: 61.2))
        renderer.handle(.activitySimulated(total: 7))
        let stamp = GoldenFile.timeString(Self.pinnedNow, includingDate: false)
        #expect(spy.out.hasSuffix("[\(stamp)] idle 61s — simulated activity (#7)\n"))
    }

    @Test("an at-or-above-threshold check logs nothing on its own")
    func aboveThresholdCheckIsSilent() throws {
        let (renderer, spy) = Self.makeRenderer(verbose: true)
        renderer.handle(.started(try YolkConfig()))
        let afterBanner = spy.out
        renderer.handle(.checked(idle: 60))
        #expect(spy.out == afterBanner)
    }

    @Test("verbose reports a pause with the frozen wording", arguments: [
        PauseReason.screenLocked, .sessionSwitchedOut, .sessionUnknown,
    ])
    func pauseWording(reason: PauseReason) throws {
        let (renderer, spy) = Self.makeRenderer(verbose: true)
        renderer.handle(.started(try YolkConfig()))
        renderer.handle(.paused(reason))
        let stamp = GoldenFile.timeString(Self.pinnedNow, includingDate: false)
        #expect(spy.out.hasSuffix("[\(stamp)] screen locked or session inactive — paused\n"))
    }

    /// The frozen CLI had no resume line — the next `idle Ns` tick is the only
    /// signal it is working again. Adding one would change observable output.
    @Test("resuming prints nothing")
    func resumePrintsNothing() throws {
        let (renderer, spy) = Self.makeRenderer(verbose: true)
        renderer.handle(.started(try YolkConfig()))
        let afterBanner = spy.out
        renderer.handle(.resumed)
        #expect(spy.out == afterBanner)
    }

    // MARK: - Warnings, always on stderr

    @Test("a missing Accessibility grant is reported on stderr")
    func permissionMissingWarning() throws {
        let (renderer, spy) = Self.makeRenderer()
        renderer.handle(.permissionMissing)
        #expect(spy.err == """
            ⚠ Simulated activity had no effect — Slack may still mark you away.
              Grant Accessibility permission to your terminal app in
              System Settings → Privacy & Security → Accessibility, then restart yolk.
              (If running inside tmux, grant it to tmux's responsible process.)

            """)
        #expect(spy.out.isEmpty)
    }

    @Test("an unconstructable event is reported on stderr")
    func postFailedWarning() throws {
        let (renderer, spy) = Self.makeRenderer()
        renderer.handle(.postFailed)
        #expect(spy.err == "⚠ Could not construct a synthetic input event — Slack may mark you away. Is the window server reachable?\n")
    }

    @Test("the display-sleep warning states both the sleep delay and yolk's worst case")
    func displaySleepWarning() throws {
        let (renderer, spy) = Self.makeRenderer()
        renderer.handle(.started(try YolkConfig(interval: 30, threshold: 60)))
        renderer.handle(.displaySleepTooSoon(120))
        #expect(spy.err == """
            ⚠ Your display sleeps after 2 min — sooner than yolk would act
              with these settings (up to 95s of idle). Once the display sleeps
              and locks, yolk pauses and Slack will mark you away. Lower --threshold/
              --interval, or raise display sleep in System Settings → Lock Screen.

            """)
    }

    @Test("warnings are not gated on verbose")
    func warningsIgnoreVerbose() throws {
        let (renderer, spy) = Self.makeRenderer(verbose: false)
        renderer.handle(.permissionMissing)
        renderer.handle(.postFailed)
        #expect(!spy.err.isEmpty)
    }

    // MARK: - Shutdown summary

    @Test("the timeout summary reports elapsed minutes and the activity count")
    func timeoutSummary() throws {
        var clock = Self.pinnedNow
        let (renderer, spy) = Self.makeRenderer(now: { clock })
        renderer.handle(.started(try YolkConfig(timeout: 3600)))
        renderer.handle(.activitySimulated(total: 1))
        renderer.handle(.activitySimulated(total: 2))
        clock = Self.pinnedNow.addingTimeInterval(3600)
        renderer.handle(.stopped(.timeoutReached))
        #expect(spy.out.hasSuffix("""

            yolk: timeout reached — ran 60 min, simulated activity 2 times. Sleep settings restored.

            """))
    }

    @Test("a user stop reads 'stopped' unless a signal renamed it")
    func userStopDefaultWording() throws {
        let (renderer, spy) = Self.makeRenderer()
        renderer.handle(.started(try YolkConfig()))
        renderer.handle(.stopped(.userRequested))
        #expect(spy.out.hasSuffix(
            "\nyolk: stopped — ran 0 min, simulated activity 0 times. Sleep settings restored.\n"))
    }

    @Test("signal handlers supply their own wording", arguments: [
        "terminated", "terminal closed",
    ])
    func signalSuppliedWording(reason: String) throws {
        let (renderer, spy) = Self.makeRenderer()
        renderer.handle(.started(try YolkConfig()))
        renderer.userStopReason = reason
        renderer.handle(.stopped(.userRequested))
        #expect(spy.out.hasSuffix(
            "\nyolk: \(reason) — ran 0 min, simulated activity 0 times. Sleep settings restored.\n"))
    }

    @Test("a single simulated event is not pluralised")
    func singularActivityCount() throws {
        let (renderer, spy) = Self.makeRenderer()
        renderer.handle(.started(try YolkConfig()))
        renderer.handle(.activitySimulated(total: 1))
        renderer.handle(.stopped(.userRequested))
        #expect(spy.out.contains("simulated activity 1 time."))
    }

    @Test("elapsed minutes truncate rather than round")
    func elapsedTruncates() throws {
        var clock = Self.pinnedNow
        let (renderer, spy) = Self.makeRenderer(now: { clock })
        renderer.handle(.started(try YolkConfig()))
        clock = Self.pinnedNow.addingTimeInterval(119)
        renderer.handle(.stopped(.userRequested))
        #expect(spy.out.contains("ran 1 min,"))
    }

    /// The frozen CLI printed the display-sleep warning *between* the banner
    /// and the tip. Folding the tip back into `.started` would reorder them for
    /// anyone redirecting `2>&1`, which is how the goldens were captured.
    @Test("the display-sleep warning lands between the banner and the tip")
    func warningInterleavesBeforeTip() throws {
        let (renderer, spy) = Self.makeRenderer(invokedBare: true)
        renderer.handle(.started(try YolkConfig(interval: 30, threshold: 60)))
        renderer.handle(.displaySleepTooSoon(120))
        renderer.renderStartupTail()

        let bannerEnd = try #require(spy.combined.range(of: "yolk pauses while locked"))
        let warning = try #require(spy.combined.range(of: "⚠ Your display sleeps after 2 min"))
        let tip = try #require(spy.combined.range(of: "💡 Did you know?"))
        let hint = try #require(spy.combined.range(of: "Press Ctrl-C to stop."))
        #expect(bannerEnd.upperBound < warning.lowerBound)
        #expect(warning.upperBound < tip.lowerBound)
        #expect(tip.upperBound < hint.lowerBound)
    }

    /// Printed before the banner, when the grant is missing and macOS is about
    /// to raise its own prompt.
    @Test("the Accessibility notice matches the frozen wording")
    func permissionRequestNotice() {
        let (renderer, spy) = Self.makeRenderer()
        renderer.renderPermissionRequestNotice()
        #expect(spy.err == """
            ⚠ yolk needs Accessibility permission to simulate activity for Slack.
              macOS should prompt you now — if not, add your terminal app under
              System Settings → Privacy & Security → Accessibility, then restart yolk.
              (Keeping the Mac awake works either way; only Slack presence needs this.)

            """)
        #expect(spy.out.isEmpty)
    }
}
