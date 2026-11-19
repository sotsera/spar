# M1b: window tests on Windows (step 8a)

Recorded 2026-11-18. What it shows: the `Window` surface of plan section 5 (level 5) on Windows, with Spar's own page API (decision "Step 8's shape: window tests driven by Spar's own API"), the required host tests of section 5, the window-test budget, flakiness over repeated runs, and that none of it reaches a Production executable. The implementation record is decision "Window tests on Windows (implementation record; choices for review)". Its follow-up (closing from a test and the page's close guard, the session end within 4 s, light by default, keys for any character) is recorded in [`m1b-window-close.md`](m1b-window-close.md).

## Environment

Windows 11 Pro 10.0.26320 (system theme dark), 32 logical processors, SDK 11.0.100, WebView2 Runtime 157.0.4341.60 (Evergreen), `Microsoft.Web.WebView2` 1.0.4314.35 (loader only), xUnit v3 4.0.3 (`xunit.v3.aot` for the NativeAOT lane). Every window was shown offscreen at (-32000, -32000) with `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` and `SW_SHOWNOACTIVATE`, never activated; all page input went through the window's in-process DevTools session (`Input.*`), never through the OS. Every user-data folder was under `artifacts/webview2-profiles/test-host/<run>-<pid>/` and was deleted at the end of its test process. After the runs, no `msedgewebview2` process had a command line under the repository, and the profiles folder held no root.

- **CoreCLR:** `dotnet test --project tests/Spar.WindowTests -c Debug` and `dotnet test --project samples/Notebook.EndToEndTests -c Debug --filter-trait "Surface=Window"`.
- **NativeAOT:** `dotnet publish <project> -c Release -p:SparTestLane=Aot -o artifacts/aot/<name>`, then the executable (Spar.WindowTests 18,807,296 B; Notebook.EndToEndTests, without its browser journeys, 18,713,088 B).
- **Budget:** `tests/Spar.WindowTests/BudgetTests.cs` records to `artifacts/test-results/Spar.WindowTests/budgets.txt`; copies are in [`m1b-window-tests/`](m1b-window-tests/).

## What was built

| Path | Contents |
|---|---|
| `src/Spar/Testing/SparTestWindow.cs` (new), `SparTestSession.cs`, `Hosting/SparApplication.cs` | `ISparTestWindowHost`, `ISparTestWindow`, `SparTestWindowRequest`, `SparDevToolsException`; the session opens the window through the host `Run` is handed, and closes it before the services are disposed |
| `src/Spar.Generator/SparGenerator.cs` | The Test variant intercepts `app.Run()` too |
| `src/Spar.Windows/AppWindow.cs` (new), `WindowsApp.cs`, `MessagePump.cs` (new) | The main window split from the run; `WM_ENDSESSION` as a non-cancellable phase held up to 10 s |
| `src/Spar.Windows/BrowserView.cs`, `BrowserView.Processes.cs` (new), `BrowserSource.cs` (new), `Interop/WebView2.Processes.cs` (new) | A shared environment and an InPrivate profile for test windows; the page's renderer processes by its main frame |
| `src/Spar.Windows/Testing/` (new) | `TestWindowProcess`, `TestWindow`, `DevToolsCall`, `DevToolsEvents`: the host's test-only part |
| `src/Spar.Testing/Window/` (new), `SparTestApp.cs`, `TestDataRoots.cs` | The page API, its driver and the engine `PageEngine.js` |
| `src/Spar.Sdk/Sdk/Sdk.targets` | The WebView2 loader in Windows test projects |
| `tests/apps/WindowHost/` (new), `tests/Spar.WindowTests/` (new) | The window tests' app and Spar's own window tests |
| `samples/Notebook.EndToEndTests/Window/` (new), its project | Seven window journeys; the NativeAOT lane without the browser journeys |
| `src/Spar.Client/test/pageEngine.test.ts` (new) | The engine's unit tests under happy-dom |
| `tests/Spar.BuildTests/ProductionBoundary.cs`, `tests/static-state/reviewed.txt`, `tests/AppModel/` | New markers; the new static fields; the document's window test, compiled against stand-ins and real Spar |
| `.github/workflows/ci.yml` | Window tests as their own steps, Windows, both lanes |

## Results

### The budget (observed)

A window test here is: start the app with the Window surface and wait for its page's "Ready", an 8-step journey (fill, check, select, click, three assertions, an echo through the backend), and dispose. 20 runs per series, milliseconds.

| Series | Whole test, median (range), p95 | Open | Journey | Close | Controller | Environment, first window / later |
|---|---|---|---|---|---|---|
| CoreCLR, Debug, in the full suite | 306 (282–348), 343 | 130 | 159 | 14.6 | 24.3 | (created earlier) / 2.8 |
| NativeAOT, in the full suite | 298 (281–349), 320 | 127 | 157 | 14.8 | 23.7 | (created earlier) / 2.7 |
| CoreCLR, Debug, alone in its process | 301 (283–895), 346 | 128 | 157 | 14.5 | 23.7 | 427 / 2.8 |
| NativeAOT, alone in its process | 303 (282–653), 325 | 127 | 161 | 14.8 | 24.3 | 228 / 2.6 |

- **Against the budget** (≤ 700 ms median on Windows): 298–306 ms. The first window of a test process pays the prerequisite checks, the environment and its anchor once (228 ms NativeAOT, 427 ms CoreCLR with its JIT), which is the 653 and 895 ms maximum.
- **Close** includes the teardown and the wait for the page's renderer processes to exit (the leftover check).

### The page API, per action (observed; NativeAOT, 100 each, median and p95)

| Action | Median | p95 |
|---|---|---|
| `GetByRole` then count | 0.48 ms | 0.81 ms |
| `GetByLabel` then count | 0.48 ms | 0.79 ms |
| An assertion that already holds | 0.59 ms | 1.22 ms |
| `ClickAsync` (checks with one frame of stability, the trusted press, the landing check) | 33.3 ms | 34.2 ms |
| `FillAsync` | 0.62 ms | 0.97 ms |
| `Keyboard.PressAsync` | 0.44 ms | 0.84 ms |
| `EvaluateAsync("1+1")` | 0.13 ms | 0.19 ms |
| Viewport screenshot (20) | 56.9 ms | 77.7 ms |

CoreCLR Debug was the same within noise: role lookup 0.23 ms, click 33.3 ms, screenshot 47.8 ms.

### Tests (observed)

| Suite | Result |
|---|---|
| `tests/Spar.WindowTests`, CoreCLR | 40 of 40 (22 window tests, 18 that need no window); `--filter-not-trait "Surface=Window"` runs the 18 |
| `tests/Spar.WindowTests`, NativeAOT | 40 of 40 (18,807,296 B executable) |
| `samples/Notebook.EndToEndTests`, window journeys, CoreCLR | 7 of 7 |
| `samples/Notebook.EndToEndTests`, window journeys, NativeAOT | 7 of 7 |
| `samples/Notebook.EndToEndTests`, browser journeys | 7 of 7 (unchanged) |
| `src/Spar.Client` Vitest (the engine's 22 included) | 231 of 231, 13 files; `npm run check` clean on TypeScript 6 and 7 |
| `tests/Spar.Tests`, CoreCLR / NativeAOT | 475 of 477, 2 skipped (Linux only) / 471 of 473, 2 skipped |
| `tests/Spar.Generator.Tests` | 94 of 94 |
| `tests/Spar.Sdk.Tasks.Tests` | 65 of 65 |
| `tests/AppModel.Tests` | 11 of 11 (34 blocks, the window test built against stand-ins and real Spar) |
| `tests/WireShapes.Tests`, CoreCLR / NativeAOT | 79 of 79 / 79 of 79 |
| `tests/WireShapes.EndToEndTests` | 17 of 17 |
| `samples/Notebook.Tests`, CoreCLR / NativeAOT | 12 of 12 / 12 of 12 |
| `samples/Notebook/frontend` page tests | 7 of 7 |
| `tests/Spar.BuildTests` (the production boundary with the new markers) | 6 of 6 |
| `tests/Spar.PackageTests` | 11 of 11 |
| `dotnet run eng/check-size.cs` | 388 files, 0 errors, 0 warnings |
| `dotnet build Spar.slnx`, NativeAOT publishes | 0 warnings, 0 errors |

### Flakiness (observed)

- **The Notebook's window journeys:** 20 runs on CoreCLR and 20 on NativeAOT, 7 journeys each: 280 journeys, 0 failures ([`flakiness-notebook.log`](m1b-window-tests/flakiness-notebook.log)).
- **The window suite** (`tests/Spar.WindowTests`, 22 window tests then: smoke, page API, host tests, the long install path, the budget, each run opening about 70 windows): 20 runs on CoreCLR and 10 on NativeAOT, 660 tests, 0 failures ([`flakiness-window-suite.log`](m1b-window-tests/flakiness-window-suite.log)).
- xUnit runs the test classes of an assembly in parallel, so the window tests of different classes ran at the same time on the one UI thread and browser of their process, each in its own InPrivate profile.

### The required host tests (observed)

| Test | What it checks |
|---|---|
| `Closing_while_the_controller_is_created_ends_cleanly` | A close request posted at the phase "controller requested": the window ends with exit 0, its page never loads (`Opened` fails), the controller that completes afterwards is closed and released, and a window opened next works |
| `Closing_during_a_long_call_cancels_it_and_ends_cleanly` | A call that runs until cancelled: the backend sees it cancelled, nothing is left running, the guard saw `CloseReason.User`, the next window works |
| `Closing_during_a_call_that_ignores_cancellation_waits_for_it` | A call that runs 800 ms whatever happens: the teardown waits for it (the settle limit is 3 s), it finishes, nothing is left running |
| `A_navigation_with_calls_in_flight_cancels_them_and_drops_their_late_replies` | A long call and a slow call, then `GotoAsync("/other.html")`: the long one is cancelled, the slow one finishes after the navigation and its reply reaches no document, the new page's own call works, and the run is clean (no problem logged, no page error, no console error) |
| `Closing_one_of_two_windows_leaves_the_other_working` | Two apps' windows on the shared environment; one closes; the other's calls work |
| `Windows_open_and_close_twenty_times_in_a_row` | 20 windows, each loading, calling its backend and closing clean |
| `The_end_of_the_session_closes_the_window_with_its_reason_and_settles_its_calls` | `WM_QUERYENDSESSION` answers 1; `WM_ENDSESSION` returns only after the controller closed; the guard saw `CloseReason.SessionEnd`; the long call was cancelled; nothing is left running |
| `The_page_renderer_is_found_and_is_gone_once_the_window_closed` | The page's renderer processes are found by its main frame, and have exited when the window's close completes |
| `The_window_works_from_an_install_folder_with_a_long_path` | The test executable copied to a folder whose executable path is 255 characters, running a window test there: passes on both lanes |
| Close during a stream | Not tested: streams arrive in M3 |

A path over 260 characters could not be started at all: `Start-Process` reported "The system cannot find the file specified" for a 299-character path (long paths are off on this machine, the default), and Git Bash refused it ("File name too long").

### The Production boundary (observed)

The Production Notebook (`dotnet publish samples/Notebook -c Release`, NativeAOT) is **5,200,896 B**, against 5,194,240 B for the commit before the step built the same way (+6,656 B; its budget is 6 MB). The map files show where: the session-end phase (`EndSession`, `SessionEndAsync`, `MessagePump`) and the window's split (`AppWindow`'s async stop and its interface). None of the window-test markers is in the image, and each is in the NativeAOT window-test executable ([`size-and-boundary.txt`](m1b-window-tests/size-and-boundary.txt)). One test path was in the image at first (`BrowserView.SharedEnvironmentAsync`, 451 B, reached through a local holding the feature switch); reading the switch in the condition itself removed it (pitfalls).

### Screenshots (observed)

[`window-page.png`](m1b-window-tests/window-page.png) and [`window-conflict.png`](m1b-window-tests/window-conflict.png) are the Notebook's window at 1100 × 720, taken by its journeys; [`window-host.png`](m1b-window-tests/window-host.png) is the window tests' own page. They were dark here only because this machine's theme is; since the follow-up window tests are light by default, and the three were replaced by the follow-up's light ones (2026-11-18, [`m1b-window-close.md`](m1b-window-close.md)).

## Not verified

- The hosted CI runner: whether its session shows WebView2 windows (the workflow's steps run at the first push).
- Windows 10, other WebView2 Runtimes, DPI other than 96, a second monitor's DPI for the offscreen position.
- IME, non-US keyboard layouts; frames, drag and drop, network waits and file choosers in the page API; the engine against the WPT accessible-name suites.
- A native engine abort in a window run (CI runs window tests as their own steps for it).
- Real OS input, focus between windows and the first click on an inactive window (Windows Sandbox, step 8c).
- Whether WebView2 writes under the real `%LOCALAPPDATA%` during window tests (the test process keeps the user's own, since the profiles are passed explicitly): after the runs, the folders changed under it in the last 90 minutes were `Temp` (also written by the builds), another program's folder and `Microsoft\Credentials`, nothing named after WebView2 or Edge; not traced to processes.
