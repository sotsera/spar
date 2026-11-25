# Spike: what Microsoft.Extensions.Configuration costs a Spar app

> A spike. Its code was deleted with `spikes/` at the start of M2; it is in git history at [`spikes/configuration-cost/`](https://github.com/sotsera/spar/tree/24f5db0942833047c81fa692908410596f3b8960/spikes/configuration-cost) (commit `24f5db0`), and this record and its `results/` moved here. Paths under `spikes/` below name that history.

Recorded 2026-11-18, on branch `m1b` after commit 704e429 (M1b step 7, logging through Microsoft.Extensions.Logging's own factory). Measurement only: nothing under `src/`, `tests/`, `samples/` or `docs/` changed.

**Question.** Decision "Logging's public shape" chose Spar's own `logging.json` reader over the configuration packages ("no configuration package, no reflection, no size"). What would the configuration packages actually cost a Spar app, in image size, warnings, start-up and memory? And does the JSON provider's use of System.Text.Json touch decision "Wire serialization: generated code, confirmed" (the wire uses the generator's own `Utf8JsonReader`/`Utf8JsonWriter` code, not System.Text.Json metadata)?

## Answer

- **Size.** The standard route (`AddJsonFile` plus `AddConfiguration(config.GetSection("Logging"))`) adds **351,232 B** to a Production NativeAOT Spar executable; with environment variables, `IConfiguration` in DI and one options class bound by the binding generator, **393,728 B**. `reloadOnChange: true` adds **0 B** more. Most of it is not configuration itself but the file machinery `AddJsonFile` always brings (file provider, file watcher, globbing, and the generic instantiations they need in CoreLib and System.Linq): reading the file in the app and handing it over with `AddJsonStream` costs **82,432 B**.
- **Warnings: none.** All five variants publish with zero trim, AOT or ILC warnings (warnings are errors in these builds, and `TrimmerSingleWarn=false`).
- **System.Text.Json: `JsonDocument` only.** The JSON provider parses with `JsonDocument.Parse` and walks `JsonElement`s. The image gains 12,787 B of System.Text.Json code (on top of the 63,160 B of `Utf8JsonReader`/`Utf8JsonWriter` code the baseline already has): `JsonDocument` and its `MetadataDb`, `PropertyNameSet`, `StackRowStack`, `JsonElement` and its enumerators, `JsonDocumentOptions`, a little more of `JsonReaderHelper` and `ThrowHelper`, and `Marvin` hashing. No `JsonSerializer`, no `JsonTypeInfo`, no `System.Text.Json.Serialization` node in any variant's map, and no reflection-based configuration binding (`ConfigurationBinder`'s reflection path is absent; Logging.Configuration carries its own source-generated binder, 1,363 B).
- **Start-up and memory: no measurable difference** in host start to `ui-ready` (medians 344–356 ms, within the runs' spread); building the configuration adds about 0.6 ms before the host starts. Working set +0.3 MB (v1) to +0.6 MB (v3); 11 threads in every variant, the file watcher included.
- **The levels take effect**, and a file change applies while running with `reloadOnChange: true` (within 192–442 ms). But the standard shape's `"Default"` **silences Spar's own host lines** and **overrides `logging.json`'s `"level"`** (details below).
- **Decisions.** Nothing measured here contradicts the wire decision: configuration's JSON is `JsonDocument` over the same reader, with no metadata or serializer, and Spar's wire code is unaffected. The logging decision's size argument holds (351 KB for the standard route against about 4 KB for `LoggingFile`), and the interaction of the standard `"Default"` with Spar's levels is a new consideration for it.

## What was built

| Path | What |
|---|---|
| `ConfigCost/` | A Spar app built as `tests/apps/SecurityProbe` is: `<Project Sdk="Spar.Sdk">` in source mode, `SparAppId` `com.example.spikes.ConfigCost`, a one-contract backend (`probe`: `details()`, `report(text)`) and a minimal page (`frontend/`, the probe's `package.json` and lockfile renamed) that signals `ui-ready`, asks for the run's details and reports them to standard error. With `CONFIG_COST_TICK=1` a timer logs `tick N` at Debug under `ConfigCost.Ticker` every 250 ms. |
| `Directory.Build.props` | `-p:ConfigCostVariant=N`; each variant restores and builds into its own `obj/vN/` and `bin/vN/`. |
| `Directory.Packages.props` | Imports the repository's central versions and adds the configuration packages at 11.0.0 for this spike only (central management needs a `PackageVersion`; the root file is not touched). |
| `ConfigRun.cs` | The run of `docs/evidence/m1b-page-security/SecurityRun.cs` with the thread count beside the working set and an optional file write while the app runs (`--write`, `--content`, `--write-after`). |
| `run.sh` | The scenarios: `levels`, `reload`, `spar-file`, `startup <set>`. |
| `map-summary.ps1`, `startup-summary.ps1` | Size attribution from the ILC maps; start-up medians. |

The variants (all `dotnet publish ConfigCost -c Release -p:ConfigCostVariant=N`: Production, NativeAOT, win-x64; .NET SDK 11.0.100):

| Variant | Program.cs |
|---|---|
| v0 | The app as it is, Spar's logging only. |
| v1 | `new ConfigurationBuilder().AddJsonFile(<data root>\appsettings.json, optional: true, reloadOnChange: false).Build()`; `spar.Services.AddLogging(b => b.AddConfiguration(config.GetSection("Logging")))`. Packages: Microsoft.Extensions.Configuration, .Configuration.Json, .Logging.Configuration. |
| v2 | v1 plus `.AddEnvironmentVariables("CONFIGCOST_")`, `IConfiguration` registered as a singleton, `services.Configure<ProbeOptions>(config.GetSection("Probe"))` with `EnableConfigurationBindingGenerator=true` (the generator intercepts the call; `obj/v2/.../BindingExtensions.g.cs`). Adds .Configuration.EnvironmentVariables, .Configuration.Binder, .Options.ConfigurationExtensions. |
| v3 | v2 with `reloadOnChange: true`. |
| v4 | v1 with the file read by the app and handed over with `AddJsonStream` (no file provider). Added after the attribution showed where v1's bytes go. |

Packages resolved (`obj/v2/project.assets.json`): Microsoft.Extensions.Configuration, .Binder, .EnvironmentVariables, .FileExtensions, .Json, .FileProviders.Physical, .FileSystemGlobbing, .Logging.Configuration, .Options.ConfigurationExtensions, all 11.0.0, plus the repository's Microsoft.Extensions.Logging and .DependencyInjection 11.0.0. The abstractions (Configuration.Abstractions, Primitives, FileProviders.Abstractions, Options, Logging.Abstractions) come from the .NET 11 NativeAOT runtime pack, not from packages.

The app's file is `appsettings.json` in its data root: `{"Logging":{"LogLevel":{"Default":"Warning","ConfigCost.ProbeService":"Debug","ConfigCost.Ticker":"Information"}},"Probe":{"Greeting":"from-file","Count":3,"Tags":["a","b"]}}`. Spar exposes its data root only through the `SparFolders` service, after the services exist, so the app repeats Spar's folder rule (`%LOCALAPPDATA%\<app id>`) to build its configuration first; in a Development build that rule appends `.Development`, which an app would have to repeat too.

## How it was run

`run.sh` with `ConfigRun.cs` copied outside the repository: each app run with `LOCALAPPDATA` in its own folder under `artifacts/webview2-profiles/configuration-cost/`, its window shown without activation and moved offscreen at once, no input sent, closed with `WM_CLOSE`; standard error read with the runner's clock. Run logs and each run's `app.log` are in [`results/logs/`](results/logs/), with the repository's path written as `<repo>`. WebView2 Runtime 157.0.4341.46. The start-up runs used the first publish of v0–v3; v0–v3 were published again after v4 was added (its code is behind its own symbol) and came out at the same sizes.

## Results

### Size

[`results/sizes.txt`](results/sizes.txt), [`results/publish-v*.txt`](results/):

| Variant | ConfigCost.exe | Delta from v0 |
|---|---|---|
| v0 baseline | 4,396,544 B | – |
| v1 JSON file for logging | 4,747,776 B | **+351,232 B** |
| v2 + environment variables, `IConfiguration`, bound options | 4,790,272 B | **+393,728 B** |
| v3 = v2 with `reloadOnChange: true` | 4,790,272 B | **+393,728 B** (0 B over v2) |
| v4 = v1 through `AddJsonStream` | 4,478,976 B | **+82,432 B** |

On the Notebook (5,182,464 B after step 7, budget 6 MB), v1's cost would make about 5.53 MB and v2's about 5.58 MB.

**Where it goes** ([`results/map-assemblies.txt`](results/map-assemblies.txt), [`results/map-types.txt`](results/map-types.txt); bytes of ILC map nodes by owning assembly, which add up to about 92% of the file delta; the rest is unwind data, relocations and alignment):

| Owner | v1 − v0 | v2 − v0 | v4 − v0 |
|---|---|---|---|
| All map nodes | 322,691 | 362,532 | 77,106 |
| System.Private.CoreLib (generic instantiations: `ConcurrentDictionary`, `List`, `SharedArrayPool`, file enumeration, …) | 109,939 | 125,708 | 16,283 |
| Embedded metadata (names for stack traces and reflection-visible members) | 28,904 | 33,928 | 9,251 |
| Microsoft.Extensions.FileSystemGlobbing | 23,106 | 23,106 | 0 |
| Microsoft.Extensions.FileProviders.Physical | 19,965 | 19,965 | 0 |
| System.Linq | 19,354 | 19,354 | 2,856 |
| Image-wide and unattributed nodes (method tables of arrays and instantiations, frozen objects, stack-trace map, dispatch cells, hashtables) | 56,517 | 63,473 | 13,055 |
| **System.Text.Json** | **12,787** | **12,787** | **12,787** |
| System.IO.FileSystem.Watcher | 11,353 | 11,353 | 0 |
| Microsoft.Extensions.Configuration | 8,158 | 8,310 | 8,437 |
| Microsoft.Extensions.Primitives (change tokens) | 6,076 | 6,076 | 811 |
| Microsoft.Extensions.Logging.Configuration | 4,966 | 4,966 | 4,966 |
| System.Collections | 4,698 | 4,706 | 2,376 |
| Microsoft.Extensions.Configuration.EnvironmentVariables | – | 4,313 | – |
| System.Security.Cryptography (`IncrementalHash`, used by the file watcher's polling token) | 4,098 | 4,098 | 331 |
| Microsoft.Extensions.Configuration.FileExtensions | 4,096 | 4,096 | 0 |
| Microsoft.Extensions.Configuration.Json | 3,553 | 3,553 | 3,180 |
| The app's own code (v2: the binding generator's code 4,773 B, the options) | 513 | 6,714 | 608 |
| Other assemblies (Configuration.Abstractions, ComponentModel.Primitives, FileProviders.Abstractions, Options, DI abstractions, Spar 432 B) | 4,608 | 6,026 | 2,165 |

`AddJsonFile` always builds a `PhysicalFileProvider` with its `PhysicalFilesWatcher`, a `FileSystemWatcher`, polling change tokens and the globbing matcher; whether it watches is decided at run time, so `reloadOnChange` cannot remove them and adds nothing either (v3's map is 2 B larger than v2's, the file the same size).

### Warnings

None. Each variant's `dotnet publish` output ([`results/publish-v*.txt`](results/)) has no warning, with `TreatWarningsAsErrors`, `IlcTreatWarningsAsErrors`, the trim and AOT analyzers on (`PublishAot`), and `TrimmerSingleWarn=false`. Nothing was suppressed.

### System.Text.Json

[`results/map-stj-types.txt`](results/map-stj-types.txt): every System.Text.Json map node by type and node kind, v0 against v1, v2 and v4.

- **Gained (v1 − v0 = 12,787 B):** `JsonDocument` 6,656 B (code 5,953), `JsonDocument.PropertyNameSet` and its comparer 2,014 B, `JsonDocument.MetadataDb` 979 B, `ThrowHelper` +871 B, `JsonReaderHelper` +768 B, `Marvin` 519 B, `JsonElement` and its object and array enumerators 697 B, `JsonDocument.StackRowStack` 153 B, `JsonDocumentOptions` 72 B, `JsonException` +45 B.
- **Already there in v0 (63,160 B), unchanged:** `Utf8JsonReader` 34,215 B, `Utf8JsonWriter` about 15,700 B, `JsonWriterHelper`, `BitStack`, `JsonReaderState`: Spar's wire and `logging.json` code.
- **Absent from every variant:** `JsonSerializer`, `JsonTypeInfo`, `JsonSerializerOptions`, anything in `System.Text.Json.Serialization` (0 matching nodes in each map).
- So the configuration's JSON is the document reader built on the same `Utf8JsonReader`: no metadata, no serializer, no reflection. It is the same in v4, so it is the floor for any JSON configuration source.

### Start-up

Host start to `ui-ready` on the host's own clock, as in `docs/evidence/m1b-logging.md`; three sets of eight interleaved rounds (v0 v1 v2 v3 in each round), the first round of each set left out, medians of the other 7 ([`results/startup.txt`](results/startup.txt) has every run; logs in [`results/logs/startup/`](results/logs/startup/)):

| Variant | Set 1 | Set 2 | Set 3 | All 21 | Process start to host start (all 21) |
|---|---|---|---|---|---|
| v0 | 359.7 ms | 360.2 ms | 347.5 ms | 351.1 ms | 20.5 ms |
| v1 | 344.2 ms | 360.7 ms | 360.2 ms | 356.0 ms | 21.2 ms |
| v2 | 343.0 ms | 352.0 ms | 346.5 ms | 344.2 ms | 21.0 ms |
| v3 | 342.5 ms | 363.2 ms | 353.5 ms | 353.5 ms | 21.1 ms |

- No difference beyond the runs' spread (314–462 ms without the outliers below), and no order between variants. The configuration is built in `Program.cs` before the host starts: process start to host start is 0.5–0.7 ms longer in v1–v3 (median).
- **Outliers:** 6 of 96 runs took about 5 s, spread over every variant (v0 once, v1 twice, v2 once, v3 twice): five between "navigation started" and "first document" (the browser's side), one before the host started (v1, set 2, round 3: `Host started 5019.7 ms after the process`, the window too appeared only then). Not explained; the m1b logging runs had none. They are left out of the medians by being single runs in a set of 7, not filtered.
- v4 was not timed; its start differs from v1 only in reading the file itself.

### Memory and threads

One second after `ui-ready` (medians of the same 21 runs): working set v0 25,280 KiB, v1 25,584 (+304), v2 25,780 (+500), v3 25,868 (+588); private bytes 8,972, 9,100, 9,244, 9,324 KiB. **Threads: 11 in every run of every variant**, v3 included: the file watcher adds no thread of its own (its notifications complete on the thread pool). The reload runs ended with 11 threads too.

### Do the levels take effect?

`levels` ([`results/logs/levels-v*.app.log`](results/logs/)), the file above, and `CONFIGCOST_Probe__Greeting=from-env` in the environment:

- **v1–v4:** `DEBUG ConfigCost.ProbeService  Debug from the app: details asked` is in `app.log` (Spar's default Information would drop it; v0's `app.log` has only the Information line). The app's category rule works through Spar's file provider.
- **v2, v3:** the page reported `options greeting=from-env count=3 tags=a,b; Logging:LogLevel:Default=Warning`: the binding generator bound the section, the environment variable overrode the file, `IConfiguration` resolved from DI.
- **But `"Default":"Warning"` silences Spar's own lines.** In v1–v4, `app.log` has Spar's host lines only up to "prerequisites checked" (the host's early logger, before the services exist); from "services built" to "exit 0" every `INFO Spar.Host` line is gone: `ui-ready`, the security headers, shutdown, the exit. Microsoft's filter picks a matching rule before the minimum level; the configuration's `"Default"` is a rule for every category, and Spar writes its default level as the minimum (`LogLevels.Apply`).
- **And it overrides `logging.json`'s `"level"`** (`spar-file`, v1): with `logging.json` `{"level":"Debug"}` beside the app's file, Spar's host lines stay silenced (4 `Spar.Host` lines, as without it). With `{"categories":{"Spar":"Information"}}` instead, they come back (39 lines, `ui-ready` and `exit 0` included): a category rule is more specific than the default rule and is added after the app's. So the per-user support switch Spar documents (raise `"level"` without a new build) does not work for an app that configures `"Default"` this way; its categories still do.

### `reloadOnChange`

`reload` ([`results/logs/reload-v*.log`](results/logs/)): the ticker running, the file rewritten 1.5 s after the page finished, with `"ConfigCost.Ticker":"Debug"`:

- **v3:** written at 12:07:51.266 UTC; first `DEBUG ConfigCost.Ticker  tick 9` at 51.708, then every 250 ms (17 ticks until the window closed). The change applied 192–442 ms after the write (the ticker's 250 ms period bounds it; the provider waits 250 ms by default before reloading).
- **v1, v2:** no tick line after the same write: the levels stay as read at start.
- **Cost:** 0 B of image over v2; no extra thread; working set +88 KiB over v2 at the median.

## What this means for the decisions

- **Wire serialization (2026-11-13):** unaffected. That decision is about Spar's bridge, whose code the generator owns; an app adding JSON configuration adds `JsonDocument` (12.8 KB), not System.Text.Json metadata or the serializer, and Spar's wire code is the same in every variant. The baseline already carries the `Utf8JsonReader`/`Utf8JsonWriter` code the document reader builds on.
- **Logging's public shape:** the size reason holds: the standard route costs 351 KB (393 KB with environment variables and binding), against about 4 KB for Spar's `LoggingFile` and 79 KB for the whole logging factory step 7 accepted. "No reflection" holds for the configuration packages too, when an app uses the binding generator. The packages are AOT-clean, so an app may add them; what it gets for the bytes is reload while running, environment variables and options binding. Two behaviours an app that adds them should know, and that the docs or Spar's levels might address: the standard `"Default"` silences Spar's host lines in `app.log`, and makes `logging.json`'s `"level"` ineffective.
- **For an app that wants configuration for little size:** `AddJsonStream` with a file the app opens itself costs 82 KB, of which System.Text.Json is 12.8 KB; it loses reload.

## Not verified

- Linux (NativeAOT linux-x64; the file watcher there uses inotify and may add a thread).
- Start-up of v4, and a cold start after a reboot (all runs were warm, back to back).
- A Development build, or the app's configuration in a test host run.
- `IOptionsMonitor` of the bound options reloading with the file (only the logging levels were watched).
- Writing `appsettings.json` atomically (rename over) instead of in place, which some editors do; and a file that fails to parse while the app runs.
- Why 6 of 96 runs took 5 s.
