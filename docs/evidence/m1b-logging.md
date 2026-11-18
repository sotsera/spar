# M1b step 7: logging

Recorded 2026-11-18. What it shows: the app's one log file of plan M1b, build step 7, with the public shape of decision "Logging's public shape": `app.log` with the host's, the app's and the page's lines, on the published NativeAOT Notebook and on the test app made for page security (`tests/apps/SecurityProbe`); the page's errors from the host-injected script with their flood limits, from floods of 100,000 errors of each kind; the image size, and the caller's cost of a line. Decision "Logging (implementation record; choices for review)".

## What was built

| Files | What |
|---|---|
| `src/Spar/Logging/LogFile.cs`, `LogLine.cs` | The writer (moved from `src/Spar.Windows/LogFile.cs`): `app.log` in the logs folder, a bounded queue of 10,000 entries drained by its own task in batches of up to 512, one append each through the append-only handle; a full queue drops the newest entries and the next batch says how many; moved aside to `app.1.log` above 5 MB. The lines: UTC time, a five-character level, category, message, further lines indented by four spaces. |
| `src/Spar/Logging/SparFileLoggerProvider.cs`, `SparLoggerFactory.cs`, `SparLogging.cs` | The file's provider (alias `SparFile`) in every app's services, Spar's own small `ILoggerFactory` and `ILogger<T>` unless the app brought a factory, and the registration hosts call. |
| `src/Spar/Logging/LoggingOptions.cs`, `LogLevels.cs`, `LoggingFile.cs` | `spar.Logging(l => l.Level(…).Level(category, …))`, the levels by category prefix, and `logging.json` in the data root, read strictly with `Utf8JsonReader`. |
| `src/Spar/Logging/PageErrorReader.cs`, `PageErrorLog.cs` | The page's error batches read untrusted and logged under `Spar.Page`: the first of a group with its stack, the rest in summaries, bounds per document, totals at exit. |
| `src/Spar.Windows/PageScript.js`, `PageScript.cs` | The injected script's `reporter` shared by the violation listener and the new error reports: `console.error` wrapped, `error` and `unhandledrejection` listened to. |
| `src/Spar/Bridge/*`, `src/Spar.Client/src/protocol.ts`, `tests/conformance/bridge-messages.json` | The `errors` message, page to host. |
| `src/Spar.Windows/HostLog.cs`, `WindowsApp.cs`, `WebViewBridge.cs`, `src/Spar/Runtime/UnsupportedWindowsHost.cs`, `src/Spar/Testing/SparTestSession.cs` | The hosts: the levels read before the first line, the file opened by the host and shared with the services, the page's batches to `PageErrorLog`; the test host's file in the test's own logs folder. |
| `samples/Notebook` | The store logs a warning for a file it skips; the page writes a failure no handler took with `console.error`. |
| `tests/apps/SecurityProbe/frontend/src/main.js` | Probe modes for the page's errors: `console`, `distinct-console`, `uncaught`, `distinct-uncaught`, `rejections`, `distinct-rejections` (each `:N`), and `page-errors`. |

## How it was run

[`m1b-logging/scenarios.sh`](m1b-logging/scenarios.sh), with the page security run's `SecurityRun.cs` copied outside the repository: each app run with `LOCALAPPDATA` in its own folder under `artifacts/webview2-profiles/m1b-logging/`, its window shown without activation (`SW_SHOWNOACTIVATE`) and moved offscreen at once, no input sent, closed with `WM_CLOSE`. Each run's `app.log` was copied beside its run log into [`m1b-logging/logs/`](m1b-logging/logs/), with the repository's path written as `<repo>`. Builds: `aot` is `dotnet publish tests/apps/SecurityProbe -c Release` (NativeAOT, Production, 4,330,496 B); `dev` its Debug build with `SparDevServer=false` (CoreCLR, Development, stall probe on), run with `SPAR_AUTOMATED=1`; `notebook` is `dotnet publish samples/Notebook -c Release`. WebView2 Runtime 157.0.4341.46. The whole set ran twice (2026-11-18, the second after the last change to the script); the numbers below are the second run's, with the first's where it differs.

## Results

### One file with every line

- **The Notebook** (`notebook.app.log`): the run's data folder held a page, a hand-written `draft.md` without front matter, and `locked.md`, which another process held open without sharing. The file has the host's start-up and shutdown lines (`Spar.Host`, with the run id), the app's own warning (`WARN  Notebook.Pages.MarkdownPageStore  Skipped draft.md: …`), the bridge's error for the failed `pages.list` with the `IOException` and its stack indented below (`ERROR Spar.Bridge`), and the page's `console.error` for the failure it reported (`ERROR Spar.Page  console.error at https://app.localhost/assets/index-….js:54:7061: Notebook: pages.list failed: …`, its call stack below), then the page's totals at exit. Exit 0.
- **The probe's `page-errors`** (`aot-page-errors.app.log`, `dev-page-errors.app.log`): a throw in a timer, a rejection nobody handled and a `console.error` with a `SyntaxError` each gave one `ERROR Spar.Page` line with the place in the page and the stack; the console one named its caller's place and showed the error's stack. In Development the file also had Debug lines.
- Every line starts with its UTC time; a message's further lines and every stack line start with four spaces.

### The page's errors under a flood

Each mode raises N errors in a loop on the page's own thread, timed in two parts: the first 1,000, which Spar's script reports, and the rest, past its bound of 1,000 per document, which it no longer sees (its listeners removed and `console.error` restored). Times include one `setTimeout(0)` after each part.

| Mode (Development, N = 100,000) | First 1,000 | Other 99,000 | Spar's cost per error | Log |
|---|---|---|---|---|
| `console` (the same text and an `Error` each time) | 22.4 ms (18.8) | 440.7 ms (466.7) | about 18 µs | 1 error line, "stopped reporting", 1 summary "999 more", totals |
| `distinct-console` | 9.6 ms (6.9) | 372.8 ms (424.0) | 3–6 µs | 100 error lines, "more than 100 different", "stopped", summary "past the group limit: 900" |
| `uncaught` (`reportError` of one error) | 12.4 ms (19.0) | 579.4 ms (430.2) | 6–15 µs | 1 error line, "stopped", summary "999 more" |
| `distinct-uncaught` | 8.9 ms (15.2) | 422.1 ms (473.7) | 5–10 µs | 100 error lines, "more than 100", "stopped", summary |
| `rejections` | 6.5 ms (1.4) | 165.7 ms (143.1) | not measurable in the page's task | 1 error line, "stopped", summary "999 more" |
| `distinct-rejections` | 1.8 ms (3.2) | 170.9 ms (163.1) | not measurable | 100 error lines, "more than 100", "stopped", summary |

- **Every flood was one bridge message** per document (`1 batches, 1000 errors` in the totals), and no batch was dropped. 100,000 errors kept the page busy 0.17–0.59 s in all, against about 20 s for 100,000 distinct violations (`docs/evidence/m1b-page-security.md`): the bound of 1,000 per document limits what Spar's script costs the page (about 2–22 ms per document), not the page's own cost.
- **Production, N = 1,000** (`aot-*-1000`): the same lines without "stopped"; the first 1,000 in 7.3–21.5 ms.
- **The UI thread:** the Development stall probe logged no stall in any run; the heartbeat's longest tick 0.64–1.20 ms (CoreCLR Debug). The page's batches reach the host as one web message a second at most and are copied off the UI thread.
- **Memory:** the app process's working set 50.1–52.1 MB at the end of a Development flood and 24.3–25.5 MB in Production (`run:` lines of the run logs).

### Image size

| Build | Notebook.exe |
|---|---|
| Before the step (step 6) | 5,010,944 B |
| Microsoft.Extensions.Logging's `AddLogging()` alone, measured and reverted | 5,101,568 B (+90,624 B) |
| The same with one filter rule and a minimum level | 5,107,200 B (+96,256 B) |
| This step, first version (Spar's own factory) | 5,131,264 B (+120,320 B) |
| This step, final | **5,103,616 B (+92,672 B)**, 0 warnings, within the 6 MB budget |

From the ILC map (`-p:IlcGenerateMapFile=true`) of the final build's predecessor (5,101,568 B): `Spar.Logging` 65.8 KB, of which `PageErrorLog` 21.2 KB, `LogFile` 9.6 KB, the factory 7.0 KB, `LogEntry`'s channel and array types 5.5 KB, `LogLevelRule` 4.9 KB, `LoggingFile` 4.0 KB, `PageErrorReader` and `PageError` 7.5 KB; `ILogger<T>`'s registration 4.6 KB; the larger injected script about 7 KB of resource. On the way, the host's logger shrank from 17.0 to 6.4 KB and the fan-out logger from 11.3 to 7.0 KB (pitfall "each `ILogger` type's `Log<TState>` is compiled again").

### The caller's cost

`LogFileTests.Writing_costs_the_caller_a_few_microseconds_and_never_waits_for_the_disk`: 10,000 lines through the file's logger with two arguments cost the caller 0.26 µs each (CoreCLR Debug, Spar.Tests): the message is formatted on the caller's thread and queued; time, level and exception are written by the file's task.

### Unit tests

`LogFileTests` (11), `LogLevelsTests` (26 with the theory rows), `PageErrorLogTests` (10), the injected script's tests in `src/Spar.Client/test/pageScript.test.ts` (19, ten of them for the errors), the `errors` vector on both sides, the two log-file tests of `SingleInstanceTests` (two writers keep all 4,000 lines; the too-old Windows writes to `app.log`).

## Not verified

- Linux: two processes appending (the writer appends at the file's length there, which two processes can race; M1c), and the script on WebKitGTK (`Error.captureStackTrace` is V8's; without it a console error has no place).
- An app with Microsoft.Extensions.Logging's own factory (`AddLogging`), its filters before the file, and the `SparFile` alias: the package is not used in this repository; a unit test covers an app's own factory instance.
- DevTools in Development: where the console links a wrapped `console.error` (a row of "Checks that need the user at the PC").
- "Exit now" after this step (a prompt on the desktop): its code is unchanged, and the file it closes now holds every line.
- A full disk, an unwritable logs folder, a dropped-entries line from a real app (unit-tested only), a `logging.json` on a real run, `KeepData` with the test's file, the Windows Sandbox focus runner's change to read `app.log` (not run).
