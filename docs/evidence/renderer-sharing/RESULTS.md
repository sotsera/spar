# Renderer sharing between test windows: results

> A spike. Its code was deleted with `spikes/` at the start of M2; it is in git history at [`spikes/renderer-sharing/`](https://github.com/sotsera/spar/tree/24f5db0942833047c81fa692908410596f3b8960/spikes/renderer-sharing) (commit `24f5db0`), and this record and its `results/` moved here. Paths under `spikes/` below name that history.

Recorded 2026-11-19. Host: Windows 11 Pro 10.0.26320, 32 logical CPUs, 96 GB of memory, SDK 11.0.100, WebView2 Runtime 157.0.4341.60 (Evergreen). The probe ran on CoreCLR Debug, in the window-test host of the repository as of commit `febdd1c` (before this investigation's host change unless a section says otherwise). Every window was a test window (offscreen, never activated, `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`) or a window of `spikes/webview-profiles`' host (offscreen the same way); no input reached the desktop.

## The question

The Sandbox runner's addendum (decision "The Windows Sandbox runner (implementation record; choices for review)") recorded that in the full window suite, `SessionEndHostTests.A_renderer_lost_during_the_session_end_is_not_reloaded` (which ends its own window's renderer, found by its main frame) made another test window's page "stop answering": that window's session end, normally about 0.1 s, held 3.0–3.1 s in 5 of 6 runs. Do test windows (InPrivate controllers under distinct `ProfileName`s on the test process's one environment, all on `https://app.localhost`) share a renderer? If not, what else lengthens the other window's session end?

## Answer

**They do not share a renderer, and the other page did not stop answering.** The 3 s was the other test's own `WM_ENDSESSION` being held under the renderer test's: every test window of a test process is on one UI thread, a window's session end holds `WM_ENDSESSION` in a message loop of its own (`MessagePump.Until`), and a second window's `WM_ENDSESSION` that arrives during that loop is dispatched inside it. The outer handler cannot return until the inner one has, whatever its own state. When the quick test's message was the outer one, it returned when the renderer test's did: 3.0–3.1 s, because that test's page guard waited out its 2 s and then its close work's 1 s for a page whose renderer was dead.

So there are two findings, neither of them renderer sharing:

1. **Nested session-end holds on one UI thread** (a test-suite matter now; an M3 matter for apps with several windows on one thread). Fixed in the tests: every test that sends `WM_ENDSESSION` to a test window is in one class, `SessionEndHostTests`, so they never overlap; the class needs no non-parallel collection.
2. **A renderer that dies during a close was waited for.** During an approved close or a session end, the host's `ProcessFailed` handler logged the lost renderer and did nothing else, so the page's document stayed "open": the page guard's request waited its full time and the close work its 1 s. Fixed in the host: the document now ends when its renderer (or the browser) exits during a close, so the request goes on without the page's answer, as the two-stage close already does for a document that goes away mid-request.

## 1. Renderers per window (`Probe/Probes.cs`, `Listing`; `results/listing.txt`)

N windows opened at once on the test process's shared environment. For each, its renderer processes by its main frame (`ICoreWebView2Environment13.GetProcessExtendedInfos`, the frames of each process, matched by `ICoreWebView2_20.get_FrameId`, as `TestWindow` does); and every `msedgewebview2` process whose command line names this test process's profile root, by `--type` and `--renderer-client-id`.

| Windows open | Renderer per window (by main frame) | Shared pids | Renderers of the root (command line) |
|---|---|---|---|
| 2 | 1 each, 2 distinct | none | 3: the two windows' and one no window lists (the anchor's) |
| 4 | 1 each, 4 distinct | none | 5 |
| 8 | 1 each, 8 distinct | none | 9 |

The one renderer no window lists was the same process (client id 5) in all three listings: the default-profile anchor's page. The browser, GPU, two utility processes and the crashpad handler are one each, shared by all windows.

## 2. Ending one window's renderer (`KillOne`; `results/kill-one.txt`)

N windows; window 0's renderer (by its main frame) ended with `Process.Kill`, 500 ms of waiting, then each window's page evaluated `1 + 1` (3 s limit), its app log searched for "renderer exited", and its renderers listed again.

| Windows | Window 0 | Every other window |
|---|---|---|
| 2 | "renderer exited" in its log, reloaded on a new renderer (the host's one automatic reload), answered in 1.7 ms | answered in 0.5 ms; no "renderer exited"; same renderer pid as before |
| 4 | the same, 1.3 ms | 0.6–0.8 ms; same pids |
| 8 | the same, 0.8 ms | 0.5–0.8 ms; same pids |

The kill targeted the right process: only window 0 saw its renderer exit.

## 3. Why Spar's test windows cannot share, and when windows do (`scripts/process-limit.sh`; `results/process-limit.txt`)

Microsoft's process model page says a renderer "can serve frames in multiple `CoreWebView2` instances that use the same user data folder, depending on website isolation" ([Process model for WebView2 apps](https://learn.microsoft.com/microsoft-edge/webview2/concepts/process-model)). To see when, `spikes/webview-profiles`' published host (scenario `multi`: every window navigates to `https://app.localhost`) opened four windows in one user-data folder, with and without `--renderer-process-limit=1` in the environment's browser arguments, and the folder's renderer processes were counted while the windows were open (most seen in 12 polls 0.5 s apart). Three rounds; three lines of an earlier, aborted round whose process filter was broken (it matched nothing) were removed from the file.

| Four windows on | No arguments | `--renderer-process-limit=1` |
|---|---|---|
| InPrivate, four distinct `ProfileName`s (Spar's test windows) | 4 renderers (3 of 3) | **4 renderers (3 of 3)** |
| InPrivate, one `ProfileName` | 4 (3 of 3) | **1 (3 of 3)** |
| The default profile (an app's windows) | 4 (3 of 3) | **1 (3 of 3)** |

Windows share a renderer only within one profile (one browsing context), and then only when the browser's renderer limit is reached; the limit forced it here, and Chromium otherwise derives it from the machine's memory (inferred: this machine, 96 GB, never reached it with 9 renderers). Windows under distinct profile names never shared, even at a limit of 1. Spar's test windows always have distinct names among the windows open at once (`TestWindowProcess.RentProfile`), so **a test window's renderer is its own**, and the leftover-renderer check and a test's renderer kill are safe. No browser argument or profile change is needed for tests, so none was measured against the 700 ms budget; the fix below costs nothing per test.

**For M3 (several windows of one app on its default profile):** two windows of an app can share a renderer once the renderer limit is reached (a small-memory machine, or many windows). Then a hung page's "Reload page" or "Close anyway", which ends renderers, would take the other window's page with it; and the Production path of `BrowserView.TerminateRenderers` today ends every renderer of the environment, on the stated ground that the environment is the one window's. M3's multi-window design has to choose: end only the window's own renderers (by main frame, as test windows do) and accept that a shared one takes its other windows with it, or give each window a profile of its own, which separates storage too (not what an app wants). Not measured: the default limit on a small-memory machine.

## 4. The overlap (`Overlap`; `results/overlap.txt`, `results/overlap-logs.txt`)

Two windows on the one UI thread: "slow" with the page guard `slow` (it holds a session end for the page guard's 2 s), "fast" with none. Each `WM_ENDSESSION` is sent by `app.Window.SendMessageAsync` (posted to the UI thread, `SendMessage` there), timed from the test. "Kill" ends the slow window's renderer 300 ms after the messages. Three rounds of the theory (each a new test process); "fast-first-0" ran in the third only. Times in ms, before the host change.

| Order | Kill | Fast window's `WM_ENDSESSION` | Slow window's |
|---|---|---|---|
| fast alone | — | 17–52 (9 of 9) | — |
| slow first, fast 30 ms later | yes | 17–48 (9 of 9): the fast one runs inside the slow one's loop and returns at once | 3,021–3,062 |
| fast first, slow 30 ms later | no | 16–44 (9 of 9): the fast one was done before the slow one arrived | 2,017–2,060 |
| fast first, slow 30 ms later | yes | **3,117, 3,130, 3,137** (3 of 9, the first row of each process, where the fast hold was still running at 30 ms); 17–42 in the other 6 | 3,040–3,072 |
| fast first, slow right after | no | **2,052–2,083** (3 of 3) | 2,022–2,053 |
| fast first, slow right after | yes | **3,071–3,092** (3 of 3) | 3,040–3,061 |

The fast window's hold, measured by the host inside its own handler ("session end held"), matched its test-side time within 2 ms in every row, so the time was spent inside its handler, under the slow one's. The kill does not cause the nesting; it only makes the slow hold 3 s instead of 2: the slow window's log (`overlap-logs.txt`) shows "A browser process failed during shutdown (kind 1); nothing is recovered" at 350 ms, "the page's close guard did not answer within 2000 ms" at 2,007 ms, then "page close work timed out" 1,005 ms later. In the full suite's run where both tests logged their times (`artifacts/test-results/Spar.WindowTests/session-end.txt`, 12:08:33 UTC), the quick test's `WM_ENDSESSION` started 20 ms before the renderer test listed its renderer, just before sending its own: the "fast first, slow right after" row.

## 5. After the changes

- **The host:** `BrowserView.OnProcessFailed`, during a close or a session end, ends the document (`WebViewBridge.OnRendererExited`, "closed (renderer exited)") when the renderer or the browser exited; recovery stays off. `A_renderer_lost_during_the_session_end_is_neither_reloaded_nor_waited_for` held 326 ms in its first run (was about 3,030 ms). A first version asserted the whole hold under 1.5 s and failed once in a solution-wide `dotnet test` (1,792 ms): with every test project running at once, WebView2 reported the lost renderer 11–872 ms after the kill (the 4 of those runs whose log times were recorded), and the window ended 20–61 ms after the report each time. The test now checks what the host does after the report, by its log's times: the window destroyed within 500 ms of it, "closed (renderer exited)", and neither "did not answer within" nor "page close work timed out". With the host change taken out it failed 3 of 3 (the window destroyed 2.7 s after the report; holds 3,067–3,087 ms).
- **The probe's overlap again** (round 4 in `results/overlap.txt`, same probe on the changed host): the slow window with its renderer ended took 326–395 ms (9 of 9, was 3,021–3,072); without the kill still 2,020–2,057 (its guard's 2 s). The nesting itself is unchanged, as expected: with the slow message right after the fast one, the fast window returned 352–361 ms (kill) and 2,059–2,089 ms (no kill), each about 30 ms after the slow one.
- **The tests:** the three session-end tests of `HostTests` moved to `SessionEndHostTests`, which lost its `DisableParallelization` collection.
- **The full window suite** (CoreCLR Debug, `scripts/suite-runs.sh`, `results/suite-runs.txt`), with the session-end class running beside the others: 30 runs of the test executable and 10 through `dotnet test --project tests/Spar.WindowTests -c Debug --no-build`, 106 tests each, **0 failures in 40 runs** (54–65 s per run). Per run: the quick session end held 54–109 ms, the renderer test's 331–461 ms, the slow one's 3,999–4,058 ms. Then 10 more runs of the executable with the renderer test's final checks (label `coreclr-final`): 0 failures; the renderer test held 332–453 ms, its window destroyed 13–77 ms after WebView2 reported the lost renderer.
- **Under load** (`scripts/session-end-under-load.sh`, `results/session-end-under-load.txt`): the renderer test alone 15 times, each in a new test process, beside two copies of the window suite: 15 of 15 passed, held 349–395 ms. Two window suites did not reproduce the solution-wide run's late report; eight solution-wide `dotnet test` runs (one with its build, seven with `--no-build`) held it 388–1,792 ms.

## 6. The long install path test (the second question of the same investigation)

`InstallPathTests.The_window_works_from_an_install_folder_with_a_long_path` had failed once, in the first of 10 full CoreCLR runs of step 8c, with no message kept. Ways it could lose its message: the 2-minute limit on the child threw a bare `OperationCanceledException` (no output), and the `finally` that deleted the copy threw over the test's own failure when a file was still held. The test now keeps every child's output (`artifacts/test-results/Spar.WindowTests/install-path/<time>-<pid>.log`, a line per run in `install-path.txt`), names that file and puts the output in its failure message, reports a child that outlives the limit with what it wrote, and retries the delete for up to 15 s, noting a cleanup failure without hiding the test's own. In the 50 full runs of section 5 it passed every time: the child ran 2,522–3,311 ms, once 7,784 and once 8,351 ms (a 5 s stall in its first window, the known "5 s wait" of decision "Step 8's shape"); the copy was deleted at the first attempt every time. The failure did not reproduce, so its cause is unknown.

## Files

| Path | What |
|---|---|
| `Probe/` | The probe: a test project on `tests/apps/WindowHost`, named `Spar.WindowTests` only to reach the internals Spar and Spar.Testing give that project. `dotnet build spikes/renderer-sharing/Probe -c Debug`, then `Spar.WindowTests.exe -method "RendererSharing.Probes.<Listing|KillOne|Overlap>"` from its `bin/Development/Debug/net11.0/win-x64/`. It writes to `results/`. |
| `scripts/process-limit.sh` | Section 3, on `spikes/webview-profiles/Profiles.Win/bin/publish` (Git Bash, from the repository root). |
| `scripts/suite-runs.sh` | Runs the built window suite N times and appends one line per run to `results/suite-runs.txt` (each run's console under `artifacts/renderer-sharing-runs/`). |
| `results/` | The raw evidence named above. |

## Not verified

- Windows 10, other Runtimes, and a small-memory machine, where the default renderer limit is low (the default profile's windows of section 3 then share without any argument; inferred, not seen).
- Whether Windows itself nests the session-end messages of several top-level windows of one thread (it may send them one after another and wait for each); only the test host's own `SendMessage` was observed.
- NativeAOT for the probe (the host's change runs in both lanes through the window suite).
