# Theme without flicker

Part of the Spar design. [`docs/plan.md`](../plan.md) has the goal, the settled decisions, the open questions and the milestones, and lists every design file. Section numbers are global: "section 3d" anywhere in the plan, the design files or `docs/decisions.md` means the heading of that number, wherever it lives.

## 3d. Light and dark theme without flicker (verified by `docs/evidence/theme-startup`; choices decided 2026-11-14)

**Goal:** no white flash in light or dark mode at startup, on a theme change, or while the page loads.

**Today's host, dark mode (baseline):**
- **Windows:** the client area was white for 333–366 ms (20–22 frames) until the app's first frame, and the title bar stayed light. The white came from the window itself, which had no class brush, not from the WebView.
- **Linux:** the GTK window colour showed for about 60 ms, then the WebView's white for 14–25 ms, then the page in light colours even in dark mode.

With the layers below, both themes on all three hosts (WebView2, WebKitGTK GTK3 and GTK4) gave **0 white or wrong-colour frames**.

- **Declared in C#** next to the CSP, with constant values the build can read:

  ```csharp
  spar.Window(w => w.Background(light: "#ffffff", dark: "#1f1f1f"));
  ```

- **The host paints before the page exists.** The minimal set, measured:
  - **Windows:**
    - the window class brush, plus a `WM_ERASEBKGND` handler that paints the current colour; this alone removes all the white;
    - `DWMWA_USE_IMMERSIVE_DARK_MODE` set before showing, for a dark title bar from the first frame;
    - the profile's `PreferredColorScheme`, needed when the app's choice differs from the system;
    - a **transparent** WebView default background (`ICoreWebView2ControllerOptions3` at creation), so the window colour shows through.

      In dark mode, while no document has painted yet (slow HTML, or a controller created before navigating), WebView2 paints Chromium's dark canvas `#121212` for about 900 ms, whatever opaque default background is set. Transparent was the only setting with 0 wrong frames in every case.

      No `WEBVIEW2_DEFAULT_BACKGROUND_COLOR`: it was not needed, and it overrides the creation options.
  - **Linux:**
    - a GTK CSS provider for the window;
    - GTK's dark preference (`gtk-application-prefer-dark-theme`). `prefers-color-scheme` follows it on GTK3 and GTK4, including live changes;
    - an **opaque** `webkit_web_view_set_background_color`. Transparent shows black for 11–22 ms on GTK3 without a compositor.

    GTK4's header bar follows the dark preference; Ubuntu's Yaru theme keeps the GTK3 header bar light.
- **The page style:** the build adds `<meta name="color-scheme" content="light dark">` and a small style with the same colours to `index.html`, and adds that style's hash to the CSP automatically. The hash works on both engines. Built in M1d step 5 (below).
  - It is not what removes the flash (no measurable effect once the host layers are in place). Keep it for the splash, form controls and as a safety net.
  - Prototype: a Vite plugin inserts the meta, the theme style (150 bytes) and the splash, hashes the styles as emitted, and writes the hashes for the host.
- **Which theme:**
  - **The system's,** by default: `AppsUseLightTheme` plus `WM_SETTINGCHANGE` (`ImmersiveColorSet`) on Windows. On Linux, read the XDG settings portal's `color-scheme` once and listen for its change signal, falling back to GTK's settings.
    - The fallback is required: under WSLg the portal runs but exports no Settings interface. With a Settings portal in a private session, the read took about 1 ms, and the page saw a change 23–28 ms after the signal.
    - Portal value 0 (no preference) means light. The fallback must never read back GTK settings the host set itself.
  - **Or the app's own choice:** the SDK's `setColorScheme("light" | "dark" | "system")`. The host saves the choice in the app's data folder and applies it at the next start, before the page loads. Measured: 0 wrong frames on all three hosts.
- **Live changes:** the host updates the window, title bar, WebView background and `prefers-color-scheme` together. The page reacts in 6–21 ms. Expect at most one mixed frame: 3 of 12 switches had the title bar one frame ahead.
- **Showing the window:** show it at once (visible in 1–5 ms on Windows, 35–60 ms on GTK3, 120–190 ms on GTK4, in the right colour).
  - A hidden window does not render on either engine, so `ui-ready` never arrives.
  - On Windows, a window cloaked with DWM until ready works, but a slow start then shows nothing for over a second.
- **Decided (user, 2026-11-14):**
  - **Showing:** the window shows at once, in the theme colour, never cloaked.
  - **WebView2 background:** transparent, not `#121212`. M1 measures what it costs.
  - **Splash delay:** it runs from the page's first frame, through a CSS animation delay.
  - **Blocked rendering:** the SDK requests an animation frame as soon as the bundle starts. M1 confirms why this fixes the minimum-visible count. (Confirmed in M1d step 5, "Built" below: one request is not enough on WebKitGTK; the SDK keeps requesting frames until the one that renders the mark.)
  - **Live theme changes:** at most one mixed frame (the title bar one frame ahead) is accepted.
  - **GTK theme name:** no dark theme name is set. GTK4's header bar follows the dark preference, and Spar uses GTK4 only.
- **Built on Windows (M1b step 2;** decision "Start-up: prerequisites, single instance and theme"; `docs/evidence/m1b-startup.md`): every host layer above, the colours from `spar.Window(w => w.Background(light, dark))` read at run time before `Run`, and the app's saved choice read from `color-scheme` in the data root. Observed on the Notebook, an on-screen window: 0 white and 0 `#121212` frames in dark, 0 dark frames in light (5 runs each), the title bar right from the first frame. **The transparent background costs nothing measurable:** `ui-ready` 290.7 against 294.5 ms (medians, 6 and 7 runs), WebView2 memory 86.3 against 86.4 MB. The page style and the SDK's `setColorScheme` came in M1d step 5 (below).
- **Built on Linux (M1c step 2;** decision "Linux start-up: prerequisites, single instance, theme and the software-GPU default"; `docs/evidence/m1c-startup.md`): the saved choice, the portal's `color-scheme` (read before GTK starts, never starting the portal, and followed live), or GTK's settings; the CSS provider, the dark preference and the opaque view background, plus one layer the list above lacks: **the X window's background pixel** in the theme's colour, set after `gtk_widget_realize`. Without it, a window on X11 with no compositor showed what was on the screen beneath it for about 6 ms before GTK's first paint. With it, ten alternating light and dark launches of the Notebook under Xvfb gave 0 samples of the other scheme.
- **Not tested:** a real light Windows theme and a real system `WM_SETTINGCHANGE`; an on-screen window, several monitors or DPI scales; Windows 10 and WebView2 Runtimes older than 157; the cost of a transparent WebView2 background; Linux on a real desktop (Wayland, compositor, portal, libadwaita); the Vite dev path; the C# declaration of the colours.

**Optional splash screen** (user decision; measured by `docs/evidence/theme-startup`):

- **Where it lives:** static HTML in `index.html`, inside the element with the id `spar-splash` (decided, user, 2026-11-22: the app writes the markup, the plugin adds the styles). It shows as soon as the HTML is parsed, before any JS runs.
- **The splash is an opaque cover in the theme colour from the first frame; only its mark fades in after the delay.** The first design, a transparent element that faded in, showed app → splash → app in manual mode.
- **Measured:**
  - a fast app (bundle +300 ms) showed 0 splash frames on all hosts;
  - slow apps always showed the splash exactly once (109 runs, never twice), for at least about 470 ms;
  - the host's `ui-ready` came 16–34 ms after removal;
  - `aria-busy` and reduced motion worked; without the CSP hash the splash simply doesn't show.
- **The delay runs from the page's first frame.** WebView2 holds the first frame of a page with nothing contentful for about 550 ms, so the effective threshold there is about 0.9–1.2 s after the window. Static text in the HTML brought the first frame to 172 ms.
- **Blocked rendering:** 1500 ms of synchronous start-up work showed no splash on WebView2, because no frames render; the plain theme colour shows instead. The minimum-visible guard miscounted under late work until the SDK requested an animation frame at bundle start (fixed in 12 of 12 runs; mechanism unconfirmed).
- **Proposed defaults:** 350 ms delay from the first frame, 400 ms minimum visible, 120 ms fade-in, 150 ms fade-out.
  - It is not native, because that would be a second visual system on two hosts, and the window colour already covers the WebView start.
  - It is not a SPA component, because that would appear only after the bundle has run, which is the wait it should hide.
- **No splash on a fast start,** to avoid a second kind of flicker: the mark appears only after the delay, through a CSS animation delay, so no JS is needed. Once visible, it stays for the minimum time before fading out. Both values are configurable.
- **Removal:** the app's `connection.ready()` removes it, once the app's first view has rendered; it is the only way a page reports ready (decided, user, 2026-11-22, replacing an automatic default and a manual mode). The `ui-ready` the host reports (and `spar snap`, tests and agents wait for) comes after the splash is gone.
- **Uses the theme:** the colours declared in C#, `prefers-color-scheme`, and images from the app's own origin. Its styles go through the same build step as the theme style, so their CSP hash is added automatically.
- **Accessibility:** `aria-busy` while shown, and no animation under `prefers-reduced-motion`.
- **Template:** `dotnet new spar-app` ships a commented-out example.
- **Later, if the SQL tool needs it:** start-up progress messages from the backend.
- **Built (M1d step 5;** decisions "M1d step 5's shape: the page side of start-up" and "The page side of start-up (implementation record; choices for review; 2026-11-22)"; `docs/evidence/m1d-page-startup.md`):
  - **The colours:** the client step reads `spar.Window(w => w.Background(light, dark))` from `Program.cs` as it reads the page security (`WindowColorsAnalysis`; SPAR106 for a colour that is not a constant `#rgb`/`#rrggbb` or a call the build cannot read) and writes them as `theme.json` in `@spar/app`; the defaults `#ffffff`/`#1f1f1f` without them.
  - **The page style:** the Vite plugin adds the meta (unless the page has one) and `<style id="spar-theme">` after `<meta charset>`: `:where(html){color-scheme:light dark;background-color:<light>}` and the dark colour under `prefers-color-scheme: dark`, which follows the app's choice or the system's as the host sets it. `:where()` keeps it below every rule of the app's. Each host hashes it from the served HTML at start (only that exact opening tag) into `style-src`; on the development server it gets Vite's nonce. Measured on both hosts: the style applied with 0 violations, the page's background the window colour in both schemes.
  - **The splash:** `#spar-splash` gets `<style id="spar-splash-style">`, hashed the same way: a fixed, opaque cover in the window colour of the scheme, whose children (the mark) fade in after the delay through `animation-delay`; under `prefers-reduced-motion` they appear at once when the delay ends (`step-start`), and there is no fade-out. `aria-busy="true"` goes on `<body>` until the removal. The defaults are the proposed ones, set with `spar({ splash: { delay, minimumVisible, fadeIn, fadeOut } })`; the minimum and the fade-out reach the SDK as custom properties of the cover.
  - **Readiness and removal:** `connection.ready()`, once per document. Without a splash it sends `ready` at once, as before. With one: not due yet, removed at once; due, kept until it has been on screen for the minimum, then faded out as a Web Animation; the host hears `ready` two frames after the removal (`ui-ready` 16–32 ms after it on WebView2 and 9–17 ms on WebKitGTK in the window tests).
  - **Blocked rendering, confirmed:** the two engines differ. WebView2 keeps the mark's opacity animation running on its compositor through a long task once a frame has started it, so the mark appears on time; WebKitGTK in WSL under Xvfb draws it only in the page's own frames, so a mark due during a long task appears at the first frame after it. Counted from the animation's timeline alone (the spike's rule), the mark was on screen 111–126 ms instead of 400 on WebKitGTK whenever 600 ms of work covered its appearance, whether a frame had been requested before the work or not: the single frame request does not fix it there. The SDK therefore keeps requesting frames from `connectSpar` (only while the page has a splash) until the first frame at or after the mark's scheduled appearance, and counts the minimum from that frame, or from now when no frame has rendered since the mark was due (a long task just ended, or the bundle started after it was due: nothing the page keeps tells whether a frame rendered before the SDK ran, since both engines' document timelines read the current time inside a long task). That keeps the mark on screen at least the minimum on both engines; its cost is up to the minimum (400 ms) longer than needed when the bundle starts after the mark was due (a slow download) on both engines, and after a long task on WebView2, whose compositor had shown the mark meanwhile. Chromium also stamps a frame with the time it began, before the task that delayed its callback, so the SDK reads `performance.now()` in the callback.
  - **`setColorScheme`:** the host's own command `spar.setColorScheme` (the contract name no app contract can take): saved with `SavedColorScheme`, then applied at once, window colour, title bar, WebView background and `prefers-color-scheme` together, as for a change of the system's theme (both hosts); declared error `notSaved`; on loopback saved only. Measured in the window tests on both hosts: the page's background and `prefers-color-scheme` changed with the window's colour and title bar, the file in the data root, the next start in the saved scheme.
