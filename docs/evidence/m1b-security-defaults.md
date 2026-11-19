# M1b: security defaults (the browser's settings and their opt-ins, the environment overrides, the app id and its folders, the profile)

Recorded 2026-11-17. What it shows: the Windows host's security defaults of plan M1b, build step 5 (plan section 3c), on the test app made for page security (`tests/apps/SecurityProbe`), each default on the published NativeAOT executable and its opt-in on a second executable built with every opt-in; the checks that need input on Development builds through the in-process DevTools Protocol; and the Notebook's size. Decision "Security defaults (implementation record; choices for review)".

## Environment

Windows 11 Pro 10.0.26320 (system theme dark), SDK 11.0.100, WebView2 Runtime 157.0.4341.46, `Microsoft.Web.WebView2` 1.0.4314.35, 32 logical CPUs. Every app run was started by [`DefaultsRun.cs`](m1b-security-defaults/DefaultsRun.cs), grown from the page security run's `SecurityRun.cs` and run from a copy outside the repository: the window shown without activation and moved offscreen as soon as it existed, standard error read with the run's own clock, the window closed with `WM_CLOSE`. No mouse or keyboard input was sent to the desktop; the checks that need input used the Development-only `SPAR_DEV_INPUT` trigger, which sends the DevTools Protocol's `Input` events inside the app's process. Each run had `LOCALAPPDATA` in its own folder under `artifacts/webview2-profiles/m1b-security-defaults/`. The logs are in [`logs/`](m1b-security-defaults/logs/), with the repository's path written `<repo>`.

**One start-up dialog reached the desktop for about 40 ms.** The first try of the Runtime folder override (`aot-opt-ins-env-folders`, before the run kept here) passed the folder as `E:/…`, which Git Bash makes of `/e/…`; the loader refused it, and the Production executable showed its "can't start" task dialog, with no owner since no window existed yet. The run found it 6 ms after it appeared and posted `WM_CLOSE`; the app exited with code 3. The variables were then passed as Windows paths, and every override was tried first on a Development build with `SPAR_AUTOMATED=1`, which shows no dialog (pitfall "Git Bash rewrites paths passed to Windows programs"). No other dialog appeared in any run: the run watches for dialog windows of the app and of every WebView2 process.

## What was added

| Path | Contents |
|---|---|
| `src/Spar/Hosting/BrowserOptions.cs`, `SparApplication.cs` | `spar.Browser(b => …)` with its eight opt-ins, each documenting its risk, when it is reasonable and the safer alternative; `Active()` for the start-up log |
| `src/Spar/Hosting/SparFolders.cs`, `BrowserProfile.cs`, `SparRunningApp.cs`, `src/Spar/Runtime/SparCatalog.cs` | Folders named from the app id per channel, `FolderOptions.Profile`, the profile checks, `SparFolders.Profile`; the profile folders no grant may reach (requested and in use); `SparCatalog.AppId` |
| `src/Spar.Generator/SparGenerator.cs`, `Emit/CatalogEmitter.cs`, `src/Spar.Sdk/Sdk/Sdk.targets` | `SparAppId` read by the generator into the catalog; SPAR130 for a missing or invalid id |
| `src/Spar.Windows/BrowserDefaults.cs`, `BrowserEnvironment.cs`, `PagePicker.cs`, `PageGuard.cs`, `BrowserView.cs`, `WindowsApp.cs`, `StartupFailure.cs` | The settings, downloads, context menu and keys; the variables removed and the environment options; the picker interception; history cancelled; the folder in use; the identity, the opt-ins logged, the profile refused with exit 70 |
| `src/Spar.Windows/DevelopmentInput.cs` | `SPAR_DEV_INPUT`, trusted input through `Input.dispatchMouseEvent` and `Input.dispatchKeyEvent`, Development only |
| `src/Spar.Windows/Interop/WebView2.Settings.cs` and the other interop files | `ICoreWebView2Settings8`, `ICoreWebView2EnvironmentOptions` (implemented by the host), the download, context menu and key arguments; `NavigationStartingEventArgs3` in place of the base declaration; the slots the slot-order test now requires |
| `tests/apps/SecurityProbe/` | `-p:SecurityProbeOptIns=true` for every opt-in, `SECURITY_PROBE_PROFILE`, the `folders` command reading `SparFolders`, and the probes `defaults`, `history`, `mouse-back`, `mouse-back-same`, `picker`, `menu`, `keys` |
| [`m1b-security-defaults/DefaultsRun.cs`](m1b-security-defaults/DefaultsRun.cs), [`scenarios.sh`](m1b-security-defaults/scenarios.sh), [`logs/`](m1b-security-defaults/logs/) | The run (port, dialog, download-folder and process checks added), every scenario, the raw logs |

**Builds:** `aot` is `dotnet publish tests/apps/SecurityProbe -c Release` (NativeAOT, Production, 4,228,608 B); `opt` is the same with `-p:SecurityProbeOptIns=true` (4,229,120 B), which calls all eight opt-ins; `dev` and `devopt` are the Debug builds with `SparDevServer=false` (CoreCLR, Development), without and with the opt-ins. `runtime-copy` is a copy of the installed Runtime's version folder under `artifacts/` (692 MB), for the executable-folder override.

## Results

Each item says how it was seen: **observed** (the page, the browser or the file system showed the effect), **read back** (the browser's setting read after it was set), or **logged** (Spar's own line only).

### Production defaults and their opt-ins (NativeAOT)

| Default | Production (`aot`) | Opt-in (`opt`) | How |
|---|---|---|---|
| Downloads denied | the page's `<a download>` of a blob raised `DownloadStarting`, which Spar cancelled; no file in the user's Downloads folder | the file was saved to the folder seeded in the profile's `Preferences`; the user's real Downloads folder got none (checked before and after) | observed |
| File System Access pickers absent | `showOpenFilePicker`, `showSaveFilePicker`, `showDirectoryPicker` are `undefined` | all three `function` | observed |
| `<input type="file">` intercepted | `Page.enable` and `Page.setInterceptFileChooserDialog` made before the first navigation, in 35–48 ms (every run of `aot` and `dev`) | no call made | logged; the click is below, on Development |
| History navigation cancelled | `history.back()` to the first document: `NavigationStarting` back-or-forward cancelled, the page stayed on the second document | the page went back to the first document | observed |
| Devtools, accelerator keys, password autosave, general autofill, swipe | off | on | set (logged); read back on Development, below |
| Host objects, reputation check | off | off (no opt-in) | set (logged); read back on Development; `chrome.webview.hostObjects` is a `function` either way (pitfall) |
| Context menu | cut down (handler subscribed) | full (no handler) | logged; the menus below, on Development |
| Opt-ins in the start-up log | none | one line naming all eight | logged |
| App id and folders | `App id com.example.tests.SecurityProbe`; data root `…\com.example.tests.SecurityProbe`, profile `…\WebView2`, logs `…\logs`, the same in the page's `folders` command (`SparFolders`) | the same | observed (folders on disk, the service's values) |
| Profile folder chosen in C# | `SECURITY_PROBE_PROFILE` = an absolute local folder: the browser used it (`Browser profile folder …\chosen-profile`), and the data root kept only `instance.lock` and `logs` | | observed |

### The environment overrides (NativeAOT): clearing in-process is enough

| Variable | Production, not allowed | Opt-in (`AllowBrowserEnvironmentOverrides()`) |
|---|---|---|
| `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=49517` | logged as ignored; port 49517 closed before the start, while the app ran and after it exited | logged as in force; the port **open** while the app ran, closed 1 s after it exited |
| `WEBVIEW2_USER_DATA_FOLDER=<root>\moved-profile` | ignored; the browser used `…\com.example.tests.SecurityProbe\WebView2`; no `moved-profile` folder was created | the browser used `moved-profile`; the host logged "Browser profile folder moved by a policy or an allowed variable" and recorded it |
| `WEBVIEW2_BROWSER_EXECUTABLE_FOLDER=<runtime-copy>` | ignored; the WebView2 processes started from `C:\Program Files (x86)\Microsoft\EdgeWebView\Application\157.0.4341.46` | the WebView2 processes started from `<repo>\artifacts\m1b-security-defaults\runtime-copy` |
| `WEBVIEW2_RELEASE_CHANNEL_PREFERENCE`, `WEBVIEW2_CHANNEL_SEARCH_KIND`, `WEBVIEW2_RELEASE_CHANNELS`, `WEBVIEW2_PIPE_FOR_SCRIPT_DEBUGGER`, `WEBVIEW2_WAIT_FOR_SCRIPT_DEBUGGER` | each logged as ignored; the app started normally | not run |

The same folder overrides ran first on `devopt` with `SPAR_AUTOMATED=1`, with the same result. So the loader reads the variables in the app's process at environment creation, and the browser inherits the process's environment block: removing them with `SetEnvironmentVariableW` before the loader's first call (the Runtime check) is enough, and needs nothing in the registry. Administrator policies under `Software\Policies\Microsoft\Edge\WebView2` were not set and not tried.

### Checks that need input (Development, `SPAR_DEV_INPUT`, `SPAR_AUTOMATED=1`)

| Check | Result | How |
|---|---|---|
| Settings read back, defaults (`dev`) | devtools on, keys on (filtered to the devtools and reload keys), password autosave off, autofill off, swipe off, host objects off, reputation check off | read back |
| Settings read back, opt-ins (`devopt`) | devtools on, keys on, password autosave on, autofill on, swipe on, host objects off, reputation check off | read back |
| `<input type="file">` clicked twice by trusted input | the page saw 2 clicks, no `change`, no `cancel`, no files; no dialog window appeared | observed |
| Right click on a text field | `ContextMenuRequested`: kept `cut copy paste selectAll inspectElement`, 13 items removed; not shown (automated mode) | logged by the handler, after the filter |
| Right click on the page | kept `inspectElement`, 9 removed; not shown | logged by the handler |
| The mouse's back button to another document | after a click on the first document: `NavigationStarting` back-or-forward cancelled, the page stayed; with `devopt` it went back | observed |
| The mouse's back button within one document (`pushState`) | moved from `#two` to `#one`, no `NavigationStarting` | observed |
| Ctrl+P, Alt+Left, F5, Ctrl+R by `Input.dispatchKeyEvent` | the page received all four; no print, no reload, not even of F5 and Ctrl+R, which Development allows | inconclusive: these events never reach the browser's accelerators (pitfall) |

### The profile folder refused (Development, automated)

- `relative\profile`: "The browser profile folder 'relative\profile' (spar.Folders) is not an absolute path on a local drive.", exit 70, 12.5 ms after the host started, before single instance and any folder was created.
- `//localhost/c$/spar-step5-profile`: "… is on a network drive, slow and readable by others: use a local drive.", exit 70.

The same check runs in Production; it was not run there, because Production shows its message on the desktop.

### Start-up cost (NativeAOT, 7 runs each, idle page)

| | `aot` (picker intercepted) | `opt` (no interception) |
|---|---|---|
| Settings set | 0.04–0.16 ms | 0.04–0.05 ms |
| The two picker calls | 40.2–47.5 ms, median 45.1, of which `Page.enable` all but 0.3–0.9 ms | none |
| Controller to `Navigate` | 40.4–47.6 ms, median 45.2 | 40.4–72.6 ms, median 43.2 |
| Host start to `ui-ready` | 312.4–335.9 ms, median 325.0 | 315.1–423.7 ms, median 329.5 |

The picker calls overlap `AddScriptToExecuteOnDocumentCreated`, which takes about as long (pitfall "`AddScriptToExecuteOnDocumentCreated` completes about 45 ms after the controller exists"), so they add nothing measurable to the start.

### The Notebook and the production boundary

- The Notebook's NativeAOT executable is 4,999,680 B (4,940,288 B before; budget 5,000,000 B), 0 warnings. The first build of this step was 5,022,208 B; the map files of both builds showed 82 KB added, mostly COM stubs and their metadata; settings getters only for the log, the download's address, `DriveInfo`, `Environment.SetEnvironmentVariable` and an `async` picker method took 22 KB and were moved behind the Development switch, replaced by Win32 calls, or made one completion handler (pitfall).
- The production boundary test now lists every DevTools Protocol method name in the image and requires exactly `Page.enable` and `Page.setInterceptFileChooserDialog`; its control is `Spar.Windows.dll`, whose Development-only input trigger names `Input.dispatchMouseEvent`. `DevelopmentInput`, `SPAR_DEV_INPUT` and `remote-debugging-port` joined the markers. The UTF-16 strings of the new image were compared with the old one's: none of the input trigger's.

### `%LOCALAPPDATA%`

After every run, the real `%LOCALAPPDATA%` had no folder named after any app id (`com.example.*`, `org.example.*`), `SecurityProbe`, `Notebook` or `WireShapes`. Each run's root also got `Microsoft\Windows\Caches` beside the app's folder (pitfall). The user's real Downloads folder had no probe file.

## Not verified

- **Real input on a Production executable:** F12 and Inspect absent, the accelerator keys (reload, print, find, zoom, Alt+Left) off, and the context menu cut down, all need real input, which here only Windows Sandbox's user session gives (asking first). The settings are set on the same code path the Development build read back.
- **The browser keys at all:** `Input.dispatchKeyEvent` never reaches the accelerators, so the Development filter (devtools and reload keys only) is checked by its unit test, not by a key press. F12 was not pressed: it opens a devtools window on the desktop.
- **The picker opt-in's dialog** and **the full context menu** of `AllowContextMenu()`: both would put a window on the desktop.
- **Password saving and autofill in a form:** read back only; the save prompt is browser UI.
- **The AppUserModelID:** set without a failure line; no tool here reads another process's explicit id.
- **The release-channel and script-debugger variables with the opt-in,** a policy-moved profile (only the variable moved it), the arguments Spar passes when `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` is also allowed (Microsoft documents that the variable is appended), the interception after a renderer crash, frames from other origins, and the SmartScreen net log (plan step 9).
- Windows 10, an older Runtime, Linux.
