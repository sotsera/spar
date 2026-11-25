# Windows custom title bar spike: results

> A spike. Its code was deleted with `spikes/` at the start of M2; it is in git history at [`spikes/windows-titlebar/`](https://github.com/sotsera/spar/tree/24f5db0942833047c81fa692908410596f3b8960/spikes/windows-titlebar) (commit `24f5db0`), and this record and its `results/` moved here. Paths under `spikes/` below name that history.

Recorded 2026-11-14 on the development machine: Windows 11 Pro 10.0.26320, two 1920×1080 monitors, both at 96 DPI (100 %), system theme dark (nothing in the system settings was changed). SDK 11.0.100, WebView2 Runtime 157.0.4341.46, SDK headers 1.0.4314.35. Real input ran in Windows Sandbox: Windows 11 Enterprise Insider 26100.9876, one 1520×823 display at 96 DPI, light theme, the same WebView2 Runtime files (157.0.4341.46).

**Question.** Spar apps must be able to draw their own title bar in the page (app menu, search, tabs, buttons) while keeping dependable Windows window behaviour. Which design should the v7 host use: which controller, which frame technique, and which caption buttons by default?

## Summary and recommendation

**Recommended design for M1:**

- **Controller: windowed** (`ICoreWebView2Controller`), as today. Visual hosting worked in every real-input check too, but costs input forwarding, DirectComposition and a host-drawn system menu, and leaves drag and drop, touch, pen and the mouse wheel unimplemented and untested. Keep it as a documented fallback, not the M1 path.
- **Frame:** keep `WS_OVERLAPPEDWINDOW` (with `WS_CAPTION`), and in `WM_NCCALCSIZE` remove only the caption and the top border: call `DefWindowProc`, then put `rgrc[0].top` back to its original value, plus `SM_CYSIZEFRAME + SM_CXPADDEDBORDER` when maximized. No frame extension (`DwmExtendFrameIntoClientArea` margins 0) for page-drawn buttons.
  - Left, right and bottom resize borders stay real non-client area. Rounded corners and the 1 px border stayed.
  - Snap, Snap Layouts, `Alt+Space`, `Alt+F4`, `Win+Up/Down/Left/Right`, double-click to maximize, and the system menu all worked with real input.
  - Maximized, the client area was exactly the work area (`[0,0,1520,775]`, window `[-8,-8,1528,783]`) in every configuration.
- **Caption buttons: drawn by the page by default.** The host answers their hit test (`HTMINBUTTON`, `HTMAXBUTTON`, `HTCLOSE`) from a cache of the page's layout, so the Snap Layouts flyout appears on hover.
  - With windowed hosting this needs one host-owned child window above the WebView, the **sink**: `WS_EX_TRANSPARENT`, clipped by `SetWindowRgn` to the top resize band and the three buttons. It returns the host's hit code and forwards the non-client mouse messages to the top-level window.
  - Windows' own buttons (DWM) are not a good default: when the window is maximized they stop working (the earlier version's bug, reproduced here). UI Automation does not expose them. They show DWM's colour for about 350 ms at a dark start, and they add 17 µs per hit test.
- **The page:** `IsNonClientRegionSupportEnabled = TRUE` (documented, `ICoreWebView2Settings9`), with `app-region: drag` on the bar and `no-drag` on its controls. WebView2 then provides drag, double-click and the system menu on right click by itself. The page reports its layout to the host: bar height, control rectangles and button rectangles, in CSS pixels, through the bridge whenever it changes. Hit testing reads only that cache.
- **Hit-test cost:** p50 0.1–0.5 µs and p99 under 4 µs on the host's own path. It was unchanged while the page's main thread was busy for 1.5 s. Nothing waits for script or the backend.
- **Theme:** with page buttons, start-up and live switching gave **0 wrong-colour frames** in both themes, with both controllers.
- **Cost:**
  - +4 KB AOT exe for the custom frame, page buttons and sink, and +15 KB more for visual hosting. Start-up time unchanged (median `ui-ready` 389 / 393 / 392 ms).
  - About 170 lines of host code for the windowed design (frame 52, button hit testing and relay 59, sink 61), against about 90 more for visual hosting, before drag and drop, pointer input and the wheel.

**Open points for the user:**

- The custom-frame window still has the Win32 title bar in UI Automation: `TitleBar` with "System", "Minimize", "Maximize" and "Close", all with empty rectangles. This happens with both controllers, and also without `WS_CAPTION`. Removing those entries was not solved here.
- Windowed hosting adds 14 unnamed `Pane` entries to the UI Automation tree: WebView2's drag-region windows, one per rectangle of this bar. The sink adds one more.
- Not tested: other DPI scales and moving between monitors with different scales, because every display here is at 96 DPI. The host keeps the cache in CSS pixels and must scale it with the window's current DPI.

## Method

| Path | Contents |
|---|---|
| `Titlebar.Desktop/` | NativeAOT host (`win-x64`, 0 warnings, no suppressions). Win32 window; WebView2 through raw vtable calls (slots read from `WebView2.h` by `tools/vtable.mjs`) and `[GeneratedComClass]` callbacks; page embedded and served on `https://app.localhost` with a strict CSP. Switches: `--hosting windowed\|visual`, `--frame standard\|custom`, `--buttons page\|system`, `--sink none\|transparent\|direct`, `--nc on\|off`, `--capdrag`, `--ncr forward\|default\|host`, `--theme`, `--mode probe\|live\|drive`, `--capture <ms>`, `--hold <ms>`, `--no-caption-style`, `--browser-folder`. `TitlebarVariant=standard\|windowed` compiles features out for the size measurement. |
| `Titlebar.Desktop/page/` | Static page: bar with menu button, title, two tabs, search, a drag spacer and three caption buttons with accessible names. It posts `layout`, `ready`, `command`, input and scheme events. |
| `run-probes.sh` | Host-safe hit-test probes (10 configurations). |
| `run-uia.sh`, `tools/uia-dump.ps1` | UI Automation trees through the managed client (misleading, see problems). `Titlebar.Desktop.exe --uia-dump <hwnd> <file> [raw]` reads them through the native UIA client. |
| `run-theme.sh` | Start-up and live-switch frame captures. |
| `run-startup.sh` | Start-up time, 10 alternating rounds. |
| `sandbox/` | `drive.sh` (host side: stages the exe under a new name, waits until the guest sees it, runs `run-guest.ps1`), `uia-guest.ps1`, `probe-session.ps1`, `find-client.ps1`. |

Output is in `artifacts/windows-titlebar/` (`probes/`, `uia/`, `theme/`, `startup/`, `sandbox/out/drive/` with JSON-lines logs and PNG screenshots). WebView2 profiles were under `artifacts/webview2-profiles/windows-titlebar*/`, passed explicitly, and deleted at the end. Before deleting them, I checked that no spike or `msedgewebview2` process was left.

**On the host desktop** nothing moved the cursor or sent input:

- Every window was placed left of the whole virtual screen and shown with `SW_SHOWNOACTIVATE`.
- Probes are `WM_NCHITTEST` messages to the spike's own windows (`SendMessage` and `SendMessageTimeout`, plus `WindowFromPoint` to find which window a point routes to).
- Captures are `PrintWindow(PW_RENDERFULLCONTENT)` of the spike window, as in `spikes/theme-startup`.

**Real input ran in Windows Sandbox:**

- The guest's user session exists only after `wsb connect`. The client window was minimized at once (about 1 s on screen), which gives activation back.
- With the client minimized, the guest kept rendering and accepted `SendInput`. Screenshots were taken inside the guest with `BitBlt` of the screen.
- `--mode drive` refuses to run unless the user is `WDAGUtilityAccount`.
- The drive sequence:
  1. Drag the bar's empty part by (120, 60).
  2. Click the menu, a tab and the search box, and type "abc".
  3. Right-click the bar.
  4. Double-click the bar to maximize, and again to restore.
  5. Probe hit tests and click controls while maximized.
  6. Click the maximize button and then the restore button.
  7. Drag the top edge up by 40.
  8. Hover over the maximize button for 1.8 s, comparing the screen below it before and during the hover.
  9. Click minimize.
  10. Press `Win+Up`, `Win+Left`, `Win+Right` and `Win+Down`, then `Alt+Space`.
  11. Click close, or press `Alt+F4`.

## Results per question

### 1. The documented WebView2 routes

Feature detection on Runtime 157: `ICoreWebView2Settings9` present, `ICoreWebView2CompositionController4` present.

**`IsNonClientRegionSupportEnabled` + CSS `app-region`** ([`ICoreWebView2Settings9`](https://learn.microsoft.com/en-us/microsoft-edge/webview2/reference/win32/icorewebview2settings9), SDK 1.0.2420.47). It takes effect at the next navigation and defaults to `FALSE`. Documented behaviour: drag regions act as the title bar (drag, system menu on right click, maximize and restore on double click).

- **Windowed controller:** works without host code. WebView2 creates one transparent child window per drag rectangle (`Chrome_WidgetWin_0`, `WS_EX_TRANSPARENT`, in the host process on the UI thread; 14 for this bar).
  - These windows forward `WM_NCHITTEST` to the host's top-level window. They return its resize codes (`HTTOP`, `HTTOPLEFT`) unchanged, but turn every other code into `HTCAPTION`. So the host's `HTMAXBUTTON` is lost inside drag regions, and Snap Layouts cannot come from them.
  - This forwarding is observed behaviour, not documented.
  - With the setting off, the page covered everything (`HTCLIENT`) and the host's hit test was never reached.
- **Visual hosting:** the same CSS feeds `GetNonClientRegionAtPoint`.
- CSS supports only `drag` and `no-drag`: `CSS.supports('app-region', v)` was false for `minimize`, `maximize`, `close` and `caption`.

**Composition non-client API** ([`ICoreWebView2CompositionController4`](https://learn.microsoft.com/en-us/microsoft-edge/webview2/reference/win32/icorewebview2compositioncontroller4)):

- `GetNonClientRegionAtPoint` returns `CAPTION` (2) or `CLIENT` (1) from the CSS.
- The enum has `MINIMIZE` (8), `MAXIMIZE` (9) and `CLOSE` (20), but nothing on this runtime marks them.
- `NonClientRegionChanged` reported the caption rectangles (14). `QueryNonClientRegion` returned them.
- The API exists only for the composition controller.
- Cost: p50 0.3 µs, p99 0.4–3.3 µs, max 8–40 µs (n = 2,221). It did not change while the renderer was blocked for 1.5 s: the answer comes from a cache in the host process.
- The documented hint, forwarding `WM_NCRBUTTONDOWN/UP` to `SendMouseInput`, did **not** show the system menu on Runtime 157. `DefWindowProc` did not either: no `WM_CONTEXTMENU` was generated. The host showing `GetSystemMenu` with `TrackPopupMenu` worked.

**Window Controls Overlay** (WebView2 drawing caption buttons): only a 2024 draft spec on a branch of the WebView2Feedback repository (`IsWindowControlsOverlayEnabled`). It is not in SDK 1.0.4314.35, not in the release notes from 1.0.3800 to 1.0.4365-prerelease, and not testable here.

**Windowed or visual hosting (cost and benefit for M1):**

| | Windowed + sink | Visual hosting |
|---|---|---|
| Real-input checks passed | all | all, with the system menu drawn by the host |
| Snap Layouts on the page's maximize button | yes | yes |
| Extra native windows | 14 drag-region windows + sink; 15 unnamed UIA panes | 1 zero-size focus window; none in UIA |
| Top resize band over controls | yes (sink) | yes |
| Host code beyond the windowed baseline | sink, about 61 lines | about 90 lines: DirectComposition device, target and visual; mouse forwarding with capture and leave tracking; cursor; system menu; UIA hook (not needed, see question 7) |
| Not done, would be needed | none | drag and drop (`IDropTarget` → `DragEnter/Over/Drop`), touch and pen (`SendPointerInput`), wheel check, IME check, DPI (`RasterizationScale`) |
| AOT size | +4 KB | +15 KB more |
| Start-up | 389 ms | 392 ms (median `ui-ready`) |

Typing into the page's search box worked in visual hosting with no extra code. WebView2 creates its own zero-size focus window.

### 2. The frame without a caption

`WM_NCCALCSIZE` as described in the recommendation, applied before the first paint with `SWP_FRAMECHANGED`. The window keeps `WS_OVERLAPPEDWINDOW`.

- **Resize:** left, right and bottom: `HTLEFT`, `HTRIGHT` and `HTBOTTOM` from `DefWindowProc`.
- **Top edge (restored):** the host returns `HTTOP`, `HTTOPLEFT` and `HTTOPRIGHT` in a band of `SM_CYSIZEFRAME + SM_CXPADDEDBORDER` = 8 px at 96 DPI. A real drag of −40 px moved the top by −40 and grew the height by 40, in every custom configuration.
- **Top edge without the sink (windowed):** the band works only over drag regions, through WebView2's forwarding. Over the menu button and the caption buttons the point went to `Chrome_RenderWidgetHostHWND` (`HTCLIENT`). With the sink or with visual hosting it works everywhere.
- **Maximized:** no band; the client area equals the monitor's work area.
- **Shadow and corners:** Windows 11's rounded corners and 1 px border were visible on the custom frame, the same as on the standard frame. No drop shadow was visible in the Sandbox screenshots for **either** frame, so the shadow could not be compared.
- **`DwmExtendFrameIntoClientArea`:** not needed with page buttons. With DWM buttons the top margin must be the caption height, and GDI-painted pixels in that band show DWM's frame colour (question 6).

### 3. Caption buttons

**(a) Windows' own buttons, without clipping:** extend the frame by the caption height, paint black (alpha 0) under `DWMWA_CAPTION_BUTTON_BOUNDS`, make the page's bar transparent there, and call `DwmDefWindowProc` first in the window procedure.

- **Visual hosting:** the top-level window gets all hit tests, so `DwmDefWindowProc` answers directly.
- **Windowed:** needs the sink. Without it, WebView2's drag windows turn `HTMAXBUTTON` into `HTCAPTION`, so clicks on the visible buttons did nothing: maximize, minimize and close all failed.
- **Restored:** clicks worked and the Snap Layouts flyout appeared (both controllers).
- **Maximized: broken.** `DwmDefWindowProc` returned nothing for the button points, so the host answered `HTCAPTION`, and a real click on the restore button did nothing. This happened with both controllers: it is the earlier version's bug. The known workaround is the host's own hit test from `WM_GETTITLEBARINFOEX` rectangles; it was not tried.
- **Other drawbacks:**
  - DWM's colours do not follow the page;
  - the dark start shows DWM's frame colour (question 6);
  - `DwmDefWindowProc` costs p50 17 µs, p99 50–380 µs, max 0.7 ms per hit test;
  - UI Automation does not expose the visible buttons (question 7).

**(b) Buttons drawn by the page (recommended default):** plain HTML buttons with accessible names. The host's hit test returns `HTMINBUTTON`, `HTMAXBUTTON` and `HTCLOSE` from the cached rectangles.

- The host handles `WM_NCLBUTTONDOWN/UP` itself, so `DefWindowProc` never runs its legacy button tracking. A release on the pressed button posts `SC_MINIMIZE`, `SC_MAXIMIZE`, `SC_RESTORE` or `SC_CLOSE`.
- Hover and pressed state go to the page as a bridge message (`WM_NCMOUSEMOVE`, `TrackMouseEvent(TME_NONCLIENT)`).
- **Snap Layouts:** the flyout appeared on hover (59.8 % of the area below the button changed, screenshot `*-04-snaphover.png`) with visual hosting, and with windowed hosting plus the sink (`--sink direct`).
- **Windowed without the sink:** the hit test reached the page (`HTCLIENT`), so no flyout. The buttons still worked as page buttons that send a command.
- **Other windowed variants that failed:**
  - a sink that returns `HTTRANSPARENT`: the point still routed to the WebView, because `HTTRANSPARENT` passes only to windows of the same thread;
  - caption buttons marked `app-region: drag` (`--capdrag`): `HTCAPTION` and no flyout, and each click produced both `SC_MOVE` and a page click.
- **Sink styles:** `WS_EX_NOREDIRECTIONBITMAP` hid the WebView's buttons underneath (they disappeared from the screen). `WS_EX_TRANSPARENT` keeps them visible and still receives hit tests.

### 4. Hit testing

Real input in the Sandbox, page buttons:

| Check | windowed, no sink | windowed + sink | visual |
|---|---|---|---|
| Drag the empty bar by (120, 60) | pass | pass | pass |
| Menu, tab, search click and typing; window does not move | pass | pass | pass |
| Right click on the bar: system menu | pass (WebView2) | pass | pass only with the host's menu |
| Double click: maximize, then restore | pass | pass | pass |
| Maximized: content inside the work area | pass | pass | pass |
| Maximized: controls clickable | pass | pass | pass |
| Maximize, then restore, by button | pass (page command) | pass | pass |
| Top edge resize (restored) | pass over drag regions only | pass | pass |
| Snap Layouts on hover | no | **yes** | **yes** |
| Minimize button | pass | pass | pass |

**Cost on the UI thread** (`WM_NCHITTEST` sent directly, n = 2,000 per point):

| Configuration | p50 | p99 | max |
|---|---|---|---|
| windowed, page buttons (± sink) | 0.1–0.2 µs | 0.2–0.3 µs | 4–9 µs |
| visual, page buttons | 0.1–0.5 µs | 0.2–0.6 µs | 2–9 µs |
| any configuration with DWM buttons | 17–19 µs | 51–128 µs | 0.2–0.5 ms |

With the renderer busy for 1.5 s, page buttons stayed at p50 0.2–0.5 µs and p99 3–14 µs. The answer never depends on the page or the backend at hit time. The cache is only as fresh as the page's last `layout` message, which the page sends from a `ResizeObserver`.

### 5. DPI and monitors

- The process is per-monitor v2 aware through its manifest.
- Every display available reported 96 DPI: both host monitors and the Sandbox.
- **Not tested:** scales above 100 %, moving between monitors with different scales, and `WM_DPICHANGED` at run time. The handler (apply the suggested rectangle, re-read metrics with `GetSystemMetricsForDpi`) exists but was not exercised.
- Bar height: 32 DIP, which is 32 px at 96 DPI. DWM's caption-button bounds were 146 × 30 px at 96 DPI.
- **Design point for M1:** keep the page's rectangles in CSS pixels in the cache and scale them with `GetDpiForWindow` at hit-test time. Otherwise the cache is wrong between a DPI change and the page's next layout message.

### 6. Theme

Measured with `PrintWindow(PW_RENDERFULLCONTENT)` about every 16 ms, as in `spikes/theme-startup`. Each frame's dominant colour is classified separately for the title-bar band and the body. 4 runs per configuration and theme (1 cold, 3 warm). Theme layers from `theme-startup`: class brush + `WM_ERASEBKGND`, `DWMWA_USE_IMMERSIVE_DARK_MODE`, `PreferredColorScheme`, and a transparent default background through `ControllerOptions3`.

| Configuration | Frames | Wrong frames after the first paint |
|---|---|---|
| standard frame, dark / light | 590 / 594 | 0 / 0 |
| windowed, page buttons, dark / light | 596 / 593 | 0 / 0 |
| visual, page buttons, dark / light | 590 / 592 | 0 / 0 |
| windowed, DWM buttons, dark | 586 | **76** (19 per run, about 350 ms) |
| windowed, DWM buttons, light | 588 | 0 |
| live, 8 switches, windowed page / visual page | 419 / 417 | 0 / 0 (no frame with a mixed theme) |
| live, 8 switches, DWM buttons | 415 | 19, all at start-up; the switches were clean |

- The 0–4 black frames per configuration before the first `WM_ERASEBKGND` are the capture artifact already known from `theme-startup`. They were excluded using the new `first-paint` mark.
- With DWM buttons, the extended frame shows DWM's caption colour (`#4a4a4a`) under GDI-painted pixels until the page paints its bar (GDI writes alpha 0). In light mode the frame colour happened to match white.

### 7. Accessibility and keyboard

The native UIA client (`--uia-dump`; the API screen readers use) shows:

- **Every custom-frame configuration** still has `TitleBar` → `MenuBar "System"` → `MenuItem "System"`, plus `Button "Minimize"`, `"Maximize"` and `"Close"`, all with empty rectangles `[0,0,0,0]`. These are the extra native title-bar entries. They stayed with `WS_CAPTION` removed (`--no-caption-style`).
- **Page:** the page's buttons appear in the document as `Button "Minimize"`, `"Maximize"` and `"Close"`, with the menu button, tab items and the search edit, with both controllers.
- **Windowed:** adds 14 unnamed `Pane` entries (WebView2's drag windows) and one for the sink. A real setup will have as many as the bar has drag rectangles.
- **Visual hosting:** the page is fully exposed without any host provider. The `WM_GETOBJECT` hook returning `get_AutomationProvider` changed nothing.
- **DWM buttons (mode a):** no visible element exposes them, only the empty-rectangle entries above.

**Keyboard (real input, every configuration including the standard frame):**

- `Alt+Space` opened the system menu.
- `Alt+F4` closed the window.
- `Win+Up` maximized.
- `Win+Left` snapped to the left half (frame `[0,0,760,775]`). `Win+Right` from there restored, as Windows does.
- `Win+Down` from restored minimized.
- Not tested: Narrator's speech, and Tab order from the host into the page.

### 8. Cost

| | AOT exe (bytes) | Delta |
|---|---|---|
| standard frame, windowed (`TitlebarVariant=standard`) | 2,772,480 | |
| + custom frame, page-button hit testing, sink (`windowed`) | 2,776,576 | +4,096 |
| + visual hosting (full) | 2,791,936 | +15,360 |

All three include the same probe and drive scaffolding. The features are removed through `const bool` switches, so the code becomes unreachable for ILC.

Start-up (10 alternating warm rounds, offscreen, median, ms after `Main`):

| | Window visible | Controller ready | `ui-ready` | `ui-ready` range |
|---|---|---|---|---|
| standard | 9.6 | 212 | 393 | 372–419 |
| custom + sink | 10.3 | 210 | 389 | 377–401 |
| visual | 10.3 | 221 | 392 | 380–403 |

Host code, non-blank lines:

| Part | Lines |
|---|---|
| Frame (`WM_NCCALCSIZE`, band, resize hit test, messages) | 52 |
| Page-button hit test, non-client button tracking, hover relay, commands | 59 |
| Sink | 61 |
| Visual hosting | about 90, without drag and drop and pointer input |

## Problems met

These are candidate pitfalls for `docs/pitfalls.md` (not edited here):

- **WebView2's windowed drag regions are child windows that rewrite hit codes.** They forward `WM_NCHITTEST` to the top-level window but return `HTCAPTION` for anything except resize codes. Caption-button codes never survive them, so a host-owned window must lie above the WebView where the host needs those codes.
- **`HTTRANSPARENT` from a child window passes only to windows of the same thread.** Below it is the WebView's window from another process, so the parent never received the hit test.
- **A `WS_EX_NOREDIRECTIONBITMAP` child window above the windowed WebView hides the WebView under it.** `WS_EX_TRANSPARENT` does not, and still receives hit tests.
- **`DwmDefWindowProc` does not report the caption buttons when the window is maximized** with a custom `WM_NCCALCSIZE`. Clicks on the visible restore button did nothing (both controllers).
- **Visual hosting: right click on a drag region showed no system menu** with the documented `SendMouseInput` forwarding, nor with `DefWindowProc` (no `WM_CONTEXTMENU` came). The host must show the system menu itself.
- **GDI paints alpha 0 in an extended frame:** a dark brush showed DWM's `#4a4a4a` there until the page painted.
- **The managed UI Automation client (`System.Windows.Automation`) is misleading here.** It showed no `TitleBar` even for a standard window, and none of the page in visual hosting. The native client shows both. Use the native client for accessibility checks.
- **Windows Sandbox:**
  - `wsb exec -r ExistingLogin` fails with 0x80070520 until a session exists. A `wsb connect`, with its window minimized at once, creates one, and the guest keeps rendering and accepting `SendInput` while minimized.
  - The guest's WebView2 registration (157.0.4341.42) did not match the files (157.0.4341.46). `CreateCoreWebView2EnvironmentWithOptions` failed with 0x80070002 until the runtime folder was passed explicitly.
  - Files added to a mapped folder became visible in the guest only after a few seconds. A file replaced in place was seen as "not a valid application". Stage new file names and poll for them.
  - `Start-Process -WindowStyle Hidden` makes the app's first `ShowWindow` hide the window.
  - `wsb exec` with `powershell -File` cannot pass an array parameter; join the configurations into one string.
- **Arrow keys need `KEYEVENTF_EXTENDEDKEY`** for the `Win+arrow` shortcuts sent with `SendInput`.

## Not tested

- DPI above 100 %, mixed-DPI monitors, `WM_DPICHANGED` at run time. Only 96 DPI displays exist here.
- Visual hosting:
  - drag and drop;
  - touch and pen (`SendPointerInput`);
  - the mouse wheel;
  - IME;
  - `RasterizationScale` handling.
- The workaround for DWM buttons when maximized (`WM_GETTITLEBARINFOEX` rectangles).
- Removing the empty-rectangle native `TitleBar` entries from UI Automation.
- Narrator or any other screen reader's actual output.
- The drop shadow, which was not visible for the standard frame either in the Sandbox session.
- Windows 10, older WebView2 runtimes (the minimum runtime for `ICoreWebView2Settings9` is 1.0.2420.47), WebView2 prerelease APIs.
- The host's light system theme and the "accent colour on title bars" setting, which would change DWM's caption colour in mode (a).
- Maximizing on a secondary monitor, an auto-hidden taskbar, Snap Layouts through `Win+Z`.
- Windows.Graphics.Capture (the second capture method of `theme-startup`); only `PrintWindow` was used.

## Reproduce

From the repository root, Git Bash:

```bash
dotnet publish spikes/windows-titlebar/Titlebar.Desktop -c Release      # 0 warnings
spikes/windows-titlebar/run-probes.sh                                   # hit-test probes
spikes/windows-titlebar/run-uia.sh                                      # managed UIA dumps
spikes/windows-titlebar/run-theme.sh 3                                  # frame captures
spikes/windows-titlebar/run-startup.sh 10                               # start-up times
for v in standard windowed full; do
  dotnet publish spikes/windows-titlebar/Titlebar.Desktop -c Release -p:TitlebarVariant=$v -p:PublishDir=bin/variants/$v/
done
```

Native UIA tree of a held window: start the exe with `--hold 7000`, read the handle from `<label>.hold`, then run `Titlebar.Desktop.exe --uia-dump <hwnd> <file> [raw]`.

Windows Sandbox:

1. Start it with `artifacts/windows-titlebar/sandbox/config.xml` (`wsb start --id <id> --config "<xml>"`), adding a writable mapping of `artifacts/webview2-profiles/windows-titlebar-sandbox` to `C:\spar\profiles` with `wsb share`.
2. Run `wsb connect`, then `sandbox/find-client.ps1 -Minimize`.
3. Run, for example:

   ```bash
   spikes/windows-titlebar/sandbox/drive.sh <id> drive "visual-page --hosting visual --buttons page --ncr host"
   ```
