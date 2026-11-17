# Windows Sandbox runner: results

Recorded 2026-11-17. Host: Windows 11 Pro 10.0.26320, 32 logical CPUs, SDK 11.0.100, `wsb` 0.8.114.0, WebView2 Runtime 157.0.4341.46. Guest: Windows Sandbox, Windows 10.0.26100.10003, user `WDAGUtilityAccount`, the same 32 threads, **no .NET** (`where dotnet` finds nothing). The configuration turned off network, clipboard, printers, camera and microphone (`results/sessions/*/config.xml`). Another agent ran a WebView2 spike on the host at the same time (no Sandbox), so timings may be noisier than on an idle machine.

This answers issue J's spike (`docs/design/testing.md`): can a NativeAOT test executable run in the guest and report back, how long each step takes, whether two sandboxes can run at once, and what a host-side runner needs.

## Summary

| # | Question | Result | Recommendation |
|---|---|---|---|
| 1 | NativeAOT xUnit v3 executable in the guest, results through a mapped folder | **Yes.** `WireShapes.Tests` (Spar's AOT lane, 16.9 MB, no pdb) passed 79 of 79. The window payload (the `test-host` spike's AOT lane, 22.9 MB) passed 15 of 15, with 4 real WebView2 window tests among them (50 windows in sequence, 4 at once). TRX, xUnit XML, console log and exit code came back as files, read on the host 11–26 ms after `wsb exec` returned | Adopt: `-result-trx` and `-result-xml` into the mapped output folder, the exit code in a file moved into place last, checked against `wsb exec`'s JSON exit code |
| 2 | Time per step, cold sandbox per run (7 cycles) | `wsb start` 0.98–1.29 s; first `wsb exec` works 0.32–0.44 s later (1 attempt every time); `wsb connect` until `ExistingLogin` works 2.5–4.5 s; staging copy 5–24 ms; staged files visible at the first probe (0.30–0.74 s, which is one `wsb exec`); collecting 11–26 ms; `wsb stop` 0.83–0.97 s; closing client windows 21–93 ms. **Fixed cost per cycle 6.2–9.0 s** (median 6.55 s) with a session; **2.6 s without one** (SYSTEM only) | "Minutes per run" in issue J is wrong for this host: seconds. The test run itself dominates |
| 3a | Two sandboxes at once | **No.** The second `wsb start` (new id) failed after 330 ms: exit `-2147221002`, `Application cannot be run more than once (0x800401F6 (CO_E_APPSINGLEUSE))`. A second start with the running id failed the same way. `wsb list` showed only the first | The runner checks `wsb list` first and fails at once with a clear message (or waits): one sandbox per user session |
| 3b | One sandbox, several runs | Works: 6 jobs (3 × WireShapes + window tests) in one sandbox with 7.9 s of overhead in all, against 3 × 6.2–7.1 s for three cold cycles: about 5.8 s saved per extra cycle | Default: one sandbox per test run, jobs in sequence, each staged under a new folder. A fresh sandbox only for tests that need a clean machine (it costs about 6.5 s) |
| 4 | Host-side runner prototype | `Runner/` (C#, 839 lines, NativeAOT, 2.7 MB, 0 warnings): configuration, start, minimized connect, staging, deadline, collection, stop, client windows closed. 17 sessions, every sandbox stopped and every client window closed by the runner | See "What the runner needed" |
| 5 | CI | From documentation only: hosted `windows-latest` is Windows Server 2025 (`windows-2025`, also `windows-2022`); Sandbox is a Windows 10/11 feature and needs nested virtualization in a VM. The only hosted Windows 11 images are Arm64 (`windows-11-arm`), and nested virtualization on them is not documented | Local or self-hosted lane only, never plain `dotnet test`; issue J's stated limit holds |

**Other findings:**

- **Window tests need the user session.** As SYSTEM (no `wsb connect`), `CreateCoreWebView2Controller` failed with `0x80080005` (`CO_E_SERVER_EXEC_FAILURE`), 1 of 1. Tests without windows run fine as SYSTEM, with no client window on the host at all (79 of 79, 3 of 3 cycles of 3.5–3.6 s).
- **The client takes the foreground on every connect.** 28 of the 32 minimizes found the client window in the foreground: 2–3 times per connect, between 0.78 and 2.1 s after `wsb connect`. So a session lane still needs "ask first" on a desktop someone is using; the SYSTEM lane does not create a window.
- **A deadline loses the reports.** With an 8 s deadline the runner ended the wait, killed the test process as SYSTEM (`taskkill /f /t`, 483 ms) and collected the exit file (`1`) and a partial console log, but no TRX or XML: xUnit writes them at the end.
- **`wsb connect` returned on its own** in 11 of 11 sessions (it did not in `windows-single-instance`). The runner still kills it if it is running at the stop.
- **Client windows were left after the stop in 6 of 11 connected sessions** (3 windows of `WindowsSandboxRemoteSession`, hidden ones included); `WM_CLOSE` closed them in 19–93 ms and no client process was left.
- **Files staged under new folder names were visible at the first probe in 29 of 29 jobs.** The "few seconds" delay of earlier spikes did not reproduce; staging before `wsb start` gained nothing (copying takes 5–24 ms).
- **The guest's WebView2 registration matched its files today** (`pv` and `EBWebView` both 157.0.4341.46); a window test without the explicit folder passed. The mismatch seen earlier is intermittent, so the runner still passes the folder.
- **Windows are slower in the guest:** 50 windows in sequence, open + journey + close, median 719–762 ms (10 runs), against 466–472 ms on the host in `spikes/test-host`.

## What the runner needed

`Runner/Session.cs` runs one cycle; the order and the reason for each step:

1. **`wsb list --raw` first.** If any sandbox runs, refuse (only one per user session; a second start fails with `CO_E_APPSINGLEUSE`).
2. **The configuration as a string** to `wsb start --config` (no `.wsb` file): network, clipboard, printer, camera and microphone off; the input folder mapped read-only to `C:\spar\in`, the output folder writable at `C:\spar\out`. A new GUID per sandbox, passed with `--id`.
3. **Ready means a no-op runs:** poll `wsb exec -r System -c "cmd /c exit 0"` (each call costs about 0.33 s).
4. **Connect only when a job runs as `ExistingLogin`.** Snapshot the Sandbox client windows first, start `wsb connect` without waiting for it, poll `ExistingLogin` (4–6 attempts), and minimize every new visible client window every 100 ms until the stop (`ShowWindowAsync(SW_MINIMIZE)`; no input). Windows are matched by process name, `WindowsSandboxRemoteSession`.
5. **Stage each job under a new folder** (`jNN-<random>`), skipping `.pdb` files (the window payload's pdb is 99 MB, its exe 23 MB), with a marker file written last; probe for the marker as SYSTEM.
6. **A generated guest `run.cmd`** sets the environment (the WebView2 runtime folder, found under `Program Files (x86)\Microsoft\EdgeWebView\Application`, and a data root on the guest's own disk), runs the executable with its reports pointed at the output folder, writes the exit code to a temporary file, moves it to `exitcode.txt`, and exits with it. `wsb exec` relays nothing else.
7. **The deadline** is the `wsb exec` timeout. On expiry: kill the local `wsb` process, then `taskkill /f /t /im <exe>` in the guest as SYSTEM, then collect what exists. The deadline proves nothing stopped until the kill returns.
8. **Collect** by waiting for `exitcode.txt` on the host (at most 10 s), then read the TRX counters.
9. **Stop:** `wsb stop`, kill `wsb connect` if still running, post `WM_CLOSE` to the client windows that appeared after this runner's connect, wait until they are gone, and `wsb list` again.

What a Spar runner should add: queueing or a clear error when another sandbox runs; a results root the payload's tests can write to (see the WireShapes failure below); a streamed result format for partial results after a deadline; and the minimize loop only for sessions that need a user.

## Method

| Path | Contents |
|---|---|
| `Runner/` | The runner prototype (`session`, `two`, `list`): `Session.cs` (the cycle), `Wsb.cs` (calls, JSON exit codes, `wsb connect` in the background), `ClientWindows.cs` (enumerate, minimize and close Sandbox client windows; `LibraryImport` and an `UnmanagedCallersOnly` callback), `Timeline.cs` (JSON lines). Published with `-p:PublishAot=true`: 0 warnings |
| `payload/` | The window payload: `spikes/test-host`'s NativeAOT test lane (`SampleApp.AotTests`), its sources linked unchanged, except two copies with one change each: `WebViewWindow.cs` takes the runtime folder from `SPIKE_WEBVIEW2_BROWSER_FOLDER` and passes it to `CreateCoreWebView2EnvironmentWithOptions` (an elevated process ignores WebView2's own environment overrides); `TestDataRoots.cs` takes the artifacts folder from `SPIKE_ARTIFACTS` (the guest has no repository) |
| `run-all.sh` | `publish`, `cold` (3 cycles staged after start, 3 staged before), `reuse`, `edge` (deadline, window tests as SYSTEM, 3 SYSTEM-only cycles, two sandboxes) |
| `probe.cmd` | Guest probe: Windows version, WebView2 registration, runtime folders, a window test without the explicit folder |
| `summarize.mjs`, `collect-evidence.sh` | Tables from the timelines; copying the evidence into `results/` with the repository path, the profile path and SIDs redacted |
| `results/` | `summary.md` (every session and job), `sessions/<name>/timeline.jsonl`, `config.xml`, and per job `guest-env.txt`, `exitcode.txt`, `console.log`, `results.trx` |

Payloads: `WireShapes.Tests` published as the CI `aot` lane does (`-p:SparTestLane=Aot`, 19.7 s); `SampleApp.AotTests` published from `payload/` (21.6 s). Both had 0 warnings. On the host, `WireShapes.Tests` passed 79 of 79 and the window payload's 11 non-window tests passed (its window tests were not run on the host desktop).

Sessions: `s01`–`s03` were the first attempts (bugs below); `cold-1..3`, `prestaged-1..3`, `reuse`, `deadline`, `system-window`, `system-only-1..3`, `two` and `probe` are the measurements. Between sessions the scripts paused 5–20 s. The Sandbox server process (`WindowsSandboxServer`) was running before each of the 13 measured starts (a new one appears with a start); it exited on its own after the last session.

## Numbers

Cold cycles, each with WireShapes (SYSTEM, copied to the guest's disk) and the window tests (user session), in milliseconds (`results/summary.md`):

| Session | `wsb start` | first exec | start → user session ready | WireShapes run | window run | stop | close clients | total | total − runs |
|---|---|---|---|---|---|---|---|---|---|
| cold-1 | 1,082 | 336 | 4,385 | 1,123 | 40,543 | 856 | 22 | 48,155 | 6,489 |
| cold-2 | 1,072 | 324 | 4,502 | 1,118 | 41,654 | 832 | 76 | 49,323 | 6,551 |
| cold-3 | 1,213 | 435 | 4,218 | 1,099 | 41,430 | 929 | 28 | 49,122 | 6,593 |
| prestaged-1 | 1,293 | 389 | 6,187 | 1,525 | 41,412 | 960 | 93 | 51,905 | 8,968 |
| prestaged-2 | 1,121 | 352 | 4,353 | 1,116 | 41,032 | 868 | 77 | 48,700 | 6,552 |
| prestaged-3 | 981 | 323 | 4,232 | 960 | 41,453 | 948 | 29 | 48,646 | 6,233 |
| s03-both | 1,099 | 339 | 5,048 | 1,076 | 40,103 | 839 | 21 | 48,276 | 7,097 |

- "Total" runs from the `wsb start` call to the `wsb list` after cleanup. "First exec" is after `wsb start` returned.
- The `run` step is the whole `wsb exec`: WireShapes' executable ran 0.24 s in the guest (`guest-env.txt`), plus 0.08 s to copy it to the guest's disk and about 0.6 s of `wsb exec` and `cmd`. The window tests ran 39.2–39.7 s in xUnit's own count.
- Each job also spends one probe (0.30–0.74 s) waiting for its staged files.

SYSTEM-only cycles (no connect, no client window), WireShapes only: total 3,519 / 3,591 / 3,549 ms, of which the run was 923 / 936 / 911 ms.

Reuse, one sandbox for 6 jobs: total 131,959 ms, runs 124,032 ms, so 7,927 ms of overhead for 6 jobs, against 6.2–7.1 s for 2 jobs in a cold cycle. Every run passed except one WireShapes run (below).

Guest windows (`SPIKE 50 windows`, 10 runs): first window ready 300–2,660 ms; later windows ready median 503–561 ms; open + journey + close median 719–762 ms, max 980–1,418 ms; 0 engine processes left.

## Problems met

- **`cmd` quoting in the generated script** (the first runs):
  - `echo %RC%> file` with `RC=1` redirected stream 1 and wrote `ECHO is off.`;
  - `(echo browser=%FOLDER%)> file` broke on the `)` of `Program Files (x86)`, and the script exited 255 before running anything, with no output at all.

  Put the redirection first: `> file echo text`.
- **`WireShapes.Tests` needs the repository:** `BudgetTests.Command_test_app_start` failed in the guest with "These tests run from a build inside the Spar repository." (`Budgets.Root()` looks for `Spar.slnx` above the executable, to write `artifacts/test-results/`). The spike copies the payload to the guest's disk under a folder with an empty `Spar.slnx` (the job's `local` option), and the budgets file came back. A Spar requirement: tests that write results take their folder from the runner (an environment variable or xUnit's results directory), never from the repository's location.
- **A flaky Spar test, not the Sandbox:** `RatesContractTests.Without_an_implementation_the_app_does_not_start` failed once in 13 guest runs ("The app's Program.cs returned 70 without calling R…" instead of the contract message), and 4 times in 60 runs of the same AOT executable on the host.
- **The deadline lost the TRX and XML** (above).
- **SYSTEM cannot create a WebView2 controller** (`0x80080005`).
- **Two sandboxes:** `CO_E_APPSINGLEUSE` (above).

## Not tested

- **Real input** (`SendInput`, focus, drag and drop) through this runner; the payload's window tests use script-driven events. Earlier spikes drove real input in the guest.
- **Unattended hosts:** a locked host, a signed-out console, a Remote Desktop host session, or a machine with no monitor; whether `wsb connect` and the guest's rendering still work there.
- **Truly cold starts:** the first start after a reboot or a Windows update (the Sandbox base image may be rebuilt). The server process was always running here.
- **Long series** (hundreds of cycles), memory settings (`MemoryInMB`), vGPU on or off, a networked configuration.
- **CI:** nothing ran on GitHub. `windows-11-arm` and self-hosted runners are not checked.
- **DPI:** the guest stays at 96 DPI (`windows-sandbox-checks`).
- **Partial results after a deadline** with a streamed format (`-reporter json` or `-result-ctrf` written as the run goes).

## Reproduce

From the repository root, Git Bash. The session lane opens the Sandbox client on the desktop and it takes the foreground briefly, so ask whether the desktop is in use first:

```bash
spikes/sandbox-runner/run-all.sh publish          # payloads to artifacts/sandbox-runner/payload, runner to artifacts/sandbox-runner/runner
spikes/sandbox-runner/run-all.sh cold
spikes/sandbox-runner/run-all.sh reuse
spikes/sandbox-runner/run-all.sh edge
artifacts/sandbox-runner/runner/Runner.exe list   # 0 running, no client window
spikes/sandbox-runner/collect-evidence.sh         # results/ and results/summary.md
```

Documentation read for question 5: [Install Windows Sandbox](https://learn.microsoft.com/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-install) (Windows 10 1903 or later, or Windows 11; virtualization, nested virtualization in a VM) and [GitHub-hosted runners](https://docs.github.com/actions/reference/runners/github-hosted-runners) (`windows-latest` is the Windows Server 2025 image; the Windows 11 images are Arm64 only).

Cleanup: every sandbox was stopped by the runner, `wsb list` was empty after each session and at the end, no `WindowsSandboxRemoteSession` or `wsb` process was left, and the client windows the runner's connects opened were closed. Disposable data is under `artifacts/sandbox-runner/`.
