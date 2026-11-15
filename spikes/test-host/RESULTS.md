# Test host spike: results

Recorded 2026-11-15. Question: can an app's one test project run the app's own `Program.cs` inside the test process, replace its dependencies with ordinary DI code, and put each of three surfaces in front of the same backend: no page, a browser over loopback, and the app's real WebView2 or WebKitGTK window? Does that hold under NativeAOT, on Windows and Linux, without leaking processes or touching the user's data? What still needs a separately launched executable?

This is evidence for the test-framework topic (plan section 5). Nothing here was decided when it was recorded; the user adopted the direction the same day (decision "Test strategy: the app runs inside the test process"), and the plan's section 5 now holds it.

**Short answer: yes, on both OSes, under CoreCLR and NativeAOT.**

- **The test calls the app's real entry point.** The top-level statements compile to `Program.<Main>$`; an `[UnsafeAccessor]` with `[UnsafeAccessorType("Program, SampleApp")]` calls it with no reflection, and that works in a NativeAOT test executable too.
- **Overrides are plain DI.** `SparApplication.Create` sees the test's session (an `AsyncLocal`), and `Run` applies the test's service changes after the app's registrations, builds the app and hands it to the test instead of opening a window. The app has no test-specific code.
- **Command-level starts cost 0.2–0.6 ms** (median, warm). Eight apps started together stayed apart: notes, fakes, logs and data roots.
- **The real window works inside the test process.** WebView2 on a test-owned STA thread and WebKitGTK 6.0 on a test-owned GTK thread, on CoreCLR and NativeAOT alike:
  - 50 windows in sequence and 4 at once;
  - nothing leaked: 0 engine processes left after every window test.
- **Cost per window test (open, journey, close):**
  - Windows: 466–472 ms median;
  - Linux under Xvfb: 297–301 ms median, 200 ms of it a fixed delay the spike adds before freeing a handle.
- **Browser level costs about 100–145 ms per test** on one shared Chromium with a context per test. A browser launched per test cost 120–277 ms.
- **The Production executable contains none of it.** With the test-host feature switch off, the trimmer removed the session type and the test fakes (searched in the binary). The same binary passed a black-box smoke run, 5 of 5.

The spike also found three requirements: Spar-owned fakes must sit behind the feature switch, test windows need `IsReputationCheckingRequired = false`, and a native engine failure kills the whole test process.

## Environment

| Item | Value |
|---|---|
| Windows | Windows 11 Pro 10.0.26320, 32 logical CPUs. WebView2 Runtime 157.0.4341.46. .NET SDK 11.0.100. Playwright 1.64.1, `chromium_headless_shell-1248`. |
| Linux | Docker Desktop 29.9.3 (WSL2 kernel 6.18.58.1). Image `spar-test-host:webkit6` (6.51 GB), built from `linux/Dockerfile`: the Playwright image `mcr.microsoft.com/playwright/dotnet:v1.64.1-noble` (Ubuntu 24.04.5, 5.06 GB, already on the machine) plus `libwebkitgtk-6.0-4` 2.52.6, GTK 4.14.5, `clang`, `zlib1g-dev` and SDK 11.0.100 from `dot.net/v1/dotnet-install.sh`. Xvfb `:99` (X11, no GPU, Mesa llvmpipe). Run as uid 1000 with `--security-opt seccomp=unconfined --init --shm-size=1g`. |
| Packages | `xunit.v3` 4.0.3 (CoreCLR lane), `xunit.v3.aot` 4.0.3 (NativeAOT lane), `Microsoft.Extensions.DependencyInjection` 11.0.0, `Microsoft.Playwright` 1.64.1, `Microsoft.Web.WebView2` 1.0.4314.35 (loader only). |
| Build policy | The repository's: warnings as errors, code style enforced, trim and AOT analyzers on in every project. All four publishes and all builds had 0 warnings; no suppressions. |

## What was built

| Part | Role |
|---|---|
| `Spar.Mini/` | A stand-in for the Spar runtime, written for this spike. <ul><li>`SparApplication.Create(args, spar => …)` and `app.Run()`, in the decided `Program.cs` shape;</li><li>folders under one data root;</li><li>`AddExternal<TService, TImpl>()`;</li><li>one Spar-owned outward service (`IClipboard`);</li><li>a per-app log;</li><li>a bridge with a JSON header, a TAB and the payload;</li><li>the test-session hook (`SparTestSession`, behind the feature switch `Spar.TestHost.IsSupported`).</li></ul> Window hosts: `Windows/` (Win32 window, WebView2 controller, `https://app.localhost`, web messages) and `Linux/` (GTK4 window, WebKitGTK 6.0 view with its own web context and network session, `spar://app`, script messages answered with a reply). The COM and GLib declarations are linked from `spikes/react/React.Desktop/Interop/` and `spikes/linux-desktop/Linux.Desktop/Native.cs`, plus `Windows/Settings8.cs` and `Linux/NativeMore.cs`. |
| `Spar.Mini.Testing/` | A stand-in for Spar's test library. `SparTestApp.StartAsync(entryPoint, options => …)` runs the entry point on a thread of its own and returns the built app: its `Services`, `Log`, `Folders` and `SendAsync` (one bridge message). Options: `Services(…)` overrides, `AllowExternal<T>()`, and a `Surface`: `None`, `Loopback` (Kestrel on 127.0.0.1 with a per-app token) or `Window`. `TestDataRoots` puts each app under `artifacts/test-host/<run>/app-NNN/`, its WebView2 profile under `artifacts/webview2-profiles/test-host/<run>/` and its WebKit data under `artifacts/linux-homes/test-host-<run>/`. Each is deleted after its test. |
| `SampleApp/` | The app: `Program.cs` in the decided shape. It has notes (an in-memory store, `TimeProvider`, `IClipboard`) and an order count through `IOrderDatabase`, registered with `AddExternal`. A plain page (`wwwroot/`, embedded) with one bridge client over whichever transport the host offers. `SampleContracts.cs` stands in for the generated dispatch. `OutputType` `WinExe`, `PublishAot`, the feature switch off. |
| `SampleApp.Tests/` | The one test project (`xunit.v3`, references the app). <ul><li>`AppEntry.cs`: the unsafe accessor;</li><li>`NotesClient.cs`: a stand-in for a generated typed test client, writing the same message a page writes;</li><li>tests: `CommandTests`, `GuardTests`, `ProcessStateTests`, `BrowserTests` (Playwright, one browser for the run), `WindowTests`;</li><li>`EngineProcesses.cs`: finds engine processes below the test process (Toolhelp on Windows, `/proc` on Linux).</li></ul> |
| `SampleApp.AotTests/` | The NativeAOT lane: the same source files, linked unchanged except `BrowserTests.cs`, against `xunit.v3.aot` with `XUNIT_AOT`. Published with `PublishAot`. |
| `boundary.mjs` | Searches the published app for test-host types and strings (UTF-8 names, UTF-16 literals), with controls. |
| `blackbox.ps1` | Starts the exact Production executable minimized, waits for its window, closes it with `CloseMainWindow`, and checks the exit code and that no engine child is left. |
| `linux/` | `Dockerfile`, `run.sh` (the Linux lane: copies the sources without `bin/` and `obj/`, sets `HOME` and `XDG_*` under `artifacts/linux-homes/<run>/`, starts Xvfb, builds, runs both lanes twice), `one.sh` (one test, for diagnosis). |

Spike-only switches (environment variables, all off by default): `SPIKE_REPUTATION=1` keeps SmartScreen on, `SPIKE_RELEASE` picks how COM wrappers are released (`final` by default, `none`, `gc`, `final+gc`), `SPIKE_EXSTYLE=0` gives test windows a taskbar button.

```sh
# Windows (Git Bash, in spikes/test-host)
dotnet build SampleApp.Tests && SampleApp.Tests/bin/Debug/net11.0/win-x64/SampleApp.Tests.exe -showLiveOutput
dotnet publish SampleApp.AotTests -c Release && SampleApp.AotTests/bin/Release/net11.0/win-x64/publish/SampleApp.AotTests.exe -showLiveOutput
dotnet publish SampleApp -c Release -o ../../artifacts/test-host/publish/production
dotnet publish SampleApp -c Release -p:SparTestHostSupported=true -o ../../artifacts/test-host/publish/switch-on
node boundary.mjs ../../artifacts/test-host/publish/production/SampleApp.exe ../../artifacts/test-host/publish/switch-on/SampleApp.exe
pwsh -File blackbox.ps1 -Runs 5

# Linux (from the repository root)
docker build -t spar-test-host:webkit6 spikes/test-host/linux
MSYS_NO_PATHCONV=1 docker run --rm --init --shm-size=1g --security-opt seccomp=unconfined --user 1000:1000 \
  -v "<repo>:/src:ro" -v "<repo>/spikes/test-host/results:/out" -v spar-test-host-nuget:/nuget \
  spar-test-host:webkit6 bash /src/spikes/test-host/linux/run.sh 2
```

## Results (observed)

Every lane ran twice on the final sources; raw output is in `results/`. Times are wall-clock, measured inside the test; "median" is over the runs listed.

| Lane | Tests | Run 1 | Run 2 |
|---|---|---|---|
| Windows, CoreCLR (`xunit.v3`) | 19 | 19 passed, 30.1 s | 19 passed, 29.7 s |
| Windows, NativeAOT (`xunit.v3.aot`, exe 22,574,592 B, publish 17 s) | 15 | 15 passed, 26.2 s | 15 passed, 25.8 s |
| Linux, CoreCLR | 19 | 19 passed, 18.8 s | 19 passed, 18.3 s |
| Linux, NativeAOT (exe 21,709,864 B, publish 20.9 s) | 15 | 15 passed, 16.2 s | 15 passed, 16.0 s |

The NativeAOT lane has four fewer tests: the three browser tests and the reflection scan of static fields.

### 1. Calling the app's entry point

- `AppEntry.Run(args)` calls `Program.<Main>$` through `[UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "<Main>$")]` with the target type named by `[UnsafeAccessorType("Program, SampleApp")]`. It worked in all four lanes. Under NativeAOT the accessor also roots the method: no `DynamicDependency` and no warning.
- **One build error on the way:** a public static `Main(string[])` in the test project is a second entry point (CS0017, beside xUnit's generated one). Any other name works.
- **Observed limit:** `<Main>$` is the compiler's name for the synthesized method, not a language guarantee. Inference, not tested: a Spar generator can read the real metadata name from `Compilation.GetEntryPoint` and emit the accessor, so a compiler change would fail the build rather than the tests.

### 2. Command level (no page)

| | Windows CoreCLR | Windows AOT | Linux CoreCLR | Linux AOT |
|---|---|---|---|---|
| First start + call + dispose | 29–31 ms | 0.4 ms | 0.8–1.0 ms | 1.0–1.5 ms |
| Next 19, median (max) | 0.52–0.57 (0.99) ms | 0.37 (0.61) ms | 0.38–0.43 (0.72) ms | 0.19–0.24 (0.35) ms |
| 8 apps started at once, all ready | 2.4–4.9 ms | 1.6–4.3 ms | 1.9–2.2 ms | 1.0–1.3 ms |

- A start is: create the session, run `Program.cs` on a new thread, build the provider (with `ValidateOnBuild`), and return. The first Windows CoreCLR start includes JIT.
- **The overrides took effect:** `TimeProvider` was replaced by a fixed clock (the note's `CreatedAt` matched) and `IOrderDatabase` by a fake (counts 42, or 1..8 across 8 parallel apps).
- **The typed client covers the real path:** the client serializes with the app's own `JsonSerializerContext` and sends the same bridge message a page sends, so declared errors (`empty`) and failures (`unknown-command`, `external-dependency`) went through real dispatch and serialization.
- **Eight apps at once stayed apart:** each saw only its own notes and its own fake count, had exactly one session marker in its own log, and had its own data root.

### 3. Guards against real resources and user data

| Check | Result (all lanes) |
|---|---|
| `IOrderDatabase` registered with `AddExternal`, not replaced | The call failed with `backend` / `external-dependency`. The log holds `SparExternalDependencyException` with "IOrderDatabase is an external dependency … Replace it in the test's services, or allow the real one with options.AllowExternal<IOrderDatabase>()". The real class was never constructed. |
| `options.AllowExternal<IOrderDatabase>()` | The real class was constructed once and ran (it throws "would query the production order database", by design). |
| Spar-owned outward service (`IClipboard`) | The test host's default is `FakeClipboard`. Copying a note recorded `["secret"]` in it; `SystemClipboard` was never constructed. |
| Folders | Data, logs and cache are under `artifacts/test-host/<run>/app-NNN/`, also the logs folder that `Program.cs` set to `AppContext.BaseDirectory/logs`. The engine data is under `artifacts/webview2-profiles/…` (Windows) or `artifacts/linux-homes/…` (Linux). Each app's folders were deleted after its test: 0 files left under `artifacts/webview2-profiles/test-host/`. |

### 4. Process-wide state

- **Two apps, one with a failing external dependency, wrote nothing to stdout or stderr.** The working directory and the whole environment block were unchanged. Checked with `Console.SetOut`/`SetError` captured, in a non-parallel collection.
- **The static fields of the runtime stand-in** (reflection scan, CoreCLR lanes) are only the reviewed ones:
  - the `AsyncLocal` session;
  - the feature-switch value;
  - the test process's one UI thread per OS (`UiThread.SharedThread`, `GtkThread.SharedThread`);
  - the registered window class;
  - the source-generated JSON metadata;
  - one spike counter.
- **What a process shares and the design must keep in mind:**
  - one UI thread per OS for all windows of a test process;
  - GTK initialised once;
  - the Win32 window class;
  - `Console`, environment variables and the working directory, which Spar must never use for per-app values;
  - `Environment.Exit` or a crash in app code, which ends every test in the process.

### 5. Browser level (Chromium over in-process loopback)

| | Windows (CoreCLR) | Linux (CoreCLR) |
|---|---|---|
| Launch Chromium once (assembly fixture) | 352–359 ms | 239–307 ms |
| App + new context + journey, shared browser: first, then median of 9 | 157 ms, 140–144 ms | 105–106 ms, 97 ms |
| App + new browser + journey, median of 5 | 230–277 ms | 120–121 ms |

The journey: open the page, wait for `window.sparReady`, fill the note, click Add, wait for the list item. Afterwards the test read the backend's store directly. A browser per test cost 86–137 ms more per test on Windows and about 25 ms more on Linux, much less than the "~1 s" in the plan's notes (that was a whole Playwright start, not a second launch).

### 6. The real window inside the test process

| | Windows CoreCLR | Windows AOT | Linux CoreCLR | Linux AOT |
|---|---|---|---|---|
| One window: open, page ready, journey done | 262–266, 264–268, 278–282 ms | 264–274, 266–277, 279–290 ms | 85, 86, 98 ms | 82–84, 83–85, 94–96 ms |
| One window: close until every engine process is gone | 475–486 ms after start (dispose 172–176 ms) | 467–483 ms (159–180 ms) | 304–306 ms (201–202 ms) | 296–298 ms (201 ms) |
| 50 in sequence: ready, median (max) | 275 (572–638) ms | 274–277 (511–539) ms | 84–86 (99–101) ms | 85–88 (92–99) ms |
| 50 in sequence: open + journey + close, median (max) | 466–468 (771–830) ms | 467–472 (714–737) ms | 297–299 (313–314) ms | 298–301 (308–312) ms |
| 4 at once: all open, all journeys done | 433–667, 452–683 ms | 451–459, 468–485 ms | 147–151, 172–176 ms | 158–160, 171–184 ms |
| Engine processes left after the window tests | 0 | 0 | 0 | 0 |
| Working set after window 1 / 10 / 25 / 50 | 112 / 117–119 / 119–121 / 119–124 MB | 36 / 38 / 42 / 48 MB | 409–468 / 408–457 / 409–459 / 411–463 MB | 302–304 / 306 / 308–309 / 312 MB |

- **Windows:**
  - Each window is a Win32 window with its own WebView2 environment and user data folder, so each test gets a fresh profile.
  - It is shown offscreen at (−30000, −30000) with `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`: no taskbar button, never activated.
  - Six engine processes per window, 24 for four.
  - The page is driven by `Runtime.evaluate` through the in-process `CallDevToolsProtocolMethod`, with synthetic DOM events, not trusted input.
- **Linux:**
  - Each window has its own `WebKitWebContext` and `WebKitNetworkSession` with data under its own folder.
  - 2 WebKit processes for one window, 5 for four.
  - The page is driven by `webkit_web_view_call_async_javascript_function`.
  - GTK runs on a thread the test library starts, not the process's main thread, and every window shares it.
- **Memory over 50 windows:**
  - Windows grew about 0.25 MB per window (36 to 48 MB AOT, 112 to 124 MB CoreCLR);
  - Linux AOT grew about 0.2 MB per window (302–304 to 312 MB);
  - Linux CoreCLR stayed flat (409 to 411 MB, and 468 to 463 MB).

  The growth was not investigated.
- **Close time on Windows is the browser's exit** (dispose ≈ 15 ms teardown + about 150 ms browser exit). Linux closes in about 1 ms, but the spike waits 200 ms before freeing the window's handle, in case a late callback names it; that wait is the whole Linux dispose figure and not a measured need.

**Problems met and fixed on the way:**

| Problem | Cause and evidence | Fix |
|---|---|---|
| Closing a Windows test window took 6.47–6.50 s (6 of 6 runs) | SmartScreen, already in `docs/pitfalls.md` from `spikes/webview-profiles`: a close soon after a navigation waits for the reputation request. Releasing the COM wrappers early (`ComObject.FinalRelease`), a forced GC, or both changed nothing (6.47–6.50 s in each variant). Neither did the window styles (closed 6.85–6.92 s after the test started, with and without them). | `IsReputationCheckingRequired = false` (the decided default): the browser exited 144–161 ms after teardown. |
| The page's script never ran on Linux (`window.sparReady` undefined) | `fetch('app.js')` showed status 200 and no `Content-Type`, so `nosniff` blocked it. `webkit_uri_scheme_response_set_http_headers` replaces the content type set before it. Already in `docs/pitfalls.md` (`csp-navigation`). | Put `Content-Type` in the header set. |
| The whole Linux test run aborted at the first window | As root, `bwrap` could not create its namespace; WebKit ended the process with a GLib `ERROR` (core dump), and every test result was lost. The root cause is a known pitfall. Losing the whole run is new. | Run as uid 1000 with `seccomp=unconfined`. |

### 7. The NativeAOT lane

- **The same test sources published with `xunit.v3.aot` with 0 warnings** in 17 s (Windows) and 20.9 s (Linux). That covers the in-process host, the overrides, the unsafe accessor, generated COM interfaces, GLib callbacks and both window hosts.
- **The AOT executables hosted real WebView2 and WebKitGTK windows** with the results in section 6.
- **Left out:** Playwright cannot run under NativeAOT (`spikes/test-frameworks`), and the reflection scan of static fields is CoreCLR-only (`[RequiresUnreferencedCode]` on that test, `#if !XUNIT_AOT`). The documented `xunit.v3.aot` limits were not reached by these tests: no generic tests, no `params`, and no stack traces in AOT failures.

### 8. Production boundary and the black-box run

`boundary.mjs` on the NativeAOT Production publish (`results/boundary.txt`):

| Text in `SampleApp.exe` (3,515,904 B) | Production | Switch-on build (control) |
|---|---|---|
| `SparTestSession` | absent | present |
| `FakeClipboard`, `MemorySparLog`, `SparExternalGuard` | absent | — |
| `SystemClipboard`, the literal "No contracts were registered" (controls) | present | — |

- **The first publish still contained `FakeClipboard`.** The fakes were registered under an ordinary `bool testHost` parameter, which the trimmer cannot remove. Moving their registration into the session, behind the feature switch, removed them. New pitfall in `docs/pitfalls.md`.
- **In the switch-on build the session's own marker string was absent too.** The app alone never creates a session, so NativeAOT drops the session's instance methods even with the switch on. The type name is the meaningful signal.

`blackbox.ps1` on the same executable, nothing replaced (`results/blackbox-windows.txt`):
- **5 of 5 runs passed:**
  - window found 32–146 ms after start, title "Notes";
  - exit code 0;
  - 163–176 ms from `CloseMainWindow` to exit;
  - no engine child left.
- **The data went where the test said:** `--spar-data-root artifacts/webview2-profiles/test-host-blackbox` (a switch the spike's Production host reads), removed afterwards.
- **An earlier run of 3 hit the console-window pitfall:** the sample was still `OutputType` `Exe`, `CloseMainWindow` closed the console instead, and the app ended with `0xC000013A`. Fixed with `WinExe`; new pitfall.

## What still needs a separately launched executable

Observed or inferred from the above, for the design discussion:

1. **The exact Production binary, black-box:** nothing replaced, the switch off, the hook trimmed away. It can only check what is visible from outside: the window appears, closes cleanly, the exit code, and its files under a disposable data root. That needs a way to move the data root that the Production binary honours (the spike used `--spar-data-root`; not designed). Driving its page needs OS automation (UI Automation, AT-SPI), not tried here.
2. **Process lifetime** (inferred, not tested here): single-instance handoff between two processes, start-up exit codes 3/4/5/7, the crash record read at the next start, OS session end.
3. **Isolation from native crashes** (observed once, by accident): an engine abort inside the test process loses every result of that run. Running the window tests as their own test invocation (a filter or a separate run) limits the loss. Checking the engine's prerequisites before the first window gives a clear message instead.

The in-process window did everything else a launched executable was planned for in section 5: the real engine, the real bridge, the app's real composition, and many windows, sequential and parallel, under CoreCLR and NativeAOT.

## Findings for the design

These are observations and proposals for the test-framework discussion; none is decided.

1. **Proposal: run the app's `Program.cs` in the test process.** No instrumented executable and no test configuration file. Test overrides are ordinary DI code, and fakes are objects the test can inspect. Evidence: sections 1–4.
2. **Proposal: one test-app API with three surfaces** (none, loopback, window) in front of the same in-process backend. Evidence: sections 2, 5, 6.
3. **The NativeAOT lane needs no second executable:** the same test sources with `xunit.v3.aot`, the browser tests left out. Evidence: section 7.
4. **Requirements the spike found for Spar:**
   - one data root per app instance, from which every folder derives, the app's own overrides included;
   - a per-app log that never uses `Console`;
   - no process-wide mutable state, enforced by a test;
   - Spar's outward services default to fakes in a test host, registered behind the feature switch;
   - a marker for external dependencies whose unreplaced use fails with its name;
   - test windows offscreen, not activated, without a taskbar button;
   - `IsReputationCheckingRequired = false`, or every early close costs 6.4 s;
   - one UI thread per test process on each OS;
   - the engine prerequisite check before the first window.
5. **Open points the spike leaves:**
   - how the generator emits the entry-point accessor, and in which variant;
   - whether the hook is an `AsyncLocal` (as here) or compile-time interception;
   - a data-root switch for black-box runs of the Production binary, which needs a security review;
   - whether window tests share one WebView2 environment with an InPrivate controller per test (76–79 ms per test in `spikes/webview-profiles`, not rebuilt here) instead of a fresh user data folder per test (about 470 ms here).

## Not tested

- **Not exercised here:**
  - trusted input (CDP `Input.*`, XTEST) in an in-process window: the journeys used synthetic DOM events;
  - focus and keyboard behaviour of the offscreen, non-activated test windows.
- **Not covered by the stand-in:**
  - streams, subscriptions and cancellation through the test client;
  - the generated typed client (the spike's client is hand-written);
  - the Development variant, the dev server and Hot Reload.
- **Not run on Linux:** a black-box run of the Production binary; the boundary check (the binary was not searched).
- **Environments not covered:**
  - a real Linux desktop (Wayland, WSLg, a GPU): only Xvfb with X11 and llvmpipe;
  - Windows Sandbox or a CI runner: only this development machine, with the windows offscreen on the user's desktop. No input was injected and no window was activated.
- **Not measured:**
  - several test processes running at once;
  - test runs longer than 50 windows;
  - the cause of the 0.2–0.25 MB per window growth (Windows, Linux AOT).
- **Not reproduced:** the shared WebView2 environment with InPrivate controllers.
