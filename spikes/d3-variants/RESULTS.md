# D3 spike: variant builds in-proc (MSBuild task) versus nested `dotnet` (Exec)

Question (plan D3): can a test project build the app in another variant (extra `#if` code, and a NativeAOT
publication) with the in-proc `MSBuild` task and explicit global properties, instead of a nested
`dotnet build/publish`, without disturbing the normal build? Measured both.

**Answer: yes.** On SDK 11.0.100 the in-proc call worked for Build and AOT Publish, kept every output in the
variant's folders, left the normal build a no-op and was faster in every case. Neither mode is safe when two
*processes* build the same variant at once: a cross-process lock is needed either way. A lock file held with
`FileShare.None` fixed all 20 runs.

## Environment

- Windows 11 Pro 10.0.26320, i9-13900K (32 logical), 96 GB.
- .NET SDK 11.0.100, MSBuild 18.12.2. Global packages already cached, apart from the win-x86 probe.
- Other sessions were building on the machine at the same time, so the numbers carry some noise.
- **Isolation.** Every script sets a private `MSBUILDNODEHANDSHAKESALT` and a private `SharedCompilationId`.
  These builds therefore neither used nor left MSBuild server, worker nodes or a compiler server for other
  sessions.
  - A Windows job object did not work: the host's job allows silent breakaway, so children never joined it.
  - Instead, `scripts/Isolate.ps1` starts every `dotnet` itself and, at the end, stops the tree descended from
    those processes (found by parent id and start time).
  - `dotnet build-server shutdown` was never run. After the last run, no process from this spike was left;
    checked by the compiler pipe names `d3-*` and the process trees.

## What was built

| Path | Contents |
|---|---|
| `Directory.Build.props` | Imports the repository props. Defines `SparVariant` (Production by default; Production, Development or DesktopTest, otherwise an error). Sets `BaseIntermediateOutputPath=obj\<variant>\` and `BaseOutputPath=bin\<variant>\` before the SDK props, adds `SPAR_VARIANT_<X>` to `DefineConstants`, and excludes `bin\**;obj\**` from the default globs. |
| `Directory.Packages.props` | Imports the repository's central versions (xunit.v3). |
| `App/` | Portable exe with `PublishAot=true`, no RuntimeIdentifier. `DesktopTestHooks.cs` exists only under `#if SPAR_VARIANT_DESKTOPTEST` and holds the marker `D3MARKER-DESKTOPTEST-6b1f0c`. |
| `Variant.targets` | Imported by the test projects through `Directory.Build.targets`. After `Build`, it builds the app's DesktopTest variant. **InProc:** an `MSBuild` call to `Restore` with an extra `MSBuildRestoreSessionId`, then a second call to `Build`/`Publish`. **Exec:** `dotnet build/publish` with the same `-p:` values. Options: `D3RestoreSession=Guid\|Fixed`, and `D3Lock=true` for a cross-process lock. Skipped when `DesignTimeBuild=true`. |
| `Tests/`, `Tests2/`, `D3.slnx` | Two xunit.v3 (MTP) test projects. `Tests` runs the variant exe and checks its output. |
| `Probe.proj` | Probes: missing restore, different RID, restore and build in one evaluation, the same call twice. |
| `scripts/` | `verify.ps1`, `measure.ps1`, `first-build.ps1`, `parallel.ps1`, `observe.ps1`, `run.ps1` and `Isolate.ps1`. |

The global properties of the variant build were identical in both modes:

- Build: `SparVariant=DesktopTest;Configuration=Debug`.
- Publish: `SparVariant=DesktopTest;Configuration=Release;RuntimeIdentifier=win-x64;PublishAot=true;_IsPublishing=true`.
- InProc also passes `RemoveProperties="TargetFramework;RuntimeIdentifier;SelfContained;D3…"`.

## Verification (observed, `scripts/verify.ps1`, both modes, all pass; repeated on the final files)

| Check | InProc | Exec |
|---|---|---|
| Production build; test build plus variant Build; test build plus variant AOT Publish | pass | pass |
| Outputs only in `App/obj/<variant>/` and `App/bin/<variant>/`, including restore output (`project.assets.json` per variant) | pass | pass |
| Production files not rewritten by the variant builds (write-time snapshot) | pass | pass |
| Marker (UTF-16 or UTF-8 scan) in the variant dll and the AOT exe; absent from the Production dll and the Production AOT exe | pass | pass |
| AOT variant exe prints `variant: DesktopTest` and the marker; the Production exe doesn't | pass | pass |
| `dotnet build App` afterwards: CoreCompile skipped, no Production file rewritten | pass | pass |
| Second test build (Build and Publish): CoreCompile, IlcCompile and LinkNative skipped, no variant file rewritten | pass | pass* |
| Design-time `-t:Compile -p:DesignTimeBuild=true` of App succeeds; `-getItem:Compile` lists sources only | pass | pass |
| Design-time Compile of Tests doesn't build the variant, and design-time builds touch no `bin` or variant file | pass | pass |
| `dotnet test --project Tests` (runs the AOT variant exe) | pass | pass |

\* In Exec mode the nested build logs quietly, so for Exec the "skipped" evidence is the write-time snapshot only.

Both AOT exes are 934,400 bytes. Size is section-aligned; the marker scan is the proof.

**Failure the glob exclusion prevents.** Without `bin\**;obj\**` in `DefaultItemExcludes`, the SDK excludes only
the *current* variant's `bin`/`obj`. The Production build then compiled `obj\DesktopTest\...\App.AssemblyInfo.cs`
and `.AssemblyAttributes.cs`, and failed with CS0579 (duplicate assembly attributes). Reproduce with
`-p:D3KeepVariantGlobs=true`.

## Timings

Wall time of `dotnet build Tests -v:q` with the variant built after it. The test project itself is up to date.
"+" is the time added over the same build with no variant (`D3VariantMode=None`). Medians, with [min–max].

### Warm MSBuild server (`scripts/measure.ps1`, 5 repetitions, modes alternated)

- **cold:** variant `obj/` and `bin/` deleted.
- **warm:** unchanged repeat.
- **edit:** one app source changed.

| Operation | State | InProc | Exec | Exec − InProc |
|---|---|---|---|---|
| (none) | base | 0.55 [0.49–0.73] | | |
| Build | cold | 0.79 [0.74–3.01] (+0.24) | 2.76 [2.64–2.80] (+2.21) | 1.97 |
| Build | warm | 0.65 [0.61–0.97] (+0.10) | 2.23 [2.21–2.37] (+1.68) | 1.58 |
| Build | edit | 0.73 [0.67–1.02] (+0.18) | 2.44 [2.32–2.48] (+1.89) | 1.71 |
| AOT Publish | cold | 6.14 [6.10–6.52] (+5.60) | 8.35 [8.28–8.55] (+7.81) | 2.21 |
| AOT Publish | warm | 1.10 [1.05–1.17] (+0.56) | 2.84 [2.76–2.96] (+2.30) | 1.74 |
| AOT Publish | edit | 2.39 [2.29–2.53] (+1.85) | 4.17 [4.10–4.27] (+3.63) | 1.78 |

The in-proc maxima (3.01 s and 1.02 s) are the first variant build in a new server: NuGet restore and the new
configuration load for the first time in that process. Exec pays this on every call.

### Fully cold: the first build of a new isolated session (`scripts/first-build.ps1`, 3 repetitions)

Each run starts a new MSBuild server and a new compiler server, with the variant's `obj/` and `bin/` deleted.

| Operation | None | InProc | Exec |
|---|---|---|---|
| Build | 1.77 [1.73–1.82] | 4.16 [4.06–4.20] (+2.39) | 5.56 [5.42–5.58] (+3.79) |
| AOT Publish | 1.75 [1.74–1.80] | 9.64 [9.62–9.66] (+7.89) | 11.27 [11.10–11.28] (+9.52) |

### `-mt` (`measure.ps1 -Arguments -mt`, 3 repetitions)

Same as `-m` within noise. All runs passed.

| | Build cold / warm / edit | AOT Publish cold / warm / edit |
|---|---|---|
| InProc | 0.76 / 0.62 / 0.72 | 6.13 / 1.03 / 2.37 |
| Exec | 2.73 / 2.15 / 2.30 | 8.26 / 2.76 / 4.13 |
| base (none) | 0.48 | 0.52 |

**Reading.**
- Exec costs a roughly fixed 1.6–2.2 s more per variant build, cold or warm: a new `dotnet` process, SDK
  resolution, evaluation and JIT with no warm caches. Each warm AOT Exec also re-ran `vswhere`, the ILCompiler's
  toolchain discovery (seen in `observe.ps1`).
- In-proc, a no-op variant costs 0.10 s (Build) or 0.56 s (AOT Publish) on top of the test build.

## Concurrency (`scripts/parallel.ps1`, 5 repetitions per cell, variant `obj/` and `bin/` deleted before each run)

| Scenario | Build: failed runs | AOT Publish: failed runs | Notes |
|---|---|---|---|
| One solution build `-m`, InProc, Guid restore session | 0/5 | 0/5 | Binlog: one `IlcCompile`, so the identical Publish calls were deduplicated by the results cache. **Two `RestoreTask` runs overlapped** on the same `project.assets.json` (467 ms and 283 ms); both restores did work in 9/10 runs. No failure seen, but this is a race. |
| One solution build `-m`, InProc, fixed restore session | 0/5 | 0/5 | One restore per build: the identical Restore calls dedupe too. |
| One solution build `-mt`, InProc, Guid | 0/5 | 0/5 | Sidecar task hosts (`/nodemode:2`) appear. |
| One solution build `-m`, Exec | **5/5** | **4/5** | `CS2012` cannot open `obj\DesktopTest\...\App.dll` for writing; `MSB4018` IOException in `GenerateDepsFile` on a `bin\DesktopTest` file. |
| Two `dotnet build` processes, MSBuild server on, InProc | 0/5 | 0/5 | Not a real overlap: the second process can't use the busy server, starts cold and arrives after the first has finished (one restore). |
| Same, Exec | 0/5 | 0/5 | Same timing skew. |
| Two processes started alike (`DOTNET_CLI_USE_MSBUILD_SERVER=false`), InProc | **5/5** | **1/5** | `CS2012` on `obj\...\App.dll`; `MSB4018` IOException on a `bin\DesktopTest` file. |
| Same, Exec | **5/5** | **4/5** | `CS2012`, `MSB3030` (apphost copy), `MSB4018` (`GenerateDepsFile`). |
| Same, InProc, with `D3Lock=true` | 0/5 | 0/5 | The second process waited for the lock in 10/10 runs. Wall time 5.1–5.4 s (Build) and 11.1–11.3 s (Publish), including serialization and compiling the spike's inline lock task. |
| Same, Exec, with `D3Lock=true` | 0/5 | 0/5 | Waited in 9/10 runs. 7.7–9.8 s and 13.7–14.0 s. |
| One solution build `-m`, with lock (fixed session, and Exec) | 0/2 each | | No deadlock. The lock also serializes Exec inside one build. |

**The lock.** A `FileShare.None` `FileStream` on `obj/<variant>/variant.lock`:
- opened with retry (100 ms polls, timeout);
- registered with `IBuildEngine4.RegisterTaskObject` (lifetime `Build`);
- disposed by a release task after the variant build, or by MSBuild when the build ends.

It is wrapped around both the Restore and the Build/Publish calls.

## Probes and causes (`Probe.proj`)

| Probe | Result |
|---|---|
| Build an unrestored variant, no Restore call | `NETSDK1004` (assets file not found). Every variant needs its own restore: its assets live in `obj/<variant>/`. |
| Build with `RuntimeIdentifier=win-x86`, restored without it | `NETSDK1047` (no target for `net11.0/win-x86`). |
| The same after an in-proc `Restore` call with `RuntimeIdentifier=win-x86` | Works in-proc, but took 84.6 s because restore downloaded the win-x86 runtime packs (NETCore, AspNetCore, WindowsDesktop, NativeAOT runtime, ILCompiler) into the user's NuGet cache. A new RID does **not** force a nested process; it only needs the in-proc restore with that RID. |
| `PublishAot=true` with no RID | The SDK already restores the host RID (`win-x64` appears under `runtimes` in both variants' assets). A win-x64 AOT publish therefore needed no extra restore work; the restore call was a no-op. |
| `Restore;Build` in one call (identical global properties) | Succeeded here, but only because this app has no package build assets. That build reuses the evaluation made before restore wrote `nuget.g.props`/`.targets`, which is why the restore gets its own evaluation (`MSBuildRestoreSessionId`). The failure case wasn't constructed. |
| The same `Build` call twice in one build | The second call is served from the results cache (one "Done Building Project"). The same applies across test projects in one solution build. |
| `NETSDK1185` | Not seen. |
| `-mt` after a `-m` build in the same server, with the **inline** lock tasks | `MSB0001` internal error: "IOutOfProcTaskFactory Roslyn Code Task Factory created a task but returned null/empty assembly path". In a fresh server `-mt` with the same tasks passed. This is a `RoslynCodeTaskFactory` problem, not a D3 one; Spar's tasks are compiled. |

Nothing in this spike forced a nested process.

## Node, server and process behaviour (observed with `scripts/observe.ps1`)

- **MSBuild server by default.** `dotnet build` on SDK 11.0.100 uses the MSBuild server: `MSBuild.exe /nodemode:8`, the apphost, which persists after the build.
  - Its workers are `MSBuild.exe /nodemode:1`; `-mt` adds `/nodemode:2` sidecar task hosts.
  - With `DOTNET_CLI_USE_MSBUILD_SERVER=false`, the workers are `dotnet MSBuild.dll /nodemode:1` instead.
- **InProc:** the variant builds inside the server. It uses the existing compiler server; ilc and link are started through `cmd`.
- **Exec:** the nested `dotnet publish` is a new process.
  - The server is busy with the outer build, so the child builds in its own process: no second server and no worker nodes were seen.
  - The child inherits the outer environment, so it reused the same compiler server (`SharedCompilationId`) and node salt.
  - It inherits MSBuild's environment too. Anything set in the outer build's environment leaks into the nested build.
- **Global properties flow in.** The `MSBuild` task passes the caller's global properties to the callee, for example `Configuration`, `Platform`, `SolutionDir` and `CurrentSolutionConfigurationContents` from a solution build, and any `-p:`. Hence the `RemoveProperties` list and the explicit `Configuration`.
  - A solution build and a project build of the test project give the variant different configurations with the same output paths. They run one after another, so this is harmless; together they would be the cross-process case above.
- **CLI defaults to pass in-proc.** `dotnet publish` adds `_IsPublishing=true`, and its Release default comes from the CLI. In-proc, both must be passed explicitly. They were, in both modes.

## Recommendation for D3

| Operation | Use | Why |
|---|---|---|
| Restore of a variant | In-proc `MSBuild` `Targets="Restore"`, in its own call, with a **deterministic** `MSBuildRestoreSessionId` (for example the variant name), not a new Guid | A separate evaluation makes the build see fresh restore output. A deterministic id lets concurrent callers in one build share one restore instead of racing on `project.assets.json`. |
| Variant Build (Development, DesktopTest JIT) | In-proc `MSBuild` | 0.10–0.24 s added against 1.7–2.2 s for Exec; deduplicated inside a build. |
| Variant AOT Publish (DesktopTest) | In-proc `MSBuild` `Targets="Publish"` with explicit `Configuration`, `RuntimeIdentifier`, `PublishAot`, `_IsPublishing` | 6.1 s cold / 1.1 s warm against 8.4 / 2.8 for Exec; a new RID works in-proc after an in-proc restore. |
| Anything else | Exec only with a recorded reason. None was found. | |

**Required with either mode:**
1. **Per-variant `obj/<variant>/` and `bin/<variant>/`,** set before the SDK props, so restore output is per variant too.
2. **`DefaultItemExcludes` covering all of `bin/**` and `obj/**`.**
3. **`RemoveProperties`** for caller-only global properties.
4. **A design-time guard.**
5. **A cross-process lock** around restore and build of one variant. It belongs in the compiled task assembly (the plan's "lease"): `FileShare.None` lock file in the variant's `obj`, a task object with `Build` lifetime, a timeout with a clear message, and a "waiting for …" log line.

## Observed, skipped, not tested

- **Observed:** everything in the tables above. That covers both modes, Build and AOT Publish, the normal build after variant builds, the incremental second test build, design-time Compile, `dotnet test`, the solution and two-process concurrency with and without the lock, `-mt`, and the RID and restore probes.
- **Skipped:**
  - Development variant beyond the probes (Probe.proj built it; it wasn't measured).
  - Variants with package references that ship build props/targets: the restore-in-same-evaluation failure wasn't constructed.
  - Measuring the lock with a compiled task. The inline task's compile cost is in the locked timings.
- **Not tested:**
  - Visual Studio's .NET Framework `MSBuild.exe` running the in-proc variant call, including ILC through VS.
  - VS or C# Dev Kit design-time builds (only the CLI design-time approximation was run).
  - Variants of a project that has ProjectReferences, where global properties flow to references.
  - Static graph (`-graph`) builds.
  - Long paths.
  - Publishing a non-Production variant while the plan's "publishing anything other than Production is an error" rule is in force. The SDK will need an internal route that only variant builds may use.
- **Side effects:**
  - The win-x86 probe added win-x86 runtime and ILCompiler packages to the user's NuGet global packages folder.
  - `dotnet test` (MTP) created an empty `TestResults/` in the working directory; it was removed.
  - Logs, CSVs and a binlog are in `spikes/d3-variants/obj/`.
