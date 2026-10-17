# Yolk for Windows

A C# port of Yolk: the same engine, CLI, and tray app as the Mac version, in
its own corner of the repo. **Not yet tried on a real Windows PC** — CI builds
it, runs the tests, and smoke-tests the CLI on a Windows runner, but the
behaviors in [Testing on a real PC](#testing-on-a-real-pc) need a person at a
keyboard.

## Layout

| Project | What it is | Mac counterpart |
|---|---|---|
| `src/Yolk.Core` | The engine: config, session, events. No OS calls. | `YolkKit` |
| `src/Yolk.Platform.Windows` | `Win32Environment` — the only code that calls Win32 | `SystemEnvironment.live` |
| `src/Yolk.Cli` | `yolk.exe`, published with NativeAOT | `Sources/yolk` |
| `src/Yolk.AppCore` | `AppModel` — the tray app's logic, unit-tested | `YolkAppKit` |
| `src/Yolk.Tray` | `Yolk.exe`, a thin WinForms tray shell | `App/` |
| `tests/*` | xUnit suites, ported from the Swift ones | `Tests/` |

Platform code stays in `Yolk.Platform.Windows` and `Yolk.Tray`. Everything
else builds and tests on any OS, including the tray app, which cross-compiles
from a Mac.

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
- **Behavior.** `YolkSession` is a line-for-line port. A change to the engine
  on one side needs the same change, and the same test, on the other.

## How the Mac mechanisms map

| | Mac | Windows |
|---|---|---|
| Stay awake | `IOPMAssertion` (`PreventUserIdleSystemSleep`) | `PowerSetRequest(PowerRequestSystemRequired)` |
| Idle time | `CGEventSource` | `GetLastInputInfo` |
| Nudge | Mouse-moved event at the cursor | `SendInput`, zero-distance mouse move |
| Locked / switched out | `CGSessionCopyCurrentDictionary` | `WTSQuerySessionInformation(WTSSessionInfoEx)` |
| Display sleep | `pmset -g` | Active power plan's "turn off display after" |
| Permission | Accessibility grant | None needed |
| Launch at login | `SMAppService` | `HKCU\…\CurrentVersion\Run` |
| Settings | `UserDefaults` | `HKCU\Software\Yolk` |

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
