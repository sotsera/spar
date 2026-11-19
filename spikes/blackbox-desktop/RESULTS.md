# Spike: the black-box fixture on a desktop of its own

M1b step 8b, before the fixture (decision "Step 8b's shape: the black-box fixture", point 3). The question: can the
fixture launch the app's Production executable on a separate Windows desktop (`CreateDesktop`, `STARTUPINFO.lpDesktop`),
so nothing shows on the monitor and focus is never taken, and still see and drive it through UI Automation? And what does
the fallback (the user's desktop, `STARTF_USESHOWWINDOW` + `SW_SHOWNOACTIVATE`) cost?

Run on 2026-11-19 on the developer's Windows 11 machine (10.0.26320, WebView2 Runtime 157), against the Production
NativeAOT Notebook (`samples/Notebook`, `dotnet publish -c Release`, 5,234,688 B). The spike is a CoreCLR console app
(`BlackBoxDesktop.csproj`) that compiles the fixture's own process, desktop and UI Automation sources from
`src/Spar.Testing/BlackBox/` by link, so its numbers are for the declarations the fixture ships. Each launch has its own
data root under `artifacts/blackbox-spike/` with `LOCALAPPDATA`, `APPDATA`, `TEMP` and `TMP` pointed there, a job object
with kill-on-close, and, in the hidden mode, a desktop of its own, created for the launch and closed after it.

The fixture's sources changed after these runs (member order, the desktop named without its window station, the hung-app limit); the spike still builds against them, and the numbers are those of the sources as they were then.

Verbs: `run hidden|shown <count>`, `dump`, `windows`, `types`, `relaunch`, `startfail`. `SPIKE_EXE`, `SPIKE_APP_ID` and
`SPIKE_WAIT_FOR` point it at another executable (used once, for WindowHost's tree). Raw evidence: `results/`.

## Verdict

**The hidden desktop works: 50 of 50 launches** reached `ui-ready`, were found, queried, driven and closed with exit 0,
with nothing ever on the input desktop and the foreground never taken. The fixture uses it. The fallback took the
foreground in both launches tried (below), so it is no fallback for a machine someone is using.

## Numbers

`run hidden 50` (`results/run-hidden-50.txt`), from `CreateProcess`:

| Step | Median | Min | Max |
|---|---:|---:|---:|
| `ui-ready` line in the app's log | 385 ms | 377 | 541 |
| UI Automation finds the top-level window | 391 ms | 385 | 547 |
| the page's heading "Start your notebook" found by name | 497 ms | 449 | 619 |
| Invoke "New page", until the dialog's "Create page" is found | 2,062 ms | 2,040 | 2,082 |
| `WindowPattern.Close` until the app exits (exit 0 in 50 of 50) | 34 ms | 31 | 38 |
| ... until every process of the job is gone | 126 ms | 97 | 133 |

The fallback on the user's desktop (`results/run-shown-2.txt`, two launches): `ui-ready` 605 and 623 ms, the window 996
and 1,158 ms, the heading 2,006 and 2,164 ms, Invoke until the dialog 985 and 1,533 ms, close 478 and 486 ms.

- **Rendering and the page's tree:** WebView2 renders there and the page connects its bridge (`ui-ready` in every
  launch). Chromium's tree is complete: the dump (`results/dump-hidden.txt`) has the page's headings, buttons, landmarks
  and text, each with its ARIA role in `UIA_AriaRolePropertyId` (`heading`, `button`, `navigation`, `main`,
  `complementary`, `searchbox`; plain text `description`), and the window's own title bar buttons.
- **Invoke takes 2.0 s on the hidden desktop**, measured inside the call: `IUIAutomationInvokePattern::Invoke` itself
  returns after 2,010–2,035 ms, and a second UI Automation thread polling meanwhile saw the dialog only after it returned
  (2,056–2,072 ms, `results/run-hidden-50.stderr.txt`). The page acts on the click only then. On the user's desktop the
  same call took 473 and 1,068 ms. Not explained; probably a wait inside the browser for a frame that a desktop nobody
  composes does not produce. Invoke is the only slow operation; finding and reading are fast.
- **Never on the monitor, never the focus:** a watcher on the spike's own (input) desktop sampled `GetForegroundWindow`
  and `EnumWindows` every 10 ms during each launch: 0 samples with an app process in the foreground and 0 visible windows
  of the app's processes, in 50 of 50.
- **The desktop and the processes are gone after close:** 8 processes started per launch (`results/process-types-hidden.txt`:
  the app, the browser, crashpad, the GPU process, two utility processes and a renderer running; one more had already
  exited), all gone 97–133 ms after the close request; `OpenDesktop` on the desktop's name failed once its handle was
  closed, in 50 of 50.
- **The GPU process** starts and stays up on the hidden desktop (`--type=gpu-process` running 1.5 s after `ui-ready`, the
  profile's `GPUCache` written); the same 8 processes start as on the user's desktop, so it does not crash and restart.
- **The data root holds:** the real `%LOCALAPPDATA%\com.example.Notebook` did not exist before and did not exist after
  the 50 launches.
- **Kill with the parent:** when the spike process was ended from outside (`taskkill /F` on its own pid) while two
  Notebook instances ran, both were gone at once and no `msedgewebview2` process with this repository in its command line
  was left (`Get-CimInstance`): the job's kill-on-close.

## Native messages through UI Automation

`relaunch hidden` and `startfail hidden` (`results/relaunch-hidden.txt`, `results/startfail-hidden.txt`):

- **A relaunch to a lock nobody serves** (the spike holds `instance.lock` with no sharing): the task dialog after
  5,125–5,136 ms, read whole (title `Notebook`, texts "Notebook can't start" and "The app is still running or closing,
  and did not let another start within 5 seconds. ...", buttons "See details" and "Close"), "Close" invoked, exit 8 in 15
  ms.
- **A relaunch to a hung first instance** (every thread of the first suspended): the dialog after 2,093 ms ("... did not
  answer within 2 seconds ..."), answered, exit 8.
- **A start whose browser profile folder is a file:** the dialog after 139 ms, owned by the app's window, exit 5 (see
  Found).

## Found

- **A search from the desktop's root stalls on a hung app.** The first version found the app's windows with
  `FindAll(TreeScope_Children)` on the root element and a process-id condition; with a suspended first instance on the
  same desktop, that search did not return within 15 s, because UI Automation asks every top-level window there. Finding
  the app's windows by process id with `EnumWindows` and `GetWindowThreadProcessId` (no messages) and reaching each by
  handle (`ElementFromHandle`) fixed it. The fixture does that, and abandons any call after 10 s with its thread.
- **The app has more top-level windows than its main one:** the input indicator's `UAC_InputIndicatorOverlayWnd` and
  `UAC Input Indicator`, visible, unowned, of the app's process. Taking the first visible unowned window failed 50 of 50
  in one run; the fixture takes the first whose UI Automation control type is Window.
- **The fallback takes the foreground.** Two launches with `SW_SHOWNOACTIVATE` through `STARTUPINFO` (which the host's
  first `ShowWindow(SW_SHOWDEFAULT)` takes): the app owned the foreground in 64 and 62 of the 10 ms samples, about 0.6 s
  each, until it closed. The spike then stopped running the fallback on the user's desktop. Not investigated further:
  something in the start activates the window after it was shown without activation.
- **A browser profile folder that cannot be created is reported as a broken Runtime:** exit 5 and a message that tells
  the user to reinstall the WebView2 Runtime, while the cause is the app's own folder (the log has the `IOException`).
  Reported for the host; not changed by this step.

## Not measured

- Windows 10, other Runtimes, a GPU-less or remote session, high DPI; a locked workstation or a disconnected session.
- Whether page timers and `requestAnimationFrame` run at full rate on the hidden desktop (the page may count as hidden or
  occluded); the 2 s Invoke suggests frames are not produced there.
- Real input: it never reaches a desktop that is not the input desktop, which is the point; real-input checks go to
  Windows Sandbox (step 8c).
- The fallback beyond two launches.
