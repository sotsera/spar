---
name: spar-webkitgtk-hosting
description: Change Spar's Linux WebKitGTK 6.0 (GTK4) host: startup prerequisite checks and the environment set before GTK starts, the GLib main loop, the `spar://app` scheme handler, the script-message bridge, the navigation lock and response headers, rendering defaults, the page-drawn title bar, Linux native services, the heartbeat, data folders and shutdown, and isolated Linux test runs in WSL or containers.
---

# Work on the Linux WebKitGTK host

Use `spar-native-interop` for declarations, GObject ownership and callbacks, and `spar-webview2-hosting` for the Windows side of a cross-host change. Plan section 3 ("Linux") holds the design; `docs/pitfalls.md` ("Linux host") holds the evidence.

## API and threads

- **`webkitgtk-6.0` and GTK4 only.** No GTK3 or 4.1 code path and no compile-time switch (decision "Linux host uses WebKitGTK 6.0"). The 4.1 notes in the plan and pitfalls are evidence only. The APIs used need WebKitGTK 2.40, and `JSC_useWasm` 2.48; M4 sets the supported minimum.
- **Never block the GLib main loop.** GTK and WebKit run on one dedicated thread with a `GMainLoop`; a `SynchronizationContext` posts to it through `g_idle_add_full`. Backend commands, I/O, synchronous waits, a nested `g_main_loop_run`, large payload parsing and long CPU work never run on it. The scheme handler and `decide-policy` answer from data at hand; anything slower finishes off the thread and completes back on it (plan 3, "The UI thread is never blocked"). Resource-usage sampling also runs off the loop.
- **Bindings:** `[LibraryImport]` and `[UnmanagedCallersOnly]` against glib, gio, gtk and webkit. The NativeAOT exe links only glibc and loads the libraries at first use, so a missing one must be caught by the startup check, not by a `DllNotFoundException`.

## Startup

- **Set with libc `setenv` before GTK, WebKit or any other thread starts** (never `Environment.SetEnvironmentVariable`, which native `getenv` and child processes do not see):
  - `JSC_useWasm=false`, so `WebAssembly` is absent in the page, workers and frames; the C# opt-in sets `true` and adds `'wasm-unsafe-eval'` (decision "WebAssembly off on Linux");
  - `GSK_RENDERER=cairo` unless the user already set it (decision "Software GPU default").
- **Software GPU default:** WebKit acceleration off (`WEBKIT_HARDWARE_ACCELERATION_POLICY_NEVER` in the view's settings) plus cairo, with a C# opt-in back to acceleration; a real GPU keeps the defaults. How the host detects a software GPU, and the option's name, are settled in M1.
- **Prerequisite checks, in plan 3's order:** display variables (exit 6 with no display, before anything can start D-Bus), GLib, then GTK 4, `g_set_prgname` (otherwise the window is "Unnamed Window"), `gtk_init_check`, WebKitGTK and its version, the helper folder (found through `/proc/self/maps`), `bwrap` and `xdg-dbus-proxy`, then a trial `bwrap` run, which catches a denied sandbox in 1.5 ms instead of an abort after 4–7 s. Exit codes are fixed for every app and the same on both OSes: 3 missing, 4 too old, 5 broken, 6 no graphical session, 7 OS too old (plan 3, "Missing prerequisites").
- **The message:** `GtkAlertDialog` once GTK 4 is initialised; otherwise kdialog, zenity, yad, a D-Bus notification, notify-send, then xmessage. Name the package to install (`libwebkitgtk-6.0-4` on Ubuntu). Write stderr when present and the journal (native protocol or `/dev/log`). The app's C# hook may replace the dialog, never the exit code.
- **Window order:** create the view with its network session, attach it, call `load_uri`, then present the window at once in the theme colour. Shown first, the page loses focus and the first Tab. A view that is never presented is a hidden page with throttled timers. Maximize only after the first map, and unrealize after hiding (WSLg Wayland pitfalls).
- **Theme (plan 3d):** a GTK CSS provider for the window, `gtk-application-prefer-dark-theme`, and an opaque `webkit_web_view_set_background_color` (transparent shows black). Read the XDG Settings portal's `color-scheme` and its change signal, falling back to GTK settings; never read back a setting the host wrote. Set no theme name.

## Serving, bridge and page security

- **The origin is `spar://app`,** because WebKitGTK cannot intercept `https` (decision "Desktop origin"). It is a secure context, but it stores no cookies, Cache Storage rejects it, and service workers do not run on it. The origin keys stored data, so it never changes without a migration.
- **Scheme responses:** answer every request, serve stored entries in place with `g_bytes_new_static`, and decode Brotli entries at start on a background thread (WebKit never decodes `Content-Encoding`). Never answer with a redirect: WebKit shows the 302 body as the page. Responses stream with no back-pressure, so never hand over an unbounded producer.
- **Headers on every response, worker scripts included:** the generated CSP, `nosniff`, `Cache-Control: no-store` and the rest of plan 3b. Repeat `Content-Type` inside the header set passed to `webkit_uri_scheme_response_set_http_headers`, or WebKit blocks the app's own CSS, scripts and workers. The `connect-src` value for `spar://app` still needs a check.
- **No origin information:** scheme requests carry no `Origin`, and script messages do not say which frame sent them. Keep the navigation lock in `decide-policy` (never configurable), inject the bridge into the top frame only, and consider an isolated script world (plan 3, "Origin policy"). Same-origin iframes stay blocked by the CSP unless the app opts in, because their messages look like the window's.
- **The bridge:** the script message handler (6.0 passes a `JSCValue*` and registers with a world name) from page to host, `evaluate_javascript` or `call_async_javascript_function` from host to page. Injected user scripts obey the page's `eval` ban, unlike on WebView2, so they must not use `eval` or string-built functions.
- **Debug proxy** (decision "Debug serves Vite through the app's own origin"): Development-variant code only. The scheme handler fetches from Vite with `HttpClient` off the loop and answers with a `WebKitURISchemeResponse`, with the same header rules as Windows. The Linux path is an M1 check.

## Production defaults (plan 3c)

- No `enable-developer-extras`, no windows opened by JavaScript, no `file://` access, downloads denied, `javascript-can-access-clipboard` off (it bypasses the clipboard permission check).
- **Permissions:** WebKitGTK ignores `Permissions-Policy`, CORP and COEP, so the `permission-request` handler is the only gate. Deny anything the app did not declare.
- **Environment overrides:** ignore exactly `WEBKIT_INSPECTOR_SERVER`, `WEBKIT_INSPECTOR_HTTP_SERVER` and `WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS` unless the app calls `AllowBrowserEnvironmentOverrides()`. Let every other variable through, in particular the GPU workarounds (`__NV_DISABLE_EXPLICIT_SYNC`, `WEBKIT_DISABLE_DMABUF_RENDERER`, `WEBKIT_DISABLE_COMPOSITING_MODE`).
- **The web-process sandbox is always on.** It needs a non-root user and user namespaces; never work around a denied sandbox by disabling it.

## Lifetime

- **Single instance before the network session exists:** a lock file plus a Unix socket, which forwards the arguments in 4–10 ms and fails clearly after 2 s on a hung first instance. Not GApplication uniqueness. A second instance on the same folders silently loses `localStorage`.
- **Data folders:** pass the app's XDG data and cache folders to `webkit_network_session_new` explicitly; tests use ephemeral sessions. Clear data with `webkit_website_data_manager_clear` (`ALL` is `0xFFF` on 6.0).
- **Shutdown:** no data waits on the browser (320 runs lost nothing; decision "Profiles and shutdown"), but keep the main loop running until the web processes exit (3–24 ms); terminate a hung web process first, or it outlives the app by about 10 s. Destroying the window runs `pagehide`, `visibilitychange` and `unload`, not `beforeunload`, so required cleanup never depends on the page.
- **Recovery:** handle `web-process-terminated` with a visible message and recovery.
- **Heartbeat** (plan 3, "Hung pages"): as on Windows, armed at document creation and skipped for hidden or minimised windows. `is-web-process-responsive` stays true for an idle hang, so rely on the heartbeat. "Reload page" means `webkit_web_view_terminate_web_process`, then one reload.

## Native services

- **Title bar drawn by the page:** WebKitGTK ignores `app-region`, so the injected script asks the host to move or resize. Start `gdk_toplevel_begin_move` after a 4 px threshold (on press, GTK4 loses the double-click), then call `gtk_event_controller_reset` on every controller of the view, or the web processes outlive the window. Keep an empty, hidden title-bar widget so the CSD shadow and edges stay. No window manager tested offered the window menu.
- **Placement:** size and maximized state everywhere; position only on X11, through `XMoveWindow`; none on Wayland.
- **Tray:** StatusNotifierItem plus dbusmenu written on GDBus, implementing the grouped calls (`EventGroup`, `AboutToShowGroup`). With no watcher, report unavailable; `CloseToTray` then minimises.
- **Others:** file and folder dialogs through GTK4, which always uses the portal; print to PDF without a dialog; JavaScript dialogs through `script-dialog`, answered later, never with a nested loop; power and session events and `KeepAwake` through logind; credentials through libsecret's non-variadic functions (needs a session bus and a Secret Service); process memory from the app's `/proc` tree (`statm` resident − shared; WebKit gives no pids). Capture protection is unavailable on Linux (`isAvailable` false). The decided APIs are in plan 3 ("Native services") and `docs/app-model.md`.

## Running and testing

- **Isolate every run** (plan 5, "Linux isolation"): private `HOME`, every `XDG_*` folder, `XDG_RUNTIME_DIR` and a D-Bus session, with `JSC_*` and inspector variables cleared. GTK, its dialogs, printing, GStreamer, Mesa and the sandbox otherwise write to the real home and `~/.cache`. Never touch the user's WSL home.
- **Trusted input is XTEST:** force `GDK_BACKEND=x11`; in CI, Xvfb in a container run as the app's user (non-root uid 1000, `--init`, the bwrap settings, an optional window manager such as openbox). Disable the DMA-BUF renderer for captures under Xvfb. WSLg cannot test window moves with synthetic input, and its windows appear on the user's desktop. Wayland input stays a check that needs the user.
- **WSL and Docker:** the distribution stops between `wsl` calls (the first window takes about 3.5 s longer), Windows variables need `WSLENV`, Git Bash needs `MSYS_NO_PATHCONV=1` for `/mnt` paths, and NativeAOT needs `clang` and `zlib1g-dev`. Count processes with `pgrep -x`, and stop only processes and containers this session started.
- **Verify on the real app.** Run the published NativeAOT host for startup, the affected services and shutdown, and check that no web process is left. Time from the host: the page's `performance.now()` has 1 ms granularity. Record the desktop, backend and GPU each result came from; GNOME, KDE, a real GPU, HiDPI and IME go to the plan's table "Checks that need the user at the PC".
