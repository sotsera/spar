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

## Follow-up (2026-11-18): Microsoft.Extensions.Logging, no file, the Development console

Decision "Logging's follow-up choices (user decisions)", record "Logging's follow-up (implementation record)". Spar's own factory is gone: the app's services use Microsoft.Extensions.Logging's `LoggerFactory`, and Spar's levels are its filter rules (`LogLevels.Apply`). `spar.Logging(l => l.WithoutFile())` adds no file. A Development build reads `console.error` from `Runtime.consoleAPICalled` (`src/Spar.Windows/DevelopmentConsole.cs`) and the injected script leaves it unwrapped there. Runs as above (`scenarios.sh follow-up`, `aot`, `dev`, `notebook`); logs in [`m1b-logging/logs/follow-up/`](m1b-logging/logs/follow-up/).

### Size

| Variant | Notebook (no logging of its own) | SecurityProbe | SecurityProbe with its own `AddLogging(… SetMinimumLevel(Warning))` | `spar.Logging` levels hold |
|---|---|---|---|---|
| Step 6, before logging | 5,010,944 B | – | – | – |
| (a) Spar's own factory, the app's factory wins if added | 5,103,616 B | 4,330,496 B | not built (the probe would need the package itself) | not under the app's factory (its minimum applies first) |
| (b) Microsoft.Extensions.Logging always (adopted) | **5,182,464 B** (+78,848 B over (a)) | 4,409,856 B | 4,411,392 B (+1,536 B) | yes, with and without the app's `AddLogging` |
| (b) with `WithoutFile()` | – | 4,410,368 B | – | – |

The Development console adds nothing to Production: the Notebook was 5,182,464 B before and after it. Measured earlier, the factory alone on step 6's Notebook: `AddLogging()` +90,624 B, with one rule +96,256 B. (c), the hybrid (Spar's factory unless the app adds Microsoft's), was not measured: the user judged the 79 KB not worth a factory of Spar's own.

### Start-up

Host start to `ui-ready` (the host's own clock), the published Notebook offscreen with `LOCALAPPDATA` under `artifacts/`, 8 runs a round, the first left out, median of the other 7 (`logs/follow-up/startup/`):

| Build | Round 1 | Round 2 | Round 3 |
|---|---|---|---|
| Before (5,103,616 B, Spar's own factory) | 418.8 ms | 388.5 ms | 412.3 ms |
| After (5,182,464 B, Microsoft.Extensions.Logging) | 414.8 ms | 376.5 ms | – |

No difference beyond the runs' spread (360–450 ms). The services' build moved from 2.2–2.3 ms to 2.8–2.9 ms after the host's start (median of the same runs).

### The levels hold

- `aot-add-logging`: the probe built with its own `AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning))` and `spar.Logging(l => l.Level("SecurityProbe", LogLevel.Debug))`: its Debug line `DEBUG SecurityProbe.ProbeService  The page asked for its probe: idle` is in `app.log`.
- `aot-plain-idle`: the plain probe at the default Information: no Debug line. `aot-logging-json`: the same build with `{"categories":{"SecurityProbe":"Debug"}}` in its data root: the Debug line is there.
- Unit tests: `LogFileTests` (`Spars_levels_hold_without_the_apps_own_logging…`, `…under_the_apps_own_AddLogging_whose_minimum_they_replace` with a second provider getting the same lines, `Logging_json_asking_for_Debug_reaches_an_app_category_under_the_apps_own_AddLogging`, `A_rule_the_app_adds_for_one_provider_still_applies_to_it` through the alias `SparFile`, `The_hosts_early_logger_filters_as_the_services_will`), and `LogLevelsTests` through `LoggerFactory`'s own rule selection.

### Without a file

`aot-without-file` (the probe with `WithoutFile()`, `page-errors`): no `app.log` and no logs folder in the run's data root; standard error had the host's 40 lines as before.

### The Development console

- `dev-console-devtools` and `dev-page-errors` (Development, `page-errors`): the `console.error` line is `ERROR Spar.Page  console.error at https://app.localhost/assets/index-….js:6:4346: probe: the settings could not be read SyntaxError: …`, the same place the Production wrapper reported (`aot-page-errors`: `:6:4346`), with the `SyntaxError`'s stack; the uncaught exception and the rejection come from the script as before, and each error is reported once. The host logged "Development console: the page's console errors come from the DevTools Protocol."
- Floods, Development (two runs each): 99,000 identical `console.error` calls with an `Error` 2.58–2.79 s of page time (0.44–0.47 s before, with the wrapper past its bound), 99,000 distinct 0.90 s (0.37–0.42 s); uncaught errors 2.63–2.71 s (0.42–0.58 s) and rejections 0.26–0.28 s (0.17 s), since `Runtime.enable` makes the renderer serialize every console call and exception for the session (pitfall). The log is as before: 1 or 100 error lines, "stopped reporting", a summary, totals. No stall; the heartbeat's longest tick 0.64–0.94 ms. The app process's working set 92 MB at the end of a console flood (50–52 MB before), from the events' JSON.
- Production (`aot-*`): unchanged, through the wrapper.

### Not verified (follow-up)

The devtools window's own display of a console error (its link now comes from the page's own stack, with no wrapper in Development); a document's navigation with `Runtime.enable` kept (one document per run here); Microsoft.Extensions.Logging's alias rule on a NativeAOT image (unit-tested on CoreCLR); the measured (c) hybrid.

## Configuration (2026-11-18): `logging.json` over the app's own configuration, `spar.ResolveFolders()`

Decision "Configuration stays the app's choice (user decision)", after [`spikes/configuration-cost/`](../../spikes/configuration-cost/RESULTS.md); record "Configuration stays the app's choice (implementation record; choices for review)". `logging.json`'s levels are now a post-configuration of `LoggerFilterOptions` (`LogLevels.ApplyFile`): its level the minimum and a rule for every category, its categories rules, all after anything the app configured. `spar.ResolveFolders()` gives `Program.cs` the folders the services get.

### How it was run

`scenarios.sh configuration`, as above (window offscreen and not activated, no input, `WM_CLOSE`, `LOCALAPPDATA` under `artifacts/webview2-profiles/m1b-logging/`). The build is `dotnet publish tests/apps/SecurityProbe -c Release -p:SecurityProbeAppConfiguration=true` (Production, NativeAOT, 4,766,208 B; 0 warnings): its `Program.cs` calls `spar.ResolveFolders()`, reads `appsettings.json` from the data folder with Microsoft.Extensions.Configuration (`AddJsonFile`) and applies its `Logging` section with its own `AddLogging(b => b.AddConfiguration(…))`. The file: `{"Logging":{"LogLevel":{"Default":"Warning","SecurityProbe":"Debug"}}}`; the probe's own Debug line shows it was read. Logs in [`m1b-logging/logs/configuration/`](m1b-logging/logs/configuration/), the repository's path written as `<repo>`. WebView2 Runtime 157.0.4341.60.

### Results

| Run | `logging.json` | `app.log` |
|---|---|---|
| `cfg-default-warning` | none | 4 `Spar.Host` lines, up to "prerequisites checked" (the host's early logger); every later Information line gone, `ui-ready` and `exit 0` included; the probe's `DEBUG SecurityProbe.ProbeService` line present. The override the documentation now describes. |
| `cfg-logging-json-level` | `{"level":"Information"}` | 40 `Spar.Host` lines, "services built", the security headers, `ui-ready at 361.3 ms`, `exit 0 at 1430.6 ms`; the probe's Debug line still present, since the app's category rule is more specific than the file's level (the choice in the record). |
| `cfg-folders` | none | the page asked the `SparFolders` service: data root `…\cfg-folders\com.example.tests.SecurityProbe`, data `…\data`, "same as Program.cs resolved: True". |
| `cfg-folders-moved` | none | `SECURITY_PROBE_DATA` moved the data folder with `spar.Folders(f => f.Data(…))` before `ResolveFolders()`: the service's data `…\cfg-folders-moved\moved-data`, "same as Program.cs resolved: True", and the probe's Debug line shows `appsettings.json` was read from the moved folder. |

All four exited 0.

### Size

| Build | Before | After |
|---|---|---|
| Notebook (`dotnet publish samples/Notebook -c Release`, 0 warnings) | 5,182,464 B | 5,184,512 B (+2,048 B) |
| SecurityProbe | 4,409,856 B | 4,411,904 B (+2,048 B) |
| SecurityProbe with its own configuration (`-p:SecurityProbeAppConfiguration=true`) | – | 4,766,208 B (+354,304 B over the probe; the spike measured +351,232 B for the same route) |

### Unit tests

`AppConfigurationTests` (Spar.Tests, with Microsoft.Extensions.Configuration and Logging.Configuration in the test project only): the app's `"Default"` overriding `spar.Logging`'s level; the file's level beating the app's `"Default"`, and its `AddFilter` without a category plus `SetMinimumLevel`, each registered before and after Spar's; the app's category rule beating the file's level and the file's category beating the app's; a `Program.cs` category keeping Spar's lines; a provider rule of the app's still applying to its provider; the file's level holding after the app's configuration reloads (a category the reload added shows it took effect); `ResolveFolders` in a desktop run's shape (`SparFolders.ForApp`, the app's choice applied, later `Folders` calls and a kept `FolderOptions` refused) and under the test host (`SparTestSession` running `SparApplication.Create`: the early value, the `SparFolders` service and the running app's folders equal the test's root). With the file's levels as a plain `Configure` instead of a post-configuration, the two "registered after" cases fail.

### Not verified (configuration)

Linux; a Development build of the configured probe (`ResolveFolders` uses the hosts' own `SparFolders.ForApp`, whose `.Development` suffix is unit-tested; not run); `reloadOnChange` with the file's level on a real run (unit-tested with an in-memory reload); an app's own `PostConfigure<LoggerFilterOptions>` (it runs before Spar's, since the app registers its services before Spar adds its own; only a test's service overrides come after).
