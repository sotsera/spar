# The wait for a new browser after the last controller closes: results

> A spike. Its code was deleted with `spikes/` at the start of M2; it is in git history at [`spikes/webview-restart-wait/`](https://github.com/sotsera/spar/tree/24f5db0942833047c81fa692908410596f3b8960/spikes/webview-restart-wait) (commit `24f5db0`), and this record and its `results/` moved here. Paths under `spikes/` below name that history.

Recorded 2026-11-18. Windows: Windows 11 Pro 10.0.26320, SDK 11.0.100, WebView2 Runtime 157.0.4341.60 (Evergreen; step 6 ran on 157.0.4341.46), `Microsoft.Web.WebView2` 1.0.4314.35 (loader only, linked statically). Every window was shown offscreen at (-32000, -32000) with `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` and `SW_SHOWNOACTIVATE` (or never shown, where a cell says "hidden"); no input was sent; every user-data folder was under `artifacts/webview2-profiles/webview-restart-wait/` and was deleted once its `lockfile` was gone. Linux: WSL Ubuntu 24.04 (kernel 6.18.58.1-microsoft-standard-WSL2), WebKitGTK 2.52.6 (6.0 API), GTK 4.14.5, on a private Xvfb `:91` with WSLg's `DISPLAY` and `WAYLAND_DISPLAY` unset. No `msedgewebview2`, probe, Xvfb or WebKit process of the spike was left behind; no system setting was changed.

**Question.** In M1b step 6 a controller created about 1 s after the environment's last controller closed took 5.7 s (2 of 2), in a new browser; `spikes/window-automation` saw no such wait in 8 of 8. What triggers it, can a Spar app hit it, is there a fix other than an anchor controller, and does WebKitGTK 6.0 have an equivalent?

## Short answer

- **It is SmartScreen, through the old browser's slow exit.** When the last controller closes within about 0.5 s of a navigation, the browser cannot exit for **6.47–6.56 s** (174 of 176 browsers) because SmartScreen's reputation request for that navigation is still open. A controller created on the same folder in that time starts a new browser process at once, but the new browser completes the controller only **about 0.15 s after the old one exits**. The wait is therefore *6.5 s minus the gap*, plus a normal start: 6.56 s at a 100 ms gap, **5.65 s at 1 s** (the step-6 number), 1.66 s at 5 s, none at 8 s.
- **Step 6 had SmartScreen on.** Its leak probe (`docs/evidence/m1b-host-details/LeakProbe/`) creates the environment with no options and never sets `IsReputationCheckingRequired`; it closed each controller as soon as its page answered, and waited 1 s. `spikes/window-automation` set `IsReputationCheckingRequired = false`, so it could not see the wait.
- **With SmartScreen off nothing waits:** the old browser exits 137–330 ms after the close and the next controller takes 203–291 ms in a new browser, at every gap (90 restarts). Spar always sets it off before the first navigation, so **a Spar app does not hit the wait today**: a relaunch of the probe with SmartScreen off took 215–256 ms (9 of 9), and the real `SecurityProbe` app relaunched 10 times on one folder reached its controller in 233–315 ms. With SmartScreen on, a relaunch within the 6.5 s waited 6.14–6.20 s (9 of 9).
- **Fixes:** SmartScreen off (`IsReputationCheckingRequired = false`) or `--disable-features=msSmartScreenProtection` remove it; so does closing 0.75 s or more after the navigation. Another user-data folder per cycle avoids it (a second browser runs in parallel). A controller that stays alive (an anchor), creating the next controller before closing the last, or creating it within 60 ms of the close all keep the browser and avoid it. **Waiting for `BrowserProcessExited` or the process exit does not help** (it waits the same 6.5 s), nor does a new environment object, InPrivate, a named profile or a hidden window.
- **Linux has no equivalent.** WebKitGTK has no browser process to wait for: the app is the UI process. The next view loaded in 83–96 ms (p50 per cell; max 176 ms) in 290 restarts at gaps from 0 to 8 s, with the default session, an ephemeral or persistent session per view, or one reused session; 0 multi-second cases.
- **Recommendation for window tests:** the anchor is no longer needed to avoid a 5.7 s wait, since test windows have SmartScreen off. It still saves about 170 ms per test (a controller on a live browser 49–88 ms against 203–291 ms in a new one), and it makes the tests immune to anything that slows the browser's exit. Keep it if that saving matters, as a **default-profile** controller, never InPrivate (`spikes/window-automation`, "Isolation"); otherwise drop it with no wait to fear. Details under [Recommendation](#recommendation).

## What was built

| Path | Contents |
|---|---|
| `Win/` | `RestartWait.exe`, NativeAOT (0 warnings): one STA with a message pump, one environment, and cycles 0..N that each create an offscreen window and a controller, navigate to a page served from memory on `https://app.localhost` through `WebResourceRequested` (as the host serves its page), and close it (`Close`, then `DestroyWindow`, as the host does). Cycle 0 is the process's first controller; cycles 1..N are the restarts. It tracks each browser's pid (`BrowserProcessId`), its exit through a process handle on a thread of its own, and `BrowserProcessExited`; `--sample true` lists the folder's browser processes every 250 ms while a creation waits. Options: gap, SmartScreen, dwell before the close, profile kind, shown or hidden window, environment kept or new, wait for the exit (event or process), folder per cycle, overlap (next controller before the close), browser arguments, exit without waiting. `Interop/` is copied from `spikes/window-automation/Shared/Interop` with `ICoreWebView2BrowserProcessExitedEventArgs` added |
| `Linux/` | `RestartWait.Linux`, NativeAOT (0 warnings, 1,520,712 B): GTK4 + WebKitGTK 6.0 through hand-written `LibraryImport`, the GLib main context pumped by the probe, `spar://app/` served by a URI scheme handler. Cycles create a window and a `WebKitWebView` with a given `WebKitNetworkSession`, wait for `load-changed` FINISHED and destroy the window. Every descendant process (web, network, GPU, `bwrap`, `xdg-dbus-proxy`) is tracked through `/proc` every 5 ms |
| `scripts/win.sh`, `scripts/matrix-win.sh` | One probe run or a relaunch series (Git Bash), and the Windows matrix; logs are redacted into `results/win/` |
| `scripts/spar-relaunch.sh` | The real app case: `tests/apps/SecurityProbe` (Production NativeAOT, as published by the M1b runs; not rebuilt) started 10 times on one `LOCALAPPDATA`, closed with `WM_CLOSE` at `ui-ready`, driven by `docs/evidence/m1b-page-security/SecurityRun.cs` from a copy outside the repository |
| `scripts/linux/` | `setup.sh` (Xvfb and xkbcomp with `apt-get download`, unpacked under `/var/tmp/spar-linux/webview-restart-wait/tools`, no root), `publish.sh`, `run.sh` (one Xvfb, each cell with a fresh `HOME` and XDG folders under the spike's folder and a private D-Bus session), `matrix.sh` |
| `scripts/summarize-win.mjs`, `scripts/summarize-linux.mjs`, `scripts/netlog-summary.mjs` | The tables below from the raw logs; the net-log excerpts |

```sh
# Windows (Git Bash, from spikes/webview-restart-wait)
scripts/win.sh build && scripts/matrix-win.sh
scripts/win.sh relaunch relaunch-on-0 10 0 --smartscreen on
RUNNER_DIR=<folder outside the repository> scripts/spar-relaunch.sh spar-relaunch-0 10 0
node scripts/summarize-win.mjs
# Linux (WSL Ubuntu-24.04)
MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/spikes/webview-restart-wait/scripts/linux/setup.sh
MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/spikes/webview-restart-wait/scripts/linux/publish.sh /mnt/<repo>
MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/spikes/webview-restart-wait/scripts/linux/matrix.sh
node scripts/summarize-linux.mjs
```

Raw evidence: `results/win/<cell>.log` (one probe process each; `matrix-summary.txt` has the probe's own summary lines), `results/win/netlog-excerpts.txt`, `results/linux/<cell>.log`. The Linux matrix ran in WSL at the same time as part of the Windows matrix; the effect measured is seconds, far above that noise.

## Windows results

Unless a row says otherwise: one environment kept for the run, the default profile, a shown offscreen window, the close right after the first `NavigationCompleted`, and 10 restarts per cell. "Next controller" is from `CreateCoreWebView2ControllerWithOptions` to its completion; "to first navigation" adds the time to the next page's `NavigationCompleted`. "New browser" counts restarts whose controller got another browser pid.

### By gap and SmartScreen (observed)

| Gap after `Close()` | SmartScreen on: next controller p50 (min–max), ms | ≥ 2 s | New browser | Old browser's exit after its close | SmartScreen off: next controller p50 (min–max), ms | ≥ 2 s | New browser | Old browser's exit after its close |
|---|---|---|---|---|---|---|---|---|
| 0 (same message) | 88 (71–616) | 0 | 0 | browser kept | 82 (70–612) | 0 | 0 | browser kept |
| 10 | 77 (62–79) | 0 | 0 | browser kept | 63 (47–79) | 0 | 0 | browser kept |
| 30 | 63 (46–79) | 0 | 0 | browser kept | 78 (62–79) | 0 | 0 | browser kept |
| 60 | 63 (61–79) | 0 | 0 | browser kept | 63 (46–63) | 0 | 0 | browser kept |
| 100 | **6,556** (6,511–6,574) | 10 | 10 | 6,517 (6,469–6,549) | 236 (221–253) | 0 | 10 | 158 (143–177) |
| 250 | **6,406** (6,373–6,429) | 10 | 10 | 6,519 (6,466–6,547) | 221 (203–269) | 0 | 10 | 149 (137–175) |
| 500 | **6,148** (6,133–6,186) | 10 | 10 | 6,497 (6,472–6,519) | 235 (205–253) | 0 | 10 | 148 (142–162) |
| 1,000 | **5,653** (250–5,669) | 9 | 10 | 6,508 (213–6,527) | 222 (219–251) | 0 | 10 | 148 (139–175) |
| 2,000 | **4,652** (4,624–4,770) | 10 | 10 | 6,513 (6,473–6,559) | 229 (218–267) | 0 | 10 | 150 (143–178) |
| 3,000 | **3,660** (3,640–3,674) | 10 | 10 | 6,517 (6,479–6,551) | 220 (204–236) | 0 | 10 | 157 (139–167) |
| 5,000 | **1,663** (1,628–1,701) | 0 | 10 | 6,512 (6,490–6,528) | 220 (205–222) | 0 | 10 | 152 (142–174) |
| 8,000 | 221 (203–252) | 0 | 10 | 6,512 (6,482–6,534) | 220 (204–222) | 0 | 10 | 154 (143–177) |

- **The wait is the old browser's remaining life plus a start:** at every gap from 100 ms to 5 s, the next controller completed 150–170 ms after the old browser's exit (`gap + wait ≈ 6.5 s + 0.16 s`).
- **Up to 60 ms after the close, the next controller attaches to the same browser** (same pid, 46–88 ms), and the browser lives on. At 100 ms the browser has already decided to end, even with SmartScreen off. Occasional 600–616 ms creations on a live browser (7 of 180 creations that kept the browser, in every kind of cell) are not explained.
- **The one fast restart at 1 s** followed a cycle-0 browser that exited 213 ms after its close; that browser's first navigation had itself taken 5,069 ms (below), so SmartScreen's request was finished when the close came. Two first browsers in all exited fast (213 and 908 ms); 174 of 176 browsers closed right after a navigation with SmartScreen on took 6.47–6.56 s.
- **The new browser starts at once and waits** (`results/win/sample-on-gap1000.log`): during each 5.6 s creation, the folder had two browser processes, the old pid and the new one, from the first sample (+257 ms) until the old one exited; the controller came about 150 ms later.
- **`BrowserProcessExited` came 5–12 ms after the kernel exit** (p50 per cell), not about 160 ms as the pitfall "`BrowserProcessExited` fires about 160 ms after the real kernel exit" says. A released environment object raises no event for its browser (`logs-on-1000.log`, browser 2).

### Other conditions and candidate fixes (observed)

| Cell | Next controller p50 (min–max), ms | ≥ 2 s | New browser | Time spent waiting before the create, p50 | Result |
|---|---|---|---|---|---|
| InPrivate, SmartScreen on, gap 1 s | 5,653 (5,622–5,691) | 10 | 10 | — | waits |
| Named profile, SmartScreen on, gap 1 s | 5,675 (5,662–5,699) | 10 | 10 | — | waits |
| Hidden (never shown) window, SmartScreen on, gap 1 s | 5,629 (5,614–5,642) | 10 | 10 | — | waits |
| New environment object per controller, same folder, SmartScreen on, gap 1 s | 5,672 (5,629–5,702) | 10 | 10 | — | waits (the environment, 1–6 ms, does not keep or replace the browser) |
| Close 3 s after the navigation (idle), SmartScreen on, gap 1 s | 214 (202–236) | 0 | 10 | — | **no wait**; old browser exit 206–744 ms |
| `--disable-features=msSmartScreenProtection`, setting on, gap 1 s | 237 (205–283) | 0 | 10 | — | **no wait**; exit 141–165 ms |
| Wait for `BrowserProcessExited`, then create; SmartScreen on | 206 (204–222) | 0 | 10 | **6,526** | no gain: the same 6.5 s is spent before the create |
| Wait for the process handle, then create; SmartScreen on | 220 (206–271) | 0 | 10 | **6,525** | no gain |
| Wait for `BrowserProcessExited`; SmartScreen off | 237 (205–270) | 0 | 10 | 167 | no wait either way |
| Another user-data folder per cycle, SmartScreen on, gap 0 | 270 (249–304) | 0 | 10 | — | **no wait**; the old browser lives 6.5 s beside the new one; a folder per test |
| Next controller created before the last one closes (overlap), SmartScreen on | 49 (37–57) | 0 | 0 | — | **no wait**: one browser throughout (an anchor in another form) |
| Gap 0: create in the same message as the close, SmartScreen on (also InPrivate, named, new environment, idle) | 45–88 p50 | 0 | 0 | — | **no wait**: the browser is kept |

Raw logs: `results/win/{private,named,hidden,envnew,idle,disablefeature,waitevent,waitprocess,percycle,overlap}-*.log`.

### Relaunch: a new process on the same folder (observed)

The probe ran 10 times in a row on one root, one controller each (`--reps 0`), closing right after the first navigation and exiting without waiting for the browser, as Spar does (`results/win/relaunch-*.log`).

| Series | Controller of processes 2–10, ms |
|---|---|
| SmartScreen on, next process at once | **6,139–6,195** (9 of 9) |
| SmartScreen on, next process 3 s after the previous one exited | **3,082–3,156** (8 of 9); 250 once, after a browser that had exited fast |
| SmartScreen off, next process at once | 215–256 (9 of 9) |
| `SecurityProbe` (a Spar app, Production NativeAOT), closed at `ui-ready`, 10 starts on one `LOCALAPPDATA` | 233–315 for all 10 (`results/win/spar-relaunch-0.log`; the driver's `dotnet run` start-up put about 1 s between one exit and the next start) |

### Mechanism

- **Observed (net logs, `results/win/netlog-excerpts.txt`):** with SmartScreen on and the close right after the navigation, each browser's net log shows SmartScreen's `nav-edge.smartscreen.microsoft.com/api/browser/edge/navigate/3` request starting 58–72 ms into the log, its TCP connection established, and its TLS handshake reaching certificate verification at +253–285 ms. **That verification job never got a worker task** (no `CERT_VERIFIER_TASK_BOUND`), and it was cancelled, with the request, when the browser exited 6.3 s later (3 of 3 browsers; in the first browser a second job, for `config.edge.skype.com`, stayed unscheduled too). With the close 3 s after the navigation, the same job ran within 1 ms, the request finished at +319 ms, and the browser exited 243 ms after the close. With SmartScreen off the net logs hold no SmartScreen request at all (2 of 2 browsers; this answers part of decision "SmartScreen default in WebView2"'s "Not verified").
- **Inferred:** once the last controller closes, the browser starts its shutdown and stops running new background work, so a SmartScreen request still in its handshake can never complete; SmartScreen's shutdown waits for its open request and gives up after about 6.3 s. The earlier research's ETW trace (pitfall "The browser process can take about 6.5 s to exit") saw that wait as about 101 sleeps of about 60 ms. The exact rule that stops the certificate task was not established.
- **Observed:** a controller requested while the old browser lives in that state starts a new browser process immediately, and that process completes the controller about 150 ms after the old one exits.
- **Inferred:** the new browser waits for the old one to release the user-data folder (one browser per folder; the folder's `EBWebView\lockfile` is held for as long as a browser lives, decision "Window-test profiles…"). Whether it waits on that file or on a message to the old process was not established.
- **Why the new controller sometimes reuses the old browser:** up to 60 ms after the close (and in the same message) the browser has not yet started its shutdown; observed, not explained further.

### Other 5 s waits seen on the way (observed, not the restart wait)

- **A named profile's first controller in a fresh folder took 5,226–5,297 ms in 5 of 15 processes** (`results/win/named-first-*.log`, SmartScreen off), 247–289 ms otherwise, plus 1 of 3 in the matrix (5,276 ms, `named-off-0.log`). It is a different wait: no other browser existed (one browser pid in every sample), and the browser's net log was idle from +8 ms to +5,006 ms (the slow `named-first-4`) where the fast one started its requests at +42 ms. So it is not network-bound; its cause was not found. InPrivate and the default profile never showed it (window tests use InPrivate).
- **A new browser's first navigation took 4,926–5,069 ms in 4 of about 386** (`gap-on-1000`, `private-on-1000`, `idle-on-1000`, `disablefeature-on-1000`, the last with SmartScreen off), and never on a browser that was already running (0 of about 180). It looks like the "about 5 s between navigation started and first document" of `spikes/configuration-cost` and the pitfall "Rare 5 s stalls, unexplained"; not investigated further.

## Linux results (WebKitGTK 6.0, Xvfb)

The app is WebKit's UI process, so there is no browser process to restart. Each view got its own web process (with its `bwrap` sandbox and `xdg-dbus-proxy`); no GPU process ever started (Xvfb, software rendering). Per cell, 10 restarts after a first view; "to load finished" is from the window's creation to `load-changed` FINISHED.

| Cell (session, gap) | Next view to load finished p50 (min–max), ms | ≥ 2 s | New network process per restart | Previous web process exit after the close, p50 (min–max), ms | Network process after the last close |
|---|---|---|---|---|---|
| Default session, gaps 0, 100, 250, 500, 1,000, 2,000, 3,000, 5,000, 8,000 | 83–96 per cell (80–125) | 0 | 0 | 23–44 per cell (16–77) | alive 8 s later |
| Ephemeral session per view, gap 0 | 86 (84–94) | 0 | 0 (the old network process was reused) | 41 (38–73) | exit +19 ms |
| Ephemeral session per view, gaps 100 to 8,000 | 86–91 per cell (82–117) | 0 | 10 of 10 in every cell | 23–26 per cell (17–31) | exit +18 to +29 ms |
| One ephemeral session reused, gaps 0, 1,000, 5,000 | 85–88 (82–90) | 0 | 0 | 21–42 (18–69) | alive 8 s later |
| Persistent session per view (same folders), gaps 0, 1,000, 5,000 | 85–87 (82–146) | 0 | 0, 10, 10 | 24–41 (18–66) | exit +17 to +34 ms |
| One persistent session reused, gaps 0, 1,000, 5,000 | 84–87 (81–176) | 0 | 0 | 22–41 (17–66) | alive 8 s later |
| Hidden (never presented) window, default session, gap 1 s | 73 (71–77) | 0 | 0 | 8 (5–12) | alive 8 s later |
| Close 3 s after the load, default session, gap 0 | 87 (83–97) | 0 | 0 | 42 (40–50) | alive 8 s later |
| Relaunch: 10 processes in a row on one home, default session | first 540, then 180–235 | 0 | — | — | — |
| Relaunch: 10 processes, ephemeral session per view | first 514, then 179–201 | 0 | — | — | — |

- **No equivalent wait:** 290 restarts and 18 relaunches, none over 176 ms. A process's first view took 462–546 ms (the network process and the first sandbox start); a restart in an existing process 71–176 ms, whether or not a new network process had to start (it appears about 15 ms after the view is created).
- **The network process lives as long as its session:** with the default session or a reused one it stayed after the last view closed (8 s checked); when the view's own session was released it exited 17–34 ms after the window was destroyed. A per-view session created at once after the close (gap 0) reused the old network process.
- **Data folders:** a new persistent session on the same folders while the previous one's network process was still exiting waited for nothing (gap 0: 85 ms). Earlier evidence: WebKitGTK takes no lock on its folders (pitfall "A second instance on the same WebKitGTK data folders starts without any error and corrupts data").

## Recommendation

**Window tests.** The 5.7 s wait needs SmartScreen on; test windows have it off (Spar's default, and a requirement of decision "Test strategy: the app runs inside the test process"), so the anchor is not needed to avoid it. The choice is about speed and robustness:

| | Anchor (a default-profile controller kept for the process) | No anchor |
|---|---|---|
| Next test's controller | 49–88 ms on the live browser | 203–291 ms in a new browser (about +170 ms per test; +17 s per 100 tests) |
| A slow browser exit (SmartScreen turned on by a test or a future default, or another shutdown wait) | never felt | felt by the next test, up to 6.5 s |
| Isolation | must be a default-profile controller: an InPrivate anchor leaks state between InPrivate tests (`spikes/window-automation`) | a new browser per test isolates by itself |
| Long runs | the GPU process gains about one handle per window (step 6, +102 in 100 windows) | reset with every browser |

My recommendation: **keep a default-profile anchor** in the window-test host, for the 170 ms per test and because it removes the dependence on how the browser exits; keep `IsReputationCheckingRequired = false` in test windows regardless. If the user prefers simpler hosts, dropping the anchor is safe as long as SmartScreen stays off; nothing else here made a restart wait. Waiting for `BrowserProcessExited` between tests is not a fix (it waits the same time), and a folder per test costs a folder and a parallel browser.

**Apps.** Nothing to change today: Spar sets SmartScreen off before the first navigation, so neither a relaunch nor (later) a reopened last window waits. When the automatic switch of decision "SmartScreen default in WebView2" turns SmartScreen on for apps that allow other origins, such an app relaunched within about 6.5 s of a close that came within about 0.5 s of a navigation will wait up to 6.2 s for its window's page; that belongs in the pitfalls then, and a host that must reopen its last window at once can create the new controller before (or in the same message as) closing the old one.

**Linux.** No action: WebKitGTK has no restart wait, with any session arrangement.

## Not verified

- **The cause inside the browser:** the unscheduled certificate task and SmartScreen's 6.3 s shutdown wait are read from net logs; no ETW or symbols were used, and the 6.3 s limit is inferred from the timings and the earlier research's ETW.
- **What the new browser waits on** (the folder's lock file, or a message to the old browser): inferred from two processes coexisting and the 150 ms after the exit.
- **The named profile's 5.2 s first controller and the 5 s first navigations:** reproduced, not explained.
- **The occasional 600 ms creation on a live browser** (7 of 180).
- **A real Spar app relaunched within 1 s:** the driver's start-up left about 1 s between processes; the immediate relaunch was measured with the probe only. Spar's own multi-window case (closing the last window and opening another in one process) does not exist yet: the app ends when its window is destroyed.
- **The browser's own verbose log** (`--enable-logging --v=1 --log-file`) wrote only 4 lines per browser and showed nothing about the wait.
- **Other Runtime versions** (only 157.0.4341.60; step 6 ran on .46), Windows 10, a machine without network access (SmartScreen's request would fail fast or not start), proxies.
- **Linux on a real desktop** (WSLg, Wayland, a hardware GPU and its GPU process) and the 4.1 API; only Xvfb with software rendering ran here.
