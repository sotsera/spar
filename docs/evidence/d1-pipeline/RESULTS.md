# D1 spike: compilation pipeline, candidate A

> A spike. Its code was deleted with `spikes/` at the start of M2; it is in git history at [`spikes/d1-pipeline/`](https://github.com/sotsera/spar/tree/24f5db0942833047c81fa692908410596f3b8960/spikes/d1-pipeline) (commit `24f5db0`), and this record and its `results/` moved here. Paths under `spikes/` below name that history.

Recorded 2026-11-12 on the development machine: Windows 11 x64, SDK 11.0.100 (Roslyn 5.12), generator and tool built against `Microsoft.CodeAnalysis.CSharp` 5.9.0.

## What was built

| Project | Role |
|---|---|
| `Spike.Runtime` | Contract attributes and small JSON helpers (`SparJson`) used by generated code. |
| `Spike.Generator` | An incremental source generator (netstandard2.0). It emits all C#: one binding per contract, a catalog with an embedded manifest, and `Utf8JsonReader`/`Utf8JsonWriter` serializers for positional records, primitives, nullables and arrays. Contract analysis lives in `ContractReader`, shared with the tool. |
| `Spike.Tool` | An out-of-process tool. `client` reads an inventory (sources, references, defines) written by MSBuild, builds an analysis compilation, calls the same `ContractReader`, and writes `contracts.json` and one TypeScript module per contract. `pack` writes a deterministic zip. |
| `Spike.targets` | `ResolveReferences` → inventory (written only when different) → `client` (Inputs/Outputs) → `pack` → `EmbeddedResource` → one `CoreCompile`. |
| `Spike.App` | A console app with a `notes` contract (save/list/count), a nested nullable record, arrays, and a NativeAOT publish. `-p:SpikeSerializer=Stj` switches the generated binding to an app-authored System.Text.Json context as a size baseline. |

The inventory replaces the experiment's `Csc SkipCompilerExecution` capture. Contracts are authored source, so analysis needs only sources, references, defines and the nullable setting.

Run `bash measure.sh` to repeat the build measurements. NativeAOT outputs go to `artifacts/d1/`.

## Results

### Correctness (observed)

On both CoreCLR and NativeAOT, the app:
- round-trips nested, nullable, array, double, bool and long values;
- rejects a missing required property;
- reads the embedded frontend zip, which contains the authored files plus the generated client;
- exposes the manifest.

Generated TypeScript (`notes.ts`) uses readonly interfaces, `T | null` for nullable values, and a typed `notes.save/list/count` object.

### NativeAOT size (Release, win-x64, `InvariantGlobalization`)

| Serializer | Executable bytes | Publish time |
|---|---:|---:|
| Generated `Utf8Json*` code (candidate A) | 2,243,072 | 8.5 s |
| System.Text.Json source-generated context | 3,131,904 | 6.6 s |

The generated serializers save **888,832 bytes (28%)** for the same contract. No AOT or trimming warnings were raised (warnings are errors).

The behaviours differ in one place. With `RespectRequiredConstructorParameters`, System.Text.Json also requires nullable constructor parameters. The generated code treats nullable properties as optional, which is the intended contract rule.

### Build behaviour (CoreCLR Debug; two runs, the second after `build-server shutdown`)

| Step | Run 1 | Run 2 | Client tool ran | DLL rebuilt | `notes.ts` rewritten |
|---|---:|---:|---|---|---|
| Clean build | 3.8 s | 12.0 s | yes | yes | yes |
| No-op build | 0.56–0.69 s | 0.97–1.25 s | no | no | no |
| Method-body edit | 1.4 s | 2.5 s | yes | yes | no |
| Add contract | 1.4 s | 2.4 s | yes (`extra.ts` created) | yes | no |
| Rename command | 1.3 s | 2.4 s | yes (old member gone) | yes | no |
| Remove contract | 1.3 s | 2.3 s | yes (`extra.ts` deleted) | yes | no |
| Unsupported type | 1.2 s | 2.3 s | yes (invalid contract skipped) | no; build fails | no |
| Design-time compile | 0.38 s | 0.80 s | no | — | — |

- **Diagnostics.** The unsupported type was reported by the generator during the normal compile as `NotesService.cs(31,59): error SPIKE003`, with the source location. The tool does not report errors itself.
- **Design time.** Design-time compilation never launched the tool and still had all generated C#. No "build once first" step is needed.
- **Timestamps.** Restoring a file with an older timestamp is not detected: MSBuild skips CoreCompile as well. A normal editor save is detected. This is a property of MSBuild's timestamp checks, not of this pipeline.

## Assessment against the exit criteria

| Criterion | Outcome |
|---|---|
| Clean and no-op build time | Met. A no-op build skips the tool, the zip and compilation. |
| AOT size versus STJ metadata | Met, 28% smaller. |
| IDE diagnostics | **Partly verified.** Diagnostics come from an in-compiler generator with source locations, and design-time compile works. Not yet checked interactively in VS Code or Visual Studio. |
| Contract add/rename/remove invalidation | Met. Stale modules are deleted. Unchanged modules keep their timestamps. |
| `dotnet run` / `dotnet publish` | `dotnet build` + run and AOT `dotnet publish` work. `dotnet run` was not exercised separately (same build). |

Candidate B (post-compile assets assembly) was **not built**. Candidate A met every measured criterion, and B's main benefit (no pre-compile analysis) does not justify a second assembly and AOT linking risk.

## Costs and follow-ups

- **The tool runs on every C# edit.** It costs about 0.5–1.0 s, of which roughly 0.2 s is process start and the rest is binding 177 references. Options:
  - narrow its inputs to files that contain `[SparContract]`, or hash the contract model and exit early;
  - keep the tool resident across builds;
  - run analysis inside MSBuild through a .NET task host.

  Decide in M1, with measurements.
- **Version skew.** The tool parses with Roslyn 5.9 at `LanguageVersion.Preview`. Newer C# syntax that Roslyn 5.9 does not know could produce parse errors in non-contract files. These are ignored, since only contract types are read, but a contract written in new syntax would be skipped. The generator has the same constraint inside older IDEs.
- **Not covered here:** a real frontend build (tsc/Vite), `dotnet watch`, and package-only consumption.

## Follow-up: the client and pack steps as MSBuild tasks (2026-11-12)

The tool's logic moved into `Spike.Tool/ClientBuilder.cs`, which the command-line tool and a new `Spike.Tasks` assembly both use. `ClientBuilder` keeps a process-wide cache of metadata references, keyed by path, timestamp and size.

The tasks:
- **`GenerateContractClient` and `PackFrontend`** derive from `Microsoft.Build.Utilities.Task` (`Microsoft.Build.Utilities.Core` 18.12.2).
- **Thread safety:** both are marked `[MSBuildMultiThreadableTask]`, implement `IMultiThreadableTask`, and resolve paths through `TaskEnvironment.GetAbsolutePath`.
- **Mode switch:** `-p:SpikeTaskMode=` selects how they run.

| Mode | How the steps run |
|---|---|
| `Exec` | A `dotnet Spike.Tool.dll` process per step (the original pipeline) |
| `InProc` | Plain `UsingTask`, so the task runs inside the MSBuild node |
| `TaskHost` | `TaskFactory="TaskHostFactory" Runtime="NET"`, a short-lived task host |
| `Net` | `Runtime="NET"` only. Same as in-process under `dotnet build`; uses the pooled .NET task host under Visual Studio's `MSBuild.exe` |

`bash measure-tasks.sh 5` repeats the measurements, with output saved in `artifacts/d1/measure-tasks.txt`. Each step below is a save of `NotesService.cs` followed by a build, with CoreCLR Debug and a warm MSBuild server after the first build.

| Mode | First build | Later builds | Client step (later builds) | References reused |
|---|---:|---:|---:|---|
| `Exec` | 4.0 s | 1.5–1.9 s | 480–540 ms | never |
| `TaskHost` | 3.9 s | 1.4–1.8 s | 460–520 ms | never (new process each build) |
| `InProc` | 3.6 s | 0.9–1.3 s | 11–18 ms on a warm node, about 470 ms on a different node | 177 when the same node is reused |
| `InProc` with `-mt` | 2.8 s | **0.75–1.0 s** | **7–17 ms** | 177 every build (one process) |
| `Net` under VS `MSBuild.exe` 18.12.2 (`-nr:true`) | 6.9 s | 2.6–2.7 s | 12–29 ms | 177 every build (same task-host pid) |

### Findings

- **Loading references is the cost.** About 95% of the client step is loading 177 metadata references. A reference cache in a long-lived host removes it, cutting the step from about 500 ms to about 15 ms and a save-and-build from about 1.6 s to about 0.9 s.
- **Which host to use:**
  - Without `-mt`, a CLI build can land on a different MSBuild node, whose cache is empty.
  - `-mt` keeps every project in one process, so the cache always hits.
  - Under Visual Studio's MSBuild, `Runtime="NET"` reuses one pooled .NET task host across builds, so the same cache works there.
  - `TaskHostFactory` starts a new process every time and gives no benefit.
- **`-mt` worked unchanged.** The multithreadable tasks ran in-process with no warnings across six consecutive builds, and the app still produced the correct output.
- **Locked task assembly (observed).** With the tasks loaded in a warm node, rebuilding `Spike.Tasks` fails with `MSB3027 ... The file is locked by: "MSBuild..."`, with and without `-mt`. This only affects framework development, where the task assembly is rebuilt inside the same build graph. A packaged SDK's task assembly sits unchanged in the NuGet cache.
- **Not covered:** design-time builds inside the Visual Studio IDE (`MSBuild.exe` was run from the command line), or how long the pooled task host lives when idle.

## Follow-up: IDE behaviour (2026-11-12)

`D1.slnx` now holds the spike projects. `Spike.App` defaults to `SpikeTaskMode=Net`; pass `-p:SpikeTaskMode=Exec` for the original pipeline. That changes what `measure.sh` measures unless you pass the mode explicitly.

### Compiler versions in the IDEs

The generator references Roslyn 5.9 and loads only in hosts with Roslyn 5.9 or newer:
- Visual Studio 18 Community, C# language service: Roslyn 5.12.0.
- VS Code C# extension 2.160.4, language server (`.roslyn`): Roslyn 5.12.0.
- C# Dev Kit 3.40.215 is installed alongside.

### Design-time pipeline (observed through `MSBuildWorkspace`)

`ide-probe.cs` opens `Spike.App.csproj` the way the IDEs do: Roslyn `MSBuildWorkspace` 5.9 runs a design-time build and the project's source generators. Run it with `dotnet run ide-probe.cs -- <path to Spike.App.csproj>`; it needs `Microsoft.Build.Locator` and `DisableMSBuildAssemblyCopyCheck`.

- **Load:** the project loads in about 1.5 s with `Spike.Generator` among its analyzers. `NotesServiceBinding.g.cs`, `SparCatalog.g.cs` and `SparSerializers.g.cs` are present and `Spar.Generated.SparCatalog` resolves.
- **No tool run:** `client.stamp` is unchanged, so design time never runs the client tool or tasks.
- **Unsaved edit:** adding a `[SparCommand] PingAsync` to the in-memory document regenerates the binding with the `ping` command, with no build.
- **Unsupported type in memory:** `SPIKE003` at `NotesService.cs(23,59)`, with the correct message.
  - The whole `NotesServiceBinding` disappears with it, because one invalid command invalidates its contract.
  - The catalog still compiles because it only lists valid contracts.
  - **M1 change:** skip only the invalid command, so one mistake doesn't remove the rest of the contract.
- **TypeScript lags until a build.** The client (`notes.ts`) only updates on a build, so the frontend editor sees a stale client between C# edits and the next build.

### Not verified

- **Real IDE windows:** computer use was not available in this session, so none of this was observed in Visual Studio or VS Code themselves. Still to check there:
  - squiggles and navigation into generated files;
  - the Error List or Problems panel;
  - how fast the IDE refreshes generated code after typing;
  - VS's own build, including `Runtime="NET"` task reuse and the locked-DLL behaviour.
- **`devenv.com D1.slnx /Build Debug`** opened a "D1 - Microsoft Visual Studio" window, started five MSBuild nodes, then sat idle with no log output, probably waiting on a dialog. It was stopped without producing results.
- **The C# language server available to this agent** opened the files without loading the project (even authored cross-file symbols didn't resolve). It says nothing about VS Code's behaviour.

### Visual Studio's build engine (`devenv.com`, observed)

The first `devenv /build` attempt was run from Git Bash. Its path conversion rewrote `/Build` and `/Out` into `C:/Program Files/Git/...`, so Visual Studio opened as an IDE showing a "files could not be found" dialog. Run `devenv.com` from PowerShell or cmd, not Git Bash.

`devenv.com D1.slnx /Build Debug`, three times, saving `NotesService.cs` before each:

| Build | Result | Elapsed | Client step | References reused | Task host pid |
|---|---|---:|---:|---:|---|
| 1 | 5 succeeded | 9.5 s | 799 ms | 0 | 56388 |
| 2 | 5 succeeded | 3.8 s | 30 ms | 177 | 56388 |
| 3 | 5 succeeded | 4.0 s | 20 ms | 177 | 56388 |

- **The pooled task host survives between separate `devenv` processes.** It is the SDK's `MSBuild.exe /nodemode:2 /nodereuse:True`, a reused .NET task-host node, so the reference cache stays warm across Visual Studio builds. How long it lives when idle was not measured.
- **The lock happens in Visual Studio too.** Changing `Spike.Tasks` and building again gives "4 succeeded, 1 failed": `Spike.Tasks` fails with MSB3026/MSB3027 copy retries, because the task host still has the DLL loaded. Until that node exits, every build that tries to copy the rebuilt task DLL fails the same way. This confirms the source-checkout rule (`TaskFactory="TaskHostFactory"` when the task assembly is built in the same graph).
- **Still not observed in the IDE windows:** editor squiggles, navigation into generated files, the Error List, and how fast the IDE refreshes after typing.

## Interactive IDE checks (2026-11-13)

Observed through computer use in Visual Studio 18 Community and in VS Code 1.146.0 with the C# extension 2.160.4 and C# Dev Kit 3.40.215. Both IDEs were click-only: no typing, keys or right-click. Every source edit was a disk write from outside the IDE. "Save" below means clicking Save inside the IDE afterwards. `NotesService.cs` was restored byte for byte afterwards (same SHA-256). Screenshots are in `artifacts/d1/vs-*.jpg` and `artifacts/d1/vscode-*.jpg`.

### Visual Studio 18

| Check | Result |
|---|---|
| 1. Clean open, no build (`Spike.App/obj` and `bin` deleted first) | **Observed.** VS only restored and ran design-time builds: `obj` gained restore files and `AssemblyInfo`/`GlobalUsings`, with no `spar/` folder and nothing in `bin`. `SparCatalog` in `Program.cs` resolves: type colouring, reference highlighting, Quick Info `class Spar.Generated.SparCatalog`, 0 errors. |
| 1. Go to definition | **Observed** (F12 itself not tried, because keys are blocked; the Edit menu has no Go To Definition). Clicking the type link in Quick Info opened `SparCatalog.g.cs [generated]` as a read-only tab, with the banner "This file was generated by 'Spike.Generator.ContractGenerator' at … and cannot be edited. Rerun generator". |
| 1. Generated-files node | **Observed.** Spike.App › Dependencies › Analyzers › Spike.Generator › Spike.Generator.ContractGenerator lists `NotesServiceBinding.g.cs`, `SparCatalog.g.cs` and `SparSerializers.g.cs`, and double-clicking opens them. For roughly the first 3 minutes after open, the Solution Explorer tree ignored clicks (no expand, no selection). It then worked normally. |
| 2. `SPIKE003` after the disk edit alone | **Not observed.** VS silently reloaded the file. After about 50 s there was no diagnostic, and the generated binding was unchanged. |
| 2. `SPIKE003` after Save in VS | **Observed in the Error List, 2–8 s after Save:** `SPIKE003 Type 'System.Collections.Generic.Dictionary<string, int>' is not a supported wire type.`, Spike.App, `NotesService.cs`, line 25. Double-clicking it selects `message` at 25:73. The open `NotesServiceBinding.g.cs` tab turned red: "The generator … has stopped generating this file; this file is no longer being included in your project". |
| 2. Editor squiggle | **Not observed.** Watched for about 2.5 minutes: no underline on line 25 and no error mark in the scrollbar, although the Error List had the error. |
| 3. Refresh after the disk edit to `string message` | **Not observed after about 40 s.** The stale `SPIKE003` and the red banner stayed. |
| 3. Refresh after "Rerun generator" (banner link) | **Observed.** The binding regenerated about 2 s after the click (banner time 00:09:57) with a `"ping" => PingAsync(...)` case. `SPIKE003` left the Error List 10–20 s later. The Save path for clearing was not tried in VS. |
| 4. Build › Build Solution | **Observed, succeeded.** Build 1: 5 succeeded in 4.08 s, `spike-task client: 1 contract(s), 3 source(s), 177 reference(s), 0 reused, 445 ms, pid 2704`. Build 2, after touching `NotesService.cs`: 1 succeeded and 4 up to date in 0.85 s, `177 reused, 14 ms, pid 2704`, `pack: changed=False`. `notes.ts` gained `ping`. |

- **Task host:** pid 2704 is a new `MSBuild.exe /nodemode:2 /nodereuse:True` that VS created at the first build. The earlier pooled host, pid 56388 from the previous `devenv /Build` runs, was alive at 23:56 and had exited by 00:10:39. The exact exit time wasn't observed.
- **Mixed line endings in generated code:** opening `NotesServiceBinding.g.cs` raised VS's "Inconsistent Line Endings" dialog (answered No), and the status bar shows `MIXED`. Cause: `CSharpEmitter.Header` uses `\n`, while `StringBuilder.AppendLine` writes `\r\n` on Windows. **M1 requirement:** emit one newline style, with no `AppendLine` and no `Environment.NewLine`.
- **Generator setting:** VS 18's `settings.json` and `ApplicationPrivateSettings.xml` hold no source-generator override, so VS ran with its default. With that default, regeneration followed Save, "Rerun generator" or a build, never an external disk change. That matches VS's Balanced execution mode, but the option page wasn't opened to confirm it.

### VS Code with the C# extension and C# Dev Kit

| Check | Result |
|---|---|
| Open | The folder opened in **Restricted Mode**, so the project didn't load (TextMate colouring only, no solution). The user trusted the folder, and Dev Kit then loaded `D1.slnx`: the status bar showed `Projects: Spike.App` and `Solution: D1.slnx`. |
| 1. Clean open, no build | **Observed, with a caveat.** `obj` already held restore and design-time files when VS Code started. They came from this agent's own `roslyn-language-server --autoLoadProjects` (Claude Code tooling, not VS Code), written 4 s before launch. There was no `spar/` folder and nothing in `bin`, so no build had run. `SparCatalog` was semantically coloured, with 0 problems. |
| 1. Go to definition | **Observed.** Go › Go to Definition on `SparCatalog` opened `SparCatalog.g.cs` as a preview tab. Unlike VS, there was no "generated" banner and no `[generated]` suffix. |
| 1. Generated-files node | **Not observed.** Dev Kit offers "C# Project Details" (the Explorer views menu lists no separate Solution Explorer). Spike.App › Analyzers lists `Spike.Generator` as a leaf with no children. Generated files are reachable only by navigation (definition, references, CodeLens). |
| 2. `SPIKE003` after the disk edit alone | **Not observed after about 45 s.** |
| 2. `SPIKE003` after File › Save | **Observed within 5 s:** a red squiggle under `message`, and a Problems entry `Type '…Dictionary<string, int>' is not a supported wire type. (SPIKE003) [Ln 25, Col 73]`. Command CodeLens counts fell to "0 references" because the binding disappeared. |
| 3. Refresh after the disk edit to `string message` | **Not observed after about 45 s.** The stale `SPIKE003` stayed, and its squiggle moved onto `FromResult`: the old column 73 on the now shorter line. |
| 3. Refresh after File › Save | **Observed within 4 s.** `SPIKE003` cleared and `PingAsync` showed "1 reference". Peek References opened `NotesServiceBinding.g.cs`, which has the `"ping" => PingAsync(...)` case. No build was run. |
| 4. Terminal › Run Build Task › `dotnet: build` (the only task offered) | **Observed, succeeded.** It runs `dotnet build …\D1.slnx /property:GenerateFullPaths=true /p:Configuration=Debug "/p:Platform=Any CPU" /consoleloggerparameters:NoSummary`. Build 1: 7.46 s, `177 reference(s), 0 reused, 447 ms, pid 55436`. Build 2, after touching `NotesService.cs`: 0.75 s, `177 reused, 53 ms, pid 55436`. |

- **Build host in VS Code:** pid 55436 is `MSBuild.exe /nodemode:8`. It was started at 00:11:49 by a short-lived parent that had exited, most likely this agent's language-server tooling. The CLI build ran the tasks inside that long-lived process, so the reference cache stayed warm here too.
- **Not tried:** the Dev Kit build action on the solution node, which needs right-click.

### What this means for M1

- **Neither IDE regenerates on an external file change.** Edits from git, scripts or agents leave generated code and generator diagnostics stale until Save, "Rerun generator" (VS) or a build. Unsaved typing could not be tested; the earlier `MSBuildWorkspace` probe covers it.
- **VS showed the generator diagnostic only in the Error List, with no squiggle.** VS Code showed both. A `DiagnosticAnalyzer` that runs the same contract checks would report on every edit in both IDEs, independent of generator execution mode. **Candidate M1 requirement.**
- **Mixed line endings in generated files, as described above.**
- **A stale diagnostic keeps its old position.** In VS Code it pointed at unrelated code until the next save.

### Not verified

- Live refresh while typing, F12 or Ctrl+click, and VS's Save path for clearing a diagnostic.
- Whether VS shows the squiggle when the generator runs in Automatic mode.
- How long VS's pooled task host lives when idle.

Left running: MSBuild pid 55436 (`/nodemode:8`) and four worker nodes from the VS Code CLI build, none started directly by this session. Task host pid 2704 had exited.
