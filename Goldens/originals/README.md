# Provenance — raw output from the pre-refactor binary

Captured 2026-08-24 from `./yolk`, the Aug 7 single-file build that predates the
YolkKit extraction. That binary is gitignored and will not survive forever;
these captures are the only remaining ground truth for the CLI's frozen output,
so the goldens one directory up can be re-derived if they are ever questioned.

| File | Invocation | Covers |
|---|---|---|
| `banner-flagged.txt` | `./yolk -t 5s` | banner, `auto-exit at`, timeout shutdown |
| `banner-bare.txt` | `./yolk`, SIGTERM | banner, startup tip, `terminated` shutdown |
| `banner-verbose.txt` | `./yolk -v`, SIGINT | banner with no tip, `stopped` shutdown |
| `banner-ticks.txt` | `./yolk -v -i 5 --threshold 300`, SIGINT | verbose per-tick idle lines |

Machine-dependent values appear verbatim here (pid, wall-clock times, this
Mac's `pmset` settings). The goldens replace them with `%PID%` / `%AUTOEXIT%`
tokens so the tests do not depend on timezone or process id.

Two paths are absent because this Mac could not produce them: the Accessibility
permission warning (already granted here) and the display-sleep warning
(display sleep is longer than `threshold + interval + 5`). Both are static text
with interpolated numbers, asserted directly in `ConsoleRendererTests`.
