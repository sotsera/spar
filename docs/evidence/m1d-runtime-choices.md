# M1d step 7: runtime choices (evidence)

Recorded 2026-11-23. Plan M1d, build step 7 (decisions "App authors choose the runtime and their own AOT warning
policy", "CoreCLR as the app's escape hatch (evidence)", "App-specific settings stay in the app" and "M1d step 7's
shape: runtime choices"), and the implementation record "Runtime choices (implementation record; choices for review;
2026-11-23)". The guide is [`docs/guide/publishing-choices.md`](../guide/publishing-choices.md), linked from section 7
of the getting-started guide.

## Setup

- Windows 11 Pro 10.0.26320, SDK 11.0.100 (ILCompiler and ILLink 11.0.0), Node 24.21.0, WebView2 Runtime
  157.0.4341.60, Visual Studio 18 Community with the C++ build tools. Its `PATH` holds the Windows SDK's Windows
  Performance Toolkit folder, which has a `WebView2Loader.dll` (see "The WebView2 loader" below).
- Linux: WSL2 Ubuntu-24.04 in the copy `docs/evidence/m1c-blackbox/wsl.sh sync` keeps in `/var/tmp/spar-m1c-blackbox/repo`
  (written `<copy>`), with that script's caches, `HOME` and XDG folders, private Xvfb, the core limit 1 byte.
- Microsoft.Data.SqlClient 7.1.2 from nuget.org through the package tests' `nuget.config` (Spar's packages from the run's
  feed, everything else from nuget.org by package source mapping), cached in `%TEMP%\spar-package-tests\nuget-packages`
  (WSL: the script's cache). Downloaded once per machine, by the first experiment on Windows (09:02 local time): the
  extracted `microsoft.data.sqlclient/7.1.2` folder is 34 MB and `microsoft.data.sqlclient.sni.runtime/7.1.2` 2.3 MB,
  with their dependencies (`System.Configuration.ConfigurationManager` 9.0.21 and the others) beside them. The CI
  package-test runners download them on each fresh runner.
- The scripts: [`m1d-runtime-choices/guide-win.sh`](m1d-runtime-choices/guide-win.sh) (Windows, `%TEMP%\spar-guide-m1d7`,
  window runs through `../m1d-hot-reload/WatchDesktop.cs` on a desktop of their own) and
  [`m1d-runtime-choices/guide-linux.sh`](m1d-runtime-choices/guide-linux.sh) (WSL, `/var/tmp/spar-guide-m1d7`, a private
  Xvfb :196), each `pack new aot globalization fd sf start… sql members`; both read the project blocks from the guide by
  their markers, and both write the SqlClient probe with [`m1d-runtime-choices/sql-probe.sh`](m1d-runtime-choices/sql-probe.sh),
  the same code as the package tests'. Paths in copied lines are written `%TEMP%` and `<copy>`.

## The publications (guide runs, from packages)

The template's app (`Notes`, without the end-to-end project), its project given each block from the guide, published
with `dotnet publish Notes -c Release -o <folder>`. Sizes without the debug symbols (`Notes.pdb`; `Notes.dbg` for
NativeAOT on Linux). Publish times are warm (the app built once before; the NativeAOT compile incremental).

| Shape | Windows | Linux |
|---|---|---|
| NativeAOT (default) | `Notes.exe` 4,745,216 B; publish 7.8 s | `Notes` 4,679,880 B; 5.8 s |
| NativeAOT, `InvariantGlobalization=false` | 4,963,328 B (+218,112); 4.1 s | 4,865,304 B (+185,424); 5.1 s |
| CoreCLR framework-dependent | 10 files, 1,812,344 B: `Notes.exe` (apphost, 74,752), `Notes.dll` (268,288), `Spar.dll`, `Spar.Windows.dll`, `Spar.Linux.dll`, the two `Microsoft.Extensions` DLLs, `Notes.runtimeconfig.json` (`Microsoft.NETCore.App` 11.0.0 only), `Notes.deps.json`, `WebView2Loader.dll` (194,912); 1.3 s | 9 files, 1,574,689 B (no loader; apphost 32,144); 1.9 s |
| CoreCLR self-contained single file (`IncludeNativeLibrariesForSelfExtract`) | `Notes.exe` alone, 81,168,450 B; 1.9 s | `Notes` alone, 78,956,833 B; 2.1 s |
| … without `IncludeNativeLibrariesForSelfExtract` (Windows, package test) | `Atlas.exe` + `WebView2Loader.dll` | not applicable |
| … compressed (`EnableCompressionInSingleFile`, experiment) | 41,156,342 B | not run |

Every publication's build identity was read by `dotnet spar doctor --app`: from the DLL beside a framework-dependent
apphost, and from the single file itself ("Build Production, no revision (not built in a git work tree), contract
fingerprint f0de0190c7341cfb" in each). The compressed bundle: "holds no build identity: … or it is compressed", a
warning.

**Before the SDK change**, the same single-file publish failed on Windows: `error NETSDK1236: The single-file bundle
generation task excluded files that were expected to be included: …\microsoft.web.webview2\1.0.4314.35\build\native\x64\WebView2Loader.dll`,
leaving `Notes.exe` alone and without its loader.

## Start to ui-ready

The host's `ui-ready at … ms` line (from the process's start), five starts each, median and range.

| Shape | Windows, guide run (offscreen desktop, sequential) | Windows, package tests (black box, other tests in parallel) | Linux, guide run (private Xvfb, sequential) | Linux, package tests (black box) |
|---|---|---|---|---|
| NativeAOT | 378 ms (335–441) | 417 ms (353–453) | 201 ms (194–253) | 149 ms (143–152) |
| CoreCLR framework-dependent | 423 ms (399–454) | 594 ms (487–667) | 263 ms (248–273) | 228 ms (218–284) |
| CoreCLR single file | 441 ms (389–471) | 639 ms (572–5,645) | 265 ms (261–269) | 223 ms (218–294) |

- The guide's table uses the guide runs (one process at a time). The package tests' numbers were taken while other
  consumers built in parallel collections, so they spread more.
- The single file's 5,645 ms was one start, the first of the five in that run (the app's first start after the test's
  publication, with a new data root and so a new `TEMP` to extract into); the four after it took 572–691 ms. Not
  isolated; the first start of a new 81 MB executable is the likely cost (not measured further).
- **Extraction:** on Windows each guide start of the single file extracted `WebView2Loader.dll` (194,912 B) into
  `<DOTNET_BUNDLE_EXTRACT_BASE_DIR>\Notes\Cqi11AodVJyF\`; on Linux nothing was extracted.
- **The loader on `PATH`:** with the Windows Performance Toolkit's folder on `PATH`, a single file published without the
  loader beside it (before the SDK change) still started (ui-ready 402.6 ms); without that folder it stopped at once
  with `Start-up problem Missing: WebView2 loader. The app's file WebView2Loader.dll is missing. Install the app again.
  (Dll was not found.) Exit code 3.` The package tests' and the guide's starts remove such folders from `PATH`.

## Accepting SqlClient's warnings, and the SqlClient recipe

A new app (`Ledger`, the template's app with the probe of [`sql-probe.sh`](m1d-runtime-choices/sql-probe.sh)), with the
guide's blocks, published with NativeAOT. Identical on both OSes:

| Project | Publish | Diagnostics |
|---|---|---|
| package + culture + single-file blocks (no accepted warnings) | fails (exit 1; ILC, then MSB3073) | `error IL2104` Microsoft.Data.SqlClient, `error IL2104` System.Configuration.ConfigurationManager, `error IL3053` Microsoft.Data.SqlClient |
| … + the `accept-aot-warnings` block (the recipe) | passes (exit 0) | the same three as `warning`, printed in the publish's output |
| the recipe with `-p:TrimmerSingleWarn=false` | fails, as the guide says | 31 distinct per-member errors: IL2026 ×12, IL2057 ×6, IL2067 ×3, IL2070 ×3, IL2072 ×4, IL2075 ×2, IL3050 ×1 (`SqlAuthenticationProviderManager`, `SqlConfigurableRetryLogicLoader`, `System.Configuration.TypeUtil`, the UDT helpers, `SqlDiagnosticListener`, `SqlVector`) |

The spike's larger app had 4 assembly-level warnings (also `Microsoft.Data.SqlClient.Internal.Logging`) and 49
per-member ones; this one reaches less.

The recipe's publish folder is the executable alone (Windows `Ledger.exe` 15,043,072 B, Linux `Ledger` 15,239,176 B,
besides `Ledger.pdb`/`Ledger.dbg`). Its probe, `Ledger --sql-probe <port>` against a loopback port where nothing
listens:

| Run | Windows | Linux |
|---|---|---|
| the recipe's executable | `SqlException` "A network-related or instance-specific error occurred … (provider: TCP Provider, error: 35 - An internal exception was caught)", inner `Win32Exception: The connection attempt timed out.`; exit 0 in 3.1–3.4 s (the 3 s `Connect Timeout`) | `SqlException` (the same text), inner `SocketException: Success`; exit 0 in 2.5 s |
| … with `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` | `NotSupportedException: Globalization Invariant Mode is not supported.`; exit 2 in 0.05–0.3 s | the same; 10 ms |
| without the single-file block (Windows): its folder | `Ledger.exe` 15,120,384 B + `Microsoft.Data.SqlClient.SNI.dll` 555,416 B; `SqlException` (TCP Provider, error: 0 - The wait operation timed out) | not applicable (SqlClient always uses managed networking there) |
| … that executable copied alone | `TypeInitializationException` → `TypeInitializationException` → `DllNotFoundException: Unable to load DLL 'Microsoft.Data.SqlClient.SNI.dll' or one of its dependencies`; exit 2 in 0.09 s | not applicable |

**An incident in the first guide run (Windows):** the `sql` step's path to `sql-probe.sh` was relative to a folder the
step had left, so the probe was not written; the app published without it, and `Ledger.exe --sql-probe 1` opened its
window on the user's desktop. The user closed it; the second start (invariant mode) was still open and was ended
(`Stop-Process`, this run's own process). That run is not evidence. The script now uses its own folder's absolute path,
refuses to start the executable unless `Program.cs` has the probe and the recipe's publish succeeded, and gives each
probe run 30 s; the package tests' probe runs have a 30 s limit after which the process tree is ended. The rerun is the
table above.

## Spar's test and development code in a CoreCLR publication

`grep -a` in the guide run's framework-dependent folder (Windows): `SparTestSession`, `DevServer` and `BackendUpdates`
are in `Spar.dll`, `Spar.Windows.dll` and `Spar.Linux.dll`; `Spar.TestHost.IsSupported` (with the other switches, all
`false` in `Notes.runtimeconfig.json`) in `Spar.dll`; `SparLoopbackHost` in none. The feature switches turn that code off
at run time; only NativeAOT (or trimming) removes it. The production-boundary test checks the NativeAOT executable. For
the user to decide (decision "Runtime choices (implementation record; choices for review; 2026-11-23)"): accepted for now (decision "Step 7's two findings").

## What the new package tests cost

From the runs' `timings.txt` (Windows, warm caches; Linux in parentheses, where the first publishes also restored
runtime packs and installed npm packages):

| Test class | Its commands |
|---|---|
| `FrameworkDependentTests` (2) | `dotnet new` 0.4 s; publish 7.5 s (40 s); doctor 0.7 s; the black-box run (build, the app's publication, 6 launches) 11.7 s (63 s) |
| `SingleFileTests` (4) | `dotnet new` 0.4 s; publish 8.3 s (50 s); the fast tests 3.3 s (3.0 s); the publish without self-extraction 1.3 s (Windows only); the black-box run 17.4 s (49 s) |
| `SqlClientTests` (3) | `dotnet new` 0.4 s; the failing publish 17.6 s (7.9 s, after the recipe's); the recipe's publish 8.3 s (58.5 s, the first); the native-SNI publish 9.3 s (Windows only); each probe 3.1 s (2.5 s) |
| `GuideSnippetTests` (1) | reads the page |
| `TemplateTests`' black-box test | five more launches, about 3 s |

They run as collections of their own, beside the main consumer's, so the suite's time grew less than their sum.

## Runs

| Run | Windows | Linux (WSL) |
|---|---|---|
| `dotnet build Spar.slnx` | 0 warnings, 0 errors (55 s) | not run (only the package tests' own builds) |
| CoreCLR tests (`--filter-not-trait Surface=Window`, `Surface=BlackBox`) | 1,237: 1,213 passed, 23 skipped, 1 failed: the Notebook's browser journey `A_taken_or_invalid_title_is_explained_in_the_form`, a Playwright 30 s timeout at its second `CreateAsync` under the solution run's load; it passed alone (2 of 2 with its theory) and in three runs of the Notebook's browser tests (7 of 7 each). Not touched by this step | not run |
| `Spar.WindowTests`, `Surface=BlackBox` | 88: 21 passed, 67 skipped (their own skips on this machine, as in earlier runs) | not run |
| `Notebook.EndToEndTests`, `Surface=BlackBox` | 6 of 6 (the executable alone beside the tests, with the new copy) | not run |
| `Spar.BuildTests` | 7 of 7 (2 min 0 s) | not run |
| `Spar.PackageTests` | 38 of 38: 2 min 37 s and 3 min 16 s (before this step: 28 in 2 min 23–30 s); an earlier run failed `SingleFileTests`' start with NETSDK1151, fixed by the SDK change | 38: 36 passed, 2 skipped (Windows-only), 3 min 30 s and 3 min 38 s, 0 processes left |
| `eng/check-size.cs` | 630 files, 0 errors, 0 warnings | |
| The guide's commands from packages | `guide-win.sh pack new aot globalization fd sf start×3 sql members` (the `sql` step rerun after the incident above) | `guide-linux.sh` the same steps |

The Windows window and black-box starts ran on offscreen desktops (the black box's hidden desktop, `WatchDesktop.cs`'s
desktop of its own), except the incident's two probe starts. After every run no app process of this step was left
(`tasklist`; WSL: the census, 0).
