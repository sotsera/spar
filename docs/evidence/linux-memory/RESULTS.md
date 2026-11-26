# Linux memory and GTK4 shutdown spike: results

> A spike. Its code was deleted with `spikes/` at the start of M2; it is in git history at [`spikes/linux-memory/`](https://github.com/sotsera/spar/tree/24f5db0942833047c81fa692908410596f3b8960/spikes/linux-memory) (commit `24f5db0`), and this record and its `results/` moved here. Paths under `spikes/` below name that history.

Recorded 2026-11-14.

Questions:
1. Where does the memory of a WebKitGTK app go, per process and per mapping kind, for 4.1 (GTK3) and 6.0 (GTK4), with acceleration on and off, and with GTK4's renderers?
2. What are the floors: a GTK window without WebKit, and a web view with an empty page?
3. Why do GTK4's web processes outlive the window after a window-manager move or resize, and what host-side fix works?

An Electron 45 reference with the same pages was added at the user's request.

## Summary

All numbers were measured on WSLg with **Mesa llvmpipe (no hardware GPU)**: a 480×360 window, medians of 3 to 4 clean rounds. Spreads were within ±2 MB unless stated. Totals are PSS over the whole process tree (app, WebKitWebProcess, WebKitNetworkProcess, sandbox helpers), in MB, Wayland / X11.

| At ready (small page) | Total PSS | Total USS | App (UI) process | Web process | Network |
|---|---|---|---|---|---|
| 4.1, default | **320 / 297** | 218 / 195 | 115 / 92 | 183 | 21 |
| 4.1, acceleration off | 220 / 181 | 129 / 90 | 111 / 72 | 88 | 21 |
| 6.0, default (GSK `ngl`) | **379 / 359** | 265 / 246 | 181 / 161 | 175 | 21 |
| 6.0, `GSK_RENDERER=cairo` | 310 / 301 | 209 / 200 | 106 / 96 | 181 | 21 |
| 6.0, acceleration off (`ngl`) | 295 / 276 | 203 / 185 | 186 / 166 | 87 | 21 |
| 6.0, acceleration off + cairo | **213 / 201** | 121 / 111 | 104 / 92 | 87 | 21 |
| Electron 45.2.1, default | 298 / 285 | 184 / 178 | main 101 / 90; GPU 94 / 93; renderer 42 / 41; zygotes 32; network 29 | | |

- **30 s idle changed nothing:** 0 to −3 MB for every WebKitGTK configuration, and −3 to −5 MB for Electron.
- **The heavy page** (the React bundle plus 10,000 table rows, 70,037 elements) added **about 245 MB, all in the web process** (anonymous memory: +232 MB with acceleration, +215 MB without), the same on both APIs. Totals: 4.1 565 / 542, 6.0 624 / 606, 6.0 with acceleration off and cairo 440 / 429. Electron's renderer grew by 417 MB (total 711 / 701).

**Where the memory goes.**
- **The software GL path is the largest single item.** On llvmpipe:
  - WebKit's accelerated compositing costs **about 90 MB per web process**: anonymous memory 78 MB against 6 MB with acceleration off, plus 16–22 MB more of libLLVM and Mesa pages touched.
  - GTK4's GL renderer (`ngl`) costs **about 75 MB in the app process**: anonymous memory 93 MB against 27 MB with cairo.
  - **libLLVM and Mesa stay mapped and partly resident even with everything off** (about 43 MB in the app process and 30 MB in the web process, file-backed and clean), because WebKit initializes an EGL display anyway, and GDK does on X11.
  - In the 6.0 default on Wayland (379 MB), about **230 MB is tied to that GL path** (145 MB anonymous, 88 MB libLLVM and Mesa pages). In the 4.1 default (320 MB) it is about 165 MB.
  - On real hardware the anonymous part (software render targets, textures, JIT-compiled shaders) would move into the GPU driver's buffers or VRAM. libLLVM's pages would mostly not be touched, because Intel's and AMD's current Mesa drivers do not compile through LLVM. **Not measured: no GPU was reachable.**
- **WebKitGTK and GTK themselves, with the GL path off,** take **about 140–155 MB on either API**:
  - app process 92–111 MB PSS (39–42 MB of it libwebkit, JSC, GTK and system libraries, shared and clean);
  - web process 87–88 MB (6 MB anonymous for the small page, 80 MB file-backed: libwebkit 30–35, JSC 8, ICU 2, plus the residual libLLVM and Mesa);
  - network process 21 MB (10 MB USS).
- **The framework's own share is small.** The NativeAOT executable maps 1.5 MB, and the managed heap after a GC was 0.2 MB committed. A GTK-only window from the AOT host used 30 MB on Wayland and 20 MB on X11 (GTK3), against 28 MB and 18 MB for the same window written in C.
- **Shared-memory and GPU-buffer mappings were negligible** (1–3 MB: memfd `WebKitSharedMemory` and `mesa-shared`), as expected with no real GPU.

**What it means for 4.1 against 6.0.**
- **6.0's default costs 59–62 MB more than 4.1's default here, all of it in the app process, and all of it GTK4's GL renderer running on llvmpipe.**
  - With GTK's cairo renderer, 6.0 is equal to or below 4.1 (310 against 320 on Wayland, 301 against 297 on X11).
  - With acceleration off on both, 6.0 plus cairo is 213 against 220 on Wayland, and 201 against 181 on X11.
- So **memory does not separate the APIs once the renderer suits the GPU.** The lever that matters on a software GPU is the same for both: acceleration off in WebKit (about −90 MB per web process), plus cairo for GTK4 (about −75 MB).
- This does not change the 6.0 recommendation. It adds one rule: **on a software renderer, use cairo and WebKit's acceleration off** (whether to default to that is already an open user decision in the plan). It also leaves one gap: what GTK4's GL renderer costs on a real GPU.
- **Electron**, measured the same way, is about the same as 4.1's default at ready (298 / 285). Its GPU process (93 MB, 49 MB of it libLLVM) is its llvmpipe share. With the heavy page it used 87–159 MB more than the WebKitGTK defaults.

**Question 3, GTK4 web processes outliving the window: cause found, fix verified 5/5.**
- **Cause:**
  - For a move or resize, the window manager (openbox) grabs the pointer, so the **button release never reaches the app**.
  - Two of WebKit's GTK4 gestures on the view (its mouse `GtkGestureClick` and its `GtkGestureZoom`) keep their press point open. **A GtkGesture holds a strong reference on its target widget for each open point.**
  - GTK4's window destroy only unparents the view; GTK3 disposes it regardless of references. So the view is never disposed, WebKit never closes its page, and the web and network processes stay alive until the next press and release on the view, or until the app exits.
- **Fix:** right after `gdk_toplevel_begin_move` or `begin_resize`, **reset every event controller on the web view**. GTK's own title bar does the same.
  - Before: 5/5 move and 5/5 resize runs left the children alive after 10 s.
  - After: 5/5 and 5/5 exited in 24–26 ms.
  - A reset at shutdown, or making the view insensitive before destroying the window, also worked 5/5.
- **Not reproduced under WSLg X11**, where weston's window manager does not grab the pointer.

## Method

### Environment

- **WSL:** Windows 11 Pro 10.0.26320, WSL Ubuntu 24.04 with WSLg (weston, RDP backend, XWayland), kernel 6.18. GTK 3.24.41 and 4.14.5, WebKitGTK 2.52.6 (4.1 and 6.0), Mesa 25.2.8 **llvmpipe** (the user is not in the `render` group, so no hardware GPU is reachable, as in `spikes/linux-wsl-desktop`). .NET SDK 11.0.100, Node 24.22.3. 48 GB RAM, 32 logical CPUs, swap unused.
- **Container** (question 3): `spar-linux-memory-q3` from the existing image `spar-linux-desktop:webkit2.52` (same GTK and WebKit versions), uid 1000, `seccomp=unconfined`, Xvfb `:99` at 1280×800 and **openbox 3.6.1**, installed inside the container (18.8 MB of packages after an `apt-get update`). The container has been removed; no image or volume was added.
- **Electron reference:** Electron 45.2.1 (Chromium 156.0.8078.75), pinned two weeks old (the latest was 45.3.2). How it was installed:
  - `npm install --ignore-scripts` into the WSL working folder, with npm's cache and user configuration there too;
  - then the binary, fetched explicitly by running the package's own `node node_modules/electron/install.js`. It downloads the release zip into `$electron_config_cache` (inside the working folder) and extracts it to `node_modules/electron/dist`. Sizes: 289 MB `node_modules`, 119 MB zip cache;
  - Electron also needs NSS and NSPR, which this WSL distribution lacks. Without root, `apt-get download libnss3 libnspr4` fetched the distribution's own packages (1.56 MB), and `dpkg-deb -x` extracted them into the working folder; `start.sh` puts them on `LD_LIBRARY_PATH` for Electron only.

  Electron ran with its defaults (GPU process on, Chromium sandbox on), `--ozone-platform=wayland` or `x11`.

### What was built

| Part | Role |
|---|---|
| `Mem.Linux/` | NativeAOT host, written for this spike. `-p:SparGtk=4` builds GTK4 + WebKitGTK 6.0; the default is GTK3 + 4.1. Modes: `--mode gtk` (a window with a label, no WebKit: the libraries load lazily, so WebKit is never mapped), and the web view with `--page empty|small|heavy`. Options: `--hw`, `--sandbox` (4.1), `--drag move|resize` (the host starts a window-manager move or bottom-right resize after a 4 px drag inside the view, from the button events it records), `--reset-after-begin`, `--fix` (shutdown order). A control FIFO takes `load PAGE`, `close`, `quit`, `gc`, `facts`. It tracks the web view's and window's dispose (weak reference) and finalize (object data) and logs the view's reference count; after the main loop it waits up to 10 s for its child processes, iterating the main context. |
| `web/` | `empty.html` (no script); `small.html` (heading, button, input, paragraph; reports ready after two animation frames); `heavy.html`: the React spike's built bundle (`main-*.js` with React, 221 KB, plus its notes chunk, copied at sync time from `spikes/linux-desktop/frontend-dist`), which mounts into `#root` and tries to reach a backend that is not there, plus `rows.js`, a plain-DOM table of 10,000 rows × 6 columns (70,037 elements) whose data objects stay referenced. The probe also reports `mousedown`, `mouseup` and `click`. |
| `wsl/` | `env.sh` (private `HOME`, XDG folders and `XDG_RUNTIME_DIR`, with a symlink to the session's Wayland socket; private NuGet, npm and Electron caches), `sync.sh`, `publish.sh`, `run.sh` (one run: start, wait for ready, smaps snapshots at the points, quit, check leftovers), `session.sh` (each run in its own `dbus-run-session`; stops the session daemons the run started), `matrix.sh`, `electron-runs.sh`, `mem.py` (per-process smaps classifier), `summarize.py`, `q3.sh`, `floor.c` (a minimal GTK window in C, built for GTK3 and GTK4), `xt.c` (XTEST drag, click and `WM_DELETE_WINDOW` for this spike's own window). |
| `electron/` | The Electron reference: `main.js` serves the same `web/` folder on `spar://app/` through `protocol.handle` (privileged standard scheme), a 480×360 window, the same FIFO commands; `preload.js` maps `webkit.messageHandlers.spar.postMessage` to IPC; `install.sh`, `start.sh`. |
| `docker/` | `q3-verify.sh`, the 5-of-5 matrix for question 3. |
| `win/wsl.sh` | Runs a command in the WSL working copy. |

```sh
# Git Bash on Windows, in spikes/linux-memory
win/wsl.sh sync; win/wsl.sh 'bash wsl/publish.sh gtk3; bash wsl/publish.sh gtk4; bash electron/install.sh'
win/wsl.sh 'bash wsl/matrix.sh floors 2; bash wsl/matrix.sh main 3; bash wsl/electron-runs.sh 3'
win/wsl.sh 'python3 -I wsl/summarize.py ../../artifacts/linux-memory/runs <out>'
# Question 3, in the container (Xvfb :99 and openbox running, XDG_RUNTIME_DIR set):
dbus-run-session -- bash docker/q3-verify.sh 5
```

**NativeAOT publish (in WSL):** 0 warnings and 0 errors under the repository's `Directory.Build.props` (warnings as errors, code style enforced), no suppressions. 1,529,824 bytes (GTK3) and 1,538,336 bytes (GTK4). Restore was offline from the Windows global packages folder into a private `NUGET_PACKAGES`, with `HOME` and `DOTNET_CLI_HOME` private.

### How memory was measured

- Each run started the app with a 480×360 window, waited for the page's ready message (two animation frames after load; for the empty page, the end of the load), then took snapshots:
  - **ready:** 2 s after ready;
  - **idle30:** 30 s after that, with the window untouched;
  - **heavy:** the host loaded `heavy.html` in the same view; 3 s after the page reported its rows.
- Before each snapshot the host ran `GC.Collect()` and logged the managed heap.
- `mem.py` read `/proc/<pid>/smaps` and `smaps_rollup` for the app and all its descendants. **PSS** comes from `smaps_rollup`; **USS** is Private_Clean + Private_Dirty. Each mapping is classed as **anonymous** (no file: heaps, stacks, JS and GC heaps, llvmpipe's software buffers), **file-backed** (code, read-only data, fonts, caches; mostly clean, so the kernel can drop and re-read it under pressure), or **shared memory/GPU** (memfd, `/dev/shm`, SysV, DRM, DMA-BUF). Mappings were also grouped by name and by library family.
- **USS of file-backed mappings is not "unique memory" in the usual sense:** a library that only one process maps (libLLVM in GTK4's UI process, for instance) counts as private clean. It is resident and counted, but reclaimable.
- Runs were strictly sequential, each in its own private D-Bus session. PSS divides each shared page among all processes that map it, so a concurrent process mapping the same libraries would lower the numbers. The GTK4 runs activate `xdg-desktop-portal` and its GTK3 backend in that session (through the sandbox's D-Bus proxy), which shares GLib, cairo and other libraries with the app; it was stopped after each run.
- Rounds were interleaved (each configuration once per round), so the Mesa shader cache and the page cache were warm after round 1.

## Results

Evidence: `results/memory-totals.txt` (every configuration and point, median and range), `results/memory-classes.txt` (anonymous, file-backed and shared per process), `results/memory-families.txt` (by library family), `results/memory-top.txt` (largest mappings, selected configurations), and `results/memory-excluded.txt`. Run logs and smaps snapshots (JSON) are in `artifacts/linux-memory/wsl/runs/`; the full top-mappings file is `artifacts/linux-memory/memory-top-full.txt`.

Clean rounds per configuration: main configurations 3–4, floors 2–3, Electron 3. All are listed with `n=` in `memory-totals.txt`.

### 1. Where the memory goes

**Totals at ready, idle and heavy** (PSS / USS, MB, medians):

| Configuration | Wayland ready | Wayland heavy | X11 ready | X11 heavy |
|---|---|---|---|---|
| 4.1 default | 320 / 218 | 565 / 462 | 297 / 195 | 542 / 440 |
| 4.1 + sandbox | 316 / 214 | 562 / 459 | 294 / 193 | 542 / 441 |
| 4.1 acceleration off | 220 / 129 | 448 / 356 | 181 / 90 | 410 / 317 |
| 6.0 default (`ngl`) | 379 / 265 | 624 / 509 | 359 / 246 | 606 / 493 |
| 6.0 cairo | 310 / 209 | 555 / 452 | 301 / 200 | 546 / 445 |
| 6.0 acceleration off (`ngl`) | 295 / 203 | 523 / 431 | 276 / 185 | 505 / 413 |
| 6.0 acceleration off + cairo | 213 / 121 | 440 / 346 | 201 / 111 | 429 / 338 |
| Electron 45.2.1 | 298 / 184 | 711 / 591 | 285 / 178 | 701 / 585 |

"Acceleration off" is `WEBKIT_DISABLE_DMABUF_RENDERER=1`, which in 2.52 turns acceleration off entirely (see `docs/pitfalls.md`). Idle 30 s was within 0 to −3 MB of ready in every WebKitGTK row (−3 to −5 MB for Electron), so it is not repeated here. The 4.1 sandbox adds 2 MB (bwrap and xdg-dbus-proxy); 6.0 always has it.

**By kind of mapping, Wayland, ready** (PSS MB; USS in brackets where it differs much):

| | App process: anonymous / file / shared | Web process: anonymous / file / shared |
|---|---|---|
| 4.1 default | 34 / 79 (35) / 1 | 79 / 103 (59) / 1 |
| 4.1 acceleration off | 26 / 84 (45) / 1 | 6 / 81 (42) / 0 |
| 6.0 default | **93** / 86 (36) / 1 | 78 / 96 (46) / 1 |
| 6.0 cairo | 27 / 78 (34) / 1 | 78 / 102 (59) / 1 |
| 6.0 acceleration off + cairo | 20 / 83 (43) / 1 | 6 / 80 (41) / 0 |
| Heavy page, 4.1 / 6.0 default | 35 / 79 / 2; 94 / 86 / 1 | **312** / 115 / 1; **311** / 109 / 1 |
| Heavy page, acceleration off | 26 / 84 / 1; 20 / 82 / 1 | 221 / 94 / 0 (both) |

**Largest items** (PSS MB, Wayland, ready; from `memory-families.txt` and `memory-top.txt`):
- **6.0 default, app process (181):**
  - anonymous 93 (three regions of 35, 30 and 20 MB: llvmpipe's buffers and GL state);
  - libLLVM 33 (60 MB resident, shared with the web process), Mesa (libgallium and others) 12;
  - libwebkitgtk 12, GTK 8, libc and system libraries 9, JSC 5, librsvg 5, cairo/pango/harfbuzz 2, the AOT executable 1.5.
  - Mesa's Vulkan drivers are also mapped (`libvulkan_asahi` 2.6 MB, `libvulkan_gfxstream` 0.8 MB).
- **4.1 default, app process (115):** anonymous 34, libLLVM 28, libwebkit2gtk 12, Mesa 10, system 9, GTK 7, JSC 5.
- **Web process, accelerated (175–183):** anonymous 78–79, libLLVM 35–40, libwebkit 37, Mesa 8–9, JSC 8, ICU 2. **With acceleration off (87–88):** libwebkit 35, libLLVM 26, JSC 8, anonymous 6, Mesa 4.
- **Heavy page, web process:** anonymous grows to 311 MB (292 MB in `[anon]`, 26 MB in `[heap]`), libwebkit to 42, JSC to 13–14. The JS heap, the DOM and the render tree all live in anonymous memory, and smaps cannot separate them; together they cost about 230 MB for this page.
- **Network process (21):** libwebkit 9, system 5, JSC 2, anonymous 2; it does not change with the page.

**How much is llvmpipe-specific:**

| Part | 6.0 default | 4.1 default | Measured how |
|---|---|---|---|
| Web process, anonymous | 72 | 73 | against acceleration off |
| App process, anonymous | 73 | 8 | against acceleration off (+ cairo on 6.0) |
| libLLVM + Mesa pages, both processes | 88 | 87 | family totals; 73 MB of it remains with everything off |
| **Tied to the software GL path** | **≈ 230 of 379** | **≈ 165 of 320** | |
| WebKitGTK, GTK, fonts, .NET | ≈ 150 | ≈ 155 | the remainder |

- On a hardware driver, the anonymous rows would become GPU buffers (VRAM, or GEM objects in shared system memory, which do not show as process anonymous memory), plus a smaller amount of driver state.
- The libLLVM pages are touched by llvmpipe's JIT. Intel's iris and AMD's radeonsi (ACO by default in Mesa 25) do not compile shaders through LLVM, so those pages would mostly not become resident. Mesa 25's `libgallium` still links LLVM, so it would remain mapped.
- None of this was measured on hardware.
- **Also seen:** WebKit loads Mesa and LLVM and touches 26–30 MB of libLLVM in each process **even with acceleration off**, so an EGL display is set up regardless. And GDK4 on X11 does it even for a GTK-only window with the cairo renderer: 98 MB against 33 MB on Wayland, with libLLVM at 49 MB.

**GTK3 against GTK4, same rendering:**
- With cairo, the 6.0 app process is 106 / 96 MB against 4.1's 115 / 92 MB, and the web processes are equal (181–183).
- With everything off: 104 / 92 against 111 / 72. GTK4 is lower on Wayland and 20 MB higher on X11, where GDK4's GL setup maps libLLVM.

**Wayland against X11:** the app process is 10–39 MB smaller on X11 in every WebKit configuration (4.1 with acceleration off: 72 against 111; 6.0 cairo: 96 against 106). Web and network processes are identical on both.

**Compared with the earlier measurement** (`spikes/linux-wsl-desktop`: 335–365 / 416–443 MB, UI 94–118 / 181–205, web 212–224): the app-process figures agree (92–115 / 161–181). The web process is smaller here (175–183), most likely because this spike's ready page is smaller than that spike's probe page; it was not rerun with that page.

### 2. Floors

| At ready (PSS / USS, MB) | Wayland | X11 |
|---|---|---|
| GTK3 window, C | 28 / 25 | 18 / 16 (one of two runs: 71 / 70, with libLLVM mapped; not explained) |
| GTK3 window, AOT host (no WebKit loaded) | 30 / 27 | 20 / 18 |
| GTK4 window, C (`ngl`) | 185 / 182 | 168 / 165 |
| GTK4 window, AOT host (`ngl`) | 187 / 184 | 170 / 167 |
| GTK4 window, AOT host, cairo | 33 / 30 | 98 / 96 (GDK4 on X11 sets up GL anyway) |
| 4.1, empty page | 291 / 196 (app 112, web 158, network 21) | 267 / 173 |
| 4.1, empty page, acceleration off | 207 / 123 (app 108, web 78, network 21) | 168 / 84 |
| 6.0, empty page | 351 / 245 (app 178, web 151, network 21) | 331 / 226 |
| 6.0, empty page, acceleration off + cairo | 200 / 115 (app 100, web 77, network 21) | 189 / 105 |
| Electron, empty page | 292 / 182 | 281 / 177 |

- **.NET's share:** the AOT host's GTK-only window costs 2 MB more than the C window on either API. The executable maps 1.5 MB, the managed heap is 0.2 MB.
- **A GTK4 window alone, on llvmpipe with its default renderer,** costs 185 MB, against 28–33 MB with cairo. Of that, 80 MB is anonymous (in the C program, 71 MB of it in the malloc heap), 59 MB libLLVM and 19 MB Mesa.
- **WebKit's own share** over a bare window, with the GL path off:
  - app process +70–80 MB (4.1: 108 against 30 on Wayland; 6.0 with cairo: 100 against 33);
  - web process 77–78 MB;
  - network process 21 MB.
- **The small page over the empty one:** +10 MB in the web process (88 against 78 without acceleration, 183 against 158 with it). The app process does not change.

### 3. GTK4: web processes outliving the window

**Reproduced** under openbox in the container, **5 of 5 for a move and 5 of 5 for a resize**: after an XTEST drag inside the view that made the host call `gdk_toplevel_begin_move` (or `begin_resize`), and a close through `WM_DELETE_WINDOW`, `WebKitWebProcess`, `WebKitNetworkProcess`, `xdg-dbus-proxy` and the four `bwrap` processes were still alive 10 s after the window was gone. The window really moved (400,226 → 520,286) or resized (480×360 → 600×420). Without a drag the children exited in 28 ms.

**Not reproduced under WSLg X11** (4 runs: GTK4 move, resize, no drag; GTK3 move): weston's X window manager does not grab the pointer for `_NET_WM_MOVERESIZE`, so the button release reached GTK, and the children exited in 23–29 ms. The earlier spike's two WSLg page-edge resize runs, which did reproduce it, started the resize from a page script message; that path was not retried here.

**Cause** (evidence in `results/q3-cause.txt`):
1. For a window-manager move, openbox grabs the pointer, so the X server sends the **button release to the window manager, not to the app**. The host logged the press and `begin-move`, never a release.
2. GTK4 gestures keep a **strong reference on the event's target widget for each point they track** (`g_set_object (&data->target, target)` in `_gtk_gesture_update_point`, gtk/gtkgesture.c:501). It is dropped only when the sequence ends or the gesture is reset (gtk/gtkgesture.c:904).
3. The web view's reference count: 4 at rest, 8 during a press, 10 after `begin_move`. At close it was 6 instead of 4, and after the window was disposed and finalized it was 3 instead of 1.
   - Resetting the view's controllers one at a time showed the two extra references released by **WebKit's `GtkGestureZoom`** (6 → 5) and **WebKit's mouse-button `GtkGestureClick`** (5 → 4).
   - Those gestures are set up in WebKitGTK 2.52.6 `WebKitWebViewBase.cpp`, lines 2372–2399.
4. GTK4's `gtk_window_destroy` drops GTK's reference to the window, and the window's dispose unparents its child (gtk/gtkwindow.c:2569); it does not dispose the child. So the view survives. WebKit closes its page in the view's dispose, so the page, its web process and the network session's process all stay alive. They go when a later press and release on the view ends the gestures' sequences (a click after the drag: 5/5 exited in 24–27 ms), or when the app process exits.
5. **GTK3 is not affected** (5/5 move and 5/5 resize exited in 30–34 ms), because `gtk_widget_destroy` runs dispose on the children whatever references remain.
6. GTK's own window-dragging code avoids this. `GtkWindowHandle` calls `gtk_event_controller_reset` on its gestures right after `gdk_toplevel_begin_move` (gtk/gtkwindowhandle.c:455–462), and GtkWindow's resize edges do the same after `begin_resize` (gtk/gtkwindow.c:1559–1566). The host started the move from the web view's events, so the gestures WebKit had attached to the view were left open.

**Fix** (host side, GTK4): **right after `gdk_toplevel_begin_move` or `gdk_toplevel_begin_resize`, reset every event controller on the web view** (`gtk_widget_observe_controllers`, then `gtk_event_controller_reset` on each), from an idle callback. This is what GTK's own title bar does.

| Run (openbox, GTK4 unless noted) | Children after close |
|---|---|
| Move, no fix | **alive after 10 s, 5/5** |
| Resize, no fix | **alive after 10 s, 5/5** |
| Move + reset after `begin_move` | **exited in 25–26 ms, 5/5** |
| Resize + reset after `begin_resize` | **exited in 24–26 ms, 5/5** |
| Move + reset the view's controllers at shutdown | exited in 24–27 ms, 5/5 |
| Resize + `gtk_widget_set_sensitive(view, FALSE)` before destroying the window | exited in 30–33 ms, 5/5 |
| Move, then one click in the page, no fix | exited in 24–27 ms, 5/5 |
| Move + reset after `begin_move`, then one click | exited in 26–34 ms, 3/3; the click reached the page as `mousedown`, `mouseup`, `click` |
| GTK3 move / resize, no fix | exited in 30–34 ms, 5/5 each |

Other shutdown orders, 1 run each, all with the move:
- **Did not help** (alive after 10 s): removing the view from the window first, hiding the window and waiting 200 ms, `webkit_web_view_try_close`.
- **Partial:** `g_object_run_dispose(view)` and `webkit_web_view_terminate_web_process` each ended only the web process; the network process, `xdg-dbus-proxy` and two `bwrap` stayed.
- The earlier spike's "waiting 3 s did not help" fits the cause: nothing happens until a pointer sequence ends.

**Recommendation:** reset the view's controllers right after starting a window-manager move or resize. The shutdown-time reset is a cheap second guard, because any other path that loses a release (a pointer grab by a menu or a drag-and-drop) would leave the same references.

**Page side:** after a window-manager drag, the page saw `mousedown` and no `mouseup` on both APIs, with or without the fix. So the page keeps `buttons=1` state until the next press. The injected script that asks for the move should treat the drag as ended once it has asked.

## Problems met

1. **Another agent's spike ran WebKitGTK apps in the same WSL distribution at the same time** (`~/spar-spikes/bridge-encoding` from 11:21, and a `Profiles.Linux` app). Shared library pages lower both PSS and USS of the measured run, and two runs dropped by up to 66 MB.
   - `mem.py` now lists, at every snapshot, any process outside the measured tree that maps libwebkit, JSC, GTK, libLLVM or libgallium. The run's own portal, started in its private session, is not counted.
   - 35 runs taken after 11:21 without that check were moved to `runs-unverified/` and rerun as further rounds. The checked reruns lost 5 snapshots to foreign processes (`memory-excluded.txt`); those were not used.
   - That agent's processes were left alone.
2. **The host's log redirect (`>`) overwrote the X11 tool's lines,** which `q3.sh` appended to the same file, so the first Q3 logs lack the window positions. Fixed (`>>`), and the moves were rechecked.
3. **GTK4's sandbox in the container needs `XDG_RUNTIME_DIR`:** `xdg-dbus-proxy` failed to bind its socket, and the host aborted (GLib error, core dump). Fixed by giving the container user a private runtime folder.
4. **Electron needed NSS and NSPR** (not installed in WSL): fetched with `apt-get download` and extracted locally, as described above.
5. **Electron did not exit on quit:** a blocking read on the control FIFO in a worker thread kept the main process alive, even after `app.exit`. Two of my Electron processes were left over and stopped by PID. Fixed by opening the FIFO non-blocking and reading it through a `net.Socket`.
6. **Chromium rewrites its children's command lines** into one string, so the process roles were first all "main". Fixed with a regular-expression search.
7. **A `pgrep -f` pattern matched its own shell,** which killed that shell; nothing else was affected. Later cleanups used PIDs from `ps`.
8. **The managed heap read 0** until the host forced a GC before reading it.
9. **Git Bash rewrote container paths** in `docker exec` (`C:/Program Files/Git/work/...`). Fixed with `MSYS_NO_PATHCONV=1`.
10. **The empty page sends no ready message;** the host marks ready at the end of its load instead (both the WebKitGTK host and the Electron reference).

## Observed, skipped, not tested

**Observed:**
- Memory: 251 clean snapshots in 36 configurations (WebKitGTK main matrix, floors, Electron), Wayland and X11, at ready, after 30 s idle and after the heavy page.
- Question 3: 45 runs in the 5-of-5 matrix, plus 10 diagnostic runs, 3 click-after-fix runs and 4 geometry checks under openbox; 4 runs under WSLg X11.
- No process of mine was left after any run, apart from the two Electron leftovers in problem 5, which were stopped.

**Skipped** because of the rules:
- A hardware GPU (it needs `render` group membership, a system change).
- Synthetic input on Wayland, so question 3 on Wayland is untested.
- Installing packages in WSL.

**Not tested:**
- **Memory on a real GPU:** GTK4's `ngl` and WebKit's DMA-BUF path with a hardware driver, and how much of the ≈ 230 MB moves to GPU memory or disappears.
- **Memory pressure:** whether the kernel reclaims the clean file-backed pages (about 80 MB per process here), and WebKit's memory-pressure settings.
- Idle longer than 30 s; several windows or views; navigation churn.
- Separating the JS heap from the DOM and render tree (smaps cannot).
- Electron variants (`--disable-gpu`, a packaged `asar` build).
- Question 3:
  - on Wayland and on a real desktop window manager (Mutter, KWin);
  - the earlier spike's WSLg reproduction path, where a page script message started the resize;
  - double-click to maximize after the fix (the reset ends the press's gesture);
  - other ways to lose a release (a menu's or a drag-and-drop's pointer grab), which the shutdown-time reset would cover.
- The one GTK3/X11 C floor run that mapped libLLVM (71 MB, against 18 MB in the other run): cause not found.

**Proposed pitfalls** (not added to `docs/pitfalls.md`, which this spike was told not to edit):
- GTK4: after the host starts `gdk_toplevel_begin_move` or `begin_resize` from the web view's events, reset the view's event controllers. Otherwise the gestures keep the view alive, and its web processes outlive the window.
- WebKitGTK maps Mesa and touches 26–30 MB of libLLVM per process even with acceleration off, and GDK4 on X11 does it even with the cairo renderer.
- On llvmpipe, GTK4's default GL renderer costs about 75 MB in the app process (185 MB for a bare window, against 33 MB with cairo).
- Measuring memory in a WSL distribution shared with other running apps is invalid without a check for foreign processes that map the same libraries.

## Leftovers and paths outside the repository

- **Docker:** container `spar-linux-memory-q3` removed. Inside it, during its life: openbox (18.8 MB of packages after `apt-get update`), and the GTK 4.14.5 (18 MB) and WebKitGTK 2.52.6 (66 MB) source tarballs, downloaded only to read the code cited in question 3. No image or volume was added; the existing image `spar-linux-desktop:webkit2.52` was left alone.
- **WSL**, all under `~/spar-spikes/linux-memory/` (1,015 MB):
  - `repo/`: the working copy, including `artifacts/` with the WebKit profiles, `node_modules`, builds and run logs;
  - `home/`: the private `HOME` and XDG folders, with the Mesa, GStreamer and WebKit caches;
  - `run/`: the private `XDG_RUNTIME_DIR`;
  - `nuget/`, `npm-cache/`, `electron-cache/`, `electron-libs/`.

  One temporary file, `/tmp/spar-mem-e.txt`, was created and deleted. Nothing was written to the real `~/.config`, `~/.cache`, `~/.local`, `~/.nuget` or `~/.dotnet` (checked by modification time at the end; the two newer entries there predate this spike's folder or come from another agent's activity). Delete the working folder with `rm -rf ~/spar-spikes/linux-memory` when it is no longer needed.
- **Repository:** `spikes/linux-memory/` (sources, `RESULTS.md`, `results/`), and disposable outputs in `artifacts/linux-memory/`: `container/` (Q3 logs), `wsl/` (all run logs and smaps snapshots, 8.3 MB), `memory-top-full.txt`.
- No commit was made.
