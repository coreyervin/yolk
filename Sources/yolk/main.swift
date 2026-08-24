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
//
// All of that lives in YolkKit, which the menu bar app shares. This file is
// only the process: flags in, events out, signals, and the one place allowed
// to call exit().

import Foundation
import YolkKit

// Line-buffered so `yolk -v | tee` shows progress instead of sitting in a
// 4KB block buffer.
setvbuf(stdout, nil, _IOLBF, 0)

func writeOut(_ message: String) {
    print(message)
}

func writeErr(_ message: String) {
    FileHandle.standardError.write(Data((message + "\n").utf8))
}

func die(_ message: String) -> Never {
    writeErr("yolk: \(message)\nTry 'yolk --help'.")
    exit(1)
}

// MARK: - Flags

let arguments = Array(CommandLine.arguments.dropFirst())

let invocation: Invocation
do {
    switch try ArgumentParser.parse(arguments) {
    case .help:
        writeOut(Usage.text)
        exit(0)
    case .version:
        writeOut(Usage.versionLine)
        exit(0)
    case .run(let parsed):
        invocation = parsed
    }
} catch let error as ArgumentError {
    die(error.message)
} catch {
    die("\(error)")
}

// MARK: - Wiring

let environment = SystemEnvironment.live

// Keep the process out of App Nap so the timer isn't throttled.
let activityToken = ProcessInfo.processInfo.beginActivity(
    options: .userInitiatedAllowingIdleSystemSleep, reason: "yolk keeping system active")

let renderer = ConsoleRenderer(
    verbose: invocation.verbose,
    invokedBare: invocation.invokedBare,
    pid: ProcessInfo.processInfo.processIdentifier,
    now: environment.now,
    out: writeOut,
    err: writeErr)

// Asked before the session starts, so the prompt is not interleaved into the
// banner. Keeping the Mac awake works without this; only Slack presence needs it.
if !environment.hasPostPermission() {
    renderer.renderPermissionRequestNotice()
    environment.requestPostPermission()
}

let session = YolkSession(config: invocation.config, environment: environment)
session.onEvent = { event in
    renderer.handle(event)
    // The engine never exits; the CLI does, once the session has reported why.
    if case .stopped = event {
        ProcessInfo.processInfo.endActivity(activityToken)
        exit(0)
    }
}

do {
    try session.start()
} catch AssertionError.creationFailed(let code) {
    die("failed to create power assertion (IOReturn \(code))")
} catch {
    die("failed to create power assertion (\(error))")
}

// The tip and the Ctrl-C hint come last, after any warning `start()` emitted.
renderer.renderStartupTail()

// MARK: - Signals

// If stdout is a pipe whose reader exits (yolk -v | head), a write must not
// kill the process silently.
signal(SIGPIPE, SIG_IGN)

@MainActor
func trapSignal(_ sig: Int32, reason: String) -> DispatchSourceSignal? {
    // Honor an inherited ignore (e.g. nohup sets SIGHUP to SIG_IGN): a dispatch
    // signal source is kqueue-based and would still fire on an ignored signal,
    // silently defeating nohup.
    let previous = signal(sig, SIG_IGN)
    guard unsafeBitCast(previous, to: UInt.self) != unsafeBitCast(SIG_IGN, to: UInt.self) else {
        return nil
    }
    let source = DispatchSource.makeSignalSource(signal: sig, queue: .main)
    source.setEventHandler {
        MainActor.assumeIsolated {
            // StopReason cannot tell these apart, and the CLI has always named
            // them differently in the shutdown line.
            renderer.userStopReason = reason
            session.stop()
        }
    }
    source.resume()
    return source
}

// Held for the process lifetime — a released signal source stops delivering.
let signalSources = [
    trapSignal(SIGINT, reason: "stopped"),
    trapSignal(SIGTERM, reason: "terminated"),
    trapSignal(SIGHUP, reason: "terminal closed"),
].compactMap { $0 }
_ = signalSources

dispatchMain()
