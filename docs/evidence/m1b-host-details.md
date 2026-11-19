# M1b: host details (focus, the controller's visibility, the window's position, the teardown leak)

Recorded 2026-11-17. What it shows: the Windows host's details of plan M1b, build step 6 (research review section 4), on the test app `tests/apps/SecurityProbe`, published as NativeAOT, against a **baseline** published from the host before the step (commit d94c48a, the same page): focus and typing with real input in Windows Sandbox; the page's visibility and throttling when the window is minimized, hidden and moved, on the desktop with offscreen, never activated windows; and the handle counts of repeated controller open and close cycles with and without the earlier teardown mitigation, on the desktop and with windows on the guest's screen. Decision "Host details (implementation record; choices for review)".

## Environment

Host: Windows 11 Pro 10.0.26320 (system theme dark), two 1920×1080 monitors at 96 DPI, SDK 11.0.100, WebView2 Runtime 157.0.4341.46, `Microsoft.Web.WebView2` 1.0.4314.35. Guest: Windows Sandbox on the same host (no network, camera, microphone, clipboard or printers; `artifacts/m1b-host-details/sandbox/config.xml`), one 1520×823 display at 96 DPI, the same Runtime files, signed-in user `WDAGUtilityAccount`.

- **On the desktop** every app run was started by [`HostRun.cs`](m1b-host-details/HostRun.cs) from a copy outside the repository ([`scenarios.sh`](m1b-host-details/scenarios.sh)): shown without activation, moved offscreen at once, `LOCALAPPDATA` in its own folder under `artifacts/webview2-profiles/m1b-host-details/`, `SPAR_AUTOMATED=1` so no dialog could reach the desktop. Window actions came from the run's process and activate nothing: `SW_SHOWMINNOACTIVE`, `SW_SHOWNOACTIVATE`, `SW_HIDE`, `SetWindowPos` with `SWP_NOACTIVATE` (still offscreen), `WM_CLOSE`. No input was sent to the desktop and no dialog appeared.
- **In Windows Sandbox** ([`sandbox.sh`](m1b-host-details/sandbox.sh)) the guest script [`focus.ps1`](m1b-host-details/sandbox/focus.ps1) drove the app with real input through `SendInput` ([`Guest.cs`](m1b-host-details/sandbox/Guest.cs)), the app's own window and a second process's window only. The sandbox client window on the host was minimized at once and closed with the sandbox at the end.
- **The leak probe** ([`LeakProbe/`](m1b-host-details/LeakProbe/), [`leak.sh`](m1b-host-details/leak.sh)) is a NativeAOT program compiled with Spar's own WebView2 declarations (`src/Spar.Windows/Interop`, copied in by the script). It keeps one environment and, per cycle, creates a window and a controller, loads a page served from memory on `https://app.localhost` that makes five fetches to its origin and five to a loopback HTTP server (through the browser's network service), then tears it down: **plain** is the host's order (events removed, `Close` on the STA, window destroyed), **mitigated** is the earlier research's (the controller hidden, a navigation to `about:blank` awaited, then the close posted to the window outside any WebView2 callback). An **anchor** controller in a window of its own keeps the browser alive across the cycles, as a second window or a test run's shared environment would; without it the browser ends with each cycle and nothing could build up. After each cycle and a 1–1.5 s pause it writes the handle counts of its own process and of every WebView2 process it started, by kind. Windows: offscreen and hidden on the desktop, shown on the screen in the guest.
- The raw logs are in [`m1b-host-details/logs/`](m1b-host-details/logs/) (desktop, sandbox, and two crops of the guest's screen); [`summarize-leak.mjs`](m1b-host-details/summarize-leak.mjs) gives the leak table below.

## What was built

| Path | Contents |
|---|---|
| `src/Spar.Windows/ViewWindowState.cs` (new) | Focus, visibility and position passed on to the controller (UI thread) |
| `src/Spar.Windows/WindowsApp.cs` | `WM_WINDOWPOSCHANGED` (then `DefWindowProc`, which sends `WM_SIZE` and `WM_MOVE`) and `WM_SETFOCUS`, both named for the stall probe |
| `src/Spar.Windows/BrowserView.cs` | `ViewWindowState` per view: attached to each controller (also after the browser is recreated), detached on release; `MoveFocusRequested` subscribed; no resize while minimized; one summary line at teardown |
| `src/Spar.Windows/Interop/` | `ICoreWebView2Controller` slots 12 `MoveFocus`, 13–14 `add/remove_MoveFocusRequested`, 23 `NotifyParentWindowPositionChanged`; `ICoreWebView2MoveFocusRequestedEventArgs` (3–5) and its handler; `WebView2Values.FocusProgrammatic` |
| `tests/Spar.Tests/WebView2SlotTests.cs`, `tests/static-state/reviewed.txt` | The four new slots required; the generator's three new static fields reviewed |
| `tests/apps/SecurityProbe/frontend/src/main.js` | The probes `focus` (three fields and a `<select>`, every focus change and key reported, F8 state, F9 a 25 s hang) and `visibility` (50 ms interval callbacks and animation frames counted each second, every visibility, size and lifecycle change) |

## Results

Each line says how it was seen: **observed** (the page reported it), **real input** (Windows Sandbox), or **logged** (Spar's own line only).

### Focus and typing (real input, Windows Sandbox)

| Step | Baseline (before step 6) | Current |
|---|---|---|
| A. Title bar clicked (activation, no click into the page), then `abc` typed | nothing reached the page: no key, `hasFocus` false | `key a`, `b`, `c` on the body; `hasFocus` true |
| B. Tab five times | nothing | focus `first` → `second` → `last` → `choice` (document order), then Tab past the last field came back to `first` (one `blur`/`focus` of the window between); `t` typed into `first` |
| C. Shift+Tab before the first field | nothing | focus to `choice`, the document's last; `s` reached it |
| D. Another process's window takes the foreground, Alt+Tab back, `r` | nothing | focus back on `choice`; `r` reached it |
| E. `SC_MINIMIZE`, then Alt+Tab restores the window, `m` | nothing; `visibilitychange` hidden and visible | `visibilitychange` hidden and visible; focus on `choice`; `m` reached it |
| F. F9: the page hangs 25 s; the hung-page dialog appears and is taken down by the host when the page answers; `h` | no key reached the page, so no hang | dialog about 15.9 s after F9, in the foreground; taken down when the page answered (`the page answered again after 23.8 s`); the window back in the foreground with the page focused; `h` reached `choice` |
| G. F9 again; Enter on the dialog (Wait); `k` | as F | dialog about 15.9 s after F9, in the foreground; Enter chose Wait (logged) and closed it and the window was the foreground again; `k`, typed while the page still hung, reached `choice` when the hang ended |
| H. A click into the `<select>` | the first click gave the page focus; from then on keys reached it | (already focused) |
| I. Alt+F4 | the page saw the keys; the window closed | the same |

Without step 6, the only way into the page was a click into it, as the research review said. With it, every activation path tried gave the page the keyboard, and Tab never left focus on the window itself. The window summary line of the current run (`host.log`): 9 focus moves, 2 Tab past the page, 2 visibility changes (the minimize and the restore), 22 moves (most from the drag); exit code 0.

### The `<select>`'s list after the window moved (real input, Windows Sandbox)

The window was dragged by its title bar by 150, 100 px. The list's position (a top-level window of the WebView2 browser process):

| How the list was opened | Baseline | Current | Expected |
|---|---|---|---|
| A click, before the move | 268,215 | 268,215 | 268,215 |
| **Alt+Down, after the move** | **268,215 (the old place)** | 418,315 | 418,315 |
| A click, after the move | 418,315 | 418,315 | 418,315 |

The crops [baseline](m1b-host-details/logs/sandbox/select-keyboard-after-move-baseline.png) and [current](m1b-host-details/logs/sandbox/select-keyboard-after-move-current.png) show the list left behind beside the moved window. A click updates the browser's idea of the window's place by itself; the keyboard does not, so without `NotifyParentWindowPositionChanged` keyboard users get popups in the wrong place.

### Visibility and throttling (desktop, offscreen, two runs each)

Per second while in each state: 50 ms interval callbacks and animation frames (the first second of a change is mixed).

| Window state | Baseline | Current |
|---|---|---|
| Shown, offscreen | `visible`, 20 intervals, 58–60 frames | the same |
| Minimized | `hidden` 32–47 ms after the minimize, by the browser itself; 1 interval, 0 frames | `hidden` 33 ms after; 1 interval, 0 frames |
| Restored | `visible` 38–40 ms after | `visible` 7 ms after |
| **Hidden (`SW_HIDE`)** | **still `visible`: 20 intervals, 60 frames for the 8 s hidden** | `hidden` 37–38 ms after; 1 interval, 0 frames |
| Shown again | — | `visible` 7 ms after |
| Moved twice | — | 2 `NotifyParentWindowPositionChanged` (plus 2 for the minimize and restore); no resize |

The times are upper bounds: the run reads standard error every 20 ms. Minimized, the page saw no `resize`, in either build (the host now also keeps the WebView's last size while minimized rather than giving it 0×0). No `freeze` event was seen in 8 s.

**A hidden or minimized page still answers:** a close request on a minimized or hidden window pinged the page, which answered within 1 ms, and the window closed in 16–20 ms, as when shown. The heartbeat itself does not ping such a page.

### The teardown leak (desktop and Windows Sandbox)

Handle counts before the cycles / after cycle 1 / after the last cycle (change from cycle 1 to the last). Each cycle loaded its page (`ready` from the page) in 84–255 ms.

| Run | Probe | Browser | Network service | GPU | Renderer | Storage |
|---|---|---|---|---|---|---|
| Desktop, offscreen, plain, 30 | 350/350/352 (+2) | 1170/1192/1398 (+206) | 434/426/428 (+2) | 414/778/804 (+26) | 245/257/309 (+52) | 249/249/249 (0) |
| Desktop, offscreen, mitigated, 30 | 350/350/352 (+2) | 1168/1190/1394 (+204) | 429/421/423 (+2) | 441/778/810 (+32) | 245/257/309 (+52) | 249/249/249 (0) |
| Desktop, hidden, plain, 30 | 348/348/350 (+2) | 1171/1192/1199 (+7) | 429/421/426 (+5) | 391/755/768 (+13) | 245/246/261 (+15) | 249/249/249 (0) |
| Desktop, offscreen, plain, 100 | 350/350/351 (+1) | 1170/1192/1402 (+210) | 425/425/415 (−10) | 387/778/882 (+104) | 245/257/320 (+63) | 249/249/249 (0) |
| Desktop, offscreen, mitigated, 100 | 350/350/351 (+1) | 1170/1192/1401 (+209) | 429/429/421 (−8) | 414/778/880 (+102) | 245/257/325 (+68) | 249/249/249 (0) |
| Sandbox, shown, plain, 30 | 279/279/282 (+3) | 1108/1141/1304 (+163) | 383/383/383 (0) | 245/500/513 (+13) | 245/257/304 (+47) | 195/195/195 (0) |
| Sandbox, shown, plain, 30 (again) | 276/276/276 (0) | 1129/1203/1328 (+125) | 378/378/378 (0) | 251/485/510 (+25) | 245/257/304 (+47) | 195/195/195 (0) |
| Sandbox, shown, mitigated, 30 | 276/276/277 (+1) | 1133/1156/1327 (+171) | 380/380/378 (−2) | 285/485/504 (+19) | 245/257/304 (+47) | 195/195/195 (0) |

- **The network service does not leak** with visible windows, offscreen or on the screen, in 100 cycles: −10 to +5 handles, the same with and without the mitigation. The earlier finding does not reproduce on Runtime 157, so the mitigation is not adopted.
- **The browser process** grows in one step at cycle 2 (+100 to +190 with a visible window, +8 hidden), then by 17–24 handles over the next 28 cycles, and by 8–9 from cycle 25 to cycle 100, the same with the mitigation. The renderer grows in one step of 47–68 somewhere between cycles 10 and 50. Neither keeps growing.
- **The GPU process gains about one handle per visible window opened and closed** (+102 and +104 in 100 cycles, linear: 776, 804, 830, 849, 882 at cycles 2, 25, 50, 75, 100), with or without the mitigation; hidden windows gained 13 in 30. It is the browser's, the mitigation does not change it, and at one window a run it is irrelevant to a Spar app; window tests that share one environment for hundreds of windows (step 8) see it.
- **The probe's own process** stays within 3 handles, and its GDI and USER objects return to the same counts after each cycle.

**Without an anchor** the browser ends after each cycle's controller closes, and the next controller, created about 1 s later, took **5.7 s** (2 of 2 cycles) against 0.28 s for the first, with a new browser process. The environment object alone does not keep the browser alive.

### Size

The Notebook's NativeAOT executable is 5,010,944 B (+11,264 B from 4,999,680 B), within the 6 MB budget; the probe 4,241,920 B (+11,264 B), 0 warnings.

## Not verified

- IME candidate windows after a move: no IME in the guest. The `<select>` is the only popup checked.
- Focus with the page-drawn title bar and its caption overlay window (not built yet), with Narrator, at a DPI other than 96, and on a real desktop with the foreground rules of a real session: the guest grants the foreground more freely (pitfall "Windows Sandbox").
- The shutdown prompt ("Keep waiting / Exit now") and focus: it has no owner and the window is gone by then.
- A modal dialog of the close guard (`NativeDialog.ConfirmClose`) over the page, and focus after it; it is modal on the UI thread and disables the window as the hung-page dialog does, so the same guard applies, but it was not run.
- Restoring a window that was minimized while the hung-page dialog was open.
- Minimized activation from the taskbar button itself (Alt+Tab was used), and a start minimized from a shortcut.
- Throttling after more than 8 s hidden (Chromium's intensive throttling begins after 5 min).
- M3's tray state, and the "memory while minimized" policy (later).
- Windows 10, an older Runtime, Linux.
