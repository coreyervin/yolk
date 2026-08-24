# Yolk — implementation handoff

**Last updated:** 2026-08-24
**Purpose:** pick this work back up cold, without re-deriving decisions.

The approved design lives in
[`specs/2026-08-07-yolk-macos-app-design.md`](superpowers/specs/2026-08-07-yolk-macos-app-design.md).
That spec is the source of truth for *what* to build. This file records *where
the work actually is*, what was decided while building it, and what is still
open.

---

## Where things stand

Steps 1–3 of the spec's implementation sequence are done. The engine is
extracted, the CLI runs entirely on it, and output parity with the pre-refactor
binary is verified rather than assumed. Nothing of the app exists yet.

| Spec step | Status |
|---|---|
| 1. Rename wakey → Yolk | done (2026-08-07, pre-git) |
| 2. `Package.swift`, extract `YolkKit` with tests | done |
| 3. Port CLI onto `YolkKit`, golden parity | **done (2026-08-24)** |
| 4. Xcode project + app target, `AppModel` | **not started — next** |
| 5. `MenuBarView` + `SettingsView` | not started |
| 6. Icon assets | not started |
| 7. Signing, entitlements, `make release` | not started — needs credentials |
| 8. README rewrite, publish tap | not started — needs GitHub |

There is **no git remote**. The repo is local only.

## Quick start

```sh
swift test                              # 86 tests, 10 suites — all should pass
swift build -c release --product yolk   # build the CLI
```

Parity checks, which must keep passing:

```sh
.build/release/yolk --help    | diff Goldens/cli-help.txt -
.build/release/yolk --version | diff Goldens/cli-version.txt -
```

The banner, verbose ticks, and shutdown summary are covered by
`ConsoleRendererTests` against `Goldens/cli-banner-*.txt`, so `swift test` is
the real parity gate. See **Goldens** below.

## What exists

```
Sources/YolkKit/
  Version.swift            YolkKit.version — the single source of version truth
  Duration.swift           DurationParser.parse — "8h"/"90m"/"45s"/"1d"/bare seconds
  YolkConfig.swift         validated config; bounds and defaults live here, nowhere else
  ConsoleState.swift       active/locked/switchedOut/unknown + pauseReason
  DisplaySleepProbe.swift  pmset -g parsing (+ the untested Process glue)
  SystemEnvironment.swift  the OS boundary: 11 closures, .live wiring
  PowerAssertion.swift     RAII over IOPMAssertion, released in deinit
  YolkSession.swift        the state machine — events, no exit()
Sources/yolk/
  main.swift               process only: flags in, events out, signals, exit()
  ArgumentParser.swift     hand-rolled flag parsing → ParseOutcome
  ConsoleRenderer.swift    YolkEvent → stdout/stderr; owns --verbose and the tips
  Usage.swift              the frozen --help and --version text
Tests/YolkKitTests/        49 tests — engine, driven through SystemEnvironment fakes
Tests/YolkCLITests/        37 tests — parser, renderer, usage goldens
Goldens/
  cli-help.txt, cli-version.txt          captured from the pre-refactor binary
  cli-banner-{bare,notip,flagged}.txt    the three banner cases the spec requires
  originals/                             raw pre-refactor output; see its README
```

## Goldens

`Goldens/originals/` holds raw output captured from `./yolk` — the gitignored
Aug 7 build — on 2026-08-24, covering four invocations. It is provenance: the
goldens above were derived from it, and it is the only ground truth left once
that binary disappears. **Do not delete `./yolk` casually**, but the captures
mean it is no longer irreplaceable.

The goldens use `%PID%` and `%AUTOEXIT%` tokens, substituted by the tests, so
expectations do not depend on process id or timezone.

Two output paths could not be captured on this Mac and are asserted from the
frozen source text instead of against the original binary: the **Accessibility
permission warning** (already granted here) and the **display-sleep warning**
(display sleep is longer than `threshold + interval + 5`). If you ever run on a
machine that produces either, capturing them would upgrade those two tests from
"matches what the source said" to "matches what the binary did".

## Decisions made while building

Judgement calls, in the order they were made. Revisit deliberately, not by
accident.

### From step 2 (engine extraction)

1. **`SystemEnvironment` has 11 closures, not 10.** Added
   `waitForEventDelivery`. The original's `usleep(200_000)` before re-reading
   the idle timer is load-bearing, but a literal sleep would make every session
   test wait and would block the main actor in the app. `.live` sleeps 200ms;
   fakes no-op.

2. **Package platform is macOS 14, not 13.** `MainActor.assumeIsolated` in the
   timer handlers requires it. The app's `@Observable` requires 14 regardless,
   so this only narrows who can install the standalone CLI.

3. **Timers live in a private `TimerBox`.** Releasing an active
   `DispatchSourceTimer` without cancelling traps in libdispatch, and a
   `@MainActor` class's `deinit` cannot safely touch isolated state. The box is
   non-isolated and cancels on its own dealloc. Timer handlers also capture
   `[weak self]` — a strong capture makes the session immortal and the
   "releases on dealloc" test fails confusingly.

4. **`ConsoleState.pauseReason` is the single pause decision point.** Every
   "should I pause?" check routes through it, so a case added later cannot
   silently start posting synthetic input into a locked or switched-out
   session. `pausesSimulation` is derived from it.

5. **Embedded CLI path moved to `Contents/Resources/yolk`** (commit `d12ad22`).
   `Contents/MacOS/Yolk` (app executable) and `Contents/MacOS/yolk` (CLI)
   differ only in case, and macOS volumes are case-insensitive by default —
   the copy silently clobbers the app executable and yields a bundle that will
   not launch. Verified on APFS.

### From step 3 (CLI port)

6. **Verbose pause logging stays once-per-transition.** Decided 2026-08-24,
   closing the step-2 open question. The old CLI printed
   `screen locked or session inactive — paused` on every verbose tick while
   locked; the event model emits `.paused` once. An overnight locked session
   now logs one line instead of ~960 identical ones. This *is* a change in
   observable CLI output and is accepted deliberately. Restoring per-tick
   logging is not a renderer-only change — `YolkSession.tick()` returns early
   once `run.pausedBecause` matches and emits nothing further, so it would need
   a new event plus a rewrite of the "announces a pause once" test.

7. **`.resumed` renders nothing**, because the frozen CLI printed nothing on
   resume — the next `idle Ns` line is the only signal. Adding a resume line
   would have been a second, unasked-for output change on top of decision 6.

8. **`ArgumentParser` validates each flag as it parses it**, against
   `YolkConfig`'s ranges rather than literals. Bounds still live in one place,
   but collect-then-validate would silently reorder error messages:
   `yolk --threshold 999 -i 999` must report the *threshold* error, and
   `YolkConfig` checks interval first. There is a test for exactly this, and it
   was confirmed to fail against the naive implementation.

9. **`renderStartupTail()` is separate from the `.started` event.** The tip and
   the `Press Ctrl-C to stop.` line must land *after* the display-sleep
   warning, which the engine emits as its own event immediately after
   `.started`. Folding them into `.started` reorders the output for anyone
   redirecting `2>&1` — which is how the goldens were captured. Tested.

10. **The permission preflight moved to before `session.start()`.** The
    original created the power assertion first, then preflighted. The banner is
    now printed from inside `start()`, so the preflight has to precede it or
    the prompt lands mid-banner. Only observable difference: if assertion
    creation fails, the permission warning is printed before the fatal error
    where previously nothing was. `IOPMAssertionCreateWithName` essentially
    never fails, so this was judged acceptable.

11. **`ConsoleRenderer` recomputes the auto-exit time** from its own injected
    `now()` rather than reading `Run.estimatedEnd`, which is not yet set when
    `.started` is emitted. The two clock reads are microseconds apart and the
    line is display-only at second resolution, so a straddled boundary is
    both vanishingly unlikely and harmless. The real deadline is monotonic and
    lives in the engine.

12. **`ConsoleRenderer` is `@MainActor`.** Dispatch signal sources take
    `@Sendable` handlers, and a non-Sendable class cannot be captured in one
    under Swift 6. `@MainActor` makes it Sendable, matching `YolkSession`.
    Consequence: `ConsoleRendererTests` is a `@MainActor` suite, and the
    golden-file helpers had to move out to a nonisolated `GoldenFile` so the
    other suites could still use them.

13. **Tests `@testable import yolk` directly** — SwiftPM links the executable
    target into the test bundle without complaint, so the spec's layout stands
    and no `YolkCLI` library shim was needed.

14. **`swiftLanguageMode(.v5)` is gone.** The whole package is Swift 6 mode,
    builds clean with no warnings in debug and release.

## Open questions

- **Git history contains ~157MB of build artifacts.** An early `git add -A`
  committed the whole `.build/` directory across the first commits.
  `.gitignore` is now correct and `.build` is untracked going forward, but the
  blobs remain in history. With no remote yet, this is still the cheap moment
  to rewrite. Not done because it is destructive and needs a deliberate call.

## Credentials — when they are actually needed

| What | Needed at | Notes |
|---|---|---|
| Nothing | most of 4–6 | all local |
| Apple ID in Xcode | step 5, first real app run | free tier is enough; without a stable signing identity macOS may force re-granting Accessibility on **every rebuild**, which makes testing the permission flow miserable |
| Developer ID **Application** cert | step 7 | not "Apple Development"; org accounts often restrict this to the Account Holder |
| App Store Connect API key (`.p8`) | step 7 | downloadable **once**; also needs key ID + issuer UUID for `xcrun notarytool store-credentials` |
| GitHub repo (+ optionally `gh`) | step 8 | `gh` is not installed; plain git works |

As of 2026-08-24 this Mac has **no** Developer ID certificate and no Xcode
account signed in. The only keychain identity is a Mosyle MDM enrollment cert,
which is not a signing identity.

**Yolk is a personal project**, not PocketHealth work — it signs under a
personal Apple Developer account. Bundle ID `io.github.coreyervin.yolk` is
permanent; changing it after release invalidates every user's Accessibility
grant.

## Next action

Step 4 — the Xcode project and app target:

- `App/Yolk.xcodeproj` checked in, referencing the local package for `YolkKit`
- `AppModel` as an `@Observable` wrapper over `YolkSession`, translating events
  into observable state and persisting settings to `UserDefaults`
- The app starts **idle** — opening it puts the icon in the menu bar ready to
  use, it does not begin a session
- `make check-version` comparing `MARKETING_VERSION` against `YolkKit.version`
  is worth adding here rather than waiting for step 7, since the project file
  is being created anyway
- The run-script build phase that embeds the CLI at
  `Contents/Resources/yolk` must run *before* code signing — see decision 5
