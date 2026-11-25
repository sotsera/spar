# page-heartbeat results

> A spike. Its code was deleted with `spikes/` at the start of M2; it is in git history at [`spikes/page-heartbeat/`](https://github.com/sotsera/spar/tree/24f5db0942833047c81fa692908410596f3b8960/spikes/page-heartbeat) (commit `24f5db0`), and this record and its `results/` moved here. Paths under `spikes/` below name that history.

Checks the design in `docs/plan.md` section 3, "Hung pages are detected by a heartbeat". The measurements ran on 2026-11-14; this file was written afterwards from the raw logs. Nothing was re-run for it.

**Outcome: the heartbeat works as proposed, and 5 s × 3 misses is the right setting.**

- Every endless main-thread hang was detected on every host, 163 of 163 runs with the heartbeat armed. While the window was visible, detection took 15.2–20.0 s (median about 17 s), not "about 15 s". A hang that started while minimised was reported 20 s after restore, by design.
- No alarm came from legitimate work up to 10 s, a hung worker, or a minimised, covered, offscreen or hidden window: 0 of 123 runs.
- The engines do not replace the heartbeat. WebView2 raises nothing for an idle hang in 72 s. WebKitGTK's `is-web-process-responsive` never changes for one.
- Recovery needs the renderer terminated. Reload, same-site navigation and Stop + Reload never recover a hung page on either engine.

The design needs five changes:

1. "Reload page" terminates the renderer first.
2. The heartbeat arms when the document is created, not at `NavigationCompleted`.
3. Pinging also pauses while the WebView is hidden or suspended.
4. WebView2's own `ProcessFailed(RenderProcessUnresponsive)` maps to the same unresponsive state.
5. A hung page gets at most one outstanding ping.

## The question

An idle page stuck in a loop raises no event. The proposal: while a document is loaded and the window is visible, the host posts a small ping over the bridge every 5 s, and the injected script answers from its message handler. After three missed answers the page counts as unresponsive. The host then logs it, calls a C# hook, and by default shows a native "Wait / Reload page" dialog. Hidden or minimised windows are not pinged. The spike asked:

1. How each kind of hang is detected: time to "unresponsive", misses and false alarms.
2. Whether WebView2 raises `ProcessFailed` for an idle hang within 60 s, with and without pings, and whether pinging a hung renderer piles up memory or blocks the host's UI thread.
3. What WebKitGTK's `is-web-process-responsive` reports for each kind of hang, with and without pings, and whether `web-process-terminated` fires for a renderer killed from outside.
4. Whether false alarms come from a minimised, covered, offscreen, hidden or suspended window, heavy healthy load or CPU stress, and how much timers and handlers are throttled.
5. Recovery: reload and renderer termination, time to a working page, and whether backend state survives.
6. A recommendation for the interval, the miss count, hangs during load, and whether Linux should rely on `is-web-process-responsive`.

## Versions

| Item | Value | Source |
|---|---|---|
| Windows host | Windows 11 Pro 10.0.26320, 32 logical CPUs | environment, `ENV cpus=` |
| WebView2 Runtime | 157.0.4341.46, on the host and in the sandbox | `ENV version=` in every Windows log |
| WebView2 SDK headers | `Microsoft.Web.WebView2` 1.0.4314.35. 1.0.4365-prerelease was also searched for a renderer-terminate API: there is none (`Beat.Win/Host.cs`). | `Directory.Packages.props` |
| Windows Sandbox guest | 32 CPUs, user `WDAGUtilityAccount`, vGPU on, no network. The guest's Windows build was not recorded. | `results/sandbox/*/runs.txt`, `sandbox/start.sh` |
| Linux | Docker image `spar-startup-prereq:webkit`: Ubuntu 24.04.5 LTS, `libwebkitgtk-6.0-4` 2.52.6-0ubuntu0.24.04.1, `libgtk-4-1` 4.14.5+ds-0ubuntu0.10, Xvfb 1280×800 with openbox, 32 CPUs. GSK renderer cairo (`START gsk=cairo`). EGL had no DRI3, so rendering is software. | `dpkg-query` in the image (run while writing this file), `results/linux/runs.txt` |
| .NET | SDK 11.0.100. Both hosts are published with NativeAOT (`PublishAot`, `IsAotCompatible`). The Linux build ran in `spar-linux-desktop:webkit2.52` (`docker/build.sh`). | `global.json`, project files |
| Node | 24.21.0, used for `tools/*.mjs` | `node --version` |

The publish warnings were not kept in `results/`, and the builds were not repeated for this file.

## Method

**Hosts.** `Beat.Win` (WebView2, Win32 window, CsWin32) and `Beat.Linux` (GTK 4 + WebKitGTK 6.0) share `Shared/`:

- `Heartbeat.cs` is the design under test: a ping every `--interval` ms (default 5000). A tick that finds the previous ping unanswered counts a miss. `--misses` (default 3) consecutive misses log `HB-UNRESPONSIVE`. Any answer resets the count and logs `HB-RESPONSIVE`.
- `Session.cs` runs a second, ungated 1 s "probe" heartbeat that never alarms. Its round trips feed the offline simulation of other intervals.
- `Session.cs` also has a backend counter that each new document reads once (`state?`).
- The timed actions (`--actions "ms:verb[:arg]"`, relative to the first DOMContentLoaded) and the recovery timing are in `Session.cs`.
- The gate is `--gate window` (default): it pauses the heartbeat while the window is minimised or hidden, or while the WebView2 controller is hidden. `--gate none` keeps pinging, to measure what an ungated heartbeat would see.
- `PumpProbe.cs` measures UI-thread latency. A background thread posts to the UI context every ~1 ms and records the post-to-run delay per phase.

**Pages** (`www/`):

- `inject.js` stands in for the bridge. It runs before any page script, answers `ping:`/`probe:` from its message handler with the page's `visibilityState`, and reports `doc` (document created) and visibility changes.
- `app.js` posts a 1 s timer tick with the number of animation frames since the last tick, and runs the commands.
- `loadhang.html` hangs in an inline script while it is parsed.
- `worker.js` hangs a dedicated worker.

**Where the cases ran.**

- `scripts/run-win.sh`: on the Windows host, in a window at (-32000,-32000) that is never activated. There is no input, and no dialog or window-state change: the host refuses those outside Windows Sandbox and logs `REFUSED`. Each run gets a fresh profile under `artifacts/webview2-profiles/page-heartbeat/<case>-<n>`.
- `sandbox/cases.sh` through `sandbox/guest.sh`: in Windows Sandbox, on screen. This covers window states, real `SendInput` clicks and keys, and the native dialog.
- `docker/cases.sh`: in one throwaway container per group, as uid 1000 with private HOME, XDG folders and runtime folder under `/tmp/u`, on Xvfb with openbox. xdotool supplies window moves and clicks.

Hang commands start at a random 5–10 s after page ready, so their phase against the 5 s ticks varies between runs.

**Analysis.** Run these from `spikes/page-heartbeat/`:

- `node tools/report.mjs results/summary/report.md` writes the compact tables used below.
- `node tools/summarize.mjs <folder> <out>` writes per-case detail with event lists: `results/summary/details-*.md`.

Both read each log only up to its `FINISH` line, so teardown is not measured. Times in a log are milliseconds since process start.

## The cases

| Case | What it does | Where |
|---|---|---|
| `baseline`, `onscreen` | healthy page, 60 s | host, Linux; sandbox |
| `idle` | `for(;;){}` started from a timer, so the page is idle when it hangs | all three |
| `handler` | `for(;;){}` inside the page's message handler (the host's `cmd` message) | host, Linux |
| `microtask` | an endless promise chain (`Promise.resolve().then(again)`) | host, Linux |
| `busy3`, `busy10`, `busy20` | one finite task of 3, 10 or 20 s | host, Linux |
| `worker` | a dedicated worker in `for(;;){}`; the main thread stays free. It must not alarm. | host, Linux |
| `load-doc`, `load-contentloading`, `load-navcompleted` | first document hangs while parsed. The heartbeat is armed on the injected script's `doc` message, on ContentLoading (WebView2) or `committed` (WebKit), or on NavigationCompleted / `finished`. | host, Linux |
| `noinject` | healthy page with no injected script, armed at NavigationCompleted: every ping is lost | host, Linux |
| `anim`, `tasks100`, `tasks1000` | healthy heavy load: an element animated every frame with 8 ms of script per frame; 100 ms or 1000 ms tasks back to back | host, Linux, sandbox (`anim`) |
| `pf-pings`, `pf-quiet` | idle hang watched for 72 s, with pings, and with no host-to-page traffic at all (`--no-ping --probe-interval 0`) | host |
| `wk-pings`, `wk-quiet` | the same on WebKitGTK, watching `is-web-process-responsive` | Linux |
| `wk-click` | idle hang, no traffic, xdotool click into the page 5 s into the hang | Linux |
| `wk-busy` | 20 s task with an xdotool click 2 s into it | Linux |
| `input-click`, `input-click-quiet`, `input-key` | idle hang, then a real click (`SendInput`) 5 s in, with and without pings; then a click plus a key press | sandbox |
| `stress-hung`, `stress-healthy` | 1,000 messages of 1 KiB per second for 30 s from the UI thread into a hung or a healthy page. Private memory is sampled every ~1.5 s and pump latency throughout. | host, Linux |
| `reload` | idle hang, then the engine's reload 20 s in | host, Linux |
| `renav` | idle hang, then a same-site navigation to `/index.html` 20 s in | host, Linux |
| `stop-reload` | idle hang, then `Stop()` and a reload 100 ms later | host |
| `blank` | idle hang, then a navigation to `about:blank`, then to `/index.html` | host, Linux |
| `xsite` | idle hang, then a navigation to another origin (`https://other.localhost/`), then back | host |
| `edge-kill` | idle hang, then a navigation to `edge://kill` | host |
| `kill` | idle hang, then renderer termination 20 s in. On Windows the host terminates the renderer PID from `GetProcessInfos`. On Linux it calls `webkit_web_view_terminate_web_process`. The host then reloads once on its own (`auto-reload-after-exit`). | host, Linux |
| `kill-sigkill` | the same, but SIGKILL to the web process, as a crash or the OOM killer would do | Linux |
| `healthy-reload`, `healthy-kill` | reload or termination of a healthy page | host, Linux |
| `reload-load`, `kill-load` | hang during load, with the C# hook set to reload or terminate on the alarm | host, Linux |
| `ctl-hidden` | `put_IsVisible(false)` for 60 s, gate off | host, sandbox |
| `ctl-suspend`, `suspend` | controller hidden, then `TrySuspend` for 30 s, then `Resume`, gate off | host, sandbox |
| `mem-low` | `MemoryUsageTargetLevel` low for 30 s, gate off | host, sandbox |
| `minimize`, `cover`, `offscreen` | 60 s minimised, covered by another top-level window, or moved off the monitor; gate off | sandbox |
| `st-minimize`, `st-hide`, `st-cover`, `st-offscreen` | the same on Linux (`st-hide` unmaps the window); gate off | Linux |
| `gate-minimize-hang` | the design's gate: minimise, hang while minimised, restore 35 s later | sandbox, Linux |
| `cover-hang` | hang while covered. Covered is not gated. | sandbox |
| `dialog-reload`, `dialog-wait`, `dialog-recover` | the native dialog answered "Reload page" or "Wait" 3 s after it appeared, and a 25 s task that ends while the dialog is open | sandbox |

Runs: 142 on the Windows host (33 cases), 147 in the Linux container (35 cases) and 45 in Windows Sandbox (17 cases). Every run exited 0 and every log has its `FINISH` line.

## Results

### 1. Detection per kind of hang (5 s interval, 3 misses)

The table shows the time from the page's `hang` message to `HB-UNRESPONSIVE`, in seconds, as median (min–max). The alarm always came at the third miss.

| Hang | Windows host | Linux | Windows Sandbox |
|---|---|---|---|
| idle `for(;;)` | 5/5 alarmed, 15.8 (15.3–19.9) | 5/5, 17.2 (15.6–19.3) | 3/3, 16.9 (15.2–19.0) |
| in a message handler | 5/5, 18.5 (15.9–19.6) | 5/5, 16.4 (15.4–19.8) | — |
| endless microtasks | 5/5, 17.4 (15.9–19.5). The renderer then exits with `out-of-memory` 20.5–25.7 s after the start, and the one automatic reload recovers the page. | 5/5, 18.0 (15.2–19.7). No exit within 30 s. | — |
| busy 3 s / 10 s | 0/5 / 0/5 | 0/5 / 0/5 | — |
| busy 20 s | 5/5 alarmed at 17.8 (16.0–19.2), responsive again 2.2 (0.8–4.0) s later | 5/5 at 17.8 (15.5–19.1), responsive again 2.2 (0.9–4.5) s later | `dialog-recover`, a 25 s task: 3/3 at 16.0, then 9.0 s later the dialog closed by itself |
| worker only | 0/5 (correct) | 0/5 (correct) | — |
| hang during load, armed on `doc` | 5/5, 20.0 (20.0–20.0) | 5/5, 19.9 (19.9–19.9) | — |
| hang during load, armed on ContentLoading / `committed` | 5/5, 20.0 | 5/5, 19.9 | — |
| hang during load, armed on NavigationCompleted / `finished` | **0/5: never armed** | **0/5: never armed** | — |
| no injected script | 5/5 alarm at 20 s on a healthy page (expected: nobody answers) | 5/5 at 20 s | — |
| hang while covered | — | — | 3/3, 18.5 (16.4–19.1) |

Detection is 15–20 s, not "about 15 s". The first unanswered ping is the first tick after the hang starts, up to 5 s later, and three more ticks follow it. A hang during load measures 20 s because the heartbeat is armed about 0.25 s before the hang, so the first ping goes out at the 5 s tick. The 1 s probe adds traffic in all runs except the `*-quiet` ones. It did not change the detector's numbers: `pf-pings` 17.0 s and `wk-pings` 16.9 s match `idle`.

### 2. WebView2: ProcessFailed, memory, UI thread

**ProcessFailed.** An idle hang raised no `ProcessFailed` in 72 s, either with pings (`pf-pings` 3/3) or with no host traffic at all (`pf-quiet` 3/3). `ProcessFailed(RenderProcessUnresponsive)` did arrive in four cases:

| Trigger | Runs | ProcessFailed after the trigger |
|---|---|---|
| a real click into the hung page (sandbox) | 3/3 with pings, 3/3 without | 16.0 s (16.0–16.1) after the click, 21.0 s after the hang started |
| a click plus a key press | 3/3 | 15.0 s after the key, 16 s after the click |
| the native dialog opening over the hung page, then "Wait" (`dialog-wait`) | 3/3 | 16.0 s after the dialog appeared, 32 s after the hang. Inferred: the window losing focus to the dialog counts as input sent to the renderer. |
| the engine's reload, a same-site navigation, or Stop + Reload | 15/15 | 30.0 s after the request. The navigation never completed. |

Pinging does not trigger WebView2's hang monitor. When a user clicks a hung page, WebView2 reports it about 16 s later, about when the heartbeat does. Both must lead to the same unresponsive state and one dialog.

**Memory while posting into a hung page.** 1,000 × 1 KiB per second for 30 s, private kB:

| | Host process, before → 8 s after | Renderer, before → peak → 8 s after | Browser process |
|---|---|---|---|
| healthy page | 4,348 → 5,872 | 11,684 → 67,144 → 30,156 (collected) | 30,700 → 30,776 |
| hung page | 4,564 → 5,828 | 11,684 → 42,168 → 42,168 (kept) | 30,724 → 30,804 |

The queue grows inside the hung renderer: about 30 MB for 19,220 messages, about 1.6 kB per 1 KiB message, and none of it is freed while the hang lasts. The host and browser processes did not grow. The design posts one ~20-byte ping every 5 s, which is 720 per hour of hang. Each ping that is never answered stays queued: after 72 s, 15 were outstanding (`outstanding=` in `HB-PING`).

**UI thread.** Posting into a hung page cost the same as posting into a healthy one. A ping post took a mean of 7–11 µs (max 62 µs) on a hung page, against 8 µs (max 31 µs) on a healthy one. The pump probe's p99 stayed between 0.21 and 0.37 ms in every phase, hung or healthy. The worst single delay in a hung phase was 8.85 ms (one `handler` run), against 9.54 ms in a healthy `stress-healthy` phase. Stress posts averaged 11.9 µs on a hung page and 11.4 µs on a healthy one.

### 3. WebKitGTK: `is-web-process-responsive` and `web-process-terminated`

| Case | Runs | `is-web-process-responsive` before FINISH |
|---|---|---|
| idle hang with pings, 72 s (`wk-pings`) | 3 | stayed true |
| idle hang, no traffic, 72 s (`wk-quiet`) | 3 | stayed true |
| idle hang plus xdotool click (`wk-click`) | 3 | stayed true. That the click reached the page was not verified. |
| 20 s task plus click (`wk-busy`) | 3 | stayed true |
| idle, handler and microtask hangs, hangs during load (detect group) | 35 | stayed true |
| `webkit_web_view_reload` into a hung page (`reload`, `reload-load`) | 10 | false 3.0 s after the request, never true again while hung |
| `load_uri` into a hung page (`renav`, `blank`) | 10 | false 3.0 s after the first request |

The property becomes true again only during teardown, after `FINISH`. For an idle hang it adds nothing. It only confirms a hang after the host has asked for a navigation.

`web-process-terminated` fired every time:

- After `webkit_web_view_terminate_web_process`: reason `terminated-by-api`, 0–3 ms after the call, 15/15 (`kill`, `healthy-kill`, `kill-load`).
- After SIGKILL from outside: reason `crashed`, 3 ms after the kill, 5/5 (`kill-sigkill`).

**Teardown.** When the app exits while its web process is hung, the web process stays alive for about 10 s and then logs `WebProcess didn't exit as expected after the UI process connection was closed`. That happened in 68 of the 70 runs that ended with a hung page, for example `idle` 5/5, `reload` 5/5 and `stress-hung` 3/3. The time in the `.err` file is 10 s after the run's exit time in `runs.txt`. The two runs without the line, `kill-load-5` and `gate-minimize-hang-3`, were each the last run of a container, and the container was removed within those 10 s.

**Memory while posting into a hung page.** Private kB, as statm resident minus shared:

| | App process, before → 8 s after | Web process, before → peak → 8 s after |
|---|---|---|
| healthy | 44,060 → 62,800 | 19,952 → 23,912 → 21,532 |
| hung | 44,320 → 70,824 | 19,964 → 55,432 → 55,432 |

Posting 24,990 messages into a hung page kept about 35 MB in the web process, about 1.4 kB per message, plus about 8 MB more in the app than the healthy run. Mean `evaluate_javascript` post time was 8.3 µs. One hung run had one 34 ms post. The pump p99 was 0.08–0.10 ms in every phase. Single maximums reached 32 ms in a hung phase and 50.6 ms in a healthy one (`stress-healthy`; baseline 20.5 ms): this is container jitter, not the hang.

### 4. False alarms and throttling

**Healthy load.** No alarm in any run without an endless main-thread hang, 0/123 runs on the three hosts:

- baseline, anim, tasks100, tasks1000, worker, busy3, busy10;
- stress-healthy, healthy-reload, healthy-kill;
- the window and WebView states below, with the gate off;
- `mem-low` and `ctl-hidden`.

| Load | Probe round trip, median / max | Page timer interval, median / max | rAF per s |
|---|---|---|---|
| baseline, Windows / Linux | 0.2 / 9.1 ms; 0.8 / 14.4 ms | 1000 / 1015 ms; 1004 / 1005 ms | 60; 62 |
| tasks100, Windows / Linux | 46 / 100 ms; 46 / 100 ms | 1046 / 1053 ms | 22; 12 |
| tasks1000, Windows / Linux | 165 / 1,042 ms; 145 / 264 ms | 1005 / 2010 ms | 2; 1 |
| busy10, Windows / Linux | max 9.6 s; max 10.0 s | max 10.6 s; 11.0 s | — |

**Window and WebView states** (gate off, so pings kept going). Every probe sent in these phases was answered, 180/180 per 60 s phase. Probe round trips stayed under 27 ms.

| State | Host | Page visibility | Page timers | rAF |
|---|---|---|---|---|
| minimised 60 s | sandbox | hidden | median 999, max 1,806 ms | 0 |
| covered by a top-level window 60 s | sandbox | visible | unchanged | 32 (the sandbox renders at 32/s) |
| moved off the monitor 60 s | sandbox | visible | unchanged | 32 |
| controller `IsVisible=false` 60 s | host, sandbox | hidden | median 999, max 1,647 / 2,015 ms | 0 |
| `MemoryUsageTargetLevel` low 30 s | host, sandbox | visible | unchanged | 60 / 32 |
| `TrySuspend` 30 s | host, sandbox | — | none | — |
| Linux minimised 60 s | container | hidden | every 2,000 ms | 0 |
| Linux unmapped (hidden) 60 s | container | hidden | every 2,000 ms | 0 |
| Linux covered / moved off screen 60 s | container | visible | unchanged | 62 |

**Suspension.** Suspension is the one state that causes a false alarm. While suspended, 0/90 probes were answered, and the ungated heartbeat alarmed 19.0 s after `TrySuspend`, 3/3 on the host and 3/3 in the sandbox. On `Resume` the queued pings were all answered at once, with round trips up to 29 s. The Windows gate already pauses while the controller is hidden, and a suspended WebView is always hidden first. This dependency must be kept.

**The design's gate.** In `gate-minimize-hang` (sandbox 3/3, Linux 3/3), pinging paused 140–240 ms after minimise. The hang that started while minimised was not reported. It was reported 19.97 s (sandbox) and 19.9 s (Linux) after restore.

**CPU stress was not run** (the `cpu` groups). See "Not tested".

### 5. Recovery

Times run from the host's recovery call to the new document's first message (DOC), its DOMContentLoaded (READY) and the first heartbeat answer, in ms, as median (min–max). The first answer is bounded by the 1 s probe cadence, so READY is the time to a working page.

| Case | Windows host | Linux |
|---|---|---|
| hung page, terminate renderer, then the automatic reload | READY 50 (47–57), 5/5 | READY 100 (78–159), 5/5; with SIGKILL 94 (78–134), 5/5 |
| hung page, the native dialog's "Reload page" (terminate, then reload; sandbox) | READY 104 (95–111), 3/3 | — (no dialog on Linux in this spike) |
| hung page, `Reload()` | never ready within 46 s, 0/5. ProcessFailed(unresponsive) came 30 s later. | never ready, 0/5 |
| hung page, `Stop()` + `Reload()` | never ready, 0/5 | — |
| hung page, same-site `Navigate("/index.html")` | never ready, 0/5 | never ready, 0/5 |
| hung page, `about:blank` then `/index.html` | invalid on Windows, see "Failures" | never ready, 0/5 |
| hung page, cross-origin navigation | READY 41 (40–64), 5/5. A new renderer starts, and the browser ends the hung one 0.55–0.56 s later. Navigating back to the app's origin is ready in 60 ms. | — |
| hung page, `edge://kill` | nothing happens within 16 s, 0/2 (`Navigate` returned S_OK) | — |
| healthy page, reload | READY 9 (9–10) | READY 33 (13–70) |
| healthy page, terminate + automatic reload | READY 51 (48–57) | READY 83 (79–94) |
| hang during load, hook reloads | never ready, 0/5 | never ready, 0/5 |
| hang during load, hook terminates + automatic reload | the reload hangs again in the same script, 5/5. The one-reload limit stopped a loop. | the same, 5/5 |

**Backend state survives.** The C# counter was 2 before the hang in every case that read it first, and every recovered document read 3, so the backend kept its state. The `xsite` second document read 4. Each new document re-registers its subscriptions from scratch; nothing replays a backend command.

**The dialog** (sandbox, 3 runs each):

- "Reload page" terminated the renderer, and `ProcessFailed(RenderProcessExited, terminated)` followed in 6–8 ms. The page was ready 104 ms after the answer and answered pings 5.0 s after the alarm.
- `dialog-recover` closed the dialog by itself as soon as the page answered again: `DIALOG-AUTO-CLOSE` was logged in the same millisecond as `HB-RESPONSIVE`, and the dialog was gone 3–14 ms later.
- "Wait" leaves the page unresponsive with no second dialog. WebView2 raised its own `ProcessFailed(RenderProcessUnresponsive)` 13 s after "Wait". Nothing in the design yet says what happens after "Wait".

### 6. Other intervals and miss counts (simulated)

Each detector is replayed on the 1 s probe pings of the Windows host and Linux runs. A probe answered between two detector ticks counts as alive, as in the real detector.

- Detection is measured over 30 idle, handler and microtask runs.
- False alarms are counted over 82 runs without an endless hang.

| Interval × misses | Detection, s | False-alarm runs | busy20 runs alarmed |
|---|---|---|---|
| 1 s × 3 | 3.5 (3.2–3.9) | 14/82 | 10/10 |
| 2 s × 3 | 6.7 (6.2–7.8) | 10/82 | 10/10 |
| 3 s × 3 | 10.4 (9.2–11.9) | 5/82 | 10/10 |
| 2 s × 5 | 10.8 (10.2–11.9) | 0/82 | 10/10 |
| 5 s × 2 | 12.3 (10.2–14.9) | 0/82 | 10/10 |
| **5 s × 3 (proposed)** | **17.3 (15.2–19.9)** | **0/82** | 10/10 |
| 5 s × 4 | 22.0 (20.2–26.4) | 0/82 | 0/10 |
| 10 s × 2 | 22.0 (20.2–26.4) | 0/82 | 0/10 |

The false alarms at shorter thresholds come from `busy10` and `tasks1000`. A threshold tolerates a single task shorter than about (misses − 1) × interval: the 10 s task stays just under the 2 s × 5 threshold, so that setting has no margin.

## Recommendation

- **Interval and misses: keep 5 s × 3.** It detects in 15–20 s (median 17 s), allows a single task up to 10 s with margin, and gave 0/82 false alarms. Shorter settings false-alarm or have no margin. The user-facing text should say "about 20 seconds", not 15. A task over about 15 s does alarm: it really did block the page for that long, and the dialog closes by itself when the page answers.
- **Arming:** arm when the document is created (the injected script's first message, or ContentLoading / WebKit `committed`), never at NavigationCompleted. Arming at NavigationCompleted misses every hang during load (0/10). Disarm for documents without the injected script, such as error pages or foreign origins. Otherwise a healthy page is reported at 20 s (10/10).
- **Gate:** pause while the window is minimised or hidden and while the controller is hidden. Suspension depends on that rule. Within 60 s, minimised, covered and offscreen pages kept answering ungated, so the gate is not needed against throttling in that window. Longer hidden periods were not measured. A hang while minimised is then reported 15–20 s after restore, which is acceptable.
- **One unresponsive state from two sources on Windows:** the heartbeat alarm, and `ProcessFailed(RenderProcessUnresponsive)`. The latter arrives 16 s after a click, a key or a focus change into the hung page, and 30 s after a navigation. Both must open the same single dialog. A later alarm from either source must not open a second one.
- **"Reload page" terminates the renderer, then reloads once.** Engine reload, Stop + Reload and same-site navigation never recover a hung renderer (0/40 across both hosts). On Windows there is no WebView2 API for this, so the host terminates its own renderer PID from `GetProcessInfos`. Ready in 50 ms (host) and 104 ms (sandbox). On Linux, `webkit_web_view_terminate_web_process` is ready in 100 ms. The one-automatic-reload limit is required: after a hang during load, the reload hangs again.
- **Bound the queue:** while the page is unresponsive, keep at most one ping outstanding. Each unanswered message stays in the hung renderer (about 1.4–1.6 kB per 1 KiB message measured). The outstanding ping is still answered as soon as the page recovers, so recovery detection is unchanged.
- **Linux: do not rely on `is-web-process-responsive`.** It never changed for any hang without a navigation. It flips to false only 3 s after the host asks for a reload or navigation. Use the heartbeat for hangs and `web-process-terminated` (reason `terminated-by-api` or `crashed`) for exits. Before quitting with a hung page, terminate the web process; otherwise it lingers for about 10 s.
- **Open for the design:** what "Wait" does next. In this spike it suppresses further dialogs for the rest of the episode. A natural rule is to ask again after another three misses. The spike did not test it.

## Failures and incomplete cases

- **Windows `blank` is invalid.** `NavigateTo` prefixed `about:blank` with the app origin, and `Navigate` returned `E_INVALIDARG` (0x80070057), so only the second navigation (to `/index.html`) ran. That makes the case equivalent to `renav`. The Linux `blank` is valid.
- **Linux `gate-minimize-hang`:**
  - The three logs are complete, each with FINISH and an alarm 19.9 s after restore.
  - Run 3's `.err` lacks the lingering-web-process line of runs 1 and 2. The group ended with that run, and the container was removed within the 10 s that line takes.
  - The conclusion stands.
- **Windows Sandbox:**
  - All five groups ran (base, states, gated, input, dialog), 45 of 45 runs with logs and FINISH.
  - `wsb exec` relays no output, so the `run-*.log` relay files in `artifacts/page-heartbeat/sandbox/out/` are empty by design.
  - The logs were copied from `artifacts/page-heartbeat/sandbox/out/<group>/` to `results/sandbox/<group>/` for this file.
  - The profile folder of the last gated run (`artifacts/webview2-profiles/page-heartbeat-sandbox/gate-minimize-hang-3`) was not removed by the guest script.
- **`wk-click`:** the xdotool command's exit code was not logged, and the page does not log clicks. That the click reached the hung page is unverified, so "WebKit does not flag an idle hang after input" holds only with that caveat.
- **`edge-kill`:** 2 runs only, and the navigation had no effect. It is not a recovery path.
- **Linux `microtask`:** the runs end 30 s after the hang starts. Whether WebKit eventually ends an endless microtask chain was not observed.

## Not tested

- **CPU stress.** The cases exist (sandbox `cpu-idle`, `cpu-tasks100`, `cpu-hang`; Linux `cpu-idle`, `cpu-tasks100`) but were never run. No host case exists.
- **Hidden for longer than 60 s:** sandbox `minimize-long` (7 min), Linux `st-minimize-long` and host `ctl-hidden-long`. These would show Chromium's intensive throttling of pages hidden for more than 5 minutes. They matter only if the gate is dropped.
- **Several WebViews sharing one browser process.** Mapping a renderer PID to its WebView before terminating it was not checked. The spike had one WebView, and the host terminated every renderer that `GetProcessInfos` listed.
- **A native dialog on Linux.** It is not implemented in this spike (`DIALOG-SKIPPED`).
- **Hangs inside iframes, or in a second window.**
- **Hangs in a page served from a foreign origin.**
- **Machine sleep or hibernate, lock screen, session switch, Remote Desktop disconnect, and DPI changes** during a heartbeat.
- **Real hardware GPU on Linux.** The container rendered in software.

## Checks that need the user at the PC

Proposed rows for the plan's table. They were not run.

| Check | Why it needs the user |
|---|---|
| The "Wait / Reload page" dialog on the real desktop: keyboard use, focus return to the window afterwards, high DPI, screen reader | real input and assistive technology on the user's desktop |
| Heartbeat across machine sleep and resume, and across lock screen or user switch: no false alarm on wake, detection still works | needs the machine to sleep or the session to change |
| Click into a hung page on the real desktop: one dialog only, with heartbeat and `ProcessFailed(RenderProcessUnresponsive)` merged | real mouse input on the user's desktop |

## Files

| Path | Contents |
|---|---|
| `Beat.Win/`, `Beat.Linux/`, `Shared/`, `www/` | the two hosts, the shared session and heartbeat, the pages |
| `scripts/run-win.sh`, `scripts/run-win-all.sh` | Windows host cases |
| `sandbox/` | Windows Sandbox start, connection, guest runner and cases |
| `docker/` | Linux build and container cases |
| `tools/summarize.mjs` | detailed per-case summary with event lists |
| `tools/report.mjs` | the compact tables in this file |
| `tools/vt.mjs` | prints WebView2.h vtable slots (used to write the interop) |
| `results/win/`, `results/linux/` | raw logs, `runs.txt` index. Linux `.err` files hold stderr. |
| `results/sandbox/<group>/` | raw sandbox logs, `runs.txt` per group |
| `results/summary/report.md`, `results/summary/details-*.md` | generated summaries |
