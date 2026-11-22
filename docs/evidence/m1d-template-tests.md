# M1d step 4: tests in the template (evidence)

Recorded 2026-11-22. Plan M1d, build step 4; decision "M1d step 4's shape: tests in the template" (user decisions,
2026-11-22) and the implementation record "Tests in the template (implementation record; choices for review)". Design:
`docs/design/testing.md`, section 5, "Template defaults" and "Projects and lanes".

## Setup

- Windows 11 Pro 10.0.26320, SDK 11.0.100, Node 24.21.0, xunit.v3 and xunit.v3.aot 4.0.3 (xunit.analyzers 2.3.0),
  Playwright 1.64.1 with its headless Chromium (build 1248).
- Linux: WSL2 Ubuntu-24.04 (kernel 6.18.58.1), the same SDK, Node 24.22.3, WebKitGTK 6.0 from the system, the unpacked
  Xvfb of `docs/evidence/m1c-host/wsl.sh`; the copy `docs/evidence/m1c-blackbox/wsl.sh sync` keeps in
  `/var/tmp/spar-m1c-blackbox/repo` (written `<copy>`), `DISPLAY` and `WAYLAND_DISPLAY` unset (nothing on WSLg's
  display), the core limit 1 byte. The script's new step `packagetests` runs `Spar.PackageTests` there, with the source's
  `.git` copied in (the fixture compares `git status` before and after packing).
- **The template's content:** `src/Spar.Templates/content/spar-app/` (`MyApp/`, `MyApp.Tests/`, `MyApp.EndToEndTests/`).
  `Spar.PackageTests` copies it into its main consumer, outside the repository under the system's temporary folder,
  with `MyApp` replaced by `Notes` and `myapp` by `notes` in every path and file, and nothing else changed.
- **Data roots:** the consumer's test hosts get `SPAR_TEST_ARTIFACTS=<repo>/artifacts` (on Linux `<copy>/artifacts`), so
  their data roots, WebView2 profiles and Linux homes are under the repository's `artifacts/`. Afterwards on Windows:
  `artifacts/test-host/` and `artifacts/webview2-profiles/test-host/` held no root of these runs, no `msedgewebview2`,
  `Notes` or `Notes.Tests` process was left, and the user's `%LOCALAPPDATA%` had no `com.example.Notes*` entry (only the
  earlier `com.example.Notebook.Development`).
- Paths in the timing files are written `<repo>`, `<copy>`, `%TEMP%` and `<node>`.

## Results

### The package tests: the template from packages

`dotnet test --project tests/Spar.PackageTests` (after `npm run build` in `src/Spar.Client`): **18 of 18 on Windows in
3 min 1 s, 18 of 18 on Linux in 4 min 35 s, none skipped on either.** Every command and its time:
[`timings-windows.txt`](m1d-template-tests/timings-windows.txt), [`timings-linux.txt`](m1d-template-tests/timings-linux.txt).

| Run in the consumer | Windows | Linux |
|---|---|---|
| `dotnet test --project Notes.Tests` (the first build: restore, npm ci, client, type-check, Vite, 2 command tests) | 14.2 s | 97.3 s (the WSL npm cache had none of the template's packages) |
| `dotnet publish Notes.Tests -c Release -p:SparTestLane=Aot`, then the executable: `Total: 2, Errors: 0, Failed: 0, Skipped: 0` | 29.6 s + 0.11 s | 15.9 s + 0.01 s |
| `npm test` in `Notes/frontend` (Vitest: 3 page tests) | 1.5 s | 0.4 s |
| `dotnet test --project Notes.EndToEndTests --filter-class …UntaggedWindowTests` (exit 2, "failed: 1", the trait message) | 2.4 s | 104.1 s (the end-to-end project's first build: Playwright's restore and the app's NativeAOT publish) |
| `--filter-trait Surface=Window` (1 window test: fill, click, `Hello, Ada!`, no problems or page errors) | 19.4 s (the first end-to-end build) | 2.1 s |
| `--filter-trait Surface=Browser` (1 browser test in Chromium) | 7.2 s | 2.2 s |
| `--filter-trait Surface=BlackBox` (1 black-box test: heading, Invoke on Greet, `Type a name first.`, exit 0, tree gone) | 5.4 s | 2.3 s |
| an unchanged `dotnet build Notes.EndToEndTests`: no `Generating native code`, the executable beside the tests untouched | 1.6 s | 1.5 s |

The rest of the 18 are the earlier package tests, now on the template's app: the analyzer's SPAR001 at `Broken.cs(6,14`,
SPAR120–SPAR122 and the `Aliases` reference, SPAR130 and five invalid app ids, the design-time build, the app's NativeAOT
publication without its host (exit 3), the update and downgrade under a warm MSBuild server, the central-package-management
consumer (both lanes' restores) and the library consumer, all as before. The template's committed lockfile is unchanged
by the build (compared after the same renaming), and its Playwright version is the repository's (a test checks it).

- **Results under `obj/`:** after the fast tests, `Notes.Tests/obj/test-results/` exists and no `TestResults/` folder is in
  the consumer's folder or any project folder (checked again after the browser run).
- **The NativeAOT lane from packages** works for the template's project as written: no change to the SDK was needed.
- **Linux:** the window and black-box invocations ran (not skipped): Xvfb, `dbus-daemon` and `libwebkitgtk-6.0.so.4`
  were found, and the test host and the black box started their own private displays. `census` found 0 processes left.
  Left on disk in the copy, as by every Linux run before this step: the window test's test-host root under
  `<copy>/artifacts/test-host/` and its `/dev/shm/spar-test-*` folder, which the next run deletes (plan section 5,
  "Linux isolation").

### Earlier string-built consumer, for comparison

The last recorded run of the string-built consumer was 12 of 12 in 1 min 36 s (M1b step 8b, `docs/evidence/m1b-black-box.md`,
with its end-to-end and black-box projects). The template's consumer adds about 67 s of runs on Windows: the window
invocation with the end-to-end project's first build (19.4 s, of which the app's NativeAOT publish for the black box),
the fast tests' NativeAOT publish (29.6 s), the browser, black-box and trait runs (15 s) and `npm test` (1.5 s). It was
not measured against the old consumer on the same machine state.

### The trait check

- `tests/WireShapes.Tests/SurfaceTests.cs` (both lanes, so the check runs in the NativeAOT lane too): a window test with no
  trait fails with "This test starts the Window surface but has no [Trait("Surface", "Window")]. …"; a browser test with
  another `Surface` value fails naming the browser trait; a test without a surface needs none; the trait on the class
  counts (the start then fails on the missing package, past the check); the existing package test carries the trait.
- **The assembly level and CaptureConsole, both lanes:** the probe [`trait-probe/`](m1d-template-tests/trait-probe/)
  (`[assembly: Trait("Surface", "Browser")]`, `[assembly: CaptureConsole]`) on WireShapes: 3 of 3 on CoreCLR and 3 of 3
  published with `-p:SparTestLane=Aot` (`Total: 3, Errors: 0, Failed: 0, Skipped: 0`); the assembly's trait passed the
  check in both, and the console line the test wrote was in each run's TRX report (`captured-console-line-42`, 1 match each).
- **Spar's own tests that lacked the trait:** the browser classes `WireShapes.EndToEndTests.BrowserBridgeTests` and
  `BudgetTests` and `Notebook.EndToEndTests.JourneyTests` got `[Trait("Surface", "Browser")]`, and `SurfaceTests`' browser
  test its own. Every window test already had its trait. CI's fast invocation filters out only `Window` and `BlackBox`, so
  the browser tests still run there, as before.

### xUnit1051

The probe with `await Task.Delay(1)` (no token), the project's `TreatWarningsAsErrors` set to false:

| `TreatWarningsAsErrors` | `MSBuildTreatWarningsAsErrors` | xUnit1051 |
|---|---|---|
| false | true (the repository's `Directory.Build.props`) | error |
| false | false | warning |
| true | false | error |

So xUnit1051 is a warning in xunit.analyzers 2.3.0, and either of the template's two settings makes it an error; Spar.Sdk
raises nothing (`spar-msbuild`: the SDK raises no warning to an error in apps). The template's tests pass
`TestContext.Current.CancellationToken` to every call that takes a token.

### Spar's own runs after the change

Windows:

| Run | Result |
|---|---|
| `dotnet build Spar.slnx -c Debug` | 0 warnings, 0 errors |
| CoreCLR, the solution without `Surface=Window` and `Surface=BlackBox` | 1,131 (1,108 passed, 23 skipped) |
| `dotnet test --project tests/Spar.WindowTests` | 192 (112 passed, 80 skipped, as before) in 1 min 37 s |
| `dotnet test --project samples/Notebook.EndToEndTests` | 27 (23 passed, 4 skipped, as before) |
| NativeAOT lane: `Spar.Tests`, `WireShapes.Tests`, `Notebook.Tests`, `Notebook.EndToEndTests` | 769 (7 skipped), 83, 12, 20 (4 not run, the explicit Sandbox tests) |
| `dotnet test --project tests/Spar.BuildTests` | 6 of 6 in 2 min 41 s |
| `dotnet test --project tests/AppModel.Tests` | 11 of 11 |
| TypeScript SDK: `npm run check`, `vitest run` | clean; 304 tests |
| `dotnet run eng/check-size.cs` | 568 files, 0 errors, 0 warnings |

Linux (`docs/evidence/m1c-blackbox/wsl.sh sync client appmodel build test`, then `window`, then `packagetests`): the
build in 28 s; CoreCLR without window and black-box 1,131 (1,090 passed, 41 skipped); `Spar.WindowTests` 192 (164 passed,
28 skipped, as before) with 0 processes left; the package tests 18 of 18 as above.

## Not run

- The Linux NativeAOT window lane (`windowaot`) and the Notebook's Linux black box (`notebook`): the change there is the
  hook's trait read, which `WireShapes.Tests`' NativeAOT lane and the Windows `Notebook.EndToEndTests` lane exercised.
- The package tests on CI's hosted runners: the Linux one has no WebKitGTK, Xvfb or at-spi2-core, so its window and
  black-box runs would skip (the test says why); the Windows one was not run.
