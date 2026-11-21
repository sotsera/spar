# M1c step 7: Linux window tests (evidence)

Recorded 2026-11-20 for decision "Linux window tests (implementation record; choices for review; 2026-11-20)". Host:
Windows 11 Pro 10.0.26320 (32 CPUs), WSL 2 Ubuntu 24.04 (kernel 6.18.58.1), SDK 11.0.100, WebKitGTK 2.52.6, GTK 4.14.5,
Xvfb 21.1.12 unpacked without root (`docs/evidence/m1c-host/wsl.sh tools`), WebView2 Runtime 157.

## How it was run

- Linux: `docs/evidence/m1c-window-tests/wsl.sh sync client appmodel build test window windowaot probe linuxhost` on a copy
  of the working tree in `/var/tmp/spar-m1c-window-tests/repo`. The window tests start their own Xvfb (display :140 on an
  abstract socket only) and their own D-Bus session; `DISPLAY` and `WAYLAND_DISPLAY` were unset, nothing ran on WSLg.
  After each run the script counted the processes left whose environment names a test runtime folder: 0 every time.
  Another agent's AT-SPI spike ran in the same distribution; its processes were not touched.
- Windows (Git Bash, repository root): `dotnet build Spar.slnx -c Debug`; `dotnet test --solution Spar.slnx -c Debug
  --no-build --filter-not-trait "Surface=Window" --filter-not-trait "Surface=BlackBox"`; `dotnet test --project
  tests/Spar.WindowTests -c Debug --no-build`; the NativeAOT lane as `ci.yml` runs it; `samples/Notebook.EndToEndTests`
  with `--filter-trait "Surface=Window"`; `tests/AppModel.Tests`; `dotnet run eng/check-size.cs`; `npm test` in
  `src/Spar.Client`. Windows test windows stayed offscreen and never activated.

## Results

| Run | Total | Passed | Skipped / not run | Failed | Time | Log |
|---|---:|---:|---:|---:|---:|---|
| Linux `Spar.WindowTests`, CoreCLR (last run) | 124 | 94 | 30 | 0 | 48.9 s | `m1c-window-tests/linux-window-coreclr.log` |
| Linux, CoreCLR, the four runs after the last input fix | 4 × 124 | 4 × 94 | 4 × 30 | 0 | 48.3–49.7 s | `linux-window-coreclr-repeats.txt` |
| Linux `Spar.WindowTests`, NativeAOT (19,623,696 B) | 124 | 94 | 18 + 12 not run | 0 | 50.5 s | `linux-window-aot.log` |
| Linux host tests left in `Spar.Tests` (their own processes) | 222 | 217 | 5 | 0 | 4 min 58 s | `linux-host-tests.log` |
| Linux solution, as CI (no Window, no BlackBox) | 1,089 | 986 | 103 | 0 | 11.1 s | `linux-solution.log` |
| Windows `Spar.WindowTests`, CoreCLR | 124 | 112 | 12 | 0 | 1 min 27 s | `windows-window-coreclr.log` |
| Windows `Spar.WindowTests`, NativeAOT | 124 | 112 | 12 not run | 0 | 78.0 s | `windows-window-aot.log` |
| Windows solution, as CI | 1,089 | 1,004 | 85 | 0 | 22.9 s | `windows-solution.txt` |
| Windows Notebook window journeys, CoreCLR | 10 | 10 | 0 | 0 | | |
| Windows `tests/AppModel.Tests` | 11 | 11 | 0 | 0 | | |
| `src/Spar.Client` Vitest (the engine's tests included) | 266 | 266 | 0 | 0 | 2.2 s | |

The Linux skips: the black-box fixture's 12 tests and the budget's Production launch (the Linux fixture waits for the
AT-SPI spike), the 5 `SessionEndHostTests` (Windows' `WM_ENDSESSION`), `InstallPathTests` (Windows' `MAX_PATH`),
`ThemeTests.Each_window_keeps_its_own_scheme_beside_another` (one scheme at a time on Linux),
`PageApiTests.Page_errors_reach_the_apps_log_through_the_host` (the Linux host does not report page errors yet),
`PageDefaultsTests.Navigations_open_no_connection_to_the_loopback_address` (port 443 needs root), and the 12 explicit
Sandbox tests. The Windows skips are the 12 explicit Sandbox tests.

## Numbers

| | Linux CoreCLR | Linux NativeAOT | Windows NativeAOT |
|---|---:|---:|---:|
| Window test: open, 8-step journey, close, median (p95), n = 20 | 237.7 ms (246.1) | 225.0 ms (240.6) | 325.6 ms (345.5) |
| Open to the page's Ready | 94.7 ms | | 139.8 ms |
| Journey | 129.4 ms | | 161.3 ms |
| Close (teardown and the renderer's exit) | 13.6 ms | | 16.2 ms |
| The view and its session ("controller requested" to "controller") | 8.0 ms | | |
| First window of the test process (check, session, GTK, window) | 492 ms | 273 ms | 440–474 ms |
| Click (checks, trusted press, landed), median (p95), n = 100 | 16.3 ms (17.3) | 16.4 ms (17.7) | 33.4 ms (35.3) |
| Key press | 1.34 ms (6.25) | 1.24 ms (5.40) | 1.01 ms (4.03) |
| Fill | 1.54 ms | | |
| Viewport screenshot (n = 20) | 21.0 ms | 20.6 ms | 52.5 ms |
| Role lookup (GetByRole, count) | 0.37 ms | | |
| The whole window suite | 48.9 s | 50.5 s | 78.0 s (CoreCLR 86.8 s) |

Budget (plan, "Provisional budgets"): a Linux window test at most 500 ms median; 225–238 ms measured. Windows' 700 ms:
325.6 ms in this run (298–306 ms in M1b step 8a; this run had WSL's lanes just before it).

## Found while building

The pitfalls of "Linux host" dated M1c step 7: WebKitGTK's `prefers-color-scheme` follows the default display's GTK
settings for every view (a view on a second display with its own preference stayed light; the default display's setting
turned it dark); the `spar` scheme must be registered on a web context of each window's own; non-BMP keys are
"Unidentified" with no `keypress`, and a key with no physical code is `code` "Unidentified"; XTEST's keymap holds some
characters on keys that type something else (è, €, ñ); a spare keycode can be one WebKit names (F19), so keycodes with no
evdev key are taken first; a binding made by one test between another's press and its delivery typed the wrong
character (about 1 in 6 suite runs) until spare strokes were serialized; windows mapped at (0, 0) took clicks of other
tests' windows until each window got a place of its own; a scroller-clipped element was clicked through its container;
a window closed while it waited for its first-load turn kept the turn (the 9-minute suite).

## Review runs (2026-11-20)

- **Production boundary** with the Linux test-only markers added to `tests/Spar.BuildTests/ProductionBoundary.cs`
  (`TestEnvironment`, `TestViews`, `SPAR_TEST_DISPLAY`, the private Xvfb's screen `6400x4200x24`, `spar-test-`): passed
  on Windows (1 of 1, 83 s) and on Linux in WSL (1 of 1, 41 s, `../m1c-security-defaults/wsl.sh sync boundary`).
- **Windows window suite, CoreCLR:** 124, 112 passed, 12 skipped, 0 failed (81.6 s); the solution built with 0 warnings.
- **Linux window suite, CoreCLR, four runs** (`wsl.sh sync client build window`, then `window` three times): each 124
  with 94 passed, 30 skipped, 0 failed, and 0 processes left. **The first run ended with exit code 1**: the test
  platform's summary counted `error: 1` and named nothing, and its TRX report says 94 of 94 passed with 0 errors, so the
  error came after the tests, from the test process's end. The other three runs, and the five of the build, exited 0:
  one in nine. Not explained; CI's runs will show how often it happens.
