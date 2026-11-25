# Window-test automation on Linux: results

> A spike. Its code was deleted with `spikes/` at the start of M2; it is in git history at [`spikes/window-automation-linux/`](https://github.com/sotsera/spar/tree/24f5db0942833047c81fa692908410596f3b8960/spikes/window-automation-linux) (commit `24f5db0`), and this record and its `results/` moved here. Paths under `spikes/` below name that history.

Recorded 2026-11-18. Host: Windows 11 Pro 10.0.26320 (32 CPUs), WSL 2 with Ubuntu 24.04.5 (kernel 6.18.58.1), SDK 11.0.100. Linux side: WebKitGTK 2.52.6 (`libwebkitgtk-6.0-4`), GTK 4.14.5, bubblewrap 0.9.0, Xvfb 21.1.12 (unpacked, not installed), libXtst 1.2.3, at-spi2-core 2.52.0, Node 24.22, Python 3.12. Windows side: WebView2 Runtime 157.0.4341.60, as in `spikes/window-automation`. Microsoft.Playwright 1.64.1 for part 1.

Every Linux run used its own Xvfb (`:95`, `:97`, `:98`; never WSLg's `:0` or Wayland: `DISPLAY` and `WAYLAND_DISPLAY` were unset first and Xvfb listened on an abstract socket only), its own `dbus-run-session`, and `HOME` and the XDG folders under `/var/tmp/spar-linux/window-automation-linux/work/artifacts/linux-homes/<run>/`, with one exception explained under [Problems met](#problems-met) (`XDG_RUNTIME_DIR` in `/dev/shm/spar-wal-<run>`, removed after each run). No root, no package installed: Xvfb, `xkbcomp`, the X utilities and the 4.1 WebDriver package were fetched with `apt-get download` (8.3 MB) and unpacked under `/var/tmp/spar-linux/window-automation-linux/tools/`. No window appeared on the Windows desktop: nothing ran on WSLg. Each run counted its own processes before and after (by their `XDG_RUNTIME_DIR`); none was left. Another agent's spike was running in the same distribution at the same time (its own Xvfb `:91`); its processes were not touched, and they may have added some noise to the timings.

**Questions:**
1. Can Microsoft's browser automation library (Playwright) drive Spar's Linux window (WebKitGTK 6.0)?
2. How hard is the Linux backend of Spar's own window-test API (the Windows spike's Option B, with the library's method names), and what does it cost?

## Summary

| # | Question | Result |
|---|---|---|
| 1 | The library on Spar's Linux window | **Not possible.** See [part 1](#part-1-microsofts-library-on-the-linux-window): no route attaches the library to a WebKitGTK view. |
| 2 | Spar's API on Linux: the same 28-step journey | **Passes**: 60 of 60 (NativeAOT, XTEST), 20 of 20 (JIT), 20 of 20 (fill through the editor), 10 of 10 under a strict CSP, 15 + 15 in two processes at once. 0 failures in 140 XTEST journeys. |
| 3 | Cost per test (p50 / p95) | **1,270 / 1,321 ms** (NativeAOT, XTEST, 60 runs): window + view + ephemeral session 1.0 ms, navigation 64 ms, journey 1,203 ms, close 1.0 ms. Windows Option B on the same machine: 1,613 / 1,682 ms (`queryAXTree`), 1,497 / 1,551 ms (with the shared engine). |
| 4 | Per action | Role lookup **0.3–0.4 ms**, assertion that already holds 0.4 ms, `ClickAsync` opening a menu plus Escape 28.9 ms (Windows B: 58.1), a key 0.9 ms, 12 typed characters 28.5 ms, viewport screenshot 5.8 ms. Each click and key is acknowledged by the page before the call returns. |
| 5 | Per process | 72 ms from process start to GTK + web context ready; the first window adds about 120 ms. Test process 188 MB RSS (59 MB anonymous: WebKit's UI side and GTK live in it), web process 200 MB, network process 45 MB. NativeAOT executable 3,478,080 bytes. |
| 6 | Trusted input | **XTEST works without any window manager**: `isTrusted` true on pointer, mouse, click, key and input events; user activation granted (WebKit's popup blocker lets `window.open` through). Clicks need no focus; keys go to the X input focus, which GTK gives the window when it is presented. The CSS → screen mapping was exact (a click at (300, 200) arrived at clientX/Y 300, 200). |
| 7 | Untrusted script input | Events have `isTrusted` false, but **calls through the WebKit API count as a user gesture** (activation true, popups allowed). No hover, no default key actions (typing, Tab). The journey needed pointer moves before each synthetic press (Radix Select failed 60 of 60 without them) and the editor for fills; then it passed (numbers below). |
| 8 | Accessible names | **A page-script engine is viable and fast**: 1.3–1.5 ms for the page's whole role/name tree, 0.3 ms per lookup. Against Chromium's own tree (Windows, `getFullAXTree`), **39 of 39** element nodes had the same role and the same name. AT-SPI works too (47 nodes walked in 44–46 ms, a Collection query in 2.4 ms, screen extents usable for XTEST) but exposes WebKit's platform roles and names (for example "Customer name Ada Lovelace", "combo box" for a menu button), not ARIA's, and cannot hand back a DOM element. |
| 9 | Isolation | An **ephemeral network session per test** isolates `localStorage` and IndexedDB, consecutive (0 of 10 found anything) and simultaneous (a session each: nothing shared; one shared session: shared, as the control). Creating one costs 0.3–0.6 ms; close 1 ms. One web process per view, one network process for all. Cookies are not stored on `spar://` at all. |
| 10 | Flakiness | **0 failures** in 140 XTEST journeys; 0 input that did not land (every click and key was acknowledged by the page, trusted, on its target). |
| 11 | NativeAOT | **0 warnings** (trim and AOT analyzers on, ILC publish), no suppressions, on Linux and for the Windows copy. |
| 12 | Shared engine | **One engine file serves both platforms.** On Windows it replaces `Accessibility.queryAXTree`: role lookup 13.0 → 0.9 ms, assertion 17.8 → 1.2 ms, click + Escape 58.1 → 31.6 ms, per test 1,613 → 1,497 ms (60 runs each, NativeAOT). |
| 13 | Effort | The Linux backend is about 1,000 lines plus the shared 503-line engine; about 15 minutes of agent time from the first source file to the first passing journey. Estimate for a Linux 1.0 on top of the shared engine: **12–20 developer-days** (below). |

**Recommendation:** keep Option B on both platforms, and make the page-script engine its shared core. Each backend then only carries the transport (one call into an isolated world), trusted input, screenshots and events. On Linux that is XTEST under Xvfb, as decided.

## Part 1: Microsoft's library on the Linux window

**Conclusion: not possible.** The library attaches only to Chromium over CDP, or launches its own patched WebKit; stock WebKitGTK offers neither. A WebDriver route exists only through the 4.1 package's driver, which the library does not speak, and even that driver cannot send input to a 6.0 view. Every attempt is in `results/linux/part1.log` and `part1-4.log` (Xvfb `:95`, ports on 127.0.0.1 only).

| Route | What happened |
|---|---|
| `WEBKIT_INSPECTOR_SERVER=127.0.0.1:9227` (WebKit's remote inspector) | The app opened a TCP listener on the port, with or without developer extras. It speaks WebKit's own binary inspector-socket protocol (the one `WebKitWebDriver` and `inspector://` use), not HTTP: `GET /json/version` got no reply and a reset (`curl` exit 52). `Chromium.ConnectOverCDPAsync("http://…")`: `socket hang up (ECONNRESET)` while "retrieving websocket url". `Webkit.ConnectOverCDPAsync`: **`ArgumentException: Connecting over CDP is only supported in Chromium.`** (thrown by the library before any connection). `Webkit.ConnectAsync("ws://…")` (the library's own server protocol): `socket hang up`. |
| `WEBKIT_INSPECTOR_HTTP_SERVER=127.0.0.1:9228` (the inspector over HTTP) | Unlike the 4.1 run in Docker (`spikes/linux-desktop`), the 6.0 app did **not** hang: it served a "Remote inspector" page listing one target (`socket/1/1/WebPage`), and 404 for `/json`, `/json/list` and `/json/version`. `ConnectOverCDPAsync` stopped there: "Unexpected status 404 … This does not look like a DevTools server". On the WebSocket it speaks the **WebKit Inspector Protocol**, target-based: `Runtime.evaluate` sent to the page through `Target.sendMessageToTarget` returned "Orders spar://app/", and `Page.snapshotRect` returned a PNG. But there is **no `Input` domain, no `Accessibility` domain, no `Page.captureScreenshot`**, and no `Target.setAutoAttach`; it is not CDP, and an adapter could not supply trusted input. **Any local process could run script in the page this way, without developer extras** (as with WebView2's debugging port): the Production host must keep ignoring both variables (skill `spar-webkitgtk-hosting`). |
| The library's `Webkit` browser type, launching its own build | `install webkit` downloaded **webkit-2370: 293 MB unpacked** (`minibrowser-gtk` 142 MB, `minibrowser-wpe` 151 MB) plus ffmpeg 5.1 MB. It is the library's patched WebKit: its own `libwebkitgtk-6.0.so.4.20.0`, and a protocol with 33 domains including `Playwright`, `Input`, `Browser`, `Emulation`, `Dialog` and `Screencast`, which stock WebKitGTK does not have. `LaunchAsync` refused: "Host system is missing dependencies" (`libavif16`, `libevent-2.1-7t64`, `libflite1`, `libgstreamer-plugins-bad1.0-0`, and `libjxl`; its WebKit also links a `libbacktrace.so.0` that no Ubuntu 24.04 package provides). Installing them needs root (`install-deps`), which this spike does not use, so it was not run. Even running, it is the library's own browser, not Spar's window: at most a WebKit **Browser surface** (headless, a page served over loopback), at 293 MB per machine plus those packages in the CI image. |
| `LaunchAsync` with `ExecutablePath` = the system's WebKitGTK 6.0 MiniBrowser | The library starts it with `--inspector-pipe --headless --no-startup-window`; MiniBrowser: "Cannot parse arguments: Unknown option --inspector-pipe", and the library: `TargetClosedException` after 113 ms. The pipe transport and the `Playwright` domain exist only in the patched build. |
| `LaunchAsync` with `ExecutablePath` = Spar's harness | The same: the harness started, did not speak the pipe protocol, and the library gave up after 682 ms. No process was left. |
| WebDriver: `WebKitWebDriver` | Ubuntu 24.04 packages it only as `webkit2gtk-driver` (for 4.1); `apt-cache` finds no 6.0 driver package. The driver binary links only libsoup, not WebKit, so the unpacked 4.1 package can launch the 6.0 harness: with `webkit_web_context_set_automation_allowed`, an `automation-started` handler that answers `create-web-view` with a view built `is-controlled-by-automation` on the automation network session, and the view's `close` ending the process. **Worked:** new session (281 ms), navigate to `spar://app/` (160 ms), title, find element by CSS and XPath (2–68 ms), computed role and label ("button", "Actions": WebCore's ARIA view), screenshot (25 ms), delete session. **Failed:** element click, element send keys and the actions API all answered **`unsupported operation`** (as `spikes/linux-desktop` saw on 6.0); the page saw no event. |
| WebDriver BiDi | Asking for `webSocketUrl: true` made session creation fail ("Failed to match capabilities"): this driver has no BiDi. |
| The library over WebDriver | Not possible: the library speaks CDP (Chromium), its own pipe protocol (its WebKit and Firefox builds), and BiDi only for browsers it launches itself; it has no WebDriver client. Selenium has a WebKitGTK driver and could use classic WebDriver here, but it would get the same `unsupported operation` for input, needs the driver process and a TCP port, and is not used by Spar. |

Building a 6.0 `WebKitWebDriver` from WebKit's sources was not tried: it would still be an out-of-process driver on a TCP port, which decision "Trusted input in Linux desktop tests" set aside.

## Part 2: Spar's API on Linux

### What was built

All written for this spike, from the platform documentation and the installed headers; nothing copied from the earlier Linux spikes.

| Part | Lines | Role |
|---|---:|---|
| `Harness/Linux/Native.cs` | 441 | `[LibraryImport]` for GLib, GObject, GIO, libsoup, GTK 4, WebKitGTK 6.0, JavaScriptCore, Xlib, XTEST, libc |
| `Harness/Linux/Ui.cs` | 158 | The GTK thread: `gtk_init_check`, a `GMainLoop`, an idle-source queue, a GLib `SynchronizationContext` |
| `Harness/Linux/Host.cs` | 489 | The host as M1 builds it: the `spar` scheme (secure, CORS-enabled) serving the Windows spike's built page, a window and view per test on an ephemeral network session, acceleration off and `GSK_RENDERER=cairo` (decision "Software GPU default"), load before present; for tests: the engine as a document-start user script in the isolated world `spar-automation`, a main-world shim reporting console calls and page errors through a script message handler, `script-dialog` answered, `run-file-chooser` answered (armed files, or cancel), `create` counted and refused, `call_async_javascript_function`, `get_snapshot`, the view's screen geometry |
| `Harness/Linux/XInput.cs` | 288 | XTEST on the harness's own X connection: move, click, keys, text with Shift levels, spare keycodes remapped for characters the keymap lacks, the X focus |
| `Harness/Automation/` | 720 | The API: `WindowPage`, `Locator`, `Expect`, `Mouse`, `Keyboard`, the `IInput` interface with an XTEST backend and a script backend, a PNG encoder for crops |
| `engine/engine.js` | 503 | **Shared with the Windows copy.** Roles (WAI-ARIA 1.2, HTML-AAM), accessible names (Accessible Name 1.2), hidden-from-the-tree rules, label/text/test-id queries, chains with `nth`, strict mode, actionability (attached, visible, enabled, editable, scrolled into view, stable over a frame, hit test with shadow roots), element state, and an input watcher that confirms each input arrived, trusted and on target |
| `Harness/Journey.cs` | 47 | The 28 steps, call for call the Windows `OptionB/Journey.cs` |
| `Harness/Program.cs`, `Probes.cs`, `Coverage.cs`, `Common.cs` | 746 | Verbs `journey`, `actions`, `coverage`, `activation`, `isolation`, `tree`, `serve`, `webdriver-host` |
| `windows/` | | A copy of the Windows spike's Option B whose role queries can go through the shared engine; the Windows host (`Shared/`) is compiled from that spike unchanged |
| `probes/`, `wsl/` | | The AT-SPI reader, the tree comparison, the part-1 probes, the WSL scripts |

How a locator works on Linux: a locator is a chain of steps serialised as JSON. Each attempt is **one** `webkit_web_view_call_async_javascript_function` in the isolated world, which resolves the chain, applies strict mode and the checks, and answers with a point or a reason. A click is then three calls: actionability (which also arms the input watcher), the XTEST events, and "landed" (a promise that resolves one task after the `pointerup` or `keyup`). There are no element handles across calls: every attempt re-resolves, as the library's locators do.

### The journey (60 runs, NativeAOT, XTEST; `results/linux/j-aot-xtest.log`)

| | p50 | p95 |
|---|---:|---:|
| Window + view + ephemeral session | 1.0 | 1.2 |
| Navigate (load, then present; until `load-changed` FINISHED) | 64.1 | 71.9 |
| Journey (28 steps) | 1,203 | 1,243 |
| Close | 1.0 | 4.0 |
| **Per test** | **1,270** | **1,321** |

Other configurations: JIT 1,283 / 1,301 (20 runs); fills through the editor instead of key strokes 1,260 / 1,280 (20); strict CSP 1,279 / 1,414 (10); two processes at once, each with its own Xvfb, 1,289 and 1,295 at p50 (15 + 15). Script input: 60 of 60 at 1,232 / 1,271 ms per test (with pointer moves and fills through the editor; the first run, without moves and with synthetic key strokes, failed 60 of 60).

Per step (p50, ms), next to Option B on WebView2 run again on the same machine with `Accessibility.queryAXTree` and with the shared engine (the Windows copy, 60 runs each, NativeAOT; `results/windows/w-aot-journey-*.log`):

| Step | Linux (engine, XTEST) | Windows B (`queryAXTree`) | Windows B + engine |
|---|---:|---:|---:|
| 01 two frames | 23.9 | 33.9 | 35.7 |
| 02 expect heading | 65.9 | 17.8 | 3.5 |
| 03 fill (label) | 39.1 (12 key strokes) | 5.1 | 5.9 |
| 05 check Radix checkbox | 16.3 | 70.7 | 35.0 |
| 07 open Radix select | 29.1 | 58.8 | 40.9 |
| 10 open Radix menu | 17.9 | 58.0 | 35.4 |
| 12 expect status | 0.5 | 14.3 | 1.1 |
| 13 wait for the list (page timer 600 ms) | 529.8 | 264.9 | 568.5 |
| 16 press Enter | 3.5 | 18.1 | 2.4 |
| 21 cancel the animated dialog | 159.9 | 233.4 | 210.0 |
| 26 click link | 10.9 | 60.9 | 41.0 |
| 27 wait for URL | 2.1 | 2.2 | 2.3 |

- The XTEST series ran with the engine as it was before two later changes, neither of which the XTEST journey touches: file inputs mapped to `button`, and pointer moves before synthetic presses. The script-input series and `tree4` used the final engine; the Windows runs had the file-input change (they never dispatch synthetic presses).
- Step 13 waits for a 600 ms timer the page starts at load, so it is longer when the earlier steps are faster.
- Step 02 takes about 65 ms on Linux every run. The likely cause is that the first role query after the page mounts forces the first style computation and layout of the page (software rendering); this is an inference from the timings, not isolated.
- Retries over the 60 journeys: 1,142 "not stable" (19 per run: the dialog's 150 ms entry animation, one frame per attempt), 360 "count is not yet" (the async list and the filter), 18 "no element matches" (an element not there yet, such as the dialog right after its click).

### Per action (100 each, p50 / p95 ms; `results/linux/act-xtest.log`)

| Action | Linux | Windows B (`queryAXTree`) | Windows B + engine |
|---|---|---|---|
| One engine call that does no work | 0.3 / 0.8 | — | — |
| Evaluate `1+1` (main world) | 0.2 / 0.4 | 0.2 / 0.3 | — |
| Resolve by role | 0.4 / 0.6 | 13.0 / 14.1 | 0.9 / 2.0 |
| Resolve by role right after a DOM change | 0.3 / 3.7 | 16.8 / 17.6 | 0.6 / 1.3 |
| Resolve `listitem`s in a named list | 0.3 / 0.7 | 35.4 / 36.1 | 1.2 / 2.4 |
| Resolve by label / by text | 0.2 / 0.6, 0.3 / 0.4 | 0.5 / 0.9 (label) | — |
| Actionability with the stability frame | 9.6 / 16.2 | — | — |
| Click on an empty area (arm, XTEST, acknowledged) | 2.8 / 17.7 | — | — |
| Key press (arm, XTEST, acknowledged) | 0.9 / 4.6 | 0.5 / 0.7 | — |
| `ClickAsync` opening the menu, then Escape | 28.9 / 34.4 | 58.1 / 62.3 | 31.6 / 45.9 |
| Assertion that already holds | 0.4 / 0.7 | 17.8 / 18.6 | 1.2 / 3.2 |
| Type 12 characters (key strokes) | 28.5 / 38.2 | — | — |
| Fill 12 characters through the editor | 1.8 / 5.2 | — | — |
| Viewport screenshot (snapshot + PNG) / snapshot only | 5.8 / 8.6, 3.5 / 6.1 | 59.1 / 60.7 | — |
| The page's whole role/name tree (engine) | 1.5 / 3.6 | — | — |

The Windows columns of this table come from `results/windows/w-aot-actions.log` (the two ways interleaved in one run), except the evaluate, key, label and screenshot rows, which are the Windows spike's.

### Trusted input (XTEST)

From `results/linux/cov-xtest.log` and `activation.log`:

- **Events:** an XTEST click and key produced `pointerdown:true mousedown:true click:true keydown:true input:true` (`isTrusted`).
- **User activation:** WebKit's popup blocker is the measure. With 6.5 s between routes (longer than transient activation): a timer with no input got no `create` request and `isActive` false; an XTEST click got one `create` request and `isActive` true. (Inside the click listener `navigator.userActivation.isActive` read false in `coverage` but true in `activation`; the `coverage` reading followed API calls, so the popup count is the reliable measure.)
- **Mapping page coordinates to the screen:** the window's XID (`gdk_x11_surface_get_xid`), its root origin (`XTranslateCoordinates`), the surface transform and the view's place in the window (`gtk_widget_compute_point`). Under Xvfb without a window manager the window sat at (0, 0) with no offset; a click at CSS (300, 200) arrived at clientX/Y (300, 200). Looking the geometry up costs 0.0–0.1 ms, so it is done before every click.
- **Focus:** no window manager is needed.
  - GTK gives the window the X input focus when it is presented; the backend still checks `XGetInputFocus` before keys and moved it 0 times in 140 journeys.
  - With the focus on None, keys were lost (0 of 1); with PointerRoot and the pointer over the window, they arrived; after the backend moved the focus, they arrived.
  - A second window presented later took the focus (as a new window does), but a click outside it still reached the test page: **clicks need no focus, keys do**.
  - XTEST's pointer and focus belong to the display, so **one display serves one test at a time**. Two test processes each had their own Xvfb and passed 15 + 15.
- **Text:** characters in the keymap are typed with their Shift level. Others ("ü", "é", "—", "你好") are bound to spare keycodes first (Xvfb's keymap had 19 spare). The app must read the `MappingNotify` before the key arrives: binding one keycode at a time and typing at once typed "Gr好好e"; binding each character to its own keycode, then waiting two turns of the GTK thread, typed "Grüße, café — 你好".
- **Fill through the editor:** `document.execCommand('insertText')` from the engine fires `beforeinput`/`input` with `isTrusted` true and updates React's state (Place order enabled). It takes 1.8 ms against 28.5 ms for 12 key strokes, but it is not keyboard input (no `keydown`).

### Untrusted script input (the comparison)

The `--input script` backend dispatches `PointerEvent`/`MouseEvent`/`KeyboardEvent` from page script (`results/linux/cov-script.log`, `j-aot-script*.log`):

- `isTrusted` false on every event.
- **User activation is still granted**: a script click run through `call_async_javascript_function` got a `create` request and `isActive` true, and so did `window.open` called directly through the API. WebKit treats script evaluated through its API as a user gesture.
- No `:hover`; no default action for keys: a synthetic Tab did not move the focus, and synthetic key strokes typed nothing.
- **The journey failed 60 of 60** at the Radix Select the first time: without pointer moves, Radix Select's `pointerup` guard (pointer moved less than 10 px since the trigger's press) cancelled the option's selection. After adding `pointerover`/`pointermove`/`mousemove` before each press, it got past the select but failed at the filter, because synthetic key strokes type nothing (2 of 2 runs; that log was not kept). With fills through the editor as well, it passed 60 of 60 at 1,232 / 1,271 ms per test, about the cost of XTEST.
- A synthetic click on the file input still opened the chooser (the host's handler answered it), because of the API's user gesture.

### Accessible names: page script against AT-SPI

**(a) The engine in page script** (`engine/engine.js`): roles from the explicit `role` (first known token, presentational roles ignored on focusable elements) or the HTML-AAM implicit role; names by the Accessible Name 1.2 steps (hidden, `aria-labelledby`, `aria-label`, native labels and alternatives, embedded control values while recursing, name from content with CSS `::before`/`::after` and block spacing, `title`); hidden means `aria-hidden`, `hidden`, `inert` or `display: none` on the element or an ancestor, or its own `visibility`; filters `checked`, `pressed`, `expanded`, `selected`, `level`, `disabled`, `includeHidden`; names by substring, exact or regular expression.

- **Against Chromium** (`results/windows/tree-*.json`; the Windows copy dumped the engine's tree and `Accessibility.getFullAXTree`, paired by element): in three states (the orders page, the menu open, the dialog open), **every one of the 39 elements the engine exposes had a Chromium node with the same role and the same name**. Chromium's extra nodes were `generic`, `LabelText` and the document. One difference was found on the way and fixed: a file input is a `button` (Chromium and the library agree).
- **Cost:** the whole tree in 1.5 ms (Linux) and a lookup in 0.3–0.4 ms; 0.9 ms on Windows through `Runtime.callFunctionOn`.
- **Not covered yet:** the page has no tables, grids, trees, `aria-owns`, shadow DOM text, `<slot>` or SVG; a 1.0 needs the WPT accname and HTML-AAM test suites as its conformance check.

**(b) AT-SPI over D-Bus** (`probes/atspi.py` with libatspi through GObject introspection, in the test's private D-Bus session; `results/linux/tree-atspi-*.json`, `tree*-atspi.log`):

- It works with no setup beyond the session bus: GTK activates the accessibility bus, and WebKitGTK's sandboxed web process exposes the page.
- **Speed:** finding the document 4–11 ms, walking 47 nodes 44–46 ms (one D-Bus round trip per property), a Collection query by role 2.4 ms, from Python.
- **Content** (`results/linux/tree4-compare-atspi.log`): all 42 engine nodes (39 elements and the document in each state) had an AT-SPI counterpart, 33 with the same name, but with **WebKit's platform mapping**, not ARIA's:
  - roles such as "push button", "combo box" (for the Radix menu button, which is ARIA `button` with `aria-haspopup`), "entry", "landmark", "status bar", "panel" (for the named `ul`), "menu item" for native `<option>`s;
  - names that include embedded values ("Customer name Ada Lovelace", "Region Americas", "Attachment Choose File") and names from content for list items; the document is named by its title;
  - the native `<select>`'s option menu stays exposed while Radix hides the rest of the page with `aria-hidden`.
- **No way back to the element:** attributes give `tag`, `id` (when there is one) and `xml-roles`, not a DOM node. Actions would have to click at `Component.GetExtents` screen rectangles (which XTEST could use: the "Actions" button was at (786, 80, 78, 38)), with no hit test, no stability check and no page state.

**Verdict:** (a) is the engine. It is complete enough for the journey and the comparison, matches Chromium on this page, costs a fraction of a millisecond, works under the page's CSP, and runs unchanged on Windows. (b) could serve a separate "the platform's accessibility tree" assertion later, not locators.

### Other primitives (`results/linux/cov-xtest.log`)

| Primitive | Linux result |
|---|---|
| Screenshot | `webkit_web_view_get_snapshot` (visible region) → `GdkTexture` → `gdk_texture_download` → PNG; element crops from the same pixels. 5.8 ms with PNG, 3.5 ms without. `results/linux/linux-xtest-settings.png`, `linux-xtest-form.png` |
| Evaluate | `call_async_javascript_function` in the main world (null world name): 0.2 ms. The page's CSP applies to page script (with `--csp`, `eval` threw `EvalError`) but not to the engine's world, which kept working |
| Console and page errors | A main-world document-start shim wraps `console.*` and listens for `error` and `unhandledrejection`, posting to a script message handler: `error: export failed: disk full`, `Error: Unhandled boom at …`. WebKitGTK 6.0 has no console signal; a page can undo the shim |
| Script dialogs | `script-dialog` returns TRUE: the alert never shows, the page continues ("Alert closed") |
| File input | `SetInputFilesAsync` arms the host's `run-file-chooser` handler and clicks the input; the handler calls `webkit_file_chooser_request_select_files`. The page got `upload.txt`, 5 bytes. **The picker is blockable**: with nothing armed the handler cancels, and no dialog window appeared (the only new X window was an 88×31 override-redirect popup, the hover tooltip of the file input; it did not appear with script input) |
| Navigation wait | URL polled through the engine (glob), as on Windows |
| `window.open` | The `create` signal counted and refused; nothing opened |
| Timeout messages | `expect role=status to have text "Shipped": timed out after 300 ms (7 attempts). Last attempt: text is "Draft"`; `click role=button[name~="Place order"]: … "Place order": not enabled`; strict mode lists the first five matches |

### Isolation (`results/linux/iso-*.log`)

- **Consecutive tests**, a new ephemeral session each: 0 of 10 found the previous test's `localStorage`, `sessionStorage`, IndexedDB value or cookie (two runs of five).
- **Simultaneous tests in one process**, a session each: nothing shared (2 of 2). With **one shared** ephemeral session (control): `localStorage` and IndexedDB shared.
- **Persistent session on one folder** (control): the second test found the first test's `localStorage` and IndexedDB values, so the writes were real.
- **Cookies** are never stored on `spar://`: `document.cookie` read back empty right after writing it.
- **Costs:** `webkit_network_session_new_ephemeral` 0.3–0.6 ms with the trip to the GTK thread; open + write + close 158–161 ms (266 ms the first time); one web process per view; one network process for all sessions.

### NativeAOT

- `dotnet publish` (`PublishAot`, `IsAotCompatible`, `InvariantGlobalization`) in WSL: **0 warnings, 0 errors**, no suppressions (`results/linux/publish-aot.log`). 3,478,080 bytes, publish 4.4–5.8 s warm.
- The Windows copy: 0 warnings (`results/windows/publish-w-aot.log`), 3,625,984 bytes.
- C# 15 again rejected `await` inside an `unsafe` type (CS8652); the `unsafe` modifier moved to the members that need it.

## The shared engine on Windows

The Windows copy (`windows/`) is the Windows spike's Option B with one change: `RoleQuery` calls the engine (injected with `Page.addScriptToEvaluateOnNewDocument`) through `Runtime.callFunctionOn`, instead of `Accessibility.queryAXTree` + `DOM.resolveNode`. The host side is the Windows spike's `Shared/`, compiled by reference and unchanged.

| 60 journeys, NativeAOT | `queryAXTree` | Engine |
|---|---:|---:|
| Journey p50 / p95 | 1,475 / 1,551 | 1,364 / 1,403 |
| Per test p50 / p95 | 1,613 / 1,682 | 1,497 / 1,551 |
| Failures | 0 | 0 |

Per step and per action above. The engine removes the frame-long `queryAXTree` waits the Windows spike found: role lookups drop from 13–35 ms to about 1 ms, and the clicks from 55–58 ms to 35–41 ms. It also removes the dependency on an experimental DevTools method.

### Shared code, Windows and Linux

| Piece | Shared? |
|---|---|
| The API shape (`GetByRole`, `GetByLabel`, `GetByText`, `GetByTestId`, `Nth`/`First`, `ClickAsync`, `FillAsync`, `PressAsync`, `CheckAsync`, `SelectOptionAsync`, `SetInputFilesAsync`, `Expect(...).ToBeVisibleAsync()` …) | Yes: `Journey.cs` is the same 28 calls on both |
| `engine/engine.js` (roles, names, queries, actionability, state, the input watcher) | Yes, the same file |
| Retry loop, timeouts and messages, `Expect` | Same design; could be one file |
| Transport | Windows: CDP over `CallDevToolsProtocolMethod`; Linux: `call_async_javascript_function` in an isolated world. With the engine as the core, a backend needs only "run this request in the engine's world" |
| Element handles | Windows keeps remote objects per attempt; Linux re-resolves the chain in every call. With the engine, both can do it Linux's way |
| Input | CDP `Input.*` against XTEST; behind one `IInput` interface |
| Screenshots, dialogs, files, console | Platform calls on each side (about 150 lines per side) |

## Effort and the way to a Linux 1.0

**Agent time** (not a developer's, from the WSL clock): the Linux harness (host plumbing, engine, API, XTEST, about 2,600 lines) went from the first source file to a passing journey in about 15 minutes, including two fixes found by running it (`webkit_user_script_new` for the main world, and the runtime folder outside `/var/tmp`); a third fix (watch `pointerdown`/`pointerup`, not mouse events, because Radix's `preventDefault` on `pointerdown` suppresses them) took the journey from 6.7 s to 1.3 s. The whole spike, with the probes, the Windows copy, part 1 and every measurement, took about 1.5 hours.

| Item | Days | Notes |
|---|---|---|
| The engine to 1.0 (shared): the WPT accname and HTML-AAM suites as conformance tests, tables and grids, `aria-owns`, shadow DOM and slots for text and labels, SVG, `Filter`/`And`/`Or`, placeholder/alt/title | 5–8 | Counts once for both platforms; replaces the Windows estimate's "role engine in page script" |
| A common core: chain locators, retry, `Expect`, call log and failure report on both backends | 2–3 | Refactor of today's two copies |
| XTEST input: the US key table, modifiers and `ControlOrMeta` shortcuts, double click, buttons, wheel, drag and drop, IME-free text for any character | 3–5 | Text beyond the keymap is already solved (spare keycodes) |
| Geometry: HiDPI scale (`gdk_surface_get_scale`), CSD offsets with a window manager, page zoom, scroll containers | 1–2 | Exact without a window manager today |
| The display per test process: Xvfb lifecycle, the `XDG_RUNTIME_DIR` rule, D-Bus session, process census, CI container | 2–3 | Partly done in `wsl/`; the test host owns it |
| Events: console and page errors, dialogs with accept/dismiss/prompt text, the file chooser wait, downloads | 1–2 | Signals exist; the shim is the weak point |
| Frames: same-origin frames through the engine; cross-origin needs another route (the navigation lock keeps them out) | 1–2 | |
| Network waits | 1–2 | No Network domain: `WebKitWebResource` signals or the scheme handler; not checked |
| Its own tests on both platforms and docs | 3–4 | |
| **Linux 1.0 on top of the shared engine** | **12–20** (plus 5–8 for the engine, shared with Windows) | The Windows spike estimated 15–25 for a WebKitGTK backend; the engine's share now counts once |

**What stays hard:** an exact accessible-name computation for every pattern; flake-free actionability with real component libraries (Radix's guards were the only failures seen here); keyboard layouts and IME; Wayland, where XTEST does not exist (decision "Trusted input in Linux desktop tests": one input interface, so a Wayland route can be added).

## Problems met

- **WebKitGTK's sandbox aborts when `XDG_RUNTIME_DIR` is under `/var/tmp`.** `bwrap: Can't make symlink at /var/tmp: destination exists and is not a symlink`, then `Failed to fully launch dbus-proxy` and exit 133. WebKit's bubblewrap setup symlinks `/var/tmp` to `/tmp`, and a runtime folder bound under `/var/tmp` creates the directory first. `HOME` and the other XDG folders under `/var/tmp` are fine. Moving only the runtime folder to `/dev/shm/spar-wal-<run>` (tmpfs, 0700, removed after each run) fixed it (`wsl/sandbox-paths.sh`, `results/linux/paths-*.log`). This matters for `eng/wsl/`, which keeps every XDG folder under `/var/tmp/spar-linux/` (a pitfall for M1c's Linux tests).
- **Xvfb is not installed and runs `xkbcomp` from `/usr/bin`.** The unpacked Xvfb ran in a bubblewrap mount namespace where `/usr/bin` is a folder of symlinks plus the unpacked `xkbcomp` (`wsl/common.sh`). Ubuntu's bubblewrap 0.9.0 has no `--overlay`.
- **Stopping Xvfb:** killing the `bwrap` parent left Xvfb running once (an early probe); the scripts now stop bwrap's children.
- **Radix's `preventDefault` on `pointerdown`** suppresses `mousedown`/`mouseup`: the first watcher listened to mouse events and three clicks per journey waited 2 s for an acknowledgement that never came (the journey still passed). It now listens to pointer events.
- **The first character-remapping version** bound every character to one spare keycode and typed "Gr好好e"; see trusted input.
- **Script input and Radix Select:** above.
- **Process counts:** another spike's WebKit processes ran in the same distribution; counts are now limited to the harness's descendants and the census to this spike's `XDG_RUNTIME_DIR`.
- **CS8652** (await in an `unsafe` type), as in earlier Linux spikes.
- **The AT-SPI reader** first failed on two GObject-introspection details (`PUSH_BUTTON`, an empty attribute table) and the harness waited 90 s per state; it now always releases the harness.

## Not tested

- **Wayland** (no XTEST; decided), a window manager, a real GPU, HiDPI scale factors, IME, keyboard layouts other than Xvfb's default US map.
- **A real desktop:** everything ran under Xvfb in WSL with software rendering.
- **The library's network waits, frames and drag and drop on Linux** (not built), WebKit's resource signals for network waits.
- **Cross-origin frames** with the engine (the navigation lock keeps them out).
- **WPT conformance** of the engine; only this page's 39 nodes were compared with Chromium.
- **Simultaneous tests with input in one process** (one display has one pointer and one focus; only evaluate ran in the simultaneous isolation check).
- **The AT-SPI route from C#** (only from Python); its cost from C# would be lower than from Python's introspection layer.
- **CI containers** (Docker was not used).

## Reproduce

In WSL Ubuntu 24.04, from this folder (no root):

```bash
bash wsl/setup.sh        # apt-get download + unpack Xvfb, xkbcomp, X utilities, webkit2gtk-driver under /var/tmp/spar-linux/window-automation-linux/tools
bash wsl/sync.sh         # copy sources and the Windows spike's built page (spikes/window-automation/page/obj/dist) to the working copy
bash wsl/publish.sh      # NativeAOT publish and a JIT build
bash wsl/series.sh       # every part-2 measurement; RUNS=60 by default
bash wsl/atspi.sh tree3  # engine against AT-SPI; then node probes/compare-trees.mjs <out> atspi orders menu dialog
bash wsl/part1.sh        # part 1
bash wsl/collect.sh      # logs, trees and PNGs into results/linux, redacted
```

Windows (Git Bash, repository root): `dotnet publish spikes/window-automation-linux/windows -c Release -o spikes/window-automation-linux/windows/bin/publish`, then `OptionB.Engine.exe journey --runs 60 [--engine]`, `actions`, `tree --out <folder>`; `node spikes/window-automation-linux/probes/compare-trees.mjs <folder> chromium orders menu dialog`. The windows are offscreen and never activated, and all input goes through CDP, as in the Windows spike.
