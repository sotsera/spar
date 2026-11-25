# Windows Sandbox sign-out: results

> A spike. Its code was deleted with `spikes/` at the start of M2; it is in git history at [`spikes/sandbox-signout/`](https://github.com/sotsera/spar/tree/24f5db0942833047c81fa692908410596f3b8960/spikes/sandbox-signout) (commit `24f5db0`), and this record and its `results/` moved here. Paths under `spikes/` below name that history.

Recorded 2026-11-19. Host: Windows 11 Pro 10.0.26320, `wsb` 0.8.114.0. Guest: Windows Sandbox (Windows 10.0.26100), network, clipboard, printers, camera and microphone off, one writable mapped folder.

The question (M1b step 8c, decision "Step 8c's shape: the Windows Sandbox runner"): with a Spar app open and a close guard waiting, does a real sign-out of the guest leave the sandbox running, can results still come back through the mapped folder, does `wsb` report it, and does the app get `WM_QUERYENDSESSION`/`WM_ENDSESSION` and finish within the 4 s budget? Can the sign-out check be an ordinary Sandbox test, or does it need a runner job of its own?

## State

| Part | Needs | Done |
|---|---|---|
| 1. Restart and shutdown of the guest as SYSTEM (no `wsb connect`, so no client window on the host) | nothing | **yes**, below |
| 2. Sign-out of the guest's user session with an app open | `wsb connect`: the Sandbox client window takes the host's foreground, so it ran only with the user's allow | **yes**, as the Sandbox check `Spar.WindowTests.SessionEndTests` (trait `Sandbox=SignOut`) in `tests/Spar.SandboxTests`, a job in a sandbox of its own: 4 runs, the first three of which ended before the sign-out (no Runtime found in the guest, then the guard's list not reached); run 4 signed out |

## Part 1: restart and shutdown as SYSTEM

[`system-restart.sh`](https://github.com/sotsera/spar/blob/24f5db0942833047c81fa692908410596f3b8960/spikes/sandbox-signout/system-restart.sh) starts a sandbox, probes it with `wsb exec -r System`, restarts the guest, probes it until it answers again, then shuts it down. Runs: `results/system-restart-1/` (an early version; the script was edited while it ran, so it ended without its stop, and the sandbox was stopped by hand afterwards) and `results/system-restart-2/`.

| Step | system-restart-1 | system-restart-2 |
|---|---|---|
| `wsb start` until a SYSTEM command runs | 2.8 s | 2.5 s |
| `shutdown /r /t 0` (no `/f`) | exit 0; the next `exec` 0.8 s later worked; whether the guest restarted is not known (the boot-time probe printed nothing) | — |
| `shutdown /r /f /t 0` | — | exit 0; `exec` answered once more (2.9 s), then failed with `0x8007139F` ("not in the correct state") for about 9 s, then worked again 18.3 s after the command. **The sandbox survived the restart**, still listed by `wsb list` throughout, and the mapped folder still took writes afterwards (`after.txt`) |
| `shutdown /s /t 0` (no `/f`) | **exit 1717** (`RPC_S_UNKNOWN_IF`); nothing happened: the sandbox stayed listed for 100 s and still ran commands | — |
| `shutdown /s /f /t 0` | — | exit 0; the next `exec` failed with `0x80070013` ("write protected"), and **the sandbox was gone from `wsb list` 1.8 s after the command**; `wsb stop` then had nothing to stop |

The uptime probe (`powershell [Environment]::TickCount64` redirected to the mapped folder) wrote empty files, so the restart is shown by the 9 s of `0x8007139F` only.

## Part 2: the sign-out

The check (`tests/Spar.WindowTests/Sandbox/SessionEndTests.cs`): in the guest's user session it launches WindowHost's Production executable on the guest's screen, keeps its data, picks the page's close guard "Keep after 5 seconds, ignoring the signal" with real keys, writes the data root to its results, and runs `shutdown /l`. The job (`window-tests/SignOut` in `tests/Spar.SandboxTests`) runs in a sandbox of its own. After the payload ends, the runner waits 15 s, copies the job's `*.log` files from the guest's disk as SYSTEM, and records whether the sandbox is still listed and whether a user session answers. The host then reads the app's log.

Run 4 of 2026-11-19 (`results/sign-out-run-4/`: the sandbox's timeline, the guest script's notes and exit code, the test's notes, and the app's log; times in UTC):

| When | What |
|---|---|
| 11:24:48.13 | the guest script starts the test executable |
| 11:24:51.979 | the app's `ui-ready` (3.36 s after its process started; first run of a profile in a fresh guest) |
| 11:24:52.747 | `shutdown /l` asked, the scene set |
| 11:24:52.88 | **the test process ended with `0x40010004`** (`DBG_TERMINATE_PROCESS`, the code a process gets when the session's end terminates it), about 130 ms after the sign-out was asked. The guest script still wrote that code to `exitcode.txt`, and `wsb exec` returned it |
| (none) | **the app's log stops at 11:24:51.989** (its favicon's 404): no `session ending`, no `WM_ENDSESSION held` line. The app ended without its session end |
| +15 s | the runner collected the logs as SYSTEM (robocopy exit 1, files copied): **results still come back through the mapped folder** |
| after | `wsb list` **still listed the sandbox**; `wsb exec -r ExistingLogin` failed with `0x80070520` ("A specified logon session does not exist"): **no user session came back by itself** |

The app's process was in the black-box fixture's job object, created with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` so that a crashed test run leaves no app behind. The sign-out terminated the test process first, its job handle closed, and Windows ended the app with it. This is inferred from the timing and the log: the app's end was not observed directly. The first three runs never reached the sign-out: the guest found no WebView2 Runtime (runs 1 and 2, `docs/pitfalls.md`), then the guard's list was below the window's fold (run 3).

## Run 5: with the app outside the kill-on-close job

Run by the coordinator with the user's allow (`results/sign-out-run-5/`). The app's log shows **the session end working, in about 35 ms**: "session ending at 4878.1 ms", the close request sent to the page, the page's and the backend's guards answering Close, the teardown, and "services disposed at 4912.8 ms". Two host defects show in the same log, and both were fixed in the host afterwards (decision record of step 8c):

- **The last line is missing:** no "session end: …; WM_ENDSESSION held N ms". The line is logged through a queue, and Windows ended the process before the queue reached the disk. The host now writes the file out before `WM_ENDSESSION` returns, within the budget.
- **The browser was being recreated:** the browser process died 17 ms into the session end, and the host began creating a new environment ("WebView2 Runtime …" logged again). Recovery now stops when a session end starts.

The guard picked for the scene ("Keep after 5 seconds, ignoring the signal") did not hold the session end: the page went away with its browser, which counts as the page agreeing. The sandbox was still listed afterwards, and no user session answered.

## Verdict

- **A sign-out keeps the sandbox.** Mapped folders and SYSTEM commands still work afterwards. No user session comes back on its own: a later user-session job would need a new `wsb connect`.
- **A guest restart keeps the sandbox; a forced shutdown ends it at once.** A runner that restarts the guest must wait about 20 s and probe again; one that shuts it down has nothing left to stop.
- **The sign-out cannot be an ordinary Sandbox test, and it needs the app outside the kill-on-close job.**
  - The test process runs in the session that ends, so it cannot observe the end, and it is terminated about 130 ms after the sign-out is asked, before the app.
  - So the sign-out is a runner job of its own: a fresh sandbox, the scene set and the data root written before the sign-out, the logs collected by the runner as SYSTEM afterwards, and the check made on the host.
  - The app is now launched with the fixture's internal `OutlivesTest` option: a job that tracks its processes without ending them. The option is refused outside a runner's guest.
  - **Measured in run 5:** with the app outside that job, the real sign-out's session end ran in about 35 ms, well within 4 s, by its log. Its last line was lost and the browser was being recreated: both fixed in the host. **Not verified:** a sign-out after those fixes; the next allowed run checks it in `RunnerTests.Signing_the_guest_out_ends_the_app_within_the_session_end_budget`.

## Reproduce

From the repository root, Git Bash, with no sandbox running: `spikes/sandbox-signout/system-restart.sh <name>` (results in `spikes/sandbox-signout/results/<name>/`, the mapped folder under `artifacts/sandbox-signout/<name>/`). Part 2: `SPAR_SANDBOX_DESKTOP=allow dotnet test --project tests/Spar.SandboxTests`, only after asking the person at the computer.
