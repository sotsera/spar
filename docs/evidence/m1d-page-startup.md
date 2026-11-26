# M1d step 5: the page side of start-up

Recorded 2026-11-22. What it shows: plan M1d, build step 5 (section 3d): the page's theme style from the window colours `Program.cs` declares, its CSP hash on both hosts; the optional splash with its delays, minimum time and accessibility; readiness through `connection.ready()` only, the host's `ui-ready` after the splash is gone; the SDK's `setColorScheme`; and the frame-by-frame measurements that settled how the SDK counts the splash's minimum visible time on WebView2 and WebKitGTK. Decisions "M1d step 5's shape: the page side of start-up (user decisions, 2026-11-22)" and "The page side of start-up (implementation record; choices for review; 2026-11-22)".

## Environment

- **Windows:** Windows 11 Pro 10.0.26320 (system theme dark), SDK 11.0.100, WebView2 Runtime 157.0.4341.60. Every window was a test window of the Window surface: offscreen, never activated, in the test process; frames read from it with `PrintWindow(PW_RENDERFULLCONTENT)`. Nothing was shown on, or read from, the user's monitors; no input was sent.
- **Linux:** WSL Ubuntu-24.04 (kernel 6.18.58.1), the repository's copy in `/var/tmp/spar-m1c-blackbox/repo` run by `docs/evidence/m1c-blackbox/wsl.sh` (`sync client build`, then `window …`), WebKitGTK 6.0 under the test process's private Xvfb (from `:140`), software rendering (no render node), `prlimit --core=1`, `DISPLAY` and `WAYLAND_DISPLAY` unset, so nothing opened on WSLg's display. Frames read from the Xvfb with `XGetImage`.
- Data roots under each copy's `artifacts/test-host/`, WebView2 profiles under `artifacts/webview2-profiles/`, Linux homes under `artifacts/linux-homes/`; the test host deleted each root. 0 processes left after each Linux run (the script's census).

## What was added

| Path | Contents |
|---|---|
| `src/Spar.Generator/WindowColorsAnalysis.cs`, `Descriptors.cs`, `PageSecurityAnalyzer.cs` | The window colours read from `Program.cs` without running it; SPAR106 for a colour that is not a constant `#rgb`/`#rrggbb` or a `Background` call the build cannot read |
| `src/Spar.Sdk.Tasks/Client/ContractSource.cs`, `ClientPackage.cs`, `SparClient.cs` | `theme.json` in `@spar/app`, beside `csp.json` |
| `src/Spar.Client/src/vite/pageStyle.ts`, `index.ts` | The theme style, the splash style, `aria-busy`, the meta, the `splash` timing options; an `order: "post"` HTML transform |
| `src/Spar/Hosting/PageHtml.cs`, `PageResponder.cs`, `src/Spar.Loopback/`, `src/Spar.Testing.Browser/SparBrowserSurface.cs` | The two styles hashed from the served HTML by their exact opening tags, into `style-src`, on every host |
| `src/Spar.Client/src/splash.ts`, `connection.ts`, `sdk.ts` | The frame watch from `connectSpar`, the splash's removal, `ready()` once per document, `ready` after the removal |
| `src/Spar.Client/src/colorScheme.ts`, `testing.ts` | `setColorScheme` with its declared error `notSaved`; `fakeSpar()` answering it |
| `src/Spar/Bridge/HostCommands.cs`, `BridgeSession.cs`, `src/Spar/Hosting/SavedColorScheme.cs` | The host's own command `spar.setColorScheme`: the choice saved in the data root, handed to the window |
| `src/Spar.Windows/AppWindow.cs`, `WindowTheme.cs`, `WebViewBridge.cs`, `BrowserSource.cs`, `Testing/TestWindow.cs` | The choice applied at once on Windows; a test window's emulated scheme following it |
| `src/Spar.Linux/AppWindow.cs`, `WindowTheme.cs`, `ScriptBridge.cs` | The same on Linux, the portal read off the loop for `system` |
| `tests/apps/StartupProbe/` | The test app: window colours `#eef3f8`/`#141a22`, an orange mark, a blue page once ready; a first page whose start the window's width picks and a splash page whose address picks it |
| `tests/Spar.WindowTests/StartupTests.cs`, `StartupFrameTests.cs`, `WindowFrames.cs`, `Linux/X11Frames.cs` | The window tests (6) and the explicit frame tests (22), on both hosts |
| `samples/Notebook/frontend/index.html`, `src/styles.css`; `src/Spar.Templates/content/spar-app/MyApp/frontend/index.html` | The Notebook's splash; the template's commented-out example |

## Results (observed)

### The theme style and its hash

`StartupTests`, 6 of 6 on Windows and 6 of 6 on Linux (final code, and in every run before it):
- In a light and a dark window the theme style applied (`sheet` present: not blocked), the page's computed background was the window colour (`rgb(238, 243, 248)` light, `rgb(20, 26, 34)` dark), `color-scheme` `light dark`, the meta present, and `app.Problems` empty: no CSP violation, no refused message.
- The built page (the Notebook's Test variant) has `<style id="spar-theme">` right after `<meta charset>`, after the SRI import map, its text exactly as `pageStyle.ts` writes it; the host's policy for it carries the theme style's hash beside the import map's.

### Readiness and the splash

- A fast start (the splash page with `ready=0`): the splash left in the same task as `ready()` (0–1 ms), `aria-busy` gone, the mark never shown.
- A slow start (`ready=1200`): at about 1 s the mark's computed opacity was 1 over the opaque cover in the window colour; at ready it had been visible for about 850 ms, so it faded out without waiting (removed 135–167 ms after ready); `aria-busy` gone.
- **The host's `ui-ready` came after the splash had left:** 16–32 ms after it on WebView2 (15 starts) and 9–17 ms on WebKitGTK (8 starts), read from the app's log against the page's clock ([`m1d-page-startup/ui-ready-after-removal.txt`](m1d-page-startup/ui-ready-after-removal.txt)).

### `setColorScheme`

On both hosts, in a light test window: `setColorScheme("dark")` resolved with success; the page then read `prefers-color-scheme: dark` and the dark background, the window reported a dark title bar (Windows: `DWMWA_USE_IMMERSIVE_DARK_MODE`; Linux: GTK's dark preference) and the dark window colour, and `color-scheme` in the data root read `dark`; `setColorScheme("light")` brought all of them back. A window closed after choosing dark left `dark` saved for the next start. The host's log line: "Colour scheme dark chosen by the page: applied and saved." On WebKitGTK the page's `matchMedia(…).matches` became true one rendering update after the style (pitfalls).

### The splash frame by frame

`StartupFrameTests` (explicit): the probe's splash page opened after a page of the theme colour alone, or a fresh window's first page; the middle of the window (the mark) and a corner sampled every 16.7–17.5 ms on WebView2 and about every 2–4 ms on WebKitGTK, each sample classified as the cover, the mark or the app. 5 runs per start, 14 later-page starts and 8 first-page starts per engine. The starts: `late` delays the bundle (a wait before `connectSpar`), `work` is synchronous work in the task that connects the SDK, in a later task, or before the SDK connects (so no frame was requested first), `ready` delays the app's first view.

**Every run on both engines and with every rule: 0 frames of another colour, the mark at most once.** A fast start and a bundle +300 ms never showed the mark. On WebView2 a test window's first sample, read before the window had been composed, was black in 0–5 of 5 runs per first-page start; those leading samples are counted apart (pitfalls) and never came later in a run.

**The mark on screen, in milliseconds (range over 5 runs; "–" when it never appeared).** "Spike rule" is the SDK counting from the entrance animation's timeline, with `docs/evidence/theme-startup`'s check of the first frame after one frame request ([`frames-webview2-spike-rule.txt`](m1d-page-startup/frames-webview2-spike-rule.txt), [`frames-webkitgtk-spike-rule.txt`](m1d-page-startup/frames-webkitgtk-spike-rule.txt)); "as built" is the final SDK ([`frames-webview2-final.txt`](m1d-page-startup/frames-webview2-final.txt), [`frames-webkitgtk-final.txt`](m1d-page-startup/frames-webkitgtk-final.txt)).

| Start (later page) | WebView2, spike rule | WebView2, as built | WebKitGTK, spike rule | WebKitGTK, as built |
|---|---|---|---|---|
| data after 1500 ms | 1188–1222 | 1200–1217 | 1216–1217 | 1215–1216 |
| bundle +1200 ms | 890–922 | 1299–1319 | 916–917 | 1315–1328 |
| bundle +600 ms, 600 ms of work in the connecting task | 1269–1323 | 1300–1301 | 913–916 | 1314–1317 |
| bundle +600 ms, work in a later task | 1285–1302 | 1300–1318 | 899–917 | 1314–1326 |
| bundle +600 ms, work before the SDK connects | 888–920 | 1301–1318 | 916–917 | 1315–1317 |
| bundle +200 ms, work in the connecting task | 885–905 | 900–901 | **114–126** | 512–522 |
| bundle +200 ms, work in a later task | 884–900 | 899–917 | **114–125** | 513–523 |
| bundle +200 ms, work before the SDK connects | 489–522 | 916–933 | **111–116** | 512–521 |
| 600 ms of work in the connecting task | – | – | 250 (1 of 5) | 515–518 (2 of 5) |
| 600 ms of work in a later task | – | 700 (1 of 5) | – | 514 (1 of 5) |
| 600 ms of work before the SDK connects | – | – | – | – |
| 1500 ms of work in the connecting task | – | – | 114 (1 of 5) | – |

| Start (a window's first page) | WebView2, spike rule | WebView2, as built | WebKitGTK, spike rule | WebKitGTK, as built |
|---|---|---|---|---|
| data after 1500 ms | 1216–1234 | 1217–1234 | 1231–1233 | 1232–1233 |
| bundle +1200 ms | 916–934 | 1333–1351 | 933–934 | 1332–1333 |
| bundle +600 ms, work in the connecting task | 1317–1333 | 1317–1334 | 916 | 1332–1333 |
| bundle +600 ms, work in a later task | 916–933 | 917–934 | 932–933 | 1332–1333 |
| bundle +600 ms, work before the SDK connects | 889–936 | 1316–1335 | 930–933 | 1332–1333 |
| 1500 ms of work in the connecting task | – | – | **110–118** | 516–519 |

Where the mark appeared, after the navigation (or after the window was asked for, on a first page): WebView2 431–468 ms (first page 517–620 ms) in every start that showed it, **whatever work ran meanwhile**; WebKitGTK 406–417 ms (first page 469–474 ms) when nothing blocked the page then, and **810–814 ms, the end of the work, when 600 ms of work covered that moment** (1584–1588 ms after 1500 ms of work on a first page).

**What the numbers say about blocked rendering:**
- **WebView2 animates the mark on its compositor.** Once a frame has started the entrance animation, the mark appears on time through any later long task, in whichever task the work runs and whether or not a frame was requested before it. A long task from the bundle's start that holds the page's first frame keeps the animation from starting at all: 0 mark frames, and the SDK, seeing no start time, removes the splash at once. The first-frame hold `docs/evidence/theme-startup` measured (about 550 ms) did not occur in these test windows: the first page's `first-paint` came 64–92 ms after its navigation.
- **WebKitGTK here draws the mark only in the page's own frames.** Counted from the animation's timeline alone, a mark due during a long task was on screen 111–126 ms instead of 400, in every placement of the work: **a single frame request at the bundle's start does not fix the count**, since the request cannot be served before the task ends either. The spike's rule (the first frame after the bundle start, when it came more than 100 ms late) did not trigger when `ready()` ran before that frame's callback, and its callback's time stamp on Chromium is the time the frame began (11–37 ms for a callback that ran after 1.5 s of work), which hid the block entirely.
- **As built,** the SDK keeps requesting frames from `connectSpar` while the page has a splash, until the first frame at or after the mark's appearance, and counts the minimum from that frame, or from now when none has rendered since the mark was due. **Every start on both engines kept the mark on screen at least 512 ms** (400 ms plus the 150 ms fade-out, at the sampling's resolution), 700 ms or more on WebView2. The cost: when the bundle starts after the mark was due (bundle +600 ms or later), or after a long task on WebView2, the mark stays up to the minimum longer than it needed (about 1300 ms against 900).
- **A rejected variant** ([`frames-webview2-timeline-shortcut.txt`](m1d-page-startup/frames-webview2-timeline-shortcut.txt), [`frames-webkitgtk-timeline-shortcut.txt`](m1d-page-startup/frames-webkitgtk-timeline-shortcut.txt)): counting a mark already due when `connectSpar` read `document.timeline.currentTime` as drawn at its appearance removed that cost on both engines, but flashed the mark for 111–116 ms on WebKitGTK after 600 ms of work before the SDK connected: the timeline reads the current time inside a long task, not the last rendering update.

## Runs

The final code, observed:
- **Windows:** `dotnet build Spar.slnx -c Debug`, 0 warnings, 0 errors; the CoreCLR tests without the window and black-box traits 1,150 (1,127 passed, 23 skipped, 0 failed), `AppModel.Tests` 11 of 11 among them; `Spar.WindowTests` 220 (118 passed, 102 skipped: the 22 explicit frame tests and the 80 skips of before); `StartupFrameTests` with `--explicit on` 22 of 22 and `StartupTests` 6 of 6; `Notebook.EndToEndTests` 27 (23 passed, 4 skipped); `Spar.BuildTests` 6 of 6 (the production boundary included); `Spar.PackageTests` 18 of 18 (run after the SDK's `npm run build`, nothing edited meanwhile); the SDK's `npm run check` and 330 of 330 tests; `tests/AppModel` `npm run check`; `dotnet run eng/check-size.cs`: 585 files, 0 errors, 0 warnings.
- **Linux (WSL, `wsl.sh sync client appmodel build test`, then `window`):** the CoreCLR tests without the window and black-box traits 1,150 (1,109 passed, 41 skipped, 0 failed); `Spar.WindowTests` 220 (170 passed, 50 skipped, 0 failed), 0 processes left; `StartupFrameTests` with `--explicit on` 22 of 22 and `StartupTests` 6 of 6 (a run of their own).
- **Not run for this step:** the NativeAOT lanes (`windowaot`, `Spar.Tests` published), the Notebook's Linux black box, `Spar.PackageTests` and `Spar.BuildTests` on Linux, Windows Sandbox, CI.

## Not verified

- On-screen windows on the user's monitor (the plan's table), a real light Windows theme, other DPI scales and several monitors; Windows 10.
- WebKitGTK on a real desktop: with a GPU, a compositor and Wayland, where WebKit may animate on its own compositor thread as `docs/evidence/theme-startup` saw; the SDK's rule then costs up to the minimum, as on WebView2.
- The first-frame hold of WebView2's first navigation that `docs/evidence/theme-startup` measured: not reproduced in test windows, so not measured with the final SDK.
- A real slow download of the bundle: the probe's slow bundle is a wait inside its module, with the page's thread free, as a download leaves it.
- Reduced motion on either engine (the style's `step-start` is unit-tested only), and the fade-out's frames.
- `setColorScheme("system")` following a live system change afterwards, and the Settings portal on Linux (none in WSL); loopback's `setColorScheme` in a real browser (unit-tested through the bridge).
- The Debug development server's nonce on the two styles in a real Debug window (Vite's order read from its source; the window tests run the packed Test variant).
