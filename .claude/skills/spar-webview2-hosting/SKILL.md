---
name: spar-webview2-hosting
description: "Change Spar's Windows WebView2 host: startup prerequisite checks, the STA and message pump, COM ownership, asset serving on `https://app.localhost`, the bridge, the navigation lock and response headers, the custom title bar and theme, Windows native services, devtools access to the page, the heartbeat, profiles and shutdown."
---

# Work on the Windows WebView2 host

Use `spar-native-interop` for declarations and ABI checks, and `spar-webkitgtk-hosting` for the Linux side of a cross-host change. Plan section 3 ("Windows", in `docs/design/hosts.md`) holds the design, with page security in `docs/design/security.md` and testing in `docs/design/testing.md`; `docs/pitfalls.md` ("Windows host", "Bridge and loopback", "Page security") holds the evidence.

## Threads and lifetime

- **Never block the UI thread.** The STA only drives the window and the WebView and marshals bridge messages. Backend commands, I/O, synchronous waits (`.Result`, `.Wait()`, `GetAwaiter().GetResult()`, `Thread.Sleep`, blocking locks), large payload parsing and long CPU work never run on it. Callbacks that run on it (`WM_NCHITTEST`, `IStream::Read`, `NavigationStarting`, `WebResourceRequested`, `ScriptDialogOpening`) answer from data already at hand, or take a deferral and finish elsewhere (plan 3, "The UI thread is never blocked").
- **Own the STA.** `Main` carries `[STAThread]`; without it environment creation fails with `0x80010106`, which looks like a broken runtime. One STA owns the window, environment, controller and their cleanup, and keeps pumping while native completions are outstanding. Track partial startup, borrowed COM references, event tokens, callbacks and deferrals separately.
- **Check COM contracts.** Verify interfaces against `WebView2.h` and the minimum supported Runtime. Feature-detect every interface newer than the base set; the slots M1 needs are listed in plan 3 ("Interface slots M1 needs") and checked by the slot-order test. Pair every subscription with its removal, and keep callbacks alive through late completions and shutdown.
- **Single instance first.** Enforce it before the environment exists. A second instance with different environment options fails at controller creation (`0x8007139F`, or `0x8000FFFF` when the first is exclusive): map both to a clear "another instance with different settings" error.
- **Shutdown follows plan 3** ("Teardown"), and starts only after the close request of the two-stage close is approved or skipped (designed at the start of M1b): mark closing, stop admitting calls and settle promises, keep pumping, check liveness before late replies, `Close` the controller on the STA. Exit without waiting for the browser. Wait for `BrowserProcessExited`, with a timeout, only before deleting or reusing a user data folder; it fires about 160 ms late and never after a failed controller creation (decision "Profiles and shutdown").
- **Failure recovery.** Handle `ProcessFailed` per view and `BrowserProcessExited` per environment, in either order; reset the affected call and stream state; no recovery during shutdown.
- **Heartbeat** (plan 3, "Hung pages"): ping every 5 s, three misses mark the page unresponsive. Arm at document creation, only for documents with the injected script; skip minimised, hidden and controller-hidden windows; keep one ping outstanding; treat `RenderProcessUnresponsive` and the alarm as one state with one dialog. "Reload page" terminates the renderer (pid from `GetProcessInfos`), then reloads once. Never kill on its own, never replay a command.

## Startup

- **Prerequisite checks, in plan 3's order:** OS build through `RtlGetVersion`, then `GetAvailableCoreWebView2BrowserVersionString` (never the registry), `CompareBrowserVersions`, then normal start, where a controller creation failure means "broken". Exit codes are fixed for every app and the same on both OSes: 3 missing, 4 too old, 5 broken, 7 OS too old (plan 3, "Missing prerequisites").
- **The message:** `TaskDialogIndirect`, which needs a Common Controls 6 and per-monitor DPI manifest, with `MessageBoxW` as fallback. The host opens links with `ShellExecuteW`. Write stderr when a handle exists and the per-user log file always. The app's C# hook may replace the dialog, never the exit code.
- **Window and readiness.** Show the window at once in the theme colour; never cloak it and never create the controller under a hidden parent, which does not render. Automation and headless runs use a shown, non-activated window placed offscreen, never `SW_HIDE` or minimized.
- **Theme without flicker (plan 3d):** a class brush plus a `WM_ERASEBKGND` handler, `DWMWA_USE_IMMERSIVE_DARK_MODE` before showing, the profile's `PreferredColorScheme`, and a transparent default background set at creation (`ControllerOptions3`, feature-detected). No `WEBVIEW2_DEFAULT_BACKGROUND_COLOR`.

## Serving, bridge and page security

- **One origin, every request answered.** Serve only the embedded frontend from `https://app.localhost`. Register the source-kinds filter (`ICoreWebView2_22`, all kinds) so workers are seen; answer unknown paths with 404 and foreign documents (`"*"`, `DOCUMENT`) with 403. Never answer with a redirect. The `MAP app.localhost ~NOTFOUND` resolver rule is defence in depth still to verify through the options object (plan 3, 3b).
- **Response headers on every response, worker scripts included:** the generated CSP (`connect-src https://app.localhost`, because WebSockets bypass the host), `nosniff` with an exact `Content-Type`, `Cache-Control: no-store`, and the other headers in plan 3b. Always send `Permissions-Policy`: on Windows it is the only gate that held for the camera.
- **Serving assets.** `IStream::Read` runs on the UI thread and the response is read whole, so never block in it. The WebView never decodes `Content-Encoding`: the host decodes Brotli entries on a background thread at start and serves stored entries straight from the image (`GetManifestResourceStream` is an `UnmanagedMemoryStream` under NativeAOT; `SHCreateMemStream` copies anyway).
- **Guard the bridge.** Validate origin and frame before routing any message. Send strings only (`PostWebMessageAsString`, never `PostWebMessageAsJson`). Keep app-specific dispatch out of the native transport. A navigation starts a new document epoch (plan 2c).
- **Debug proxy** (decision "Debug serves Vite through the app's own origin"): Development-variant code only. `WebResourceRequested` takes a deferral, `HttpClient` (`UseProxy=false`, no automatic decompression) fetches from Vite, and the deferral completes on the STA. Forward `Accept` and `Sec-Fetch-*`, pass `Content-Type`, never cache, and drop `ETag` and `If-None-Match` for HTML.

## Production defaults (plan 3c)

- Browser devtools, accelerator keys, the default context menu (text fields keep Cut, Copy, Paste and Select all), password saving and autofill, swipe and history navigation (mouse back and forward, Alt+Left) and host objects are off; downloads are denied. `<input type="file">` gets its policy at the start of M1b. `IsReputationCheckingRequired = false` while the navigation lock keeps the page on the app origin, on automatically when the app allows other origins (decision "SmartScreen default").
- **Permissions:** deny anything the app did not declare; answer `PermissionRequested` explicitly and set `SavesInProfile = false` (plan 3b, "Permissions, two layers").
- **Environment overrides:** ignore exactly `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS`, `WEBVIEW2_BROWSER_EXECUTABLE_FOLDER`, `WEBVIEW2_USER_DATA_FOLDER` and `WEBVIEW2_RELEASE_CHANNEL_PREFERENCE`, unless the app calls `AllowBrowserEnvironmentOverrides()`.
- Each default has an explicit C# opt-in, logged at startup and listed by `spar doctor`.

## Inspection

- Devtools and page automation exist only in Development and test builds and need an explicit session flag. Use in-process `CallDevToolsProtocolMethodAsync`; Spar never opens a remote debugging port (plan 3c, level 1). `AreDevToolsEnabled=false` only hides the browser UI and does not gate in-process CDP, so exclude automation at compile or trim time (a feature switch with `Trim="true"`), and prove the absence with the production boundary test.
- Before the first trusted input after a navigation, wait two animation frames; it can otherwise stall for 5 s.

## Native services

- **Title bar drawn by the page** (plan 3, "Title bar drawn by the page"): windowed controller, `WS_OVERLAPPEDWINDOW` with the `WM_NCCALCSIZE` adjustment, `WM_NCHITTEST` from the page's cached layout (CSS pixels, scaled by the window's DPI), caption codes through one host-owned `WS_EX_TRANSPARENT` child above the WebView, the UIA provider with `ProviderOptions_RefuseNonClientSupport`, and the press kept across `WM_NCMOUSELEAVE`.
- **On the STA:** file dialogs (`IFileOpenDialog`, `IFileSaveDialog`) and `GetProcessInfos` (re-list on `ProcessInfosChanged`). Read process memory with kept handles on a pool thread.
- **Dialogs and focus:** JavaScript dialogs become the OS message box through a deferral. `beforeunload` never prompts. Forward `WM_SETFOCUS` to `MoveFocus` so Tab reaches the page.
- **Tests with a tray icon** follow `AGENTS.md`: every icon leaves a per-user registry entry, so use Windows Sandbox or a fake (pitfall "notification-area icon").
- The decided APIs for each service are in plan 3 ("Native services") and `docs/app-model.md`; implement from the decided shape, not from the spikes' code.

## Loader, profiles and verification

- **Loader.** Under NativeAOT link `WebView2LoaderStatic.lib` (`DirectPInvoke` for `WebView2Loader` plus a `NativeLibrary` item, from `Microsoft.Web.WebView2` referenced with `ExcludeAssets="all" GeneratePathProperty="true"`). CoreCLR publications ship `WebView2Loader.dll`.
- **Profiles.** One user data folder per app, windows on the default profile. Shipped apps default to `%LOCALAPPDATA%\<Company>\<App>\WebView2`. In this repository pass a folder under `artifacts/webview2-profiles/`, never `%LOCALAPPDATA%`; tests use InPrivate. Clear data with `ClearBrowsingDataAll`, never `Profile8.Delete`, which poisons the name.
- **Verify on the real app.** Check startup, the affected native operations and shutdown on the published NativeAOT app, with an isolated profile, and that resources were released. Focus-dependent and real-input checks run in Windows Sandbox; checks that need the user go to the plan's table "Checks that need the user at the PC".
