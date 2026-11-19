# M1b budgets at the exit

Recorded 2026-11-19 on the development PC (Windows 11 Pro 10.0.26320, WebView2 Runtime 157.0.4341.60), with `tests/Spar.WindowTests/BudgetTests.cs` (plan, "Provisional budgets"; decision "M1b budgets measured at the exit"). The tests record and never assert; the file each run writes is `artifacts/test-results/Spar.WindowTests/budgets.txt`.

Commands:

```bash
dotnet test --project tests/Spar.WindowTests -c Release --filter-class "Spar.WindowTests.BudgetTests"
dotnet publish tests/Spar.WindowTests -c Release -p:SparTestLane=Aot -o artifacts/aot/Spar.WindowTests
artifacts/aot/Spar.WindowTests/Spar.WindowTests -class "Spar.WindowTests.BudgetTests"
```

What each new line measures:
- **Launch to ui-ready:** WindowHost's Production NativeAOT executable, launched through the black-box fixture on its hidden desktop, 1 warm-up launch and 10 measured ones on the same data root (the browser profile reused, as a user's second start); the host's "Host started … after the process" plus its "ui-ready at …", so from the process's creation to the page's ready message. In both lanes the app is the same Production executable; the lane is the test process's.
- **Working set:** 2 s after `ui-ready`, the app process alone, and with the browser's processes of its job.
- **Bridge round trip:** the page times `host.echo("ping")` 200 times one after another after 20 warm-up calls, with `performance.now()`, whose resolution in WebView2 is 0.1 ms (hence the 0.00 minimum).

## CoreCLR test lane (Release)

```text
Window test: open, journey and close: 324.32 ms (296.80–5875.29, n = 20), p95 342.54 ms [Release, CoreCLR]
Window test: open (to the page's Ready): 135.26 ms (124.27–5575.69, n = 20) [Release, CoreCLR]
Window test: journey (fill, check, select, click, 3 assertions): 167.52 ms (155.71–261.20, n = 20) [Release, CoreCLR]
Window test: close (teardown and the renderer's exit): 14.03 ms (13.20–38.40, n = 20) [Release, CoreCLR]
Window test: its InPrivate controller: 24.00 ms (21.93–53.54, n = 20) [Release, CoreCLR]
Window test: the shared environment, first window of the process: 364.45 ms (364.45–364.45, n = 1) [Release, CoreCLR]
Window test: the shared environment, later windows: 3.11 ms (2.65–5.88, n = 19) [Release, CoreCLR]
Launch to ui-ready, warm (profile reused), from the process's start (WindowHost, Production NativeAOT): 359.50 ms (338.10–390.20, n = 10) [Release, CoreCLR]
App process working set 2 s after ui-ready (WindowHost, Production NativeAOT): 42.3 MB (41.9–42.4, n = 10) [Release, CoreCLR]
Working set with the browser's processes, 2 s after ui-ready (same launches): 357.7 MB (269.2–360.9, n = 10) [Release, CoreCLR]
Window test action: GetByRole, count: 0.61 ms (0.25–3.17, n = 100), p95 0.82 ms [Release, CoreCLR]
Window test action: GetByLabel, count: 0.46 ms (0.18–0.90, n = 100), p95 0.63 ms [Release, CoreCLR]
Window test action: an assertion that holds: 0.29 ms (0.22–0.81, n = 100), p95 0.58 ms [Release, CoreCLR]
Window test action: click (checks, trusted press, landed): 35.63 ms (34.18–37.04, n = 100), p95 36.30 ms [Release, CoreCLR]
Window test action: fill: 0.57 ms (0.47–1.28, n = 100), p95 0.96 ms [Release, CoreCLR]
Window test action: key press: 0.39 ms (0.36–1.42, n = 100), p95 0.56 ms [Release, CoreCLR]
Window test action: evaluate 1+1: 0.12 ms (0.10–0.66, n = 100), p95 0.22 ms [Release, CoreCLR]
Window test action: viewport screenshot: 53.32 ms (35.95–55.51, n = 20), p95 55.03 ms [Release, CoreCLR]
Desktop bridge: small unary round trip, timed by the page: 0.10 ms (0.00–10.00, n = 200), p95 0.20 ms [Release, CoreCLR]
```

## NativeAOT test lane (Release)

```text
Launch to ui-ready, warm (profile reused), from the process's start (WindowHost, Production NativeAOT): 375.65 ms (346.80–430.20, n = 10) [Release, NativeAOT]
App process working set 2 s after ui-ready (WindowHost, Production NativeAOT): 42.3 MB (39.8–42.5, n = 10) [Release, NativeAOT]
Working set with the browser's processes, 2 s after ui-ready (same launches): 358.0 MB (46.4–361.1, n = 10) [Release, NativeAOT]
Window test: open, journey and close: 342.60 ms (299.99–756.24, n = 20), p95 359.33 ms [Release, NativeAOT]
Window test: open (to the page's Ready): 154.67 ms (120.93–454.63, n = 20) [Release, NativeAOT]
Window test: journey (fill, check, select, click, 3 assertions): 171.74 ms (159.57–284.39, n = 20) [Release, NativeAOT]
Window test: close (teardown and the renderer's exit): 14.21 ms (13.07–31.77, n = 20) [Release, NativeAOT]
Window test: its InPrivate controller: 26.19 ms (22.65–65.58, n = 20) [Release, NativeAOT]
Window test: the shared environment, first window of the process: 244.44 ms (244.44–244.44, n = 1) [Release, NativeAOT]
Window test: the shared environment, later windows: 3.06 ms (2.46–3.71, n = 19) [Release, NativeAOT]
Window test action: GetByRole, count: 0.34 ms (0.20–1.02, n = 100), p95 0.74 ms [Release, NativeAOT]
Window test action: GetByLabel, count: 0.22 ms (0.17–1.91, n = 100), p95 0.31 ms [Release, NativeAOT]
Window test action: an assertion that holds: 0.28 ms (0.23–1.35, n = 100), p95 0.39 ms [Release, NativeAOT]
Window test action: click (checks, trusted press, landed): 35.67 ms (34.25–36.61, n = 100), p95 36.26 ms [Release, NativeAOT]
Window test action: fill: 0.56 ms (0.50–1.23, n = 100), p95 0.89 ms [Release, NativeAOT]
Window test action: key press: 0.43 ms (0.35–1.08, n = 100), p95 0.57 ms [Release, NativeAOT]
Window test action: evaluate 1+1: 0.13 ms (0.12–0.45, n = 100), p95 0.16 ms [Release, NativeAOT]
Window test action: viewport screenshot: 53.52 ms (50.36–71.34, n = 20), p95 56.34 ms [Release, NativeAOT]
Desktop bridge: small unary round trip, timed by the page: 0.10 ms (0.00–0.60, n = 200), p95 0.20 ms [Release, NativeAOT]
```

## Notes

- The 5 s outliers of "open, journey and close" (5,875 ms in the CoreCLR run) are the known rare first-load stall (pitfall "Rare 5 s stalls"); the medians and p95 are unaffected.
- One NativeAOT launch counted only 46.4 MB "with the browser's processes", while the app's own working set was normal: the job listed fewer browser processes at that moment. Why was not checked.
- The Production Notebook, published with `dotnet publish samples/Notebook -c Release`, is 5,237,248 B.
