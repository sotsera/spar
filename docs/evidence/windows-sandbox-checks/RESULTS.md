# Windows Sandbox checks: results

> A spike. Its code was deleted with `spikes/` at the start of M2; it is in git history at [`spikes/windows-sandbox-checks/`](https://github.com/sotsera/spar/tree/24f5db0942833047c81fa692908410596f3b8960/spikes/windows-sandbox-checks) (commit `24f5db0`), and this record and its `results/` moved here. Paths under `spikes/` below name that history.

Recorded 2026-11-14. Host: Windows 11 Pro 10.0.26320, two 1920×1080 monitors at 96 DPI, SDK 11.0.100, WebView2 Runtime 157.0.4341.46, SDK headers 1.0.4314.35. Guest: Windows Sandbox, Windows 11 Enterprise Insider 26100.9877 (24H2), one 1520×823 display at 96 DPI, the same WebView2 Runtime files (157.0.4341.46), no network, **`<VideoInput>Disable</VideoInput>` and `<AudioInput>Disable</AudioInput>`** (`results/sandbox-config.xml`). The guest session is a remote session (`SM_REMOTESESSION` = 1).

## Summary

**Part 1, title bar at other DPI scales: blocked.** The guest's display scale cannot be changed from inside the guest. The session takes its DPI from the Sandbox client window, which sits on a 96 DPI host monitor. Four routes failed (DisplayConfig, `SPI_SETLOGICALDPIOVERRIDE`, custom `LogPixels` with a new sign-in, and the same with `IgnoreClientDesktopScaleFactor`); see "DPI". So 125 %, 150 %, 200 % and `WM_DPICHANGED` are **not tested**. Getting there needs a host monitor at that scale (a host display setting, so the user's call) or a VM with a console session.

At 96 DPI in the guest, the title bar with the cache kept in CSS pixels and scaled at hit-test time passed every real-input check: drag, controls, system menu, double-click, maximized insets, maximize and restore button, top edge, Snap Layouts restored and maximized, minimize, `Win+arrows`, `Alt+Space` and close. The bar is 32 DIP = 32 px.

| Scale | Verdict |
|---|---|
| 100 % (96 DPI) | pass, all checks, with the CSS-pixel cache and with the accessibility fixes below |
| 125 %, 150 %, 200 %, `WM_DPICHANGED` | not tested (guest DPI fixed by the Sandbox client) |

**A bug in the copied host code, fixed here:** the first click on a page caption button of an inactive window was lost, every time. Activation raises `WM_NCMOUSELEAVE` between the button's down and up, and the handler cleared the press. After the fix, first clicks work.

**Accessibility: the empty native title bar and the 15 unnamed panes can both be removed** (native UIA client, host and guest):

- **Title bar:** a server-side UIA provider on the top-level window (`WM_GETOBJECT`, `UiaRootObjectId`) whose only change is `ProviderOptions_RefuseNonClientSupport` (`0x40`), with everything else delegated to `UiaHostProviderFromHwnd`. `TitleBar`, "System", "Minimize", "Maximize" and "Close" all disappear. The page, the window name and the document stay.
- **Panes:** WebView2's 14 drag-region windows are in the host process, on the UI thread. Subclassing them and answering their `WM_GETOBJECT` with a provider that returns `IsControlElement` and `IsContentElement` = false removes them from the control view. The sink answers the same way in its own window procedure.
- **Result:** the control view goes from 45 elements to 24 (host) and from 50 to 29 (guest). It now holds the window, WebView2's own panes and the page.
- **Still working, with real input and the provider active:** every check above. UIA `Invoke` on the page's Maximize, Restore and Minimize buttons maximized, restored and minimized the window. The page's `aria-label` changed to "Restore" when maximized.
- **Did not work:** dynamic annotation (`IAccPropServices::SetHwndProp`, `S_OK` but no effect); answering `WM_GETTITLEBARINFOEX` (the phantom buttons only got classic rectangles); removing `WS_SYSMENU` (an empty `TitleBar` stays, and the system menu is gone).
- **Not run:** Narrator, because the guest's audio output plays on the host.

**Windows' own caption buttons become viable when maximized** with the host taking over from DWM there:

- Hit-test the button rectangles from `WM_GETTITLEBARINFOEX` (or from `DWMWA_CAPTION_BUTTON_BOUNDS`; both worked), and own the press and release only while `IsZoomed`.
- **Maximized:** the restore click restored, minimize and close worked, and the Snap Layouts flyout appeared on the restore button. Without the takeover, none of these worked.
- **Taking over while restored broke every DWM click**, so leave the restored state to `DwmDefWindowProc`.
- **Still open:**
  - DWM's hover and pressed visuals when maximized;
  - the 350 ms dark-start colour from `windows-titlebar`;
  - DWM buttons are invisible to UIA, and with `RefuseNonClientSupport` nothing exposes them at all.

  Page buttons remain the better default.

**Camera and microphone on WebView2** (guest only; fake devices through `ICoreWebView2EnvironmentOptions.AdditionalBrowserArguments`):

| Situation | `PermissionRequested` | Result |
|---|---|---|
| No device, no `Permissions-Policy` | none | `NotFoundError` in about 1 ms |
| No device, `camera=(), microphone=()` | none | `NotFoundError` in about 1 ms (`permissions.query` = denied) |
| Fake devices, no header or `camera=(self)` | **fires**: camera = kind 2, microphone = kind 1; one event per kind for `{video, audio}` | as answered |
| … answered `ALLOW` | | stream in 4–184 ms, tracks `live` |
| … answered `DENY` | | `NotAllowedError` in 1–186 ms |
| … not answered, or answered `DEFAULT` | | **WebView2's own prompt** ("app.localhost wants to Use your cameras", Block or Allow) inside the window; the call waits |
| Fake devices, `camera=()` | none | `NotAllowedError` in 1–131 ms |
| Fake devices + `--use-fake-ui-for-media-stream` | **none, even with a deny handler** | stream granted |
| … with `camera=()` | none | still `NotAllowedError`: the header held |

- **`SavesInProfile` defaults to true.** An `ALLOW` or `DENY` answer is remembered in the profile. Later requests, also in a new process on the same profile, get no event: a remembered allow starts the camera without the host. With `SavesInProfile = false`, nothing is stored and every request reaches the handler.
- **`ICoreWebView2Profile4` exists on Runtime 157.**
  - `GetNonDefaultPermissionSettings` lists the remembered answers, for example `camera|https://app.localhost/|allow`.
  - `SetPermissionState(kind, "https://app.localhost", state)` changes them: `DEFAULT` removes the entry, so the next request raises the event again, and `DENY` blocks it.
  - **Setting `DENY` while the camera runs ends the live track at once.** Revoking the microphone ended both tracks of a combined stream.
- **No Windows privacy prompt** appeared in any guest screenshot. With fake devices Chromium never opens a Windows capture device, so this does not reproduce the host incident. The guest's `ConsentStore` had no `NonPackaged` entry afterwards.
- **Fake video frames were produced but never delivered** in the guest. `stats.totalFrames` rose by 20 per second while `deliveredFrames` stayed 0, `<video>` stayed at `readyState` 0, and `ImageCapture.grabFrame` timed out. "The stream starts" is shown by a live track only.

**Rule this implies for the host:**

1. **`Permissions-Policy` is the gate that holds.** It blocked camera and microphone with no event, even under `--use-fake-ui-for-media-stream`, which bypassed the `PermissionRequested` deny handler completely. Send it on every document, denying everything not declared in C#. Keep ignoring `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` in Production.
2. **Always answer `PermissionRequested` with an explicit `ALLOW` or `DENY`.** Leaving the state at `DEFAULT` shows WebView2's prompt to the user. Use `DEFAULT` only when the author chose "let the WebView ask".
3. **Set `SavesInProfile = false` when the host keeps the answer** (the C# handler and app setting). Otherwise WebView2 remembers it and later requests bypass the host. If WebView2 is allowed to remember, the app setting that turns the webcam off calls `SetPermissionState(DENY or DEFAULT)`, which also stops a running capture.
4. **A real device on Windows can still raise the OS privacy prompt** for unpackaged apps before or instead of `PermissionRequested` (the host incident). That path stays untested; the header keeps it unreachable for apps that do not declare the camera.

## Method

| Path | Contents |
|---|---|
| `Checks.Desktop/` | NativeAOT host (`win-x64`, 0 warnings, no suppressions), copied from `spikes/windows-titlebar/Titlebar.Desktop` and extended: CSS-pixel hit-test cache (`--cache css\|px`), `WM_DPICHANGED` cache check, DWM button takeover (`--dwm-fix none\|tbi\|bounds`), accessibility experiments (`--nc-provider refuse`, `--annotate subclass\|panes\|titlebar\|both`, `--tbi invisible\|page`, `--style nosysmenu`), `ICoreWebView2EnvironmentOptions` implemented by the host (additional browser arguments), `PermissionRequested` (`--perm observe\|allow\|deny\|default`, `--saves keep\|true\|false`), `Permissions-Policy` (`--pp none\|deny\|self\|camself`), `ICoreWebView2Profile4` list and set, a media page (`page/media.html`, `media.js`) driven only by the host, display-scale tool (`--scale`), native UIA dump and `--uia-invoke`. Drive, media and scale changes refuse to run outside the `WDAGUtilityAccount` account. |
| `run-uia.sh` | Host-safe UIA dumps of an offscreen, non-activated window (no input). |
| `sandbox/` | `start.sh` (writes the configuration and starts the sandbox), `connect.sh` (`wsb connect`, minimizes the client until it stays down), `guest.sh` (stages the exe and scripts under new names and runs one guest script), guest scripts `run.ps1`, `uia.ps1`, `invoke.ps1`, `info.ps1`, `scale.ps1`, `logpixels.ps1`, host helpers `find-client.ps1` (copied), `client-place.ps1`, `close-client.ps1`. |
| `tools/` | `vtable.mjs` (copied: vtable slots from headers), `summarize.mjs` (compact view of the JSON-lines logs). |
| `results/` | Small evidence: `media-summary.txt`, `drive-summary.txt`, `uia-host/`, `uia-guest/`, `uia-invoke.txt`, `scale-attempts.txt`, `guest-info.txt`, `sandbox-config.xml`. |

Logs and screenshots are in `artifacts/windows-sandbox-checks/` (`sandbox/out/{media1..5,drive96*,uia2,invoke,...}`, `host-uia/`, `host-probe/`).

- **Slots and IIDs** come from `WebView2.h` 1.0.4314.35 and the Windows SDK 10.0.26100 headers (`UIAutomationCore.h`, `UIAutomationClient.h`, `oleacc.h`), read with `tools/vtable.mjs`:
  - `ICoreWebView2` 23 `add_PermissionRequested`;
  - `ICoreWebView2PermissionRequestedEventArgs3` 3–12;
  - `ICoreWebView2Profile4` 15 `SetPermissionState`, 16 `GetNonDefaultPermissionSettings`;
  - `ICoreWebView2PermissionSettingCollectionView` 3 and 4;
  - `ICoreWebView2PermissionSetting` 3–5;
  - `ICoreWebView2EnvironmentOptions` 3–10 (implemented as a `[GeneratedComClass]`, target version `156.0.4314.35`).
- **On the host desktop:**
  - one offscreen probe run and the UIA dumps of offscreen windows shown with `SW_SHOWNOACTIVATE`;
  - no input, no camera or microphone call, no display or system change, no notification icons, no registry writes.
- **Everything with input, media or scaling ran in the guest.** Profiles were under `artifacts/webview2-profiles/windows-sandbox-checks/`, mapped to `C:\spar\profiles`, one per configuration, with the runtime folder passed explicitly.
- **Cleanup:** I stopped the sandbox and closed the two Sandbox client windows that my `wsb connect` calls had opened (`WM_CLOSE`). The host's process list then matched the list taken before the work (`artifacts/windows-sandbox-checks/host-processes-{before,after}.csv`): no spike, `msedgewebview2` or Sandbox process of mine was left. The `WindowsSandboxServer` that ran before I started had exited on its own. Then I deleted the profiles.

## Results per part

### Part 1a. DPI

| Attempt (guest only) | Result |
|---|---|
| `DisplayConfigGetDeviceInfo(-3)` | recommended 100 %, range 100..100 % |
| `DisplayConfigSetDeviceInfo(-4)` to 125 % and 150 % (range check bypassed) | `ERROR_ACCESS_DENIED` (5); the stored value changed (reported current 125 %), but the DPI stayed 96 |
| `SPI_SETLOGICALDPIOVERRIDE` | failed (returned 0) |
| `LogPixels` = 120, `Win8DpiScaling` = 1, sign out, new session | DPI 96 |
| The same plus `HKLM\...\Terminal Server\WinStations\IgnoreClientDesktopScaleFactor` = 1 | DPI 96 |
| Larger guest display, by resizing the client window offscreen | the client ignored the requested position and appeared on the host screen at its old place; minimized again after about 10 s, abandoned |

Evidence: `results/scale-attempts.txt`. The `WM_DPICHANGED` handler and its immediate check (button centres against the CSS cache and the old pixel cache, logged as `dpi-cache-check`) exist in `Host.cs`, but never ran.

### Part 1b. Title bar at 96 DPI with the CSS-pixel cache (guest, real input)

`drive96/page-css` and `drive96/page-a11y` (the latter with the accessibility fixes), window 900×500 DIP:

| Check | page-css | page-a11y |
|---|---|---|
| Bar height | 32 px = 32 DIP, top band 8 px | same |
| Drag (120, 60) | pass | pass |
| Menu, tab, search and typing, no move | pass | pass |
| Right click: system menu | pass | pass |
| Double click maximize and restore; maximized client = work area `[0,0,1520,775]` | pass | pass |
| Maximized controls | pass | pass |
| Maximize and restore by button | pass | pass |
| Top edge −40 px | pass (`HTTOP`) | pass |
| Snap Layouts flyout, restored and maximized | yes (59.8 % and 57.9 % of the area changed) | yes |
| Minimize; `Win+Up/Left/Right/Down`; `Alt+Space`; close | pass (`Win+Left` frame `[0,0,760,775]`) | pass |

**First click on an inactive window** (`drive96d`, `drive96e`, `drive96f`):

- **Before the fix,** with "maxbutton" as the very first input, 3 of 3 runs lost the click.
- **The trace:**
  1. `WM_NCLBUTTONDOWN(HTMAXBUTTON)` set the press.
  2. Activation followed: `WM_NCACTIVATE`, then `WM_NCMOUSELEAVE`, which cleared the press.
  3. `WM_NCLBUTTONUP` found no press, so no command ran.
- **After the fix** (keep the press across the leave), the first click maximized in 2 of 2 runs.
- **Still to do for M1:** proper tracking with capture.

### Part 1c. Accessibility

Native UIA client, control view (`results/uia-host/`, `results/uia-guest/`):

| Variant | Elements | `TitleBar` | Drag panes + sink | Page document |
|---|---|---|---|---|
| Windowed, page buttons (base) | 45 host / 50 guest | yes, 4 children with `[0,0,0,0]` | 14 + 1 | yes |
| + `RefuseNonClientSupport` provider | 39 / 44 | **no** | 14 + 1 | yes |
| + subclassed panes only | 30 | yes | **0** | yes |
| + both | **24 / 29** | **no** | **0** | yes |
| Dynamic annotation (`IsControlElement` = false, panes and `OBJID_TITLEBAR`) | 45 | yes | 14 + 1 | yes |
| `WM_GETTITLEBARINFOEX` answered (invisible, or page rectangles) | 45 | yes, children moved to classic 22 px rectangles | 14 + 1 | yes |
| `WS_SYSMENU`, minimize and maximize boxes removed | 40 | empty `TitleBar` | 14 + 1 | yes |
| Visual hosting, base / + refuse | 35 / 29 | yes / **no** | 0 | yes |
| DWM buttons + both fixes | 26 | no; the visible DWM buttons are not exposed at all | 0 | yes |

- **The provider is created on the first `UiaRootObjectId` request** (`nc-provider 0x42` in the log) and disconnected in `WM_DESTROY`.
- **The subclass** restores the original window procedure and disconnects its provider on `WM_NCDESTROY`. WebView2 recreates its drag windows when the regions change, so the host re-subclasses after every layout message.
- **UIA `Invoke`** (`results/uia-invoke.txt`) found the page's buttons by AutomationId and invoked them: maximize (`SC_MAXIMIZE`), restore (`SC_RESTORE`), minimize. The name followed the `aria-label`.
- **Not done:** the page buttons have `tabindex="-1"`, so keyboard users rely on `Alt+Space` and `Win+arrows`, which pass. Narrator's speech was not checked.

### Part 1d. Windows' own caption buttons (guest, real input; `drive96`, `drive96b`, `drive96c`)

| | No takeover | Takeover from `WM_GETTITLEBARINFOEX` | Takeover from `DWMWA_CAPTION_BUTTON_BOUNDS` |
|---|---|---|---|
| Restored: maximize click | pass | pass (after restricting the takeover to maximized; before, all restored clicks failed) | pass |
| Maximized: restore click | **fail** | pass | pass |
| Maximized: Snap Layouts flyout on the restore button | **no** (`HTCAPTION`) | yes (59.8 %) | yes |
| Maximized: minimize | fail | pass | pass |
| Maximized: close | fail | pass | pass |

The rectangles, maximized, in screen coordinates (work area 1520×775):

| Source | Minimize | Maximize | Close |
|---|---|---|---|
| `WM_GETTITLEBARINFOEX` | `[1379,0,1426,29]` | `[1426,0,1472,29]` | `[1472,0,1520,29]` |
| `DWMWA_CAPTION_BUTTON_BOUNDS` | `[1381,0,1527,30]` in window coordinates for all three, which is `[1373,-8,1519,22]` on screen | | |

Clicks at the button centres worked with both. The takeover owns `WM_NCLBUTTONDOWN` and `UP` and posts `SC_*`, like page buttons.

### Part 2. Camera and microphone (guest only)

Full event sequences are in `results/media-summary.txt`. The runs:

| Run | Configuration | Shows |
|---|---|---|
| `media1` | no device | `NotFoundError` with any header or handler, no event |
| `media2/fake-allow` | | allow, list, revoke (track ended at once), re-request after `DEFAULT` |
| `fake-deny` | | remembered deny, no second event |
| `fake-observe`, `fake-default` | | WebView2 prompt, 8 s timeout |
| `fake-nosave` | | nothing stored, event each time |
| `fake-save1` + `fake-save2` | two processes, one profile | remembered allow, stream with no event; reset to `DEFAULT`, then the event again |
| `fake-ppdeny`, `fake-camself` | | header gate, no event |
| `fake-ui` | | event bypassed |
| `media3` | `fake-ui` + deny handler | stream granted |
| | `fake-ui` + `camera=()` | denied |
| `media4` | | frames not delivered |
| `media5` | | combined request: two events, revoke ends both tracks |

`IsUserInitiated` was true for every request, which all came from `ExecuteScript` (the 5-second rule in the pitfalls). Device labels were empty before permission and filled after (`fake_device_0`, `Fake Default Audio Input`).

Screenshot of the WebView2 prompt: `artifacts/windows-sandbox-checks/sandbox/out/media2/fake-observe-01-gum-video.png`. No screenshot showed a Windows privacy dialog.

## Problems met

Candidate pitfalls for `docs/pitfalls.md` (not edited here):

- **Windows Sandbox's DPI is set by its client window,** not by the guest:
  - DisplayConfig set returns `ERROR_ACCESS_DENIED` in the remote session;
  - the reported range at 1520×823 is 100..100 %;
  - `LogPixels` and `IgnoreClientDesktopScaleFactor` are ignored after a new sign-in.

  Scaling tests need a host monitor at that scale or another VM.
- **The Sandbox client restores and activates itself when the connection completes,** after an early minimize. `connect.sh` minimizes it again in a loop. `SetWindowPlacement` with an offscreen rectangle put it back on screen at its old position, visible but not activated, for about 10 s. Do not move the client window.
- **Signing out of the guest and connecting again leaves the old client window open** (disconnected). Each `wsb connect` adds one; close them after `wsb stop`.
- **A press tracked through `WM_NCMOUSELEAVE` loses the first click on an inactive window** (copied `windows-titlebar` code).
- **Taking over DWM's caption buttons while restored breaks their clicks.** Take over only when maximized.
- **`--use-fake-ui-for-media-stream` bypasses `PermissionRequested` entirely** (stream granted under a deny handler). Only `Permissions-Policy` still blocked.
- **`PermissionRequested` left at `DEFAULT` shows WebView2's prompt; `SavesInProfile` defaults to true,** so answers persist and later requests skip the handler.
- **With no capture device, `getUserMedia` fails with `NotFoundError` before any permission check,** even under `camera=()`, so a device-less machine cannot test the permission path.
- **Fake video frames in the guest were counted but never delivered** to `<video>` or `ImageCapture`.
- **Dynamic annotation of UIA properties on WebView2's windows returns `S_OK` and does nothing.**
- **`wsb exec` relays nothing, so a wrong script path shows only as exit code `-196608`** (PowerShell's "file not found"). The first `guest.sh` passed a literal `$script` because of an escaping slip. Wrapping the guest call in `cmd /c ... > log 2>&1` keeps its output.

## Not tested

- **DPI:** 125 %, 150 %, 200 %, `WM_DPICHANGED` at run time, mixed-DPI monitors, Snap Layouts and maximized insets above 100 % (Part 1a).
- **Narrator's actual output:** the guest's only audio endpoint is "Remote Audio", which plays on the host. UIA focus events were not recorded either.
- **DWM caption buttons:** hover and pressed visuals when maximized under the takeover, and hit-test cost (the summary is not written when the window closes by button).
- **The accessibility fixes:** with a screen reader running during input, with Voice Access, and their effect on Windows 10.
- **A real camera and microphone, and the Windows privacy prompt** for unpackaged apps on first real device access (the host incident). Also the WebView2 prompt's own `Block` and `Allow` buttons (not clicked).
- **Camera frames reaching the page** in the guest.
- **`PermissionRequested` details:** deferral (`GetDeferral`, an answer from asynchronous app UI) and `Handled`.
- **Profiles across runtimes:** `GetNonDefaultPermissionSettings` on older runtimes (feature-detect `ICoreWebView2Profile4`).

## Reproduce

From the repository root, Git Bash:

```bash
dotnet publish spikes/windows-sandbox-checks/Checks.Desktop -c Release            # 0 warnings
spikes/windows-sandbox-checks/run-uia.sh artifacts/windows-sandbox-checks/host-uia subclass-refuse --annotate subclass --nc-provider refuse
spikes/windows-sandbox-checks/sandbox/start.sh && spikes/windows-sandbox-checks/sandbox/connect.sh
spikes/windows-sandbox-checks/sandbox/guest.sh run -Mode drive -Out drive96 -Configs "page-a11y --size 900x500 --annotate subclass --nc-provider refuse"
spikes/windows-sandbox-checks/sandbox/guest.sh run -Mode media -Out media2 -Configs "fake-allow --frame standard --page media.html --fake-media --pp self --perm allow --steps gum:video,list,set:camera:deny,tracks"
spikes/windows-sandbox-checks/sandbox/guest.sh uia -Out uia2 -Configs "base --size 900x500;subclass-refuse --size 900x500 --annotate subclass --nc-provider refuse"
node spikes/windows-sandbox-checks/tools/summarize.mjs artifacts/windows-sandbox-checks/sandbox/out/media2/*.jsonl
wsb stop --id "$(cat artifacts/windows-sandbox-checks/sandbox/id.txt)"
```
