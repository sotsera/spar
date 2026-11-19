# Desktop hosts

Part of the Spar design. [`docs/plan.md`](../plan.md) has the goal, the settled decisions, the open questions and the milestones, and lists every design file. Section numbers are global: "section 3" anywhere in the plan, the design files or `docs/decisions.md` means the heading of that number, wherever it lives.

## 3. Desktop hosts: Windows (WebView2) and Linux (WebKitGTK)

The core (contracts, dispatch, protocol, SDK) is host-neutral. Each host provides the SDK's channel object, its transport, its origin checks, its automation and its native services, in `Spar.Windows` and `Spar.Linux`.

**The UI thread is never blocked** (user requirement, 2026-11-14, on both hosts). Work on the WebView2 STA or the GLib main loop must never block, and must take as little time as possible, so the window keeps painting, resizing, dragging and answering input while the backend works.
- **What may run on it:** window and WebView calls, marshalling bridge messages to and from the dispatcher, and short state updates.
- **What never runs on it:**
  - backend commands, including their DI scope;
  - file, network or database I/O;
  - synchronous waits (`.Result`, `.Wait()`, `GetAwaiter().GetResult()`, `Thread.Sleep`, blocking locks, `g_main_loop_run` nested to wait);
  - CPU work beyond a small budget;
  - serializing or parsing a large payload.
- **Host callbacks that run on it must return quickly:** `WM_NCHITTEST` reads a cache, `IStream::Read` and the WebKit scheme handler serve memory already in the executable, and `decide-policy` and `NavigationStarting` decide from data already at hand. Anything slower takes a deferral and finishes off the thread.
- **The UI-thread stall probe** (decided, user, 2026-11-17; decision "M1b's remaining questions without a spike"):
  - Development and Test builds include it: a timer posted through the pump or main loop logs each delay over 50 ms as a warning, with the Spar callback that was running.
  - Window tests fail on a stall over 250 ms, until M4 sets the budget from measurements.
  - Production builds contain no probe unless a C# option keeps it, logging only, for diagnosing a user's machine. It sits behind a feature switch, so without the option it is trimmed away and the production boundary test still finds none.
  - **Built on Windows** (M1b step 3; decision "Hangs: heartbeat, stall probe and shutdown prompt"): `Spar.Hosting.UiStallProbe`, one probe at a time posted through the UI thread's queue about every 20–35 ms, and every Spar callback on the thread named through `UiThread.Enter` (window messages, WebView2 events, posted work and `await` continuations); a callback during which the thread pumped (a modal dialog) is not a stall. `spar.Diagnostics(d => d.KeepStallProbe())` is the only thing that roots it in Production: the Production Notebook carries neither its type nor its message, the console harness, which calls it, carries both (32 KB), and both are checked by `Spar.BuildTests`. Window tests read `Longest` and `Recent()` (step 8).

**Hung pages are detected by a heartbeat** (user, 2026-11-14; confirmed by `spikes/page-heartbeat`, with the changes listed below). An idle page stuck in a loop raises no event: `ProcessFailed(RenderProcessUnresponsive)` needs unanswered input, and an idle hung page gave nothing in 60 s (earlier research).
- **Ping:** while a document is loaded and the window is visible, the host sends a small ping over the bridge every 5 s, and the injected script answers from its message handler.
- **Unresponsive:** three missed answers (15–20 s) mark the page unresponsive. The host then logs it, calls a C# hook, and by default shows a native "Wait / Reload page" dialog.
- **Limits:** the library never kills anything on its own. It makes at most one automatic reload, only after a document had loaded, and never replays backend commands.
- **Hidden or minimised windows** are not pinged, so a throttled background page is not reported. On Windows such a window also hides the controller (M1b step 6, "The window's state" below), so the page itself is `hidden` and throttled; it still answers the ping a close request sends, within 1 ms.
- **Measured by `spikes/page-heartbeat`** (WebView2 157 on the host and in Windows Sandbox, WebKitGTK 2.52 on 6.0). The design holds:
  - Every endless main-thread hang was caught: 163 of 163 runs with the heartbeat armed. No false alarm: 0 of 123 runs.
  - Detection takes 15–20 s, because the first missed ping can leave up to 5 s after the hang starts. Users are told "about 20 seconds".
  - Finite work of 3 s or 10 s never alarmed. A 20 s task alarmed and the page answered again about 2 s later.
  - A hung worker alone does not alarm, which is correct.
  - WebView2 never raised `ProcessFailed` for an idle hang in 72 s. It raises `RenderProcessUnresponsive` about 16 s after input reaches a hung page, including the focus change when a dialog opens.
  - WebKitGTK's `is-web-process-responsive` stayed true for every idle hang. It turned false only 3 s after a navigation request.
  - Pinging never touched the UI thread: 7–11 µs per post, pump p99 0.21–0.37 ms.
  - Messages queue inside a hung renderer, about 1.6 kB each.
  - **Only terminating the renderer recovers:** terminate, then reload once, gives a working page in 50 ms (Windows) or 100 ms (Linux). Reload, Stop + Reload and same-site navigation recovered 0 of 40 runs. WebView2 has no terminate API, so the host uses the process id from `GetProcessInfos`. Backend state survived, and no command was replayed.
- **Changes to the design, from the spike:**
  1. Keep 5 s × 3.
  2. Arm the heartbeat when the document is created, never at `NavigationCompleted`, which missed every hang during load.
  3. Disarm it for documents without the injected script, which otherwise give a false alarm at 20 s.
  4. Gate on minimised, hidden and controller-hidden windows. A suspended WebView answers nothing, and the controller-hidden gate covers it.
  5. Treat `ProcessFailed(RenderProcessUnresponsive)` and the heartbeat alarm as one state with one dialog.
  6. "Reload page" terminates the renderer, then reloads once.
  7. Keep at most one ping outstanding while the page is unresponsive.
  8. On Linux, rely on the heartbeat and `web-process-terminated`, not `is-web-process-responsive`, and terminate a hung web process before quitting. It otherwise outlives the app by about 10 s.
- **After "Wait"** (decided, user, 2026-11-17; decision "What "Wait" does after the hung-page dialog"): the heartbeat keeps pinging, one ping outstanding, and the dialog comes back after another three misses (about 20 s), then after 40 s, 80 s and every 160 s. A page that answers again closes an open dialog and resets the count and the interval.
- **Closing a window whose page does not answer** (its close guard silent for about 5 s, or already hung): the same dialog offers "Wait / Close anyway". Close anyway skips only the page's guard; the backend's guard still runs, then the window closes and its renderer is terminated. Wait keeps the close request open, with the same interval.
- **Milestone:** built with each host, in M1b and M1c.
- **Built on Windows** (M1b step 3; decision "Hangs: heartbeat, stall probe and shutdown prompt"; `docs/evidence/m1b-hangs.md`):
  - the `ping` and `pong` bridge messages (section 2c). Since step 4 (decision "Page security (implementation record; choices for review)") the host's injected script answers them, and the heartbeat is armed when the document is created (`ContentLoading`, not for an error page), before any page script runs: a page that hangs in its own start-up code, before it connects the SDK, was reported 19.6–19.7 s after its document opened (3 runs, `docs/evidence/m1b-page-security.md`). Step 3 had armed it on the SDK's `hello` (decision "The page hang hook gets its async form; the heartbeat's start moves to step 4");
  - the rules in `Spar.Hosting.PageHeartbeat`, for both hosts; the Wait schedule counts visible ticks, so each return comes up to one interval early;
  - the dialogs on a thread of their own, owned by the window and taken down by the host when the page answers; "Reload page" ends every renderer of the app's environment (`GetProcessInfos`), then reloads once, without using up the automatic reload;
  - a close request pings the page and waits one interval until the page's own close guard is built; built in M1b step 8 (decision "Window tests: closing, session-end budget, test theme and any-character keys"): the ping goes first, then `close-request` to a page whose SDK listed it (section 2c). After one interval (about 5 s) without the guards' answer, a page whose last pong said it shows a modal dialog of its own (an open modal `<dialog>` or a visible `aria-modal="true"` element) is waited for, and checked again each interval; a page that answers pings but not the request has a silent guard and gets "Wait / Close anyway" without being reported to `OnPageUnresponsive` (it is not hung); a page that answers nothing is hung, reported, and gets the same dialog. Wait keeps the request open and asks again after 4, 8, 16, then every 32 intervals. A script `confirm()` in a guard blocks the page and counts as no answer. A document that goes away during the request agrees, and its late answer is ignored. Measured (window tests, a test answering in place of the dialog): a silent guard's and a hung page's "Close anyway" closed the window 5.0 s after the request, and "Wait" on a page hung for 12 s ended with its guard's own answer 11.9 s after the request (`docs/evidence/m1b-window-close.md`);
  - measured: alarm 15.5–19.2 s after a loop began and 16.8–18.9 s after the renderer was suspended; reload to `ui-ready` 85–140 ms; the tick costs the UI thread 0.15–0.36 ms on NativeAOT; no false alarm on an idle page or on work of 3 s and 10 s. `RenderProcessUnresponsive` never fired on the desktop, where the windows were not activated and the dialog took no focus.
- **Not tested:**
  - CPU stress;
  - hidden windows for longer than 60 s;
  - several WebViews sharing one browser;
  - the native dialog on Linux;
  - hangs in iframes;
  - sleep, lock and session switch.

**Missing prerequisites get a visible error** (user requirement; `spikes/startup-prerequisites`; built in M1b for Windows and M1c for Linux). An app launched from an icon has no terminal. When something it needs is missing, too old or broken, it must show a native message saying what is missing and how to fix it, then exit with a defined code. It must never crash silently.
- **Exit codes, the same on both OSes (decided, user, 2026-11-14):**
  - 0 OK;
  - 2 bad command line;
  - 3 missing;
  - 4 too old;
  - 5 present but broken;
  - 6 no graphical session (Linux);
  - 7 OS too old;
  - 8 already running but not responding (single instance: no acknowledgement within 2 s, or still closing after about 5 s);
  - 9 already running for another user on the same data folder (decided, user, 2026-11-17; decision "The relaunch handler").
  - 10 shutdown did not finish and the user chose "Exit now" (decided, user, 2026-11-17; decision "Hangs: the page's hook, the Production stall probe and "Exit now"").

  Apps cannot change them.
- **Windows, in order** (built in M1b step 2; decision "Start-up: prerequisites, single instance and theme"):
  1. `[STAThread]` on `Main`. Without it, environment creation fails with `0x80010106`, which would wrongly blame the runtime. Spar's host runs the window, the WebView and the start-up message on STA threads of its own, so the app's `Main` needs no attribute.
  2. The OS build through `RtlGetVersion`: Spar's minimum is Windows 10 22H2, build 19045 (decided provisionally, user, 2026-11-17; M4 confirms it on a real Windows 10). Windows 11 features are detected at run time. A Windows older than 1809, which cannot load the host at all, gets the same "OS too old" from `Spar.Runtime.UnsupportedWindowsHost`, which the generated `Run` hands over there.
  3. Single instance (below), before anything touches the profile, and before the Runtime check so that a relaunch hands over at once (0.4–0.9 ms from the host's start).
  4. `GetAvailableCoreWebView2BrowserVersionString`, never the registry: the loader follows `ClientState\…\EBWebView`, and a stale path reads as missing while the registry looks fine. It reads the version from `EmbeddedBrowserWebView.dll`'s version strings.
  5. `CompareBrowserVersions` against the minimum, Runtime 139.0.3405.78.
  6. Normal start. A controller creation failure maps to 5.

  Environment creation succeeds even with the browser deleted, and `BrowserProcessExited` never fired in any failure, so only controller creation detects a broken runtime.

  **Measured in Windows Sandbox** (each case 3 runs, real or simulated):
  - dialog after 46–128 ms when the runtime is missing (exit 3);
  - 49–104 ms when it is too old (4);
  - 71–285 ms when it is broken (5);
  - a healthy window was ready in 281–305 ms.
- **Windows dialog:**
  - **The dialog:** `TaskDialogIndirect`, which needs a manifest declaring Common Controls 6 and per-monitor DPI awareness. Without it the call throws `EntryPointNotFoundException`.
  - **Its content:** the missing item, a "Download the WebView2 Runtime" button and link to Microsoft's end-user page (https://developer.microsoft.com/microsoft-edge/webview2/consumer/), and expandable details. The host opens the link with `ShellExecuteW`; the dialog never opens links itself.
  - **Fallback:** `MessageBoxW` (25–81 ms, no links, only system-DPI-aware).
  - **Dark mode:** neither dialog follows it.
  - **Screen readers:** UI Automation exposes every text and button.
  - **The app window:** the app's empty window must be hidden or become the dialog's owner. Built: a problem found before the window exists has no owner; a controller failure's message is owned by the window, which keeps pumping meanwhile (the message runs on an STA thread of its own).
  - **Built (M1b step 2):** the main instruction "<app> can't start", the problem's sentence, the link and the "Download the <component>" button when there is a page, and the problem's line and the log file's path as expandable details. Observed 67–449 ms from launch to the message on NativeAOT and CoreCLR (`docs/evidence/m1b-startup.md`).
- **Linux, in order:**
  1. The display variables. With no display there is no graphical message: exit 6 within 6–23 ms, with stderr and the journal.
  2. GLib, then GTK 4.
  3. `g_set_prgname`. Without it the window has no class or app id and shows as "Unnamed Window".
  4. `GSK_RENDERER=cairo` for the startup error dialog, if the user hasn't set it. The app itself gets cairo only on a software GPU (decision "Guidance review applied", point 3).
  5. `gtk_init_check`.
  6. WebKitGTK and its version.
  7. The helper folder, `bwrap` and `xdg-dbus-proxy`.
  8. A trial `bwrap` run. A denied sandbox fails it in 1.5 ms; without the trial, WebKit aborts after 4.4–6.7 s with exit 133 and no message.
- **Linux message:**
  - **With GTK 4 initialised:** the GTK dialog (`GtkAlertDialog`). It takes 91–212 ms with cairo, against 420–820 ms with GTK's GL renderer on a software GPU.
  - **Otherwise,** in this order: kdialog (first on KDE), zenity, yad, a D-Bus notification, notify-send, then xmessage. zenity needs GTK 4 on Ubuntu 24.04, so it fails exactly when GTK 4 is missing.
  - **Packages:** the message names the package to install.
  - **What desktops ship:** Ubuntu 24.04 and Fedora 43 Workstation already have everything. Kubuntu 24.04 lacks WebKitGTK 6.0 and `xdg-dbus-proxy`, so it gets the GTK dialog asking for `libwebkitgtk-6.0-4`.
- **Both OSes:**
  - stderr is written whenever a console or redirected handle exists. Built on Windows: a desktop executable started from a console without redirection attaches to it for the problem's line.
  - The system log is written too: the journal's native protocol or `/dev/log` on Linux. On Windows, a log file in the app's per-user folder, because an unregistered event-log source reads poorly. Built: the app's log file in its logs folder (M1b step 2 as `host.log`; since step 7 `app.log`, the app's whole log).
  - A C# hook runs before the default dialog and receives the problem's details. It may replace the dialog with the app's own message, and suppress Spar's dialog only when it shows one. stderr and the system log are written regardless, and the exit code cannot change (decided, user, 2026-11-14). Its shape (decided, user, 2026-11-17): `spar.OnStartupProblem(problem => …)` returning true when the app showed its own message, with `StartupProblem` (`Kind`, `Component`, `Found`, `Required`, `Details`, `HelpUrl`), before the app's services exist, also for a relaunch that fails (8, 9); `docs/app-model.md` 3.15. In the automated mode it is not called and no message shows.
- **Not tested:**
  - DPI above 100 %, and Narrator and Orca;
  - the download page opening;
  - a real older Windows or WebView2 Runtime, per-user installs, non-stable channels;
  - real GNOME and KDE desktops, and a `.desktop` launch.

  The checks that need the user are in the table "Checks that need the user at the PC".

**Windows:**
- **Threading:** one STA thread owns the window, environment, controller and their cleanup. Its message pump stays responsive. No synchronous waits on pump-dependent work.
- **How an app reaches its host** (built in M1b step 1; decision "The Windows host, first slice"): the generator intercepts `app.Run()` and passes the host the build references (`Spar.Windows.SparWindowsHost`, created behind an OS check that NativeAOT folds per target), so no reflection finds it and each executable keeps only its own OS's host. The host builds the app's services on the thread pool while its STA shows the window and creates the environment.
- **Document epochs on WebView2** (built): `NavigationStarting` ends the current document's session; the next one opens at `ContentLoading`, before the new document can send, and a message arriving between the two is dropped. Messages are copied out on the STA and decoded and admitted on one reader task, in order; replies are posted back and sent only while their document is current.
- **Bindings:**
  - CsWin32 for Win32;
  - hand-declared `[GeneratedComInterface]` for WebView2 (CsWinRT 3.0 added 10–23 MB; not now), with a slot-order test;
  - the WebView2 loader linked statically under AOT, and `WebView2Loader.dll` published on CoreCLR.
  - Feature-detect newer interfaces, and test on the minimum supported Runtime.
  - **The provisional minimum Runtime is 139** (decided, user, 2026-11-17; decision "M1b's remaining questions without a spike"), with no fallbacks: every interface below is stable there, find in page (`ICoreWebView2_28`, 139) and `ICoreWebView2ControllerOptions3` (137) being the newest. An older Runtime gets the start-up dialog and exit 4. M4 confirms it on a real Runtime 139.
  - **Interface slots M1 needs beyond the base set,** as the spikes declared them (0-based vtable slots, `IUnknown` included). The evidence is in the spike files named; the slot-order test should check each one against the WebView2 header:
    - `ICoreWebView2_22` (`db75dfc7-a857-4632-a398-6969dde26c0a`), declared flat: slots 3–122 are placeholders, and `AddWebResourceRequestedFilterWithRequestSourceKinds` is slot 123. It is called with context 0 (all) and source kinds `0xFFFFFFFF`. Also `ICoreWebView2WebResourceRequestedEventArgs2.GetRequestedSourceKind` (1 document, 2 shared worker, 4 service worker) and `ICoreWebView2PermissionRequestedEventArgs3.SavesInProfile` (`spikes/desktop-origin/Origin.Win/Interop/WebView2.cs`).
    - For focus and the window's state (M1b step 6): `ICoreWebView2Controller` (`4d00c0d1-…`) 12 `MoveFocus`, 13/14 `add/remove_MoveFocusRequested`, 23 `NotifyParentWindowPositionChanged`; `ICoreWebView2MoveFocusRequestedEventArgs` (`2d6aa13b-…`) 3 `get_Reason`, 5 `put_Handled`; all in the base interfaces.
    - For the theme, called through raw vtables (`spikes/theme-startup/Theme.Desktop/Interop/WebView2.cs`, class `WebView2Raw`): `ICoreWebView2Controller2` (`c979903e-…`) 26/27 get/put `DefaultBackgroundColor`, the colour struct passed by value; `ICoreWebView2_13` (`f75f09a8-…`) 105 `get_Profile`; `ICoreWebView2Profile` (`79110ad3-…`) 8/9 `PreferredColorScheme`; `ICoreWebView2Environment10` (`ee0eb9df-…`) 20 `CreateCoreWebView2ControllerOptions` and 21 `CreateCoreWebView2ControllerWithOptions`; `ICoreWebView2ControllerOptions3` (`b32b191a-…`) 10 `put_DefaultBackgroundColor`.
- **Assets and the bridge:** `WebResourceRequested` on the app origin `https://app.localhost` (user decision; verified by `spikes/desktop-origin`), and `postMessage` for calls.
  - The host answers every request to its origin, with a 404 for unknown paths. An unanswered request opened 2 TLS connections to `[::1]:443`; a host 404 never leaked.
  - The filter is registered with `AddWebResourceRequestedFilterWithRequestSourceKinds(…, ALL)` (`ICoreWebView2_22`, stable since Runtime 122; the provisional minimum Runtime is 139, decision "M1b's remaining questions without a spike"). The legacy filter missed shared and service workers.
  - WebSockets are never intercepted, and CSP `'self'` in `connect-src` still allows `wss://app.localhost`. So the policy names the origin with its scheme (section 3b).
  - **Defence in depth:** `--host-resolver-rules="MAP app.localhost ~NOTFOUND"` stopped everything else, including the preconnects every navigation makes to `[::1]:443` before any request. It gave 0 loopback connections, with no load-time cost. Verified in M1b step 9 through the options object (`spikes/webview2-flags`): 0 loopback connections in 253 runs against 16 per run (3 navigations) without it, no load cost, and no change in the rate of the rare 5 s first-load stalls (5 of 400 with it, 4 of 400 without). The value must be quoted inside the argument; unquoted, the browser keeps only `MAP`. The host passes it always, before its other arguments; a window test counts connections on both loopback addresses.
  - **Load times** (10 alternating runs each, median from `Navigate()` to `ui-ready`): `app.localhost` 68.5 ms, `app.spar.invalid` 67.3 ms, `app.example` 68.8 ms, `app.spar.local` 190 ms.
  - **Local network access** rules did nothing for host-served pages, for either `.localhost` or `.invalid`: loopback fetches got 200 with no prompt. CSP is the only gate protecting loopback services from the page.
  - The origin keys the page's stored data (`localStorage`, IndexedDB, cookies, remembered permissions), so it never changes between versions without a migration.
- **Instrumentation from the first slice:**
  - Monotonic timestamps, a run ID, and view, navigation and call IDs.
  - Milestones: window visible, environment and controller ready (with HRESULTs), navigation start/DOMContentLoaded/complete with status, `ui-ready`, each RPC (bytes, duration, handler time), and every shutdown phase.
  - Consumers: tests, devtools and measurements.
- **Teardown, in this order.** It starts only after the close request of the two-stage close is approved (decision "Two-stage close"; `docs/app-model.md` 6.11: the page's guard, then the backend's, each close, keep or ask; the automated mode runs them without prompts):
  1. Mark the view closing, so a second close is harmless.
  2. Stop admitting calls, cancel, and settle pending promises.
  3. Keep pumping during bounded cleanup.
  4. Check view and document liveness before every late reply.
  5. Close the controller explicitly on the STA. `Close` is synchronous. On Runtime 157 it does run `beforeunload`, `visibilitychange`, `unload` (29 of 29) and `pagehide` (26 of 29), and their writes persisted (`spikes/webview-profiles`; this corrects an earlier note). Required cleanup still must not depend on the page.
  6. Observe `BrowserProcessExited` before deleting a profile.

  The earlier research's mitigation for a handle leak in a visible window's teardown (hide the controller, navigate to `about:blank`, post the final close) is not used: on Runtime 157 the leak did not reproduce in 100 cycles, with windows offscreen and on the screen, and the mitigation changed nothing measured (M1b step 6; decision "Host details (implementation record; choices for review)", `docs/evidence/m1b-host-details.md`). The only steady growth seen, about one handle of the GPU process per window opened and closed, is the same with it.

  A page that does not answer gets no close work, and its renderer is ended before the controller closes. From the teardown's start until the process returns its code, the shutdown is watched: after about 10 s, "Keep waiting / Exit now", without an owner, again every 30 s; "Exit now" exits with code 10 (built in M1b step 3; decision "Hangs: heartbeat, stall probe and shutdown prompt").

  **When Windows ends the session** (built in M1b step 8a; decision "Window tests on Windows (implementation record; choices for review)"; budget decided by the user on 2026-11-18, decision "Window tests: closing, session-end budget, test theme and any-character keys"): `WM_QUERYENDSESSION` is answered `TRUE`, and `WM_ENDSESSION` with `TRUE` starts a close request with `CloseReason.SessionEnd`, all within 4 s of the message's start. The page's guard is asked with `sessionEnd` (at most 2 s, no dialog; `close-ended` when it runs out), then the backend's guard in what is left of 3 s; neither can keep the window or ask. Then the same teardown, its waits cut to the 4 s mark, and the disposal of the app's services. The window holds the message, pumping its thread, until all of that is done or 4 s have passed, then returns whatever is left. A later `WM_ENDSESSION` with `FALSE` changes nothing: the phase is not cancellable. Why 4 s: Microsoft's [`WM_QUERYENDSESSION`](https://learn.microsoft.com/windows/win32/shutdown/wm-queryendsession) page says that after five seconds the system shows which applications are preventing shutdown; the archived [Application Shutdown Changes in Windows Vista](https://learn.microsoft.com/previous-versions/windows/desktop/ms700677(v=vs.85)) (Table 1) says an app with a visible top-level window may take as long as it needs in `WM_ENDSESSION`, with new UI after 5 seconds; and the [`WM_ENDSESSION`](https://learn.microsoft.com/windows/win32/shutdown/wm-endsession) page says the session can end any time after every application has returned from the message. Measured: 4,004 ms with a page guard and a backend guard of 5 s each, a call in flight and a service whose disposal takes 5 s (the disposal still running when the message returned); 75 ms with nothing slow. A real sign-out on Windows 11 is not verified (plan, step 8c).
- **The window's state, passed on to the WebView** (built in M1b step 6, `ViewWindowState`; decision "Host details (implementation record; choices for review)", `docs/evidence/m1b-host-details.md`). A windowed controller is a child window, and Windows tells it none of these:
  - **Focus:** `WM_SETFOCUS` on the window calls `MoveFocus(Programmatic)`, so activation (through `DefWindowProc`'s focus), Alt+Tab and a closed dialog give the page the keyboard; a window activated while minimized gets it when restored, and a controller created after the window had focus takes it at once. Nothing moves the focus while the window is disabled (a modal dialog, the hung-page dialog owns it) or not the active window. `MoveFocusRequested` (Tab past the page's last field, Shift+Tab before its first) is handled and posts `MoveFocus` with the same reason, so focus comes back in at the page's other end and never stays on the window. Without this the keyboard reached the page only after a click into it (real input in Windows Sandbox).
  - **Visibility:** `WM_WINDOWPOSCHANGED` sets the controller's `IsVisible` to "shown and not minimized". Minimized, the browser already made the page `hidden`; hidden with `SW_HIDE`, the page kept 60 frames and full-rate timers until the controller was hidden. M3's close to tray hides the window and is followed the same way; a tray state that kept the window shown would add its condition there. The WebView keeps its last size while minimized.
  - **Position:** every move (`WM_WINDOWPOSCHANGED` without `SWP_NOMOVE`) calls `NotifyParentWindowPositionChanged`. Without it, a `<select>`'s list opened from the keyboard after a move appeared at the old place.
  - **Not verified:** IME candidates, the page-drawn title bar's overlay, DPI other than 96, a real desktop's foreground rules.
- **Failure recovery:** handle `ProcessFailed` per view and `BrowserProcessExited` per environment, in either order. Reset the affected RPC and stream state. Coordinate recreation across views. No recovery during app shutdown.
- **Hardening:** `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` is appended even in Production, so Production ignores it and the other browser environment overrides unless the app opts in (section 3c). Built in M1b step 5 (decision "Security defaults (implementation record; choices for review)"): the host removes the variables from its own process before the loader's first call (the Runtime check), which the published NativeAOT executable showed is enough; then the environment options carry Spar's arguments, the WebView's settings and events carry the browser defaults of section 3c, and the host checks the profile folder actually in use (`ICoreWebView2Environment7.UserDataFolder`). The process's explicit AppUserModelID is the app id of its channel, set before the window exists.
- **Title bar drawn by the page** (`spikes/windows-titlebar`, real input in Windows Sandbox). **Decided (user, 2026-11-14): when an app uses a custom title bar, the page draws the caption buttons**, on Windows as on Linux:
  - **Controller: windowed**, as in M1. Visual hosting also passed every input check, but it needs mouse forwarding, DirectComposition and a host-drawn system menu. Drag and drop, touch, pen, the wheel and IME are untested there, so it stays a fallback.
  - **Frame:**
    - keep `WS_OVERLAPPEDWINDOW`;
    - in `WM_NCCALCSIZE`, call `DefWindowProc` and then restore the top edge. When maximized, add `SM_CYSIZEFRAME + SM_CXPADDEDBORDER`, so the content is exactly the work area;
    - the host answers the top resize edge in an 8 px band (96 DPI).

    No `DwmExtendFrameIntoClientArea` is needed.
  - **Page:**
    - `IsNonClientRegionSupportEnabled` with CSS `app-region: drag` on the bar and `no-drag` on its controls;
    - the page posts its button layout to the host whenever it changes;
    - the host's `WM_NCHITTEST` reads only that cache (p50 0.1–0.5 µs, p99 under 4 µs, unchanged while the page was busy).
  - **Caption buttons drawn by the page, by default.**
    - The host returns `HTMINBUTTON`, `HTMAXBUTTON` and `HTCLOSE` through one host-owned `WS_EX_TRANSPARENT` child window above the WebView, clipped to the top band and the buttons.
    - It is needed because WebView2's drag-region windows turn every non-resize code into `HTCAPTION`.
    - With it, the Windows 11 Snap Layouts flyout appears on the page's maximize button.
    - Theme: 0 wrong-colour frames at start and across 8 live switches.
  - **Windows' own buttons** work only restored. Maximized, `DwmDefWindowProc` stops reporting them and restore clicks do nothing; the `WM_GETTITLEBARINFOEX` workaround was not tried. In dark mode they also show DWM's `#4a4a4a` for about 350 ms at every start.
  - **Verified with real input:** drag, controls inside the bar, double-click, right-click system menu, the maximized state, the four resize edges, Snap, `Alt+Space`, `Alt+F4`, `Win+arrows`.
  - **Cost:** +4,096 bytes of AOT executable, no start-up difference (393 against 389 ms), about 170 lines of host code.
  - **Open:**
    - DPI other than 100 %, `WM_DPICHANGED`, mixed-DPI monitors, Windows 10 and the shadow were not tested. The Sandbox guest takes its scale from the client window on a 96 DPI monitor, and four ways to change it failed. This needs a host monitor at that scale, which is the user's setting, or a VM with a console session.
    - Narrator was not run, because its audio would play on the user's speakers.
  - **Accessibility, solved** (`spikes/windows-sandbox-checks`, native UIA client on the host and in the guest):
    - A UIA provider on the top-level window with `ProviderOptions_RefuseNonClientSupport` removes the empty native title bar.
    - Subclassing WebView2's drag-region windows, which run on our UI thread, and the host overlay hides the unnamed panes.
    - The control view drops from 45 to 24 elements, the page stays fully exposed, and every input check still passes.
  - **First click on an inactive window** was lost on the page's caption buttons, because activation sends `WM_NCMOUSELEAVE` between press and release. Fixed: keep the press across it.
  - **Windows' own buttons become viable when maximized** if the host hit-tests them from `WM_GETTITLEBARINFOEX` and handles the clicks only while maximized (restore, minimize, close and Snap Layouts work). Taking over while restored broke them. They stay invisible to UIA. Page-drawn buttons remain the decided default.
    - The cache is kept in CSS pixels and scaled by the window's DPI at hit-test time.

**Linux:**
- **API: `webkitgtk-6.0` (GTK4) only** (user decision, 2026-11-14). There is no GTK3 build and no compile-time switch; the 4.1 measurements below stay as evidence. The Linux choices that depended on it are decided: acceleration off plus cairo on software GPUs, and XTEST for trusted test input (below). M4 checks the oldest distribution with a usable `webkitgtk-6.0` and GTK4: another framework documents Ubuntu 24.04 or Debian 13 as the minimum for the same stack, while the package pages list `webkitgtk-6.0` for Ubuntu 22.04.
- **Trusted input in window tests (decided, user, 2026-11-14): XTEST.**
  - Tests force `GDK_BACKEND=x11` and send input through XTEST. Under XWayland it delivered trusted Tab and clicks (`isTrusted: true`) on GTK3 and GTK4 (`spikes/linux-wsl-desktop`).
  - In CI they run on Xvfb in a container.
  - WebKitWebDriver is out: Ubuntu packages it only for 4.1, and it opens a TCP port.
  - The test framework sends input through one interface, so a Wayland route can be added later.
  - Wayland input stays a check that needs the user.
- **Threading:** GTK and WebKit on a dedicated thread with a GLib main loop. A `SynchronizationContext` posts through `g_idle_add_full`. The loop keeps running until the web processes exit.
- **Bindings:** `[LibraryImport]` and `[UnmanagedCallersOnly]` against gtk, glib, gio and webkit. The AOT exe links only glibc, and the libraries load at first use, so check for them at startup with a clear error.
- **Assets and the bridge:** a custom scheme (`spar://app/`), and the script message handler.
- **Origin policy:** messages and scheme requests carry no origin. Use a navigation lock, inject into the top frame only, and run the bridge in an isolated script world with a relay in the top document only (adopted from the research review, user, 2026-11-15). M1c builds it and tests foreign iframes against it. Freezing the bridge entry points and `Object.prototype` as a section 3c opt-in is still to discuss.
- **Hardening:** ignore `WEBKIT_INSPECTOR_SERVER` and the other browser environment overrides in Production (section 3c). GStreamer, Mesa and dconf write to `~/.cache`.
- **The sandbox:** always on with GTK4 (a non-root user, user namespaces). It works in WSL, costing 5–22 ms.
- **Startup order** (`spikes/linux-wsl-desktop`): create and attach the web view, call `load_uri`, then show the window.
  - Shown first, GTK3 on X11 without the sandbox stalled about 0.5 s. WebKit's draw handler waits for the web process's first frame, while the web process waits for the UI thread to set up its network connection.
  - Shown first, the page also lacks focus, and the first Tab is lost.
  - With this order: 167–185 ms warm on GTK3, 214–261 ms on GTK4 (WSLg, software GPU).
- **Recovery:** handle `web-process-terminated` with a visible message and recovery.
- **Built in M1c step 1** (`src/Spar.Linux`; decision "The Linux host, first slice (implementation record; choices for review)", `docs/evidence/m1c-host.md`):
  - **How an app reaches its host:** as on Windows, the generator hands `Spar.Linux.SparLinuxHost` to `app.Run()` behind `OperatingSystem.IsLinux()` when the build references it; the Notebook's Linux executable keeps no Windows host and links `libc` and `libm` only.
  - **Document epochs on WebKitGTK:** `WEBKIT_LOAD_STARTED` ends the current document's session and `WEBKIT_LOAD_COMMITTED` opens the next, on the app's origin only; a message between the two is dropped. A document's `BridgeSession` is created by the reader for its first message once the services exist, so the view loads and the window is presented without waiting for them.
  - **The relay:** the handler `spar` and the relay live in the isolated world `spar`, injected into the top frame at document start for `spar://app/*`; page and relay meet as `spar:send` and `spar:receive` events with string details on the top window. A frame of another origin reached nothing (tested).
  - **Recovery** follows Windows' accepted policy: one silent reload after a lost web process, given back at the next `ui-ready`, and exit 5 with a message on a second loss before it.
  - **Teardown:** the backend's guard, the page's close work, calls stopped and settled, the view's signals disconnected, the window destroyed, then the loop runs until the app's `WebKit*` descendants have exited (17–49 ms here; at most 3 s).
- **Startup check:** see "Missing prerequisites get a visible error" above for the order, the messages and the exit codes. It covers the libraries, the WebKit helper folder (found through `/proc/self/maps`) and `bwrap`/`xdg-dbus-proxy`. An earlier prototype exited with code 3 within 7–102 ms, naming what is missing and the package to install. Before it, the app died with an unhandled `DllNotFoundException` after 253–813 ms (exit 134), or with a GLib abort (SIGTRAP, exit 133) after 1.9–4.3 s for a missing helper or `bwrap`.
- **API differences between 4.1 (GTK3) and 6.0 (GTK4)** (`spikes/linux-desktop`). One code base with a compile-time switch covered both, with about 20 `#if` blocks in the host:

  | Topic | 4.1 / GTK3 | 6.0 / GTK4 |
  |---|---|---|
  | Data and cache folders | `WebKitWebsiteDataManager` (`base-data-directory`, `base-cache-directory`), created through `g_object_new_with_properties` because the constructor is variadic, then `webkit_web_context_new_with_website_data_manager` | `webkit_network_session_new(data, cache)`, passed to the view as the construct property `network-session` |
  | Script message signal | `(manager, WebKitJavascriptResult*)`, then `webkit_javascript_result_get_js_value` | `(manager, JSCValue*)`; registration takes a world name |
  | Snapshot | `cairo_surface_t*` | `GdkTexture*` |
  | Window | `gtk_init_check(argc, argv)`, `gtk_window_new(TOPLEVEL)`, `gtk_container_add`, `gtk_widget_show_all` | `gtk_init_check()`, `gtk_window_new()`, `gtk_window_set_child`, `gtk_window_present` |
  | Main loop | `GMainLoop` (`gtk_main` not needed) | `GMainLoop` (`gtk_main` does not exist) |
  | Web-process sandbox | Off by default; `webkit_web_context_set_sandbox_enabled` | Always on (bubblewrap); as root under Docker's defaults it aborts with exit 133 |
  | Session bus | Not needed | Warns "Unable to acquire session bus" without one, and still works |

  Identical in both: the custom scheme, `WebKitURISchemeResponse` and the request body, `evaluate_javascript` and `call_async_javascript_function`, user scripts, the security manager, `load-changed`, `close` and the automation signals.
- **Runtime packages** (Ubuntu 24.04 names): `libwebkit2gtk-4.1-0` for the GTK3 build, `libwebkitgtk-6.0-4` for the GTK4 build. Their dependencies bring GTK, libsoup 3, JavaScriptCore, GStreamer base and good, `bubblewrap` and `xdg-dbus-proxy`. Each adds about 34 MB of download and 128 MB on disk to a system that already has GTK3 and GTK4. The executable itself needs only glibc.
  - **Minimum version:** the APIs used (request body, `evaluate_javascript`) need WebKitGTK 2.40, and the `JSC_useWasm` option name needs 2.48 (section 3b). So the startup check's minimum is 2.48, provisionally, until M4 sets the supported minimum.
  - **Distributions** (package pages read 2026-11-14): both APIs are packaged in Debian 12 and later, Ubuntu 22.04 and later (jammy-updates has 2.50.4) and Fedora (`webkit2gtk4.1`, `webkitgtk6.0`). Ubuntu 24.04 has a WebDriver package for 4.1 only. Versions per distribution, other than jammy's, were not recorded; M4 sets the supported minimum.
- **Software GPUs:** on llvmpipe, WebKit's accelerated path cost about 300 ms on the cold first frame (30 ms warm), and GTK4's GL renderer more.
  - **Default on a software GPU (user decision, 2026-11-14):** WebKit acceleration off (`WEBKIT_HARDWARE_ACCELERATION_POLICY_NEVER`) plus GTK4's cairo renderer. Measured on WSLg: 213 MB in total against 379 MB, and a faster cold first frame. An app can turn acceleration back on from C#.
  - On a real GPU the defaults stay as they are (not measured).
  - The host sets `GSK_RENDERER` before `gtk_init` and leaves it alone if the user already set it. The startup error dialog always uses cairo; the app uses it only on a software GPU (decision "Guidance review applied", point 3).
  - To define in M1: how the host detects a software GPU (for example the GL renderer string, or no `/dev/dri` render node), and the C# option's name.
- **Memory** (`spikes/linux-memory`; WSLg with llvmpipe; total PSS of the app and its WebKit processes, Wayland / X11, in MB):

  | At ready, small page | Total | App process | Web process |
  |---|---|---|---|
  | 4.1, default | 320 / 297 | 115 / 92 | 183 |
  | 6.0, default (GTK4 GL renderer) | 379 / 359 | 181 / 161 | 175 |
  | 6.0, `GSK_RENDERER=cairo` | 310 / 301 | 106 / 96 | 181 |
  | 4.1, WebKit acceleration off | 220 / 181 | 111 / 72 | 88 |
  | 6.0, acceleration off + cairo | 213 / 201 | 104 / 92 | 87 |
  | Electron 45 (reference, all processes) | 298 / 285 | main 101 / 90 + GPU 94 | 42 |

  - **The software GL path is the largest item:** WebKit's acceleration costs about 90 MB per web process, and GTK4's GL renderer about 75 MB in the app process. Without it, WebKitGTK and GTK take about 140–155 MB on either API.
  - **.NET adds about 2 MB.**
  - **A heavy page** (React plus 10,000 rows) adds about 245 MB of web-process heap on both APIs, against 417 MB in Electron's renderer.
  - **30 s idle** changes nothing.
  - **On a software GPU, the levers are** WebKit acceleration off plus cairo on GTK4. They would also cut start-up time (see above), so they are candidates for the software-GPU default.
  - **A real GPU was not measured.**
- **Verified on a real desktop (WSLg):**
  - Both APIs map, render and theme correctly on Wayland and X11 (40 captured runs, 0 wrong frames).
  - Resize, maximize and restore showed no white or black frames. At maximize, one capture (35 ms or less) showed the new bounds before the content had grown, with the desktop visible behind it.
  - Animation runs at 61–62 fps.
  - Not covered: a real GPU, HiDPI, IME, screen readers, GNOME, KDE, libadwaita.
- **Desktop integrations** (`spikes/linux-integration`, WSLg plus xfce4-panel and openbox in Docker). Everything tested works on both APIs:
  - **Title bar drawn by the page:**
    - WebKitGTK ignores `app-region`, so the injected script asks the host to move the window. The host uses `gtk_window_begin_move_drag` on GTK3 and `gdk_toplevel_begin_move` on GTK4.
    - The move starts after a 4 px threshold, 16–30 ms after mousedown. Starting on press lost the double-click on GTK4.
    - On openbox, 6 of 6 drags moved the window exactly, and the edges resized it. Double-click maximize took 2–16 ms.
    - The window menu is unavailable on both window managers tested.
    - The window keeps its CSD shadow and edges with an empty title-bar widget, which GTK4 needs hidden.
    - Wayland drag is untested.
  - **Tray:** StatusNotifierItem plus dbusmenu, written directly on GDBus with no library.
    - On a real xfce4-panel: icon, tooltip, updates, left click (`Activate`) and the menu all work, and the item re-registers after a panel restart. The panel uses the grouped menu calls (`EventGroup`, `AboutToShowGroup`).
    - With no watcher, the tray reports "unavailable" without failing.
  - **Close to tray:** restore to the first frame in 18–25 ms, with the page's state kept.
  - **Placement:** size and maximized state on every backend. Position only on X11: exact with `XMoveWindow` on GTK4, while GTK3 drifts by the frame size. None on Wayland.
  - **Other services:**
    - file pickers (GTK4 always through the portal; cancel from code in about 710 ms);
    - clipboard (outside text needs WebKit's permission, denied by default);
    - print to PDF with no dialog in 82 ms;
    - the launcher progress signal.
  - **Single instance:** a lock file plus a Unix socket (decided, user, 2026-11-14). The handoff takes 4–10 ms, and a hung first instance gives a clear failure after 2 s. GApplication's D-Bus uniqueness waits forever on a hung first instance.
  - **GTK4-only problems:**
    - web processes outlive the window after a window-manager move or resize (5 of 5 runs, more than 10 s). Cause and fix found by `spikes/linux-memory`: reset the web view's event controllers (`gtk_event_controller_reset`) right after `gdk_toplevel_begin_move`/`begin_resize`, as GTK's own title bar does. The processes then exit in 24–26 ms (5 of 5 moves, 5 of 5 resizes);
    - on WSLg Wayland, a hidden window keeps showing its last frame (unrealize after hiding);
    - on WSLg Wayland, a window started maximized is killed by the compositor (maximize after the first map).

    The 6.0 recommendation stands.
  - **Not tested:** file drop, Wayland input, GNOME and KDE tray hosts, notifications (none present under WSLg).

**Profiles and shutdown** (decided, user, 2026-11-14; decision "Profiles and shutdown"). The topic was: one user data folder with a profile per window or per instance, or separate folders; InPrivate; how an app setting clears its browsing data; fresh profiles for tests; and whether the host ever waits for the browser to exit.
- **Decided, all of these:**
  - exit without waiting, and wait for `BrowserProcessExited`, with a timeout, only before deleting or reusing the folder;
  - single instance enforced before the environment is created, which Linux needs for data integrity;
  - one folder per app, with windows on the default profile and named profiles only for separate data;
  - "clear data" through `ClearBrowsingDataAll` / `webkit_website_data_manager_clear`, never `Profile8.Delete`;
  - InPrivate or ephemeral data sets for in-process tests. The user data folder those tests still need lives under `artifacts/webview2-profiles/` (AGENTS.md), never versioned, so cleanup knows where to look. How parallel tests and test processes share or split those folders without stepping on each other is part of the test-framework topic;
  - SmartScreen (`IsReputationCheckingRequired`) is off while the navigation lock keeps the page on the app origin, and on automatically when the app allows navigation to other origins from C#. An explicit C# override exists both ways. It is a level-2 security default (section 3c; decision "SmartScreen default in WebView2"). Built in M1b step 5 as off: no app can allow navigation to other origins yet, so neither the automatic switch nor an override is reachable, and neither is built.
- **Measured by `spikes/webview-profiles`** (WebView2 157, WebKitGTK 2.52 on 6.0 and 4.1; 330 Windows and 320 Linux shutdown runs):
  - Exiting without waiting lost no data on either OS, even with the host killed. Every browser process exited.
  - The 6.4 s exits are SmartScreen: a close within about 0.5 s of a navigation, with its reputation request still in its TLS connect. With `IsReputationCheckingRequired = false` the exit took 52–67 ms; on Linux, 3–24 ms.
  - `Close` does run `beforeunload`, `unload` and `pagehide` on this Runtime.
  - A relaunch works with no special handling. A second instance with different options fails with `0x8007139F`. On Linux a second instance on the same folders starts silently and loses `localStorage`.
  - Extra windows: same profile +26 MB, another profile +30 MB, InPrivate +27 MB, another folder +149 MB.
  - `ClearBrowsingDataAll` clears in about 70 ms. `Profile8.Delete` poisons the name for good (`ERROR_DELETE_PENDING` at every later start).
  - Test profiles: InPrivate costs 76–79 ms per test; ephemeral data sets on Linux 60–69 ms.
- **What Microsoft's documentation says:** `Close` is synchronous, the browser exits once no WebView uses it, and no wait is set before the host exits. Waiting for `BrowserProcessExited` is needed only before deleting the user data folder: in tests, or for a "clear data" setting implemented as a deletion. Microsoft recommends profiles under one folder over several folders; each profile has its own cookies, preferences and cache. The documentation is silent on where InPrivate data lives, on a host that exits without waiting, and on a quick relaunch while the old browser is still exiting, which is what the spike measured.

**Native services, per host, behind one API:**
- **API quality comes first** (user requirement, 2026-11-14). The C# and TypeScript APIs for native features must be as easy and pleasant to use as Spar can make them. The features, with their state in `docs/research-review.md` section 6:
  - **Adopted (M3):**
    - file access: open and save dialogs with grants, as below;
    - the clipboard. **API decided (2026-11-14):**
      - The page uses the web's `navigator.clipboard`. Writing is allowed; reading needs `Permission.ClipboardRead` declared in C#, which sets the header and the host gate.
      - The backend gets an injectable `IClipboard` (`SetTextAsync`, `GetTextAsync`) returning `Result`s with `Busy` (after a short retry), `TooLarge` and `Unavailable`.
      - Text only at first. Real copy and paste, which needs focus, is checked in Windows Sandbox or a Linux VM.
      - The example is `docs/app-model.md` 3.7;
    - drag and drop, where dropped files become grants and the parent folder gets none. **API decided (2026-11-14):**
      - Off by default. The page declares drop areas with `files.dropTarget(element, { accept, folders, limit }, onDrop)`, which returns a function that removes the area.
      - `onDrop` gets `Result<DroppedItems, DropError>`: file grants, plus read-only folder grants with `folders: true`. The error cases are `notAccepted` and `tooMany`.
      - While a valid drag hovers, the SDK sets `data-spar-drop="over"` on the area.
      - Drag and drop inside the page keeps working.
      - How each host receives the files (WebView2 `postMessageWithAdditionalObjects`, a GTK drop target) is checked in M3. The example is `docs/app-model.md` 3.6;
  - **Measured, API to design:**
    - the tray icon and its menu, and close to tray;
    - the custom title bar (decided, page-drawn buttons);
    - window placement;
    - single instance and argument forwarding. Decided: it is enforced before the WebView environment exists, a relaunch forwards its arguments to the running instance, and an app that wants several instances says so in C# (decision "Profiles and shutdown"). Linux uses a lock file plus a Unix socket. Windows uses a lock file plus a named pipe (decided, user, 2026-11-17; decision "Windows single instance: a lock file plus a pipe"; `spikes/windows-single-instance`):
      - **Lock:** `FileShare.None` on a lock file in the app's data folder, so the lock covers exactly the folder a second instance would corrupt. The OS releases it when the process ends; a killed primary left nothing stale (10 of 10).
      - **Pipe:** named from the lock file's volume serial number and file index, so a junction or a short name reaches the same instance. One instance, reused with `DisconnectNamedPipe` after the client closes its end.
      - **Security:** a DACL with only the token's user SID, `FILE_FLAG_FIRST_PIPE_INSTANCE` and `PIPE_REJECT_REMOTE_CLIENTS`. The secondary checks the user of the server process (`GetNamedPipeServerProcessId`, then its token). Never `PipeOptions.CurrentUserOnly`, which opens an elevated user's pipe to every administrator.
      - **Timing:** the handoff takes 1.2 ms from the secondary's `Main`. A hung primary gives a clear failure after the 2 s acknowledgement timeout. A launch while the primary exits waits up to about 5 s for the lock and becomes the primary.
      - **Focus:** the secondary calls `AllowSetForegroundWindow` with the server's pid, and the primary shows, restores and activates its main window: 9 of 9 on the desktop, 0 of 3 without the call.
      - **The relaunch handler:** `spar.OnRelaunch(…)` in `Program.cs`, run after the window is focused (decided, user, 2026-11-17; decision "The relaunch handler"; `docs/app-model.md` 3.14). A relaunch that cannot reach the running instance shows the start-up error message and exits 8 or 9;
      - **Built in M1b step 2** (decision "Start-up: prerequisites, single instance and theme"): the lock is `instance.lock` in the data root; the client opens the pipe with `CreateFileW` and compares token user SIDs itself (no `NamedPipeClientStream` or `WindowsIdentity`, which added 168 KB to the image); each frame carries the sender's `GetTickCount64`, and the primary drops a relaunch that waited longer than the sender's 2 s, so a resumed primary never acts on a launch already reported as failed; the pipe closes when the teardown starts. Observed: hand-off 0.5–0.6 ms after the second host starts, the second process exits 0 in 30–31 ms, a suspended primary gives exit 8 at 2.0 s;
    - print to PDF (82 ms with no dialog on Linux; WebView2 has `PrintToPdfAsync`), plus printing with the system dialog. **API decided (2026-11-14):**
      - On the page, `print.show()` opens the system dialog and `print.toPdf(options)` saves through the save dialog with a safe write. Both are `Call`s, and `window.print()` does the same as `show()`.
      - In C#, `IPrinting.ToPdfAsync(options, grant, name, ct)` writes without a dialog.
      - The cases are `Dismissed`, `NoPrinter`, `Unavailable` and `Failed`, plus the file cases.
      - The first version prints the current page, styled with `@media print`. The example is `docs/app-model.md` 3.8;
  - **New, from the user (2026-11-14):**
    - a folder picker. **Decided (2026-11-14):** `IFileDialogs.OpenFolderAsync` and `files.openFolder()` return `Result<FolderGrant, FileDialogError>`.
      - The grant declares `Read` (the default) or `ReadWrite`, and offers list, read, safe write, create folder and delete (write access only).
      - Every path is relative to the folder. Absolute paths, `..` and links leaving it give `FileAccessError.OutsideFolder`, checked on the final resolved path. A write through a read grant gives `ReadOnlyGrant`.
      - The page sees names, never full paths. Grants are tied to the window.
      - Remembering a folder across restarts is left out of the first version.
      - The Linux portal path is measured in M3. The example is `docs/app-model.md` 3.6;
    - resource usage.
  - **Later:** notifications (after the installer decision), deep links and file associations.
  - **To discuss with the user** (2026-11-14): menus and accelerators (window, context and tray menus), notifications (balloon or toast, and what an unpackaged app can do), deep links and file associations. They stay listed even where they ship later.
  - **Keyboard shortcuts** (user, 2026-11-14), designed with the menus. The decision comes first, then the points it had to answer:
    - **Decided with context menus (2026-11-14):** actions are declared once with `actions.define({ id: { label, shortcut, run } })`, and menus reference the action objects, so a duplicate id (TS1117) or a misspelled action (TS2551) fails `tsc`. Native context menus come from `menus.show(event, items)`. WebView's default menu is off, and text fields keep a minimal native Cut/Copy/Paste/Select all. Shortcuts work while the window has focus; plain editing keys go to inputs; browser accelerators stay off. Scoped actions may shadow app-wide shortcuts while active. A shortcut is one string or one per OS (`{ windows, linux, default }`; user, 2026-11-14), checked for duplicates per OS. A duplicate id across `define` calls, or the same shortcut on two app-wide actions, throws at once. C# actions share the ids. A native menu bar and system-wide shortcuts are not in the first version. The example is `docs/app-model.md` 6.4.
    - shortcuts inside the app's windows (for example Ctrl+S, Ctrl+K), declared once and shown in menus and tooltips;
    - how they coexist with the page's own key handling and with the browser accelerators turned off by default (plan 3c);
    - platform conventions (Ctrl on Windows and Linux);
    - whether system-wide shortcuts are ever offered. The review marked them "ignore", and on Linux they work only on X11.
  - **Added by the user (2026-11-14), likely needed soon:**
    - **context menus:** turn off or replace WebView's default menu (Back, Reload, Save), and show the app's own native menu from the page;
    - **JavaScript dialogs. Decided (2026-11-14):**
      - `alert`, `confirm` and `prompt` are shown as the OS's message box, owned by the window, through a deferral.
      - New code uses `dialogs.message({ title, text, buttons, default })`, a `Call` typed by its button names whose only error case is `dismissed`. C# gets the same through `IDialogs`.
      - `beforeunload` never prompts; the two-stage close asks instead.
      - The example is `docs/app-model.md` 6.5;
    - **opening things outside the app. Decided (2026-11-14):**
      - `target="_blank"` links to allowed schemes open in the system browser by themselves, and `shell.openExternal(url)` serves links built in code.
      - Granted files and folders get `openWithDefaultApp()` and `reveal()`. C# gets `IShell` (`OpenExternalAsync`, `OpenAsync`, `RevealAsync`).
      - The cases are `notAllowed`, `noHandler` and `failed`.
      - **Opening files is allow-listed** (user, 2026-11-14), not block-listed:
        - nothing opens by default; the app declares its types with `spar.Shell(s => s.AllowOpening(…))`;
        - types the OS reports as dangerous (`AssocIsDangerous` on Windows; executables, `.desktop`, scripts and executable MIME types on Linux) stay refused even when declared, unless `AllowDangerous(ext)` is set, with the risk documented;
        - M3 checks whether Windows flags `.crt`, `.cer`, `.iso` and `.vhdx`. If not, Spar adds them to its own refused list.
      - The example is `docs/app-model.md` 6.6;
    - **taskbar progress and badges. Decided (2026-11-14):**
      - An injectable `ITaskbar` per window, and `taskbar` in the SDK: `SetProgress(0–1 | Indeterminate | Paused | Error)`, `ClearProgress()`, `SetBadge(count | null)`.
      - On Windows a count is a Spar-drawn overlay, or the app's own icon; on Linux the launcher shows it.
      - Where unsupported the calls do nothing and `IsAvailable` is false.
      - The example is `docs/app-model.md` 3.10;
    - **power and session events. Decided (2026-11-14):**
      - An injectable `ISystemEvents.Watch()` yields `Suspending`, `Resumed`, `Locked`, `Unlocked` and `SessionEnding(Deadline)`; the page gets `system.on(event, handler)`.
      - Session end is not cancellable and has a short fixed deadline.
      - `IPower.KeepAwake(reason)` holds the machine awake during long work (`SetThreadExecutionState`, a logind inhibitor).
      - The host listens to `WM_POWERBROADCAST`, session notifications and end-session messages on Windows, and to logind on Linux.
      - Tested in Windows Sandbox or a Linux VM. The example is `docs/app-model.md` 3.11;
    - **more windows. Decided (2026-11-14):**
      - Window kinds are declared in C# with `spar.Window(name, w => w.Route(…).Size(…).Modal().SingleInstance().Commands(…))`, and the generator emits their names as a TypeScript union, so a typo fails `tsc` (TS2345).
      - The page uses `windows.open(kind, params)` and `windows.showModal(kind, params)`, a `Call` with the case `dismissed`; inside a modal, `windows.closeWith(value)`.
      - Modals disable their owner. Single-instance kinds focus the open window.
      - Windows share data through the backend, and the main window closes its secondary ones first.
      - A duplicate kind fails at startup. The example is `docs/app-model.md` 6.7;
    - **zoom and find in page. Decided (2026-11-14):**
      - Zoom is on by default (Ctrl+wheel, Ctrl+plus/minus/0, handled by the host), saved per window with its placement, and limited or turned off from C# (`w.Zoom(min, max)`, `w.Zoom(false)`). The page has `zoom.get/set/onChange`.
      - Find in page is off by default. `w.FindInPage()` turns on Ctrl+F with an SDK find bar on both hosts; `find.start(text)` returns the match count, with `next`, `previous` and `stop`.
      - The engine is WebView2's find API (`ICoreWebView2_28::get_Find`, `ICoreWebView2Environment15::CreateFindOptions`, `ICoreWebView2Find`, `ICoreWebView2FindOptions`), stable since Runtime 139.0.3405.78 by Microsoft's release notes, so Spar's minimum covers it with no fallback. `SuppressDefaultFindDialog` is always set: unsuppressed, the browser's own find bar opened as a popup window (M1b step 9, `spikes/webview2-flags`; counts, next, previous and stop checked on Runtime 157 only, as Microsoft offers no Fixed Version Runtime older than 153). The example is `docs/app-model.md` 6.8;
    - **screen-capture protection. Decided (2026-11-14):**
      - `w.ProtectFromCapture()` per window kind, `IWindows.SetCaptureProtection(bool)` and `capture.protect(bool)` at run time, which report whether it took effect.
      - Windows uses `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`, on 10 2004 and later. Linux cannot block capture, so `isAvailable` is false there.
      - It is documented as reducing casual leaks, not as a security boundary.
      - The example is `docs/app-model.md` 6.7;
    - **network status. Decided (2026-11-14):**
      - The page uses `navigator.onLine` and the `online`/`offline` events, with no SDK API.
      - The backend has an injectable `INetworkStatus` (`Current`, and `Watch()` debounced about 1 s) on .NET's `NetworkChange`, with the metered flag on Windows.
      - The documentation says that available is not reachable.
      - M1 checks the page events in Windows Sandbox or a Linux VM by dropping the virtual network. The example is `docs/app-model.md` 3.12;
    - **logs and crash reports for users. Decided (2026-11-14):**
      - `ILogger` with rotating files in Production (5 × 10 MB by default; level from configuration).
      - Page errors reach the same log with flood limits.
      - **Built on Windows** (M1b step 7; decisions "Logging's public shape" and "Logging (implementation record; choices for review)"; `docs/evidence/m1b-logging.md`): one plain-text file, `app.log` in the logs folder, for Spar's lines and every `ILogger` of the app's services (through Microsoft.Extensions.Logging's factory, which an app may also configure with `AddLogging`; no file with `spar.Logging(l => l.WithoutFile())`), written by its own task from a bounded queue (10,000 entries; the newest dropped and counted when full) through the append-only handle every process of the app shares, the host's lines from its first moment. Levels: Information (Production) or Debug (Development, Test) by default, `spar.Logging(…)` by category prefix, and `logging.json` in the data root read strictly at start (a refused file is one warning), all written as the factory's filter rules, so they hold for every provider, the app's own included. An app's own rules (a configuration's `"Default"`, `AddFilter`) override `spar.Logging`, Spar's `Spar.*` lines included, while `logging.json`'s level and categories apply after everything the app configured, its level as a rule for every category, so the per-user switch always holds (decision "Configuration stays the app's choice"; Spar takes no configuration package, an app may add Microsoft.Extensions.Configuration itself). Lines: UTC time, a five-character level, category, message; further lines indented, so no entry can forge another. Moved aside to `app.1.log` above 5 MB until M3's rotation. Test runs write it in their own logs folder.
      - **Page errors, built on Windows** (M1b step 7): the host-injected script wraps `console.error` (a Development build reads it from the DevTools Protocol's `Runtime.consoleAPICalled` instead, so the devtools' links stay the page's) and listens to `error` and `unhandledrejection` (capturing, never cancelling), groups by kind, text and place, and posts `errors` batches with the CSP listener's machinery: one a second at most, 100 groups and 1,000 errors per document, then it stops and restores `console.error`. The host logs them under `Spar.Page` (`PageErrorLog`): the first of a group as an error with its stack, the rest in a summary at most every 10 s.
      - An unhandled exception writes a crash record, and the next start sees it through `IAppHealth.LastCrash`. Spar never uploads anything.
      - `diagnostics.collect()` and `IAppHealth.CollectAsync` pack a zip into a grant.
      - **Every folder is configurable** (user, 2026-11-14) with `spar.Folders(f => f.Data(…).Logs(…).Crashes(…))`, each with a per-user default, and test runs move them all under their own data root. `spar.ResolveFolders()` gives `Program.cs` the folders the services will get, for what it builds before them (the app's own configuration file), after which `spar.Folders` throws.
      - The example is `docs/app-model.md` 3.13.
  - **Added by the user (2026-11-14), later:**
    - automatic updates and relaunch (M4);
    - installers;
    - localisation, together with the invariant-globalisation default;
    - starting at login;
    - recent documents and jump lists;
    - always on top, fullscreen and kiosk mode;
    - system-wide shortcuts;
    - spellcheck;
    - sounds.
  - The common case takes one short, discoverable call. Defaults are sensible on both OSes, so platform differences never show in app code.
  - Every feature gets a compiled example in `docs/app-model.md` before it is implemented, and is designed with the user one feature at a time.
  - **Declared in the `SparApplication.Create` configuration, all decided:**
    - **decided (2026-11-14):** the tray is declared in `SparApplication.Create`: `spar.Tray(t => t.Icon(…).Tooltip(…).OnActivate(TrayAction.ShowWindow).Menu(m => m.Item("Open", TrayAction.ShowWindow).Item<IMonitor>("pause", "Pause monitoring", monitor => …).Separator().Item("Quit", TrayAction.Quit)))` and `w.CloseToTray()`. App services change it through the injectable `ITray` (tooltip, icon, checked and enabled items, `IsAvailable`); the page reaches it only through the app's contracts. On Linux, a diagnostic warns when the click action is missing from the menu, and `CloseToTray` minimises where no tray exists. The example is `docs/app-model.md` 3.5;
    - **decided (2026-11-14):** window placement is `w.Size(1200, 800).MinSize(800, 600).RememberPlacement()`. Placement is remembered by default for the main window, in the app's data folder, and restored on a visible screen; Wayland restores size and maximized state only. App services change windows through the injectable `IWindows` (`Maximize`, `Restore`, `SetSize`, `Focus`). Test runs start from the declared size, because each has a fresh data root;
    - **decided (2026-11-14):** a custom title bar is one window option, `.TitleBar(TitleBar.Custom)`, plus markup: `data-spar-titlebar` on the drag area, and `data-spar-caption`, empty for the SDK's standard buttons or with `minimize`, `maximize` or `close` on the app's own buttons. The SDK posts the layout, and sets `data-spar-window` to `maximized` or `normal` on `<html>`. The example is `docs/app-model.md` 6.9.
- **File dialogs:** `IFileOpenDialog` and `IFileSaveDialog` on Windows (on the window's STA); GTK or the XDG portal on Linux. The page gets only opaque grants (`fg_…`, in memory, revoked when the window closes).
  - **API (decided, user, 2026-11-14):** `IFileDialogs.OpenAsync` (C#) and `files.open()` (page) return `Result<FileGrant, FileDialogError>`. The cases are `Dismissed`, `AccessDenied`, `Unavailable` and `Busy`; it is `dismissed`, not `cancelled`, so it is not confused with an aborted call.
  - Reads and writes return `Result`s with `FileAccessError`: `NotFound`, `AccessDenied`, `InUse`, `TooLarge`, `GrantExpired`.
  - A command can take a `FileGrant` parameter. Grants are opaque on the wire, tied to the window, limited in number, with an optional expiry.
  - Saves write to a temporary file, then replace the target.
  - The example is `docs/app-model.md` 3.6. A forgotten `Dismissed` fails C# (CS8509) and TypeScript (TS2741).
- **Credentials:**
  - Windows: Credential Manager (target `<AppId>/<id>`, 2,560-byte limit), or DPAPI for larger blobs.
  - Linux: `libsecret`, which needs a session bus and a Secret Service.
  - Page-facing secrets are write-only (`set`, `has`, `delete`).
  - Neither OS isolates apps run by the same user.
- **Resource usage** (user request, 2026-11-14; measured by `spikes/resource-usage`, API decided). A C# service reports how much disk the WebView data uses and how much memory the backend and the web engine use, so an app can show it in its frontend, for example on a settings or "about" page.
  - **Exposure:** the app exposes it through one of its own contracts, so the page sees nothing by default. Spar ships no ready-made contract (decided below).
  - **Live values** (user, 2026-11-14): an app may show them continuously, for example in the main window's status bar.
    - So besides a one-shot read there is a subscription that samples on a timer and pushes the latest values. It keeps only the newest sample, so a slow page never builds a backlog.
    - Proposed:
      - sampling runs only while someone is subscribed;
      - memory and disk get separate intervals, because walking a data folder costs far more than reading process counters (for example about 1–2 s for memory and 30 s or more for disk);
      - a value is pushed only when it changes beyond a small threshold;
      - the interval is a C# option with a floor.
    - **Flexible** (user, 2026-11-14): an app chooses what it samples. For example it may subscribe to memory only and read the folder sizes only on request, such as when a settings page opens. The decided API covers this with one memory read, one memory watch and one disk read on request. Watching disk, or one measure on its own, is not in the first version.
  - **API decided (2026-11-14):** an injectable `IResourceUsage` with `GetMemoryAsync`, `WatchMemory(interval)` (minimum 250 ms) and `GetDiskAsync` (returns `Result<DiskUsage, FileAccessError>`). `MemoryUsage` holds the app total, the engine (with its GPU process), the app process and each engine process by kind; `DiskUsage` holds profile, app data and cache. The page sees it only through a contract the app writes, typically a coalesced subscription for memory and a command for disk; Spar ships no ready-made contract. The example is `docs/app-model.md` 3.9.
    - `spikes/resource-usage` measured the sampling cost behind these numbers (below).
  - **Windows, candidates listed before the spike:**
    - the browser's processes and their kinds from `ICoreWebView2Environment` (`GetProcessInfos`, and the extended form that maps renderers to frames);
    - each process's private memory through `GetProcessMemoryInfo`;
    - the backend's own memory through `Environment.WorkingSet` and GC counters;
    - disk as the size of the user data folder, or of one profile's folder.
  - **Linux, candidates listed before the spike:**
    - WebKit's web and network processes found under the app's process tree, with PSS from `/proc/<pid>/smaps_rollup` (as `spikes/linux-memory` measured);
    - the website data sizes from `webkit_website_data_manager_fetch`;
    - the folder sizes.
  - **Open before the spike, answered below:**
    - private bytes, working set or PSS, and the same meaning on both OSes;
    - cost and refresh rate;
    - whether to report the GPU process and shared memory;
    - folders locked by the running browser.
  - **Measured by `spikes/resource-usage`** (NativeAOT hosts; WebView2 157, WebKitGTK 2.52 on 6.0). It answers most of the open points above:
    - **One measure on both OSes: "private memory".** It is resident memory that only the app's processes hold and that no file backs.
      - Windows: `PROCESS_MEMORY_COUNTERS_EX2.PrivateWorkingSetSize`. It matched Task Manager's "Memory" column in 25 of 25 processes.
      - Linux: `statm` resident − shared, which is within 0.4 MB of `Private_Dirty`.
      - Small page: Windows 88.5 MB (engine 75.0 + host 13.5); Linux 70.7 MB (engine 28.3 + app process 42.4, which holds GTK and WebKit's UI side).
      - The headline is the app total. Engine and app process are its parts, with per-kind detail. The GPU process counts in the engine.
      - PSS and USS are Linux-only detail.
    - **Windows processes:**
      - `ICoreWebView2Environment8.GetProcessInfos` (slot 18) listed browser, renderer, GPU and two utilities. It omits the crashpad handler.
      - It works only on the STA (`UI_E_WRONG_THREAD` elsewhere) and takes 29 µs. So the host re-lists only on `ProcessInfosChanged`.
      - Memory is read with kept handles on a pool thread: 2.5 µs for all processes, 0 bytes allocated.
      - `GetProcessExtendedInfos` (Environment13) maps renderers to frames; feature-detect it.
      - A second instance on the same data folder shares the browser and counts both renderers, one more reason for single instance.
    - **Linux processes:**
      - The app's tree: network process, web process, `xdg-dbus-proxy`, `bwrap`. No GPU process with acceleration off, and no WebKit API for their pids.
      - Found through `/proc/PID/task/*/children` in 107 µs and re-found every few seconds. The sandboxed processes' `/proc` files were readable.
      - `statm` with kept handles: 4.6 µs per sample, 0 bytes. `smaps_rollup`: 0.55 ms.
      - Sampling on the main loop pushed its latency to 1.6 ms, against 0.4 ms off it.
    - **Backend:**
      - Windows: EX2 on the host process (135 ns). Linux: `statm`.
      - Not `Process.PrivateMemorySize64`, which costs 3.2 ms on Windows and reads a ~70 GB virtual size on Linux.
      - `GC.GetGCMemoryInfo` is 0 before the first GC.
    - **Disk:**
      - A folder walk took 0.9–1.1 ms on Windows for 26–149 MB (228–673 files), and 0.2–1.2 ms on Linux. 20,000 files took 3.4–5 ms on Windows and 19 ms on Linux.
      - The walk opens no files, so locked files don't matter. NTFS directory entries lag for files open for writing (1.0 MB short 13 s after the fill).
      - WebKitGTK's `webkit_website_data_manager_fetch` gives sizes for four data types in 1.7 ms, covering 81–84 % of the walk.
      - `navigator.storage.estimate()` is incomplete on WebView2 and missing on WebKitGTK.
    - **Sampling, proposed:**
      - memory every 1 s by default, with a 250 ms floor;
      - disk on request, as in the decided API. The spike's proposal of a disk watch every 30 s, with a 5 s floor, applies only if a disk watch is added later;
      - push only on a change over 1 % or 1 MiB.
      - The sampler allocated 0 bytes per tick.
    - **Decided (user, 2026-11-14):** live samples use a coalesced subscription (section 2c, "Subscriptions"). It keeps the newest value and sends no `gap` events.
    - **Not tested:**
      - a cold file-system cache, and Defender's effect;
      - several windows, a heavy page, crashes;
      - Linux with a GPU;
      - the crashpad handler's memory;
      - posting samples over the real bridge;
      - the oldest supported Runtime.
