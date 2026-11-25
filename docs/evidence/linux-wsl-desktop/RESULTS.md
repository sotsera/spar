# Linux real-desktop spike (WSLg): results

> A spike. Its code was deleted with `spikes/` at the start of M2; it is in git history at [`spikes/linux-wsl-desktop/`](https://github.com/sotsera/spar/tree/24f5db0942833047c81fa692908410596f3b8960/spikes/linux-wsl-desktop) (commit `24f5db0`), and this record and its `results/` moved here. Paths under `spikes/` below name that history.

Recorded 2026-11-14. Question: what does the WebKitGTK host do on a real desktop, which Xvfb in Docker could not show? This covers Wayland and X11, the GPU path, HiDPI, focus and trusted input, the XDG portal, the theme layers, the GTK3 slow start, the native-library check and the choice between WebKitGTK 4.1 and 6.0.

**Short answer.**
- Both APIs work on Wayland and X11 under WSLg: the window maps, the page renders, the sandbox runs, and the theme layers give 0 wrong-colour frames.
- Three host rules come out of it:
  1. **Create the view, attach it and start the load before showing the window** ("load-first"). This removes a 480–560 ms stall on X11 (GTK3, unsandboxed), and it gives the page keyboard focus from the start. With the old order `document.hasFocus()` stayed false and the first Tab went to GTK instead of the page.
  2. **Handle `web-process-terminated`.** One GPU override crashed GTK4's sandboxed web process, which left a blank window with no error.
  3. **Check native libraries, helper executables and `bwrap` at startup.** Verified: a clear message within 7–102 ms, instead of an unhandled `DllNotFoundException` or a GLib abort 2–3.5 s later.
- **GPU: WSLg gave WebKit no hardware GPU here; every path used Mesa llvmpipe.** On llvmpipe, WebKit's accelerated (DMA-BUF) path costs about 300 ms on the first frame with a cold Mesa cache, and 30 ms warm. Turning acceleration off (`hardware-acceleration-policy` never) gave the fastest starts: GTK3 115–137 ms.
- **API recommendation: WebKitGTK 6.0 (GTK4)**, with reasons and the counter-arguments below.

## Environment

| Item | Value |
|---|---|
| Windows | Windows 11 Pro 10.0.26320. Two 1920×1080 monitors at 100 % (96 DPI). Dark app theme. GPUs: Intel UHD 770 and NVIDIA GTX 1650. |
| WSL | Ubuntu 24.04.5, kernel 6.18.58.1-microsoft-standard-WSL2, systemd. WSLg 1.0.79 (weston with the RDP backend and `rdprail-shell`, XWayland, WSLg mesa 23.1.0). `enable_hi_dpi_support = 1`, fractional off, scale 1. |
| Session | `DISPLAY=:0`, `WAYLAND_DISPLAY=wayland-0`, a session bus. `XDG_SESSION_TYPE` and `XDG_CURRENT_DESKTOP` are empty. User 1000 is **not** in `video` or `render`, so `/dev/dri/renderD128` (root:render 0660) is not accessible; `/dev/dxg` is world-writable. |
| Packages | WebKitGTK 2.52.6 for 4.1 and 6.0, with `-dev`. GTK 3.24.41, GTK 4.14.5. Mesa 25.2.8 (llvmpipe, d3d12, zink). bubblewrap 0.9.0, xdg-dbus-proxy 0.1.5. xdg-desktop-portal 1.18.4 with portal-gtk 1.15.1. clang 18. .NET SDK 11.0.100 (`~/.dotnet`). Node 24.22.3. |
| Missing | `strace`, `ltrace`, `gdb`, `perf`, `xdotool`, the x11 utilities, ImageMagick, `webkit2gtk-driver` (WebKitWebDriver), ibus, fcitx, libadwaita, the Yaru GTK theme. No `sudo` was used, so these stayed missing. |
| Unprivileged user namespaces | Allowed (`unshare -U` works, no AppArmor restriction, no seccomp). |

The environment dump is in `artifacts/linux-wsl-desktop/env.txt`, produced by `wsl/env.sh`.

## What was built

| Part | Role |
|---|---|
| `Wsl.Linux/` | NativeAOT host, started from `spikes/theme-startup/Theme.Linux`. `Native.cs`, `NativeTheme.cs` and `GLibContext.cs` are copied; `Host.cs` and `Program.cs` are rewritten. `-p:SparGtk=4` builds GTK4 + 6.0; the default is GTK3 + 4.1. Additions over the theme host: <ul><li>window facts: GDK backend, CSD, size, scale, monitor, refresh, `is-active`, X11 XID;</li><li>map and focus marks;</li><li>`--order show-first\|load-first\|show-on-commit`;</li><li>`--grab-focus`, `--sandbox` (4.1), `--hw ondemand\|always\|never`;</li><li>`--gpu-page` (saves `webkit://gpu`), `--resize-test`;</li><li>the portal read plus a `SettingChanged` subscription;</li><li>`web-process-terminated`;</li><li>`LibraryCheck.cs` (libraries, helper executables, `bwrap`).</li></ul> |
| `web/` | Probe page under the strict CSP. It reports devicePixelRatio, sizes, `hasFocus`, the active element, the colour scheme, paint timing and the first two animation frames. It also logs input events with `isTrusted`, resize and scheme changes, and runs a 5 s `requestAnimationFrame` jitter test (`?raf=5000`) with a CSS animation running. |
| `wsl/` | `sync.sh` copies the sources to `~/spar-wsl-desktop` and writes an offline `nuget.config` that points at the Windows global packages folder. Also: `publish.sh`, `run.sh`, `startup.sh` (the matrix), `summarize.mjs`, `input.sh`, `portal.sh`, `libcheck.sh`, `mem.sh`, `frames-summary.mjs`, `env.sh`. Tracing without strace: `procsample.py` (per-thread `/proc` sampler), `trace-summary.mjs`, `stackpeek.py` (stack words of a blocked thread, mapped to libraries). C tools: `wcap.c` (X11 window capture by title), `xtest.c` (XTEST keys and clicks, with libXtst loaded via `dlopen`), `wlglobals.c` (lists Wayland globals). |
| `win/` | `wincap.ps1` captures the WSLg window from Windows: whole frames classified at about 60 Hz, plus full shots through `PrintWindow`, so no other window is ever captured. Also `capture-run.sh`, `theme-runs.sh`, `sendkeys.ps1` (Tab through Windows `SendInput`, only when the window is in the foreground), and `wsl.sh`. |

```sh
# Git Bash on Windows, in spikes/linux-wsl-desktop
win/wsl.sh sync
win/wsl.sh 'bash wsl/publish.sh gtk3; bash wsl/publish.sh gtk4'
win/wsl.sh 'bash wsl/startup.sh lf 5 --order load-first'                                    # Q1 (also: su, sunodmabuf, lfnodmabuf, hwnever)
win/wsl.sh 'GDK_BACKEND=x11 python3 -I wsl/procsample.py <out> 2 -- <exe> --label t'         # Q2
win/wsl.sh 'GDK_BACKEND=x11 python3 -I wsl/stackpeek.py "Spar UI" "window is-active" 200 -- <exe> --label peek'
bash win/capture-run.sh "-Frames -DurationMs 6500 -Shots 1000" "" gtk3 rs-wayland-gtk3 --order load-first --resize-test
win/wsl.sh 'bash wsl/input.sh gtk3 in-gtk3 --order load-first'                              # XTEST on X11
win/wsl.sh 'bash wsl/portal.sh gtk3'; bash win/theme-runs.sh; win/wsl.sh 'bash wsl/libcheck.sh'
```

**NativeAOT publish (in WSL):**
- 0 warnings and 0 errors under the repository's `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild`, with no suppressions.
- Final sizes: GTK3 2,756,528 bytes, GTK4 2,752,272 bytes. Publish took 2.5–4.5 s warm.
- Restore was offline from the Windows global packages folder: 8.2 s the first time.
- The assembly carries `[assembly: SupportedOSPlatform("linux")]` (CA1416 for `File.GetUnixFileMode`); this is an annotation, not a suppression.
- C# 15 again rejected an async lambda inside an `unsafe` method (CS8652), as in the earlier spikes. The fix was a safe helper method.

## Results (observed)

Every number comes from the AOT executable on the WSLg desktop with visible windows. **Cold** means fresh WebKit data and a fresh `XDG_CACHE_HOME`, so the Mesa shader cache, GStreamer registry and fontconfig user cache start empty; the OS page cache stays warm (dropping it needs root). **Warm** means a reused profile and cache. Times are in ms after `gtk_window_new`. Medians are over 5 runs, given as median (min–max). Logs are in `artifacts/linux-wsl-desktop/wsl/runs/<label>/`.

### 1. Visible window on Wayland and X11

All four combinations mapped a window and rendered the page, in every one of the runs. No process was left after any run, apart from zombies not yet reaped, which `run.sh` now ignores.

| | Wayland (default) | X11 (`GDK_BACKEND=x11`, XWayland) |
|---|---|---|
| GDK display | `GdkWaylandDisplay` | `GdkX11Display` |
| Decorations | **GTK client-side (Adwaita) with a close button only.** The layout comes from the GNOME default `menu:close`; no minimize or maximize buttons. GTK3 adds a 26 px shadow margin (window 1012×809 for 960×720 content). | **Weston's server-side frame** (title, minimize, maximize, close), `csd=0` |
| GTK settings seen | theme Adwaita, font Cantarell 11 (from GSettings) | GTK3: theme name `Yaru` (not installed, so it falls back), font Sans 10. GTK4: `Default`. |
| Monitor | 1920×1080, scale 1, 60.000 Hz | 1920×1080, scale 1, 59.962 Hz |
| `webkit://gpu` | Type Wayland, VBlank Timer, renderer DMABuf with Shared Memory buffers, platform Surfaceless, **llvmpipe** | Type X11, same renderer, **llvmpipe** |

Screenshots: `artifacts/linux-wsl-desktop/win/rs-*/shot-1000.png` (Wayland GTK3 and GTK4, X11 GTK3 and GTK4).

**Window creation to `ui-ready`, host order "load-first", default renderer** (`startup-loadfirst.log`):

| Variant | Wayland cold | Wayland warm | X11 cold | X11 warm |
|---|---:|---:|---:|---:|
| GTK3 (4.1, unsandboxed) | 463 (459–475) | **185** (177–191) | 451 (443–457) | **167** (160–174) |
| GTK3 + sandbox | 485 (475–492) | 192 (187–195) | 456 (449–469) | 175 (168–180) |
| GTK4 (6.0, sandboxed) | 1059 (1044–1071) | 261 (254–271) | 794 (781–809) | 214 (208–216) |

**The same with WebKit acceleration off** (`WEBKIT_DISABLE_DMABUF_RENDERER=1`; `--hw never` gave the same, 3 runs: GTK3 115–137 ms, GTK4 165–215 ms warm):

| Variant | Wayland cold | Wayland warm | X11 cold | X11 warm |
|---|---:|---:|---:|---:|
| GTK3 | 118 (117–122) | **109** (107–110) | 85 (83–85) | **85** (85–87) |
| GTK3 + sandbox | 129 (126–135) | 120 (118–127) | 92 (91–93) | 97 (95–99) |
| GTK4 | 758 (748–762) | 221 (199–243) | 446 (437–452) | 148 (138–154) |
| GTK4 + `GSK_RENDERER=cairo` (3 runs) | 137 (134–138) | 130 (127–144) | 104 (101–108) | 102 (102–104) |

Where the time goes:
- **WebKit's accelerated path on llvmpipe.** The page's second animation frame comes 330–345 ms after navigation when cold, and 57–89 ms warm. With acceleration off it comes at 28–32 ms, cold or warm. First contentful paint is 11–18 ms in every mode, so the first frame is drawn and its presentation is what waits.
- **GTK4's own GL renderer on llvmpipe.** Cold, the window maps after 366–383 ms on Wayland, against 103–108 ms warm. With `GSK_RENDERER=cairo` it maps after 51–60 ms, and GTK4 then starts as fast as GTK3.
- **GTK4's `mapped` notification on X11** came at 405–1112 ms, after `ui-ready`. The X11 capture showed the window viewable after 81–101 ms (warm), so the property notification is not a usable map signal on X11.

The old order, "show-first", gave the same numbers on Wayland (GTK3 warm 195, GTK4 warm 267). On X11 GTK3 unsandboxed it took **674 ms warm and 975 ms cold** (question 2).

### 2. GTK3's slow start

**Reproduced, but only on X11.** With show-first:

| GTK3, show-first, warm | load-started | ui-ready |
|---|---:|---:|
| X11, unsandboxed | 596 (595–598) | **674** (673–677) |
| X11, sandboxed | 122 (115–131) | 214 (191–223) |
| Wayland, unsandboxed | 130 (127–141) | 195 (189–208) |
| Wayland, sandboxed | 140 (137–146) | 204 (199–208) |

With acceleration off it was the same: X11 unsandboxed loaded at 551–555 ms, sandboxed at 63–66 ms. Docker's Xvfb is an X11 server, which explains the earlier 686–711 ms.

**Where the time goes** (`trace/x11-gtk3-*.jsonl`, `trace-journal.txt`, the stack peek):
- The `/proc` sampler showed the web process spawned at 79 ms, its main thread in a futex wait from 86 ms to 581 ms, and the UI process's main thread ("Spar UI") also in a futex wait from 83 ms to 585 ms.
- No CPU was used in that time, so it is a wait, not work.
- `WEBKIT_DEBUG=all` writes WebKit's release log to journald (not stderr). The web process logged `getNetworkProcessConnection: Request connection` at 276.185 s. The UI process handled it (`NetworkProcessProxy::getNetworkProcessConnection`) at 276.680 s, **494 ms later**, because its main thread was blocked.
- The blocked UI thread was in `futex(FUTEX_WAIT_BITSET_PRIVATE, timeout set)`. Its stack held, in order: libc, then libjavascriptcoregtk (WTF condition wait), then libwebkit2gtk, then cairo, then libwebkit2gtk, then libgtk-3, then libgdk-3 dispatching an X11 event, then the GLib main loop. That is a WebKitWebView draw handler, on X11, waiting with a timeout for the first frame of a web process that cannot render until the same blocked thread brokers its network-process connection. The wait ends when the timeout (about 0.5 s) expires.
- Things that did not help (2 runs each, load-started 579–623 ms):
  - `NO_AT_BRIDGE=1`, `GTK_A11Y=none`;
  - `WEBKIT_DISABLE_COMPOSITING_MODE=1`, `LIBGL_ALWAYS_SOFTWARE=1`;
  - a broken session bus address, an empty `WAYLAND_DISPLAY`, `WEBKIT_GST_DISABLE=1`.
- **What fixed it:** issuing `webkit_web_view_load_uri` before showing the window. X11 GTK3 then took 171–199 ms (3 runs; 167 ms median over 5 in the matrix). Showing at `load-committed` gave 197–204 ms. The sandbox avoids the stall too, probably through different launch timing; the reason was not found.

**The sandbox in WSL works for both APIs.** 4.1 with `--sandbox`, and 6.0 always, spawned `bwrap` → `xdg-dbus-proxy` and `bwrap` → `WebKitWebProcess` (`trace/sandbox-*.jsonl`) and rendered normally. Its cost with load-first: +7–8 ms warm and +5–22 ms cold with acceleration, +11–12 ms warm without.

### 3. GPU and rendering

- **No hardware GPU by default.** Every `webkit://gpu` page reported `GL_RENDERER llvmpipe (LLVM 20.1.2, 256 bits)` with the renderer "DMABuf (Supported buffers: Shared Memory)", in the web process and, on GTK4, in the UI process.
  - Mesa printed `failed to get driver name for fd -1`, `ZINK: failed to choose pdev`, and on X11 `DRI3 error: Could not get DRI3 device`.
  - WSLg's XWayland logged "glamor: GBM Wayland interfaces not available … falling back to sw".
  - The likely cause is that the user cannot open `/dev/dri/renderD128`. Not changed: it would need a group membership, which is a system change.
- **Forcing `GALLIUM_DRIVER=d3d12`** (`gpu-*` runs):
  - GTK3 (unsandboxed): the web process rendered on **D3D12 (Intel UHD Graphics 770)** at 62.1 fps. But the **process segfaulted at exit** in 4 of 4 runs (exit 139, after the timeline was written; the core dump took 7–15 s).
  - GTK4: the sandboxed web process could not create its EGL display (`Could not create surfaceless EGL display: EGL_NOT_INITIALIZED. Aborting...`) and crashed. The window stayed **blank**, `web-process-terminated` (crashed) fired after 8.8 s, and `ui-ready` never came.
  - `MESA_LOADER_DRIVER_OVERRIDE=d3d12` alone changed nothing (llvmpipe).
- **`WEBKIT_DISABLE_DMABUF_RENDERER=1` in 2.52 turns acceleration off entirely.** `webkit://gpu` then shows the policy `never` and no GL information, the same as `WEBKIT_DISABLE_COMPOSITING_MODE=1` and `--hw never`. On llvmpipe it is the faster mode (tables above).
- **Animation-frame jitter**, 5 s with a CSS animation (`raf-summary.txt`):

  | Mode | Wayland GTK3 / GTK4 | X11 GTK3 / GTK4 |
  |---|---|---|
  | Default (DMA-BUF, llvmpipe) | 62.2 / 62.1 fps; p99 17 ms; 0 gaps over 20 ms | 61.1 / 62.1 fps; p99 18 / 17; 0 |
  | DMA-BUF off | 62.2 / 62.2; p99 17; 0 | **97.1 fps, mean 10.3 ms, sd 5.2** / 62.2; 0 |
  | Compositing off | 62.2 / 62.2; 0 | **97.3 fps, sd 5.2** / 62.2; 0 |
  | d3d12 (GTK3 Wayland) | 62.1; p99 17; 0 | — |

  - The vblank is a timer in every case (`VBlank type Timer`; "no drm node with CRTC found" in the log).
  - GTK3 on X11 without acceleration fires animation frames faster than the display, and unevenly. The page's 1 ms clock limits the resolution of these numbers.
  - This measures callback cadence, not on-screen presentation.
- **Resize, maximize and restore** with the Windows capture at 16–35 ms per frame (`win/rs-*`):
  - No white or black frames on any combination.
  - At maximize, one capture (≤35 ms) showed the new bounds before the content had grown, with the desktop behind visible. Those PNGs were deleted because they showed other windows.
  - The page reported each resize and its next frame.
- **Before the first page frame:**
  - L0 (no theme layers) on WSLg shows GTK's window colour, then the page. **No white WebView frame**, unlike Xvfb (14–25 ms white).
  - On X11, `XGetImage` of the new window returns **black** until GTK's first paint: GTK3 for 1–2 frames (~5 ms), GTK4 for 20–21 ms warm and 63–69 ms cold. The Windows capture showed one black frame on screen in 3 of the 5 GTK3 X11 warm theme runs, and none in the GTK4 X11 runs.

### 4. HiDPI

Both monitors run at 100 %, so this machine cannot show real HiDPI. Changing the Windows scale would be a system change.

| Setting | Wayland GTK3 | Wayland GTK4 | X11 GTK3 | X11 GTK4 |
|---|---|---|---|---|
| None | dpr 1 | dpr 1 | dpr 1 | dpr 1 |
| `GDK_SCALE=2` | dpr 1 (ignored) | dpr 1 (ignored) | **dpr 2**, `screen` 960×540, window 1280×960 physical | **dpr 2**, GTK surface scale 2.00 |
| `GDK_DPI_SCALE=1.5` | **dpr 1.5** (viewport 426×320 CSS px) | dpr 1 | **dpr 1.5** | dpr 1 |

- With `GDK_SCALE=2` on X11, text and controls render at 2× and are crisp (`win/dpi-x11-gtk4-gdkscale2/shot-1200.png`). Weston's title bar is not scaled, and the doubled window ran off the top of the screen (y = −32).
- WebKitGTK 4.1 turns GDK's font DPI scale into a page zoom (dpr 1.5); 6.0 does not.
- At dpr 1 the text is crisp with greyscale antialiasing on all four combinations (`win/rs-*/shot-1000.png`).
- How GDK scale combines with WSLg's own HiDPI scaling (`enable_hi_dpi_support=1`) was not testable at 100 %.

### 5. Focus and input

- **The window gets focus on start.** WSLg brings it to the foreground (Windows `GetForegroundWindow` was the WSLg window in every capture), and GTK reports `is-active=1` 3–12 ms after the map.
- **`document.hasFocus()` depends on the host's order:**
  - **show-first:** false at ready, +250 ms and +1000 ms on every combination. The first Tab only moves GTK's focus to the web view: on GTK3 X11 both Tabs were swallowed, and on the other combinations the first one was.
  - **load-first**, or show-first plus `gtk_widget_grab_focus(webView)`: true from ready, and the first Tab focused the first input.
- **Trusted input:**
  - **XTEST under XWayland (X11 backend) works on GTK3 and GTK4** (XTEST 2.2): Tab, Tab, then a click on the button's centre (from the page's report) gave `keydown`, `focusin` and `click` with `isTrusted: true` (`wsl/input.sh`, 6 runs).
  - **Wayland:** weston (WSLg) offers no virtual-keyboard, virtual-pointer or EI protocol (`wayland-globals.txt`).
    - It lists `zwp_input_method_v1`, `zwp_text_input_manager_v1` and `weston_screenshooter`. In weston these are normally reserved for the clients weston itself launches; binding them was not tried.
    - **Specific to WSLg:** Windows `SendInput` reaches the Wayland client through RDP as trusted input. Two Tabs gave trusted `keydown`/`focusin` on GTK3 and GTK4 (`win/sendkeys.ps1`, which refuses unless the window is in the foreground). This is not a Linux mechanism.
  - **WebKitWebDriver is not installed.** Ubuntu 24.04 packages it only for 4.1 (`webkit2gtk-driver` 2.52.6, 562 KB); there is no 6.0 driver package in 24.04. Not installed, so not tested here. The Docker spike found it trusted on 4.1, with no element input on 6.0.
- **IME:** no ibus or fcitx, `GTK_IM_MODULE` unset. GTK3's modules include `im-wayland` and `im-xim`; GTK4 has no module folder. Weston offers text-input **v1**, while GTK's Wayland IM uses v3. Untested.

### 6. Theme on a real desktop

- **XDG portal:**
  - In the user's session, `xdg-desktop-portal` 1.18.4 and `-gtk` are D-Bus-activated (by GTK at startup), but **no `org.freedesktop.portal.Settings` interface is exported**. `ReadOne` failed with `UnknownMethod` in 0.4–91 ms. The cause: `XDG_CURRENT_DESKTOP` is empty, `gtk.portal` declares `UseIn=gnome`, and the fallback mode does not apply to Settings.
  - **So on WSLg the host falls back to GTK settings.** Also, GTK's own settings differ by backend (GSettings on Wayland, defaults on X11).
  - **In a private D-Bus session** (`dbus-run-session`, private `XDG_CONFIG_HOME`, `XDG_CURRENT_DESKTOP=GNOME`):
    - Still no Settings interface without a `portals.conf`.
    - With a private `portals.conf` (`[preferred] default=gtk`), `ReadOne color-scheme` answered in 0.9–1.0 ms, and `SettingChanged` arrived for every `gsettings set org.gnome.desktop.interface color-scheme`.
    - The host applied the theme within 7–11 ms of the signal, and the page saw the `prefers-color-scheme` change 23–28 ms after the signal (GTK3 and GTK4, 2 changes each).
    - The user's real setting stayed `'default'`, and no `~/.config/dconf/user` was created.
  - **Host bug found:** the portal value 0 ("no preference") made the host fall back to GTK's `gtk-application-prefer-dark-theme`, which the host itself had set to dark. So a change to "light" was missed. Fixed: once the portal answers, 0 means light.
- **Theme layers**, captured from Windows (Wayland and X11) and from X11 `XGetImage` (merged with the host's marks); 1 fresh and 1 warm run each, 40 runs (`theme-frames.txt`):

  | Layers (theme-startup names) | GTK3 Wayland | GTK4 Wayland | GTK3 X11 | GTK4 X11 |
  |---|---|---|---|---|
| dark L0 (none) | window colour `#f6f5f4` for about 120 ms (warm) to 320 ms (fresh), then **page in light** | `#f6f5f4` about 40 ms (warm) to 330 ms (fresh), page light | `#f6f5f4` 101–148 ms, page light | `#f6f5f4` 102 ms (warm) to 373 ms (fresh), page light |
  | dark L3 (CSS + prefer-dark + WebView colour) | **0 wrong** | **0 wrong** | 0 wrong after the first paint | 0 wrong after the first paint |
  | dark L4 (+ themed page) | 0 | 0 | 0 | 0 |
  | light L4 | 0 | 0 | 0 | 0 |
  | dark L4, transparent WebView | 0 | 0 | 0 (no black) | 0 |

  - On X11, the frames before GTK's first paint read black (question 3).
  - The first Windows capture of a new window sometimes (≤1 frame, ≤8 ms after the window appeared) showed what was behind it; those PNGs were deleted.
  - The transparent background showed no black here, because WSLg composites. The slow-HTML case, where GTK3 on Xvfb showed black for 1 s, was not rerun.
- **Capture methods that worked:**
  - **The Windows-side capture of the RAIL window**, which works for Wayland and X11 alike. `PrintWindow` gives clean full shots; `CopyFromScreen` is fast enough for classification at 16–35 ms per frame, but it also sees whatever is behind the window.
  - **X11 `XGetImage` of the app's own window** (`wsl/wcap`), at 0.5–1 ms per frame, on the host's clock.
- **Capture methods that did not:** an X11 root grab is useless, because rootless XWayland has no desktop on the root. A Wayland client capture is not available (no `grim`; `weston_screenshooter` is restricted).

### 7. API choice: 4.1 (GTK3) or 6.0 (GTK4)

| Evidence | 4.1 / GTK3 | 6.0 / GTK4 | Source |
|---|---|---|---|
| Startup, load-first, warm, accelerated (llvmpipe) | 167–185 ms | 214–261 ms | this run |
| Startup, cold, accelerated | 451–463 ms | 794–1059 ms (GTK's GL renderer compiling shaders) | this run |
| Startup, acceleration off (+ cairo GSK on GTK4) | 85–137 ms | 102–137 ms | this run |
| Memory after ready (PSS, all processes) | 335–365 MB (UI process 94–118) | 416–443 MB (UI process 181–205) | `mem-summary.txt` |
| Web-process sandbox | Opt-in. Works in WSL; +5–22 ms | Always on. Works in WSL. Docker needs non-root and seccomp off. Blocks `/dev/dxg`, so a d3d12 override crashes the web process | this run, linux-desktop |
| X11 stall with show-first | 480–560 ms unsandboxed | none | this run |
| `document.hasFocus()` with show-first | false | false | this run |
| Decorations | Wayland CSD (close only); X11 SSD. Yaru keeps the GTK3 header bar light | Wayland CSD; X11 SSD. The header bar follows the dark preference | this run, theme-startup |
| `GDK_DPI_SCALE` | Becomes a page zoom (dpr 1.5) | Ignored | this run |
| WebDriver | `webkit2gtk-driver` in Ubuntu 24.04; element input trusted | No package in Ubuntu 24.04; element input unsupported | this run (packages), linux-desktop |
| XTEST trusted input (X11) | Yes | Yes | this run |
| Trusted input on Wayland | None (WSLg: Windows input) | None (WSLg: Windows input) | this run |
| Distributions shipping the runtime | Debian 12+ and backports, Ubuntu 22.04+, Fedora (`webkit2gtk4.1`) | Debian 12+, Ubuntu 22.04+ (jammy-updates: 2.50.4), Fedora (`webkitgtk6.0`) | packages.debian.org, packages.ubuntu.com, packages.fedoraproject.org, read 2026-11-14 |
| Code difference | One compile-time switch; about 20 `#if` blocks in the host | | this run |

### 8. Native library check at startup

Each library was hidden in a private mount namespace (`bwrap --dev-bind / / --ro-bind /dev/null <file>`), or a helper folder with `--tmpfs`. Nothing on the system changed. Output is in `libcheck-summary.txt`.

| Hidden | Before the check (`--no-lib-check`) | With the check |
|---|---|---|
| `libwebkit2gtk-4.1.so.0` | **Unhandled `DllNotFoundException`** on the UI thread after the window existed (exit 134, 253–813 ms), with four "cannot open" lines for probes in the app folder | Exit 3 after 12 ms: "This application needs libraries that could not be loaded: WebKitGTK 4.1 (libwebkit2gtk-4.1.so.0): … cannot open shared object file … sudo apt install libwebkit2gtk-4.1-0" |
| `libjavascriptcoregtk-4.1.so.0`, `libsoup-3.0.so.0` | — | Exit 3 after 9–11 ms. Both the missing library and WebKit (as its dependant) are named, with both packages |
| `libwebkitgtk-6.0.so.4`, `libgtk-4.so.1` | — | Exit 3 after 7–11 ms, same form |
| Helper folder `webkit2gtk-4.1/` or `webkitgtk-6.0/` | GLib `ERROR **: Unable to spawn … WebKitNetworkProcess`, **SIGTRAP (exit 133) after 1.9–3.5 s** | Exit 3 after 16–17 ms, naming the two missing executables |
| `/usr/bin/bwrap` (GTK4) | `Failed to start dbus proxy … bwrap (Permission denied)`, SIGTRAP after 3.0–4.3 s | Exit 3 after 102 ms: "bwrap (needed by the web process sandbox; packages bubblewrap and xdg-dbus-proxy)" |
| An empty `libwebkit2gtk-4.1.so.0` next to a copy of the executable | — | The loader skipped the invalid file and used the system library; the app ran normally |

Two details of the check:
- **`dlerror` must be bound before the first `dlopen`.** The first P/Invoke call binds lazily through the dynamic loader and cleared the error, so the first failure's reason came back empty.
- The helper folder is derived from the library's real path in `/proc/self/maps`, because the libexec path is compiled into WebKit.

## Recommendation: WebKitGTK 6.0 (GTK4)

1. **It is the maintained direction, and its sandbox cannot be forgotten.** The sandbox worked unprivileged on this desktop. The cost GTK4 showed here comes mostly from its own GL renderer on a software GPU, which disappears with GTK's cairo renderer.
2. **The measured differences that favour 4.1 are small or avoidable:**
   - 47–76 ms warm with acceleration;
   - about 80 MB more in the UI process (GTK4's GL renderer, here on llvmpipe);
   - WebDriver, which the design does not rely on: D8 rejected a TCP driver, and XTEST works equally on both APIs.
3. **Both are packaged by the same distributions** from Debian 12 and Ubuntu 22.04 on, and by Fedora.
4. **4.1's sandbox is opt-in:** a misconfigured app would ship an unsandboxed web process. In return, 4.1 can run unsandboxed in a root container for CI, while 6.0 needs a non-root user and user namespaces there.

**What would change this:**
- a need for WebDriver on Linux;
- a memory budget near the GTK3 figure;
- GTK4 on real GPUs turning out slower (untested: no hardware GPU was reachable here);
- a target distribution older than Debian 12 or Ubuntu 22.04.

**Regardless of the API**, adopt load-first, the startup check (libraries, helpers, sandbox tools) and a `web-process-terminated` handler.

## Failures and causes

1. **No `Settings` portal** in the WSLg session: there is no desktop name, and `UseIn` fallback does not cover Settings. The fix needs a `portals.conf` or a desktop name, which belong to the system or the user. Only a private session was tested.
2. **The host fell back to its own GTK setting** on portal value 0. Fixed in `Host.cs`.
3. **An empty first-failure reason** from `dlerror`. Fixed by binding `dlerror` first.
4. **CA1416** on `File.GetUnixFileMode`. Fixed with an assembly-level `SupportedOSPlatform("linux")`.
5. **CS8652** (async lambda in an `unsafe` method). Moved into a safe method.
6. **The d3d12 override:** GTK3 segfaulted at exit, and GTK4's web process aborted (blank window). Not fixed; the default llvmpipe path was used.
7. **Measurement fixes:**
   - `pgrep -f` matched the probing shell's own command line;
   - `run.sh` counted zombies of the exited host as "left";
   - the first memory pass missed the sandboxed web process (three levels down under `bwrap`).
   All three were corrected and the affected numbers rerun.
8. **The distro restarts after a short idle.** Each `wsl` call from Windows could start a fresh distro, adding 3.5 s to the first window. Matrices ran inside one `wsl` call, and Windows-side capture batches kept the distro up with a background `sleep 900`, which was stopped afterwards.

## Observed, skipped, not tested

**Observed**, all on WSLg with visible windows:
- Startup matrices with 5 rounds of cold and warm runs per combination: show-first and load-first, each with acceleration on and off (240 runs). Also `--hw never` and the cairo GSK renderer (3 rounds).
- The GTK3 X11 stall: `/proc` traces (6), the WebKit journal (1), the stack peek (1), environment variations (9 × 2), and the order fix (3 × 6).
- `webkit://gpu` for every backend, API and renderer mode; the d3d12 overrides (6); animation-frame jitter (13); resize captures (4).
- HiDPI variables (8, with shots); XTEST input (6); Windows-side input on Wayland (4).
- The portal in a private session (2 × 2 changes); the theme layers (40 captured runs).
- Memory (12); library hiding (11); sandbox process trees (2).

**Skipped** because of the rules or missing tools:
- `strace`, `gdb` and `perf` (not installed, no `sudo`). Replaced by `/proc` sampling, WebKit's journald log and stack-word mapping.
- WebKitWebDriver (not installed; downloading it was not needed for a decision).
- ibus and fcitx (not installed).
- Adding the user to the `render` group (system change).
- A real `portals.conf` in the user's session (user configuration).
- Changing the Windows display scale (system setting).

## Not tested

- **A hardware GPU path** without the d3d12 override (needs render-node access). DMA-BUF with real GPU buffers; NVIDIA's explicit-sync issues; GTK4 with a hardware GL renderer.
- **Real HiDPI** (125–200 %), and WSLg's own scaling together with GDK scale; fractional scaling; several monitors at different scales.
- **IME composition** (no framework installed); a screen reader.
- **Binding** weston's `zwp_input_method_v1` or `weston_screenshooter` as an ordinary client.
- **Native Linux desktops:**
  - GNOME/Mutter (where GTK CSD gets minimize and maximize, and the portal has a real backend), KDE Plasma, wlroots compositors;
  - libei and the RemoteDesktop portal;
  - libadwaita and the Yaru theme, including Yaru's dark header bar.
- **WebKitWebDriver** for 4.1 or 6.0 on a desktop.
- **The slow-HTML theme case** (the transparent background under a 1 s HTML delay) on WSLg.
- **The splash on a real desktop.**
- **The cause of the stall's ~0.5 s timeout** in WebKit's source (only the stack's libraries were seen, not symbols), and why the sandbox avoids it.
- **Memory with acceleration off**; long-running memory; several windows or views.
- **Distribution versions** other than Ubuntu 24.04's packages (only the package indexes were read); whether a stock Ubuntu desktop has either runtime. In this distribution both were installed by hand for the earlier spikes (apt history, 2026-11-13).
- **ARM64.**

## Downloads and leftovers

**Downloads:**
- None by me.
- NuGet restored offline from the Windows global packages folder (copied into the WSL work copy: 341 MB).
- The .NET SDK's own first-use step wrote its sentinels, three CRL files and a vulnerability-status file to `~/.dotnet`. The CRLs and status file suggest small network fetches by the SDK, which were not observed directly.
- The four package-index pages above were read with a web fetch.

**Left in WSL:**
- Deleted at the end: the work copy `~/spar-wsl-desktop` (818 MB: 341 MB NuGet copy, 236 MB Mesa and WebKit caches, builds), plus `~/.nuget/NuGet/NuGet.Config` and `~/.local/share/{NuGet,flatpak}`, which the tools created during this run. `~/.cache` was not written (every run set `XDG_CACHE_HOME`).
- Still there: `~/.dotnet` first-use files (the SDK's own); journald entries from one `WEBKIT_DEBUG=all` run; the user session's portal and at-spi processes, which GTK activates at startup and which stop with the distro. No process started by this spike is left running.

**Disposable outputs** in `artifacts/linux-wsl-desktop/`:
- `env.txt`, `*-summary.txt`, `theme-frames.txt`, `trace-journal.txt`;
- `win/` (Windows captures);
- `wsl/` (run logs, timelines, X11 frames, traces, libcheck logs).
