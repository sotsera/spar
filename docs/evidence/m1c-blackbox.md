# M1c step 7, part 2: the Linux black box and SIGTERM

Recorded 2026-11-20. What it shows: `SparBlackBox` on Linux over AT-SPI (decision "The Linux black box: AT-SPI over GDBus; the dialog's title; SIGTERM as a session end"), `SIGTERM` and `SIGINT` as the session end of the Linux host, the host-lifecycle suite (`BlackBoxFixtureTests`, `SessionEndHostTests`, `BudgetTests`) on both hosts, and the Linux host tests that ran BridgeProbe from outside, moved from `tests/Spar.Tests` into `tests/Spar.WindowTests/Linux/` as black-box tests. Decision "The Linux black box and SIGTERM (implementation record; choices for review; 2026-11-20)".

Machines: Windows 11 Pro 10.0.26320 (32 CPUs), WebView2 Runtime 157; WSL 2 Ubuntu 24.04.5 (kernel 6.18.58.1), GTK 4.14.5, WebKitGTK 2.52.6, at-spi2-core 2.52.0 (installed in the distribution; nothing installed for this), Xvfb 21.1.12 unpacked by `../m1c-host/wsl.sh tools`, SDK 11.0.100. The runner is `m1c-blackbox/wsl.sh` (its own copy at `/var/tmp/spar-m1c-blackbox`, its own display numbers chosen by the tests: :140 and up for the Window surface, :180 and up for the black box). During the last runs WSL's DNS was down (no name resolved), so NuGet's audit could not reach nuget.org and failed restore with NU1900: those runs set `NuGetAudit=false` in the environment (packages came from the cache; no other setting changed).

## What was built

| Where | What |
|---|---|
| `src/Spar.Linux/SessionEnd.cs`, `LinuxApp.cs`, `AppWindow.Closing.cs` | `SIGTERM`/`SIGINT` through `PosixSignalRegistration`, registered once the window has started, only in an app; `AppWindow.EndSession` (the session-end close in place of an open request); the session end run off the loop and cut at 4 s; the final line, the log flushed, exit 0; a second signal exits 128 + its number |
| `src/Spar.Linux/Testing/TestWindow.cs`, `src/Spar/Testing/SparTestWindow.cs`, `src/Spar.Windows/Testing/TestWindow.cs`, `src/Spar.Testing/Window/SparWindow.cs` | `ISparTestWindow.EndSessionAsync` (internal): Windows' `WM_QUERYENDSESSION` + `WM_ENDSESSION`; on Linux the signal's path without the signal |
| `src/Spar.Testing/BlackBox/Linux/` | `Gio.cs` (GDBus and GVariant imports), `DBusClient.cs` (calls and replies as managed values, `DBusException.IsGone`), `AtSpi.cs` (the app's accessibility bus: by pid, roots, nodes), `AtSpiAutomation.cs` (`IBlackBoxAutomation`), `LinuxDesktop.cs` (private Xvfbs, a private D-Bus session per launch, the prerequisites), `LinuxAppProcess.cs` (`IBlackBoxProcess`: `KillOnParentExit`, the launch's environment tag, signals) |
| `src/Spar.Testing/BlackBox/SparBlackBox.cs`, `…Options.cs`, `…App.cs`, `SparProject.cs`, `TestDataRoots.cs` | The Linux launch, folders (`HOME`, `XDG_*`, `TMPDIR`, `XDG_RUNTIME_DIR` in `/dev/shm`), environment filter, internal options for Spar's own tests, `SparProject.Built` |
| `src/Spar.Sdk/Sdk/Sdk.targets` | `SparBlackBoxPublish` on by default on Linux too |
| `src/Spar.Linux/Testing/SchemeGate.cs`, `TestWindowProcess.cs`, `TestEnvironment.cs` | Two CI-image fixes from the coordinator's report: the one-scheme gate reentrant per holder; no helper ended at `ProcessExit` |
| `tests/Spar.WindowTests/Linux/` | The moved Linux host tests (`LinuxHostTests`, `LinuxHangTests`, `LinuxPageSecurityTests`, `LinuxStartupTests`), `LinuxSessionEndTests` (new), `ProbeApp` (the launcher on `SparBlackBox`), `X11Frames`, `X11Close` |

## Results

### Windows (the host machine)

| Command | Result |
|---|---|
| `dotnet build Spar.slnx -c Debug` | 0 warnings, 0 errors |
| `dotnet test --solution Spar.slnx -c Debug --no-build --filter-not-trait "Surface=Window" --filter-not-trait "Surface=BlackBox"` (as `ci.yml`) | 1,028: 1,005 passed, 23 skipped, 0 failed (the first run had 2 failures: the static-state list lacked the new fields, fixed; `WireShapes.EndToEndTests` failed once and passed alone and in the rerun) |
| `dotnet test --project tests/Spar.WindowTests -c Debug --no-build` (CoreCLR) | 192: 112 passed, 80 skipped (the Linux probe tests and Linux-only cases, the 12 explicit Sandbox tests), 0 failed |
| `Spar.WindowTests` NativeAOT (`dotnet publish … -p:SparTestLane=Aot`, then the executable) | 192: 112 passed, 68 skipped, 12 not run (explicit), 0 failed; the publish 0 warnings |
| `samples/Notebook.EndToEndTests`, `Surface=BlackBox` / `Surface=Window` | 6 of 6 / 10 of 10 passed (black box again after the Linux changes: 6 of 6) |
| `BlackBoxFixtureTests` and `SessionEndHostTests` after the last changes | 25 of 25 |
| `dotnet run eng/check-size.cs` | 523 files, 0 errors, 0 warnings |
| `tests/AppModel.Tests` | 11 of 11 |
| `Spar.BuildTests`, `*The_production_executable_carries_its_page_and_no_test_or_loopback_code` | 1 of 1 (the new markers `AtSpiAutomation`, `AtSpiBus`, `LinuxAppProcess`, `LinuxDesktop`, `SPAR_BLACK_BOX_LAUNCH`, `spar-bb-` absent from the Production Notebook) |

### Linux (WSL, `m1c-blackbox/wsl.sh`)

All on the final tree (after the last change), unless a row says otherwise.

| Command (step of `wsl.sh`) | Result |
|---|---|
| `build`: `dotnet build Spar.slnx -c Debug` | 0 warnings, 0 errors |
| `test`: the solution as `ci.yml` runs it (no `Window`, no `BlackBox`) | 1,028: 987 passed, 41 skipped, 0 failed |
| `window`: `Spar.WindowTests` on CoreCLR (window tests, black box, the probe tests on the Production probe), five times in a row | each 192: 164 passed, 28 skipped, 0 failed, exit 0, 0 processes left; 316–319 s each |
| `window`, five earlier runs (before the profile-folder fix and the last probe-test changes) | four clean; one with 1 failure, `PageApiTests.A_form_is_filled_checked_selected_and_sent_with_trusted_input` (an in-process window test of part 1: "ordered" never appeared in 5 s), not seen again in the 5 later runs |
| `windowdevprobe`: the probe tests on the Development build | 67: 64 passed, 3 skipped (the flood's numbers, the two Notebook tests without `SPAR_LINUX_NOTEBOOK`), 0 failed |
| `windowdev`: the probe tests with the WebAssembly build and the Notebook | 67: 62 passed, 5 skipped (Development-only: the Wait schedule, the automated mode, the raised minimum, the second loss's exit 5; the flood's numbers), 0 failed |
| `notebook`: `Notebook.EndToEndTests`, `Surface=BlackBox` | 6 of 6 (its first run on Linux found the missing profile-folder message; fixed) |
| `windowaot`: `Spar.WindowTests` published with NativeAOT | 192: 164 passed, 16 skipped, 12 not run (explicit Sandbox), 0 failed, 0 processes left; the publish 0 warnings (before the profile-folder fix: the same) |
| `boundary`: the production-boundary build test | 1 of 1 (the Notebook published in a clean copy carries none of the new markers) |

### Numbers (Linux; Production NativeAOT executables)

| What | Measured |
|---|---|
| Launch to ready (`ui-ready` and the window over AT-SPI), WindowHost, first launch on a new root | 228–255 ms (6 runs) |
| Launch to `ui-ready`, warm, from the process's start (BudgetTests, n = 10) | 213.1 ms median (203.7–224.2); working set 159.4 MB, with WebKit's processes 374.5 MB |
| A page button found and invoked over AT-SPI (a walk of WindowHost's page, about 100 nodes) | 123–147 ms |
| Close (`window.close`) to exit | 23–49 ms |
| The failed relaunch's dialog found, from the launch (the host's 2 s wait included) | 2,094–2,149 ms (7 runs) |
| "Close" pressed (`DoAction(click)`) to exit 8 | 270–283 ms (GTK shows the press about 250 ms first) |
| `SIGTERM`/`SIGINT` to exit 0, nothing slow | 22–37 ms (the final line's hold 11–29 ms) |
| `SIGTERM` with a 15 s disposal | held 3,998–4,001 ms, exit 0 4,004–4,008 ms after the signal |
| A second signal to exit 143 | 7–8 ms |
| A test window's session end (in-process) | fast 26–31 ms; slow guards and disposal 4,000 ms; a renderer lost 300 ms in: closed 1 ms after the loss was seen |

### Found after the runs, fixed, checked once

After the ten full runs `/dev/shm` held 116 `spar-bb-*` runtime folders: a root that several launches share (`BudgetTests`, the Notebook's tests that keep their data) leaves its runtime folder to whoever deletes the root, often the test itself. `LinuxDesktop` now deletes the test process's own at exit (folders only; nothing is ended) and, at its first launch, those of test processes that are gone. A run of `BlackBoxFixtureTests`, `BudgetTests`, `SessionEndHostTests` and `LinuxStartupTests` afterwards (46: 42 passed, 4 skipped, 0 failed; `run-window.log`) left 0 `spar-bb-*` folders and 0 processes. The ten full runs above were before this change; it touches no test's path.

## The Linux tests before and after

| Before (`tests/Spar.Tests`, run with `SPAR_LINUX_HOST_APP` and `SPAR_LINUX_TEST_DISPLAY`) | After |
|---|---|
| `LinuxHostTests` (11 tests, 18 cases) | `tests/Spar.WindowTests/Linux/LinuxHostTests.cs`, the same 18 cases on `ProbeApp` |
| `LinuxHangTests` (9) | `Linux/LinuxHangTests.cs`, the same 9; five now also answer their dialogs on the Production probe through AT-SPI (they had returned early or skipped there) |
| `LinuxPageSecurityTests` (18 cases) | `Linux/LinuxPageSecurityTests.cs`, the same 18; WebAssembly after Reload page now runs on the Production probe too; a relative profile folder now shows its start-up message, answered through AT-SPI |
| `LinuxStartupTests` (14 tests, 17 cases) | `Linux/LinuxStartupTests.cs`, the same 17 (the mount namespaces through the fixture's `Wrapper`, the missing display through `Display`, the fake portal through `SessionBus`) |
| `LinuxHostUnitTests`, `LinuxStartupUnitTests` | stay in `tests/Spar.Tests`, plus the scheme gate's test |
| `LinuxAppRun.cs`, `X11Close.cs`, `X11Frames.cs` | `ProbeApp.cs` on `SparBlackBox` (deleted `LinuxAppRun.cs`); the X11 helpers moved |
| `Spar.WindowTests`: `BlackBoxFixtureTests` (12), `SessionEndHostTests` (5), the black-box budget skipped on Linux | 10 of 12, 5 of 5 and the budget run on Linux; new `LinuxSessionEndTests` (5 cases) |
| `Notebook.EndToEndTests` black box (6) skipped on Linux | 6 of 6 run on Linux |

`tests/Spar.Tests` has 33 files (was 40). Every case that passed before passes after, on the Production probe (`window`, `windowdev`) and on the Development build (`windowdevprobe`); the Development-only cases skip on the Production probe as before.

## Files of this record

| Path | What |
|---|---|
| `m1c-blackbox/wsl.sh` | The runner |
| `m1c-blackbox/final-runs-summary.log` | The final chain: build, solution, five full window runs, the Development, WebAssembly and Notebook runs, NativeAOT, the boundary |
| `m1c-blackbox/run-*.log` | The last log of each step (`run-window.log` is the check after the runtime-folder fix) |
| `m1c-blackbox/linux-*.txt` | What the tests recorded on Linux (black box, session end, budgets, hangs, start-up, page security, security defaults, the Notebook's numbers) |
| `m1c-blackbox/windows-runs.txt` | The Windows runs' totals |
| `m1c-blackbox/container-run-{1,2,3}.log` | The first three container runs (before the fixes of 2026-11-21) |
| `m1c-blackbox/container-failures.txt` | The container failures: logs, the cores' backtraces, the measurements, the withdrawn theory |
| `m1c-blackbox/alert-dialog-race.c`, `m1c-blackbox/xvfb-reset.py`, `m1c-blackbox/xvfb-reset.sh` | The reproductions of the dialog race and the Xvfb reset |
| `m1c-blackbox/final-sequence-{1,2-stopped,4,5,6}.log` | The full sequences after the fixes |

## Not verified

- A real desktop session (GNOME, KDE) and Wayland: only X11 under Xvfb; `systemd`'s or logind's real session end (only `kill`).
- The CI image (the other agent's work) with these tests: not run here.
- Real input from the fixture on Linux (`SparBlackBoxWindow`'s internal Sandbox input): not built, a gap; the Linux host tests send XTEST themselves.
- `TopLevelWindowsAsync`'s menu items for a WebKitGTK context menu, `FocusAsync` and `BoundsAsync` on Linux: built, not exercised by a test.
- Two test processes on one machine at once.
- The Windows Sandbox lanes (they need the user).

## The CI container (2026-11-21)

The image `spar-linux-ci:m1c-step7`, rebuilt from `eng/linux-ci/Dockerfile` with a new layer (`python3-gi`, `libglib2.0-bin`; the earlier layers cached), run as `ci.yml` runs it: `docker run -d --name spar-linux-ci-bb --init --shm-size=1g --security-opt seccomp=unconfined -e CI=true -v "<repo>:/src:ro" spar-linux-ci:m1c-step7 sleep infinity`, then `run.sh check copy client build window windowaot apps blackbox blackboxdev`, each run in a fresh container removed afterwards (logs: `m1c-blackbox/container-run-{1,2,3}.log`).

| Step | Run 1 (before the fixes below) | Run 2 | Run 3 |
|---|---|---|---|
| `check` | failed (the switch looked in `~/repo` before `copy`; fixed) | passed | passed |
| `window`: `Spar.WindowTests` / Notebook journeys | 66 (62 passed, 4 skipped) / 9 of 10 | 66 (62, 4) / 9 of 10 | 66 (62, 4) / 10 of 10 |
| `windowaot`: the same with NativeAOT | 66 (62, 4) / 9 of 10 | 66 (62, 4) / 9 of 10 | 66 (62, 4) / 9 of 10 |
| `apps` | passed | passed | passed |
| `blackbox` (`--no-skips`): `Spar.WindowTests` / Notebook | 81: 78 passed, 3 failed (`gio` missing, `/proc/sys` read-only, the roots race) / not run | 80: 79, 1 failed (the session end's final line, since fixed) / not run | 80: 79, 1 failed (`A_missing_bwrap…`: its GTK message did not show; not reproduced) / not run |
| `blackboxdev` (`--no-skips`) | 64: 62, 2 failed (`gio`, `/proc/sys`) | 63: 62, 1 failed (the Development probe exited 139 after its window was destroyed, `A_page_that_hangs_before_it_connects…`; not reproduced) | 63 of 63 |

The step stops at its first failed run, so the Notebook's black box did not run in the container (6 of 6 in WSL). No test was skipped in a `--no-skips` step; 0 test processes were left in the container after each run. The Notebook journeys' failures (characters lost while typing, a stale list count) are in the other agent's newly enabled Linux journeys, not changed here. The exit 139, the missing start-up message and the lost characters are explained and fixed in the next section.

## The container failures: causes and fixes (2026-11-21)

The coordinator's follow-up: the causes of the three failures above, with evidence (`m1c-blackbox/container-failures.txt`: the logs, the cores' backtraces and the measurements; `m1c-blackbox/alert-dialog-race.c` and `m1c-blackbox/xvfb-reset.py`, the reproductions).

| Failure | Cause, as measured | Fix |
|---|---|---|
| Characters lost in the Notebook's window journeys | The X server keeps one keyboard state per display, which parallel test windows share: one test's Control+End came while another's Control was down, so its Control never reached its page and the other's release ended it early; End alone moved the caret to the end of the first line. 6 of 829 instrumented keys never reached their page, all sent while the document lacked the focus. | `XInput` holds the input for one window from its first key or button down to its last up (other windows' keys, pointer events and focus changes wait); a window whose document lacks the focus gets it, and the document has it, before the key; each key is confirmed by its own event type (`keydown`/`keyup`); a key that does not arrive is a warning in the app's log. The server grab around the focus and the key is kept as a guard (it alone did not help). Also found: the Notebook's save rebuilt its state from the state it was called with, dropping an edit React had not applied (a new vitest; a functional update). |
| Exit 139 after "window destroyed" (Development probe, a hung page closed anyway) | GTK 4.14's `GtkAlertDialog` destroys its window with the parent, and again, through a pointer it holds unreferenced, when the answer is read; GLib gives a cancelled dialog its answer on the loop's next turn. The teardown destroyed the main window in between, so the answer destroyed a freed window (core: `gtk_window_destroy` under `gtk_alert_dialog_choose_finish`). A C program that cancels and destroys the parent in one turn fails GTK's window check 3 times of 3, and not when the answer is read first. | `WindowDialogs`: a window's dialogs are taken down and their answers waited for (at most 1 s) before the window is destroyed. |
| Exit 3 without the start-up message (`A_missing_bwrap…`) | Xvfb resets when its last client leaves and refuses connections meanwhile; the black box's Xvfb has no long-lived client, so an app's `gtk_init_check` failed now and then (42 of 3,000 back-to-back opens failed; none with `-noreset`). Reproduced 1 in 10 runs of `LinuxStartupTests`. | The black box's Xvfb runs with `-noreset`. |
| Exit 134 after "exit 0" (found while looping: `The_software_GPU_default…` with `Always`, 1 in 10 runs of the class) | The loop thread ended with GDK's GL context current; the thread-local cleanup finalised it, and the destroyed window's surface it held, while the process exited, and GDK aborted on its X error-trap assertion (core: `gdk_x11_surface_finalize` under `gdk_draw_context_dispose` under `__nptl_deallocate_tsd`). | The loop thread calls `gdk_gl_context_clear_current` before it ends. |
| The stall-probe test (found while looping the `blackboxdev` step: once in 3 runs, with the Windows window tests running on the host meanwhile) | It took the first stall line, and a slow window start under load came first. | It waits for the development trigger's own line. |
| The search journey unclean: "a message arrived between documents and was ignored" twice (found while looping the journeys after the fixes above: 14 of 167 runs) | The two messages were the page's first ones, `{"type":"call","id":1,"contract":"pages","command":"list"}` and `{"type":"ready"}`, received after the reload's load had started. A first theory, that WebKitGTK delivered the new document's messages before its commit, was tried and withdrawn: in a copy whose relay tagged every message with a random id of its document, the ten such messages of 5 failing runs all carried the tag of the document the reload replaced. The journey reloaded about 50 ms after the window opened, while its first page was still starting. | The journey waits for the empty list's "Start your notebook" before it creates pages and reloads (the host's warning is right: those messages belong to a document that is going away). |

Loops in one container (CoreCLR): `LinuxStartupTests` 10 of 10 runs after `-noreset` (8 of 10 before); the Notebook's 10 window journeys, per run: 5 of 10 failed before any fix, 0 of 20 with the keyboard held, then the search journey's reload race (14 of 167 runs, item 6 of `m1c-blackbox/container-failures.txt`) and one lost click (1 of 25, item 7); with the journey's wait and the input hold, 30 of 30.

The full sequence after these fixes, `check copy client build window windowaot apps blackbox blackboxdev`, each in a fresh container of the image removed afterwards (`m1c-blackbox/final-sequence-*.log`). Sequence 1 passed every step on the tree before the session-end fix; sequence 2 failed `window` on it (item 8 of `container-failures.txt`) and was stopped; sequences 4, 5 and 6 ran on the final tree, one after another:

| Step | Sequence 4 | Sequence 5 | Sequence 6 |
|---|---|---|---|
| `check` | passed | passed | passed |
| `window`: `Spar.WindowTests` / Notebook journeys | 66 (62 passed, 4 skipped) / 10 of 10 | 66 (62, 4) / 10 of 10 | 66 (62, 4) / 10 of 10 |
| `windowaot`: the same with NativeAOT | 66 (62, 4) / 10 of 10 | 66 (62, 4) / 10 of 10 | 66 (62, 4) / 10 of 10 |
| `apps` | passed | passed | passed |
| `blackbox` (`--no-skips`): `Spar.WindowTests` / Notebook | 80 of 80 / 6 of 6 | 80 of 80 / 6 of 6 | 80 of 80 / 6 of 6 |
| `blackboxdev` (`--no-skips`) | 63 of 63 | 63 of 63 | 63 of 63 |

0 test processes were left in the container after each. Before them, on the final tree: the Notebook's window journeys 30 of 30 looped runs in one container; `Spar.WindowTests` in WSL (`m1c-blackbox/wsl.sh window`) 192: 164 passed, 28 skipped, 0 failed, 0 processes left; on Windows `Spar.WindowTests` 192: 112 passed, 80 skipped (after the surface's change), `Notebook.EndToEndTests` 27: 23 passed, 4 skipped, `Spar.Tests` 698: 691 passed, 7 skipped, the Notebook's vitest 12 of 12, `Spar.slnx` built with 0 warnings, `check-size` 0 errors.
