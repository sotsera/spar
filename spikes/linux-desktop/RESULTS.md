# Linux desktop spike: results

Recorded 2026-11-13. Question: can a Spar desktop host on Linux be built with WebKitGTK from NativeAOT C#, and tested headlessly from the Windows development machine in Docker? What does it cost, and where does it differ from the WebView2 host?

**Answer: yes, on both WebKitGTK APIs (GTK3 / 4.1 and GTK4 / 6.0).**

- The React spike's real bundle and generated SDK ran unchanged, with one small injected shim.
- Unary calls, streams with credits, cancellation and push all worked, under strict NativeAOT with zero warnings.
- Everything ran headless under Xvfb in Docker.
- Two differences matter for the design:
  - Custom-scheme responses **can** stream, which WebView2 cannot, but WebKit reads them with **no back-pressure**.
  - There is **no in-process trusted-input API** like CDP `Input.*`. Trusted input needs WebKitWebDriver (an out-of-process driver on a TCP port) or X11 XTEST.

## Environment

| Item | Value |
|---|---|
| Host | Windows 11, Docker Desktop 29.9 (Linux containers on WSL2), 32 CPUs |
| Image | `spar-linux-desktop:webkit2.52`, built `FROM spar-linux-loopback:sdk11.0.100` (Ubuntu 24.04, SDK 11.0.100). See `Dockerfile`. |
| WebKitGTK | 2.52.6-0ubuntu0.24.04.1, both `libwebkit2gtk-4.1-0` (GTK 3.24.41) and `libwebkitgtk-6.0-4` (GTK 4.14.5); libsoup 3.4.4 |
| Display | `Xvfb :99 -screen 0 1280x1024x24`, no window manager, no GPU. Mesa llvmpipe software rendering; `libEGL warning: DRI3 error` is printed and harmless. |
| Containers | `spar-linux-desktop-spike`: Docker defaults, root. `spar-linux-desktop-sandbox`: `--security-opt seccomp=unconfined`, app run as uid 1000. Both used `--init --shm-size=1g`. |

The sources were copied from a read-only mount (`/src`) into a named volume (`/work`) with `sync.sh`, so no Windows `bin/` or `obj/` was shared. The repository's `artifacts/` was bind-mounted at `/work/artifacts` for logs, PNGs and WebKit data directories. NuGet used its own volume.

## What was built

| Part | Role |
|---|---|
| `Linux.Desktop/Native.cs` | `[LibraryImport]` declarations for GLib, GObject, GIO (incl. `g_unix_input_stream_new`), libsoup 3, GTK 3/4, cairo, JavaScriptCore and WebKitGTK 4.1/6.0. Variants are chosen at compile time (`SPAR_GTK4`). Callbacks are `[UnmanagedCallersOnly]` function pointers passed to `g_signal_connect_data`. There is no reflection and no delegate marshalling. `GObject.New` wraps `g_object_new_with_properties`, because the variadic constructors cannot be imported safely. WebKit, libsoup and GIO signatures were compared with the installed headers. |
| `Linux.Desktop/GLibContext.cs` | `SynchronizationContext`: a queue drained by one `g_idle_add_full(G_PRIORITY_DEFAULT)` source, so continuations stay on the GTK thread. Exceptions are caught at the unmanaged boundary. |
| `Linux.Desktop/LinuxHost.cs` | The host. One dedicated "Spar UI" thread runs GTK, the view and a `GMainLoop`. A `spar://app/` custom scheme serves the embedded assets (`Cache-Control: no-store`), the fetch bridge (`POST /_spar/call/…`, `/_spar/ready`) and a **streamed** `POST /_spar/stream/…` response, built from a pipe read by a `GUnixInputStream`. The script message handler `spar` plus batched `webkit_web_view_evaluate_javascript` carry the message protocol (`h q s k x` up; `r i e c n u` down), with epochs per document (`load-changed` STARTED). A document-start user script presents the channel as `chrome.webview`, so the existing SDK uses it unchanged. Optional flags: `--sandbox` (4.1), `--automation` (WebDriver), `--devtools`. |
| `Linux.Desktop/SelfTest.cs` | Compiled only with `-p:SparSelfTest=true`. It runs a journey through `webkit_web_view_call_async_javascript_function`, takes a snapshot (`webkit_web_view_get_snapshot` → PNG), and runs a caching check, an external-click probe, the bench driver and memory reporting, then closes the window. |
| `Linux.App` | The React spike's backend, with `NotesService.cs`, `NoteStores.cs` and `LabService.cs` **copied unchanged**. The d1 generator (`../d1-pipeline/Spike.Generator`, referenced as an analyzer) and `Spike.Runtime` are referenced, not copied. The contract fingerprint matched the bundle's (`ad941eadd87873d8`): no contract-mismatch occurred. |
| `frontend-dist/` | **A copy of the React spike's built Debug bundle** (`spikes/react/React.App/obj/Debug/net11.0/win-x64/spar/frontend-dist`: `index.html`, `bench.html`, three chunks), embedded one resource per file. Nothing was built with npm here. |
| Scripts | `publish.sh`, `run.sh`, `measure.sh`, `webdriver.sh`, `xclick.sh`, `sync.sh` (container side), `bench-spec.txt` and `bench-summary.mjs` (log summary). |

```sh
docker build -t spar-linux-desktop:webkit2.52 spikes/linux-desktop
docker run -d --name spar-linux-desktop-sandbox --init --shm-size=1g --security-opt seccomp=unconfined \
  -v <repo>:/src:ro -v spar-linux-desktop-work:/work -v <repo>/artifacts:/work/artifacts \
  -v spar-linux-desktop-nuget:/root/.nuget/packages spar-linux-desktop:webkit2.52 sleep infinity
docker exec -d spar-linux-desktop-sandbox Xvfb :99 -screen 0 1280x1024x24 -nolisten tcp
docker exec spar-linux-desktop-sandbox bash /src/spikes/linux-desktop/sync.sh   # then, in /work/spikes/linux-desktop:
./publish.sh gtk3 && ./publish.sh gtk4
# as uid 1000 with DISPLAY=:99:
./measure.sh gtk3 m-gtk3 5
./run.sh gtk4 b-gtk4-1 --selftest --bench bench-spec.txt
./webdriver.sh gtk3
./xclick.sh gtk4
node bench-summary.mjs b-gtk3 b-gtk3sb b-gtk4   # on Windows, reads ../../artifacts/linux-desktop/
```

## Results (observed)

All runtime numbers come from the NativeAOT executable under Xvfb with software rendering, inside Docker on WSL2. **Compare variants with each other here, not with the Windows numbers**: the machine state and the rendering paths differ. Logs are in `artifacts/linux-desktop/`.

Variants:
- `gtk3`: WebKitGTK 4.1, with its default unsandboxed web process.
- `gtk3sb`: 4.1 with `webkit_web_context_set_sandbox_enabled(TRUE)`.
- `gtk4`: 6.0, which always runs a bubblewrap sandbox.

### NativeAOT publish (inside the container)

- `TreatWarningsAsErrors` (repo `Directory.Build.props`), `IsAotCompatible`, `PublishAot`, `InvariantGlobalization`: **0 warnings, 0 errors, no suppressions**.
- **Executable sizes:**
  - GTK3 build with the self-test compiled in: **6,962,464 bytes**.
  - GTK4 build with the self-test: 6,962,368 bytes.
  - Production GTK3 build (no self-test): **6,895,648 bytes**, so the self-test costs 66,816 bytes.
  - Each build also writes a 10.6 MB `Linux.App.dbg` symbol file beside the executable.
  - The app has DI, `HttpClient` (the `links` contract) and the `lab` contract. It has no zip reader: assets are embedded per file.
- **Publish time with `bin/` and `obj/` removed** (the two spike projects; NuGet warm; build servers running):

  | Variant | Run 1 | Run 2 | Run 3 |
  |---|---:|---:|---:|
  | GTK3 | 7.6 s | 5.2 s | 5.0 s |
  | GTK4 | 4.8 s | 4.3 s | 4.5 s |

  The first publish, with cold NuGet and restore, took 2 min 10 s.
- **Dynamic dependencies:** `readelf -d` shows only `libc.so.6`, `libm.so.6` and `ld-linux-x86-64.so.2` as NEEDED. GTK and WebKit are `dlopen`ed at the first call.
- **C# 15:** `await` inside an `unsafe` member, even inside a lambda, fails with CS8652 again. Await-using code was moved to safe members (as in D8).

### Startup (5 runs each, milliseconds from host start)

| Step | gtk3 | gtk3sb | gtk4 |
|---|---:|---:|---:|
| Process start → host | 9–17 | 7–19 | 7–16 |
| Window shown (GTK init, context, view, window) | 83–114 | 85–111 | 134–162 |
| `load-started` (web process ready) | 640–672 | 164–193 | 231–253 |
| `hello` from the SDK | 660–686 | 177–220 | 244–266 |
| **UI ready** (React calls `/_spar/ready` over custom-scheme `fetch`) | **686–711** | **201–244, one 403** | **271–292** |
| Wall, launch → ready line | 697–720 | 210–419 | 279–302 |
| First contentful paint (page clock) | 33–76 | 35–223 | 34–43 |

- **The unsandboxed 4.1 web process spends about 550 ms** between being spawned (at about 89 ms) and starting the load.
  - It was the same as root and as uid 1000, and with or without a D-Bus session bus.
  - It was the same with `NO_AT_BRIDGE=1`, `WEBKIT_DISABLE_DMABUF_RENDERER=1`, `WEBKIT_DISABLE_COMPOSITING_MODE=1` or `LIBGL_ALWAYS_SOFTWARE=1`.
  - It disappears when the sandbox is on.
  - **Cause not found.** It may be specific to this container, so check it on a real desktop before drawing conclusions.
- **GTK4's window takes about 50 ms longer** (GL renderer initialisation with llvmpipe).

### Journey, snapshot and automation

- **JavaScript journey** (`call_async_javascript_function`: set the input through the native value setter, dispatch `input`, `click()`, poll for the list item): **5.5–12 ms**, 15 runs over the three variants.
  - The note was saved and listed with its date.
  - The click is **untrusted** (`isTrusted: false`), as expected.
- **Snapshot:** `webkit_web_view_get_snapshot` of the visible region took 21–26 ms and saved the PNG.
  - 4.1 returns a `cairo_surface_t` (`cairo_surface_write_to_png`); 6.0 returns a `GdkTexture` (`gdk_texture_save_to_png`).
  - Files: `artifacts/linux-desktop/try1.png` (GTK3) and `try4-gtk4.png` (GTK4).
- **WebKitWebDriver** (`webkit2gtk-driver`, 562 kB):
  - The driver launches the app's binary (capability `webkitgtk:browserOptions`) with `WEBKIT_INSPECTOR_SERVER=127.0.0.1:<port>` in its environment.
  - The app must call `webkit_web_context_set_automation_allowed`, construct its view with `is-controlled-by-automation=TRUE`, and return the view from the session's `create-web-view` signal.
  - It must also handle the view's `close` signal, or `DELETE /session` leaves the app running.
  - **GTK3:** new session 124–174 ms; navigate 22–40 ms. Find, send keys, click, then poll by script took 53–56 ms (2 runs). **The click was trusted** (`isTrusted: true`). The screenshot took 43 ms.
  - **GTK4:** navigate and screenshot worked, but **element send-keys and click answered `unsupported operation`** (1 run).
- **XTEST (`xdotool mousemove x y click 1`)** from outside the process: **trusted on both GTK3 and GTK4** (`--external-click`, 1 run each). This works on X11 only.
- **No public WebKitGTK API was found that synthesizes trusted input in-process.** The available routes are JavaScript (untrusted), WebDriver (a separate process on a TCP port) and the X server (XTEST).

### Bridge (3 bench runs per variant; the React spike's `bench.html` and generated SDK; summary by `bench-summary.mjs`)

**Timer resolution:** `performance.now()` in WebKitGTK has 1 ms granularity here, so page medians read 0 or 1 ms. Unary latency below is therefore the **mean**: host wall time / (n + 10 warm-up) calls, including the in-process evaluation that starts each batch.

| Unary call | gtk3 message | gtk3 fetch | gtk3sb message | gtk3sb fetch | gtk4 message | gtk4 fetch |
|---|---:|---:|---:|---:|---:|---:|
| `notes.count` (n=300) | **0.21** | 0.28 | 0.20 | 0.28 | 0.22 | 0.26 |
| `echo` 1 KB (n=300) | 0.21 | 0.28 | 0.22 | 0.29 | 0.23 | 0.28 |
| `echo` 64 KB (n=100) | 0.84 | **0.72** | 0.88 | 0.71 | 0.87 | 0.77 |
| `echo` 1 MB (n=20) | 8.65 | **6.85** | 8.06 | 6.82 | 8.18 | 7.38 |

Unlike WebView2 (`postMessage` 5–10× faster than `fetch` interception), the two transports are close on WebKitGTK:
- **For small calls the message channel is about 25% faster.**
- **From 64 KB up, custom-scheme `fetch` is 15–20% faster.** The message channel pays for JSON escaping into a script, and evaluation.

**Custom-scheme `fetch` POST works fully:**
- Method, headers (`X-Spar-*`, `Content-Type`) and the body (`webkit_uri_scheme_request_get_http_body`) arrive.
- Status codes and headers go back through `WebKitURISchemeResponse`.
- **The request carries no `Origin` header** (`origin=(none)` in every run), so the WebView2 host's origin check cannot be ported as is.

**Streams of 100,000 rows** (medians of 3; "first" = first row):

| Transport | Batch | gtk3 total ms (rows/s) | gtk3sb total ms | gtk4 total ms (rows/s) | First row ms | Long tasks |
|---|---:|---:|---:|---:|---:|---:|
| Channel (credits, evaluate per flush) | 100 | 132 (758k) | 151 | 127 (787k) | 1–2 | 0 |
| Channel | 1000 | **85 (1.18M)** | 82 | 100 (1.00M) | 2–3 | 0 |
| Custom-scheme `fetch` stream | 100 | **71 (1.41M)** | 96 | 93 (1.08M) | 1–2 | 0 |
| Custom-scheme `fetch` stream | 1000 | 86 (1.16M) | 86 | 91 (1.10M) | 2 | 0 |

- All checksums were correct.
- The maximum frame gap was 17–33 ms. Batch-100 `fetch` showed 23–33 ms.
- The host batches messages: about 930 evaluations carried 1,001 messages at batch 100.

**Streaming a custom-scheme response works: the page sees bytes before the end.**
- The test streamed 2,000 rows with a 20 ms pause every 100 rows (at least 380 ms).
- Over `fetch`, the first row arrived after **0–21 ms** and the total was 378–382 ms. Over the channel, the first row arrived after 0–1 ms.
- WebView2 delivered nothing until the end (first row at 598 ms of 599).
- The response is a pipe: the command writes NDJSON from a pool thread, and WebKit polls the read end (`GUnixInputStream` is pollable, so the GTK thread never blocks on it).

**But there is no back-pressure on custom-scheme responses.**
- The test read 5,000 rows of a 1,000,000-row stream, then stopped for 2 s. Over `fetch`, the backend had **produced all 1,000,000 rows** (995,000 ahead) after about 0.6 s, so WebKit drains the stream regardless of the page's reader.
- An isolated run showed the cost: the **web process grew from 288 MB to 1,115 MB RSS**.
- Over the channel with credits: 5,600 produced (600 ahead), the producer stalled, and the web process gained 2 MB.
- Closing a `fetch` stream took 231–246 ms (draining); closing a channel stream took 1–3 ms.

**Cancellation (both transports):**
- Unary and stream cancels ended `cancelled` in 0–4 ms.
- No work was still running at the outcome, and the counter did not move afterwards.
- Cancels for `fetch` calls travel over the message uplink (`x id`).

**Push at 20,000 events/s for 3 s:**
- 59,940–59,997 events delivered, 0 dropped, all accounted for, 0 long tasks, in all variants.
- Latency cannot be resolved with a 1 ms page clock: p99 was within ±0.6 ms, including clock skew.

### Memory (MB; RSS counts shared pages repeatedly, PSS divides them among sharers)

| After the journey (5 runs) | gtk3 | gtk3sb | gtk4 |
|---|---:|---:|---:|
| App process (UI process: GTK + WebKit UI side + .NET) | RSS 168 / PSS 93–94 | 168 / 93–95 | **261–264 / 180–184** |
| WebKitWebProcess | RSS 288–296 / PSS 215–222 | similar | 294 / 211 |
| WebKitNetworkProcess | 47–48 / 21–22 | similar | 48 / 21 |
| bwrap (2 processes) | — | 3.5 / 0.6 | 3.5 / 0.6 |
| **Total** | **RSS 503–512 / PSS 329–336** | 507–508 / 328–331 | **606–616 / 413–426** |

- After the whole bench, the totals were 792–911 MB PSS.
- The web process (636–669 MB PSS) dominates. The `fetch` back-pressure case is the main reason (see above).
- The UI process is far heavier than WebView2's host process (28 MB working set), because WebKit's UI side and GTK live inside it. GTK4 adds about 87 MB PSS, mostly the GL renderer on llvmpipe.

### Shutdown

- **Normal close** (`gtk_window_close` → `destroy` → `g_main_loop_quit`, then `g_object_unref(context)` while pumping the main context): the loop ended 0.8–4.3 ms after the close.
  - **WebKitWebProcess, WebKitNetworkProcess and the bwrap processes were all gone 20–51 ms after the close** in all 15 measured runs, and none were left after the process exited.
- **The main context must keep running:** in the first version the host stopped iterating after `g_main_loop_run` returned. Both children then **stayed alive** until the app process exited (still alive after the 10 s wait).
- **`kill -9` of the app:** all WebKit and bwrap processes exited 15–33 ms later (9 runs over the three variants).

### Caching

- With `Cache-Control: no-store`, a `webkit_web_view_reload` re-requested `index.html` and the entry chunk (1 → 2), but **not the modulepreloaded `notes-*.js` chunk (1 → 1)**. This was 1 run, and the cause was not investigated.
- WebView2 re-requested everything under `no-store`.

### GTK3 (webkit2gtk-4.1) against GTK4 (webkitgtk-6.0): differences that matter

| Topic | 4.1 / GTK3 | 6.0 / GTK4 |
|---|---|---|
| Web-process sandbox | Off by default; `webkit_web_context_set_sandbox_enabled` | **Always on (bubblewrap)**. Under Docker's defaults as root it aborts: `bwrap: Creating new namespace failed: Operation not permitted` (exit 133). It worked as uid 1000 with `seccomp=unconfined`. |
| Data and cache directories | `WebKitWebsiteDataManager` (`base-data-directory`, `base-cache-directory`; variadic constructor, so `g_object_new_with_properties`) → `webkit_web_context_new_with_website_data_manager` | `webkit_network_session_new(data, cache)`, passed to the view as the construct property `network-session` |
| Script message signal | `(manager, WebKitJavascriptResult*)` → `webkit_javascript_result_get_js_value` | `(manager, JSCValue*)`; registration takes a world name |
| Snapshot | `cairo_surface_t*` | `GdkTexture*` |
| Window | `gtk_init_check(argc, argv)`, `gtk_window_new(TOPLEVEL)`, `gtk_container_add`, `gtk_widget_show_all` | `gtk_init_check()`, `gtk_window_new()`, `gtk_window_set_child`, `gtk_window_present` |
| Main loop | `GMainLoop` (no `gtk_main` needed) | `GMainLoop` (no `gtk_main` exists) |
| WebDriver element input | Works, trusted | `unsupported operation` |
| UI process memory | PSS 93 MB | PSS 180 MB |
| Startup to UI ready | 686–711 ms (unsandboxed), 201–244 ms (sandboxed) | 271–292 ms |
| Session bus | Not required | Warns `Unable to acquire session bus` without one; it still works |

Everything else used is identical in both:
- the custom scheme, `WebKitURISchemeResponse`, the request body;
- `evaluate_javascript`, `call_async_javascript_function`;
- user scripts, the security manager;
- `load-changed`, `close`;
- the automation signals.

One code base with a compile-time switch covered both. The executables differ by 96 bytes.

### What a distribution needs

- **Runtime packages (Ubuntu 24.04 names):**
  - GTK3 build: `libwebkit2gtk-4.1-0` (≥ 2.40 for the request-body and `evaluate_javascript` APIs used). Its Depends bring GTK3, libsoup 3, JavaScriptCore, GStreamer base and good, `bubblewrap` and `xdg-dbus-proxy`.
  - GTK4 build: `libwebkitgtk-6.0-4`, with GTK4 and the same set.
  - The executable itself needs only glibc.
- **Download and disk cost** of adding each runtime to a system that already has GTK3 and GTK4 (this base image):
  - 4.1: 33.8 MB download, 128 MB on disk.
  - 6.0: 33.5 MB download, 127 MB on disk.
  - Whether a stock Ubuntu desktop already has them was not checked.
- **Missing library:**
  - The GTK3 executable in the loopback image (no WebKit) printed its first two marks.
  - It then died with an **unhandled `DllNotFoundException` for `libwebkit2gtk-4.1.so.0` on the UI thread**.
  - The loader probed the app's own folder first (`<app dir>/libwebkit2gtk-4.1.so.0`, `liblib…`, `….so`), then the system.
- **Files outside the data directory:** WebKit's data and cache went where they were told (`artifacts/webview2-profiles/linux-webkit-*`: about 15–17 KB each, with sqlite HSTS, storage salt and `WebKitCache`). But the stack also wrote to `$HOME/.cache`:
  - `gstreamer-1.0/registry.x86_64.bin`
  - `mesa_shader_cache/`
  - `dconf/`
  - `.flatpak/webkit-<pid>-*`, the 6.0 sandbox's info directory, which remained after a crashed run.

### Inspector environment variables (production hardening)

- **`WEBKIT_INSPECTOR_SERVER=127.0.0.1:9334` on a production-style run opened a listening TCP port** in the app (`/proc/net/tcp` showed `0100007F:2476`).
  - The run had no `--devtools` and no `--automation`.
  - Whether a page is inspectable through it without developer extras was not checked.
- **`WEBKIT_INSPECTOR_HTTP_SERVER=127.0.0.1:9333`** made the app **hang after `gtk-init`**, before the view was created (1 run, killed after 70 s). The cause was not investigated.
- These are the Linux counterpart of D8's open `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` question.

## Windows (WebView2) and Linux (WebKitGTK) hosts compared

| Concern | Windows host (`spikes/react/React.Desktop`) | Linux host (this spike) |
|---|---|---|
| Native binding | CsWin32 + hand-declared `[GeneratedComInterface]` COM; WebView2 loader linked statically | Plain C ABI: `[LibraryImport]` + `[UnmanagedCallersOnly]` function pointers; libraries `dlopen`ed at first use. No COM, no loader, no ref-counting interfaces, but GObject ownership (`ref`, `unref`, floating references, transfer annotations). |
| UI thread | Dedicated STA thread, Win32 message loop, `UiContext` posts `WM_APP` | Dedicated thread, `GMainLoop`, `GLibContext` posts through `g_idle_add_full`. GTK on a non-main thread **worked** for GTK3 and GTK4 here, though GTK documents the main thread. |
| Process model | `msedgewebview2`: browser plus about 5 children, a separate executable tree | WebKit's UI side **inside the app process**, plus WebKitWebProcess and WebKitNetworkProcess (plus bwrap) as children of the app |
| Assets | `WebResourceRequested` on `https://app.spar.local/`; no streaming; `no-store` honoured | Custom scheme `spar://app/` (registered secure and CORS-enabled); **streams incrementally**; `no-store` mostly honoured (one chunk not re-requested) |
| Page → host messages | `chrome.webview.postMessage`, with source URI available for an origin check | `webkit.messageHandlers.spar.postMessage`. **No sender frame or origin** is given; the host can only check the top document's URI. |
| Host → page messages | `PostWebMessageAsString` per message | `evaluate_javascript` of `window.__sparDeliver([...])`, batched per main-loop turn (about 930 evaluations for 1,001 messages in a batch-100 stream; one per reply for sequential unary calls) |
| `fetch` to the app's own origin | Works; carries `Origin` | Works with method, headers and body; **no `Origin` header** |
| Unary latency | `postMessage` 0.1–0.2 ms ≪ `fetch` 0.8–1.0 ms | message 0.21 ms ≈ `fetch` 0.28 ms; `fetch` is faster from 64 KB |
| Streams | Message channel only (`fetch` buffers to the end) | Message channel with credits (bounded); `fetch` streams progressively but **without back-pressure** |
| Trusted input for tests | In-process CDP `Input.*` (D8) | **None in-process.** WebKitWebDriver (TCP port, out of process; GTK3 only for input here) or XTEST (X11 only) |
| Devtools and debug environment variables | `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` | `WEBKIT_INSPECTOR_SERVER` / `WEBKIT_INSPECTOR_HTTP_SERVER` |
| Profile and data folder | User-data folder under `artifacts/webview2-profiles/` | Data and cache directories (passed explicitly), same place; but GStreamer, Mesa, dconf and the 6.0 sandbox still write to `$HOME/.cache` |
| Shutdown | Browser exit 236–284 ms, 2 of 14 runs about 6.46 s | Children gone 20–51 ms after close (main context must keep running); 15–33 ms after the app is killed |
| Startup to UI ready | 339–396 ms (+45–54 ms process start) | 201–292 ms (sandboxed 4.1 and 6.0; one 4.1 run 403 ms), 686–711 ms (unsandboxed 4.1 in this container) |
| Memory after the journey | Host 28 MB working set; total 117–125 MB private | Total PSS 329–336 MB (GTK3), 413–426 MB (GTK4); UI process alone 93 / 180 MB PSS |
| Executable | 6,233,600 bytes (streaming-era production build with zip reader) | 6,895,648 bytes production (no zip reader); the GTK3 and GTK4 builds differ by 96 bytes |
| Headless test | Shown, non-activated, offscreen window | Xvfb (no compositor or WM); `requestAnimationFrame` ran (17 ms frame gaps) |

## Consequences for Spar's host abstraction

1. **The message protocol is portable; the channel adapter is per host.**
   - The SDK's `chrome.webview` detection worked on Linux through a 12-line document-start shim.
   - In M1 the SDK should take a small host-provided channel object (`postMessage` + `onmessage`) instead of probing for WebView2's global.
   - Host → page delivery should be batched on Linux (one evaluation per turn), and could be on Windows too.
2. **Transport per host is a measured choice, not a constant.**
   - On Linux, `fetch` and messages are within 25% of each other for unary calls, and `fetch` wins from 64 KB.
   - Keep the abstraction "one protocol, cheapest channel per host", and allow unary-over-`fetch` on Linux.
3. **Streams stay on the credit channel on every desktop host.**
   - Linux can stream a custom-scheme response, but WebKit drains it without back-pressure (+826 MB for a stalled 1 M-row reader).
   - A progressive `fetch` stream is only safe for bounded, small outputs. Do not build on it.
4. **Origin checks must be redesigned for Linux.**
   - Neither script messages nor scheme requests identify the sender.
   - The host must instead:
     - control navigation (`decide-policy`: keep the top frame and subframes on `spar://app`);
     - inject the shim into the top frame only;
     - consider registering the handler in an isolated world.
   - Iframes from other origins posting to the handler were **not tested**. Add that test before M1 relies on this.
5. **The UI-thread abstraction holds:** "post to the UI context, run the backend on the pool, finish native requests on the UI thread" maps one-to-one (STA plus window messages becomes GLib main context plus idle sources).
   - One new rule: **keep iterating the main context during shutdown** until the child processes are gone.
6. **Devtools and test automation need a Linux-specific design.**
   - D8's in-process CDP has no WebKitGTK equivalent.
   - The options are WebKitWebDriver, which reintroduces a local TCP port and an external driver (D8 rejected exactly that for Windows), and XTEST, which is X11-only and needs coordinates.
   - Untrusted JavaScript input (what the Linux self-test used) may be enough for `spar snap`-style checks but not for D8's "trusted by default".
   - **Wayland** (no XTEST) was not tested.
7. **Production hardening:** the Linux host should clear `WEBKIT_INSPECTOR_SERVER` and `WEBKIT_INSPECTOR_HTTP_SERVER`, and decide about `WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS`, before WebKit initialises. This joins the open D8 item.
8. **The sandbox is a platform requirement on 6.0** and a choice on 4.1.
   - Sandboxed 4.1 was faster to start in this environment.
   - Containers and CI need non-root plus a seccomp profile that allows user namespaces, or 6.0 aborts.
9. **Profiles and data directories:**
   - The `--data-dir` contract works.
   - "Never write to the user's home" cannot be fully kept, because GStreamer, Mesa, dconf and the sandbox use `$HOME/.cache`.
   - Tests should set `HOME` or `XDG_CACHE_HOME` to a disposable folder.
10. **Packaging:**
    - Pick one API: 4.1/GTK3 is lighter (UI process PSS 93 against 180 MB) and its WebDriver input works; 6.0/GTK4 is the maintained direction, with an enforced sandbox.
    - The host should check for the library at startup (for example with `NativeLibrary.TryLoad`) and fail with a clear message, not an unhandled `DllNotFoundException`.
    - The loader probes the app folder first, so library placement in the app folder matters.
11. **Testing from Windows works:** Docker plus Xvfb ran build, publish, journeys, snapshots, bench, WebDriver and XTEST. A visible desktop is still needed for real GPU paths, Wayland, HiDPI, focus and IME, and window-manager behaviour.

## Failures and causes

1. **CS8652** (C# 15): `await` in a lambda inside an `unsafe` method. Fixed by moving the code into safe members.
2. **CS8620**: tuple `params` with an `object?` element and literals. Fixed by replacing the tuple with a `GProperty` record struct.
3. **Children not exiting after close:** the host stopped iterating the main context after the loop ended. Fixed by pumping `g_main_context_iteration` while waiting.
4. **WebDriver `DELETE /session` left the app running:** the app ignored the view's `close` signal. Fixed by handling `close` and closing the window.
5. **GTK4 under Docker defaults as root:** bwrap cannot create namespaces, and the app aborts with SIGTRAP (exit 133). It worked as uid 1000 in a container with `seccomp=unconfined`. Running with `WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS=1` was **not** tried; the session's permission check refused it.
6. **`WEBKIT_INSPECTOR_HTTP_SERVER` hang:** not investigated.
7. **Docker Desktop's engine disappeared once mid-bench** (both containers exited with 143; the API came back after about 60 s). The cause is unknown. Xvfb's stale lock had to be removed, and the two affected bench runs were repeated.
8. **Measurement artefact (fixed):** `pgrep -f` counted the shell running the probe. Process counts now use `pgrep -x` on process names.

## Observed, skipped, not tested

**Observed** (all in Docker, Xvfb, software rendering):
- Strict AOT publishes of both variants, 3 clean runs each.
- 5 startup, journey, memory and shutdown runs per variant (15).
- 3 full bench runs per variant (9).
- Snapshots on both variants.
- WebDriver: GTK3 twice, GTK4 once.
- XTEST clicks: once per variant.
- SIGKILL cleanup: 3 per variant.
- One caching run, one missing-library run, two inspector environment-variable runs.
- Back-pressure memory in isolation: 1 run per transport.

**Skipped:**
- Building the frontend inside the container: the built Debug bundle was copied.
- The zip or Brotli asset packing: assets are embedded per file.
- `WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS` (refused).
- The 4.1 sandbox as root.

**Not tested:**
- A visible desktop (for example WSLg), Wayland, GPU or DMA-BUF rendering, HiDPI, IME and keyboard focus, several windows or views.
- Iframes and foreign-origin frames posting to the message handler; a `decide-policy` navigation lock.
- Uploads (page → host streams); `webkit_uri_scheme_request` behaviour under redirects.
- The remote inspector's page exposure; the HTTP inspector hang.
- ARM64; distributions other than Ubuntu 24.04; WebKitGTK versions before 2.52.
- Whether a stock Ubuntu desktop ships `libwebkit2gtk-4.1-0`.
- Running GTK on the process's main thread instead of a dedicated thread.
- A JIT (CoreCLR) run of the Linux host.
- Reload and navigation epochs under load (`load-started` cancelled nothing in these runs).
- The production boundary as a proper test: only a P/Invoke-name scan was done. `call_async_javascript_function` and `get_snapshot` are absent from the production build and present in the self-test build. `bench.html` is in both, because the copied bundle is the Debug variant.

## Downloads and Docker leftovers

**Downloads.** The image build ran `apt-get update` (a 55.4 MB layer of package lists), then:

| apt step | Download | Installed |
|---|---:|---:|
| `libwebkit2gtk-4.1-0` | 33.8 MB | 128 MB |
| `libwebkitgtk-6.0-4` | 33.5 MB | 127 MB |
| `libwebkit2gtk-4.1-dev`, `libwebkitgtk-6.0-dev` | 48.3 MB | 243 MB |
| `webkit2gtk-driver` | 0.56 MB | 2.0 MB |
| `clang`, `zlib1g-dev` | 70.9 MB | 334 MB |
| `xdotool`, `procps`, `psmisc` | 0.24 MB | 0.8 MB |

That is about 187 MB downloaded. NuGet restore into a new volume added 467 MB:
- the `linux-x64` runtime and NativeAOT packs;
- the ILCompiler packages;
- DI and Roslyn for the generator.

The apt logs are in the image under `/opt/spar-apt/`.

**Docker leftovers:**

| Kept | Size | Remove with |
|---|---|---|
| Image `spar-linux-desktop:webkit2.52` | 7.08 GB in total; 1.24 GB unique on top of `spar-linux-loopback:sdk11.0.100`. Layers: 55.4 MB lists, 257 MB runtimes, 624 MB dev + driver + clang + probes | `docker rmi spar-linux-desktop:webkit2.52` |
| Volume `spar-linux-desktop-nuget` | 467 MB | `docker volume rm spar-linux-desktop-nuget` |

Removed: the containers `spar-linux-desktop-spike` and `spar-linux-desktop-sandbox` (including their MSBuild and VBCSCompiler nodes), the throwaway `spar-linux-desktop-probe1` and `spar-linux-desktop-nowebkit` runs (both `--rm`), and the volume `spar-linux-desktop-work`.

**Disposable outputs** (in the repository's `artifacts/`, which is ignored by git):
- `artifacts/linux-desktop/`: logs and PNGs;
- `artifacts/webview2-profiles/linux-webkit-{gtk3,gtk3sb,gtk4,gtk3-webdriver,gtk4-webdriver}/`: WebKit data and cache.
