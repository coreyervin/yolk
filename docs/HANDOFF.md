# Yolk — implementation handoff

**Last updated:** 2026-08-20
**Purpose:** pick this work back up cold, without re-deriving decisions.

The approved design lives in
[`specs/2026-08-07-yolk-macos-app-design.md`](superpowers/specs/2026-08-07-yolk-macos-app-design.md).
That spec is the source of truth for *what* to build. This file records *where
the work actually is*, what was decided while building it, and what is still
open.

---

## Where things stand

Steps 1–3 of the spec's implementation sequence are done. The engine is
extracted and tested; the CLI still runs on its original single-file
implementation and has not yet been ported onto it.

| Spec step | Status |
|---|---|
| 1. Rename wakey → Yolk | done (2026-08-07, pre-git) |
| 2. `Package.swift`, extract `YolkKit` with tests | **done** |
| 3. Port CLI onto `YolkKit`, golden parity | **not started** |
| 4. Xcode project + app target, `AppModel` | not started |
| 5. `MenuBarView` + `SettingsView` | not started |
| 6. Icon assets | not started |
| 7. Signing, entitlements, `make release` | not started — needs credentials |
| 8. README rewrite, publish tap | not started — needs GitHub |

### Commits so far

```
4110572  Baseline: single-file yolk CLI before YolkKit extraction
d12ad22  spec: embed CLI at Contents/Resources/yolk, not Contents/MacOS
10da860  Extract DurationParser and YolkConfig into YolkKit
ee14c60  Extract SystemEnvironment, ConsoleState, DisplaySleepProbe, PowerAssertion
4a6bbd9  Extract YolkSession state machine
```

There is **no git remote**. The repo is local only.

## Quick start

```sh
swift test                              # 48 tests, 6 suites — all should pass
swift build -c release --product yolk   # build the CLI
.build/release/yolk --help              # should match Goldens/cli-help.txt exactly
```

Parity check, which must keep passing through step 3:

```sh
.build/release/yolk --help    | diff Goldens/cli-help.txt -
.build/release/yolk --version | diff Goldens/cli-version.txt -
```

## What exists

```
Sources/YolkKit/
  Duration.swift           DurationParser.parse — "8h"/"90m"/"45s"/"1d"/bare seconds
  YolkConfig.swift          validated config; bounds live here and nowhere else
  ConsoleState.swift        active/locked/switchedOut/unknown + pauseReason
  DisplaySleepProbe.swift   pmset -g parsing (+ the untested Process glue)
  SystemEnvironment.swift   the OS boundary: 11 closures, .live wiring
  PowerAssertion.swift      RAII over IOPMAssertion, released in deinit
  YolkSession.swift         the state machine — events, no exit()
Sources/yolk/
  main.swift                STILL the original self-contained CLI (step 3 rewrites it)
Tests/YolkKitTests/
  Support/FakeEnvironment.swift   AssertionRecorder, FakeSystem, EventCollector
  ...Tests.swift                  48 tests
Goldens/
  cli-help.txt, cli-version.txt   captured from the pre-refactor binary
```

## Decisions made while building (not in the spec)

These were judgement calls. Revisit them deliberately, not by accident.

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

5. **The CLI target is pinned to `swiftLanguageMode(.v5)`.** The original
   `main.swift` is top-level code with mutable globals, which Swift 6 mode
   rejects. **Remove this override in step 3** — `YolkKit` is already full
   Swift 6 mode and the whole package should be.

6. **Embedded CLI path moved to `Contents/Resources/yolk`** (commit `d12ad22`).
   `Contents/MacOS/Yolk` (app executable) and `Contents/MacOS/yolk` (CLI)
   differ only in case, and macOS volumes are case-insensitive by default —
   the copy silently clobbers the app executable and yields a bundle that will
   not launch. Verified on APFS.

## Open questions

- **Verbose pause logging changed.** The old CLI printed
  `screen locked or session inactive — paused` on *every* verbose tick while
  locked. The event model emits `.paused` once per transition, so it now
  reports once. Arguably better (an overnight locked session no longer emits
  hundreds of identical lines) but it *is* a change in observable CLI output,
  and the spec froze CLI behavior. **Needs a decision**: keep, or have
  `ConsoleRenderer` track pause state and restore per-tick logging.

- **Startup banner golden is not captured yet.** `--help` and `--version` are
  in `Goldens/`. The banner is not, because printing it runs
  `CGRequestPostEventAccess()`, which can raise the Accessibility prompt. To
  capture it, run the pre-refactor binary deliberately:
  ```sh
  ./yolk -t 5s > Goldens/cli-banner.txt 2>&1
  ```
  Note `./yolk` at the repo root is the **original Aug 7 build** and is
  gitignored — do not delete it until the banner golden exists. Per the spec
  the renderer needs an injectable `tipPicker` so the random "Did you know?"
  line doesn't make the banner unreproducible; capture three cases (fixed tip,
  `{ nil }`, and a flagged invocation which must emit no tip).

- **Git history contains ~157MB of build artifacts.** An early `git add -A`
  committed the whole `.build/` directory across the first commits. `.gitignore`
  is now correct and `.build` is untracked going forward, but the blobs remain
  in history. With no remote yet, this is the cheap moment to rewrite. Not done
  because it is destructive and needs a deliberate call.

## Credentials — when they are actually needed

| What | Needed at | Notes |
|---|---|---|
| Nothing | steps 3, and most of 4–6 | all local |
| Apple ID in Xcode | step 5, first real app run | free tier is enough; without a stable signing identity macOS may force re-granting Accessibility on **every rebuild**, which makes testing the permission flow miserable |
| Developer ID **Application** cert | step 7 | not "Apple Development"; org accounts often restrict this to the Account Holder |
| App Store Connect API key (`.p8`) | step 7 | downloadable **once**; also needs key ID + issuer UUID for `xcrun notarytool store-credentials` |
| GitHub repo (+ optionally `gh`) | step 8 | `gh` is not installed; plain git works |

As of 2026-08-20 this Mac has **no** Developer ID certificate and no Xcode
account signed in. The only keychain identity is a Mosyle MDM enrollment cert,
which is not a signing identity.

**Yolk is a personal project**, not PocketHealth work — it signs under a
personal Apple Developer account. Bundle ID `io.github.coreyervin.yolk` is
permanent; changing it after release invalidates every user's Accessibility
grant.

## Next action

Step 3 — port the CLI onto `YolkKit`:

- `Sources/yolk/ArgumentParser.swift` — hand-rolled, ~40 lines, no dependency
- `Sources/yolk/ConsoleRenderer.swift` — `YolkEvent` → stdout/stderr, owns
  `--verbose` and the injectable `tipPicker`
- `main.swift` shrinks to: parse flags → build `YolkConfig` → construct
  session → render events
- Signal handling stays in the CLI, including the inherited-`SIG_IGN` check
  that keeps `nohup` working. Only the CLI calls `exit()`.
- Drop the `swiftLanguageMode(.v5)` override from `Package.swift`
- Keep the parity diffs green at every commit
