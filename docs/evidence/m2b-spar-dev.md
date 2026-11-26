# M2b: spar dev

Recorded 2026-11-26. What it shows: plan M2b, build steps 1 to 4 (section 6, `docs/design/agents.md`): `spar dev` builds a Spar app's Development build, starts it from a copy of the output, and on `spar reload` builds again, answering with a failed build's diagnostics at file and line while the running app keeps running, or with the new run once it is ready; everything it starts ends with it. Decisions "M2's decide-first questions" (item 5), "M2b's decide-first questions" and "M2b: spar dev (implementation record; choices for review; 2026-11-26)". The user-facing description is `docs/guide/spar-dev.md`.

## Environment

- **Windows:** Windows 11 Pro 10.0.26320, SDK 11.0.100, WebView2 Runtime 158.0.4370.33. `spar dev --private` ran the Notebook on a desktop of its own: no window reached the monitor, took the focus or got input. Data folders under `artifacts/webview2-profiles/m2b-spar-dev/<run>/` (`LOCALAPPDATA`).
- **Linux:** WSL Ubuntu 24.04 (kernel 6.18.58.1), GTK 4.14.5, WebKitGTK 2.52.6, the repository's copy in `/var/tmp/spar-m1c-blackbox/repo` (`docs/evidence/m1c-blackbox/wsl.sh`, steps `sync build devsmoke`): `spar dev --private` with its own Xvfb (the unpacked one, abstract socket only), a private D-Bus session, HOME and XDG folders under the copy's `artifacts/linux-homes/m2b-spar-dev/`, the runtime folder in `/dev/shm`. Nothing opened on WSLg's display.

## What was added

| Path | Contents |
|---|---|
| `src/Spar.Tool/Dev/` | `spar dev`, `spar dev --stop` and `spar reload` (`DevCommand`); the supervisor and its socket (`DevSupervisor`, `DevFile`); the build and its diagnostics (`AppBuild`, `BuildDiagnostics`); launch profiles (`LaunchProfile`); the run's copy (`RunCopy`); a run, its readiness and its stop (`AppRun`); the reload's answer (`ReloadResult`); the job object (`JobProcesses`) and the process groups (`GroupProcesses`); `--private` (`PrivateDisplay`); the window's place (`WindowKeeper`) |
| `src/Spar.Tool/Spar.Tool.csproj`, `NativeMethods.txt` | CsWin32; the devtools' listener, JSON-RPC framing, owner-only file and the black box's command-line quoting linked in |
| `src/Spar/Devtools/DevtoolsTools.cs`, `DevtoolsServer.cs`, `DevtoolsHost.cs`, `DevtoolsListener.cs` | The `stop` tool and the host's `EndSession`; the listener's name prefix |
| `src/Spar.Windows/AppWindow.Closing.cs`, `AppWindow.cs`, `WindowPlacement.cs`, `WindowsApp.cs` | `RequestSessionEnd` (`WM_ENDSESSION` to its own window); `SPAR_DEV_WINDOW` in Development builds |
| `src/Spar.Linux/LinuxApp.cs`, `src/Spar.Loopback/LoopbackApp.cs` | The `SIGTERM` path and the Ctrl+C path for `stop` |
| `src/Spar/HotReload/ConsoleInterrupt.cs` | A run `spar dev` started (`SPAR_DEV_SUPERVISOR`) leaves the console alone, so its Ctrl+C is `spar dev`'s (found at the PC) |
| `src/Spar.Sdk/Sdk/Sdk.targets` | The type-check runs `tsc` in the project's folder, so its errors' files are the project's |
| `tests/Spar.Tool.Tests/DevCommandTests.cs`, `tests/Spar.Tests/DevtoolsServerTests.cs` | 19 tests of the tool's parts, 2 of `stop` |
| `tests/Spar.BuildTests/ProductionBoundary.cs`, `NotebookBuildTests.cs`; `tests/Spar.PackageTests/ToolTests.cs` | The `SPAR_DEV_WINDOW` marker and its control; the type-check's project-relative file |
| `docs/evidence/m2b-spar-dev/smoke.sh`, `smoke-windows.sh`, `redact.pl`; `wsl.sh` step `devsmoke` | The evidence runs below |

## Results (observed)

### Tests

- **Windows**, `dotnet test --solution Spar.slnx -c Debug` (window tests included): 1,495 (1,382 passed, 113 skipped, 0 failed); before M2b 1,475. `Spar.Tool.Tests` alone after the last change (the loopback answer's test): 114 of 114.
- **WSL**, `wsl.sh sync client build test window`: the solution without window or black-box tests 1,297 (1,256 passed, 41 skipped, 0 failed), `Spar.WindowTests` 220 (170 passed, 50 skipped, 0 failed), no process left.
- **NativeAOT and the boundary (Windows):** the AOT lane of `Spar.Tests` 805 (798 passed, 7 skipped), published with no warning; `Spar.BuildTests` 7 of 7, the Notebook's Production NativeAOT executable without `SPAR_DEV_WINDOW` and the control finding it in the Development build's `Spar.Windows.dll`.
- **Packages (Windows):** `Spar.PackageTests` 42 of 42, the type-check's error at `frontend/src/probe.ts(1,14)`. The first run, while the WSL window tests ran at the same time, failed one test: the template app's black-box journey did not see "Type a name first." within its 10 s; the rerun on an idle machine passed, and CI passed it on the commit before M2b.
- **The tool's parts** (`DevCommandTests`, 19: 12 facts and 7 cases of 2 theories): MSBuild's canonical lines (a file with line and column, a node prefix, a TypeScript error without a column, an end position, a tool's error without a file, repeats once); a relative file made full and MSB3073 left out after the tool's own errors; the first profile that runs the project, a named one, comments and trailing commas, quoted arguments, a missing one listing the others, no launch settings; the build's properties from their block; the run's copy taking turns, copying only what changed and deleting what the output lost; the session file's round trip and its deletion by its own session only; a failed reload's text and JSON, a ready one, a failed start's last lines, a loopback run; wrong command lines (64), a reload with no session, a named profile the project lacks. **`stop`** (`DevtoolsServerTests`, 2): it calls the host's session end and answers with the budget; a host without one, and a run that cannot end yet, answer `unavailable`.


### The smoke runs

The same sequence (`docs/evidence/m2b-spar-dev/smoke.sh`) on both systems: `spar dev --private` in the background; a reload with nothing changed; `version` and `tree` against the new run; a reload with a C# error (a missing semicolon) and with a TypeScript error (`--json`), each file put back from a copy afterwards; a reload of the fixed code; a second `spar dev`; the app killed from outside, then a reload; `spar dev --stop`; what was left.

- **Windows** ([`logs/windows-smoke.txt`](m2b-spar-dev/logs/windows-smoke.txt)): the first build 5.1 s and the page ready 1.2 s after the start; a reload with nothing changed 2.8 s from the command to its answer, the page ready 0.7 s after the new run's start; the C# error at `Pages\PageTypes.cs(33,68)` (CS1002) in 3.0 s and the TypeScript error at `frontend\src\main.tsx` line 27, column 14 (TS2322) in 3.7 s, both with the previous run still running (`kept`) and exit 2; the fixed code 5.0 s; the second `spar dev` exit 64, naming the first; the killed app reported as exited, and the reload after it 2.1 s; `spar dev --stop` 0.2 s. Every planned stop went through the session end (`WM_ENDSESSION` held 72 ms in an earlier run of the same sequence). Both discovery files were gone and no process of the run was left, 2 s after the stop.
- **Linux** ([`logs/linux-smoke.txt`](m2b-spar-dev/logs/linux-smoke.txt)): the same, on its own Xvfb: reloads 1.6 s (nothing changed), 1.9 s (C#), 1.4 s (TypeScript), 2.2 s (fixed) and 1.0 s (after the kill); every planned stop through the `SIGTERM` path; no process left that names the copy's Notebook.
- **The files the run broke** were put back from copies, and git saw no change in the Notebook after either run.


### Loopback (Windows)

`spar dev -lp Loopback` with `SPAR_LOOPBACK_BROWSER=none`: the first build 10.5 s (beside a NativeAOT publish), ready at `loopback listening` 0.3 s from the start; a reload 3.3 s, the previous run ended through its session end; `spar call pages.list` answered; `spar tree` answered `unavailable` (the page is in a browser). The first version waited for `ui-ready`, which comes only when a browser page connects, and answered `starting` after 60 s; ready is now `loopback listening` for loopback (choice 10).

### At the PC (Windows, with the user)

`spar dev` (not `--private`) on the user's desktop, in the app's Terminal panel; each reload 10 s after the user was ready, while they typed in Notepad (decision "M2b: the checks at the PC (implementation record; 2026-11-26)"):
- **Place and focus:** the new window in the moved and resized window's place and size; the typing stayed in Notepad. Passed.
- **Maximized:** first back over the same area, not maximized, as built; then, after the spike `spikes/maximized-noactivate` (decision "A restarted maximized window comes back maximized, without the focus"), maximized again with the Restore button, the typing still in Notepad, and Restore back to the normal place. Passed.
- **Visual Studio building while `spar dev` ran:** first MSB4186 (the type-check's .NET-only property function in Visual Studio's .NET Framework MSBuild), then, after the fix, built with the window open. Passed.
- **Ctrl+C in `spar dev`'s terminal:** first the app took the Ctrl+C itself and `spar dev` killed it; after the fix (`SPAR_DEV_SUPERVISOR`), no dialog, the session end held 52 ms, exit 0, no process left. Passed.
- **Once, a slow restart:** one reload on the desktop was ready 3.4 s from the start instead of about 0.7 s: the kept Vite server did not answer its identity check within 2 s, so the run ended it and started another (the same session's first Vite had taken 3.7 s to be ready, and the UI thread logged stalls of 83 to 115 ms outside Spar's callbacks). Not reproduced: on a private desktop, a reload after 4.5 minutes, and another, reused the server (0.7 and 0.6 s).

### Found and fixed during the runs

- The type-check's errors named files relative to the frontend's folder (`src/main.tsx`); `tsc` now runs in the project's folder (choice 3), and the `Exec` task's MSB3073 after them is left out.
- A planned restart printed "the app exited …; spar reload builds and starts it again"; only an exit `spar dev` did not ask for says it now.
- The ready time was rounded to whole milliseconds; it keeps the host's tenth.
- Loopback's readiness (above).
- The Windows runner's process count was a PowerShell regular expression that broke on `samples\Notebook`, and the first redaction missed the paths JSON writes with doubled backslashes (`redact.pl`).

## Not verified

- A real Linux desktop (the window manager's placement and focus), and Wayland.
- `spar dev` killed outright (Windows' job ends the tree by design; on Linux the direct children only, pitfall).
- CI: no CI job runs `spar dev`.
