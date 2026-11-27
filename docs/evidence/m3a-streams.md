# M3a: streams and subscriptions

Recorded 2026-11-27. What it shows: streams and subscriptions built as decided (decision "M3a's decide-first questions") and verified at every layer, on Windows and Linux: the generator and its diagnostics, the bridge's credits, batches and subscription buffer, the typed C# test clients, the generated TypeScript and the SDK's iterator, resumption on a new channel, tracing, the app model's contracts through real Spar, the Notebook's live feature, and the two stream budgets. Decision "M3a: streams and subscriptions (implementation record; choices for review; 2026-11-27)".

Code: `0df572b` (C#), `01b35b3` (the page), `661f11f` (resumption), `d2e574e` (tracing), `8498344` (the Notebook), `0e3ddde` (budgets), on branch `m3`.

Machine: the development PC, Windows 11 Pro 10.0.26320, 32 logical CPUs, SDK 11.0.100, Node 24, WebView2 Runtime 158 as installed, Playwright 1.64.1 with its Chromium; WSL 2 Ubuntu 24.04 for the CI lane (`eng/wsl/`, a fresh clone of `0e3ddde`); Spar's Linux CI image (`eng/linux-ci/`, Ubuntu 24.04, WebKitGTK 2.52) in Docker Desktop for the Linux window tests. Windows test windows were offscreen; Linux windows on a private Xvfb, the core limit at 1 byte. No window reached the monitor.

## Commands

```bash
dotnet test --solution Spar.slnx                                           # Windows, Debug
dotnet test --project tests/WireShapes.EndToEndTests -c Release -- --filter-class "WireShapes.EndToEndTests.BudgetTests"
dotnet test --project samples/Notebook.EndToEndTests                       # three runs
dotnet test --project tests/Spar.Tool.Tests                                # the skills' excerpts
cd src/Spar.Client && npm test && npm run check                            # TypeScript 6 and 7
MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/eng/wsl/setup.sh /mnt/<repo> m3a-exit
#   then step.sh with: size client appmodel build chromium test pagetests budgets
docker run … spar-linux-ci:eval sleep infinity                             # as in eng/linux-ci/run.sh's header
docker exec … bash /src/eng/linux-ci/run.sh copy client build window windowaot apps blackbox blackboxdev
```

## Results on Windows

- **The whole solution:** 1,618 tests, 1,505 passed, 0 failed, 113 skipped (Linux-only tests and the Sandbox lane), at `8498344` (before it, 1,588 at M2's end).
- **New tests:** the bridge's stream session 11 (`tests/Spar.Tests/Streams`) and the stream span 1; the generator's four new diagnostic cases, the fixture's four stream shapes compiling in every variant, the catalog entries and the fingerprint (resume changes it, capacity does not); the typed test clients 7 (`StreamClientTests`: 100,000 rows in order, the in-band error, an early break, a gap of 736 before the newest 8 of a capacity of 8, latest-only skipping to 999 with no gap, an automatic resume, a manual subscription ending lost); the SDK 16 stream tests, 2 tracing and 3 page-test fake; the client step's TypeScript 6 and 7 consumer and mistakes (a missing `reset` and a `gap` on a latest-only subscription are TS2322, a manual subscription asked to resume TS2322) and the typed twin's output; the browser 5 (`StreamBrowserTests`, one a resume over the real loopback channel: `reconnecting 1` … `reset`, `resumed`, then values, on a new document); the app model with `copyJobs` and `status` (11 of 11); the Notebook's page tests 16, window journeys 8 and browser journeys 7.
- **The Notebook end to end:** 32 tests, 28 passed, 4 skipped (Sandbox), in two runs in a row after the conflict journeys were reordered. One earlier full run had a browser journey (`A_taken_or_invalid_title_is_explained_in_the_form`) time out after 30 s once; it passed alone and in the two later full runs, and was not explained.

## Results on Linux

- **The CI lane in WSL** (`eng/wsl/`, a fresh clone of `0e3ddde`): the size check, the SDK's tests and both TypeScript versions' checks, the app model's install, the Debug build, the Notebook's page tests and the budgets passed. The solution's tests: 1,619, 21 failed, 220 skipped; the 21 are the Notebook's window, scene and black-box tests, each refused with "Xvfb was not found on PATH", since this lane has no Xvfb (the Linux window tests run in the CI image, below). Every other test passed, the stream tests of every layer and the browser tests in Chromium included.
- **The Linux window lane in Spar's CI image** (`spar-linux-ci:eval`, built from the current Dockerfile; the working tree at `0e3ddde` plus these records, copied by `run.sh`): every step passed. Window tests on CoreCLR: `Spar.WindowTests` 101 (75 passed, 26 skipped: the Windows-only start-up frame tests and four that M1c left), the Notebook 14 of 14, its new live journey and the reordered conflict journey included; on NativeAOT: `Spar.WindowTests` 98 (72 passed, 4 skipped, 22 not run, as on CoreCLR), the Notebook 11 of 11; the black box: `Spar.WindowTests` 82 of 82, the Notebook 7 of 7, the Development probe 4 of 4. The copy step reported "file changed as we read it" for `docs/evidence` because this file was being written; no code changed during the run.

## The budgets

Release, CoreCLR, timed by the page with `performance.now()`, over loopback in a headless Chromium (WireShapes' `BudgetTests`):

| Measure | Windows | Linux (WSL) | Decided |
|---|---|---|---|
| 100,000 stream rows from the call to the last row, median of 5 after a first run | 294.50 ms (287.40–330.70) | 506.20 ms (467.20–565.10) | "within the browser test's budget"; no line in the table yet (proposed below) |
| 20,000 subscription events offered over one second: all delivered, no gap; time to the last | 1,027.20 ms (1,025.60–1,027.50, n = 3) | 1,008.80 ms (1,003.60–1,026.00, n = 3) | 20,000 a second delivered whole |

The other browser lines of those runs, for comparison: the small unary round trip 0.70 ms (0.50–1.30, n = 200) on Windows and 0.80 ms (0.60–1.70) on Linux, and a browser test's app, context and journey 129.70 ms (124.01–139.96, n = 9) and 67.61 ms (61.49–81.20, n = 9).

**Proposed budget lines** (the user decides): "100,000 stream rows over loopback, median: ≤ 1 s" and "20,000 subscription events a second over loopback: all delivered", measured at each exit from M3a on.

## Not verified

- **The desktop bridges' stream throughput** (WebView2, WebKitGTK): measured only over loopback here; the window journeys run subscriptions on both desktop hosts, but nothing times them.
- **A resume after a backend restart under `dotnet watch` or `spar reload`:** the page reloads on a new run, as decided in M1d, so it never resumes there (decision "M3a: streams and subscriptions", item 11).
- **The "yield after an error" diagnostic:** not built.
- **CI on `m3`:** the workflow runs on pull requests and `main`; no pull request is open.
