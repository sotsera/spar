# M1c: the security defaults on the Linux host (environment overrides, `JSC_*`, developer extras, the browser's defaults)

Recorded 2026-11-20. What it shows: plan M1c, build step 6, section 3c on the Linux host, on the bridge probe (`tests/apps/BridgeProbe`) as a Development build and as a Production NativeAOT executable under an Xvfb of the run's own, with real input (XTEST) where a person's input matters: the browser environment overrides removed in-process unless `AllowBrowserEnvironmentOverrides()`, the user's decision on JavaScriptCore's options, the GPU workarounds passing, developer extras and GTK's own inspector, the context menu, downloads, the page's picker, history navigation and the browser's keys, each with its opt-in; steps 1 to 5's Linux tests again; the Notebook; the production boundary on Linux. Decision "Linux security defaults (implementation record; choices for review; 2026-11-20)".

## Environment

- **Linux:** WSL2 `Ubuntu-24.04` (kernel 6.18.58.1-microsoft-standard-WSL2), the normal user, no root; `libwebkitgtk-6.0-4` 2.52.6, `libgtk-4-1` 4.14.5, Mesa llvmpipe, SDK 11.0.100. The Xvfb of `docs/evidence/m1c-host/wsl.sh tools`, on its abstract socket only, with no window manager; a private D-Bus session per test run. Never WSLg's display.
- **Isolation:** each app run has its own `HOME` and `XDG_*` folders under the working copy's `artifacts/linux-homes/host-tests/<run>/`; `XDG_RUNTIME_DIR` is `/dev/shm/spar-m1c-security-defaults`; the runner clears every inherited `JSC_*`, `SPAR_DEV_*`, inspector, sandbox and GPU variable, and the tests set their own.
- **Input:** XTEST on the run's own X server only (clicks, a right click, keys, the mouse's button 8).
- **Network:** nothing left the machine; the inspector servers the tests asked for listened on loopback ports of the test's choosing, only in the runs with the opt-in, and ended with the app.
- **Windows:** Windows 11 Pro 10.0.26320, SDK 11.0.100, the same tree.
- **Working copy:** the Windows tree as it was, uncommitted changes included, copied to `/var/tmp/spar-m1c-security-defaults/repo` (`wsl.sh sync`); the WebAssembly build from a copy of the probe's folder (`tests/apps/BridgeProbeWasm`, only there); for `boundary`, the copy also gets the repository's `.git`, read only, since the build tests list files through git.

## What was added

| Path | Contents |
|---|---|
| `src/Spar.Linux/EngineEnvironment.cs` | `Filter`: after single instance and before any WebKit library is loaded, the names in libc's `environ`; `WEBKIT_INSPECTOR_SERVER`, `WEBKIT_INSPECTOR_HTTP_SERVER` and `WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS` removed in every build unless `AllowBrowserEnvironmentOverrides()`, every other `JSC_*` removed from a Production build unless the opt-in, kept in Development; `unsetenv`, checked with `getenv`, and .NET's copy too; one warning per variable, never its value; exit 70 if one stays. `Decide` is the pure rule. `Apply` (`JSC_useWasm`) no longer warns about other `JSC_*` |
| `src/Spar.Linux/BrowserDefaults.cs` | The view's settings (developer extras only in Development or with `AllowDevTools()`; script clipboard, script windows, both `file://` accesses and top-level `data:` off), logged in one line; `download-started` cancelled unless `AllowDownloads()`; `run-file-chooser` cancelled unless `AllowPagePicker()`; `context-menu` cut down as on Windows unless `AllowContextMenu()`, described in Development; GTK's inspector keys stopped at the window outside Development |
| `src/Spar.Linux/PageGuard.cs`, `BrowserView.cs`, `AppWindow.cs`, `LinuxApp.cs` | Back and forward to another document ignored unless `AllowHistoryNavigation()`, except the host's own reload (`HostReloads()`); the new signals connected (a missing one stops the start) and disconnected; the filter called before the library checks |
| `src/Spar.Linux/Interop/Libc.cs`, `WebKit.cs`, `Gtk.cs` | `unsetenv`, the `environ` names; the settings, context menu, file chooser and download calls; the key controller |
| `src/Spar/Hosting/BrowserOptions.cs`, `docs/app-model.md`, `tests/AppModel/Spar.Stubs/Browser.cs` | The opt-ins' documentation for Linux, `AllowBrowserEnvironmentOverrides()`'s list and risk among them; no public shape changed |
| `tests/apps/BridgeProbe` | `BRIDGE_PROBE_BROWSER` (opt-ins by name, at run time), the command `steps`, the page's steps `jsc`, `download`, `picker`, `menu:<kind>`, `keys`, `history` (`frontend/src/defaults.js`, `other.js`) |
| `tests/Spar.Tests/LinuxHostTests.cs` | The real-host tests below; `X11Frames.cs` now holds `XTest` (clicks with any button, keys) and window titles; `LinuxHostUnitTests.cs` the filter's rule, the menu's items and the inspector keys; `LinuxPageSecurityTests.cs` step 5's test with the new `JSC_*` lines |
| `tests/Spar.BuildTests/ProductionBoundary.cs` | `SPAR_DEV_PROMPT_ANSWERS` and the Development-only "Browser settings read back (" and "Context menu (" lines as markers |
| [`m1c-security-defaults/wsl.sh`](m1c-security-defaults/wsl.sh), [`m1c-security-defaults/`](m1c-security-defaults/) | The runner and the recorded results of the last run |

## Commands

From Git Bash at the repository's root, after `docs/evidence/m1c-host/wsl.sh tools` once:

```
MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/docs/evidence/m1c-security-defaults/wsl.sh sync client appmodel build test probe probewasm defaults regress probeaot defaultsaot regressaot notebook boundary
```

`explore <plan> [seconds] [NAME=value...]` runs the Development probe once and prints its log, the listening TCP sockets halfway through and the files in its home. On Windows: `dotnet build Spar.slnx -c Debug`, the solution's tests without the `Window` and `BlackBox` surfaces, the NativeAOT `Spar.Tests` lane (`dotnet publish tests/Spar.Tests -c Release -p:SparTestLane=Aot -o artifacts/aot/Spar.Tests`, then the executable), `dotnet run eng/check-size.cs`.

## Results (observed)

From the last runs (2026-11-20, 12:07–12:21 local): Development builds and Production NativeAOT executables gave the same results unless a row says otherwise. Each app run's log is kept in this folder for a few cases (`aot-*.log`, `dev-*.log`); the recorded lines are `linux-security-defaults-development.txt`, `linux-security-defaults-production.txt` and `notebook.txt`.

| Check | Result |
|---|---|
| The three overrides set, no opt-in | Each named `… ignored (AllowBrowserEnvironmentOverrides allows it).`; `WEBKIT_INSPECTOR_SERVER` and, in its own run, `WEBKIT_INSPECTOR_HTTP_SERVER` on a free loopback port: nothing listening (`/proc/net/tcp`, `tcp6`); the web process under `bwrap` although `WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS=1`; none of the three in the web or network process's environment |
| The same with `AllowBrowserEnvironmentOverrides()` | `Browser opt-ins in force: AllowBrowserEnvironmentOverrides()` and each `… in force (AllowBrowserEnvironmentOverrides).`; each inspector server listening in the app's own process; the web process without `bwrap`; all three in both processes' environments. With both inspector variables set (an earlier run), only the HTTP one listened |
| `JSC_useDollarVM=true` and `JSC_useWasm=true` inherited | Production: `JSC_useDollarVM ignored: a JavaScriptCore option (AllowBrowserEnvironmentOverrides allows it).`, absent from the web process; Development and the opt-in: `… in force …`, present, and JavaScriptCore printed `ERROR: invalid option: JSC_useDollarVM=true` (a restricted option in a release build); `typeof $vm` `"undefined"` in the page in all eight cases; `JSC_useWasm=false` in the web process in all, with `replacing the inherited JSC_useWasm=true` |
| GPU workarounds | `__NV_DISABLE_EXPLICIT_SYNC=1` and `WEBKIT_DISABLE_COMPOSITING_MODE=1` from the test, and Spar's own `WEBKIT_DISABLE_DMABUF_RENDERER=1` (software GPU), in the web process in every case, opt-in or not |
| Cost | The filter's lines carry the single-instance line's millisecond (under 1 ms); the settings 0.00 ms; the Notebook's start unchanged within noise (below) |
| Settings line | Production: `Browser settings set (0.00 ms): devtools off, GTK inspector keys off, script clipboard off, script windows off, file access off, universal file access off, data: navigation off; menu cut down, downloads denied, picker cancelled, history cancelled.`; with `AllowDevTools()` `devtools on`; Development `read back`, `devtools on, GTK inspector keys on` |
| Ctrl+Shift+I (real input) | Production, with or without `AllowDevTools()`: no window opened (1). Development: GTK's inspector and its warning opened (two more windows, one titled with the app id). WebKit's inspector never opened on the key: 1 web process throughout |
| `GTK_DEBUG=interactive` | GTK's inspector opened at start in both builds (two windows), so the user decided a Production build removes `GTK_DEBUG` unless the opt-in; the results after that change are in decision "GTK_DEBUG removed in Production" |
| Right click on plain text | Production: no menu (1 window). With `AllowDevTools()`, and in Development: a menu (one more window); Development's line `Context menu (not editable): kept [inspectElement], removed …` |
| Right click in an editable field | A menu in both builds; Development's line `kept [cut copy paste selectAll inspectElement]`; with `AllowContextMenu()` the full menu, not described |
| Browser keys | F5, Ctrl+R, Ctrl+Shift+R, Alt+Left, Alt+Right, Ctrl+P, Ctrl+F, Ctrl+=, Ctrl+-, Ctrl+0, F12, Ctrl+S, Ctrl+O: every one reached the page, zoom `1/900` before and after, one document, one window, both builds |
| Downloads (`download` links to `/other.html`, a `blob:` and a `data:` address) | Default: 1 `A download was cancelled (AllowDownloads allows it).` (the app's own file; Development adds its address), 2 refused by the navigation lock (`blob:`, `data:`), no file anywhere in the run's folders. `AllowDownloads()`: no cancel line, the same 2 refused by the lock, and no file written within 2 s, with a Downloads folder in `user-dirs.dirs` |
| The page's file picker (a click on a full-window file input) | Default: the page got `cancel`, one window, `The page's file picker was cancelled (AllowPagePicker allows it).`. `AllowPagePicker()`: GTK's own file chooser opened (a second window; the private session's portal has no GTK backend), `cancel` after Escape, no cancel line |
| History | `pushState` and `history.back()` within the document: `popstate`, back to no hash, both cases. `history.back()` from the second document: `Back or forward navigation cancelled (AllowHistoryNavigation allows it).` once, the page still at `/other.html`, 2 documents; the mouse's button 8 then did nothing. `AllowHistoryNavigation()`: no cancel line, back to the first page, 3 documents |
| The host's reload after a lost web process | Before the fix, 4 of 4 WebAssembly recovery tests failed: the reload arrived as a back-or-forward navigation and was cancelled; after `HostReloads()`, 4 of 4 passed (Development) |
| This step's tests | Development 125 of 125 (with the host's unit tests and step 5's WebAssembly tests); Production NativeAOT 125: 123 passed, 2 skipped (they need Development's prompt answers) |
| Steps 1 to 5's Linux tests (the six Linux classes) | Development 212: 209 passed, 3 skipped; Production NativeAOT 212: 201 passed, 11 skipped (Development only); run before the last 9 unit cases were added |
| Linux solution, without the Window and BlackBox surfaces | 1,079: 977 passed, 102 skipped, 0 failed (before the last 9 unit cases) |
| Production boundary on Linux (`Spar.BuildTests`, the Notebook published in a clean copy) | Passed (36.5 s): none of the markers, `SPAR_DEV_PROMPT_ANSWERS`, "Browser settings read back (" and "Context menu (" included; no DevTools Protocol method |
| Probe, Production NativeAOT | 4,425,296 B (step 5: 4,416,736 B) |
| Notebook, Production NativeAOT | 5,095,056 B (step 5: 5,094,768 B); `ui-ready` median 228.3 ms (213.2–255.2, n = 11), and 218.6 ms (212.2–226.4) in this step's run before the GTK key guard; close 39.9 ms (29.1–49.3); PSS 231.9 MB; 0 violation lines; `devtools off, GTK inspector keys off` |
| Windows | `dotnet build Spar.slnx -c Debug --no-incremental` 0 warnings; the solution's tests 1,088: 1,004 passed, 84 skipped; NativeAOT `Spar.Tests` 753: 685 passed, 68 skipped; 0 failures; `eng/check-size.cs` 503 files, 0 errors |
| Leftovers | No `Xvfb`, `WebKit*`, `xdg-desktop-portal`, probe, Notebook, `dbus-daemon` or `dotnet` process of the user's left in WSL after the runs |

## Not verified

- A real GNOME or KDE desktop (added to the plan's "Checks that need the user at the PC"): `AllowPagePicker()`'s file chooser through the desktop's portal (under Xvfb GTK fell back to its own dialog, a second window, and the page saw `cancel` after Escape), an `AllowDownloads()` download being saved (nothing was written in the run's home within 2 s, with or without a Downloads folder in `user-dirs.dirs`; Spar no longer cancels, which is what the test checks), the full menu of `AllowContextMenu()`, and WebKit's inspector opening from Inspect Element with `AllowDevTools()` (the item is there; opening it was not seen under Xvfb).
- Wayland, a real GPU (the GPU variables were checked to reach the web process, not to change drawing), WebKitGTK 2.54, other distributions' GTK schema defaults.
- Which JavaScriptCore options other than `useDollarVM` a release build accepts; the tests only show that a Production build removes them all.
- Windows' window suite (no Windows code changed).
