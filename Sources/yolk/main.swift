// yolk — keep your Mac awake and Slack active while agents run.
//
// Two mechanisms:
//   1. An IOKit power assertion (PreventUserIdleSystemSleep) stops the machine
//      from idle-sleeping, like `caffeinate -i`.
//   2. When system idle time crosses a threshold, posts a synthetic mouse-moved
//      event at the current cursor position. This resets the CoreGraphics
//      combined-session idle timer, which is exactly what Slack's Electron
//      powerMonitor reads to decide "away" — so you stay active without the
//      cursor ever visibly moving.
//
// While the screen is locked, activity simulation pauses: locking is the
// "I actually walked away" gesture (Slack goes away on lock regardless, and
// the display gets to sleep).

import CoreGraphics
import Foundation
import IOKit.pwr_mgt

let VERSION = "1.0.0"

// MARK: - Configuration

struct Config {
    var interval: TimeInterval = 30
    var threshold: TimeInterval = 60
    var timeout: TimeInterval?
    var verbose = false
}

let USAGE = """
yolk v\(VERSION) — keep your Mac awake and Slack active

usage: yolk [options]

options:
  -i, --interval <sec>    seconds between idle checks (default 30, 5–120)
      --threshold <sec>   idle seconds before simulating activity
                          (default 60, 5–300; capped so Slack's ~10 min
                          away timer can never be reached)
  -t, --timeout <dur>     exit automatically after a duration: 8h, 90m,
                          45s, 1d, or plain seconds (default: run until Ctrl-C)
  -v, --verbose           log every check and simulated event
  -h, --help              show this help
      --version           show version

notes:
  • Needs Accessibility permission for your terminal (yolk will prompt
    on first run) to post synthetic input events.
  • Lock your screen (Ctrl-Cmd-Q) when you walk away — yolk pauses while
    locked, so Slack correctly shows you away and the display can sleep.
  • Closing the lid still sleeps the Mac (same as caffeinate -i).
"""

// One of these is shown at startup when yolk is run bare, to surface options
// that are easy to miss. Passing any flag suppresses them — someone using
// flags has already found the help.
let TIPS = [
    "yolk -t 8h stops on its own after 8 hours. Also takes 90m, 45s, or 1d.",
    "yolk -v logs every idle check and simulated event, if you want to watch it work.",
    "yolk --threshold 30 nudges sooner, if Slack still slips to away on you.",
    "yolk -i 15 checks more often, for a tighter idle window.",
    "Lock with Ctrl-Cmd-Q when you step away — yolk pauses, so Slack shows you away honestly.",
    "yolk --help lists every option.",
]

func warn(_ msg: String) {
    FileHandle.standardError.write((msg + "\n").data(using: .utf8)!)
}

func die(_ msg: String) -> Never {
    FileHandle.standardError.write("yolk: \(msg)\nTry 'yolk --help'.\n".data(using: .utf8)!)
    exit(1)
}

/// Parses "8h", "90m", "45s", or bare seconds into a TimeInterval.
func parseDuration(_ s: String) -> TimeInterval? {
    let lower = s.lowercased()
    let multipliers: [Character: Double] = ["s": 1, "m": 60, "h": 3600, "d": 86400]
    var numPart = lower
    var mult = 1.0
    if let last = lower.last, let m = multipliers[last] {
        numPart = String(lower.dropLast())
        mult = m
    }
    guard let n = Double(numPart), n.isFinite, n > 0 else { return nil }
    return n * mult
}

func parseArgs(_ args: [String]) -> Config {
    var config = Config()
    var i = 0
    func value(for flag: String) -> String {
        i += 1
        guard i < args.count else { die("\(flag) requires a value") }
        return args[i]
    }
    while i < args.count {
        let arg = args[i]
        switch arg {
        case "-i", "--interval":
            guard let n = Double(value(for: arg)), n >= 5, n <= 120 else {
                die("--interval must be 5–120 seconds")
            }
            config.interval = n
        case "--threshold":
            guard let n = Double(value(for: arg)), n >= 5, n <= 300 else {
                die("--threshold must be 5–300 seconds (above 300 Slack could mark you away)")
            }
            config.threshold = n
        case "-t", "--timeout":
            guard let d = parseDuration(value(for: arg)) else {
                die("invalid duration '\(args[i])' — use e.g. 8h, 90m, 45s, or seconds")
            }
            config.timeout = d
        case "-v", "--verbose":
            config.verbose = true
        case "-h", "--help":
            print(USAGE)
            exit(0)
        case "--version":
            print("yolk v\(VERSION)")
            exit(0)
        default:
            die("unknown option '\(arg)'")
        }
        i += 1
    }
    return config
}

// MARK: - System helpers

// kCGAnyInputEventType isn't surfaced to Swift; it's defined as ((CGEventType)(~0)).
let anyInputEventType = CGEventType(rawValue: ~0)!

func idleSeconds() -> TimeInterval {
    CGEventSource.secondsSinceLastEventType(.combinedSessionState, eventType: anyInputEventType)
}

/// True when simulation should pause: screen locked, session switched out
/// (fast user switching), or session state unreadable. Fails safe — never
/// post events into a session that isn't ours and visibly unlocked.
func simulationPaused() -> Bool {
    guard let session = CGSessionCopyCurrentDictionary() as? [String: Any] else { return true }
    let locked = (session["CGSSessionScreenIsLocked"] as? NSNumber)?.boolValue ?? false
    let onConsole = (session[kCGSessionOnConsoleKey as String] as? NSNumber)?.boolValue ?? false
    return locked || !onConsole
}

/// The display-sleep timeout from pmset, in seconds (nil if disabled or unreadable).
func displaySleepSeconds() -> TimeInterval? {
    let process = Process()
    process.executableURL = URL(fileURLWithPath: "/usr/bin/pmset")
    process.arguments = ["-g"]
    let pipe = Pipe()
    process.standardOutput = pipe
    process.standardError = FileHandle.nullDevice
    guard (try? process.run()) != nil else { return nil }
    let data = pipe.fileHandleForReading.readDataToEndOfFile()
    process.waitUntilExit()
    guard let output = String(data: data, encoding: .utf8) else { return nil }
    for line in output.split(separator: "\n") {
        let parts = line.split(separator: " ", omittingEmptySubsequences: true)
        if parts.first == "displaysleep", parts.count >= 2, let minutes = Double(parts[1]) {
            return minutes > 0 ? minutes * 60 : nil
        }
    }
    return nil
}

/// Posts a mouse-moved event at the cursor's current position — invisible,
/// but resets the system idle timer. Returns false if the event couldn't be built.
func postSyntheticActivity() -> Bool {
    guard let location = CGEvent(source: nil)?.location,
          let move = CGEvent(mouseEventSource: nil, mouseType: .mouseMoved,
                             mouseCursorPosition: location, mouseButton: .left)
    else { return false }
    move.post(tap: .cghidEventTap)
    return true
}

// MARK: - Startup

let arguments = Array(CommandLine.arguments.dropFirst())
let config = parseArgs(arguments)
let invokedBare = arguments.isEmpty
let startDate = Date()

// The timeout is anchored to CLOCK_MONOTONIC — on Darwin it keeps counting
// across system sleep but is immune to wall-clock steps (NTP, manual changes),
// so `-t 8h` means 8 real hours regardless of either.
func monotonicNow() -> TimeInterval {
    Double(clock_gettime_nsec_np(CLOCK_MONOTONIC)) / 1_000_000_000
}
let monotonicDeadline: TimeInterval? = config.timeout.map { monotonicNow() + $0 }
let estimatedDeadline: Date? = config.timeout.map { startDate.addingTimeInterval($0) }
var activityCount = 0
var warnedNoPermission = false
var warnedPostFailure = false

setvbuf(stdout, nil, _IOLBF, 0)

let timeFormatter: DateFormatter = {
    let f = DateFormatter()
    f.locale = Locale(identifier: "en_US_POSIX")
    f.dateFormat = "HH:mm:ss"
    return f
}()

func vlog(_ msg: String) {
    if config.verbose { print("[\(timeFormatter.string(from: Date()))] \(msg)") }
}

// Keep the process out of App Nap so the timer isn't throttled.
let activityToken = ProcessInfo.processInfo.beginActivity(
    options: .userInitiatedAllowingIdleSystemSleep, reason: "yolk keeping system active")

var assertionID: IOPMAssertionID = 0
let assertionResult = IOPMAssertionCreateWithName(
    "PreventUserIdleSystemSleep" as CFString,
    IOPMAssertionLevel(kIOPMAssertionLevelOn),
    "yolk: keeping Mac awake while agents run" as CFString,
    &assertionID)
guard assertionResult == kIOReturnSuccess else {
    die("failed to create power assertion (IOReturn \(assertionResult))")
}

func shutdown(_ reason: String) -> Never {
    IOPMAssertionRelease(assertionID)
    ProcessInfo.processInfo.endActivity(activityToken)
    let minutes = Int(Date().timeIntervalSince(startDate) / 60)
    print("\nyolk: \(reason) — ran \(minutes) min, simulated activity \(activityCount) time\(activityCount == 1 ? "" : "s"). Sleep settings restored.")
    exit(0)
}

if !CGPreflightPostEventAccess() {
    warn("""
    ⚠ yolk needs Accessibility permission to simulate activity for Slack.
      macOS should prompt you now — if not, add your terminal app under
      System Settings → Privacy & Security → Accessibility, then restart yolk.
      (Keeping the Mac awake works either way; only Slack presence needs this.)
    """)
    CGRequestPostEventAccess()
}

print("""
yolk v\(VERSION) (pid \(ProcessInfo.processInfo.processIdentifier))
  ✓ system sleep prevented while running
  ✓ simulating activity when idle ≥ \(Int(config.threshold))s (checking every \(Int(config.interval))s)
  • lock your screen (Ctrl-Cmd-Q) when you walk away — yolk pauses while locked
""")
if let estimatedDeadline, let timeout = config.timeout {
    // For day-plus timeouts a bare time of day reads as "today" — include the date.
    let fullFormatter = DateFormatter()
    fullFormatter.locale = Locale(identifier: "en_US_POSIX")
    fullFormatter.dateFormat = timeout >= 86400 ? "yyyy-MM-dd HH:mm:ss" : "HH:mm:ss"
    print("  • auto-exit at \(fullFormatter.string(from: estimatedDeadline))")
}
// Simulation fires, worst case, after threshold + interval + timer leeway of
// idle. If the display sleeps sooner than that, the screen locks first and
// yolk pauses forever — warn up front rather than fail silently.
if let displaySleep = displaySleepSeconds(),
   config.threshold + config.interval + 5 >= displaySleep {
    warn("""
    ⚠ Your display sleeps after \(Int(displaySleep / 60)) min — sooner than yolk would act
      with these settings (up to \(Int(config.threshold + config.interval + 5))s of idle). Once the display sleeps
      and locks, yolk pauses and Slack will mark you away. Lower --threshold/
      --interval, or raise display sleep in System Settings → Lock Screen.
    """)
}
if invokedBare, let tip = TIPS.randomElement() {
    print("\n  💡 Did you know? \(tip)\n")
}
print("Press Ctrl-C to stop.")

// MARK: - Main loop

func tick() {
    if let monotonicDeadline, monotonicNow() >= monotonicDeadline {
        shutdown("timeout reached")
    }
    if simulationPaused() {
        vlog("screen locked or session inactive — paused")
        return
    }
    let idle = idleSeconds()
    guard idle >= config.threshold else {
        vlog("idle \(Int(idle))s — real activity, nothing to do")
        return
    }
    guard postSyntheticActivity() else {
        if !warnedPostFailure {
            warnedPostFailure = true
            warn("⚠ Could not construct a synthetic input event — Slack may mark you away. Is the window server reachable?")
        }
        return
    }
    // Posting goes through the window server asynchronously; give it a moment,
    // then confirm the idle timer actually reset (it won't without permission).
    // A landed event leaves idle ≈ 0.2s, so < 1.0s is a robust "did it land"
    // check. Concurrent real input can false-pass it, which is acceptable:
    // real input means presence, and a missing permission still gets caught
    // on the next genuinely idle tick.
    usleep(200_000)
    if idleSeconds() < 1.0 {
        activityCount += 1
        vlog("idle \(Int(idle))s — simulated activity (#\(activityCount))")
    } else if !warnedNoPermission {
        warnedNoPermission = true
        warn("""
        ⚠ Simulated activity had no effect — Slack may still mark you away.
          Grant Accessibility permission to your terminal app in
          System Settings → Privacy & Security → Accessibility, then restart yolk.
          (If running inside tmux, grant it to tmux's responsible process.)
        """)
    }
}

// If stdout is a pipe whose reader exits (yolk -v | head), a write must not
// kill the process silently.
signal(SIGPIPE, SIG_IGN)

func trapSignal(_ sig: Int32, reason: String) -> DispatchSourceSignal? {
    // Honor an inherited ignore (e.g. nohup sets SIGHUP to SIG_IGN): a dispatch
    // signal source is kqueue-based and would still fire on an ignored signal,
    // silently defeating nohup.
    let previous = signal(sig, SIG_IGN)
    guard unsafeBitCast(previous, to: UInt.self) != unsafeBitCast(SIG_IGN, to: UInt.self) else {
        return nil
    }
    let source = DispatchSource.makeSignalSource(signal: sig, queue: .main)
    source.setEventHandler { shutdown(reason) }
    source.resume()
    return source
}
let signalSources = [
    trapSignal(SIGINT, reason: "stopped"),
    trapSignal(SIGTERM, reason: "terminated"),
    trapSignal(SIGHUP, reason: "terminal closed"),
].compactMap { $0 }

// One-shot timer so the timeout fires on time instead of waiting for the next
// tick (up to interval + leeway late). DispatchTime pauses during system sleep,
// so the monotonic check in tick() remains the sleep-safe backstop.
var deadlineTimer: DispatchSourceTimer?
if let timeout = config.timeout {
    let t = DispatchSource.makeTimerSource(queue: .main)
    t.schedule(deadline: .now() + timeout, leeway: .seconds(1))
    t.setEventHandler { shutdown("timeout reached") }
    t.resume()
    deadlineTimer = t
}

let timer = DispatchSource.makeTimerSource(queue: .main)
timer.schedule(deadline: .now() + 1, repeating: config.interval, leeway: .seconds(5))
timer.setEventHandler { tick() }
timer.resume()

dispatchMain()
