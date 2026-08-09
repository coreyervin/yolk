# Yolk — macOS menu bar app + CLI

**Date:** 2026-08-07
**Status:** Approved design, ready for implementation planning

## Summary

Yolk is a single-file Swift CLI that keeps a Mac awake and keeps Slack showing
you as active while agents run. This design turns it into two products sharing
one engine: the existing CLI, and a native macOS menu bar app with a settings
UI.

The project was renamed from **wakey** to **Yolk** on 2026-08-07 — the
directory, source strings, comments, `Makefile`, and `README.md` are already
migrated. Everything below describes work still to be done.

## Motivation

Three things drive this work.

**A menu bar app fixes the worst usability problem.** Accessibility permission
is granted to the *responsible process*, which for the CLI is the terminal — or
the tmux server, which is why the current README carries a warning about it. A
signed `.app` bundle owns its own permission: grant once, permanently, with no
ambiguity about which process was blessed.

**A GUI suits the actual usage pattern.** Toggling on before an agent run and
off afterward is a two-click operation in a menu bar, versus finding a terminal
and Ctrl-C'ing a background process.

**The name collides.** [Wakey](https://maolab.gumroad.com/l/wakey) is an
existing paid macOS menu bar keep-awake app with a near-identical feature set,
and [wakey](https://github.com/jonathanruiz/wakey) is a Wake-on-LAN CLI already
installable via Homebrew. Publishing a third `wakey` in the same space would be
actively confusing.

## Naming and identity

| | |
|---|---|
| Product name | Yolk |
| CLI binary | `yolk` |
| App bundle | `Yolk.app` |
| Library | `YolkKit` |
| Bundle identifier | `io.github.coreyervin.yolk` |
| Repository | `github.com/coreyervin/yolk` |
| Homebrew tap | `coreyervin/homebrew-tap` |
| Version at first release | 1.0.0 |

The name comes from "wakey wakey, eggs and bakey" — the original working title
was Wakey, and the fried-egg icon outlived it. The README keeps this as the
origin story.

The bundle identifier is permanent. Changing it after release invalidates every
user's Accessibility grant and silently breaks the app for them.

## What is deliberately not changing

The CLI's observable behavior is unchanged: same flags, same defaults, same
validation bounds, same signal handling, same exit codes. From here the CLI's
output is frozen exactly as it stands today.

Two changes were made to the pre-refactor tree before that freeze took effect,
both on 2026-08-07: the `wakey` → `yolk` rename, and the startup tip described
under **CLI** below.

This is a refactor plus an addition, not a redesign of the working part.

## Architecture

### Repository layout

```
yolk/
├─ Package.swift                  YolkKit + CLI targets
├─ Sources/
│  ├─ YolkKit/
│  │   ├─ YolkConfig.swift        validated settings
│  │   ├─ Duration.swift          "8h" / "90m" / "45s" parsing
│  │   ├─ PowerAssertion.swift    IOKit RAII wrapper
│  │   ├─ IdleMonitor.swift       idle seconds + console state
│  │   ├─ ActivitySimulator.swift synthetic event posting
│  │   ├─ DisplaySleepProbe.swift pmset -g parsing
│  │   ├─ SystemEnvironment.swift injectable system boundary
│  │   └─ YolkSession.swift       the state machine
│  └─ yolk/
│      ├─ main.swift              entry point
│      ├─ ArgumentParser.swift    flag parsing
│      └─ ConsoleRenderer.swift   YolkEvent → stdout/stderr
├─ Tests/YolkKitTests/
├─ App/
│  ├─ Yolk.xcodeproj/             checked in
│  └─ Yolk/
│      ├─ YolkApp.swift           @main, MenuBarExtra + Settings scenes
│      ├─ AppModel.swift          @Observable wrapper over YolkSession
│      ├─ MenuBarView.swift
│      ├─ SettingsView.swift
│      ├─ Yolk.entitlements
│      └─ Assets.xcassets/        app icon + menu bar template icons
├─ Makefile                       test · app · dmg · notarize · release
├─ docs/superpowers/specs/
└─ README.md
```

Shared code and the CLI live in a local Swift package, which is where SwiftPM
is genuinely the right tool. The app is an Xcode project, because SwiftPM
cannot produce `.app` bundles and that is a deliberate Apple stance rather than
a gap about to close. The `.xcodeproj` stays thin — it is a shell around the
package, not where logic lives.

### YolkKit

The current `main.swift` mixes engine logic with process control: `die()` and
`shutdown()` call `exit()` from inside the core loop. That is fine for a CLI
and fatal for an app, which must stop and restart sessions without quitting.

YolkKit throws and emits events. Only the CLI calls `exit()`.

**`YolkConfig`** — validated configuration. Validation bounds live here and
nowhere else, so the CLI's argument parser and the app's settings UI cannot
drift apart.

```swift
public struct YolkConfig: Equatable, Sendable {
    public static let intervalRange: ClosedRange<TimeInterval> = 5...120
    public static let thresholdRange: ClosedRange<TimeInterval> = 5...300

    public var interval: TimeInterval    // seconds between idle checks
    public var threshold: TimeInterval   // idle seconds before simulating
    public var timeout: TimeInterval?    // nil = run indefinitely

    public init(interval: TimeInterval = 30,
                threshold: TimeInterval = 60,
                timeout: TimeInterval? = nil) throws
}

public enum ConfigError: Error, Equatable {
    case intervalOutOfRange(TimeInterval)
    case thresholdOutOfRange(TimeInterval)
    case invalidTimeout
}
```

The threshold cap of 300s is a product decision, not an arbitrary limit: above
it, Slack's ~10 minute away timer becomes reachable and the tool silently stops
doing its job.

`YolkConfig` deliberately omits the CLI's `--verbose` flag. Verbosity controls
how events are *rendered*, not how the engine behaves, so it belongs to the
CLI's renderer. The engine emits `checked(idle:)` on every tick regardless and
lets each frontend decide whether to display it.

YolkKit also owns the single source of version truth:

```swift
public enum YolkKit {
    public static let version = "1.0.0"
}
```

**`SystemEnvironment`** — a struct of closures forming the single boundary
between YolkKit and the operating system. `.live` wires up the real
CoreGraphics, IOKit, and `pmset` calls; tests substitute fakes.

```swift
public struct SystemEnvironment: Sendable {
    public var idleSeconds: @Sendable () -> TimeInterval
    public var consoleState: @Sendable () -> ConsoleState
    public var postActivity: @Sendable () -> Bool
    public var hasPostPermission: @Sendable () -> Bool
    public var requestPostPermission: @Sendable () -> Void
    public var displaySleepSeconds: @Sendable () -> TimeInterval?
    public var monotonicNow: @Sendable () -> TimeInterval
    public var now: @Sendable () -> Date

    // Power assertion lifecycle must be injectable too, or the assertion
    // tests below can only run against real IOKit.
    public var createAssertion: @Sendable (String) throws -> AssertionHandle
    public var releaseAssertion: @Sendable (AssertionHandle) -> Void

    public static let live: SystemEnvironment
}

/// Opaque wrapper over `IOPMAssertionID` so fakes can vend their own handles.
public struct AssertionHandle: Equatable, Sendable {
    public let rawValue: UInt32
}

public enum ConsoleState: Equatable, Sendable {
    case active         // ours, on console, unlocked
    case locked
    case switchedOut    // fast user switching
    case unknown        // session dictionary unreadable
}
```

A struct of closures rather than a protocol: fewer types, trivial partial
overrides in tests, and no protocol-witness ceremony for ten functions.

**`PowerAssertion`** — RAII wrapper over `IOPMAssertionCreateWithName` /
`IOPMAssertionRelease`, released in `deinit`. The current code has exactly one
release path, inside `shutdown()`; an app that starts and stops repeatedly
needs release tied to object lifetime instead.

**`YolkSession`** — the state machine. Owns the timer, holds the assertion
while running, emits events.

```swift
@MainActor
public final class YolkSession {
    public init(config: YolkConfig, environment: SystemEnvironment = .live)

    public private(set) var state: State
    public var onEvent: ((YolkEvent) -> Void)?

    public func start() throws
    public func stop()

    /// Applies new settings to a live session without disturbing `startedAt`
    /// or `activityCount`. A changed `timeout` restarts the countdown from
    /// now. No-op semantics when stopped: the config is simply stored.
    public func reconfigure(_ config: YolkConfig)

    public enum State: Equatable {
        case stopped
        case running(Run)
    }

    public struct Run: Equatable {
        public let startedAt: Date
        public let estimatedEnd: Date?           // display only — see below
        public var activityCount: Int
        public var pausedBecause: PauseReason?   // nil = actively working
    }
}

public enum YolkEvent: Equatable {
    case started(YolkConfig)
    case checked(idle: TimeInterval)             // every tick; CLI logs when verbose
    case paused(PauseReason)
    case resumed
    case activitySimulated(total: Int)
    case permissionMissing                       // event posted, idle didn't reset
    case postFailed                              // event could not be constructed
    case displaySleepTooSoon(TimeInterval)       // emitted once at start
    case stopped(StopReason)
}

public enum PauseReason: Equatable { case screenLocked, sessionSwitchedOut, sessionUnknown }
public enum StopReason: Equatable { case userRequested, timeoutReached }
```

`permissionMissing` and `postFailed` are emitted at most once per session, as
today — a warning on every tick would be unusable in both frontends.

The timeout stays anchored to `CLOCK_MONOTONIC`. That is a subtle and correct
property of the current implementation: it survives system sleep but ignores
wall-clock steps from NTP or manual changes, so `-t 8h` means eight real hours.
The one-shot dispatch timer remains the fast path and the monotonic comparison
in `tick()` remains the sleep-safe backstop.

`Run.estimatedEnd` is named deliberately. It is a wall-clock `Date` used only
to render "auto-exit at 17:30" in the CLI banner and the menu. It is never
consulted to decide whether to stop — that is the monotonic deadline's job
alone. Keeping the two distinct prevents a future change from accidentally
making expiry sensitive to clock adjustments.

### CLI

`main.swift` shrinks to: parse flags → build `YolkConfig` → construct a
session → render events. Signal handling (`SIGINT`, `SIGTERM`, `SIGHUP`, plus
the inherited-`SIG_IGN` check that keeps `nohup` working) stays in the CLI,
because process control is not the library's concern.

Argument parsing stays hand-rolled. It is roughly forty lines, has no
dependencies today, and `swift-argument-parser` would buy little for four
flags.

**Startup tips.** A bare `yolk` prints one randomly chosen "Did you know?" line
in the startup banner, surfacing options that are easy to miss. Passing any
flag suppresses it — someone using flags has already found the help, and the
tips would become nagging rather than helpful.

This makes the startup banner non-deterministic, which the golden tests must
account for. `ConsoleRenderer` therefore takes an injectable
`tipPicker: () -> String?`, defaulting to `TIPS.randomElement`. Tests pin it to
a fixed tip or to `{ nil }`. The tip belongs to the renderer, not to YolkKit —
it is a presentation concern, and the menu bar app has no equivalent.

### App

**`AppModel`** is an `@Observable` class wrapping a `YolkSession`, translating
events into observable state and persisting settings to `UserDefaults`.

**Menu bar dropdown:**

```
┌─────────────────────────────┐
│  ● Yolk is active           │
│  Awake 2h 14m · 47 nudges   │
├─────────────────────────────┤
│  ✓ Keep Mac Awake      ⌘K   │
├─────────────────────────────┤
│  Stop after…                │
│     Never              ✓    │
│     30 minutes              │
│     1 hour                  │
│     4 hours                 │
│     8 hours                 │
├─────────────────────────────┤
│  Settings…             ⌘,   │
│  Quit Yolk             ⌘Q   │
└─────────────────────────────┘
```

The status line is derived from `YolkSession.State`. When paused it reads
`● Paused — screen locked`. When Accessibility permission is missing, an
additional warning row appears above Settings that deep-links to
`x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility`.

The chosen "Stop after" duration persists across launches.

**Changing "Stop after" mid-session** restarts the countdown from now rather
than recomputing against the original start time. Picking "1 hour" two hours
into a session means one more hour, not immediate expiry. Selecting "Never"
cancels the deadline and leaves the session running.

**Settings window** — one pane, no tabs:

- Check interval, 5–120s
- Idle threshold, 5–300s
- Launch at Login, via `SMAppService.mainApp`
- Accessibility permission status with a button to the relevant System Settings pane
- The display-sleep warning, shown inline only when
  `threshold + interval + 5 >= displaySleep`

Both numeric controls carry plain-language explanations rather than raw
numbers, and are bounded by `YolkConfig`'s ranges.

**Changing interval or threshold while a session is running** applies
immediately: the session is reconfigured in place, keeping `startedAt`,
`activityCount`, and any deadline intact. It does not silently wait until the
next toggle, and it does not reset the session's statistics — either would be
surprising given the controls sit next to a live status readout.

**Launch at Login** uses `SMAppService.mainApp`, which requires the app to live
in `/Applications` to register reliably. The Homebrew cask installs it there,
but a user running the app from `~/Downloads` will find the toggle fails. The
settings pane detects this and explains it rather than silently reverting.

**Launch behavior:** the app starts **idle**. Opening it puts the icon in the
menu bar ready to use; you toggle it on when starting an agent run. "Launch at
Login" therefore means "have Yolk available at login", not "be awake from
boot".

### Icons

The menu bar icon must be a **template image** — monochrome, so macOS can
invert it for light and dark menu bars and tint it correctly. A full-color
fried egg cannot serve this role; it would render wrong in dark mode.

| Asset | Treatment |
|---|---|
| App icon (Finder, System Settings) | Full-color sunny-side-up fried egg |
| Menu bar — active | Line-art egg, **filled** yolk, template |
| Menu bar — idle | Line-art egg, **hollow** yolk, template |
| Menu bar — paused | Hollow yolk, rendered at reduced opacity |

State is legible at a glance from the yolk alone, which survives the 16pt menu
bar rendering better than a color or badge change would.

## Permissions, signing, and sandboxing

**App Sandbox must be off.** A sandboxed app cannot post synthetic input events
or shell out to `pmset`. This is a hard constraint, and its direct consequence
is that **Yolk cannot ship on the Mac App Store**. Distribution is Homebrew and
direct download only.

This was investigated specifically rather than assumed, because App Store
distribution was initially wanted. The conclusion is firm:

- Apple DTS states the position directly on the developer forums: "The App
  Store requires that all apps be sandboxed and modifying user events
  completely undermines that goal; if you can inject user events you could
  easily bypass the sandbox. […] If this functionality is critical to your
  product, I recommend that you distribute it independently using Developer
  ID." Event injection defeats the sandbox by definition, so no permitting
  entitlement exists — this is not review roulette.
- `IOPMAssertionDeclareUserActivity` is sandbox-legal but is **not** a
  substitute. It postpones display sleep and power-management idle without
  resetting the CoreGraphics HID idle timer, which is the specific value Slack
  reads. Slacktive, the nearest open-source equivalent, calls it *and* still
  posts synthetic mouse movement plus an F16 key tap on top — and ships
  DMG-only from GitHub rather than the App Store.

A two-edition strategy — a sandboxed keep-awake build for the App Store
alongside a full Developer ID build — is viable, and is what the unrelated
Wakey app does today. It was considered and **rejected**: it doubles the build
configuration and feature matrix, and the crippled edition would compete
head-on with Amphetamine while omitting the only feature that justifies Yolk's
existence. One full-featured version shipped via Homebrew is the decision.

**Hardened Runtime is on**, which notarization requires and which unsandboxed
apps can still adopt.

**Signing** uses a Developer ID Application certificate. This matters more here
than for a typical app: macOS ties Accessibility grants to code signing
identity, so an unsigned or ad-hoc-signed build would force users to re-grant
permission after every update.

Accessibility permission has no usage-description string. The app requests it
at runtime via `CGRequestPostEventAccess()` and detects a missing grant the way
the CLI already does — by posting an event and confirming the idle timer
actually reset.

## Distribution

The app bundle **embeds the CLI binary** at `Yolk.app/Contents/MacOS/yolk`, and
the Homebrew cask symlinks it onto `PATH` via a `binary` stanza. The embedding
happens inside Xcode as a run-script build phase — it invokes
`swift build -c release --product yolk` and copies the result into the bundle
*before* the code-signing phase, so Xcode signs the nested binary and the outer
bundle in the correct inside-out order. Doing this in the Makefile after export
would invalidate the app's signature.

One install command delivers both:

```sh
brew install --cask coreyervin/tap/yolk
```

This avoids a formula and a cask competing for the same name in one tap, and
means the CLI ships already notarized rather than requiring a source build.

Building from source stays supported for people who want only the CLI:
`make install` puts `yolk` in `~/.local/bin`.

Note that the embedded CLI does not inherit the app's Accessibility grant when
run from a terminal — TCC still attributes it to the responsible process. The
README documents this.

**Release pipeline**, driven by `make release`:

1. `make check-version` — fail early if `MARKETING_VERSION` and
   `YolkKit.version` disagree, before anything is built or signed
2. `swift test`
3. `xcodebuild archive` — which builds and embeds the CLI via the run-script
   phase above, then signs bundle and nested binary together
4. Export with Developer ID signing
5. Assemble DMG
6. `xcrun notarytool submit --wait`
7. `xcrun stapler staple`

Notarization credentials are stored once via `xcrun notarytool
store-credentials` and referenced by keychain profile, so no secrets live in
the repo.

Releases run locally at first. GitHub Actions requires the signing certificate
and an App Store Connect key in repository secrets, which is not worth solving
before a first release exists.

Homebrew core and homebrew-cask both have notability requirements that a new
project will not meet, so the personal tap is the starting point and remains
correct indefinitely.

## Testing

The project currently has no tests. YolkKit gets a suite using Swift Testing,
driven entirely through `SystemEnvironment` fakes — no real permissions, no
waiting on real timers.

**Duration parsing:** `8h`, `90m`, `45s`, `1d`, bare seconds, and rejection of
empty, negative, zero, non-numeric, and non-finite input.

**Config validation:** both range boundaries inclusive, values outside them
rejected with the specific error.

**Session state machine:**

- No simulation when idle is below threshold
- Simulation when idle is at or above threshold and the console is active
- Pause on `.locked`, `.switchedOut`, and `.unknown` — the fail-safe behavior
  that must never regress into posting events at someone else's session
- Resume emits `.resumed` exactly once, not per tick
- `activityCount` increments only when the idle timer actually reset
- `permissionMissing` fires once per session across many failing ticks
- `postFailed` fires once per session
- Timeout fires at the monotonic deadline, including across a simulated system
  sleep that advances the monotonic clock without advancing dispatch time
- `PowerAssertion` is released when the session stops and when it is
  deallocated — verified through the injected `releaseAssertion` closure, not
  against real IOKit
- `reconfigure` preserves `startedAt` and `activityCount` on a live session
- `reconfigure` with a new timeout restarts the countdown from now rather than
  measuring against the original start
- `reconfigure` to a nil timeout cancels the deadline and leaves the session
  running

**Parity:** the CLI's `--help` text and startup banner are captured as golden
tests. The rename and the startup tip are already complete in the pre-refactor
tree, so the baseline is captured verbatim from the current build before
extraction begins — no transformation applied, and any difference afterward
fails the test.

Banner goldens are captured with `tipPicker` pinned, since a random tip would
otherwise make the output unreproducible. Three cases: bare invocation with a
fixed tip, bare invocation with `{ nil }`, and a flagged invocation — which must
emit no tip at all regardless of what the picker would return.

**Version consistency:** the Xcode project's `MARKETING_VERSION` must match
`YolkKit.version`. This cannot be a SwiftPM unit test — the test target has no
access to `project.pbxproj` and the app's `Info.plist` does not exist until
`xcodebuild` runs. It is a `make check-version` target instead, run as the
first step of `make release` so a mismatch fails before anything is signed.

## Implementation sequence

1. ~~Rename `~/Code/wakey` to `~/Code/yolk`, and rename the product throughout
   `main.swift`, `Makefile`, `.gitignore`, and `README.md`~~ (done 2026-08-07).
   Version control is deliberately deferred; `git init` happens before step 2
   so the pre-refactor state is recoverable once code starts moving.
2. Add `Package.swift`; extract `YolkKit` from `main.swift` with tests.
3. Port the CLI onto `YolkKit`; verify output parity via golden tests.
4. Create the Xcode project and app target; wire `AppModel` to `YolkSession`.
5. Build `MenuBarView` and `SettingsView`.
6. Produce icon assets — full-color app icon, template menu bar variants.
7. Configure signing, entitlements, and the `make release` pipeline.
8. Rewrite the README for both products; publish the tap.

Steps 2 and 3 land together — the CLI must never be broken between commits.

## Out of scope

Deferred deliberately, recorded so they are not re-litigated during
implementation:

- **Notification on timer expiry.** Useful, but adds
  `UNUserNotificationCenter` and another permission prompt. The menu bar icon
  changing state is sufficient for v1.
- **Sparkle auto-update.** The Homebrew cask handles updates.
- **Scheduling / work hours.** Feature creep toward the crowded end of this
  category.
- **Lid-closed / clamshell override.** Closing the lid still sleeps the Mac,
  matching `caffeinate -i`.
- **`yolk -- <cmd>` process scoping.** Carried forward from the original
  README's future ideas; still a good idea, still not now.
- **Shared configuration between CLI and app.** The CLI stays flag-driven and
  the app stays `UserDefaults`-driven. Running both simultaneously is harmless:
  two power assertions, both nudging.
- **Mac App Store.** Investigated and ruled out — see the sandboxing section.
  Synthetic event posting is categorically prohibited there, and the
  sandbox-legal alternative does not achieve the effect.

## Prior art

The category is crowded, which is worth knowing rather than discovering after
release. Keep-awake: KeepingYouAwake, Caffeinated, Caffeine, Amphetamine,
DontSleep. Presence-specific: Slacktive, Active Now, NoAway, Shake It On.

Yolk differs in four ways worth leading with in the README:

- **The cursor never moves.** Competitors jiggle or sine-wave the pointer;
  Yolk posts `mouseMoved` at the cursor's existing position, resetting the idle
  timer with zero visible motion.
- **It pauses on lock, by design.** Locking is treated as the honest "I walked
  away" gesture. Most alternatives fake presence unconditionally.
- **Delivery is verified.** Yolk re-checks idle after posting to detect a
  missing Accessibility grant, instead of failing silently.
- **There is a real CLI.** Most alternatives are GUI-only.
