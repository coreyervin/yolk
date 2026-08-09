# Yolk

Keep your Mac awake **and** Slack active while agents run — `caffeinate` for the
whole workday.

`caffeinate` stops the Mac from sleeping, but Slack still marks you away after
~10 minutes without keyboard/mouse input, even when you're at the machine
watching agents work. `yolk` fixes both:

1. **Stays awake** — holds a `PreventUserIdleSystemSleep` power assertion
   (same as `caffeinate -i`) for as long as it runs.
2. **Stays active** — when system idle time crosses a threshold (default 60s),
   it posts a synthetic mouse-moved event *at the cursor's current position*.
   The cursor never visibly moves, but the system idle timer resets — and that
   timer is exactly what Slack's desktop app reads to decide "away".

If you're actually typing or mousing, idle never reaches the threshold and
yolk does nothing. It only fills the gaps.

<sub>Named for the fried egg — the project started life as "wakey", as in
*wakey wakey, eggs and bakey*. The egg outlived the name.</sub>

## Install

```sh
make install        # builds and installs to ~/.local/bin/yolk
```

(Override with `make install PREFIX=/usr/local` if you prefer.)

## Usage

```sh
yolk               # run until Ctrl-C
yolk -t 8h         # stop automatically after 8 hours
yolk -v            # log every check and simulated event
```

| Flag | Meaning | Default |
|------|---------|---------|
| `-i, --interval <sec>` | seconds between idle checks (5–120) | 30 |
| `--threshold <sec>` | idle seconds before simulating activity (5–300) | 60 |
| `-t, --timeout <dur>` | auto-exit after `8h`, `90m`, `45s`, `1d`, or seconds | run forever |
| `-v, --verbose` | log every check | off |

`-t` matches `caffeinate -t` semantics (timeout). The threshold is capped at
300s so Slack's ~10-minute away timer can never be reached by accident.

## First run: Accessibility permission

Posting synthetic input requires **Accessibility** permission for your
terminal app. yolk prompts on first run; if the prompt doesn't appear, add
your terminal manually under **System Settings → Privacy & Security →
Accessibility** and restart yolk.

- Keeping the Mac awake works without the permission — only the Slack half
  needs it. yolk warns you (once) if its simulated events aren't landing.
- **tmux users:** macOS attributes the permission to the *responsible
  process*. Inside tmux that's the tmux server, not your terminal — if events
  aren't landing, grant the permission to whatever macOS prompts for.

## Walking away

**Lock your screen (Ctrl-Cmd-Q) when you leave.** yolk pauses simulation
while the screen is locked, so:

- Slack correctly shows you away (it flips on lock regardless),
- your display gets to sleep,
- your agents keep running (the power assertion stays held).

## Caveats

- While yolk runs and the screen is unlocked, your display won't sleep and
  auto-lock won't engage — synthetic activity counts as real activity to
  macOS. Locking manually is the walk-away gesture. (This holds as long as
  your display-sleep timeout is longer than `threshold + interval`; yolk
  warns at startup if it isn't.)
- yolk also pauses when your session is switched out via fast user
  switching — it never posts input into someone else's session.
- Closing the lid still sleeps the Mac (no clamshell override, same as
  `caffeinate -i`).
- If yolk is killed or crashes, the kernel releases the power assertion
  automatically — nothing is left stuck.

## Roadmap

A native macOS menu bar app is designed and pending implementation — same
engine, with a toggle, timer presets, and a settings UI. See
[the design spec](docs/superpowers/specs/2026-08-07-yolk-macos-app-design.md).

Also still on the list:

- `yolk -- <cmd>`: stay awake only while a wrapped command runs
  (caffeinate-style process scoping).
