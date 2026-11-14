# Theme start-up spike: results

Recorded 2026-11-14 on the development machine: Windows 11 Pro 10.0.26320 (system theme dark: `AppsUseLightTheme = 0`; nothing in the system settings was changed), SDK 11.0.100, WebView2 Runtime 157.0.4341.46 with SDK headers 1.0.4314.35, Node 24.21, Vite 8.3.8, React 19.3.1. Linux: the existing image `spar-linux-desktop:webkit2.52` (Ubuntu 24.04, WebKitGTK 2.52.6 for GTK 3.24.41 and GTK 4.14.5, default GTK theme Yaru) under Docker Desktop, Xvfb with no window manager and no compositor, Mesa llvmpipe.

**Question.** Plan 3d proposes that the host paints the window, the title bar and the WebView in the theme's colour before the page exists, that the page agrees from its first frame, and an optional HTML/CSS splash. Does that give zero white or wrong-colour frames, which layers are needed, how should the window be shown, and what should the splash defaults be?

**Answer: yes, with changes.** Zero white or wrong-colour frames in both themes on WebView2 (measured frame by frame, confirmed with a second capture method) and on WebKitGTK GTK3 and GTK4 (as far as Xvfb shows; see its limits). The proposal needs these changes:

- **Windows: the WebView colour must be transparent.** The window background and the dark title bar remove everything a user sees today. The WebView's default background is not what WebView2 paints in dark mode: while no document has rendered yet, it paints Chromium's dark canvas `#121212`, whatever `DefaultBackgroundColor` says.
  - A **transparent** default background (the window colour shows through) was the only setting with zero wrong frames in every case.
  - Set it through `ICoreWebView2ControllerOptions3::put_DefaultBackgroundColor` at creation, not through the environment variable.
- **Linux: the opposite.** GTK3 needs an **opaque** `webkit_web_view_set_background_color`: transparent shows black on X11 without a compositor.
  - The GTK CSS provider paints the window.
  - `prefers-color-scheme` follows `gtk-application-prefer-dark-theme`, a `-dark` theme name, or `GTK_THEME=…:dark`, on both GTK versions, live.
  - With Ubuntu's Yaru GTK3 theme the dark preference does not darken the header bar (GTK4's Adwaita does).
- **Show the window at once** in the right colour. Showing it only at `ui-ready` works on Windows only with DWM cloaking. A hidden window stops rendering on both engines, so `ui-ready` never comes before the timeout.
- **The splash must be an opaque cover from the first frame**, with only its mark appearing after the delay. The first design (a transparent element that fades in) showed app → splash → app in manual mode.
- **The splash delay counts from the page's first frame.** WebKitGTK renders that frame right after the HTML. WebView2 holds the first frame of a page without contentful content for about 550 ms. On both engines, synchronous start-up work delays the frames.
- **Proposed splash defaults:** 350 ms delay, 400 ms minimum, 120 ms fade-in, 150 ms fade-out, removed at `ui-ready`.

## Layout

| Path | Contents |
|---|---|
| `Theme.Desktop/` | Windows host (NativeAOT, `win-x64`). Win32 window, WebView2 (hand-written COM; `Interop/` started as a copy of `browser-telemetry/react/React.Desktop/Interop`, plus raw vtable calls for `ICoreWebView2Controller2`, `ICoreWebView2_13`/`ICoreWebView2Profile`, `ICoreWebView2Environment10` and `ICoreWebView2ControllerOptions3`), each theme layer behind `--layers`, and two captures: `FrameCapture.cs` (`PrintWindow(PW_RENDERFULLCONTENT)`) and `WgcCapture.cs` (Windows.Graphics.Capture through raw vtables). The page comes from `frontend/dist` through `WebResourceRequested` on `https://app.localhost`, with the strict CSP header plus the build's style hashes. Permission requests are denied in the handler. |
| `Theme.Linux/` | Linux host (NativeAOT; GTK3 + WebKitGTK 4.1, or `-p:SparGtk=4` for GTK4 + WebKitGTK 6.0). `Native.cs` and `GLibContext.cs` are copies from `linux-desktop`; `NativeTheme.cs` adds GTK settings, the CSS provider, the header bar, `webkit_web_view_set_background_color` and the GDBus calls for the settings portal. The page comes through `spar://app/` with the same CSP. |
| `linux/` | `xcap.c` (X11 root capture with the same colour classes, CLOCK_MONOTONIC stamps, PNGs), `sync.sh`, `publish.sh`, `run.sh`, `run-sets.sh`. |
| `frontend/` | React app; `spar-theme-plugin.ts` (question 6); `src/spar.ts`, a stand-in for the SDK (`setColorScheme`, splash removal, `ready()`, `useManualReady()`, page diagnostics); pages `baseline`, `themed`, `splash` (cover design), `splash-200`, `splash-500`, `splash-text`, `splash-v1` (first design). |
| `tools/summarize.mjs` | Frame timelines as runs of colour classes, per-run numbers, and `--group` (cold run and warm median per configuration). `tools/pixel.mjs` reads one pixel of a captured PNG. |
| `run-windows.sh` | The Windows measurement sets. |

Outputs: `artifacts/theme-startup/<label>/` (`timeline.json`, a PNG for each change of frame class) and `artifacts/theme-startup/*.log`. Profiles: `artifacts/webview2-profiles/theme-startup-*`.

## How it was run

```sh
# Frontend (Windows; node_modules came from the local npm cache with --offline)
cd spikes/theme-startup/frontend && npm install --offline --ignore-scripts && npm run build
# Windows host (strict NativeAOT: 0 warnings), then one measurement set; each configuration gets a cold run
# (--fresh: a new profile) and N warm runs
dotnet publish spikes/theme-startup/Theme.Desktop -c Release
spikes/theme-startup/run-windows.sh <layers|validate|slowhtml|navdelay|cssdelay|wvwhite|show|showfix|live|persist|splash|splashfix> [warm runs]
node spikes/theme-startup/tools/summarize.mjs --quiet --group $(ls artifacts/theme-startup | grep '^layers-')
# Linux: container from the existing image; NuGet from a copy of spar-linux-desktop-nuget
docker volume create spar-theme-startup-nuget; docker volume create spar-theme-startup-work
docker run --rm -v spar-linux-desktop-nuget:/from:ro -v spar-theme-startup-nuget:/to spar-linux-desktop:webkit2.52 cp -a /from/. /to/
docker run -d --name spar-theme-startup --init --shm-size=1g --security-opt seccomp=unconfined -v <repo>:/src:ro \
  -v spar-theme-startup-work:/work -v <repo>/artifacts:/work/artifacts -v spar-theme-startup-nuget:/root/.nuget/packages \
  spar-linux-desktop:webkit2.52 sleep infinity
docker exec -d -u 1000 spar-theme-startup Xvfb :99 -screen 0 1024x768x24 -nolisten tcp -br   # same user as the app
docker exec spar-theme-startup bash /src/spikes/theme-startup/linux/sync.sh
docker exec -w /work/spikes/theme-startup spar-theme-startup bash linux/publish.sh gtk3   # and gtk4
docker exec -u 1000 -e HOME=/work/home -e DISPLAY=:99 -w /work/spikes/theme-startup spar-theme-startup \
  bash -c 'for v in gtk3 gtk4; do for s in scheme layers slowhtml splash show live persist; do linux/run-sets.sh $v $s 1; done; done'
```

Host switches (both hosts): `--theme light|dark|system` (the app's choice for the run), `--system light|dark` (simulated system theme), `--layers`, `--page`, `--query`, `--show now|ready|cloak`, `--html-delay`, `--css-delay`, `--bundle-delay`, `--navigate-delay` (Windows), `--live`, `--reduced-motion`, `--no-hash`. Light-theme runs on Windows are "the app chose light on a dark system": the real system theme was never changed.

## How frames were captured

- **Windows, method 1: `PrintWindow(hwnd, PW_RENDERFULLCONTENT)`** on a capture thread, every 16.6–17.0 ms (mean gap; max 17–40 ms), into a DIB. It works for a window placed left of the whole virtual screen and shown with `SW_SHOWNOACTIVATE` (no focus change, nothing on the user's screens). Each frame is reduced to the dominant colour (5-bit bins) and the shares of white, the light and dark theme colours, black, the app's header blue (`#2563eb`, "app visible") and the splash mark's orange, separately for the client area and the title-bar band.
  - Two pitfalls found in this method: a print that starts between `ShowWindow` and the first `WM_PAINT` returns an empty (black) surface that DWM never composes, so the summaries drop prints that start before the first `WM_ERASEBKGND`. And `PrintWindow` waits for the window's thread: joining the capture thread from the window thread deadlocked one run (90 s timeout), so the host awaits the capture's end while it keeps pumping.
- **Windows, method 2: Windows.Graphics.Capture** (`CreateForWindow`, free-threaded frame pool, `SystemRelativeTime` stamps converted to the host's QPC clock). It delivers a frame only when DWM composes a change, so it shows what the compositor produced. `CreateForWindow` rejects a hidden window (`E_INVALIDARG`); the host shows the window cloaked, attaches, then uncloaks. D3D and WinRT start-up took about 390 ms, so those runs start later.
- **Agreement:** in the `validate` set (10 configurations × 3 runs, both captures in the same run) every class and every wrong-frame kind matched: the same white period (PrintWindow 333–384 ms, WGC 350–383 ms), the same `#121212` period (899–917 ms), the same zero counts. WGC stamps the first app frame 28–30 ms later than PrintWindow (composition after the redirection surface).
- **Linux: `xcap`**, `XGetImage` of the Xvfb root every 2.0–2.5 ms (no compositor: the root is what a person would see), CLOCK_MONOTONIC stamps merged with the host's marks. With WebKit's DMA-BUF renderer on, nothing WebKit drew ever reached the X window under Xvfb; `run.sh` sets `WEBKIT_DISABLE_DMABUF_RENDERER=1`.
- **Classes:** `WHITE` (≥ `#fafafa`), `light-bg` `#f3f3f3`, `dark-bg` `#1f1f1f`, `BLACK`, `APP-<tone>`, `SPLASH-<tone>`, or the hex colour. A frame is *wrong* when its class is not the theme's background, the app or the splash in the theme's tone. In light runs `WHITE` counts as wrong (the theme colour is `#f3f3f3`), although a person would hardly see it.

## Results on Windows (WebView2)

All numbers are milliseconds after `CreateWindowEx` returned; "cold" is the first run with a new profile, "warm" the median of the warm runs. Frame counts are PrintWindow frames (about 16.7 ms each).

### Question 1: baseline (today's host: no brush, light title bar, default WebView, no page style)

| Theme | Shown | First app frame (cold / warm) | `ui-ready` | White client frames | Wrong title bar |
|---|---|---|---|---|---|
| dark (system) | 1–2 | 389 / 366 | 401 / 408 | 20–22 per run (333–366 ms), from the first frame to the app | every frame (light) |
| light (app's choice on a dark system) | 1–2 | 391 / 371 | 403 / 400 | 19–22 (316–366 ms) | none |

- The white is the **window's own background**, not the WebView: the host registered no class brush, and DWM shows that window white until something paints. With a brush (next table) there is no white at all, although the controller still has its default white background.
- In light, the page then renders **dark** for 20–22 frames: it follows the system (dark), not the app's choice.
- Cold and warm differ little (cold 389 against warm 363–385 for the first app frame). The environment callback came at 8–15 ms, the controller at 190–230 ms, the HTML request at about 265 ms, the first app frame at about 390 ms.
- One run in about 400 (`firstframe-light-none-warm3`) waited 5 s between `NavigationStarting` and the HTML request; unexplained, not reproduced.

### Question 2: the layers, added in turn (4 runs each: cold + 3 warm, both themes)

| Layers | Dark: white / wrong frames (max) | Dark: wrong title bar | Light: wrong frames (max) |
|---|---|---|---|
| L0 none | 22 / 22 | all | 43 (white, then the app in dark) |
| L1 + window brush (`hbrBackground` + `WM_ERASEBKGND`) | 0 / 0 | all | 20 (app in dark) |
| L2 + `DWMWA_USE_IMMERSIVE_DARK_MODE` before `ShowWindow` | 0 / 0 | 0 | 21 (app in dark) |
| L3a + `put_DefaultBackgroundColor` after creation | 0 / 0 | 0 | 21 |
| L3b + `WEBVIEW2_DEFAULT_BACKGROUND_COLOR` set in-process | 0 / 0 | 0 | 22 |
| L3c + `ControllerOptions3.DefaultBackgroundColor` | 0 / 0 | 0 | 21 |
| L4 + `Profile.PreferredColorScheme` | 0 / 0 | 0 | **0** |
| L5 + page meta and style (`themed.html`) | 0 / 0 | 0 | 0 |
| page only (no host layer) | 23 / 23 | all | 43 |

The WebView background layers changed nothing in a normal start, because WebView2's first frame was already the app's. They matter when the document is slow or the controller exists before navigation:

| Set (1 cold + 1 warm, or 2 + 1) | Dark | Light |
|---|---|---|
| HTML delayed 1 s, any opaque WebView colour (put, env, options) or none | 52–58 frames of **`#121212`** (~900 ms) | with `PreferredColorScheme` light: 0; without: `#121212`, then the app in dark |
| HTML delayed 1 s, **transparent** WebView (alpha 0 via options) | **0** | **0** |
| Controller alive 1 s before `Navigate`, opaque colours | 52–54 frames `#121212` | with the light scheme: 0 |
| Same, transparent | 0 | 0 |
| Same, light scheme, **no** WebView colour (`wvwhite`) | – | **52 white frames**: the default white |
| Same, light scheme, `put_DefaultBackgroundColor` right after creation, or the environment variable | – | 0 |
| Bundle stylesheet delayed 1 s (transparent WebView), `baseline` or `themed` page | 0 | 0 |

- **`#121212` is Chromium's dark canvas.** With the dark scheme, WebView2 paints its initial empty document in that colour from about 170 ms after the controller exists, ignoring `DefaultBackgroundColor` (all three ways of setting it). Read from the PNGs with `tools/pixel.mjs`: `#121212` against the theme's `#1f1f1f`. Not a flash, but a visible step. The `validate` set confirmed it with Windows.Graphics.Capture.
- **The controller did not paint white before `put_DefaultBackgroundColor` could run.** Its first frame came about 170 ms after creation; a `put` in the creation callback was always earlier (0 white frames in 2 + 2 runs). The documented white flicker that the environment variable fixes did not occur on this runtime.
- **The page style changed nothing measurable on WebView2:** the stylesheet is render-blocking, so the page shows nothing before its CSS. It is still needed for the splash, for form controls and scrollbars (`color-scheme`), and on WebKitGTK (below).
- **CSP hash:** with the hashes in `style-src`, both inline styles were applied (`sheet` present). With `--no-hash` both were `BLOCKED` (no `sheet`); the theme's colour still came from the host layers, and the splash silently did not appear (0 splash frames, app at 1479 ms).
- **Minimal set for zero white or wrong-colour frames, both themes:** window brush + dark title bar attribute + `PreferredColorScheme` (when the app's choice can differ from the system's) + a **transparent** default background set through `ControllerOptions3` (for slow documents and a controller created before navigation).

### Question 3: showing the window

| Mode (`themed.html`, all layers) | Something visible | App visible (cold / warm) | Notes |
|---|---|---|---|
| show at once | 1–5 | 398 / 389 (dark), 616 / 399 (light) | right colour from the first frame |
| show at once, bundle 1.2 s late | 1–5 | 1508 / 1470 | 1.5 s of the plain theme colour |
| cloaked until `ui-ready` (DWM `DWMWA_CLOAK`) | 418 / 408 | same frame | first composed frame is the app; 0 wrong frames |
| cloaked, bundle 1.2 s late | 1525–1561 | same frame | nothing on screen for 1.5 s |
| hidden (`SW_HIDE`) until `ui-ready`, 3 s timeout | 3252–3302 (timeout) | **never** (no `ui-ready` within 15 s) | the WebView does not render under a hidden parent, not even after `ShowWindow` |
| hidden, then `put_IsVisible(false/true)` after the timeout show | 3256–3303 | 3273–3342 | renders only once shown, so it always waits for the timeout |

- Cloaking is the only working "show at `ui-ready`". It hides the start completely, so a slow start shows nothing at all until the timeout. Showing at once gives feedback in under 5 ms and, with the layers, no wrong frames.

### Question 4: theme sources and live changes

- **System theme:** `HKCU\…\Themes\Personalize\AppsUseLightTheme` read with `Microsoft.Win32.Registry` (0 here: dark). Only read.
- **Simulated system change:** an in-process override, then `SendMessageTimeout(own window, WM_SETTINGCHANGE, 0, "ImmersiveColorSet")`. Nothing was broadcast.
- **Live sequence** (`live` set, starting dark and starting light; plus one run with both captures): system change → follows; app picks the other theme → follows and is saved; system change while the choice is fixed → ignored; `setColorScheme('system')` → back to the system's; system change → follows.
  - Every switch updated the client, the title bar and `prefers-color-scheme` together: the title bar changed in place (no `SWP_FRAMECHANGED` needed), and the page's `matchMedia` change event came 6–21 ms after the host applied the theme.
  - **3 of 12 switches showed one mixed frame** (16 ms): the title bar in the new theme, the content still in the old. Never more than one frame.
- **The app's saved choice at the next start** (`persist` set, 2 runs each): saved dark on a (simulated) light system, saved light on a dark system, and saved light on the real dark system: 0 wrong frames and 0 wrong title-bar frames in all 6 runs. The choice is read from `artifacts/theme-startup/appdata/<name>/color-scheme.txt` before the window is created.

### Question 5: the splash (Windows)

Two designs were measured.

- **v1:** the whole `#spar-splash` element is transparent, and fades in after the delay.
- **v2** (now `splash.html`): the element is an opaque cover in the theme colour from the first frame, and only the mark fades in after the delay.

Both: delay 350 ms (CSS `animation-delay`), at least 400 ms visible, fade-out 150 ms, removed by the SDK at `ready()`. Counts are per run; each configuration ran 3 times (cold + 2 warm).

| App | Splash frames | Splash appearances | First app frame (warm) | Notes |
|---|---|---|---|---|
| fast (no delay) | 0 | 0 | 366–390 | |
| bundle +300 ms | 0 | 0 | 614–627 | |
| bundle +600 ms, delay 200 or 500 | 0 | 0 | 911–912 | |
| bundle +1200 ms | 28–29 | 1 | 1612–1658 | v1 and v2 the same |
| bundle +2000 ms | 75–76 | 1 | 2439–2441 | |
| bundle +600 ms then 600 ms of synchronous work | v2 before the fix: **10–11** (~170 ms on screen, although the SDK thought 583 ms); after: 28–29 | 1 | 1638–1662 | see below |
| 1500 ms of synchronous work, no bundle delay | 0 | 0 | 1798–1834 | the splash cannot appear |
| manual mode, data after 1000 ms, **v1** | 37–40 | 1 | **401–408** | app visible, then the splash over it, then the app: a flash |
| manual mode, data after 1000 ms, **v2** | 39–40 | 1 | 1424–1436 | background → splash → app |
| manual mode, data after 150 ms | 0 | 0 | 462–474 | |
| static text in `#root` (`splash-text`), bundle +600 ms | 29 | 1 | 1216–1223 | first paint 172 ms instead of ~600 |
| reduced motion (CDP `Emulation.setEmulatedMedia`) | 23–24 | 1 | 1536–1548 | no fades; `animation-duration` 0.001 s |
| CSP without the hashes | 0 | 0 | 1479–1488 | both inline styles blocked |

- **Never on and off:** every run with a splash showed exactly one appearance. The shortest was 28 frames (about 470 ms: 400 ms minimum plus the fade-out), 23 with reduced motion.
- **The delay counts from the first frame, and WebView2 holds the first frame.** A page with no contentful content (the splash page, the theme-only page) had `first-paint` at 576–604 ms after navigation, against `domInteractive` at about 40 ms. Chromium holds the first frame until contentful paint or about 500 ms. With static text in the HTML, `first-paint` came at 172–216 ms. So on WebView2 a start must take about 0.9–1.2 s from the window before the mark shows, not 350 ms.
- **Synchronous start-up work blocks the splash.** 1500 ms of synchronous work right after the bundle starts gave 0 splash frames: no frame, so no CSS animation, until the work ended. The user saw the plain theme colour, which is not a flicker.
- **The minimum-visible rule and blocked rendering.** With v2 and bundle +600 ms then 600 ms of work, the SDK counted 583 ms of visibility from the CSS animation's timeline, but the mark was on screen for only 10–11 frames, because no frame rendered during the work.
  - The fix tried: a `requestAnimationFrame` at bundle start, and taking the first frame after the bundle start as the earliest visibility when that frame came more than 100 ms late.
  - After it, the mark was on screen 28–29 frames in all 12 runs, with the rule on and off (`timelineOnly`). The rule itself never triggered: the frame request at bundle start alone let the mark appear during the work. The mechanism is not confirmed.
- **`ui-ready` after removal:** in every splash run the host's `ui-ready` came 16–34 ms after the page's `splash-removed` (the SDK waits two frames; 96 runs).
- **`aria-busy`:** `true` on `<body>` at bundle start, removed at `ready()`; the splash is `role="status"`, `aria-label="Loading"`.
- **CSP:** the splash style's hash was accepted; without it the style was blocked and the splash silently did not show.

## Results on Linux (WebKitGTK)

Each configuration ran cold (`--fresh` WebKit data) and warm, on GTK3 (WebKitGTK 4.1, web process unsandboxed) and GTK4 (WebKitGTK 6.0, sandboxed, uid 1000 with `seccomp=unconfined`). The window is 960×720 at 0,0 with a GTK header bar (`--headerbar`), so the title-bar band is GTK's own drawing. Frames are about 2.2 ms apart, so durations are given in ms. All numbers are after the host created the window.

**Limits of this capture, found on the way:**
- With WebKit's DMA-BUF renderer on (the default), nothing WebKit drew ever reached the Xvfb window; GTK's own drawing did. All runs therefore set `WEBKIT_DISABLE_DMABUF_RENDERER=1`. The DMA-BUF path, which real desktops use, was not captured.
- With Xvfb running as root and the app as uid 1000, Mesa could not attach MIT-SHM (`Failed to attach to x11 shm`) and GTK4 showed nothing. Xvfb now runs as the app's user.
- The window maps 35–60 ms (GTK3; up to 119 ms) or 118–187 ms (GTK4) after creation. Frames before the map show the root and are left out.

### Question 1 and 2: layers (`layers` set, header bar on)

| Layers | GTK3 dark | GTK3 light | GTK4 dark | GTK4 light |
|---|---|---|---|---|
| L0 none | window `#f6f5f4` (GTK theme) ~60 ms, **white 16–25 ms** (WebView default), then the page **in light** | `#f6f5f4` ~60 ms, white 19–20 ms | same as GTK3: `#f6f5f4`, white 14–18 ms, page in light | `#f6f5f4`, white 14–18 ms |
| L1 + GTK CSS provider (window background) | white 23–26 ms, page still in light | white 16–18 ms | white 11–18 ms, page in light | white 12–19 ms |
| L2 + `gtk-application-prefer-dark-theme` | white 19–22 ms; page now dark | white 20–23 ms | white 15 ms; page dark | white 12–13 ms |
| L3 + `webkit_web_view_set_background_color` | **0 wrong** | **0** | **0** | **0** |
| L4 + page meta and style | 0 | 0 | 0 | 0 |
| L4 with a **transparent** WebView instead | **black 20–22 ms** | black 11–15 ms | 0 | 0 |
| page only | white, `#f6f5f4`, page in light | white, `#f6f5f4` | the same | the same |

- First app frame (cold / warm): GTK3 about 140 / 122 ms, GTK4 about 245 / 230 ms; `ui-ready` 35–40 ms later. Much faster than WebView2 (about 390 ms), because there is no browser-process start.
- **Header bar:** GTK4 (built-in Adwaita) drew it dark from L2 on. GTK3 with Ubuntu's default theme **Yaru** kept it light in every dark run (`titleWrong` on all frames): `gtk-application-prefer-dark-theme` does not darken Yaru's header bar here. `gtk-theme-name=Adwaita-dark` was not combined with the header-bar capture.
- **What drives `prefers-color-scheme`** (`scheme` set, page report, 2 runs each, both GTK versions): nothing set → `light`; `gtk-application-prefer-dark-theme=TRUE` → `dark`; `gtk-theme-name=Adwaita-dark` → `dark`; `GTK_THEME=Adwaita:dark` in the environment → `dark`. No libadwaita is installed in the image, so `AdwStyleManager` was not tried.
- **Slow document** (HTML delayed 1 s): with the CSS provider and dark preference but no WebView colour, **white for 1004–1020 ms** on both GTK versions. With the WebView colour: 0. Transparent: **black for 1010–1020 ms on GTK3**, 0 on GTK4.
- **CSP hash:** WebKitGTK honoured the CSP header on `spar://` responses. Both inline styles were applied with their hashes, and both were `BLOCKED` without them (`--no-hash`: 0 splash frames).
- **A host bug found here, worth a pitfall:** `webkit_uri_scheme_response_set_http_headers` replaces the response headers. Without `Content-Type` in that header set, `X-Content-Type-Options: nosniff` made WebKit drop the bundle's stylesheet: the `<link>` had a sheet with **0 rules**, and the JavaScript still ran. Adding `Content-Type` to the headers fixed it (`rules=6`). All Linux numbers above are from the fixed host.

### Question 3: showing the window

- At once: something visible at the map (35–60 ms on GTK3, 118–187 ms on GTK4), the app at 119–164 ms (GTK3) or 218–248 ms (GTK4). 0 wrong frames.
- Hidden until `ui-ready` (3 s timeout): **WebKit did not render while the window was unmapped**. `ui-ready` came only after the timeout show: 3088–3102 ms on GTK3, 3164–3174 ms on GTK4. The same as WebView2 under a hidden parent. No cloaking equivalent was tried on X11.

### Question 4: theme sources

- **XDG settings portal:**
  - No session bus (the container's default): `g_bus_get_sync` failed with "Failed to execute child process dbus-launch".
  - With a bus (`dbus-run-session`) but no portal: `ReadOne` returned `org.freedesktop.DBus.Error.ServiceUnknown` after 1.9 ms.
  - In both cases the fallback ran: `gtk-theme-name` (`Yaru`), `gtk-application-prefer-dark-theme` (`FALSE`) and `GTK_THEME` (unset) → light. The portal's `SettingChanged` signal was not available, so system changes were simulated by calling the same handler.
- **Live** (`live` set, 2 runs per start theme per GTK version): every switch updated the window colour, the WebView colour and `prefers-color-scheme` together. The page's change event came 9–14 ms after the host applied the theme.
  - GTK4's header bar switched 12–15 ms **before** the content (5–6 frames at 2 ms).
  - GTK3's Yaru header bar never switched.
  - A choice fixed by the app ignored system changes, as on Windows.
  - Note: `SystemTheme()` called the portal again on every change (synchronous D-Bus); a real host should cache the value and listen for the signal.
- **Saved choice** (`persist`, 2 runs each): saved dark on a simulated light system, saved light on a simulated dark system, and saved dark with no system information: 0 wrong frames on GTK3 and GTK4 (apart from the Yaru header bar on GTK3).

### Question 5: the splash on WebKitGTK (cover design, the SDK with the frame request)

| App | GTK3 splash on screen | GTK4 splash on screen | Appearances |
|---|---|---|---|
| fast | 0 | 0 | 0 |
| bundle +300 ms | 0 | 0 | 0 |
| bundle +1200 ms | 949–988 ms | 950–977 ms | 1 |
| bundle +600 ms then 600 ms of work | 1334–1352 ms | 1332–1349 ms | 1 |
| 1500 ms of synchronous work | 505–511 ms | 498–514 ms | 1 |
| manual, data after 1000 ms | 751–768 ms | 761–766 ms | 1 |
| reduced motion (`gtk-enable-animations=false`), bundle +1200 ms | 877–878 ms | 0 (cold) and 894 ms (warm) | ≤ 1 |
| CSP without hashes | 0 | 0 | 0 |

- **WebKit has no paint holding:** `first-contentful-paint` at 34–42 ms for the fast page, and the mark became visible about 366 ms after navigation (`visible-since` ≈ delay + 16 ms).
- **Synchronous work does not hide the splash on WebKit:** the mark showed up 350 ms after the HTML even with 1500 ms of synchronous work from the bundle's start. Windows showed 0 frames for the same case.
- The minimum-visible rule's frame check triggered in 16 runs (`splash-blocked`; work blocked the first frame after the bundle start). In those runs the SDK waited 366–386 ms longer, so the splash stayed at least 400 ms on screen.
- **Reduced motion on GTK4:** in 1 of 2 runs the mark never appeared on screen, although the SDK computed it as visible for 876 ms: the 1 ms step animation did not cause a frame. On GTK3 it appeared in both runs.
- `prefers-reduced-motion` followed `gtk-enable-animations=false` on both GTK versions (`reducedMotion: true` in the page report).

## Defaults proposed for the splash

| Setting | Proposal | Evidence |
|---|---|---|
| Delay before the mark may appear | **350 ms** after the page's first frame | No splash for any start up to bundle +300 ms on WebKitGTK, nor up to bundle +600 ms on WebView2 (first app frame 911 ms). On WebView2 the effective threshold is about 0.9–1.2 s after window creation, because the first frame is held (~550 ms). Delays of 200 and 500 ms gave the same result with bundle +600 ms on WebView2. |
| Minimum visible | **400 ms**, plus a 150 ms fade-out | Shortest appearance on screen: 28 frames (about 470 ms) on WebView2, 498 ms on WebKitGTK; never two appearances (109 runs with a splash, across all splash sets and engines). |
| Fade-in | 120 ms | No partly visible frames were counted as a separate appearance. |
| Reduced motion | No fades, no pulse; keep the delay | 23–24 frames on WebView2; WebKitGTK GTK4 needs a real frame (see above). |
| Removal | At `ui-ready` by default; `useManualReady()` + `ready()` for apps that wait for data | Manual mode with the cover: background → splash → app on all three engines. |
| Design | **Opaque cover in the theme colour from the first frame; only the mark is delayed** | v1 (transparent until the delay) showed the app, then the splash over it, then the app (manual mode, 3 of 3 runs). |

## What must change in plan 3d

1. **WebView2 background:** use a **transparent** `DefaultBackgroundColor` (alpha 0) set through `ICoreWebView2ControllerOptions3` at controller creation, with the window brush showing through.
   - An opaque colour is overridden by Chromium's dark canvas `#121212` while no document has painted.
   - Drop "the host may set `WEBVIEW2_DEFAULT_BACKGROUND_COLOR` itself". The documented flicker did not occur; the options API does the same at creation; and the variable overrides the options, so an outside value would win, which conflicts with plan 3c.
2. **WebKitGTK background:** opaque `webkit_web_view_set_background_color` in the theme colour. Do not use transparent there: black on GTK3 without a compositor.
3. **Window background on Windows:** the class brush plus a `WM_ERASEBKGND` handler that fills with the current colour, so live changes repaint. The white in today's host is the window, not the WebView.
4. **`prefers-color-scheme` on Linux** is confirmed: `gtk-application-prefer-dark-theme` (or a dark theme name, or `GTK_THEME`) drives it, live, on both GTK versions. Header-bar colour depends on the GTK theme: Yaru's GTK3 header bar ignored it.
5. **Showing the window:** show at once in the theme colour, `SW_SHOWNOACTIVATE`-style, with no wait for `ui-ready`. If "show at ready" stays as an option, Windows needs DWM cloaking, never a hidden window. On Linux a hidden window does not render, so the option is not available there as measured.
6. **The page style is not what removes the flash** on either engine; the host layers do. Keep it (with `color-scheme`) for the splash, form controls and scrollbars, and as a safety net. Its hash works under `style-src` on both engines.
7. **The splash:** an opaque cover from the first frame, with only the mark delayed. The SDK counts visibility from the CSS animation, but checks the first rendered frame after the bundle starts: when start-up work blocked rendering, visibility starts at that frame. State that the delay runs from the page's first frame: about 550 ms later on WebView2 for a page without contentful content, and later still when the bundle blocks the main thread.
8. **Live changes:** apply the native layers and the WebView's colour scheme in one step. Expect at most one frame (WebView2) or 12–15 ms (GTK4) of title bar ahead of content. Optional refinement: switch the title bar after the page reports its repaint.
9. **System theme on Linux:** the XDG portal first (read once, then `SettingChanged`), then GTK's settings as the fallback. The container had no portal and no session bus, so that path is the one measured.

## Pitfalls worth recording

- **Windows host:** a window class without a background brush is shown **white** by DWM until something paints it. That, not the WebView, is today's white flash.
- **Windows host:** in dark mode WebView2 paints `#121212` (Chromium's dark canvas) for its initial empty document, whatever `DefaultBackgroundColor` is. Only a transparent default background avoids it.
- **Windows host:** a WebView2 controller created under a **hidden** parent window does not render after `ShowWindow`, and needs `put_IsVisible`. While hidden, `requestAnimationFrame` does not run, so a "show at ui-ready" never sees `ui-ready`. Use DWM cloaking.
- **Windows host:** Chromium holds a page's first frame for about 500 ms when the page has no contentful content, so CSS animation delays (and anything timed from the first frame) start late.
- **Windows test tooling:** `PrintWindow` waits for the target window's thread. Joining a capture thread from the window thread deadlocks. A print that races the first `WM_PAINT` returns an empty surface DWM never shows.
- **Windows test tooling:** Windows.Graphics.Capture `CreateForWindow` rejects a hidden window (`E_INVALIDARG`); a shown, cloaked window works.
- **Linux host:** `webkit_uri_scheme_response_set_http_headers` replaces the headers. Put `Content-Type` in that header set, or `nosniff` silently drops stylesheets (sheet with 0 rules) while scripts still run.
- **Linux host:** a transparent `webkit_web_view_set_background_color` shows black on GTK3/X11 without a compositor.
- **Linux host:** a GTK theme can ignore `gtk-application-prefer-dark-theme` for its own widgets (Yaru's GTK3 header bar), while WebKit's `prefers-color-scheme` still follows it.
- **Linux test tooling (Xvfb):** WebKit's DMA-BUF renderer output never reaches the X window under Xvfb, so capture with `WEBKIT_DISABLE_DMABUF_RENDERER=1`. Run Xvfb as the same user as the app, or Mesa cannot attach MIT-SHM and GTK4 shows nothing.

## Open choices for the user

1. **Show at once (recommended) or cloaked until `ui-ready`.**
   - At once: feedback in under 5 ms, the theme colour until the app.
   - Cloaked: the first visible frame is the app, but nothing is on screen for the whole start (1.5 s with a slow bundle, up to the timeout). Windows only.
2. **Opaque or transparent WebView2 background.** Transparent removes the `#121212` frames; an opaque colour equal to the theme is simpler but shows `#121212` whenever the document is slow in dark mode. Choosing `#121212` as the dark default is a third option.
3. **Splash delay semantics.** Accept that the delay runs from the first frame (about 550 ms later on WebView2 for content-less pages), or make the HTML contentful at once. Static text in `#root` brought the first frame to 172 ms.
4. **The splash's minimum-visible guard:** keep the frame check in the SDK (it only matters when start-up work blocks rendering), or document "no long synchronous work at start".
5. **Title bar switching order on live changes:** switch the native parts at once (what was measured), or wait for the page's repaint to avoid the one mixed frame.
6. **Linux header bar under themes such as Yaru:** accept it, or also set `gtk-theme-name` to a dark theme. That changes all GTK widgets, and is not measured with the header bar.

## Question 6: build-time hashes (prototype)

`frontend/spar-theme-plugin.ts`, used by `vite.config.ts`:

- `transformIndexHtml` (`order: "post"`) inserts, right after `<meta charset>`:
  - `<meta name="color-scheme" content="light dark">`;
  - `<style id="spar-theme">`, one line built from the declared constants (`#rrggbb` only, checked);
  - optionally `<style id="spar-splash-style">`, the splash markup as the first child of `<body>`, and `aria-busy="true"` on `<body>`.
- `writeBundle` hashes every inline `<style>` **as emitted** (`sha256`, base64) and writes `spar-csp.json` (`styleSrc` plus per-page lists). It writes only when the content changes, although Vite's `emptyOutDir` recreates the folder here; Spar would write it under `obj/`.
- Sizes: the theme style is 150 bytes, the splash style about 900 bytes; `themed.html` 604 bytes, `splash.html` 1,496 bytes.
- Both hosts read `spar-csp.json` and append the sources to `style-src`.
- The theme style's hash stayed `'sha256-KTPk7uDd…'` across all rebuilds.

Not done here: the C# declaration and its extraction (plan 3b's spike), and the Debug path (a Vite nonce instead of hashes).

## Not tested

- A real **light** Windows system theme, and a real `WM_SETTINGCHANGE` sent by Windows: the system setting was only read, and changes were simulated in-process.
- High contrast, accent colours, `DWMWA_CAPTION_COLOR`, Mica or other system backdrops, Windows 10 (where the dark-mode attribute had another number).
- A window **on screen**, maximised, on other monitors or DPI scales: every Windows run was offscreen with `SW_SHOWNOACTIVATE`.
- WebView2 Runtimes older than 157, and whether `ICoreWebView2ControllerOptions3` exists on the minimum supported Runtime.
- The cost of a transparent WebView2 background (rendering or memory).
- The root cause of the one 5 s navigation stall on Windows.
- A cold start after a reboot: "cold" here means a new profile, with the OS file cache warm.
- Linux on a real desktop:
  - window manager, compositor and server-side title bars (Mutter, KWin);
  - Wayland;
  - WebKit's DMA-BUF renderer;
  - the real XDG portal and its `SettingChanged` signal;
  - libadwaita / `AdwStyleManager`;
  - the GTK3 web-process sandbox.
- `gtk-theme-name=Adwaita-dark` together with the header-bar capture, and a Yaru dark header bar.
- Cloaking or a hidden-but-rendering mode on X11.
- The Vite dev server path (a nonce instead of hashes), and the C# declaration of the colours.
- A screen reader with `aria-busy` (only the attribute was checked); the operating systems' own reduced-motion settings (CDP emulation on Windows, `gtk-enable-animations` on Linux).
- Splash images from the app origin.
- Live switches on Windows with both capture methods beyond one run.

## Left behind

- `artifacts/theme-startup/` (~77 MB: timelines, PNGs, logs, and `appdata/` with the saved choices).
- No downloads. npm used the local cache (`--offline`), NuGet a copy of the existing volume, and Docker the existing image.
- The container `spar-theme-startup` and the volumes `spar-theme-startup-work` and `spar-theme-startup-nuget` were removed at the end. The spike's WebView2 and WebKit profiles (`artifacts/webview2-profiles/theme-startup-*`, about 1.6 GB) were deleted once no browser process used them.
