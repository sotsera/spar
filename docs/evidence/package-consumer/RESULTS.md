# Package-consumer spike: Spar from packages only

> A spike. Its code was deleted with `spikes/` at the start of M2; it is in git history at [`spikes/package-consumer/`](https://github.com/sotsera/spar/tree/24f5db0942833047c81fa692908410596f3b8960/spikes/package-consumer) (commit `24f5db0`), and this record and its `results/` moved here. Paths under `spikes/` below name that history.

Recorded 2026-11-13 on the development machine: Windows 11 x64, SDK 11.0.100 (MSBuild 18.12, Roslyn 5.12), Visual Studio 18 Community `MSBuild.exe`. Generator and tasks build against `Microsoft.CodeAnalysis.CSharp` 5.9.0 and `Microsoft.Build.*` 18.12.2, as in D1.

**Question.** Can an app consume Spar purely from packages, from a folder with no repository ancestor, created by `dotnet new`, and keep working across a package update with a warm build server?

**Answer: yes.** Every step below ran for real in three scripted runs plus one manual run, with no failures in the scripted runs. One hazard was found: reusing a package version while a build server is warm (see "Failures").

## What was built

The generator, runtime, tasks and `ClientBuilder` are copies of `spikes/d1-pipeline`. The only code changes are a version stamp (`GeneratorInfo`, `SparRuntimeInfo`) and a task log line that names the folder each assembly was loaded from.

| Project | Package | Role |
|---|---|---|
| `src/Spike.Generator` | none (shipped inside `Spike.Sdk`) | Incremental generator. Also emits `SparCatalog.GeneratorVersion` and puts the version in the TypeScript header. |
| `src/Spike.Sdk` | `Spike.Sdk` (`PackageType=MSBuildSdk`) | The task assembly (`Spike.Tasks.dll`), packed as an MSBuild SDK: `Sdk/Sdk.props`, `Sdk/Sdk.targets`, and `tools/`. |
| `src/Spike.Runtime` | `Spike.Runtime` | Attributes, `SparJson`, `SparRuntimeInfo`. |
| `src/Spike.Templates` | `Spike.Templates` (`PackageType=Template`) | `dotnet new spar-app`: csproj, `Program.cs`, `NotesService.cs`, `frontend/`, `Properties/launchSettings.json` with `Desktop` and `Loopback` profiles. |

- `run-consumer.ps1` repeats everything: pack, isolated consumer, all checks, measurements, process cleanup and removal of the temp area. Switches: `-SkipPack`, `-SlimRestore`, `-Probe` (adds the `MSBuildWorkspace` probe), `-Keep`.
- `ide-probe.cs` is D1's probe, unchanged. It must run outside the repository (see failure 2), so the script copies it into the temp area.
- The feed is `artifacts/package-consumer/feed/` (root `artifacts/`, gitignored, disposable).

### SDK layout

```text
Spike.Sdk.0.1.0.nupkg  3,699,907 bytes (14.0 MB extracted)
  Sdk/Sdk.props                      994   imports Microsoft.NET.Sdk props, app defaults, implicit Spike.Runtime reference
  Sdk/Sdk.targets                  3,699   imports Microsoft.NET.Sdk targets, UsingTask Runtime="NET", Analyzer item, pipeline
  Sdk/Spike.Sdk.Version.props         98   written at pack time from $(Version) (obj/, WriteOnlyWhenDifferent)
  tools/Spike.Tasks.dll           30,208
  tools/Spike.Tasks.deps.json      2,286
  tools/Spike.Generator.dll       52,224   the only copy: loaded by the tasks and passed to Csc as /analyzer
  tools/Microsoft.CodeAnalysis.dll           3,103,056
  tools/Microsoft.CodeAnalysis.CSharp.dll    7,079,208
Spike.Runtime.0.1.0.nupkg  4,991 bytes: lib/net11.0/Spike.Runtime.dll (7,168)
Spike.Templates.0.1.0.nupkg  5,241 bytes: content/spar-app/{.template.config/template.json, SparApp.csproj, Program.cs,
                                          NotesService.cs, Properties/launchSettings.json, frontend/index.html, frontend/main.ts}
```

- **Generator shipped once.** A package resolved through `<Project Sdk="...">` is not a `PackageReference`, so NuGet's `analyzers/` convention does not apply. `Sdk.targets` adds `<Analyzer Include="$(_SparToolsDirectory)Spike.Generator.dll" />` itself. The task assembly finds the same file beside it, because MSBuild resolves a task's dependencies from the task's own folder. The compiler server and `MSBuildWorkspace` both loaded the generator from `tools/`, next to Roslyn 5.9 DLLs, without warnings.
- **Roslyn is 99% of the package** (10.18 of 10.27 MB uncompressed). The tasks need it for client analysis. Satellite resources are dropped with `SatelliteResourceLanguages=en`.
- **Runtime version lockstep.** `Sdk.props` adds `<PackageReference Include="Spike.Runtime" Version="$(SparSdkVersion)" IsImplicitlyDefined="true" />`. Changing the SDK version changes the runtime version too. `IsImplicitlyDefined` lets a consumer that uses central package management accept it (not exercised here: the consumer has no CPM).
- **SDK update invalidates the client.** The inventory gains a `sdk=<version>` line, so an SDK update regenerates the TypeScript client even when no source changed.
- **App defaults in `Sdk.props`:** `OutputType=Exe`, `Nullable`, `ImplicitUsings`, `PublishAot=true`. The template csproj sets only `TargetFramework`, `RuntimeIdentifier=win-x64`, `InvariantGlobalization` and `TreatWarningsAsErrors`.
- **Pack warnings:** none. NU5100 is not raised for `tools/`. NU5128 does not appear, because of `SuppressDependenciesWhenPacking`. The only message is "missing a readme".

## Isolation

- **Consumer area:** a new folder `%TEMP%/spar-pc-<timestamp>/`. The script checks that no ancestor has `.git`, `Directory.Build.*`, `Directory.Packages.props`, `global.json` or `nuget.config`. SDK selection outside the repository resolves to 11.0.100, the newest installed SDK.
- **NuGet:**
  - `apps/nuget.config` has `<clear />`, the local feed and nuget.org, with package source mapping (`Spike.*` from the local feed only).
  - `NUGET_PACKAGES` and `NUGET_HTTP_CACHE_PATH` point inside the temp area.
  - The NuGet SDK resolver honoured both: `Spike.Sdk` was extracted into the isolated folder before evaluation.
- **Templates:** installed only with `--debug:custom-hive <temp>/hive`. `%USERPROFILE%\.templateengine` has no `Spike` entries before or after.
- **MSBuild:**
  - `MSBUILDNODEHANDSHAKESALT` is set per run, with separate salts for packing and the consumer. This gives the spike its own MSBuild server and nodes, and a cold server for the consumer's first build.
  - At the end, the script stops only processes descended from the ones it started, excluding `VBCSCompiler`.
  - In .NET 11, `dotnet build` and `dotnet publish` go through the MSBuild server (`MSBuild.exe /nodemode:8`). `dotnet msbuild` does not.

## Results

Three scripted runs: run 1 default; run 2 `-SlimRestore -Probe`; run 3 default, starting from clean spike `bin/` and `obj/`. All times are wall clock for the whole CLI command. The shared compiler server (`VBCSCompiler`) was already running in every run.

| Step | Run 1 | Run 2 (slim) | Run 3 |
|---|---:|---:|---:|
| `dotnet pack` 0.1.0, fresh MSBuild server | 9.3 s ¹ | 4.0 s | 4.5 s (clean `bin/obj`) |
| `dotnet pack` 0.1.1, warm | 2.4 s | 2.1 s | 2.0 s |
| `dotnet new install` (custom hive) | 1.6 s | 1.7 s | 1.6 s |
| `dotnet new spar-app` | 0.6 s | 0.7 s | 0.6 s |
| **Cold restore** (empty package folder and HTTP cache, downloads from nuget.org) | **62.3 s** | **36.4 s** | **55.4 s** |
| First build, fresh MSBuild server | 2.2 s ² | 2.5 s | 2.5 s |
| No-op `dotnet build` (×3) | 0.49–0.62 s | 0.67–0.94 s | 0.67–0.73 s |
| `dotnet run -lp Loopback`, up to date | 1.7 s | 1.8 s | 1.8 s |
| Design-time compile (0.1.0 / 0.1.1) | 0.40 / 0.42 s | 0.44 / 0.43 s | 0.42 / 0.43 s |
| `dotnet publish` NativeAOT | 10.4 s | 10.9 s | 10.7 s |
| `NotesApp.exe` | 5,266,432 bytes | same | same |
| Update build to 0.1.1, warm | 2.05 s | 2.12 s | 2.10 s |
| VS `MSBuild.exe -restore` build | 3.4 s | 3.6 s | 3.5 s |

¹ The first scripted pack after the manual runs, which had left different versions in `bin/obj`; not comparable.
² In run 1, packing and the consumer shared a salt, so this "first build" reused the warm server from packing. Runs 2 and 3 used separate salts.

- **Cold restore contents** (default template). Restore is network-bound and the downloads are almost all .NET's own packs:

  | Package | Bytes |
  |---|---:|
  | `microsoft.netcore.app.runtime.win-x64` | 160,972,876 |
  | `microsoft.windowsdesktop.app.runtime.win-x64` | 138,525,692 |
  | `microsoft.netcore.app.runtime.nativeaot.win-x64` | 121,051,141 |
  | `runtime.win-x64.microsoft.dotnet.ilcompiler` | 45,149,503 |
  | `microsoft.aspnetcore.app.runtime.win-x64` | 43,478,362 |
  | `spike.sdk` | 13,972,406 |
  | `microsoft.net.illink.tasks` | 5,424,399 |
  | `microsoft.dotnet.ilcompiler` | 461,019 |
  | `spike.runtime` | 12,891 |

- **Slim restore.** `DisableTransitiveFrameworkReferenceDownloads=true` drops the ASP.NET Core and Windows Desktop runtime packs (182 MB). Cold restore took 36.4 s against 55–62 s, with one sample each. The app still built, ran and published.
- **Manual run, for comparison:** cold restore 84 s, which wrote to the user's HTTP cache (failure 3). A clean build after stopping the spike's MSBuild server took 4.1 s; no-op builds took 0.73–0.88 s.

### Functional checks (observed in every scripted run)

- **Template:** `dotnet new spar-app -n NotesApp` creates `NotesApp.csproj` with `<Project Sdk="Spike.Sdk/0.1.0">`, renames the namespace, and adds `Properties/launchSettings.json`.
- **`dotnet run`:**
  - With no profile it uses `Desktop`: `host=desktop runtime=0.1.0 generator=0.1.0`, a `notes.save`/`notes.count` round trip, and the embedded frontend listing.
  - `dotnet run -lp Loopback` serves `index.html` from the embedded zip on `127.0.0.1:0` and fetches it: `loopback GET /index.html -> 143 chars`.
- **TypeScript client** under `obj/Debug/net11.0/win-x64/spar/client/`:
  - files: `contracts.json`, `notes.ts`, `notes.js`, `transport.ts`, and `packages/@spar/{sdk,notes}/{index.ts,package.json}`;
  - `notes.ts` starts with `// Generated by Spar 0.1.0. Do not edit.`
- **Generator diagnostic:**
  - A command taking `Dictionary<string, int>` fails the build with `NotesService.cs(25,55): error SPIKE003: Type 'System.Collections.Generic.Dictionary<string, int>' is not a supported wire type.`
  - The whole contract drops out of the client (0 contracts), which is D1's known behaviour and an M1 fix.
- **Incremental behaviour:**
  - No-op builds run no Spar task.
  - The client task reuses all 177 metadata references when the server is warm (22–68 ms, against 0.8–1.0 s cold).
  - The reference cache is shared across projects in the same server.
- **Design-time compile.** `dotnet msbuild -t:Compile -p:DesignTimeBuild=true -p:BuildingInsideVisualStudio=true -p:SkipCompilerExecution=true -p:ProvideCommandLineArgs=true`:
  - succeeds;
  - passes `/analyzer:...\packages\spike.sdk\<version>\tools\Spike.Generator.dll` and `/reference:...\spike.runtime\<version>\lib\net11.0\Spike.Runtime.dll`;
  - leaves `client.stamp` untouched.
- **`MSBuildWorkspace` probe** (run 2, and the manual run; the design-time path both IDEs use):
  - opens the package-only consumer in about 2.2 s, with `Spike.Generator` among its analyzers;
  - all generated files are present, and no Spar task runs;
  - an in-memory `PingAsync` regenerates the binding;
  - an in-memory unsupported type reports `SPIKE003` at `NotesService.cs(25,59)`.
- **NativeAOT publish:** `TreatWarningsAsErrors`, zero warnings, a 5.27 MB exe (the loopback smoke's `HttpClient` included). The exe ran the loopback smoke.
- **Visual Studio's `MSBuild.exe`** (.NET Framework, `-nr:false`): the `Runtime="NET"` task ran in a .NET task host loaded from the package folder, and the design-time compile succeeded.

### Package update with a warm server

The consumer switched from `Spike.Sdk/0.1.0` to `0.1.1` by editing the csproj, then ran `dotnet build` against the warm server.

- **The new task loads.** The task logged `spike-task client 0.1.1 ... pid <same pid as the 0.1.0 build> ... from ...\spike.sdk\0.1.1\tools`. The generator in the warm compiler server is the new one too: the app prints `runtime=0.1.1 generator=0.1.1`, and `notes.ts` has the 0.1.1 header.
- **No locks.** No MSB3027, MSB3021 or MSB3026. Each version has its own immutable folder, so nothing is overwritten.
- **Old and new stay loaded side by side.**
  - Switching back to 0.1.0 in the same server used the still-loaded 0.1.0 task, with its warm cache (177 reused).
  - The first 0.1.1 build ran the client step cold (0 reused, about 0.9 s), because the reference cache is a static of each task-assembly version.
  - Old versions stay in memory until the server exits.
- **The design-time compile works after the update** and passes the 0.1.1 generator.
- **`global.json` `msbuild-sdks`** (`<Project Sdk="Spike.Sdk">` plus `{"msbuild-sdks": {"Spike.Sdk": "0.1.0"}}`) resolves the same way. Editing `global.json` to 0.1.1 while warm switched the task, generator and runtime on the next build.

**`TaskHostFactory` is not needed for a package consumer.** With `Runtime="NET"` alone, the task ran in-process in the MSBuild server under `dotnet build`, and in the .NET task host under VS `MSBuild.exe`. The locking problem D1 found needs the task DLL to be rebuilt in place, and a package version folder never is. That is, unless someone reuses a version (failure 1).

### `Sdk="Name/version"` against `global.json`

| | Versioned `Sdk` attribute | `global.json` `msbuild-sdks` |
|---|---|---|
| Works from `dotnet new` with nothing else | yes | needs a `global.json` beside or above the project |
| Warm version switch | yes | yes |
| App and test project in lockstep | two edits | one edit |
| Other effects | none | `global.json` also controls SDK selection and test-runner settings for everything below it |

## Failures and how they were resolved

1. **Reusing a package version while a server is warm** (manual run, deliberately provoked).
   - **What happened.** Deleting `packages/spike.sdk/0.1.0` while the MSBuild server had the tasks loaded **failed partway**. `Microsoft.CodeAnalysis.CSharp.dll` was locked ("Access to the path ... is denied"), but `Sdk/` had already been deleted.
   - **Masked by the warm server.** The warm server then reported `dotnet build` **succeeded** (0.2 s, everything up to date), even though `Sdk/Sdk.props` no longer existed.
   - **A fresh evaluation failed.** `dotnet msbuild -getProperty:...` reported `MSB4019: The imported project "...\spike.sdk\0.1.0\Sdk\Sdk.props" was not found`. NuGet still treated the package as installed because `.nupkg.metadata` remained.
   - **Fix.** Stop the spike's own salted server and its nodes, delete the folder, and restore. This is how a developer would hit it: repacking the same version and clearing the cache to pick it up.
   - **Rule:** never reuse a package version. Every local pack gets a new version.
2. **`ide-probe.cs` would not run inside the repository.** NU1008: the repository's `Directory.Packages.props` enables central package management, which rejects `#:package Name@version`. Fixed by copying the probe into the temp area and running it there.
3. **Isolation gap: the HTTP cache.**
   - `NUGET_PACKAGES` alone does not isolate NuGet's HTTP cache. The manual restores (and the manual probe run) downloaded into the user's `%LOCALAPPDATA%\NuGet\v3-cache`: .NET 11 runtime and ILCompiler packs, and the Roslyn Workspaces packages with their dependencies.
   - The user's global packages folder and NuGet configuration were not changed.
   - A second slim restore then took 2 s, from that HTTP cache, which made "cold" numbers meaningless.
   - Fixed in the script with `NUGET_HTTP_CACHE_PATH`. The cache entries already written were left in place: they are ordinary download-cache files.
4. **A "cold" first build reused a warm server.** In run 1, packing and the consumer shared one handshake salt. Fixed with separate salts.
5. **Tooling only:** a bash heredoc with an apostrophe failed to write the project files, and `python` is not installed. The files were written with the editor tools and `sed` instead. These had no effect on results.

## Observed, skipped, not tested

**Observed** (by running): everything under "Results", in the three scripted runs (`run-consumer.ps1`) and the manual run.

**Skipped:**
- A real frontend toolchain (npm, tsc, Vite). The frontend is two static files, and the generated client is not type-checked here.
- Package signing, symbols packages and package readmes.
- Restoring from a feed that requires authentication.

**Not tested:**
- `TaskHostFactory` inside a package. It was never needed, so its cost and lock behaviour there are inferred from D1, not measured.
- The real Visual Studio and VS Code windows (only `MSBuildWorkspace`, the headless design-time compile and VS `MSBuild.exe` from the command line), and whether VS's long-lived pooled task host locks an old package version during an update.
- A consumer with central package management (`IsImplicitlyDefined` is untested there).
- An app plus a test project sharing one SDK version.
- `dotnet watch` and `dotnet test`.
- An unreachable feed or a missing version; the resolver's error message was not captured.
- Whether dropping `RuntimeIdentifier` from the csproj avoids the 161 MB `microsoft.netcore.app.runtime.win-x64` download.
- Whether the tasks could use the SDK's own Roslyn instead of shipping 10 MB of it.
- `-mt` builds of the consumer.
- How long the idle MSBuild server keeps old task versions in memory.

## Requirements for `Spar.Sdk` in M1

1. **Layout.**
   - `Sdk/Sdk.props` and `Sdk/Sdk.targets` wrap `Microsoft.NET.Sdk`.
   - One `tools/` folder holds the task assembly, the generator and analyzer, and their Roslyn dependencies.
   - `Sdk.targets` adds the generator from `tools/` as an `Analyzer` item, because MSBuild SDK packages get no `analyzers/` convention. Each binary ships once.
   - `IncludeBuildOutput=false`, `SuppressDependenciesWhenPacking=true`, `PackageType=MSBuildSdk`.
2. **Tasks** are declared `Runtime="NET"` only for package consumers. `TaskHostFactory` stays limited to Spar's own source checkout, as already decided.
3. **Versions.**
   - The SDK knows its own version from a pack-generated props file.
   - Runtime packages are implicit `PackageReference`s at exactly that version, marked `IsImplicitlyDefined`.
   - The SDK version is an input to the client step.
4. **Never reuse a version.** Local and CI packs use unique versions (for example a `-dev.<n>` suffix). Docs say an SDK update is a version change in the csproj or `global.json`, never a cache clear.
5. **The package-only consumer test from M1's first day** is a script or test that:
   - isolates `NUGET_PACKAGES`, `NUGET_HTTP_CACHE_PATH`, the template hive (`--debug:custom-hive`) and the MSBuild handshake salt;
   - checks for inherited files;
   - covers `dotnet new`, restore, build, no-op, `run` with both profiles, a diagnostic, the client under `obj/`, design-time compile, AOT publish, an update and a downgrade while warm;
   - stops only the processes it started.
6. **The template** ships `Properties/launchSettings.json` with `Desktop` (first, so it is the default) and `Loopback`.
   - Decide in M1 whether the template pins the SDK in the csproj (self-contained, works anywhere) or in a solution-level `global.json` (one edit for app and tests).
7. **Restore weight is .NET's, not Spar's.** Spar adds 3.7 MB, against 330–515 MB of runtime and ILCompiler packs.
   - Consider defaulting `DisableTransitiveFrameworkReferenceDownloads=true` in `Spar.Sdk` (-182 MB, -20 to -26 s cold, in one sample each).
   - Measure what `RuntimeIdentifier` costs at build time.
   - Record either choice in `docs/decisions.md` with numbers.
8. **Package size** is dominated by Roslyn (10.18 of 10.27 MB uncompressed). If size matters, test loading the SDK's own Roslyn for the client task, or moving client generation to a path that does not need a second Roslyn.
