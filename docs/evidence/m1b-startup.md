# M1b: start-up (prerequisites, single instance, theme)

Recorded 2026-11-17. What it shows: the Windows host's start-up of plan M1b, build step 2, on the Notebook sample: the prerequisite checks with their native message and fixed exit codes, the app's hook that replaces the message, the per-user log file, single instance through a lock file and a named pipe with the relaunch handler, and the window in the theme's colour from its first frame. Decision "Start-up: prerequisites, single instance and theme (implementation record; choices for review)".

## Environment

Windows 11 Pro 10.0.26320 (system theme dark), SDK 11.0.100, WebView2 Runtime 157.0.4341.46, `Microsoft.Web.WebView2` 1.0.4314.35. Windows were shown on the user's desktop as an app's window is; no input was sent to them. Dialogs were closed by the probe with `WM_CLOSE`. Every app run had `LOCALAPPDATA` pointing into `artifacts/webview2-profiles/m1b-startup/`; the Notebook's data and profile were there, and nothing of these runs appeared in the real per-user folder (checked after the runs). Not run on Windows 10, in Windows Sandbox, on Linux, or at a DPI other than 100 %.

## What was added

| Path | Contents |
|---|---|
| `src/Spar/Hosting/StartupProblem.cs`, `StartupReport.cs` | `StartupProblem` and `StartupProblemKind` (public), the fixed exit codes, the line for standard error and the log, and the hook's rules (a throwing hook counts as "show Spar's message") |
| `src/Spar/Hosting/SparApplication.cs` | `spar.OnStartupProblem(Func<StartupProblem, bool>)`, `spar.OnRelaunch(…)` (both overloads) and `spar.Window(w => …)` on the real builder |
| `src/Spar/Hosting/WindowOptions.cs` | `WindowOptions` with `Background(light, dark)`, `Size` and `MinSize`; the colours as `#rrggbb` or `#rgb` |
| `src/Spar/Hosting/Relaunch.cs`, `RelaunchMessage.cs`, `RelaunchQueue.cs` | `Relaunch`; its wire form (versioned, length-checked, strict UTF-8); the queue that runs the handler off the UI thread, one at a time, with the root services, its token cancelled at shutdown |
| `src/Spar/Runtime/UnsupportedWindowsHost.cs` | What the generated `Run` hands over on a Windows older than 1809, where the host cannot load: "OS too old", exit 7, with the hook, stderr, the log line and a message box |
| `src/Spar.Windows/Prerequisites.cs` | `RtlGetVersion` against build 19045, `GetAvailableCoreWebView2BrowserVersionString` and `CompareBrowserVersions` against Runtime 139.0.3405.78, as plain decision functions; Development-only overrides of both minimums |
| `src/Spar.Windows/StartupFailure.cs`, `NativeDialog.cs` | The report: stderr (or the parent console when there is none), the log file, then the hook and the task dialog on an STA thread of their own; the task dialog with a link, a download button and expandable details, the host opening the page with `ShellExecuteW`; `MessageBoxW` without Common Controls 6 |
| `src/Spar.Windows/LogFile.cs`, `HostLog.cs` | `host.log` in the app's logs folder, written by a task of its own, appended by several processes at once through an append-only handle (`FILE_APPEND_DATA` only); moved aside to `host.1.log` above 5 MB until M3's rotation |
| `src/Spar.Windows/SingleInstance.cs`, `Interop/Native.Startup.cs` | The lock file, the pipe named from its identity, the explicit DACL, the client's check of the server's token user, `AllowSetForegroundWindow`, the 2 s acknowledgement and the 5 s lock wait |
| `src/Spar.Windows/WindowTheme.cs`, `WindowPlacement.cs`, `Interop/WebView2.Options.cs` | The theme (the system's, or the app's saved choice in `color-scheme` in the data root), the brush and `WM_ERASEBKGND`, `DWMWA_USE_IMMERSIVE_DARK_MODE` before the show, the profile's `PreferredColorScheme`, the transparent default background on `ICoreWebView2ControllerOptions3`; the size and minimum size in device-independent pixels, centred on the primary monitor, and `WM_DPICHANGED` |
| `src/Spar.Generator/Emit/HostEmitter.cs` | The `Run` interceptor's third branch for Windows older than 1809 |
| `samples/Notebook/Program.cs` | `spar.Window(w => w.Background(light: "#ffffff", dark: "#1d1f24").Size(1100, 720).MinSize(640, 420))`: the page's own sheet colours |
| `tests/apps/WireShapes/Program.cs` | A relaunch handler that writes the arguments to stderr, and a start-up hook that shows its "own message" on stderr when `WIRESHAPES_OWN_STARTUP_MESSAGE=1`: the test app the runs below use for both |
| [`m1b-startup/Probe.cs`](m1b-startup/Probe.cs), [`m1b-startup/matrix.sh`](m1b-startup/matrix.sh) | The probe that ran everything below: a file-based C# program, run from a copy outside the repository (the repository's build props would apply inside it) |

## Results (observed)

### Prerequisites: each path forced on four builds

The Notebook as a NativeAOT executable (Production and Development variants) and as CoreCLR builds (Release, which is Production, and Debug with `SparDevServer=false`, which is Development). Times are from `Process.Start` in the probe to the message being visible. Final run, after the last code change (`matrix.sh`).

| Path, how it was forced | NativeAOT Production | NativeAOT Development | CoreCLR Production | CoreCLR Development |
|---|---|---|---|---|
| Missing: `WEBVIEW2_BROWSER_EXECUTABLE_FOLDER` at an empty folder | exit 3, message 129 ms | 3, 125 ms | 3, 449 ms | 3, 121 ms |
| Too old, real: a copy of the Runtime's `EmbeddedBrowserWebView.dll` whose version strings were patched to 138.0.3351.83 | 4, 120 ms | 4, 72 ms | 4, 201 ms | 4, 119 ms |
| Too old, override: `SPAR_DEV_REQUIRE_WEBVIEW2=999.0.0.0` | **ignored**: exit 0, normal start | 4, 82 ms | **ignored**: 0 | 4, 118 ms |
| Broken: a folder with only `EBWebView\x64\EmbeddedBrowserWebView.dll` (the version reads 157, controller creation fails `0x80070002`) | 5, 136 ms (window first, at 42 ms; the message owned by it) | 5, 129 ms | 5, 189 ms | 5, 201 ms |
| OS too old, override: `SPAR_DEV_REQUIRE_WINDOWS_BUILD=99999` | **ignored**: 0 | 7, 67 ms | **ignored**: 0 | 7, 113 ms |
| Automated mode (`SPAR_AUTOMATED=1`) with the Runtime missing | **ignored**: the message shows, exit 3 | exit 3 after 54 ms, no message | ignored, message, 3 | 3 after 133 ms, no message |

- Inside the process, a missing Runtime was reported 1.7–2.3 ms after the host started; the Runtime check with a healthy Runtime took 0.2–0.4 ms; with a Runtime folder passed, reading the DLL's version took about 38 ms.
- Every run wrote the problem to standard error and to `host.log` in the run's own logs folder, in one line: `Start-up problem Missing: WebView2 Runtime (required 139.0.3405.78). … Help: https://developer.microsoft.com/microsoft-edge/webview2/consumer/ Exit code 3.`
- **The hook,** on the WireShapes app (NativeAOT Production, Runtime missing): returning true, no message appeared, its own line was on stderr, Spar logged "The app showed its own start-up message", exit 3 after 145 ms. Returning false, Spar's message appeared (147 ms) and the exit code was 3.
- **Windows older than 1809** (`UnsupportedWindowsHost`): unit test only, with the hook taking the message: exit 7, the problem in its own logs folder.

### Single instance and relaunches

| Check | Result |
|---|---|
| A second launch of the NativeAOT Notebook with two arguments, the first minimized, 5 runs | each second launch exited **0 in 30–31 ms** from `Process.Start`; it handed over 0.5–0.6 ms after its host started; the first logged "relaunch with 2 arguments: window shown, foreground taken", and its window was no longer minimized and was the foreground window afterwards (see "Not verified") |
| The arguments, on the WireShapes app's handler | `[--recipe \| nightly copy.sqlrecipe \| …]` with the launcher's working directory, written by the handler on the first instance's stderr; the unit tests check non-ASCII arguments, empty ones and quotes through the wire form |
| A hung first instance (suspended with `NtSuspendProcess`), 3 + 3 runs | exit **8**, the problem reported **2.00–2.02 s** after the second's host started, its message at 2.07–2.10 s; after the resume, the first dropped the stale relaunch ("waited 2377–2414 ms, longer than its sender waits") instead of acting on it |
| Ten launches at once (unit test, real pipe) | 10 of 10 arrived |
| A lock released while a launch waits (unit test) | the launch became the primary |
| A lock that nobody serves (unit test) | exit 8 after the 5 s lock wait |
| A junction to the data root (unit test) | the same instance was reached |
| A closing first instance (unit test) | it refused, and the launch reported "closing" (exit 8) |

### Theme at start-up

`PrintWindow(PW_RENDERFULLCONTENT)` from the probe's process every 16.7 ms from the moment the window appeared, for 1 s; each frame's client area sampled on a 4-pixel grid. The light runs are the app's saved choice (`color-scheme` = `light` in the data root) on this dark system: the system's setting was not changed.

| Run (NativeAOT Production, Notebook colours `#ffffff` / `#1d1f24`) | Frames | Frames with any white pixel | Frames with `#121212` | Frames with dark pixels | Title bar |
|---|---|---|---|---|---|
| Dark, 5 runs | 55–57 each | **0** | **0** | (all, expected) | dark from the first frame |
| Light, 5 runs | 55–58 each | (all, expected) | 0 | **0** | light from the first frame |
| Dark, CoreCLR Debug (services built by JIT before navigation), transparent / opaque, 3 + 3 runs | 65–69 | 0 / 0 | 0 / 0 | — | dark |

- The first captured frame was already the window colour, 34–72 ms after `Process.Start` ([`first-frame-dark.png`](m1b-startup/first-frame-dark.png), [`first-frame-light.png`](m1b-startup/first-frame-light.png)); the page in light renders light ([`ready-light.png`](m1b-startup/ready-light.png)) and in dark, dark ([`ready-dark.png`](m1b-startup/ready-dark.png)).
- The opaque WebView background showed no `#121212` either: in a normal start the page paints before Chromium's dark canvas would (as `docs/evidence/theme-startup` found), so transparency did not change what was seen here.

**The cost of the transparent background** (NativeAOT Development, `SPAR_DEV_WEBVIEW_BACKGROUND=default` for the opaque runs, alternating, 7 pairs; memory is the private working set 2 s after `ui-ready`):

| | `ui-ready` from the host's start, median (range) | Controller | WebView2 processes (6) | App process |
|---|---|---|---|---|
| Transparent | 290.7 ms (285.3–312.6), 6 runs | 194–218 ms | 86.3 MB (85.9–86.9) | 5.1–5.6 MB |
| WebView2's default | 294.5 ms (282.8–312.9), 7 runs | 193–205 ms | 86.4 MB (85.2–89.1) | 5.3–5.5 MB |

No measurable cost. The first transparent run was left out: its controller took 6.27 s, right after the previous run had closed within half a second of its navigation (pitfall "The browser process can take about 6.5 s to exit", SmartScreen; step 5's `IsReputationCheckingRequired = false` removes it).

### Start-up phases and size

- **NativeAOT Production, 8 warm runs** (ms from the host's start): single instance claimed 0.4–0.9; checks done 0.6–1.3; window shown 16.8–30.5 (median about 20); controller 203–231 (median 215); `ui-ready` 295–324 (median 309). Step 1 recorded 265 for `ui-ready` in another session; the transparent/opaque pairs above show nothing of this step costs controller time, and this run was not compared against the step-1 build on the same machine state.
- **The Notebook's executable:** 4,772,864 B (step 1: 4,512,768 B; budget 5 MB). A first version with `NamedPipeClientStream` and `WindowsIdentity` was 4,941,312 B: they brought `System.Security.Claims`, `Principal` and `AccessControl` (about 88 KB of code in the map file); the client now opens the pipe with `CreateFileW` and reads the token's user SID itself.

### Tests

| Suite | Result |
|---|---|
| Spar.Tests | 235 of 235 (49 new: the start-up decisions, the hook, the wire form, the relaunch queue, single instance with real pipes, the old-Windows stand-in, the native layouts, the colours and window options) |
| Spar.Generator.Tests | 84 of 84 (the `Run` interceptor's new branch) |
| WireShapes.Tests | 79 of 79; the NativeAOT lane 79 of 79, 0 warnings |
| Notebook.Tests | 12 of 12 |
| Spar.Sdk.Tasks.Tests | 62 of 62 |
| AppModel.Tests | 10 of 10, with 31 blocks (the new `SupportPage` block) |
| Spar.BuildTests | 6 of 6 (3 min 2 s), the Notebook's Production boundary included |
| Spar.PackageTests | 10 of 10 (1 min 35 s) |
| Size check | 277 source files, 0 errors, 0 warnings |

## Screenshots

Captured with `PrintWindow` from the probe's process, without input; the dialogs' details stayed collapsed.

- [`missing.png`](m1b-startup/missing.png), [`too-old.png`](m1b-startup/too-old.png), [`broken.png`](m1b-startup/broken.png): NativeAOT Production. [`os-too-old.png`](m1b-startup/os-too-old.png): NativeAOT Development with the build override. [`not-responding.png`](m1b-startup/not-responding.png): the second launch against a suspended first.
- [`first-frame-dark.png`](m1b-startup/first-frame-dark.png), [`first-frame-light.png`](m1b-startup/first-frame-light.png), [`ready-dark.png`](m1b-startup/ready-dark.png), [`ready-light.png`](m1b-startup/ready-light.png).

## Not done or not verified

- **Focus on the user's desktop:** the probe saw "foreground taken" and the primary as the foreground window, but the probe itself was not the foreground process and no click started the second launch, so this does not show Windows' foreground rules; `docs/evidence/windows-single-instance` checked them with real clicks. Not re-run.
- **A live system theme change** (`WM_SETTINGCHANGE`, `ImmersiveColorSet`): built, not run, since it needs the user's setting changed; added to the plan's "Checks that need the user at the PC".
- **Exit 9 (another user):** unit-tested only as far as the mapping; no second account was used. **Windows below 22H2 or below 1809, a real Runtime 139 or older, Common Controls 5 (`MessageBoxW` fallback), the download page opening, Narrator, DPI above 100 %, several monitors.**
- **The splash and the page's theme style:** the build does not yet read the colours from `Program.cs` as constants to write the theme style and its CSP hash into `index.html` (plan 3d, "The page style"); the window's first frame does not need it.
- **The SDK's `setColorScheme`,** which will write the saved choice the host now reads, is not built.
- **Two sessions of the same user, elevated and non-elevated instances together, a launch from Explorer or a file association** (as in the spike).
