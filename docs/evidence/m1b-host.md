# M1b: the Windows host, first slice

Recorded 2026-11-17. What it shows: the Notebook sample running in its own window on Windows, a NativeAOT single-file executable and a CoreCLR build, with its page served on `https://app.localhost`, its notes loaded through the WebView2 bridge, the two-stage close running the backend's guard, and recovery from a renderer and a browser that exit. Plan M1b, build step 1; decision "The Windows host, first slice (implementation record)".

## Environment

Windows 11 Pro 10.0.26320, SDK 11.0.100, WebView2 Runtime 157.0.4341.46 (the Runtime reported by the environment), `Microsoft.Web.WebView2` 1.0.4314.35 (loader and header only), `Microsoft.Windows.CsWin32` 0.3.369, Node 24. Warnings as errors with the trim and AOT analyzers on. The window was shown on the user's desktop, as an app's window is; no input was sent to it. Not run on Linux (there is no Linux host yet) or in Windows Sandbox.

## What was added

| Path | Contents |
|---|---|
| `src/Spar.Windows/Interop/` | WebView2 as hand-declared `[GeneratedComInterface]` interfaces, written from `WebView2.h` as flat interfaces (every slot of every base, `SlotNNN_<name>` placeholders), committed as source: `ICoreWebView2Environment10`, `ICoreWebView2Controller`, `ICoreWebView2_22`, the event args and handlers the host uses. `Com.Take`/`Borrow`/`Release` wrap every pointer as a unique instance and release it on the UI thread. The loader and `SHCreateMemStream` are `LibraryImport`s; Win32 is CsWin32 (`NativeMethods.txt`). |
| `src/Spar.Windows/UiThread.cs` | One STA thread with its message loop, its synchronization context and a message-only window for posted work; one window procedure whose objects live in each window's user data (no static map). |
| `src/Spar.Windows/WindowsApp.cs` | One run: the window shown at once with the system theme's colour as its class brush, the app's services built on the pool meanwhile, the close request and the teardown, the exit code. `SparWindowsHost` is the `ISparHost` the generated code creates. |
| `src/Spar.Windows/BrowserView.cs` | Environment (on the app's profile folder), controller, events, filters, the first navigation, and recovery from `ProcessFailed` and `BrowserProcessExited` in either order. |
| `src/Spar.Windows/PageServer.cs` | `WebResourceRequested`: the packed page from memory, 404 for unknown paths, 403 for foreign documents (second filter `*`, documents), 405 for other methods, 204 for CSP reports, `SparPageHeaders` and an exact `Content-Type` on every response, a deferral while Brotli files are still decoding. |
| `src/Spar.Windows/WebViewBridge.cs` | A `BridgeSession` per document: closed at `NavigationStarting`, opened at `ContentLoading`; messages from the top frame of the app's origin only, as strings; decoded and admitted on a reader task, in order; replies posted to the UI thread and dropped unless their document is still the current one. |
| `src/Spar.Windows/HostLog.cs`, `NativeDialog.cs` | The start-up and shutdown phases with a run id and times from the host's start, through the app's logging and standard error when the process has one; the "Close anyway / Keep open" task dialog (message box without Common Controls 6) and the failure message. |
| `src/Spar/Hosting/Closing.cs`, `CloseGuard.cs` | `CloseReason`, `CloseRequest`, `CloseDecision` and `SparBuilder.OnCloseRequested` in the shape of `docs/app-model.md` 6.11; the guard runner every host uses (thread pool, session-end deadline, automated mode). |
| `src/Spar/Runtime/ISparHost.cs`, `SparApplication.cs` | `SparApp.Run(ISparHost?)`, which the generated interceptor of `app.Run()` calls with the host the build references. |
| `src/Spar.Generator/Emit/HostEmitter.cs` | The interceptor: `new Spar.Windows.SparWindowsHost()` behind `OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)`, only when the compilation references it and outside the Test variant. |
| `src/Spar.Sdk/Sdk/Sdk.targets`, `Windows/app.manifest` | `SparPlatforms` (default `windows;linux`) wired: `Spar.Windows` by reference from source or as a package at the SDK's version; `Microsoft.Web.WebView2` for the loader (`DirectPInvoke` plus `WebView2LoaderStatic.lib` under NativeAOT, `WebView2Loader.dll` otherwise); the application manifest; the `Spar.Development.IsSupported` switch. |
| `src/Spar.Client/src/webview.ts` | `webViewChannel()`, `webViewPort()` and `hostChannel()`, which picks WebView2 inside the Windows host and loopback elsewhere; the Notebook's page now connects with `hostChannel()`. |
| `samples/Notebook/Program.cs` | A close guard: a page still being written asks before the window closes. |

## Results (observed)

| Check | Result |
|---|---|
| NativeAOT publish of the Notebook (`dotnet publish samples/Notebook -c Release`) | 0 warnings; one executable of **4,512,768 B** (M1a's Production publish: 2,918,400 B), no `WebView2Loader.dll` beside it |
| CoreCLR Release build and Debug with `SparDevServer=false` | both run in a window; `WebView2Loader.dll` beside them |
| Start-up, NativeAOT, 7 runs (median, range; ms from the host's start) | process to host 19 (18–27.5); window shown 18.5 (18.2–20.9); environment 24.2 (23.7–26.9); controller 177.6 (172.3–183.7); first document (`ContentLoading`) 222.2 (218.1–230.7); first bridge message 245.6 (238.6–255.2); `ui-ready` 265 (257.8–273.9) |
| Start-up, CoreCLR, 1 run | window shown 143 ms, controller 361, `ui-ready` 502 (the services build took 133 ms of JIT) |
| The page | the window shows the notes from the data folder (screenshot below), so `pages.list` crossed the bridge both ways; 404 for `/favicon.ico` only |
| Close: `WM_CLOSE` posted to the app's own window, 7 runs | the backend's guard ran and answered `Close` (logged), the page's close work answered, the document closed, the controller closed 8.8–10.7 ms and the process exited **0** 25.4–29.6 ms after the request (33–53 ms measured from outside) |
| Leftover browser processes | 0 of the 6 `msedgewebview2` processes left 3 s after each exit, in every run |
| Renderer killed (`--type=renderer` process terminated) | `ProcessFailed(RenderProcessExited)`: the document closed, one reload, a new document opened and reported `ui-ready` 72 ms later with the notes loaded; no command replayed |
| Browser killed (the main `msedgewebview2` process) | recovery once: environment and controller created again, a new document `ui-ready` 234 ms after the loss; the second notification of the same loss was ignored |
| Tests | Spar.Tests 186 of 186 (new: the slot-order test, close guard, page addresses, a closed session runs nothing); Spar.Generator.Tests 84 of 84 (new: the `Run` interceptor); WireShapes.Tests 79 of 79; Notebook.Tests 12 of 12; Spar.Sdk.Tasks.Tests 62 of 62; AppModel.Tests 10 of 10; the SDK's Vitest 165 of 165 (5 new: the WebView2 transport) and `npm run check` (TypeScript 6 and 7); the Notebook's page tests 7 of 7 |
| NativeAOT lane, WireShapes.Tests | 79 of 79, 0 warnings, 16,897,024 B |
| Browser tests | Notebook.EndToEndTests 7 of 7 and WireShapes.EndToEndTests 17 of 17 in Chromium, so `hostChannel()` keeps the loopback path; the static-state test there now also covers `Spar.Windows` |
| Spar.BuildTests | 6 of 6 (1 min 23 s), the Notebook's Production boundary included |
| Spar.PackageTests | 9 of 10 on the first run: the package consumer's NativeAOT executable now opened its window (so `Spar.Windows` worked from packages) and the test, written for M1a's "no desktop host" exit 2, timed out after 20 min. It now publishes that run with `-p:SparPlatforms=linux` and expects exit 3 with the message: 1 of 1 on the rerun of that test; the other 9 were not rerun after the change |
| Slot-order test, broken on purpose | a placeholder renamed to the wrong slot failed with "slot 5 is get_Bounds in WebView2.h, declared as Slot006_GetBounds" |
| Size check | 255 source files, 0 errors, 0 warnings |

## Screenshots

Captured with `PrintWindow` from a separate process, without input.

- [`m1b-host/notebook-aot-ready.png`](m1b-host/notebook-aot-ready.png): the NativeAOT Notebook after `ui-ready`, with the two notes of the data folder.
- [`m1b-host/notebook-after-renderer-exit.png`](m1b-host/notebook-after-renderer-exit.png): after its renderer was killed and the page reloaded once.
- [`m1b-host/notebook-after-browser-exit.png`](m1b-host/notebook-after-browser-exit.png): after its browser process was killed and the environment recreated.

## How it ran

The executables were started with `LOCALAPPDATA` pointing at `artifacts/webview2-profiles/m1b-host/`, so the profile was `…/m1b-host/Notebook/WebView2` and the notes `…/m1b-host/Notebook/data/pages/*.md` (decision "M1b's remaining questions without a spike": the per-user root comes from the environment only). Standard error was redirected to read the host's phases. A PowerShell probe found the window by its class (`Spar.Window`) and process, captured it, killed the renderer or the browser by command line, posted `WM_CLOSE`, waited for the exit and listed the `msedgewebview2` descendants before and after.

## Not done or not verified

- **The confirmation dialog** ("Close anyway / Keep open") was not shown: the Notebook's guard asks only while a page is being written, and answering it needs input. It belongs in a window test or Windows Sandbox.
- **Session end** (`WM_QUERYENDSESSION` is answered; `WM_ENDSESSION` does not yet run the guards with `SessionEnd`), the page's guard (`windows.onCloseRequested`), `CloseReason.App`, and the shutdown hang prompt are later steps.
- **Not in this slice** (build steps 2–8): the prerequisite checks and the start-up dialog (a missing Runtime is mapped to exit 3 from `CreateCoreWebView2EnvironmentWithOptions` only), single instance, the theme layers and dark title bar, the heartbeat, the navigation lock and permissions, the Production browser settings (devtools, accelerators, context menu, SmartScreen are still WebView2's defaults), the ignored environment overrides, the app id and channel folders, focus forwarding, the per-user log file, window tests.
- **The UI-thread stall probe** was not built; no stall was measured.
- **Linux, Windows 10, DPI other than 100 %, a real Runtime 139.**
