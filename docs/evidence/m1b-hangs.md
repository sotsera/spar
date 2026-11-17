# M1b: hangs (the hung-page heartbeat, the UI-thread stall probe, the shutdown prompt)

Recorded 2026-11-17. What it shows: the Windows host's hang handling of plan M1b, build step 3, on the Notebook sample: the heartbeat over the bridge with its native "Wait / Reload page" and "Wait / Close anyway" dialogs, the Wait schedule, the app's observing hook, the UI-thread stall probe in Development and its opt-in in Production, and the "Keep waiting / Exit now" prompt with exit code 10. Decision "Hangs: heartbeat, stall probe and shutdown prompt (implementation record; choices for review)".

## Environment

Windows 11 Pro 10.0.26320 (system theme dark), SDK 11.0.100, WebView2 Runtime 157.0.4341.46, `Microsoft.Web.WebView2` 1.0.4314.35, 32 logical CPUs. Windows were shown on the user's desktop as an app's window is, never activated by the probe; no mouse or keyboard input was sent: dialogs were answered with `TDM_CLICK_BUTTON` posted to the dialog, the window was closed with `WM_CLOSE`. Every app run had `LOCALAPPDATA` in its own folder under `artifacts/webview2-profiles/m1b-hangs/`, so the profiles, data and `host.log` stayed there; nothing of these runs appeared in the real per-user folder (checked after the runs).

**Real input touched one superseded run.** During the first full run (15:03–15:10), the user maximized one of the Notebook windows (one of the Production cases, whose renderer the probe had suspended, so it was "not responding" by design). That run also had two probe mistakes (below), so all of it was set aside (`artifacts/webview2-profiles/m1b-hangs/logs-run1/`, disposable) and every scenario was run again on the final code (15:12–15:26). Every number below is from that second run; nobody reported touching its windows, but it ran on the same desktop.

## What was added

| Path | Contents |
|---|---|
| `src/Spar/Bridge/BridgeMessage.cs`, `BridgeCodec.cs`, `BridgeSession.cs` | `ping` (host to page) and `pong` (page to host) with a `seq` field; the session pings only a page that listed `ping` in `hello`, reports `Opened(answersPing)` and each `Pong(seq)` |
| `src/Spar.Client/src/protocol.ts`, `connection.ts` | The same in the SDK: `ping` listed in `hello`, and each `ping` answered with its `pong` at once, from the message handler |
| `tests/conformance/bridge-messages.json` | Two canonical vectors (`ping`, `pong`) and four malformed ones (no `seq`, `seq` 0, unsafe, a string), read by both codecs' tests |
| `src/Spar/Hosting/PageHeartbeat.cs` | The heartbeat's rules for every host, without timers: one ping outstanding, three misses, the visibility gate, the Wait schedule, one state with the engine's report |
| `src/Spar/Hosting/PageHang.cs`, `PageHangHook.cs`, `SparApplication.cs` | `spar.OnPageUnresponsive((hang, services) => …)`, `PageHang(Window, State, Duration)`, `PageHangState`; the hook runs off the UI thread, one change at a time |
| `src/Spar/Hosting/UiStallProbe.cs`, `DiagnosticsOptions.cs` | The stall probe, `spar.Diagnostics(d => d.KeepStallProbe())`, and the one place that creates a probe (the Development switch, or the kept factory) |
| `src/Spar/Hosting/ShutdownHang.cs` | The shutdown policy's numbers: first prompt at 10 s, again every 30 s, exit code 10 |
| `src/Spar.Windows/PageWatch.cs` | The heartbeat on the UI thread: a thread-pool timer posts the tick, the dialogs, the close check, the hook calls |
| `src/Spar.Windows/PromptDialog.cs` | A two-button task dialog on an STA thread of its own that the host can take down (`TDM_CLICK_BUTTON`) |
| `src/Spar.Windows/ShutdownWatch.cs`, `ProcessControl.cs` | The shutdown prompt's thread; ending a renderer, and this process for "Exit now" |
| `src/Spar.Windows/BrowserView.cs`, `UiThread.cs`, `WebViewBridge.cs`, `WindowsApp.cs` | "Reload page" (renderers from `GetProcessInfos`, then one reload), `RenderProcessUnresponsive` raised to the watch, every UI-thread callback named for the probe, the close request asking the page first, the teardown skipping a hung page's close work and ending its renderer |
| `src/Spar.Windows/DevelopmentTriggers.cs`, `Interop/` | Development-only triggers: `SPAR_DEV_PAGE_HANG` (an endless or finite loop in the page through `ExecuteScript`), `SPAR_DEV_UI_STALL_MS`, `SPAR_DEV_SLOW_DISPOSE_MS`, `SPAR_DEV_HEARTBEAT_MS`; `GetProcessInfos`, `ICoreWebView2ProcessInfo(Collection)`, `ExecuteScript` and its handler declared |
| `tests/apps/Spar.Harness/Program.cs`, `tests/Spar.BuildTests/` | The harness keeps the probe; the boundary test finds the probe and the triggers absent from the Production Notebook and the probe present in the harness |
| [`m1b-hangs/HangProbe.cs`](m1b-hangs/HangProbe.cs), [`run.sh`](m1b-hangs/run.sh), [`scenarios.sh`](m1b-hangs/scenarios.sh), [`summarize.mjs`](m1b-hangs/summarize.mjs) | The probe (a file-based C# program, run from a copy outside the repository), one scenario, every scenario, and the summary of the logs |
| [`m1b-hangs/logs/`](m1b-hangs/logs/), [`summary.txt`](m1b-hangs/summary.txt), [`scenarios-output.txt`](m1b-hangs/scenarios-output.txt) | The final run's raw logs (the probe's clock on the left, the app's standard error on the right), their summary and the probe's steps |

**Builds:** `dev` is the Notebook in Debug with `SparDevServer=false` (CoreCLR, Development variant: probe and triggers present); `aot` is `dotnet publish samples/Notebook -c Release` (NativeAOT, Production, 4,849,664 B); `kept` is a copy of the Notebook under `artifacts/m1b-hangs/notebook-kept/` whose `Program.cs` adds `spar.Diagnostics(d => d.KeepStallProbe())` and an `OnPageUnresponsive` hook that writes each change to stderr (NativeAOT, Production, 4,886,528 B). A Production page cannot be made to hang from inside, so the `aot` and `kept` cases suspend the renderer from outside (`NtSuspendProcess`), which is what a hung page looks like to the host.

## Results (observed, second run, 27 of 27 scenarios passed)

### The heartbeat

| Case | Runs | Result |
|---|---|---|
| Idle page, 60 s (`aot`) | 1 | no dialog; 12 ticks, 13 pings (one more at the close); longest tick on the UI thread **0.149 ms** |
| Finite work of 3 s / 10 s (`dev`, `SPAR_DEV_PAGE_HANG=3000:3000` / `3000:10000`) | 3 + 3 | **no dialog** in 30 s, every run |
| Endless loop, started at 3.0, 4.2, 5.4, 6.6 and 7.8 s after `ui-ready` (`dev`) | 5 | alarm **15.5, 16.7, 19.2, 18.1, 16.9 s** after the loop began; the dialog was shown 1 ms after the alarm's log line, and the probe saw it 46–48 ms later |
| Endless loop, other runs (`dev` close and automated cases) | 2 | 16.7, 16.7 s |
| Renderer suspended (`aot`, `kept`) | 6 | alarm **16.8–18.9 s** after the suspension (17.6 s four times), dialog visible 46–48 ms after the alarm (514 ms once, when the probe was busy) |
| Work of 25 s (`dev`) | 1 | alarm at 16.6 s; at 25 s the page answered and the **dialog closed by itself**; "answered again after 23.4 s" |
| Suspended renderer resumed with the dialog open (`aot`) | 1 | the dialog was gone **20 ms** after the resume; "answered again after 17.1 s" |
| "Reload page" (`dev` 5 runs + schedule run, `aot`, `kept`) | 8 | 1 renderer ended 0.3–3.7 ms after the choice; the new document's `ui-ready` **84.7–140.4 ms** after the choice (median 90.9); the reload is the user's, so the automatic one stayed available; no command replayed |
| Cost on the UI thread | every run | longest tick (visibility check, state, encoding and posting a ping) 0.15–0.36 ms on NativeAOT, 0.75–2.2 ms on CoreCLR Debug (the first tick, JIT) |

- **"About 20 s":** detection took 15.5–19.2 s from the hang (the spike: 15.2–20.0 s). `Duration` in the log and the hook was 15.0 s at the alarm in every run: it counts from the first ping the page missed, up to 5 s after the hang began.
- **WebView2's own report never came:** in 0 of 27 runs, also with the dialog open 30 s over a suspended renderer (`aot engine-merge`, 47 s without an answer in all). The spike saw `RenderProcessUnresponsive` 16 s after its dialog opened, in Windows Sandbox, where every process gets the foreground. Here the windows were never activated and the dialog took no focus, so no input reached the page. The merge of the two reports into one state is covered by the unit tests only.

### The Wait schedule

On a 500 ms heartbeat (`SPAR_DEV_HEARTBEAT_MS=500`, Development only), so 4, 8, 16 and 32 ticks are 2, 4, 8 and 16 s: the alarm 1.7 s after the hang; after each Wait the dialog came back after **1.9, 3.9, 7.9, 15.9, 15.9 s** (decided: 20, 40, 80, 160, 160 s at 5 s a tick). Then "Reload page" recovered the page. On the normal 5 s heartbeat (`dev close-silent`) the first Wait came back after 15.6 s: the schedule counts ticks, so each return comes up to one interval early (15–20 s, 35–40 s and so on).

### Closing a page that does not answer

| Case | Result |
|---|---|
| Close 1 s after the page hung, before the alarm (`dev close-silent`) | the close's ping went unanswered; "did not answer the close request within 5000 ms" **5,016 ms** after the request; "Wait / Close anyway" at once; Wait; asked again 15.6 s later; Close anyway: the backend's guard answered Close, the page's close work was skipped, the renderer was ended, exit 0, 42 ms after the choice |
| Close while the "Wait / Reload page" dialog was open (`dev close-hung`) | the open dialog was taken down and "Wait / Close anyway" shown 1 ms after the request; Close anyway; exit 0 |
| Close of a healthy page (every other run) | the ping answered in about 5 ms; the close went on as before |
| Automated mode (`SPAR_AUTOMATED=1`, `dev`) | the hang was logged and no dialog shown; a close of the hung page "closes a page that does not answer without asking": renderer ended, exit 0 |

### The hook (the `kept` copy, NativeAOT Production)

One run, in order, with a suspended renderer each time: `Unresponsive after 15.0 s`, then "Reload page" → `Reloaded after 16.1 s`; again `Unresponsive after 15.0 s`, the renderer resumed → `Recovered after 15.0 s`; again `Unresponsive after 15.0 s`, then a close and "Close anyway" → `ClosedAnyway after 15.5 s`, exit 0. Each line came from the app's hook on its own thread, after Spar's own log line. The unit tests add: changes in order, with the app's services; a hook that throws is logged and still gets the next change; no hook, nothing queued.

### The shutdown prompt

| Case | Result |
|---|---|
| A service whose disposal takes 120 s (`dev`, `SPAR_DEV_SLOW_DISPOSE_MS=120000`) | "closing for 10.0 s" and the prompt **10.0 s** after the teardown began; Keep waiting; the prompt again at **40.1 s**; Exit now: its log line, then **exit code 10** about 10 ms later; no browser process left |
| Disposal of 15 s | the prompt at 10.0 s; at 15.5 s the shutdown finished, the watch was completed, and the process exited 0 with the prompt |
| Automated mode, 13 s | "closing for 10.0 s without finishing … The automated mode shows no shutdown prompt; still waiting", exit 0 at 13.5 s |

The prompt had no owner (the window was already destroyed: the services are disposed after it), as designed.

### The stall probe

| Build | Case | Result |
|---|---|---|
| `dev` | `SPAR_DEV_UI_STALL_MS=300` at `ui-ready` | "The UI thread stalled 312.5 ms; the slowest Spar callback meanwhile was development stall trigger (312.5 ms)" |
| `dev` | the UI thread suspended 200 ms from outside | "The UI thread stalled 203.5 ms outside Spar's callbacks" |
| `aot` (no `KeepStallProbe`) | suspended 300 ms | nothing logged; no "stall probe on" line |
| `kept` (`KeepStallProbe`) | suspended 300 ms | "UI-thread stall probe on." at start; "The UI thread stalled 281.1 ms outside Spar's callbacks" |

- No stall over 50 ms was logged in any `dev` run other than the two forced ones: start-up, the heartbeat, the dialogs, Reload and the close included.
- **What the Production images carry** (searched as the boundary test does): the `aot` Notebook has neither `UiStallProbe` nor "The UI thread stalled", nor `DevelopmentTriggers` nor any `SPAR_DEV_*` name of this step; the `kept` copy has both probe markers. The console harness, which keeps the probe, is 3,146,752 B with it and 3,114,496 B without: **the kept probe costs 32,256 B**. The Notebook grew from 4,772,864 B (step 2) to 4,849,664 B (+76,800 B) for the heartbeat, the dialogs and the shutdown watch.

### Probe mistakes in the first run (fixed before the second)

- A dialog step found the hung-page dialog that was being replaced by the close one and pressed it as it closed, so the close dialog waited until the probe gave up. The probe now finds the dialog again at the press, and the scenario waits 0.5 s for the replacement.
- A "dialog gone" step failed when the process exited with the prompt still up, which is the expected end of a shutdown that finishes.
- The first run also found a real bug: `Recovered` reported a duration of 0.0 s, because the heartbeat forgot the hang's start before the change was reported. `PageWatch` now keeps it.

## Not verified

- Real input: a click into a hung page, the dialogs and the prompt pressed with the mouse or keyboard, Narrator, and whether the unowned shutdown prompt comes to the front on the real desktop (plan, "Checks that need the user at the PC").
- `RenderProcessUnresponsive` merging into an open hang on a real run (it never fired; unit tests only).
- A hang before the page's SDK connects (a loop in a module that runs before `connectSpar`): not pinged by design; not provoked.
- Minimised and hidden windows on a real run (the gate is unit-tested; the probe did not change window states).
- The `MessageBoxW` fallback of `PromptDialog`, a Windows without Common Controls 6, Windows 10, DPI other than 100 %.
- Linux (M1c), and the window tests' use of the stall data (step 8).
