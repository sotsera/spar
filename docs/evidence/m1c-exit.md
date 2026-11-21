# M1c exit: the Notebook on Linux, the budgets, the old-WebKitGTK refusal and the shared host suite

Recorded 2026-11-21. What it shows: the Notebook as a single-file NativeAOT executable on Linux with its size, the M1c budgets of plan "Provisional budgets" (Linux column) measured with the M1b exit's method (decision "M1b budgets measured at the exit", `m1b-budgets.md`), the start-up refusal of a WebKitGTK older than 2.48 with exit code 4, and the host-lifecycle suite both hosts pass in CI. Decision "M1c budgets measured at the exit (implementation record; 2026-11-21)".

Code: `161ca00` (the CI-green `308364a` plus one decision entry) with two test-only additions of this record: `BudgetTests` records the total PSS on Linux and the round trip's mean, and the Notebook's Linux test (`LinuxHostTests.The_Notebook_starts_and_closes_and_its_numbers_are_recorded`) also records its launch from the process's start. No product code changed.

Machine: the development PC, Windows 11 Pro 10.0.26320, 32 logical CPUs; WSL 2 Ubuntu 24.04.5 (kernel 6.18.58.1-microsoft-standard-WSL2, 46 GB), GTK 4.14.5 and WebKitGTK 2.52.6 as installed in the distribution, SDK 11.0.100, the Xvfb that `m1c-host/wsl.sh tools` unpacked (nothing installed for this record). Every window and every launched app ran on a private Xvfb on an abstract socket (the test host's from :140, the black box's from :180), with `DISPLAY` and `WAYLAND_DISPLAY` unset: nothing opened on WSLg's display. The software-GPU default was on (no render node in WSL: `GSK_RENDERER=cairo`, `WEBKIT_DISABLE_DMABUF_RENDERER=1`). The runner sets the core limit to 1 byte. The PC was otherwise idle (2% CPU load before the runs).

## Commands

The runner is [`m1c-exit/wsl.sh`](m1c-exit/wsl.sh); it works in `m1c-blackbox/wsl.sh`'s copy of the tree and environment. From Git Bash at the repository's root:

```bash
MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/docs/evidence/m1c-exit/wsl.sh sync budgets launch launch budgetsaot notebook minimum
```

What the steps run, in the copy:

```bash
dotnet test --project tests/Spar.WindowTests -c Release --filter-class "Spar.WindowTests.BudgetTests"               # budgets
dotnet test --project tests/Spar.WindowTests -c Release -- \
  --filter-method "*The_Production_app_starts_to_ui_ready_and_holds_its_working_set"                                # launch
dotnet publish tests/Spar.WindowTests -c Release -p:SparTestLane=Aot -o artifacts/aot/Spar.WindowTests              # budgetsaot
artifacts/aot/Spar.WindowTests/Spar.WindowTests -class "Spar.WindowTests.BudgetTests"
dotnet publish samples/Notebook -c Release -r linux-x64 -o /var/tmp/spar-m1c-blackbox/notebook-exit                  # notebook
SPAR_LINUX_NOTEBOOK=<the executable copied alone> dotnet test --project tests/Spar.WindowTests -c Release -- \
  --filter-method "*The_Notebook_starts_and_closes_and_its_numbers_are_recorded"
dotnet test --project tests/Spar.Tests -c Release -- --filter-method "*WebKitGTK_2_48_is_the_oldest_supported"        # minimum
SPAR_LINUX_HOST_APP=<the probe's Development build> dotnet test --project tests/Spar.WindowTests -c Release -- \
  --filter-method "*A_WebKitGTK_older_than_required_is_exit_4_with_a_message"
```

What each budget line measures is as in `m1b-budgets.md`: WindowHost's Production NativeAOT executable through the black-box fixture, 1 warm-up launch and 10 measured on the same data root, from the process's creation (the host's "Host started … after the process" plus its "ui-ready at …"), 2 s after `ui-ready` for memory; 20 window tests of open, the 8-step journey and close; 200 echoes timed by the page after 20 warm-up calls.

## The budgets (Linux)

| Budget | Measured | Met | Reused or measured again |
|---|---|---|---|
| Launch to `ui-ready`, warm, Release, Linux GTK4: ≤ 350 ms | WindowHost, Production NativeAOT, profile reused, from the process's start: **220.9 ms (195.3–229.5, n = 10)** with the CoreCLR test lane, **213.2 ms (205.2–231.5, n = 10)** with the NativeAOT lane; the launch alone, twice: 217.1 ms (205.5–228.6) and 222.5 ms (211.7–229.6) | Yes | Measured again: the black-box record's 213.1 ms was the Debug test lane and before the fixture's session-bus change |
| Small unary round trip on the desktop bridge, median, timed by the page: ≤ 1 ms | **median 0.00 ms, p95 1.00 ms (0.00–4.00, n = 200), mean 0.285 ms** (CoreCLR lane); median 0.00 ms, p95 1.00 ms (0.00–4.00), mean 0.245 ms (NativeAOT lane) | Yes | Measured again |
| Window test, open + journey + close, median, Linux: ≤ 500 ms | **189.8 ms (183.7–775.4, n = 20), p95 221.7 ms** CoreCLR; **189.4 ms (186.7–236.3, n = 20), p95 236.1 ms** NativeAOT | Yes | Measured again: step 7's 237.7 and 225.0 ms were on earlier code |
| Notebook NativeAOT executable, Linux: ≤ 8 MB | **5,141,248 B** (4.90 MiB) | Yes | Measured again; CI's black-box step recorded the same 5,141,248 B at `308364a` |
| Total memory with the web engine, Linux GTK4 (PSS): recorded | WindowHost, the app and WebKit's processes, 2 s after `ui-ready`: **200.0 MB (199.8–202.8, n = 10)** and 199.9 MB (199.7–202.6, n = 10) in a test process with no WebKit of its own; 166.1 MB (165.8–168.7) and 165.7 MB (165.6–168.4) inside the whole class. The Notebook, one run 1 s after `ui-ready`: **233.2 MB** (232.8–234.2 over this record's runs) | Recorded | New with the M1b statistics; earlier steps recorded the Notebook's one run, 231–235 MB |

Raw: [`budgets-coreclr.txt`](m1c-exit/budgets-coreclr.txt), [`budgets-aot.txt`](m1c-exit/budgets-aot.txt), [`budgets-launch-1.txt`](m1c-exit/budgets-launch-1.txt), [`budgets-launch-2.txt`](m1c-exit/budgets-launch-2.txt), with the console in `budgets.log`, `budgetsaot.log` and `launch.log`.

For the record, in the same launches: the app process's working set 159.4–159.6 MB median, 374.5–374.7 MB with WebKit's processes (RSS counts each shared library once per process; Windows measured 42.3 and 358 MB).

### Notes on the numbers

- **The round trip at WebKitGTK's timer.** `performance.now()` in WebKitGTK counts in whole milliseconds, so a single call reads 0 or 1 ms (a few read 2–4 ms). The median of single calls is 0.00 ms: more than half of the 200 calls ended in the tick they started in, which is unlikely for calls of 0.5 ms or more; the budget's median is under 1 ms. The mean of the 200 readings estimates the call's time, since a call taking a fraction *f* of a tick reads 1 ms in about *f* of the samples: 0.25–0.29 ms, as step 1 measured (0.23–0.26 ms) and the spike (0.20–0.29 ms). `BudgetTests` now records that mean on both hosts.
- **PSS depends on what else maps the same libraries.** PSS shares each page among the processes that map it. Inside the whole `BudgetTests` class, the test process has opened its own WebKitGTK windows (and their web processes) before the launch test, and takes a share of GTK's and WebKit's libraries from the app: 166 MB. Run alone, in a test process that never loads WebKit, the same launches total 200 MB, which is what a user's machine sees with no other WebKit app running. The recorded value is the 200 MB. The spike's 413–426 MB was measured after its journey, with GTK4 drawing through GL on llvmpipe (about 87 MB of the app's PSS by the spike's account); the host's software-GPU default draws with cairo. Neither difference was measured here.
- **The first window test's 775 ms** is the first window of the test process (its Xvfb, D-Bus session, GTK and the engine check, 519 ms of it); the median and p95 are unaffected. The NativeAOT lane's first window had already been made by an earlier test.
- **On CI's runner** (the `linux-window` job at `308364a`, GitHub's `ubuntu-26.04` runner, the container of `eng/linux-ci/Dockerfile`, NativeAOT lane): launch 266.95 ms (259.6–276.0, n = 10), window test 301.7 ms (280.3–352.4), round trip median 0.00 ms, p95 1.00 ms, working set 160.2 / 391.7 MB ([`ci-linux-budgets.txt`](m1c-exit/ci-linux-budgets.txt)). The budgets are checked on the development PC, as at the M1b exit; these show the runner's margin.

## The Notebook on Linux

`dotnet publish samples/Notebook -c Release -r linux-x64` writes `Notebook`, 5,141,248 B, an ELF 64-bit PIE executable, stripped, with the page embedded; beside it only symbol files: `Notebook.dbg` (7,791,208 B, its native symbols) and the libraries' `Spar.pdb`, `Spar.Linux.pdb` and `Spar.Windows.pdb`. The test ran the executable copied alone into an empty folder, as it ships. On the black box's private Xvfb, with a private session bus, the accessibility bus and registry per launch and a new data root each time, it reached `ui-ready` in each of 12 launches, closed with exit 0 each time (close request to exit 41.8 ms median, 17.5–54.6), came back after its web process was ended, and ran without WebAssembly (`JSC_useWasm=false` in the web process, also after the loss). Raw: [`notebook.txt`](m1c-exit/notebook.txt), [`notebook.log`](m1c-exit/notebook.log).

Its own launch to `ui-ready` (the last 11 of 12 launches; not the budget's measure, which is WindowHost's, as at the M1b exit):

| Run | From the test's clock at the launch | From the process's start (the host's log) |
|---|---|---|
| 1 | 281.6 ms (273.4–290.3) | not recorded yet |
| 2 | 316.9 ms (301.9–329.4) | not recorded yet |
| 3 | 308.9 ms (305.1–318.7) | 308.3 ms (302.6–317.6) |
| 4 | 312.8 ms (307.8–335.8) | 311.2 ms (303.5–332.2) |
| 5 | 308.7 ms (295.5–422.1) | 305.7 ms (293.5–419.9) |
| 6 (the executable alone in its folder) | 312.4 ms (289.3–325.3) | 308.5 ms (288.9–324.6) |

Under the 350 ms budget, with less margin than WindowHost (about 213–222 ms), and higher than steps 1–6 recorded for the same test (210–228 ms). What differs, found and not found:
- The two clocks agree within 4 ms, so the black box's own start of the launch's buses before the app is not in the number.
- Steps 1–6 launched the Notebook with no session bus at all (`tests/Spar.Tests/LinuxAppRun.cs` of that time); since step 7 the black box gives each launch its own session bus, and since `68db043` it starts the accessibility bus and registry first, as a desktop session has them. The black-box record of 2026-11-21 00:22, before that change, had 283.3 ms (`m1c-blackbox/linux-host-default.txt`).
- One run in an edited copy of the tree with GTK's accessibility turned off (`GTK_A11Y=none`, which the fixture normally refuses as its own variable) gave 283.0 ms (283.9 ms from the process's start; [`notebook-a11y-off.txt`](m1c-exit/notebook-a11y-off.txt)). That is inside the spread of the normal runs (281.6–316.9 ms), so accessibility is not shown to be the cause; an interleaved comparison would be needed.
- In one launch's log the window was shown at 69.5 ms, the page committed at 186.2 ms, the first bridge message came at 213.2 ms and `ui-ready` at 281.7 ms: the time after the window is shown is the web process's start, the page's load and the page's own work before it reports ready.
- On CI's runner the same test recorded **391.5 ms (374.1–472.2)** ([`ci-linux-notebook-and-minimum.txt`](m1c-exit/ci-linux-notebook-and-minimum.txt)), over 350 ms; WindowHost was 267.0 ms there.

## The start-up refusal of an old WebKitGTK

The exit criterion is covered by two tests together:
- `tests/Spar.Tests/LinuxStartupUnitTests.cs`, `WebKitGTK_2_48_is_the_oldest_supported`: `Prerequisites.DecideWebKit` with the real minimum (`Prerequisites.MinimumWebKit`, "2.48.0") accepts 2.52.6 and 2.48.0, and refuses 2.46.9 and 2.44.0 as `TooOld` with exit code 4, component "WebKitGTK 6.0", the found and required versions and the distribution's upgrade hint. 4 of 4 passed in WSL.
- `tests/Spar.WindowTests/Linux/LinuxStartupTests.cs`, `A_WebKitGTK_older_than_required_is_exit_4_with_a_message` (`Probe=Development`): the probe's Development build with `SPAR_DEV_REQUIRE_WEBKITGTK=99.0` raises the minimum above the installed WebKitGTK, so the real check reads the real library's version (`webkit_get_*_version`), compares it in the same `DecideWebKit`, shows the start-up message in a window, logs the line the test matches, "Start-up problem TooOld: WebKitGTK 6.0 (found 2.x.y, required 99.0.0)", and exits 4 once the message is closed. In WSL: passed, the problem reported 90.8 ms after the launch, the message shown at 168.5 ms, exit 4 ([`minimum.log`](m1c-exit/minimum.log), [`minimum-startup.txt`](m1c-exit/minimum-startup.txt)). In CI: the `blackboxdev` step of run [44888383564](https://github.com/sotsera/spar/actions/runs/44888383564) (job "window tests (Linux container)", 136798693433, at `308364a`) passed it in 228 ms, reported 105.7 ms after the launch, message at 200.4 ms, exit 4.

The raised minimum is a Development-build variable only (`SparDevelopment.IsSupported`); a Production build always checks 2.48.0. No WebKitGTK older than 2.48 was run: Ubuntu 24.04 ships 2.52.6, and installing an older one is a system change.

## The host-lifecycle suite on both hosts (CI, `308364a`)

From run 44888383564's test results (the `.trx` files of its artifacts; [`ci-suites.txt`](m1c-exit/ci-suites.txt)), not run again. The Linux job ran, in one container: the window step (CoreCLR, `Surface=Window`) 66 tests, 62 passed, 4 skipped; the NativeAOT window step the same 66, 62 and 4; the Notebook's window journeys 10 of 10 on both lanes; the black-box step (CoreCLR, the Production probe) 80 of 80 and the Notebook's black-box tests 6 of 6; the `blackboxdev` step (the probe's Development build) 63 of 63. On Windows the `tests` job ran the window and black-box tests on CoreCLR and the `aot` job on NativeAOT.

The shared suite is `tests/Spar.WindowTests`, one project for both hosts with no OS branches in its tests (decision "M1c step 7's shape"): the in-process `Window` surface and the black box on each host's real window.

| Class | Tests | Windows passed (CoreCLR / NativeAOT) | Linux passed (CoreCLR / NativeAOT) | One host only, and why |
|---|---|---|---|---|
| `HostTests` (the required host tests) | 8 | 8 / 8 | 8 / 8 | |
| `CloseTests` | 15 | 15 / 15 | 15 / 15 | |
| `SessionEndHostTests` | 5 | 5 / 5 | 5 / 4 | The black-box case runs in Linux's CoreCLR black-box step only |
| `BlackBoxFixtureTests` | 20 | 20 / 20 | 18 / not run | 2 are `OS=Windows`: the job object ending the app's tree, and a network `LOCALAPPDATA` (Windows' mechanisms); Linux CI runs the black box on CoreCLR only (the app is the same Production executable either way; the NativeAOT lane ran them in WSL in step 7) |
| `BudgetTests` | 4 | 4 / 4 | 4 / 4 | |
| `PageApiTests` | 11 | 11 / 11 | 10 / 10 | `Page_errors_reach_the_apps_log_through_the_host`: the Linux host does not report the page's errors yet (left by M1c step 4) |
| `PageDefaultsTests` | 2 | 2 / 2 | 1 / 1 | `Navigations_open_no_connection_to_the_loopback_address`: port 443 cannot be listened on without root on Linux, so connections cannot be counted |
| `PageSecurityTests` | 4 | 4 / 4 | 4 / 4 | |
| `KeyTests` | 9 | 9 / 9 | 9 / 9 | |
| `SmokeTests` | 2 | 2 / 2 | 2 / 2 | |
| `ThemeTests` | 5 | 5 / 5 | 4 / 4 | `Each_window_keeps_its_own_scheme_beside_another`: WebKitGTK follows GTK's dark preference of the whole process |
| `InstallPathTests` | 1 | 1 / 1 | skipped | Windows' `MAX_PATH`; Linux has no such limit |
| `NetworkStatusTests` | 1 | skipped | 1 / 1 | Switching the network is built on Linux (a network namespace); Windows checks the page offline in Windows Sandbox (`OfflineTests`) |
| **Shared total** | **87** | **86 / 86** | **81 / 62 + the black box's 19 on CoreCLR** | |

One host only besides: on Linux `LinuxHostTests` (17 in each black-box step), `LinuxHangTests` (7 on the Production probe, 9 on the Development build), `LinuxPageSecurityTests` (17), `LinuxStartupTests` (15), `LinuxSessionEndTests` (5), the Linux host's own mechanisms through the black box; left out of CI by their traits: the two flood and Notebook measurements (`Kind=Measurement`, run by the WSL runners) and `A_sandbox_the_system_denies_is_exit_5_with_a_message` (`Needs=WritableProcSys`, which the container does not allow). On Windows the Windows Sandbox classes (`SystemLaneTests`, `FocusAndDialogTests`, `OfflineTests`, `SessionEndTests`, `DeadlineProbeTests`; explicit, run by `tests/Spar.SandboxTests`).

## Not verified

- A WebKitGTK older than 2.48 at run time (the check is exercised with a raised minimum and the decision with the real one; see above).
- The cause of the Notebook's launch time being about 80 ms above WindowHost's and above steps 1–6's numbers, and CI's 391.5 ms; accessibility was tried once, not interleaved.
- A cold start after a reboot, a real Linux desktop and GPU, other machines; M4 sets the real budgets.
- The edited `BudgetTests` on Windows: built (0 warnings), not run there; its Windows line now also has the mean.
- The black box on the NativeAOT test lane in CI on Linux (CI runs it on CoreCLR).

## Files of this record

| Path | What |
|---|---|
| `m1c-exit/wsl.sh` | The runner |
| `m1c-exit/budgets-coreclr.txt`, `budgets-aot.txt` | `BudgetTests` in Release on each test lane |
| `m1c-exit/budgets-launch-1.txt`, `budgets-launch-2.txt` | The Production launch alone, twice (PSS without the test process's own WebKit) |
| `m1c-exit/notebook.txt`, `notebook.log` | The Notebook's publish, folder and its launches (run 6 above) |
| `m1c-exit/notebook-a11y-off.txt`, `notebook-a11y-off.log` | The one run with `GTK_A11Y=none`, in an edited copy of the tree (not the repository's code) |
| `m1c-exit/minimum.log`, `minimum-startup.txt` | The two minimum tests in WSL |
| `m1c-exit/budgets.log`, `budgetsaot.log`, `launch.log` | The consoles of the budget steps |
| `m1c-exit/ci-suites.txt` | CI's window and black-box results per class and host |
| `m1c-exit/ci-linux-budgets.txt`, `ci-linux-notebook-and-minimum.txt` | What CI's Linux job recorded |
