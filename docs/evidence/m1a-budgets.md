# M1a: the budgets

Recorded 2026-11-16. What it shows: the three M1a budgets still open in the plan's "Provisional budgets" are met with room to spare on Windows, in Release builds. The command-test start is measured on CoreCLR and NativeAOT; the browser test and the loopback round trip on CoreCLR, the only runtime Playwright supports. Decision: "M1a budgets measured; the NativeAOT lane builds into bin/aot".

## How they are measured

The measurements are tests, so each slice exit reruns them the same way. They record and do not assert: times vary with the machine, and the plan checks budgets at slice exits. Each writes its median, range and count to `artifacts/test-results/<project>/budgets.txt`, with the build configuration and the runtime (`tests/WireShapes.Tests/Budgets.cs`, linked into the browser tests).

| Budget | Test | What one sample is |
|---|---|---|
| Command test: app start, median ≤ 2 ms | `tests/WireShapes.Tests/BudgetTests.cs` | `SparTestApp.StartAsync<Program>()` returning, over 19 starts after a first one; the same loop also records start + one call through the typed client + stop, the spike's measure |
| Browser test: app, context and journey, median ≤ 250 ms | `tests/WireShapes.EndToEndTests/BudgetTests.cs` | start the app on the Browser surface, open a new context in the shared Chromium, load the page and wait for ready, make one call, check the console is clean, close both; 9 after a first one |
| Small unary round trip over loopback, median ≤ 2 ms | same | one `echo.check` from the page to its result, timed by the page with `performance.now()`, 200 calls one after another after 20 to warm up |

To rerun them:

```
dotnet test --project tests/WireShapes.Tests -c Release -- --filter-class "WireShapes.Tests.BudgetTests"
dotnet test --project tests/WireShapes.EndToEndTests -c Release -- --filter-class "WireShapes.EndToEndTests.BudgetTests"
dotnet publish tests/WireShapes.Tests -c Release -p:SparTestLane=Aot -o artifacts/aot/WireShapes.Tests
artifacts/aot/WireShapes.Tests/WireShapes.Tests -class "WireShapes.Tests.BudgetTests"
```

## Results (observed, Windows 11, 32 logical CPUs; two runs each)

| Measure | Budget | Run 1 | Run 2 |
|---|---|---|---|
| Command test, app start, CoreCLR | ≤ 2 ms | 0.20 ms (0.17–0.39) | 0.24 ms (0.18–0.43) |
| Command test, app start, NativeAOT | ≤ 2 ms | 0.14 ms (0.13–0.34) | 0.20 ms (0.14–0.31) |
| Command test, start + call + stop, CoreCLR | (spike 0.52–0.57 ms) | 0.33 ms (0.28–1.43) | 0.42 ms (0.30–1.70) |
| Command test, start + call + stop, NativeAOT | (spike 0.37 ms) | 0.24 ms (0.21–0.50) | 0.35 ms (0.25–0.65) |
| Command test, the first one, CoreCLR / NativeAOT | – | 153 ms / 1.2 ms | 30 ms / 0.9 ms |
| Browser test, app + context + journey | ≤ 250 ms | 122.5 ms (117.2–142.6) | 136.5 ms (123.0–145.7) |
| Browser test, the first one | – | 161.8 ms | 142.6 ms |
| Small unary round trip over loopback | ≤ 2 ms | 0.70 ms (0.50–2.80, n = 200) | 0.70 ms (0.50–1.00, n = 200) |

Ranges are minimum to maximum over the samples. The first CoreCLR command test includes JIT; the first browser test includes the first page load in the shared browser.

### The Notebook's journeys, for reference

The budget's browser workload is the spike's: a small page and one action. The Notebook's own journeys do much more: a dialog, typing with autosave, a second window, and screenshots. Their durations in a Release run (`dotnet test --project samples/Notebook.EndToEndTests -c Release -- --report-xunit-trx`, the test duration from the TRX file, twice):

| Journey | Run 1 | Run 2 |
|---|---|---|
| The search box narrows the list | 213 ms | 191 ms |
| Deleting a page asks first | 442 ms | 392 ms |
| A new notebook invites the first page (with a screenshot) | 644 ms | 611 ms |
| A taken or invalid title is explained | 983 ms | 615 ms |
| The dark theme follows the system (screenshots) | 1,080 ms | 566 ms |
| Typing saves by itself | 1,237 ms | 1,178 ms |
| A page changed in another window (two contexts) | 1,740 ms | 1,186 ms |

They are not the budget's measure and are not compared with it. Whether the browser budget should also be set on a Notebook journey is left for the M1b review of budgets, when the Notebook's window test is measured too.

## Found while measuring

The first Release run of `WireShapes.Tests` did not start: "You must install or update .NET to run this application", with the .NET location being the test's own output folder. The NativeAOT lane builds the test project self-contained, and it shared `bin/Production/Release/` with the CoreCLR Release build. The runtime it copied there (`hostfxr.dll`, `coreclr.dll`) made the launcher of the CoreCLR build treat it as self-contained. The lane already restored into `obj/aot/`; it now builds into `bin/aot/` too (`Sdk.props`, `BaseOutputPath`). After the change, a lane publish followed by a CoreCLR Release run worked, and the folders are `bin/Production/` and `bin/aot/Production/`.

## Not covered

- **Linux:** none of these were measured there yet (the Linux runs are still open in M1a).
- **The page's clock is coarse:** every round-trip median, minimum and maximum above is a multiple of 0.1 ms, consistent with Chromium coarsening `performance.now()` on a page that is not cross-origin isolated (inferred, not checked against the browser's source).
- **CI:** the measurements run with the tests in Debug there; the budget's numbers are the Release runs above.
- Checked after the change: `dotnet test --solution Spar.slnx` 363 of 363, `Spar.BuildTests` 6 of 6, `Spar.PackageTests` 7 of 7 (the packed `Sdk.props` included), `-mt` and the size check clean (208 files).
