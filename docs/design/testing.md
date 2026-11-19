# Testing

Part of the Spar design. [`docs/plan.md`](../plan.md) has the goal, the settled decisions, the open questions and the milestones, and lists every design file. Section numbers are global: "section 5" anywhere in the plan, the design files or `docs/decisions.md` means the heading of that number, wherever it lives.

## 5. Testing

**Decided** (user, 2026-11-15; decision "Test strategy: the app runs inside the test process"; evidence `spikes/test-host`). The points still open are listed at the end of this section; each is decided with the user when its slice reaches it.

**The idea.** A test runs the app's own `Program.cs` inside the test process and chooses what sits in front of the backend. The app contains no test code, there is no test configuration file, and replacing a dependency is ordinary DI code in the test:

```csharp
await using var app = await SparTestApp.StartAsync<Program>(o =>
{
    o.Surface = SparTestSurface.Window;   // None | Browser | Window
    o.Services(s => s.Replace<IOrderDatabase>(new FakeOrders(42)));
});
```

**How the test reaches the app** (user, 2026-11-15; decision "Test host mechanics: the generated entry point"; evidence `spikes/entry-accessor`). In the `Test` variant only, Spar's generator makes `Program` public and implements `Spar.ISparEntryPoint` on it, calling the compiler's top-level method (its name read from `Compilation.GetEntryPoint`) through an unsafe accessor. So the test writes `SparTestApp.StartAsync<Program>()`, with no reflection, under CoreCLR and NativeAOT, and Production keeps `Program` internal with no trace of the accessor. A `Program.cs` in another shape (no `return app.Run();`, async, a classic `Main`) fails the `Test` build with a located error. The test sets its session in an `AsyncLocal` and calls the entry point on a thread of its own; `SparApplication.Create` reads the session behind the feature switch, and `Run` applies the test's overrides after the app's registrations and Spar's test defaults, builds the app and hands it to the test instead of opening its window.

**Levels** (pick the smallest that can show the failure):

| # | Level | What runs | Cost (`spikes/test-host`) | Project |
|---|---|---|---|---|
| 1 | Unit | Services called directly | µs | `MyApp.Tests` |
| 2 | Command | Surface `None`: commands through real serialization and dispatch, with typed C# test clients generated per contract | 0.2–0.6 ms per app start | `MyApp.Tests` |
| 3 | Page | The page code under Vitest in Node against `@spar/app/testing`'s fake backend | ms | the frontend (decision "Page tests: @spar/app/testing") |
| 4 | Browser | Surface `Browser`: the page in Chromium over in-process loopback, one browser per test run and a context per test (Playwright, CoreCLR only) | 97–144 ms per test | `MyApp.EndToEndTests` |
| 5 | Window | Surface `Window`: the app's real WebView2 or WebKitGTK window on the test process's UI thread, shown offscreen and never activated, driven through `app.Page` | 298–306 ms per test with an 8-step journey (Windows, M1b step 8a), 297–301 ms (Linux, spike) | `MyApp.EndToEndTests` |
| 6 | Real resources | Any of levels 2, 4 or 5 with the external dependency allowed and a disposable real service (a SQL Server container through Aspire's test builder or Testcontainers) | the container's start | `MyApp.EndToEndTests` |
| 7 | Black box | The exact Production executable, launched, nothing replaced: smoke checks (window, clean close, exit code, files under a disposable data root), and behaviour across processes (single-instance handoff, start-up exit codes, the crash record at the next start), through `SparBlackBox.LaunchAsync(Projects.X)` on a desktop of the test process's own, read and driven through UI Automation (built on Windows in M1b step 8b); real input and focus-dependent checks in Windows Sandbox | 0.4 s to ready, 2–3 s per test (Windows, M1b step 8b) | `MyApp.EndToEndTests` |

**Safe by default** (the test author does nothing for these):
- **One data root per app instance,** from which every Spar folder derives: data, logs, cache, settings, the WebView2 profile or WebKit data, the credential namespace and the single-instance name, including folders the app set itself in `Program.cs`. Data roots sit under `artifacts/` in this repository (WebView2 profiles under `artifacts/webview2-profiles/`, Linux homes under `artifacts/linux-homes/`) and are deleted after each test.
- **Spar's outward services are fakes in a test host:** clipboard, credentials, shell open, dialogs, tray, notifications, power and taskbar. The test can inspect them, and can ask for a real one explicitly.
- **External dependencies:** the app marks its own real resources (a database, a remote API) when it registers them. Resolving one that the test did not replace or allow fails the call, and the message names the dependency and both ways out.
- **No replacements across processes:** a launched executable (level 7) gets only a disposable data root and real disposable resources through configuration.

**Rules for Spar's own code,** which the in-process host depends on:
- Spar keeps no process-wide mutable state; a test lists every static field of Spar's assemblies against a reviewed list.
- Spar never uses `Console`, environment variables or the working directory for per-app values. Each app instance has its own log sink; options are passed explicitly.
- One UI thread per test process on each OS (WebView2 objects belong to the thread that created them; GTK is initialised once), shared by every window the process opens.
- Test windows are shown offscreen with no taskbar button and no activation (`WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` on Windows), and set `IsReputationCheckingRequired = false`, or every early close waits about 6.4 s for SmartScreen.
- The engine's prerequisite check runs before the first window test: a native engine abort ends the whole test process and loses every result of the run (observed with `bwrap` refused).
- The test host and Spar's test fakes sit behind a feature switch, so the trimmer removes them from Production. The production boundary test searches the published binary for them (type names as UTF-8, literals as UTF-16).

**Projects and lanes:**
- **`MyApp.Tests`** holds levels 1 and 2 and runs on every `dotnet test`. **`MyApp.EndToEndTests`** holds levels 4 to 7 and runs as its own CI stage; window tests run as their own invocation there, so a native abort loses only that run, and black-box tests (trait `Surface=BlackBox`) as another, since they take seconds. The page tests (level 3) run in the frontend's own test command.
- **Framework:** `xunit.v3`. **The NativeAOT lane** is the same sources built against `xunit.v3.aot` and published with NativeAOT, without the browser tests (Playwright for .NET cannot run under NativeAOT). Its documented limits apply: no generic test methods, no `params`, no stack traces in failures. An end-to-end project joins the lane for its window tests by leaving its browser files and Playwright out when `SparTestLane` is `Aot` (as `samples/Notebook.EndToEndTests` does); window tests carry the trait `Surface=Window`, so `--filter-trait`/`--filter-not-trait` run them as their own invocation.
- **Test variant:** a test project builds the app it names with `SparTestedApplication` as `SparVariant=Test` whatever its configuration (decision "SparTestedApplication: the test project names its app") (the test-host hook, page automation and the UI-thread stall probe). It replaces the earlier `DesktopTest` and `ManagedTest` (decision "Polished sample apps: a Markdown notebook; the test variant"). Its `app.Run()` is handed the desktop host as in the other variants (since M1b step 8a), so the test session can open the app's window; the page driver itself lives in `Spar.Testing`, which apps never reference, and the host's test-only part sits behind the test-host feature switch.
- xUnit writes no report files, but `dotnet test` (MTP) still leaves an empty `TestResults/` folder in the working directory (`spikes/test-frameworks`, `spikes/d3-variants`; `spikes/TestResults/` is one). The test-project defaults put results under `artifacts/test-results/<project>/` at the repository root (user, 2026-11-15; decision "M1a names"); the mechanism (an MTP results-directory setting from Spar.Sdk or the template) is checked in M1a.
- **Paths:** no absolute paths in tests and scripts.

**Required host tests:** close during controller creation, close during a long RPC or stream, navigate with an RPC in flight, close one of two windows sharing an environment, and repeated open/close. Each ends with all pending work final, stale replies dropped, and the profile reusable. Two more are adopted from the research review (user, 2026-11-15): the end of the OS session, treated as a non-cancellable, best-effort phase, and an app installed under a long path. They run at level 5 on both hosts, plus level 7 where the behaviour spans processes.

Built on Windows in M1b step 8a (`tests/Spar.WindowTests`, `HostTests` and `InstallPathTests`, on the test app `tests/apps/WindowHost`; decision "Window tests on Windows (implementation record; choices for review)"): every one of them except the close during a stream, which waits for streams (M3). The session end sends `WM_QUERYENDSESSION` (answered `TRUE`) and `WM_ENDSESSION`, which the window holds, pumping, until its teardown is done; the long install path is the test executable copied to a folder whose executable path is 255 characters, just under what Windows starts without long-path support. Each window's own close checks that the renderer processes of its page have exited. Since step 8's follow-up (decision "Window tests: closing, session-end budget, test theme and any-character keys"), the session end also runs the page's guard, and a test measures that `WM_ENDSESSION` returns within 4 s with every part slow (4,004 ms).

**Test types** (2026-11-14, revised 2026-11-15 for the test strategy; ⚠ marks a type with an open issue, listed under the table).

| # | Type | Who | What it checks | Runs with | Speed | Spar provides | |
|---|---|---|---|---|---|---|---|
| 1 | C# unit | App, Spar | Services and domain rules, called directly | xUnit | µs–ms | Nothing special; a `Result` reads with `switch` or `Assert` | |
| 2 | Command | App, Spar | A command through real serialization and dispatch, services replaced, no window | The test host, surface `None` | 0.2–0.6 ms per app | The test host, generated typed test clients | |
| 3 | Page | App, Spar | Page code against a fake backend: errors, failures, cancellation, stream events | Vitest in Node (happy-dom for components) | ms | `@spar/app/testing`: `fakeSpar`, scripts per command, typed replies | |
| 4 | Browser | App, Spar | Real backend and page together, Chromium via Playwright over in-process loopback | The test host, surface `Browser` | 97–144 ms | The surface, a shared browser fixture, locators | |
| 5 | Window | App, Spar | The real window (WebView2, WebKitGTK under Xvfb): selectors, input, screenshots | The test host, surface `Window` | 0.3–0.5 s | `app.Page`: locators by role, label and text, trusted input, assertions, evaluation, `WaitForUiReady`, screenshots (built on Windows); scenes, fixed clock | ⚠ D, L |
| 6 | Integration with real resources | App | The app against disposable real services (a SQL Server container) | Levels 2, 4 or 5 plus Aspire's test builder or Testcontainers | 42–48 s per run (`spikes/aspire`) | The external-dependency allowance; values passed as options | ⚠ E |
| 7 | Black box | App, Spar | The exact Production executable: smoke checks and behaviour across processes | `SparBlackBox` (built on Windows); UI Automation, AT-SPI on Linux (M1c) | seconds | The fixture, the generated `Projects` class, the publish beside the tests, the data root through the environment, `app.Window` and `WaitForDialogAsync` over UI Automation | |
| — | Contract compatibility | App, Spar | The committed contract snapshot | The build (SPAR100 in CI) | build time | Built in | |
| — | Visual snapshot | App, Spar | `spar snap` with scenes: PNG and accessibility tree | `spar snap` | seconds | Built in (M2) | ⚠ F |
| 8 | Generator and analyzers | Spar | Exact diagnostics and locations, the analyzer corpus, code fixes, false positives on a sample app | xUnit + Roslyn, SARIF | ms–s | — | |
| 9 | Generated client | Spar | `.d.ts` on TypeScript 6 and 7 at the strictest settings, deliberate mistakes with exact codes, `.js` against `.d.ts` | tsc, Node | s | — | |
| 10 | Serializers and wire | Spar | Differential against System.Text.Json, seeded fuzzing, golden vectors shared by C# and TypeScript, JIT/AOT parity | xUnit, Node | s–min | — | ⚠ G |
| 11 | Runtime and protocol | Spar | Dispatcher, bridge codec, cancellation, `Result`, stream rules | xUnit | ms | — | |
| 12 | Build | Spar | Incremental and no-op builds, variants, the production boundary, a clean clone, `-mt` | MSBuild with binlogs | min | — | ⚠ H |
| 13 | Package | Spar | A consumer using Spar through packages only, isolated caches | `dotnet` in a temp folder | 36–62 s cold restore | — | ⚠ H |
| 14 | Host lifecycle | Spar | Close during startup or a call, navigation with a call in flight, two windows, shutdown, profiles, startup errors, the heartbeat | Levels 5 and 7, both hosts | seconds | — | ⚠ I |
| 15 | Windows Sandbox | Spar | Real input, dialogs, tray, focus-dependent features, the production exe offline | Windows Sandbox | minutes | — | ⚠ J |
| 16 | Performance regression | Spar | BenchmarkDotNet, page benchmarks on three engines, build timing, against baselines | BenchmarkDotNet, runners | minutes | — | ⚠ K |
| 17 | Documentation | Spar | `docs/app-model.md` examples compile and match their copies | `npm run check`, `dotnet build` | s | — | |
| — | Needs the user | Spar | DPI, Narrator, a real camera, Wayland input, real desktops | The user at the PC | — | — | manual |

**Open issues** (to investigate further, with spikes where needed):
- **A. The managed level's form:** answered by the test strategy (2026-11-15).
- **B. Where page tests run:** answered 2026-11-16 (decision "Page tests: @spar/app/testing"): in Node with Vitest; the real engine is level 4. The fake backend speaks the real protocol, so values cross the generated encoders and decoders as with a real host.
- **C. Replacing services in end-to-end tests:** answered by the test strategy (2026-11-15) for levels 2 to 6; level 7 replaces nothing by design.
- **D. Window specifics.**
  - Trusted input in the in-process window (CDP `Input.*` on Windows, XTEST with `GDK_BACKEND=x11` on Linux, decision "Trusted input in Linux desktop tests: XTEST"). Windows answered (user, 2026-11-17; decision "Window-test profiles, trusted input, the page's pickers and the Sandbox runner"; `spikes/window-tests`): CDP input in an offscreen, never-activated window is trusted, gives user activation and focuses the page; a click takes about 15 ms; tests wait two frames after a navigation.
  - Focus-dependent features (clipboard, user activation, the first click on an inactive window) fail in a non-activated offscreen window; they go to Windows Sandbox or the user.
  - Linux window tests need the container image. Whether Windows machines may use WSLg, and whether Spar publishes the image or only its Dockerfile, are open (M1c).
  - Selectors: role and accessible name, label and text, no test ids for now; window tests use Spar's own API with the browser automation library's method names, over a page-script engine shared by both hosts (user, 2026-11-18; decision "Step 8's shape: window tests driven by Spar's own API"). Built on Windows in M1b step 8a: `SparPage`, `SparLocator` and `SparAssertions` in `Spar.Testing`, the engine `Spar.Testing/Window/PageEngine.js` in an isolated world (decision "Window tests on Windows (implementation record; choices for review)"; `docs/app-model.md` 6.12). How scenes are declared is open.
- **E. Integration cost and quirks.** 42–48 s per run. A `localhost` → `::1` hang. Aspire writes keys to `%LOCALAPPDATA%`.
- **F. Visual snapshots** need determinism (clock, fonts, DPI), which is not proven.
- **G. The fuzz budget:** a fixed seed budget on every run or nightly, and whether a minimised corpus is committed.
- **H. Build and package tests** are slow and noisy: 5 s process stalls, cold restores. They need isolation (MSBuild server off, no node reuse, unique compiler id, isolated caches) and medians with ranges.
- **I. Profiles for window tests:** answered (user, 2026-11-17; decision "Window-test profiles, trusted input, the page's pickers and the Sandbox runner"): InPrivate on one shared environment per test process (81 ms per test), each process under its own root; a later run deletes a leftover root only when its `EBWebView\lockfile` is gone. The original question: one shared WebView2 environment per test process with an InPrivate controller per test (76–79 ms per test in `spikes/webview-profiles`) against a fresh user data folder per test (about 470 ms in `spikes/test-host`, the safest); cleanup of folders left by crashed runs. Spike in M1b.
- **J. Windows Sandbox:** fixed at 96 DPI, no network, minutes per run. **Proposal** (user, 2026-11-15: recorded as a proposal, not decided):
  - **Why:** it is the one place for checks that cannot run in the test process or on the developer's desktop: real input and focus (the first click on an inactive window, clipboard actions that need a user gesture, drag and drop); anything that registers itself with the system (tray icons, notifications, deep links, file associations, autostart), which `AGENTS.md` keeps off the developer's machine; and the Production executable on a clean, offline machine (first run, start-up prerequisite failures).
  - **A host-side runner in `Spar.Testing`:**
    - it writes the Sandbox configuration (network, clipboard, printer, camera and microphone off unless a test asks), starts the sandbox, stages the payload, runs it with `wsb exec`, collects the results with a deadline, then stops the sandbox and closes its client windows;
    - it handles what `spikes/windows-sandbox-checks` found: files staged under new names (the guest caches replaced files of a mapped folder), results returned as files (`wsb exec` relays no output), and leftover client windows closed after a stop.
  - **The payload is the app's own tests:**
    - `MyApp.EndToEndTests` published with NativeAOT is one executable that needs no .NET in the guest;
    - window and black-box tests tagged with a Sandbox trait run inside the guest, where real input is allowed;
    - the runner returns the xUnit report with the screenshots and UI Automation dumps.

    Writing a Sandbox test means writing an ordinary window or black-box test and tagging it.
  - **One-off tasks:** run a command with given files in the guest, and get its exit code and output folder back (installer checks, "launch it twice").
  - **Limits, stated in the documentation:**
    - Windows Pro, Enterprise or Education only, with the feature turned on, and probably unavailable on hosted CI runners (not checked). It is a local or self-hosted lane, never part of plain `dotnet test`.
    - The guest is fixed at 96 DPI, so display scaling stays in "Checks that need the user at the PC".
    - A run needs the Sandbox client window, which appears on the host desktop and activates itself, so unattended runs on a machine someone is using follow the same "ask first" rule as other desktop work.
    - Windows only: Linux uses its container image.
  - **Answered (user, 2026-11-17; decision "Window-test profiles, trusted input, the page's pickers and the Sandbox runner"; `spikes/sandbox-runner`):** the runner works and costs seconds per run, not minutes (6.2–9.0 s with a user session, 2.6 s without). Two lanes: SYSTEM for tests without windows, the user session for window and input tests, with "ask first". One sandbox per run, since only one can run at a time; results stream as they arrive. Built in M1b inside `Spar.Testing` for Spar's own checks; the app-facing layer waits for M3.
  - **Sequence:**
    - a spike before M1b: a NativeAOT test executable running and reporting from the guest, the start time, whether two sandboxes can run at once, and whether any CI runner offers it;
    - Spar's own runner in M1b;
    - the app-facing layer (the trait, the fixture, one-off tasks), decided with the user at the start of M3, when tray, notifications and deep links arrive.
- **K. Performance:** which suites run in CI, ratio or absolute thresholds, which machine sets the baselines, and the noise policy. The M1 budgets (Milestones, "Provisional budgets") are the first numbers.
- **L. The test host's mechanics:** answered (2026-11-15): the generated entry point, the `AsyncLocal` session and the spike's names (`SparTestApp`, `SparTestSurface`, `AddExternal`, `AllowExternal`). Still open for M1a: streams and cancellation through the typed test client, and a diagnostic for an app that declares `internal partial class Program` (conflicting accessibility in the `Test` variant).
- **M. The black-box data root:** answered (user, 2026-11-17; decision "M1b's remaining questions without a spike", with its security review): no switch in Production; the fixture launches the app with `LOCALAPPDATA` (Windows) or `XDG_*` (Linux) pointing into its disposable root, and closes a failed relaunch's message through UI Automation. The fixture finds the executable through the generated `Projects.X` class. The original question: the Production binary needs a way to put its data under a disposable root (the spike read `--spar-data-root`). It is an input to a shipped executable, so it needs a security review before it is designed (M1b). The same review settles how a black-box test of a failed relaunch avoids the native message that exits 8 or 9 (decision "The relaunch handler"): a Production build ignores the automated mode.
  Built on Windows in M1b step 8b (decision "The black-box fixture (implementation record; choices for review)"; `spikes/blackbox-desktop`): `LOCALAPPDATA`, `APPDATA`, `TEMP` and `TMP` point into a root under `artifacts/test-host/`, the app runs on a desktop of the test process's own, and a failed relaunch's message is read and answered through `WaitForDialogAsync` (exit 8, both the lock-wait and the not-answering case).

**What Spar provides:**
- **`Spar.Testing`** (NuGet):
  - the test host: `StartAsync` with the surface, service overrides, the external-dependency allowance, `Services`, the per-app log, the folders, and an injectable `TimeProvider`;
  - typed C# test clients generated per contract (RR);
  - the browser surface with a shared-browser fixture and a context per test, in its own package `Spar.Testing.Browser` for end-to-end projects (user, 2026-11-15; decision "Loopback transport accepted; Browser surface in its own package"; the fixture still lives in the test project);
  - the window surface: page evaluation, `WaitForUiReady`, tree and selectors, input, screenshots, scenes and the fixed clock shared with `spar snap` (built on Windows in M1b step 8a, without scenes and the clock: `o.Surface = SparTestSurface.Window` and `app.Page`); closing the window through its close request with `app.Window.CloseAsync`, the test answering in place of every native dialog, light by default (`o.ColorScheme`), and keys for any character (step 8's follow-up; `docs/app-model.md` 6.12);
  - the black-box fixture (built on Windows in M1b step 8b; `docs/app-model.md` 6.13): `SparBlackBox.LaunchAsync(Projects.X, o => …)` with the app's Production NativeAOT executable published beside the tests (`<SparBlackBoxTests>true</SparBlackBoxTests>`, an app path that also works from a publish folder), the data root through the environment, a ready signal (the host's `ui-ready` log line and the window through UI Automation), kill with the parent (a job object) and after a deadline, output capture, the exit code, `app.Window` and `WaitForDialogAsync` over UI Automation, on a desktop of the test process's own;
  - the fixture rules (RR): assertion, capture and teardown failures are reported together, unresolved cleanup is not a pass, an empty test selection fails, and skips are explicit.
- **`@spar/app/testing`** for pages (built for unary calls; decision "Page tests: @spar/app/testing"; `docs/app-model.md` section 6.10):
  - `fakeSpar()`: a fake backend that speaks the real protocol to the page's connection, with a script per command (`succeed`, `fail`, `failWith`, `hang`, `answer` with typed replies, delays through `answer`), abort semantics (a hanging call is confirmed `cancelled` / `stopped`), and a call log read back from the wire (`calls`);
  - a reset of `configureSpar`'s hooks, by `fakeSpar()` itself;
  - stream events and subscriptions, with streams in M3;
  - framework notes for React StrictMode, Angular zones and `OnPush`, Vue scope disposal, and TanStack `retry: false` (not written yet).
- **Template defaults:** the two test projects; results under `obj/`; a `Browser` and a `Window` trait so CI can run them as their own invocations; `CaptureConsole`, with xUnit1051 as an error; `TestContext.Current.CancellationToken`; the NativeAOT lane as a publish of `MyApp.Tests`.
- **Shared infrastructure** (used by both Spar's tests and the app-facing kit):
  - **Profiles:** under the test data root, created with identical options, with a bounded wait for `BrowserProcessExited` before a folder is deleted, and cleanup of folders left by crashed runs only when no process uses them (issue I). Built on Windows (M1b step 8a): one root per test process under `artifacts/webview2-profiles/test-host/`, one environment per set of browser arguments with a default-profile anchor, an InPrivate controller per window under a profile name no other open window has, leftover roots deleted only when their `EBWebView\lockfile` is gone, and the process's own root deleted at exit once its browsers let go.
  - **Process ownership:** engine processes are tracked below the test process (or by job object; the black-box fixture puts each launched app and everything it starts in a job object with kill-on-close, M1b step 8b). A leftover-process assertion runs after each window test. Built on Windows as the window's own close: the renderer processes that served its page (from `GetProcessExtendedInfos`, by its main frame) must have exited within 5 s, or the test's cleanup fails.
  - **Linux isolation:** a private `HOME` and every `XDG_*` folder under `artifacts/linux-homes/<run>/`, `XDG_RUNTIME_DIR` and a D-Bus session; `JSC_*` and inspector variables cleared. The window tests run as a non-root user with user namespaces available (the `bwrap` sandbox).
  - **Waits:** every wait awaits its predicate and has a deadline; a timed-out call is abandoned, never replayed; output is structured (JSON lines) and kept in files.
- **Spar's own tests:**
  - **Generator and analyzers:**
    - exact `(id, located text)` diagnostics for the generator;
    - one analyzer corpus format (`//~ ID` markers, `Kind` headers, an exact set over SARIF), with a fix-and-recompile harness and a false-positive run on a sample app;
    - the TypeScript exact-code negative runner on TypeScript 6 and 7.
  - **Serializers and wire:** a differential test against System.Text.Json, a seeded fuzz lane with JIT/AOT pairs, and shared golden wire vectors read by the C# and TypeScript tests.
  - **`Spar.BuildTests`:**
    - isolation (MSBuild server off, no node reuse, unique compiler id);
    - a step matrix from binlog replay;
    - "authored files untouched";
    - a clean clone;
    - the production-boundary test on the published binary;
    - a `-mt` strict lane.
  - **`Spar.PackageTests`:** the package-consumer recipe.
  - **Host-level runners:**
    - a Windows Sandbox runner;
    - one Linux CI image (non-root uid 1000, `--init`, the `bwrap` settings, Xvfb with an optional window manager);
    - library hiding for the startup-error paths.
  - **Performance:**
    - BenchmarkDotNet with a standard configuration;
    - a cross-engine page benchmark runner (Node, WebView2, WebKitGTK);
    - a build-timing harness with medians and ranges that logs stalls instead of averaging them.
- **Rules:**
  - a check asserts; it never only prints;
  - a missing tool (Node, a browser, Visual Studio) is an explicit skip, never a pass;
  - tests never touch the user's desktop, settings or home folder.
