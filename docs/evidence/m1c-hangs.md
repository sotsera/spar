# M1c: hangs on the Linux host (the heartbeat, Reload page, the shutdown prompt, the stall probe)

Recorded 2026-11-20. What it shows: the Linux host's hang handling of plan M1c, build step 3, on the bridge probe (`tests/apps/BridgeProbe`) under an Xvfb of the run's own, as a Development build and as a Production NativeAOT executable: the heartbeat with the relay's answer from the document's creation, the "Wait / Reload page" dialog and its Wait schedule, "Reload page" through `webkit_web_view_terminate_web_process`, no false alarm on an idle page or on work of 3 s and 10 s, a page hung before it connects, a close that meets a hung page or a silent page guard, the shutdown prompt with exit code 10, and the stall probe on the GLib main loop. The hang handling is now one implementation in the core for both hosts. Decision "Linux hangs: heartbeat, reload and shutdown prompt (implementation record; choices for review)".

## Environment

- **Linux:** WSL2 `Ubuntu-24.04` (kernel 6.18.58.1-microsoft-standard-WSL2), the normal user, no root; `libwebkitgtk-6.0-4` 2.52.6, `libgtk-4-1` 4.14.5, Mesa llvmpipe, SDK 11.0.100. The Xvfb of `docs/evidence/m1c-host/wsl.sh tools`, on its abstract socket only, with no window manager; a private D-Bus session per test run. Never WSLg's display; no input was sent to any window.
- **Isolation:** each app run has its own `HOME` and `XDG_*` folders under the working copy's `artifacts/linux-homes/host-tests/<run>/`; `XDG_RUNTIME_DIR` is `/dev/shm/spar-m1c-hangs`.
- **Windows:** Windows 11 Pro 10.0.26320, SDK 11.0.100, WebView2 Runtime as in M1b, the same tree.
- **Working copy:** the Windows tree as it was, uncommitted changes included, copied to `/var/tmp/spar-m1c-hangs/repo` (`wsl.sh sync`).
- **Answers:** the Linux dialogs and the shutdown prompt were shown as real `GtkAlertDialog`s on the Xvfb (each seen as one more viewable window of the app's process) and answered by the Development build's scripted answers (`SPAR_DEV_PROMPT_ANSWERS`), which take the dialog down when they answer; the Production probe has no such answers, so its runs check what shows and end the app.

## What was added

| Path | Contents |
|---|---|
| `src/Spar/Hangs/PageWatch.cs`, `PageWatch.Closing.cs`, `ShutdownWatch.cs`, `PageWatchHost.cs` | The heartbeat's driver, the hung-page dialogs, the page's side of a close request and the shutdown watch, moved out of the Windows host into the core unchanged in behaviour, behind `IPageWatchHost` and `IHostPrompt` (a new folder: `src/Spar/Hosting` holds 39 of 40 files) |
| `src/Spar.Windows/WindowsPageHost.cs` | The Windows side: the bridge, the view, the UI thread and `PromptDialog` behind the seam (`PageWatch.cs`, `PageWatch.Closing.cs` and `ShutdownWatch.cs` left the Windows host) |
| `src/Spar.Linux/LinuxPageHost.cs` | The Linux side: the bridge, the view, the visibility (mapped, not minimised or suspended), and the prompts as `GtkAlertDialog`s on the main loop, posted there from other threads |
| `src/Spar.Linux/BridgeRelay.js`, `ScriptBridge.cs` | The relay answers `ping` in its own world, with `{"dialog":true}` while the page shows a modal dialog; the host reads the `pong` on the loop, without a session; documents opened and closed, `close-request`, `close-answer` and `close-ended` for the page's close guards |
| `src/Spar.Linux/BrowserView.cs`, `AppWindow*.cs`, `LinuxApp.cs`, `AlertDialog.cs` | `notify::is-web-process-responsive`, "Reload page" (terminate, then reload on `TERMINATED_BY_API`), a hung page's process ended before its window is destroyed, the page's guard before the backend's, the main loop kept until the shutdown is done, the shutdown watch with "Exit now" (`_exit(10)`), the hang hook, dismissable dialogs |
| `src/Spar.Linux/DevelopmentTriggers.cs`, `Interop/` | Windows' Development triggers on Linux, and `SPAR_DEV_PROMPT_ANSWERS`; `webkit_web_view_get_is_web_process_responsive`, `gtk_widget_get_mapped`, `gtk_native_get_surface`, `gdk_toplevel_get_state`, `g_cancellable_new`/`_cancel`, `_exit`, checked against the 2.52.6, 4.14 and GLib 2.80 headers in WSL |
| `tests/apps/BridgeProbe` | Plan steps `hang`, `busy:<ms>`, `wait:<ms>`, `hang-before-connect` (a page, `hang.html`, that loops before it connects), `guard-keep`, `guard-silent`; the hang hook in the log; a slow-disposing service and the kept stall probe on request |
| `tests/Spar.Tests/LinuxHangTests.cs`, `LinuxAppRun.cs`, `X11Frames.cs` | Nine tests in a real window, the window count on the display, longer waits |
| [`m1c-hangs/wsl.sh`](m1c-hangs/wsl.sh), [`m1c-hangs/`](m1c-hangs/) | The runner and the raw logs |

## Commands

From Git Bash at the repository's root, after `docs/evidence/m1c-host/wsl.sh tools` once:

```
MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/docs/evidence/m1c-hangs/wsl.sh sync client appmodel build test probe hangs regress probeaot notebook
```

`build` rebuilds the probe as a Debug build that embeds no page, so `probe` (with `-p:SparDevServer=false`) must come after it and before `hangs` and `regress`. On Windows: `dotnet build Spar.slnx -c Debug`, the solution's tests without the `Window` and `BlackBox` surfaces, `dotnet test --project tests/Spar.WindowTests -c Debug --no-build`, the NativeAOT lane of `tests/Spar.Tests`, `dotnet run eng/check-size.cs`.

## Results (observed)

Every number below is from the last full run (run 4 of the hang tests, 2026-11-19 23:54–00:01 UTC, [`linux-hangs-development.txt`](m1c-hangs/linux-hangs-development.txt), [`linux-hangs-production.txt`](m1c-hangs/linux-hangs-production.txt), and the apps' logs in [`m1c-hangs/`](m1c-hangs/), `dev-*` for the Development probe and `aot-*` for the Production NativeAOT one), with earlier runs' values in brackets where they ran the same code. Intervals are the host's monotonic times (each step line's "at N ms", or its standard error's "+N ms"), since WSL's wall clock jumped by about 1.2 s twice in one earlier run (pitfall).

| Check | Result |
|---|---|
| Windows build | `dotnet build Spar.slnx -c Debug`: 0 warnings, 0 errors |
| Windows solution tests | 969: 915 passed, 54 skipped, 0 failed (the static-state list, updated for `PageCloseResult`'s move into the core, and `tests/AppModel.Tests` among them) |
| Windows window suite | 118: 106 passed, 12 skipped (Sandbox-tagged), 0 failed: the close, silent-guard, hung-page and session-end tests over the moved `PageWatch` among them |
| Windows NativeAOT lane of `tests/Spar.Tests` | 634: 596 passed, 38 skipped (the Linux tests skip before any process or listener starts), 0 failed |
| Size check | 0 errors, 0 warnings |
| Linux solution tests (`wsl.sh test`) | 969: 897 passed, 72 skipped, 0 failed (the static-state list among them) |
| Linux hang tests, Development probe | 9 of 9 passed (`wsl.sh hangs`) |
| Linux hang tests, Production NativeAOT probe | 5 passed, 4 skipped (they need the Development build's answers, interval or automated mode) |
| Steps 1 and 2's Linux tests on this host (`wsl.sh regress`) | 94: 92 passed, 2 skipped (Development probe), 0 failed |

### The heartbeat

| Case | Result |
|---|---|
| A page in an endless loop after `ui-ready` | alarm **19.9 s** after the hang (Development and Production) [19.9 s in run 1]; the dialog shown 14–17 ms after the alarm's line, as a second viewable window of the app on the display; the hook's line "Unresponsive after 15.0 s" |
| A page hung in its own start-up code, before it connects (`hang.html`) | alarm **19.9 s** after its document opened (Development and Production): the relay answered the pings before the page could, so the heartbeat was armed at `LOAD_COMMITTED` |
| Idle 20 s, then work of 3 s and 10 s, then idle 6 s | **no alarm** in either build; 7 ticks, 7 pings; the longest tick on the main loop 0.57–0.64 ms (CoreCLR Debug) and **0.031 ms** (NativeAOT) |
| "Reload page" (scripted answer 1 s after the dialog) | the web process ended (`TERMINATED_BY_API`) **1.8 ms** after the choice [2.0], the reload issued 6 ms later, the new document's `ui-ready` **68 ms** after the choice [72]; "first document … (reloaded)" from the backend, so the page started again; the automatic reload untouched ("page reloaded after the web process ended" never logged); hook "Reloaded" |
| The Wait schedule on a 500 ms heartbeat (five Waits, then Reload) | back after **1.9, 3.9, 7.9, 15.9, 15.9 s** (decided 20, 40, 80, 160, 160 s at 5 s; each up to one tick early), in two runs |
| WebKit's own report (`notify::is-web-process-responsive`) | never changed in any run: no input reached the hung pages (as in `docs/evidence/page-heartbeat`); the merge into one state is the shared rule that Windows' unit tests cover |
| The automated mode (`SPAR_AUTOMATED=1`) | the alarm logged, "The automated mode shows no hung-page dialog."; a close "closes a page that does not answer without asking", its web process ended first, exit 0, no dialog ever shown |

### Closing

| Case | Result |
|---|---|
| A close 1 s into a hang | the close's ping unanswered; "Wait / Close anyway" **5,019 ms** after the request [5,016–5,018]; Wait; asked again **18.8 s** later [18.9, 18.9, 20.0]; Close anyway: the backend's guard closed, the page's close work skipped, "web process of the hung page ended before closing", web processes gone 22 ms later [8], **exit 0 36 ms** after the choice [35, 37]; hook "ClosedAnyway" |
| A close while the hung-page dialog is open | the window manager's close reached nothing while the modal dialog showed (pitfall); after Wait, the close found the page hung and asked "Wait / Close anyway" at once; Close anyway, exit 0, no web process left |
| The page's guard answers keep (`windows.onCloseRequested(() => "keep")`) | `close-request` sent, "the page answered Keep" 8 ms later, the window kept |
| A silent page guard (a promise that never settles) | "its close guard has not answered", "Wait / Close anyway" **5,063 ms** after the request [5,014 ×3]; Close anyway, exit 0; the hook not called and no web process ended (the page was not hung) |

### The shutdown prompt

| Case | Result |
|---|---|
| A service whose disposal takes 120 s (Development; the probe's `BRIDGE_PROBE_SLOW_DISPOSE_MS`) | "closing for 10.0 s", the prompt **10.0 s** after the teardown began, unowned, while the main loop still ran; Keep waiting; again at **40.5 s**; Exit now: its line, then **exit code 10**; "services disposed" never logged; no WebKit process left (3 runs) |
| A disposal of 15 s (Production NativeAOT) | the prompt at **10.0 s**, taken down when the shutdown finished at 15 s; exit 0 |

### The stall probe

| Case | Result |
|---|---|
| `SPAR_DEV_UI_STALL_MS=300` (Development) | "The UI thread stalled 300.1 ms; the slowest Spar callback meanwhile was development stall trigger" [300.2 ×2]; no other stall over 50 ms in that run |
| Production NativeAOT probe | no "UI-thread stall probe on." without `KeepStallProbe()`; on with it (`BRIDGE_PROBE_KEEP_STALL_PROBE=1`) |

### The Notebook (Production NativeAOT, `wsl.sh notebook`)

4,960,320 B (+95,376 B over step 2's 4,864,944 B; budget 8 MB); launch to `ui-ready` median 226.3 ms (212.0–239.7 but for one launch of 1,581.7 ms, n = 11); close to exit median 43.1 ms (25.6–52.1); total PSS 231.4 MB ([`linux-host-hangs.txt`](m1c-hangs/linux-host-hangs.txt)). In its image: no `DevelopmentTriggers` and no `UiStallProbe` (UTF-8), no `SPAR_DEV_`, `SPAR_DEV_PROMPT_ANSWERS` or "Development trigger" (UTF-16); the shutdown prompt's texts are there.

## Not verified

- **Real input on the Linux dialogs and the shutdown prompt:** every answer was scripted (`SPAR_DEV_PROMPT_ANSWERS`); Escape, the dialog's close button and keyboard focus were not tried, nor the Production dialogs' buttons. XTEST on the run's own Xvfb, or a container with a window manager, can drive them.
- **WebKit's `is-web-process-responsive`** never changed, so its merge into the heartbeat's state ran in no Linux run (the shared rule is unit-tested in `PageHeartbeatTests`).
- **A real desktop:** GNOME, KDE, Wayland, a compositor, a window manager that minimises or suspends the window (the visibility gate was not exercised: Xvfb has no window manager), the dialog's placement and focus there.
- **The shutdown prompt with the main loop itself stuck,** which gets no prompt by design (choice 6); a hang hook that runs long at exit.
- **The Notebook's own page hung** (the probe's page was used throughout), a hang in a worker or a frame, CPU stress, a hidden window over 60 s.
- Windows: no new run beyond the suites (the refactor kept every log line; the hang scenarios of `docs/evidence/m1b-hangs.md` were not repeated by hand).
