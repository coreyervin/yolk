# Yolk for Windows

A C# port of Yolk: the same engine, CLI, and tray app as the Mac version, in
its own corner of the repo. **Not yet tried on a real Windows PC** — CI builds
it, runs the tests, and smoke-tests the CLI on a Windows runner, but the
behaviors in [Testing on a real PC](#testing-on-a-real-pc) need a person at a
keyboard.

## Layout

Clean Architecture, with the use cases as vertical slices. Dependencies only
point inward:

```
Yolk.Cli, Yolk.Tray  ──►  Yolk.Infrastructure  ──►  Yolk.Application  ──►  Yolk.Domain
   (presentation)          (Win32 adapters)          (slices + ports)       (rules)
```

| Project | What it holds | Mac counterpart |
|---|---|---|
| `src/Yolk.Domain` | `KeepAwakeSession`, the rules as a pure aggregate: readings in, events out. `YolkConfig`, `StopAfter`, durations. References nothing. | `YolkKit` |
| `src/Yolk.Application` | `Features/`, one folder per slice, each holding its request, result, and handler. `Abstractions/`, the ports. `Sessions/`, the live session the slices share. | `YolkKit`, `YolkAppKit` |
| `src/Yolk.Infrastructure` | One Win32 adapter per port, grouped by concern (`Power/`, `Input/`, `Sessions/`, `Display/`, `Persistence/`, `Startup/`, `Native/`). The only code that calls Win32. | `SystemEnvironment.live` |
| `src/Yolk.Cli` | `yolk.exe`: parse flags, compose, render events. Published with NativeAOT. | `Sources/yolk` |
| `src/Yolk.Tray` | `Yolk.exe`: a WinForms shell that calls slices and redraws on events. | `App/` |

The slices in `Yolk.Application/Features`:

| Slice | Requests |
|---|---|
| `KeepAwake` | `StartKeepAwake`, `StopKeepAwake`, `ReconfigureKeepAwake` |
| `ActivityCheck` | `CheckActivity`, run on every timer tick |
| `Settings` | `GetSettings`, `UpdateSettings`, `ApplyStoredSettings` |
| `LaunchAtSignIn` | `GetLaunchAtSignIn`, `SetLaunchAtSignIn` |
| `Status` | `GetStatus`, what the tray icon and menu show |

Presentation resolves a slice's `ICommandHandler<>` or `IQueryHandler<,>` from
Microsoft.Extensions.DependencyInjection and calls `Handle`. There's no
mediator library. `AddApplication()` and `AddInfrastructure()` list every
registration explicitly, which keeps the CLI NativeAOT-compatible.

**Adding a slice:** add a folder under `Features/` with the request, its
result, and an `internal` handler. Register the handler in
`Yolk.Application/DependencyInjection.cs`, and add any new port to
`Abstractions/Ports.cs` with its Win32 adapter in Infrastructure.
`Yolk.Architecture.Tests` fails if a handler sits outside `Features/` or isn't
registered, or if native code shows up outside Infrastructure.

Tests mirror the layers. `Yolk.Application.Tests` drives each slice through
the real `AddApplication()` container with the ports faked (`TestHost`).

## Build and test

Needs the .NET 10 SDK.

```sh
dotnet test windows/Yolk.slnx
```

Publishing needs Windows. The CLI needs the C++ build tools from Visual
Studio too, for NativeAOT:

```sh
dotnet publish windows/src/Yolk.Cli -c Release -r win-x64 -o dist/cli
dotnet publish windows/src/Yolk.Tray -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/tray
```

Or skip building: every CI run on `main` uploads both executables as the
`yolk-windows-x64` artifact, on the run's page under **Actions**.

## Keeping it in step with the Mac

- **Output.** The CLI is tested against the Mac's `Goldens/`, not copies of
  its own. `tests/Yolk.Cli.Tests/MacGoldens.cs` lists every wording difference
  allowed (`Ctrl-Cmd-Q` → `Win+L`, and so on); anything else fails.
- **Version.** `Directory.Build.props` must match
  `Sources/YolkKit/Version.swift`. `make check-version` and `VersionTests` both
  enforce it.
- **Behavior.** The Mac's `YolkSession` is split here into the
  `KeepAwakeSession` aggregate (the rules) and the `KeepAwake` and
  `ActivityCheck` slices (the orchestration). Together they behave the same,
  and `CheckActivityTests` ports the Mac's session tests. A change to the
  engine on one side needs the same change, and the same test, on the other.

## How the Mac mechanisms map

| Port | Mac | Windows adapter |
|---|---|---|
| Stay awake (`IPowerRequestService`) | `IOPMAssertion` (`PreventUserIdleSystemSleep`) | `PowerSetRequest(PowerRequestSystemRequired)` |
| Idle time (`IIdleTimeProvider`) | `CGEventSource` | `GetLastInputInfo` |
| Nudge (`IInputSimulator`) | Mouse-moved event at the cursor | `SendInput`, zero-distance mouse move |
| Locked / switched out (`ISessionStateProvider`) | `CGSessionCopyCurrentDictionary` | `WTSQuerySessionInformation(WTSSessionInfoEx)` |
| Display sleep (`IDisplaySettingsProvider`) | `pmset -g` | Active power plan's "turn off display after" |
| Permission | Accessibility grant | None needed |
| Launch at sign-in (`IStartupRegistration`) | `SMAppService` | `HKCU\…\CurrentVersion\Run` |
| Settings (`ISettingsRepository`) | `UserDefaults` | `HKCU\Software\Yolk` |

## Testing on a real PC

CI proves it builds, the logic is right, and the Win32 calls don't crash.
These need a real session:

1. **The nudge resets the idle timer.** Run `yolk -v --threshold 5 -i 5` and
   leave the mouse alone. Expect `simulated activity (#1)`, `(#2)`, …, not a
   "had no effect" warning. This is the riskiest assumption in the port.
2. **Slack stays active.** With the above running, Slack should not go away
   after its ~10 minutes. Teams too, if you use it.
3. **The machine stays awake.** Set sleep to 1–2 minutes in Settings → System
   → Power, run `yolk`, and walk away. `powercfg /requests` (admin) should list
   `yolk: keeping the PC awake while agents run` under SYSTEM.
4. **Locking pauses.** Win+L with `-v` running: expect `screen locked or
   session inactive — paused`, and no nudges until you unlock.
5. **Admin windows.** Focus an administrator terminal and idle past the
   threshold. Windows may block the nudge; yolk should warn rather than fail
   silently.
6. **Modern Standby laptops.** Some laptops ignore "system required" once the
   screen turns off. Check the machine is still awake after the display sleeps.
7. **The tray app.** Left-click toggles, right-click opens the menu. Check the
   icon changes (colour / grey / faded when locked), Stop after, Settings, and
   Launch at sign-in — Task Manager → Startup apps should list Yolk.
8. **Ctrl-C and closing the window.** Both should print the shutdown summary,
   and `powercfg /requests` should no longer list yolk.
