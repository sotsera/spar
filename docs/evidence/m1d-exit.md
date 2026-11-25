# M1d exit: every budget again, the template's CI workflow, and the checks left to the user

Recorded 2026-11-24. What it shows: plan "Provisional budgets" measured on M1d's code on Windows and Linux, M1d's two budgets (the Notebook's no-op build with the frontend step, Hot Reload of a method body) and every earlier line, with the method of decisions "M1b budgets measured at the exit" and "M1c budgets measured at the exit"; the new app's GitHub Actions workflow from the template, proved locally from packages; and the M1d rows of "Checks that need the user at the PC", prepared as one checklist ([`m1d-exit/user-checklist.md`](m1d-exit/user-checklist.md)). Decisions "M1d exit's shape (user decisions, 2026-11-24)", "M1d budgets measured at the exit (implementation record; 2026-11-24)" and "The template's CI workflow (implementation record; choices for review; 2026-11-24)".

Code: `134d50e` (M1d step 7) with this record's changes, none of them product code: the template's new `.github/workflows/ci.yml`, the package tests' `Workflow.cs` and `WorkflowTests.cs`, the getting-started guide's section 9, and the runners in `m1d-exit/`.

Machine: the development PC, Windows 11 Pro 10.0.26320 (updates KB5132191 of 17 November, KB5132244 of 20 November, KB5132222 and KB5137003 of 21 November, booted 21 November), 32 logical CPUs, the Balanced power plan, CPU load 2% before the runs, WebView2 Runtime as installed, SDK 11.0.100, Node 24, Playwright 1.64.1 with Chromium build 1248 (installed 5 November); WSL 2 Ubuntu 24.04.5 (kernel 6.18.58.1-microsoft-standard-WSL2), GTK 4.14.5 and WebKitGTK 2.52.6 as installed, the Xvfb that `m1c-host/wsl.sh tools` unpacked. Windows test windows were offscreen and the black box's on its hidden desktop; every `dotnet watch -lp Desktop` window was on a desktop of the run's own (`WatchDesktop.cs`); every Linux window on a private Xvfb with `DISPLAY` and `WAYLAND_DISPLAY` unset and the core limit at 1 byte. No window reached the monitor. The runs were one at a time, except the two OSes' package tests at the end.

## Commands

The runners are [`m1d-exit/win.sh`](m1d-exit/win.sh) (Git Bash) and [`m1d-exit/wsl.sh`](m1d-exit/wsl.sh) (WSL, in `m1c-blackbox/wsl.sh`'s copy of the tree, written `<copy>`), with [`bisect-win.sh`](m1d-exit/bisect-win.sh) for the M1a lines at earlier commits and [`collect.sh`](m1d-exit/collect.sh), which copied the raw files below with the paths, the user's name and the loopback tokens redacted:

```bash
bash docs/evidence/m1d-exit/win.sh budgets budgetsaot m1a notebook noop      # then m1a again, for a second run
bash docs/evidence/m1d-exit/bisect-win.sh d745ea5 ffca7e4 9e675cc 134d50e
MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/docs/evidence/m1d-exit/wsl.sh sync budgets launch budgetsaot notebook m1a noop
MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/docs/evidence/m1d-exit/wsl.sh hotreload buildtests firstedit harness
bash docs/evidence/m1d-exit/win.sh hotreload firstedit harness buildtests
bash docs/evidence/m1d-exit/prepare-user-checks.sh && bash docs/evidence/m1d-exit/win.sh templatefirstedit
dotnet test --project tests/Spar.PackageTests                                   # and wsl.sh's sync client packagetests
```

What each step runs is in the runner's header. The earlier lines are measured as at the M1c exit (`m1c-exit.md`): `BudgetTests` in Release on both test lanes, WindowHost's Production NativeAOT executable through the black-box fixture, 1 warm-up launch and 10 on the same data root, 20 window tests, 200 echoes; the M1a lines by `WireShapes`' `BudgetTests` (`m1a-budgets.md`); the sizes from a Release publish. M1d's two:
- **No-op build:** `dotnet build samples/Notebook` (Debug, the development loop's build) after a first build, 2 warm-up builds, then 10 timed by the shell from start to exit, with the MSBuild server and node reuse as a developer has them (the 6b record's method); a detailed build after them shows `SparClient`, `SparFrontendInstall` and `SparFrontendTypeCheck` skipped as up to date (`SparFrontendBuild` and `SparFrontendPack` do not run in a Debug build with the development server). The isolated case (no server, `-nr:false`) is `Spar.BuildTests`' `notebook-debug-noop`.
- **Hot Reload:** `m1d-hot-reload/watch-run.mjs` on the Notebook under `dotnet watch --non-interactive -lp Loopback` (its page in headless Chromium, `SPAR_LOOPBACK_BROWSER=none`): one session of 5 body edits and their 5 restores, then 3 sessions of one edit and its restore; "applied" is the write to `dotnet watch`'s "C# and Razor changes applied". The first edit of each session is reported apart: it is written as soon as the page lists its first page, about 2 s after `dotnet watch`'s "Waiting for changes". On Windows also a new app from packages (`templatefirstedit`): `dotnet watch` (Desktop profile, on a desktop of its own), the body of `GreetingService` edited as soon as the window reports `ui-ready`.

## The budgets

| Budget | Windows | Linux (WSL) | Met | Reused or measured again |
|---|---|---|---|---|
| No-op build of the Notebook, with the frontend step: ≤ 1.5 s | **1.32 s (1.29–1.35, n = 10)**; isolated 9.54 s (n = 1) | **0.48 s (0.42–1.46, n = 10)**; isolated 2.13 s (n = 1) | Yes, both (warm) | Measured again: the 6b record had 0.695 s on Windows (see the notes) |
| Hot Reload of a method body applied: ≤ 4 s | Notebook, first edit of a session: **4.70 s (1.13–4.86, n = 4)**, over 4 s in 3 of 4; later edits 0.12 s (0.07–0.21, n = 12). New app from packages, first edit: 0.85 s (0.84–0.86, n = 3) | Notebook, first edit: **2.93 s (2.58–3.34, n = 4)**; later edits 0.11 s (0.06–0.20, n = 12) | **Windows: yes on a new app from packages, the budget's measure by the user's decision; the Notebook's first edit over it**; Linux: yes | Measured: step 3 had 1.76 s (Windows) and 2.69 s (Linux) for one first edit each |
| Command test: app start, median: ≤ 2 ms | CoreCLR 0.86 and 0.90 ms (0.64–2.93, n = 19 each); NativeAOT 0.71 and 0.76 ms (0.57–1.24) | CoreCLR 0.44 ms (0.37–1.43, n = 19); NativeAOT 0.29 ms (0.25–0.36) | Yes | Measured again (M1a: 0.20–0.24 / 0.14–0.20 ms Windows, 0.28–0.29 / 0.11–0.12 ms Linux) |
| Browser test: app, context and journey, median: ≤ 250 ms | **247.80 ms (240.01–269.14, n = 9)** and **250.33 ms (244.59–255.88, n = 9)** | 64.88 ms (63.08–72.33, n = 9) | **Windows: at the budget, over it by 0.33 ms in one of two runs**; Linux: yes | Measured again (M1a: 122.5–136.5 ms Windows, 59.8–63.3 ms Linux); the M1a commit measures the same today (below) |
| NativeAOT console harness: ≤ 3.5 MB (3,500,000 B) | 3,259,904 B | 3,430,832 B | Yes | Measured again (M1a: 3.10 MB, Windows); `Spar.BuildTests`' assertion passed on both (7 of 7) |
| Small unary round trip over loopback, median: ≤ 2 ms | 1.20 and 1.30 ms (1.00–3.90, n = 200) | 0.80 ms (0.50–6.80, n = 200) | Yes | Measured again (M1a: 0.70 ms Windows, 0.70–0.90 ms Linux) |
| Notebook NativeAOT executable: ≤ 6 MB / ≤ 8 MB | **5,279,744 B** | **5,186,848 B** | Yes | Measured again (M1b exit 5,237,248 B, M1c exit 5,141,248 B) |
| Launch to `ui-ready`, warm, Release: ≤ 400 ms / ≤ 350 ms | **319.50 ms (300.70–398.40, n = 10)** CoreCLR lane, **302.05 ms (292.60–326.20, n = 10)** NativeAOT lane | **152.35 ms (147.70–183.90)** and **151.40 ms (142.00–165.30)**; the launch alone 145.40 ms (143.00–153.20) | Yes | Measured again (M1b: 359.5 and 375.7 ms; M1c with the portal started first: 146.2 and 153.1 ms) |
| Small unary round trip on the desktop bridge, median: ≤ 1 ms | 0.10 ms, p95 0.20 ms (0.00–2.30, n = 200), mean 0.108 ms; NativeAOT lane 0.10 ms, p95 0.20, mean 0.137 | 0.00 ms, p95 1.00 ms (0.00–5.00, n = 200), mean 0.285 ms; NativeAOT lane mean 0.310 ms | Yes | Measured again |
| App process working set, Windows, NativeAOT: ≤ 60 MB | 45.8 MB (45.5–45.9, n = 10) and 45.9 MB (45.8–46.0); with the browser's processes 373.6 and 374.3 MB | — | Yes | Measured again (M1b: 42.3 MB) |
| Window test: open, journey and close, median: ≤ 700 ms / ≤ 500 ms | 304.34 ms (282.97–1547.58, n = 20), p95 351.32, CoreCLR; 294.76 ms (277.90–738.12), p95 326.12, NativeAOT | 239.46 ms (193.81–926.80, n = 20), p95 248.42, CoreCLR; 229.29 ms (189.12–270.16), p95 244.25, NativeAOT | Yes | Measured again (M1b step 8a: 306 / 298 ms; M1c exit: 189.8 / 189.4 ms) |
| Total memory with the web engine, Linux (PSS): recorded | — | the launch alone 199.2 MB (198.9–201.7, n = 10); inside the class 165.3 and 165.0 MB | Recorded | Measured again (M1c: 200.0 MB) |

Raw: `m1d-exit/windows/` and `m1d-exit/linux/` (`budgets-coreclr.txt`, `budgets-aot.txt`, `budgets-launch-*.txt`, `m1a-*.txt`, `noop.log`, `notebook.log`, `harness.log`, `hotreload.run.log`, `firstedit-*.run.log`, `buildtests-timings.txt`; Windows' `template-firstedit.txt`), and [`m1d-exit/bisect.txt`](m1d-exit/bisect.txt).

### Hot Reload on Windows: the first edit after a session's start

- **Where the time goes** (`firstedit-1.run.log`, `--verbose`): the edit is read 38 ms after the write and the solution updated at 93 ms; then 3.67 s pass before `dotnet watch` prints the Hot Reload capabilities and 0.8 s more before the update is applied, 4.49 s in all ("changes applied in 4486ms"). The restore right after takes 97 ms. In the 5-edit session the first edit came 8.4 s after "Hot reload session started" (the first Chromium start of the run made the page slower to list) and took 1.06 s, with 0.2 s before the capabilities line. So the first edit waits for work `dotnet watch` does once after its start, before its first update; written later, it does not.
- **Not Spar's generator:** Spar's generator takes 0.086 s of the Notebook's compilation (`-p:ReportAnalyzer=true`: 0.089 s for every generator of the Notebook's own compile).
- **A new app from packages does not show it:** its first edit, written as soon as its window reported `ui-ready` (about 1.7 s after "Waiting for changes", as close to the start as the Notebook's), was applied in 0.84–0.86 s. The Notebook is built from Spar's sources in this repository: its `dotnet watch` workspace holds Spar, Spar.Windows, Spar.Linux, Spar.Loopback, Spar.Generator and Spar.Sdk.Tasks as projects, where an app has packages. That the first update waits for those projects' compilations is the likely cause, inferred from the two runs, not traced.
- **Linux** runs the same first edit in 2.58–3.34 s, under the budget with 0.7–1.4 s to spare; step 3 measured 2.69 s.

### The M1a lines on Windows: the environment, not the code

The browser test took 122.5–136.5 ms at M1a (2026-11-16) and 247.8–250.3 ms today; the command test's start 0.20–0.24 ms then and 0.86–0.90 ms now; the loopback round trip 0.70 ms then and 1.20–1.30 ms now. Linux, in WSL on the same PC, is where M1a left it (64.9 ms; 0.44 ms; 0.80 ms). `bisect-win.sh` ran the same tests today at four commits, each extracted by `git archive` into `%TEMP%`, twice each:

| Commit | Browser test | Command start (CoreCLR) | Loopback round trip |
|---|---|---|---|
| `d745ea5` (M1a budgets measured) | 250.06, 244.06 ms | 0.98, 1.04 ms | 1.30, 1.35 ms |
| `ffca7e4` (M1b exit) | 249.36, 245.51 ms | 1.71, 1.79 ms | 1.30, 1.30 ms |
| `9e675cc` (M1c exit) | 252.81, 250.60 ms | 1.55, 1.61 ms | 1.40, 1.40 ms |
| `134d50e` (HEAD) | 253.18, 256.29 ms | 1.79, 1.62 ms | 1.40, 1.20 ms |

The code measured at M1a gives today's numbers, with the same Playwright (1.64.1) and the same Chromium (build 1248, unchanged since 5 November), so the doubling came from the machine after 2026-11-16, not from Spar. The command start is also slower from `%TEMP%` (C:) than from the repository (E:): 1.6–1.8 ms against 0.86–0.90 ms for the same HEAD, since each start creates its data root. Other signs of the same change on Windows: `cmd /c exit` took 50–69 ms, `git status --porcelain` 144–154 ms (the 6b record: 97–126 ms), the isolated no-op build 9.54 s (M1a: 1.63–1.64 s, the same build with its binary log) and the build tests' first build 48.9 s (M1a: 16.8–16.9 s). What changed was not found: Windows updates were installed on 17, 20 and 21 November and Defender's real-time protection is on; nothing was changed to test either (system and security settings).

### Notes on the numbers

- **The no-op build on Windows** doubled since the 6b record (0.695 s, 22 November, the same method): MSBuild's own "Time Elapsed" is 0.82 s, the `git status` of the build identity 241 ms (151 ms in 6b), restore's targets about 0.25 s; the CLI's start makes up the rest. Timed afterwards from PowerShell, 5 builds each: 1.20 s as built, 0.93 s with `-p:SparBuildIdentity=false` (the 6b record: 0.695 against 0.555 s, so the identity's `git` now costs about 0.27 s where it cost 0.14 s) and 1.04 s with `--no-restore`. It is under the budget. Linux: 0.48 s, one outlier of 1.46 s.
- **The Linux window test** is 239.5 / 229.3 ms against 189.8 / 189.4 ms at the M1c exit, under its 500 ms budget; M1d added the start-up styles and the build identity to every page and app, but the cause of the 50 ms was not looked for.
- **The Windows working set** is 45.8 MB, 3.5 MB above the M1b exit, and the Notebook 42,496 B (Windows) and 45,600 B (Linux) larger than at the M1b and M1c exits: M1d's steps each recorded their share (the development server's switch, Hot Reload's handler, the identity).
- **The launch on Windows** is faster than at the M1b exit (319.5 and 302.1 ms against 359.5 and 375.7 ms).

## The template's CI workflow

`src/Spar.Templates/content/spar-app/.github/workflows/ci.yml`, written into every new app (choices in the implementation record): one job, `build-test`, on each runner the app's `--platforms` names (`windows-latest`, `ubuntu-latest`, both by default), `bash` as every step's shell: restore, build, the fast tests (`--report-xunit-trx`), the page tests (`npm test`), the fast tests published with NativeAOT and run (`-result-trx`, failing an empty run), Playwright's headless Chromium and the browser tests, the window and black-box tests on the Windows runner only, the publish, and two uploads (the TRX reports of `*/obj/test-results/`, the published executable without its symbols). `--no-end-to-end-tests` leaves out the four end-to-end steps; `--platforms linux` leaves out the Windows-only ones. The actions are pinned to the commits the repository's own workflow uses.

**Proved locally** (`tests/Spar.PackageTests/WorkflowTests.cs`, 4 tests): the main consumer's workflow has both runners and every step; its actions are pinned to the repository's commits, each with its release in a comment; three new apps (`--no-end-to-end-tests`, `--platforms windows`, `--platforms linux`) have the runners and steps their options ask for, with no template condition left; and the main consumer runs each step's `run` block in order from packages, with the workflow's `env` and `RUNNER_OS`, through bash with GitHub's own `--noprofile --norc -eo pipefail` (Git for Windows' bash on Windows), then finds the TRX reports and the executable the uploads collect. Not run locally: the Linux package step (`sudo apt-get`), the Chromium step (it downloads into the user's folders; the package tests' browser test needs Chromium installed already), the actions themselves, and `CI=true` (with it the black box shows the app's window on this desktop, as on a runner).

**Results** (`Spar.PackageTests`, the whole suite, run now):

| | Windows | Linux (WSL) |
|---|---|---|
| `Spar.PackageTests` | 42 of 42 in 5 min 27 s (38 before this record) | 42: 40 passed, 2 skipped (Windows-only, as before) in 3 min 28 s; 0 processes left |
| `WorkflowTests` | 4 of 4 | 4 of 4 |
| The workflow's steps run from packages, with their times | Restore 7.24 s, Build 7.78 s, Fast tests 1.26 s, Page tests 3.01 s, Fast tests NativeAOT 24.38 s, Browser tests 3.40 s, Window tests 2.78 s, Black-box tests 1.91 s, Publish 5.44 s | Restore 0.86 s, Build 4.77 s, Fast tests 0.71 s, Page tests 0.46 s, Fast tests NativeAOT 16.17 s, Browser tests 1.27 s, Publish 5.31 s (the two Windows-only steps are not in a Linux run) |

The numbers per command are in `m1d-exit/windows/packagetests-timings.txt` and `m1d-exit/linux/packagetests-timings.txt`. The steps ran in the main consumer after the other tests of its collection had built it, so restore and build found most of their work done; on GitHub they start from a clean checkout.

**Not verified:** the workflow on GitHub (it runs only after a push of an app's repository); `pwsh` and `--with-deps` on the Ubuntu runner; the window and black-box tests on GitHub's Windows runner from packages (Spar's own CI runs them there from source); the uploads' globs on GitHub (checked here by the test's own file search).

## M1d's Exit list

| Exit line | Evidence | Run now (observed) |
|---|---|---|
| `dotnet run`, `watch`, `test` and `publish` work in the repo and in the package-only consumer, on Windows and Linux | Repo: step 1's `dotnet run` (`m1d-dev-server.md`), step 2's `-lp Loopback` (`m1d-loopback.md`), step 3's `dotnet watch` (`m1d-hot-reload.md`). Consumer: the guide's runs on both OSes (`m1d-template-package.md`, `m1d-spar-tool.md`), the package tests (`m1d-template-tests.md`, `m1d-runtime-choices.md`) | `dotnet watch` on the Notebook (both OSes, 4 sessions each) and on a new app from packages (Windows, 3 sessions); `dotnet test` (BudgetTests, both lanes, both OSes; the package tests, both OSes); `dotnet publish` of the Notebook and the harness (both OSes) and of the new app (the workflow's step, both OSes). Not run now: `dotnet run` in the repository (cited) |
| A new app made from the template builds, runs, tests and publishes from packages only, and F5 starts it under the debugger in Visual Studio and VS Code | `m1d-template-package.md` (the guide from packages, both OSes), `m1d-spar-tool.md` | The package tests' `NewAppTests`, `TemplateTests` and `WorkflowTests` on both OSes. **F5: open**, the user's checklist, check 3 |
| The template's command, browser and window tests pass on both OSes, and its NativeAOT lane publishes and passes | `m1d-template-tests.md`; on Linux the window and black-box runs need Xvfb, a D-Bus daemon and WebKitGTK (present in WSL) | `TemplateTests` and `WorkflowTests`, both OSes (results above) |
| The M1d budgets are measured and recorded | This file | Both OSes; all met as the user decided to read them (decision "M1d exit's budgets and the CI workflow"): Hot Reload on a new app from packages (0.85 s), the Notebook's 4.70 s first edit on Windows recorded beside it; the browser test at its budget, the machine's slowdown the cause |
| Evidence is recorded for each result | `m1d-dev-server.md`, `m1d-loopback.md`, `m1d-hot-reload.md`, `m1d-template-tests.md`, `m1d-page-startup.md`, `m1d-template-package.md`, `m1d-spar-tool.md`, `m1d-runtime-choices.md`, this file | — |

**Checks left to the user** (the plan's table, prepared in [`m1d-exit/user-checklist.md`](m1d-exit/user-checklist.md) by [`prepare-user-checks.sh`](m1d-exit/prepare-user-checks.sh)): the Notebook's F5 in Visual Studio and in VS Code, a new app's F5 in both, `spar init frontend`'s question in a real terminal, and the start-up splash and `setColorScheme` on an on-screen window. M1d closes when the user reports them.

**The IDE checks (checks 1–3), run by the agent on 2026-11-24 at the user's request** through computer use, clicks only, with the app windows hidden from it: F5, breakpoints, Visual Studio's Hot Reload and contract restart, the Loopback profile and the fast tests in Test Explorer and VS Code's Testing view passed in both IDEs, for the Notebook and a new app from packages; VS Code's Hot Reload applied nothing; the windows' own content was not seen. The results and eight findings are in decision "The IDE checks at the M1d exit"; checks 4 and 5 stay the user's.

## Not verified

- The cause of the slower Windows numbers since 2026-11-16 (the browser test, the command start, the loopback round trip, the no-op builds, process starts); the cause of the Notebook's slow first Hot Reload edit on Windows is inferred, not traced.
- A cold start after a reboot, other machines, a real Linux desktop and GPU; M4 sets the real budgets.
- Hot Reload of a method body in the window rather than on loopback (step 3 measured both; the time measured is `dotnet watch`'s, the same for either host), and a new app's first edit on Linux.
- The template's workflow on GitHub.
- Checks 4 and 5 of the user's checklist, and in checks 1–3 the app windows' content and VS Code's Hot Reload (decision "The IDE checks at the M1d exit").

## Files of this record

| Path | What |
|---|---|
| `m1d-exit/win.sh`, `m1d-exit/wsl.sh` | The runners |
| `m1d-exit/bisect-win.sh`, `m1d-exit/bisect.txt` | The M1a lines at four commits, and their results |
| `m1d-exit/collect.sh` | Copies the raw files with the redactions |
| `m1d-exit/windows/`, `m1d-exit/linux/` | The raw results of each OS |
| `m1d-exit/prepare-user-checks.sh`, `m1d-exit/user-checklist.md` | The user's checklist and what prepares it |
