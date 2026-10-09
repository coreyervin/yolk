# Yolk — implementation handoff

**Last updated:** 2026-10-09
**Purpose:** pick this work back up cold, without re-deriving decisions.

The approved design lives in
[`specs/2026-08-07-yolk-macos-app-design.md`](superpowers/specs/2026-08-07-yolk-macos-app-design.md).
That spec is the source of truth for *what* to build. This file records *where
the work actually is*, what was decided while building it, and what is still
open.

---

## Where things stand

Steps 1–6 are done and step 8 is done except for the parts that depend on
step 7. The engine is extracted, both products run on it, the app has been in
daily use since late August, the repo is public, and the CLI is installable
with `brew install coreyervin/tap/yolk`.

**Everything still outstanding traces back to one thing: no Developer ID.**

| Spec step | Status |
|---|---|
| 1. Rename wakey → Yolk | done (2026-08-07, pre-git) |
| 2. `Package.swift`, extract `YolkKit` with tests | done |
| 3. Port CLI onto `YolkKit`, golden parity | **done (2026-08-24)** |
| 4. Xcode project + app target, `AppModel` | **done (2026-08-24)** |
| 5. `MenuBarView` + `SettingsView` | **done (2026-10-09)** |
| 6. Icon assets | **done (2026-10-09)** |
| 7. Signing, entitlements, `make release` | **not started — needs an Apple Developer account** |
| 8. README rewrite, publish tap | **done (2026-10-09)**, except the cask — see below |

There is **no git remote**. The repo is local only.

## Quick start

```sh
make test            # 129 tests — all should pass
make cli             # build the CLI
make app             # build Yolk.app with the CLI embedded and signed
make install-app     # and copy it to /Applications
make check-version   # MARKETING_VERSION vs YolkKit.version
make install         # CLI to ~/.local/bin
```

The tap lives in a separate repo, cloned at `~/Code/homebrew-tap`. Releasing a
new CLI version means bumping the version in all of the places below, tagging,
then updating the formula's `url` and `sha256`.

`make app` needs the `-derivedDataPath` the Makefile passes. Without it
xcodebuild puts the package products and the app in separate build roots and the
app cannot find `YolkAppKit` — the failure reads as an unresolvable module, not
as a path problem.

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
Sources/YolkAppKit/
  AppModel.swift           @Observable wrapper over YolkSession — all app logic
  SettingsStore.swift      UserDefaults boundary, closures like SystemEnvironment
  LoginItemService.swift   SMAppService boundary, same closure style
Sources/yolk/
  main.swift               process only: flags in, events out, signals, exit()
  ArgumentParser.swift     hand-rolled flag parsing → ParseOutcome
  ConsoleRenderer.swift    YolkEvent → stdout/stderr; owns --verbose and the tips
  Usage.swift              the frozen --help and --version text
Sources/YolkTestSupport/   shared fakes; not a product, never linked into a build
App/
  Yolk.xcodeproj/          hand-written, checked in, with a shared Yolk scheme
  Yolk/YolkApp.swift       @main — MenuBarExtra + Settings scenes
  Yolk/MenuBarView.swift   the dropdown
  Yolk/SettingsView.swift  the one-pane settings window
  Yolk/Yolk.entitlements   app-sandbox false, and it must stay that way
Tests/YolkKitTests/        49 tests — engine, driven through SystemEnvironment fakes
Tests/YolkCLITests/        37 tests — parser, renderer, usage goldens
Tests/YolkAppKitTests/     43 tests — AppModel, status strings, login item
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

### From step 4 (app target)

15. **`AppModel` lives in a package target, not in the Xcode target.** The
    spec's layout diagram puts it at `App/Yolk/AppModel.swift`, but the same
    spec says the `.xcodeproj` should stay thin — "a shell around the package,
    not where logic lives". `Sources/YolkAppKit/` keeps it reachable from
    `swift test`, which is where its 28 tests run. `App/Yolk/` holds scenes and
    nothing else. Decided 2026-08-24.

16. **`@Observable` and `didSet` cannot be combined.** The settings properties
    use explicit `access(keyPath:)` / `withMutation(keyPath:)` over
    `@ObservationIgnored` storage. A stored property with a `didSet` that
    assigns to itself — which is how clamping was first written — recurses
    forever and dies with a stack overflow, because the macro rewrites the
    stored property into a computed one and the assignment re-enters the
    setter. Reproduced in isolation, not guessed at. Anyone adding a fourth
    setting must follow the same pattern.

17. **`YolkSession.tick()` is `package`, not internal.** `AppModel`'s tests
    drive real check-loop iterations rather than feeding synthetic events, so
    the wiring itself is under test. `package` keeps it out of YolkKit's public
    API while both test targets can reach it.

18. **The embed phase signs the CLI itself.** Two failures stack up here and
    both were hit for real:
    - `swift build` follows the host architecture, so a universal app shipped
      an arm64-only CLI. The phase now derives `--arch` flags from Xcode's
      `ARCHS`.
    - A lipo-combined universal binary carries no signature, and Apple Silicon
      SIGKILLs unsigned arm64 executables — the embedded CLI died with exit
      137. Xcode does not fix this for you: it seals `Contents/Resources` as
      data, not as nested code. The phase now runs `codesign` with
      `--options runtime`, ad-hoc today and Developer ID at step 7.

19. **Signing is ad-hoc (`CODE_SIGN_IDENTITY = "-"`, style Manual).** There are
    no credentials on this Mac, and automatic signing without a team fails
    outright. Release builds already carry the hardened runtime, so step 7 is a
    change of identity rather than of configuration.

20. **The project file is hand-written.** No XcodeGen or Tuist is installed, and
    the spec calls for a checked-in `.xcodeproj`. Object ids are the readable
    `1A00…0001` series rather than random hex. `plutil -lint` validates it, and
    `make app` is the real check.

21. **`make install` was broken and is fixed.** The Makefile still compiled a
    root-level `main.swift` that step 2 moved into `Sources/yolk/`, and the CLI
    now needs YolkKit so bare `swiftc` could not work either. Regression from
    step 2, found in step 4; `make install` now builds through SwiftPM.

### From steps 6 and 8

25. **Icons are generated by `Tools/make-icons.swift`, not checked in as
    opaque binaries.** The PNGs are committed (Xcode needs them), but they can
    be regenerated and tweaked. The egg outline is a radius modulated by angle
    rather than an ellipse, which reads as organic while staying byte-for-byte
    reproducible.

26. **The tap is a formula, not a cask**, for the reason under Open questions.
    `brew audit --strict --online` passes and `brew test` passes; both were run
    against a real `brew install`, not assumed.

27. **A version bump touches nine files.** `Sources/YolkKit/Version.swift`,
    `VersionTests`, both `MARKETING_VERSION` lines, and the five goldens that
    carry the version in their first line. Nothing automates this, but nothing
    needs to: `make check-version` catches the first two disagreeing and the
    test suite catches the goldens. The captures under `Goldens/originals/`
    deliberately keep saying 1.0.0 — they record what the pre-refactor binary
    actually printed.

28. **The embed phase must stay idempotent.** It is `alwaysOutOfDate`, so it
    runs every build, but Xcode does not track a script phase's side effects
    and skips re-signing the outer bundle on an incremental build. Rewriting an
    unchanged `Contents/Resources/yolk` therefore left its sealed hash stale
    and the app failed `codesign --verify --deep`. The phase now stamps what it
    embedded and does nothing when the binary matches, and re-seals the bundle
    itself when it does replace it. Every build of the app between step 4 and
    2026-10-09 had an invalid signature; it only surfaced when copying the app
    out of the build directory. If this phase is ever edited, re-test all
    three cases: clean build, no-op rebuild, and a real change to the CLI.

29. **The CLI is installed twice on the dev machine.** `make install` puts it
    in `~/.local/bin`, which shadows Homebrew's copy on PATH. Harmless, but it
    means `which yolk` may not be the one you just changed.

### From step 5 (views) and after

22. **Local builds sign with a self-signed certificate.** Ad-hoc signing ties
    the Accessibility grant to the binary's cdhash, so every rebuild silently
    revoked it while System Settings still showed Yolk toggled on — which
    presents as a detection bug in the Settings pane. A self-signed
    "Yolk Local Development" certificate in the login keychain makes the
    designated requirement `identifier + certificate root`, which is identical
    across rebuilds. Verified by building twice and diffing the requirement.

    Recreating it on a new machine, if the keychain item is ever lost:

    ```sh
    openssl req -x509 -newkey rsa:2048 -nodes -days 3650 \
      -keyout k.key -out c.crt -subj "/CN=Yolk Local Development/O=Yolk" \
      -addext "basicConstraints=critical,CA:false" \
      -addext "keyUsage=critical,digitalSignature" \
      -addext "extendedKeyUsage=critical,codeSigning"
    # -certpbe/-keypbe/-macalg are required: OpenSSL 3's defaults produce a
    # PKCS12 that macOS refuses with "MAC verification failed".
    openssl pkcs12 -export -inkey k.key -in c.crt -out c.p12 -passout pass:yolk \
      -name "Yolk Local Development" \
      -certpbe PBE-SHA1-3DES -keypbe PBE-SHA1-3DES -macalg sha1
    security import c.p12 -k ~/Library/Keychains/login.keychain-db -P yolk \
      -T /usr/bin/codesign
    # User-level trust is enough; no admin password needed.
    security add-trusted-cert -r trustRoot -p codeSign \
      -k ~/Library/Keychains/login.keychain-db c.crt
    ```

    Switching to the Developer ID at step 7 changes the requirement once more,
    so it costs exactly one further re-grant.

23. **Swift 6.4 (Xcode 27) cannot infer wide tuple-literal test arguments.**
    The argument-rejection cases are hoisted into an explicitly-typed constant;
    inline, the type checker gives up with "unable to type-check this
    expression in reasonable time". Other parameterized suites still compile
    inline, but this is the first place to look if a new one stops building.

24. **History was rewritten before the first push** (2026-10-09). The early
    `git add -A` blobs are gone: `.git` went from 157MB to 224K, every commit
    is preserved, and the working tree hash is unchanged. All hashes before the
    rewrite are therefore dead — any old clone or note referring to them is
    stale. A pre-rewrite bundle was taken but lives only in a scratch
    directory, not in the repo.

## Open questions

- **The app has no distribution path.** The tap ships a *formula* that builds
  the CLI from source, which needs no signing. A *cask* would install
  `Yolk.app`, and without notarization Gatekeeper blocks it on any machine
  other than the one that built it. So the README tells people to build the app
  themselves. Everything needed for the cask is already in place — the embed
  phase signs the nested CLI, release builds carry the hardened runtime — so
  this unblocks the moment step 7 does.

## Credentials — when they are actually needed

| What | Needed at | Notes |
|---|---|---|
| Nothing | steps 5–6 | all local; ad-hoc signing builds and launches fine |
| Apple ID in Xcode | step 5, first real app run | free tier is enough; without a stable signing identity macOS may force re-granting Accessibility on **every rebuild**, which makes testing the permission flow miserable |
| Developer ID **Application** cert | step 7 | not "Apple Development"; org accounts often restrict this to the Account Holder |
| App Store Connect API key (`.p8`) | step 7 | downloadable **once**; also needs key ID + issuer UUID for `xcrun notarytool store-credentials` |
| GitHub repo (+ optionally `gh`) | step 8 | done — `gh` is installed and authenticated |

There is still **no** Developer ID certificate on the build machine and no
Xcode account signed in, so there is no identity capable of a distributable
signature. Local builds use the self-signed "Yolk Local Development"
certificate instead — see decision 22.

Yolk signs under a **personal** Apple Developer account. Bundle ID
`io.github.coreyervin.yolk` is permanent; changing it after release
invalidates every user's Accessibility grant.

## Next action

Step 7 — Developer ID signing and notarization. It is the only thing left, and
it unblocks the cask, a downloadable release, and the end of re-granting
Accessibility after a rebuild. Needs an Apple Developer account
($99/year), then:

- Developer ID **Application** certificate (not "Apple Development")
- An App Store Connect API key (`.p8`), downloadable **once**, plus its key ID
  and issuer UUID for `xcrun notarytool store-credentials`
- Swap `CODE_SIGN_IDENTITY` from `Yolk Local Development` to the Developer ID —
  the embed phase already switches to a secure timestamp on its own when the
  identity starts with "Developer ID"
- Then `make release`: check-version → test → archive → export → DMG →
  `notarytool submit --wait` → `stapler staple`
- Finally add the cask to the tap and point the README's install section at it

Superseded, kept for reference — step 5 — `MenuBarView` and `SettingsView`. `AppModel` already exposes
everything both need, so this should be presentation only:

- `MenuBarView` — the dropdown in the spec: status line from `isActive` /
  `pauseReason`, `uptimeDescription(asOf:)` and `activityCount` for the detail
  line, the Keep Mac Awake toggle (⌘K), the Stop after… submenu over
  `AppModel.StopAfter.allCases`, and the Accessibility warning row that
  deep-links to
  `x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility`
- `SettingsView` — interval and threshold bounded by `YolkConfig`'s ranges,
  `displaySleepWarning` shown inline, Accessibility status with a button
- **Launch at Login is not built yet.** `SMAppService.mainApp` was deliberately
  left out of step 4 to keep it focused; it needs an injectable seam in
  `AppModel` so it can be tested, and it only registers reliably from
  `/Applications`, which the settings pane must detect and explain
- Views hold no logic. Anything decidable belongs in `AppModel`, where it can
  be tested — that is the whole reason it lives in a package target
