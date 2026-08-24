import Foundation
import YolkKit

/// Turns `YolkEvent`s into the CLI's frozen stdout/stderr output.
///
/// All presentation lives here: `--verbose` gating, the timestamp prefix, the
/// startup tip, and the shutdown summary. `YolkKit` emits the same events
/// regardless, which is what lets the menu bar app render them completely
/// differently without touching the engine.
@MainActor
final class ConsoleRenderer {
    /// Surfaced at startup on a bare invocation, to advertise options that are
    /// easy to miss. Any flag suppresses them — someone using flags has
    /// already found the help, and the tips would become nagging.
    static let tips = [
        "yolk -t 8h stops on its own after 8 hours. Also takes 90m, 45s, or 1d.",
        "yolk -v logs every idle check and simulated event, if you want to watch it work.",
        "yolk --threshold 30 nudges sooner, if Slack still slips to away on you.",
        "yolk -i 15 checks more often, for a tighter idle window.",
        "Lock with Ctrl-Cmd-Q when you step away — yolk pauses, so Slack shows you away honestly.",
        "yolk --help lists every option.",
    ]

    /// The word the shutdown summary uses for a user-requested stop. Signal
    /// handlers overwrite it — `StopReason.userRequested` cannot distinguish
    /// SIGINT from SIGTERM from SIGHUP, and the CLI has always named them
    /// differently.
    var userStopReason = "stopped"

    private let verbose: Bool
    private let invokedBare: Bool
    private let pid: Int32
    private let now: () -> Date
    private let tipPicker: () -> String?
    private let out: (String) -> Void
    private let err: (String) -> Void

    private var config = try? YolkConfig()
    private var startedAt: Date?
    private var activityCount = 0
    /// The idle reading from the most recent check, reused by the
    /// simulated-activity line — re-reading after posting would report ~0.
    private var lastCheckedIdle: TimeInterval = 0

    init(
        verbose: Bool,
        invokedBare: Bool,
        pid: Int32,
        now: @escaping () -> Date = { Date() },
        tipPicker: @escaping () -> String? = { ConsoleRenderer.tips.randomElement() },
        out: @escaping (String) -> Void,
        err: @escaping (String) -> Void
    ) {
        self.verbose = verbose
        self.invokedBare = invokedBare
        self.pid = pid
        self.now = now
        self.tipPicker = tipPicker
        self.out = out
        self.err = err
    }

    func handle(_ event: YolkEvent) {
        switch event {
        case .started(let config):
            self.config = config
            startedAt = now()
            renderBanner(config)
        case .checked(let idle):
            lastCheckedIdle = idle
            // At or above the threshold the engine goes on to simulate, and
            // the simulated line reports it — logging here too would double up.
            if idle < (config?.threshold ?? 0) {
                vlog("idle \(Int(idle))s — real activity, nothing to do")
            }
        case .activitySimulated(let total):
            activityCount = total
            vlog("idle \(Int(lastCheckedIdle))s — simulated activity (#\(total))")
        case .paused:
            // Deliberately reason-agnostic: the frozen CLI printed one line for
            // locked, switched-out, and unreadable sessions alike.
            vlog("screen locked or session inactive — paused")
        case .resumed:
            // The frozen CLI printed nothing here. The next `idle Ns` line is
            // the signal that work has resumed.
            break
        case .permissionMissing:
            err("""
                ⚠ Simulated activity had no effect — Slack may still mark you away.
                  Grant Accessibility permission to your terminal app in
                  System Settings → Privacy & Security → Accessibility, then restart yolk.
                  (If running inside tmux, grant it to tmux's responsible process.)
                """)
        case .postFailed:
            err(
                "⚠ Could not construct a synthetic input event — Slack may mark you away. Is the window server reachable?"
            )
        case .displaySleepTooSoon(let displaySleep):
            let worstCase = (config?.threshold ?? 0) + (config?.interval ?? 0) + 5
            err("""
                ⚠ Your display sleeps after \(Int(displaySleep / 60)) min — sooner than yolk would act
                  with these settings (up to \(Int(worstCase))s of idle). Once the display sleeps
                  and locks, yolk pauses and Slack will mark you away. Lower --threshold/
                  --interval, or raise display sleep in System Settings → Lock Screen.
                """)
        case .stopped(let reason):
            renderSummary(reason)
        }
    }

    /// Printed before the banner when the Accessibility grant is missing, on
    /// the way to macOS raising its own prompt. Not event-driven: the engine
    /// has no say in whether the CLI asks for permission.
    func renderPermissionRequestNotice() {
        err("""
            ⚠ yolk needs Accessibility permission to simulate activity for Slack.
              macOS should prompt you now — if not, add your terminal app under
              System Settings → Privacy & Security → Accessibility, then restart yolk.
              (Keeping the Mac awake works either way; only Slack presence needs this.)
            """)
    }

    /// The tail of the startup output: the tip, then the Ctrl-C hint.
    ///
    /// Split from `.started` because it must land *after* the display-sleep
    /// warning, which the engine emits as a separate event immediately after
    /// starting. The CLI calls this once `start()` has returned.
    func renderStartupTail() {
        if invokedBare, let tip = tipPicker() {
            out("\n  💡 Did you know? \(tip)\n")
        }
        out("Press Ctrl-C to stop.")
    }

    private func renderBanner(_ config: YolkConfig) {
        out("""
            yolk v\(YolkKit.version) (pid \(pid))
              ✓ system sleep prevented while running
              ✓ simulating activity when idle ≥ \(Int(config.threshold))s (checking every \(Int(config.interval))s)
              • lock your screen (Ctrl-Cmd-Q) when you walk away — yolk pauses while locked
            """)
        if let timeout = config.timeout {
            // Display only. The real deadline is monotonic and lives in the
            // engine; this line never decides anything.
            let estimatedEnd = now().addingTimeInterval(timeout)
            // For day-plus timeouts a bare time of day reads as "today".
            out("  • auto-exit at \(format(estimatedEnd, includingDate: timeout >= 86400))")
        }
    }

    private func renderSummary(_ reason: StopReason) {
        let word: String
        switch reason {
        case .timeoutReached: word = "timeout reached"
        case .userRequested: word = userStopReason
        }
        let elapsed = startedAt.map { now().timeIntervalSince($0) } ?? 0
        let minutes = Int(elapsed / 60)
        let plural = activityCount == 1 ? "" : "s"
        out(
            "\nyolk: \(word) — ran \(minutes) min, simulated activity \(activityCount) time\(plural). Sleep settings restored."
        )
    }

    private func vlog(_ message: String) {
        guard verbose else { return }
        out("[\(format(now(), includingDate: false))] \(message)")
    }

    private func format(_ date: Date, includingDate: Bool) -> String {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = includingDate ? "yyyy-MM-dd HH:mm:ss" : "HH:mm:ss"
        return f.string(from: date)
    }
}
