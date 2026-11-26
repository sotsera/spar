# M1b: the Windows Sandbox runner (step 8c)

Recorded 2026-11-19. What it shows: Spar's Windows Sandbox runner (`src/Spar.Testing/Sandbox/`), its suite `tests/Spar.SandboxTests`, and the Sandbox checks of `Spar.WindowTests` and `Notebook.EndToEndTests`, run end to end. Decisions "Step 8c's shape: the Windows Sandbox runner (user decisions)" and "The Windows Sandbox runner (implementation record; choices for review)". Spike: [`docs/evidence/sandbox-signout`](../../docs/evidence/sandbox-signout/RESULTS.md).

## Environment

Host: Windows 11 Pro 10.0.26320, 32 logical CPUs, SDK 11.0.100, `wsb` 0.8.114.0, WebView2 Runtime 157. Guest: Windows Sandbox (Windows 10.0.26100), no .NET; network, clipboard, printers, camera and microphone off; the run's staging folder mapped read-only at `C:\spar\in`, its output folder writable at `C:\spar\out`.

- **No client window on the host in the SYSTEM lane:** without `wsb connect` the sandbox shows nothing on the host; every SYSTEM-only run below minimized the client window 0 times, and the runner never touched any window.
- **The user-session lane ran four times,** with `SPAR_SANDBOX_DESKTOP=allow` set on each command only, after the user said they were away from the PC and allowed up to four runs. Its own section is below.

## How to run

```
dotnet test --project tests/Spar.SandboxTests
```

The build publishes `Spar.WindowTests` and `Notebook.EndToEndTests` with NativeAOT (`SparTestLane=Aot`) into `tests/Spar.SandboxTests/obj/sandbox-payloads/` (34 s the first time with the AOT lanes' `obj/aot/` warm; an unchanged build skips it). Results: `artifacts/test-results/Spar.SandboxTests/<run>/` with `summary.txt` and, per sandbox `sNN/out/`, `timeline.jsonl`, `config.xml` and each job's folder (`console.log`, xUnit's JSON reporter as it streamed; `results.trx` and `results.xml` when the run ended on its own; `guest.txt`; `exitcode.txt`; `test-results/<project>/` written by the tests; `artifacts/` with the logs of the job's data roots). [`collect.sh`](m1b-sandbox/collect.sh) copies a run's evidence here with the repository's path, profile paths and SIDs redacted ([`redact.pl`](m1b-sandbox/redact.pl)).

## The SYSTEM lane, five runs

`dotnet test --project tests/Spar.SandboxTests --no-build`, five times in a row, no allow ([`runs/`](m1b-sandbox/runs/)). Each run: one sandbox, three jobs (`window-tests/System`: 3 tests; `notebook/System`: 1 test; `window-tests/Deadline`: the 3 SYSTEM tests and a probe that outlives the 45 s deadline), then 9 skips for the user-session lane and the host's own checks.

| Run | `dotnet test`, wall | sandbox alive | result |
|---|---|---|---|
| 1 | 54,261 ms | 51,785 ms | 14 passed, 10 skipped |
| 2 | 53,582 ms | 51,691 ms | 14 passed, 10 skipped |
| 3 | 53,361 ms | 51,561 ms | 14 passed, 10 skipped |
| 4 | 53,670 ms | 51,839 ms | 14 passed, 10 skipped |
| 5 | 53,463 ms | 51,665 ms | 14 passed, 10 skipped |

Plus a first run before the five (`20261119-113008`, not copied): 14 passed, 10 skipped, 52.2 s.

Per step, in milliseconds, over the five runs (min–max; the per-job steps over the 15 jobs):

| Step | Time | What it is |
|---|---|---|
| `wsb list` before | in the run's first 0.3 s | refuses when any sandbox runs |
| `start` | 992–1,124 | `wsb start --id <new> --config <xml>` |
| `ready-system` | 328–356 | the first `wsb exec -r System cmd /c exit 0`, answered at the first attempt every time |
| `marker` | 315–386 | the runner's marker written in the guest as SYSTEM |
| `stage` | 7.1–12.6 | copy of the payload without PDBs (3 files, 24.3 MB for `Spar.WindowTests`, the Notebook's Production executable among the files of its payload) and `run.cmd` |
| `stage-visible` | 287–381 | one `exec` probe for `ready.txt`: seen at the first probe in 15 of 15 jobs |
| `run`, SYSTEM jobs | 743–981 | the whole `wsb exec` of a payload: robocopy to the guest's disk, the tests (0.35 s by the guest's clock), the reports |
| `run`, deadline job | 45,042–45,058 | ended by the 45 s deadline |
| `deadline-kill` | 344–417 | `taskkill /f /t /im` in the guest as SYSTEM |
| `collect-logs` | 290–382 | robocopy of the job's `*.log` files from the guest's disk to the output |
| `stop` | 560–578 | `wsb stop`; `wsb list` afterwards had no sandbox in 5 of 5 |

So a sandbox with SYSTEM jobs costs about 1.9 s to the first job, about 1.3 s of runner overhead per job beyond the payload's own time, and 0.6 s to stop. The deadline check dominates the lane (45 of the 52 s).

### What the guest tests saw

- **Folders:** `SPAR_TEST_ARTIFACTS` = `C:\spar\work\<stage>\artifacts` (the guest's disk; data roots under its `test-host\`), results under `C:\spar\out\<stage>\test-results\<project>\`, which came back on the host in every run.
- **The guest's mark:** `SandboxGuest.IsRunnerGuest` true; as SYSTEM `Environment.UserName` is the machine account (`4EA8E113-8F49-4$`), so `IsUserSession` false, and real input, the screenshot and a second window were refused there, in 5 of 5 runs.
- **Network:** no adapter up, `NetworkInterface.GetIsNetworkAvailable()` false.
- **The Notebook's payload:** its Production executable (5,235,712 B) came with it, beside the tests.

### The deadline

In 5 of 5 runs the deadline job's `wsb exec` was ended at 45.04–45.06 s, the test process ended in the guest in 344–417 ms, and the guest script then wrote exit code 1. The JSON reporter's stream had the 3 SYSTEM tests as passed and the probe as started without an end (`Unfinished`); no TRX or XML was written, as the spike found, so without the stream nothing of that run would have come back.

## The host's view

`GuestTests.Guest` is one theory, not enumerated at discovery, with a row per guest test named `[<job>] <guest test>` and a row per job (`[<job>] the job ran and came back`): 4 job and test rows of `window-tests/System`, 2 of `notebook/System`, 5 of the deadline job, and the user-session lane's 9 tests as skips with the allow's text. `RunnerTests`: results through the mapped folder, the deadline keeping the finished results, no sandbox left and the client window untouched without a connect (all passed), and the sign-out's session end (a skip without the allow).


Two SYSTEM tests were added after the five runs, in answer to the user-session lane's first run: the guest's WebView2 registration and a Production app finding the guest's Runtime. Each later run, with or without the allow, passed them.

## The user-session lane, four runs

`SPAR_SANDBOX_DESKTOP=allow dotnet test --project tests/Spar.SandboxTests --no-build`, four times. These were the four runs the user allowed, and the code was fixed between runs ([`user-runs/`](m1b-sandbox/user-runs/): each run's summary and timelines; run 4 with its jobs' notes, UI Automation dumps, and two of its screenshots). Each run used three sandboxes: the shared one (the SYSTEM jobs, then the two user-session jobs), one for the clean machine, and one for the sign-out. So each run made three `wsb connect`s.

| Run | Wall | Result | What failed, and the fix before the next run |
|---|---|---|---|
| 1 (`115933`) | 327 s | 18 passed, 10 failed | Every app in the user session: "Missing: WebView2 Runtime". The guest's registration named 157.0.4341.46 while its files were 157.0.4341.60. The runner then set `Clients\…\pv` |
| 2 (`120755`) | 334 s | 20 passed, 10 failed | The same. The loader follows `ClientState\…\EBWebView`, not `pv` (already in the pitfalls, from `docs/evidence/startup-prerequisites`). The runner now sets both. Two SYSTEM tests check it without a user session: the registration names a Runtime on disk, and a Production app as SYSTEM logs "WebView2 Runtime 157.0.4341.60" |
| 3 (`121741`) | 171 s | 25 passed, 7 failed | Passed: the offline page, the 25 keys and 2 mouse buttons, the clean first run. Failed: (a) the guard's list was not found, because it sat below the window's fold and a click needs it on screen; it is now focused through UI Automation. (b) The second window was shown over the app's, stayed behind it, and the click hit the app; it now goes beside the app's window. (c) The menu check read the app's own system menu ("System"); it now reads the browser's windows only. (d) The sign-out test stopped at (a) |
| 4 (`122244`) | 146 s | **30 passed, 2 failed** | (a) "Wait / Close anyway": after six arrow keys sent at once, the list did not read "Never answer" within 5 s; the keys now go one at a time, each read back. (b) The sign-out's session end: the app was ended with the test process (below) |

### Run 4, per check (the guest's own times)

| Check | Result | Time |
|---|---|---|
| The page starts offline (`navigator.onLine` false) and Echo still answers: Window surface, in-process | passed | 7.72 s |
| The first click on an inactive window reaches the page: a second window of the test's own takes the foreground with a real click, then a real click on "Echo" gives "Echo: hello" and the app's window the foreground | passed | 2.22 s |
| Tab after a title-bar activation: the second window active, a real click on the app's title bar, Tab then Enter start "Start a long call" | passed | 1.12 s |
| The close confirmation: guard "Ask" picked with real keys and read back; a real click on the title bar's Close, then "Close without saving?" [Close anyway, Keep open]; Enter keeps it open; Close again, then a real click on "Close anyway"; exit 0 | passed | 2.29 s |
| "Wait / Close anyway" for a silent guard | failed before the dialog (the list's choice, above) | |
| 25 browser and devtools keys and the mouse's back and forward buttons, with the "New page" dialog open and "Draft" typed: no new window in the app's process tree, the dialog's heading not moved, the field still "Draft", no browser UI in the dump; then "x" typed gives "Draftx" | passed: "nothing happened" for each of the 27 | 28.25 s |
| The text field's menu (a real right-click on "Search pages"): exactly Cut, Copy, Paste, Select all, all disabled on the empty field; Escape closes it; a right-click on the heading "Start your notebook" opens none | passed | 3.57 s |
| The clean, offline first run in a fresh sandbox: ready after 3,519 ms, title "Notebook", the page's heading, close with exit 0, every process gone, the log and the browser profile written; the `data` folder not created (the Notebook creates it at its first page) | passed | 4.01 s |
| The sign-out | see [`docs/evidence/sandbox-signout`](../../docs/evidence/sandbox-signout/RESULTS.md): the sandbox stays and the logs come back, but the app was ended with the test process, about 130 ms after `shutdown /l`, before its session end. The app is now launched outside the fixture's kill-on-close job (`OutlivesTest`); not run again | |

The Production input checks were also all "nothing happened" in run 3. The F5 and Ctrl+R pair of a Development build was not checked: the payload carries only the Production executable.

### The Sandbox client window on the host

| Run | `wsb connect` until a user command runs | Minimized | Of those, in the foreground | Left after the stop, closed by `WM_CLOSE` |
|---|---|---|---|---|
| 1 | 2.5–3.1 s | 10 | 7 | all |
| 2 | – | 6 | 3 | all |
| 3 | – | 7 | 4 | all |
| 4 | 2.7–3.5 s (5–7 attempts) | 10 | 7 | 3, 3 and 5 windows, closed in 63–70 ms, none left |

So the client took the host's foreground 3 to 7 times per run (1 to 3 times per connect), each time minimized within 100 ms. `wsb connect` returned on its own every time. `wsb list` was empty after every run.

## Run 5 (`124634`), run by the coordinator with the user's allow

Same command; 30 of 32 passed in 2 min 59 s. "Wait / Close anyway" passed: a real click on Wait kept the window, and the re-ask about 20 s later was answered "Close anyway" with a real click, exit 0. The two failures:

- **The close confirmation** stopped while it set its scene. After its arrow keys the guard's list still read "No guard" ("Timed out after 5 s waiting for the close guard reads 'Ask'"). It passed in run 4. Fix: the window is activated by its title bar when it is not in the foreground; the list is focused through UI Automation before every key; each key is read back; and the walk goes up or down by the list's known order. Choosing the option through UI Automation was tried on the host and does not work for Chromium's `<select>`: no options are in the tree, even after `Expand`, and `SetValue` changes nothing. The dialog's answers stay real keys and clicks.
- **The sign-out**: `RunnerTests` found no "WM_ENDSESSION held" line. With the app outside the kill-on-close job, its log ([`user-runs/20261119-124634/s03/sign-out-app.log`](m1b-sandbox/user-runs/20261119-124634/s03/sign-out-app.log)) shows the session end working: "session ending at 4878.1 ms", the page's and the backend's guards answering Close, the teardown, and "services disposed at 4912.8 ms", **about 35 ms in all**. It also shows two host defects, both fixed after the run:
  - **The last lines were lost.** `EndSession` logs its "session end: …; WM_ENDSESSION held N ms" through the queued writer and returns, and Windows ends the process before the writer reaches the disk. The host now closes the log file synchronously before `WM_ENDSESSION` returns, bounded by what is left of the 4 s budget (`LogFile.Close(TimeSpan)`, through `IAppWindowOwner.SessionEndLogged`). Other exits were checked: a normal exit and a failed start close the file in `WindowsApp.Run`'s `finally`, and "Exit now" closes it before `ExitProcess`.
  - **The host recovered the browser during the session end.** The browser process died 17 ms in ("browser lost (the browser process failed) at 4895.9 ms"), and the host logged "WebView2 Runtime …" and "Browser profile folder …" again: it was creating a new environment while the session ended. Recovery now stops when a session end or an approved close starts (`BrowserView.EndRecovery`, called by `EndSession` and at the teardown's start). Before this, only the teardown's controller close (step 5) stopped it.
- After the sign-out the sandbox was still listed, and no user session answered (`0x80070520`), as in run 4.

The host-side tests of both fixes:
- `SessionEndHostTests.A_renderer_lost_during_the_session_end_is_not_reloaded`: a Window test that ends the page's renderer while the page's guard holds a simulated session end. It failed 3 of 3 with the fix taken out and passes with it.
- `SessionEndHostTests.The_session_ends_last_lines_are_in_the_log_when_Windows_ends_the_process_right_after`: a black-box test that sends `WM_QUERYENDSESSION` and `WM_ENDSESSION` to WindowHost's Production window and ends the process the moment the second returns. It passes, but it also passed 3 of 3 with the fix taken out: on the host the writer is faster than the kill, so it guards the line without reproducing its loss.
- `LogFileTests.A_bounded_close_writes_what_is_queued_before_it_returns_and_a_spent_budget_does_not_wait`: the bounded close itself.
- In the full window suite the renderer test first disturbed another window's session end (3.0–3.1 s instead of about 0.1 s, in 5 of 6 runs), although it ended one renderer, its own window's. It now runs with no other test at the same time, and the suite then passed 5 of 5 (106 tests each). The Production Notebook is 5,236,736 B after the two fixes (+1,024 B).

## Run 6 (`205820`), with the user's allow (2026-11-19)

`SPAR_SANDBOX_DESKTOP=allow dotnet test --project tests/Spar.SandboxTests` on the M1b step-9 tree: 32 results, 31 passed, 1 failed, 2 min 50 s; three sandboxes (130.7, 10.6 and 27.1 s).

- **Passed:** the SYSTEM jobs (window tests 5, Notebook 1, the deadline job), the user-session jobs (window tests 5: the Production input checks, focus, the first click, Tab into the page and the close confirmation answered with real keys and the mouse, after its run-5 change; Notebook 2), and the clean-machine job (1).
- **Failed: the sign-out** (`Signing_the_guest_out_ends_the_app_within_the_session_end_budget`): "The app's log has no 'WM_ENDSESSION held' line". The rest of the session end was in the log and fast: `session ending` at 4,270.8 ms, the page's guard answered (its renderer had exited) at 4,286.4, the backend's at 4,286.5, the controller closed at 4,287.8, `window destroyed` at 4,290.8 and `services disposed` at 4,290.9 ms. The final line, written once the pump sees the services disposed (it checks every 20 ms), never came. The guest's own test process ended at 21:00:51.77 local, the app's last line at 20:00:51.791 UTC.
- **Reading:** Windows ended the process as soon as the window holding `WM_ENDSESSION` was destroyed, without waiting for the handler to return. Run 5's fix (the log closed before the message returns) was right but came after that point. Microsoft's documentation for `WM_ENDSESSION` says an application need not destroy its windows when the session ends.
- **Fix (not yet run in a sandbox):** at session end the teardown closes the page and the controller and keeps the window; `WM_ENDSESSION` waits for that, the services' disposal and the final line with the log's flush, then posts a message of the window's own that destroys it after the handler returns, should the process outlive it. The host's session-end tests check the order locally (their `WM_ENDSESSION` comes from `SendMessage`, which does not end the process, so they cannot show the kill itself); the renderer test now measures to `controller closed`. The window suite passed 118 of 118 twice on CoreCLR and once on NativeAOT, the Notebook's window (10) and black-box (6) tests too.

## Run 7 (`211851`), with the user's allow (2026-11-19)

The same command on run 6's fix: 32 results, 31 passed, 1 failed, 3 min 6 s.

- **The sign-out passed:** `session ending` at 4,152.7 ms, `controller closed` at 4,175.7, `window kept until WM_ENDSESSION returns` at 4,175.8, `services disposed` at 4,176.0, and the final line, `session end: window closed, last work done; WM_ENDSESSION held 45 ms`, at 4,197.8 ms, all in the log. The 22 ms between the last two is the pump's 20 ms check.
- **Failed: the first click on an inactive window** (`The_first_click_on_an_inactive_window_reaches_the_page`), in its setup: the helper window it clicks with real input did not take the foreground within 5 s. The Tab test, with the same setup, passed in the same job, and the check passed in run 6. The run kept no evidence of which window held the foreground. Both tests now note it (the foreground window's class and process, both windows' rectangles) and save a screenshot before failing, so the next run that sees it explains it.

## Not run here

- Why the first click's helper window once did not take the foreground (run 7): run 7 was the last allowed user-session run.
- A browser process (rather than a renderer) lost during a session end on the host: the test process's window tests share one browser, so the host test ends the renderer.
- The page's `online` and `offline` events: the guest has no network adapter to switch (its configuration turns networking off), so only "starts offline" is checked.
- `RunnerTests` on a machine without Windows Sandbox (it would be one skip naming why).
