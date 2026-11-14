# MSBuild task analyzer and multithreadable tasks (spike)

## Question

Spar's build steps are compiled MSBuild tasks (`Runtime="NET"`) in a task assembly inside the `Spar.Sdk` MSBuild SDK, and they must be thread-safe tasks. This spike answers, by running things on SDK 11.0.100 (Windows 11):

1. Which MSBuild ships with the SDK, and which `Microsoft.Build.Framework` / `Microsoft.Build.Utilities.Core` versions match it.
2. Whether `Microsoft.Build.TaskAuthoring.Analyzer` 18.13.0 runs with this SDK's compiler, which rules it reports and what triggers them, and what its code fix does.
3. What `IMultiThreadableTask` and `TaskEnvironment` are in this MSBuild, and how tasks are scheduled with and without multithreaded mode (`-mt`).
4. Whether the analyzer works in the shape Spar's task project will have (warnings as errors, central package management), and what it costs.

## Versions

| Item | Value |
|---|---|
| SDK | 11.0.100 (`global.json`), runtime 11.0.0 |
| MSBuild | `dotnet msbuild -version`: **18.12.2.53018** (18.12.2-1.26529.118+297acf930) |
| MSBuild assemblies in the SDK | `Microsoft.Build.Framework.dll`, `Microsoft.Build.Utilities.Core.dll`: file 18.12.2.53018, assembly version 15.1.0.0 |
| C# compiler | Roslyn 5.12.0-1.26529.118 (`Microsoft.CodeAnalysis` 5.12.0.0) |
| Matching NuGet packages | `Microsoft.Build.Framework` **18.12.2**, `Microsoft.Build.Utilities.Core` **18.12.2** (the newest on nuget.org, and the versions already in the root `Directory.Packages.props`) |
| Analyzer | `Microsoft.Build.TaskAuthoring.Analyzer` **18.13.0** (only version on nuget.org; 94,237-byte package; netstandard2.0; references Roslyn 4.12.0.0, `System.Collections.Immutable` 10.0.0.0; source commit `4f3669e6` in dotnet/msbuild) |
| Fix harness host | `Microsoft.CodeAnalysis.CSharp.Workspaces` 5.9.0 (the repository's Roslyn version) |

The analyzer ships independently of `Microsoft.Build.Framework`, so 18.13.0 next to Framework 18.12.2 is the intended combination, not a mismatch. Only the analyzer package was downloaded for this spike (94 KB); everything else was already in the NuGet cache.

## Method

Everything lives in this folder. `Directory.Build.props` imports the repository's (net11.0, nullable, `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`); `Directory.Packages.props` imports the repository's central versions and adds the analyzer and the Workspaces package. All runs used `DOTNET_CLI_USE_MSBUILD_SERVER=0` and `-nodeReuse:false` (plus `--disable-build-servers` for `dotnet build`), so no node outlives a run.

| Part | What it is |
|---|---|
| `Probe.Rules/` | Rule catalogue. One task (or helper) per rule, misusing each API on purpose. Each probe line ends with `// expect: <IDs>`. `TreatWarningsAsErrors` is off **in this project only**, so the build reports instead of failing. Diagnostics, Info level included, go to SARIF (`ErrorLog`). |
| `Probe.Rules/config/*.globalconfig` | Option probes, selected by a property: `all-tasks` sets `msbuild_task_analyzer.run_mt_analyzers_on_all_tasks = true`; `enable-0013` sets `dotnet_diagnostic.MSBuildTask0013.severity = suggestion`; `wrong-option` sets `msbuild_task_analyzer.scope = all` (the name in the spike brief). No `.editorconfig` was added anywhere. |
| `Probe.Tasks/` | The shape Spar will use: warnings as errors and code style on, central versions, Framework/Utilities.Core with `ExcludeAssets="runtime" PrivateAssets="all"`, the analyzer with `PrivateAssets="all"` (`-p:ProbeNoAnalyzer=true` drops it). Tasks: `ThreadSafeProbe` (attribute + interface + constructor injection), `LegacyProbe` (plain `Task`), `InterfaceOnlyProbe` (interface, no attribute), `TypedParameterProbe` (typed parameters). Each logs process id, thread id, process name, MSBuild node mode, project directory and an environment variable. |
| `Probe.Build/` | `Build.proj` builds generated leaf projects (`obj/leaves/leaf-NN/Leaf.proj`, each in its own folder) in parallel with the `MSBuild` task. `Leaf.targets` declares the tasks with `Runtime="NET"` (in-process) or, with `-p:ProbeTaskHost=true`, `TaskFactory="TaskHostFactory"`. `StrictLeaf.targets` and `TypedLeaf.targets` drive the strict-mode and typed-parameter probes. |
| `Inspect/` | Prints the public surface of types in an assembly (used on the SDK's `Microsoft.Build.Framework.dll`). |
| `FixHarness/` | Loads the analyzer DLL's analyzers and code fix providers into a Roslyn `AdhocWorkspace`, applies every offered fix to its own copy of the catalogue, and checks that the result compiles. |
| `scripts/rules.ps1` | Builds the catalogue under each configuration; writes `results/rules-<config>.{sarif,build.log,tsv,compare.txt}`. |
| `scripts/codefix.ps1` | `dotnet format analyzers` on a copy under `obj/fix-copy`, then the harness; writes `results/format-analyzers.log`, `results/format-report/`, `results/codefix-report.txt`. |
| `scripts/run-modes.ps1` | Scheduling modes (`-m:1`, `-m:8`, `-m:8 -mt`, each also with `TaskHostFactory`); writes `results/*modes-summary.txt` and raw logs in `results/modes/`. |
| `scripts/strict-mode.ps1`, `scripts/typed-parameters.ps1`, `scripts/cost.ps1` | Strict mode, typed parameter binding, build cost; summaries in `results/`. |

Raw evidence: `results/` (SARIF logs, build logs, compare tables, the code-fix report, per-run mode logs, `binlog-summary.txt`, `mt8-all-kinds.binlog`, `probe-tasks-build.binlog`, `framework-18.12.2-sdk-surface.txt`). For the cost runs only repetition 1's detailed log is kept; all repetitions' numbers are in `cost-summary.txt`. `results/stall/m8-diag-8.log` is a diagnostic log of one slow run (see "Process launch stalls").

## Answer 1: versions to reference

Reference **`Microsoft.Build.Framework` 18.12.2 and `Microsoft.Build.Utilities.Core` 18.12.2** with `ExcludeAssets="runtime" PrivateAssets="all"`. They match the SDK's MSBuild exactly, and the repository already pins them.

- Restore of the task project brings `Microsoft.NET.StringTools` 18.12.2 and `System.Configuration.ConfigurationManager`, `System.Diagnostics.EventLog`, `System.Security.Cryptography.ProtectedData` 10.0.10 as compile references. None of them is copied to the output, even with `CopyLocalLockFileAssemblies=true` (`results/copylocal-output.txt`: only `Probe.Tasks.dll`, `.pdb`, `.deps.json`).
- No NuGet warnings (no NU1510, NU1608, NU1605 or audit warnings) in any restore or build.

## Answer 2: the analyzer

**It loads and runs** with the SDK 11.0.100 compiler (Roslyn 5.12) on the command line, and in a Roslyn 5.9 workspace. 81 diagnostics in the catalogue; every `expect:` line matched (`results/rules-default.compare.txt`).

It contains seven analyzers, one suppressor and two code fix providers: `MultiThreadableTaskAnalyzer`, `MultiThreadableTaskDeclarationAnalyzer`, `PreferTypedParameterAnalyzer`, `TaskEnvironmentConstructorInjectionAnalyzer`, `TransitiveCallChainAnalyzer`, `UnsupportedTaskItemTypeAnalyzer`, the `RequiredTaskPropertyInitializationSuppressor` (`MSBuildTaskSPR0001`, suppresses CS8618), and the fix providers `MultiThreadableTaskCodeFixProvider` (0002, 0003) and `PreferTypedParameterCodeFixProvider` (0006–0008).

### Scope

A type is "multithreadable-scoped" if it carries `[MSBuildMultiThreadableTask]` directly, declares `IMultiThreadableTask` in its own base list, or carries `[MSBuildMultiThreadableTaskAnalyzed]` (helpers). Observed:

- A plain `Task` gets only 0001 (and 0009/0010). `Environment.CurrentDirectory` and `File.Exists("relative.txt")` in it are silent by default.
- With `msbuild_task_analyzer.run_mt_analyzers_on_all_tasks = true`, the same plain task gets 0002 and 0003, a helper reached only from it gets 0005, and a `ToolTask`'s `GenerateFullPathToTool` override gets 0002 for `Path.GetFullPath` (+5 diagnostics; `rules-all-tasks.compare.txt`).
- **`msbuild_task_analyzer.scope` does not exist.** The analyzer's strings contain only `msbuild_task_analyzer.run_mt_analyzers_on_all_tasks`; the `wrong-option` run is identical to the default run (78 vs 78 relevant diagnostics before `R_StrictMode.cs` was added, 81 vs 81 after). Roslyn ignores unknown keys silently.
- `Microsoft.Build.Framework` 18.12.2 does **not** contain `MSBuildMultiThreadableTaskAnalyzedAttribute`. The analyzer looks it up by metadata name; an `internal` copy declared in source in namespace `Microsoft.Build.Framework` works (`AnalyzedHelper` got 0002 without being reached from a task).
- A helper reached from a multithreadable task gets 0005 **at the unsafe call site**, with the whole chain in the message. A helper not reached from any task is not analyzed.
- A class that is not a task and has no attribute is not analyzed (`Console.WriteLine` in it: silent).

### Rule table (default configuration, observed)

Default severity is what the compiler reported (SARIF `note` = Info). "Scope: MT" means multithreadable-scoped code only, unless `run_mt_analyzers_on_all_tasks` is set.

| ID | Default | Scope | Observed triggers | Message (abridged) |
|---|---|---|---|---|
| MSBuildTask0001 | Info | every task | every `Console` member (`WriteLine`, `BufferWidth`, `Out`, `Error`, `ReadLine`), `Environment.Exit`, `Environment.FailFast`, `Process.Kill`, `ThreadPool.SetMinThreads`/`SetMaxThreads`, `CultureInfo.DefaultThreadCurrentCulture`/`UICulture` setters, `Directory.SetCurrentDirectory` | "'X' must not be used in MSBuild tasks: …" |
| MSBuildTask0002 | Warning | MT | `Environment.CurrentDirectory` (get and set), `Directory.GetCurrentDirectory`, `Environment.GetEnvironmentVariable` (both overloads), `SetEnvironmentVariable`, `GetEnvironmentVariables`, `ExpandEnvironmentVariables`, `GetFolderPath`, `Path.GetFullPath("relative")`, `Path.GetTempPath`, `Path.GetTempFileName`, `Directory.CreateTempSubdirectory`, `Process.Start`, `new ProcessStartInfo(...)`. Not flagged: `Path.GetRandomFileName`, `Path.GetFullPath(absolutePath.Value)`. | "'X' should use TaskEnvironment alternative: use TaskEnvironment.Y instead" |
| MSBuildTask0003 | Warning | MT | `File.Exists/WriteAllText/Copy`, `Directory.CreateDirectory/GetFiles`, `new FileInfo/DirectoryInfo/FileStream/StreamReader/StreamWriter/FileSystemWatcher` with a relative literal or constant, `item.ItemSpec`, `absolutePath.Value`, `Path.Combine("obj", "x")`. Accepted: `TaskEnvironment.GetAbsolutePath(...)`, an `AbsolutePath` local or parameter (also through implicit conversion to a `string` local), `FileInfo.FullName`, `item.GetMetadata("FullPath")`, `Path.Combine(TaskEnvironment.ProjectDirectory, …)`. `File.Copy(relative, relative)` gives **one** diagnostic per call, not one per argument. | "'X' may resolve a relative path against the shared working directory: wrap path argument with TaskEnvironment.GetAbsolutePath()" |
| MSBuildTask0004 | Warning | MT | `Assembly.Load` (string and bytes), `LoadFrom`, `LoadFile`, `LoadWithPartialName`, `Activator.CreateInstance(string, string)`, `Activator.CreateInstanceFrom`, `AppDomain.Load/CreateInstance/CreateInstanceFrom`. Not flagged: `AppDomain.AssemblyResolve +=`, `AssemblyLoadContext.Default.LoadFromAssemblyPath`. | "'X' may cause issues in multithreaded tasks: may cause version conflicts in shared task host" |
| MSBuildTask0005 | Warning | call chains from MT tasks | `File.Exists`, `Console.WriteLine`, `Environment.GetEnvironmentVariable` in a static helper two calls deep | "'Task.Execute' transitively calls unsafe API 'X' via: Task.Execute → Helper.A → Helper.B → X" |
| MSBuildTask0006 | Info | `[MSBuildMultiThreadableTask]` tasks | `TaskEnvironment.GetAbsolutePath(InputPath)`, `new FileInfo(GetAbsolutePath(FilePath))` on a `string` input; reported where the value is converted, not on the property | "Consider changing task property 'P' from 'string' to 'AbsolutePath'/'FileInfo' …" |
| MSBuildTask0007 | Info | `[MSBuildMultiThreadableTask]` tasks | `int.Parse(Item.ItemSpec)`, `File.Exists(item.ItemSpec)` and `File.Exists(item.GetMetadata("FullPath"))` on an `ITaskItem[]` input | "Consider changing task property 'Items' from 'ITaskItem[]' to 'ITaskItem<FileInfo>[]' …" |
| MSBuildTask0008 | Info | `[MSBuildMultiThreadableTask]` tasks | `public string RelativeDefault { get; set; } = "obj";` later rooted | "Task property 'P' has a relative default path; initialize it in Execute() …" |
| MSBuildTask0009 | Info | every task | `ITaskItem<Guid>` | "… MSBuild cannot automatically parse 'Guid' … Use one of: string, bool, AbsolutePath, FileInfo, DirectoryInfo." |
| MSBuildTask0010 | Info | every task | `ITaskItem<int>` | "… parses through Convert.ChangeType using CultureInfo.InvariantCulture …" |
| MSBuildTask0011 | Info | concrete `IMultiThreadableTask` implementations | every catalogue task without a `TaskEnvironment` constructor, and a `ToolTask`-derived task (inherits the interface). Not reported with a public `ctor(TaskEnvironment)`. | "Task 'T' receives TaskEnvironment only after construction; add a public constructor with a single TaskEnvironment parameter …" |
| MSBuildTask0012 | Warning | `[MSBuildMultiThreadableTask]` tasks with a `TaskEnvironment` property and no interface | `R0012_NoInterface` | "… declares a TaskEnvironment property but does not implement IMultiThreadableTask, so MSBuild never assigns it …" |
| MSBuildTask0013 | Info, **disabled** | tasks declaring the interface without the attribute | reported only with `enable-0013` (`R0013_NoAttribute`) | "Task implements IMultiThreadableTask but is not marked with [MSBuildMultiThreadableTask]" |
| MSBuildTask0014 | Warning | types with the attribute | attribute on a non-task class, and on an abstract task (not inherited) | "[MSBuildMultiThreadableTask] on type 'T' has no effect because …" |
| MSBuildTaskSPR0001 (suppressor) | – | `[Required]` properties of tasks | CS8618 on an uninitialized `[Required] string` was suppressed; the same property without `[Required]` still got CS8618 | – |

**Not detected** (all inside a `[MSBuildMultiThreadableTask]` task, `R_NotCovered`): incrementing a `static int`, adding to a `static readonly List<string>`, `AppContext.SetSwitch`, `Thread.CurrentThread.CurrentCulture =`, `CultureInfo.CurrentCulture =`, `GC.Collect`, `Environment.ExitCode =`, `GCSettings.LatencyMode =`, `AppDomain.AssemblyResolve +=`, `AssemblyLoadContext.LoadFromAssemblyPath`. The analyzer checks API calls, not shared state; "no unsynchronized static state" still needs review and tests.

`System.CodeDom.Compiler.TempFileCollection` (0002) was not probed: the type is not in the net11.0 shared framework.

### Info diagnostics are invisible on the command line

At `-v:n`, the catalogue build log (`results/rules-default.build.log`) shows the 50 warnings (each twice) and **none** of the 32 Info diagnostics that the SARIF log records. 0001, 0006–0011 appear only in SARIF and, presumably, the IDE. In Spar's task project, `Console.WriteLine`, `Environment.Exit` or `Directory.SetCurrentDirectory` in a task would compile without any visible message.

### Code fix

- **The 18.13.0 package's fixes are not discoverable.** Neither provider carries `[ExportCodeFixProvider]` (checked in the DLL's metadata and in the source at the package commit). dotnet/msbuild PR #15402, "Disable TaskAnalyzer code fixes for 18.13", removed the exports on purpose: the fixes "can produce uncompilable code, change path/parsing behavior, or break callers when retyping public properties". `dotnet format analyzers --severity info` found the diagnostics and printed "Unable to fix MSBuildTask000N. No associated code fix found" for every rule; it changed no file. Visual Studio would not offer them either (not run).
- **Applied directly** through `FixHarness` (`results/codefix-report.txt`):

  | Rule | Diagnostics | Fix offered | Result compiles |
  |---|---|---|---|
  | 0002 | 17 | 9 | 7 |
  | 0003 | 16 | 16 | 16 |
  | 0006 | 2 | 2 | 2 |
  | 0008 | 1 | 1 | 1 |
  | 0007 | 3 | 0 | – |
  | others | – | 0 | – |

  - 0002: `Environment.CurrentDirectory` → `TaskEnvironment.ProjectDirectory`, `GetEnvironmentVariable/SetEnvironmentVariable/GetEnvironmentVariables` → the `TaskEnvironment` method, `Path.GetFullPath(x)` → `TaskEnvironment.GetAbsolutePath(x)`. The setter `Environment.CurrentDirectory = cwd` became `TaskEnvironment.ProjectDirectory = cwd` (CS0200, read-only). `GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User)` kept the second argument (CS1501). No fix for `ExpandEnvironmentVariables`, `GetFolderPath`, `GetTempPath`, `GetTempFileName`, `CreateTempSubdirectory`, `Process.Start`, `new ProcessStartInfo`.
  - 0003: wraps the path argument in `TaskEnvironment.GetAbsolutePath(...)`. For `File.Copy(a, b)` it wraps only the first argument; the diagnostic stays. It also wraps `absolute.Value` instead of passing `absolute`.
  - 0006/0008: retype the property (`string` → `AbsolutePath`/`FileInfo`, initializer `= default`/`= null!`), and for 0008 move the default into `Execute()` behind `if (P == default)`. This changes the task's public parameter types.

## Answer 3: `IMultiThreadableTask`, `TaskEnvironment` and multithreaded mode

### API surface (SDK's `Microsoft.Build.Framework.dll` 18.12.2; `results/framework-18.12.2-sdk-surface.txt`)

```csharp
public interface IMultiThreadableTask : ITask { TaskEnvironment TaskEnvironment { get; set; } }

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class MSBuildMultiThreadableTaskAttribute : Attribute { }

public sealed class TaskEnvironment
{
    public static TaskEnvironment Fallback { get; }
    public static TaskEnvironment CreateWithProjectDirectoryAndEnvironment(string projectDirectory, IDictionary<string, string> environmentVariables);
    public AbsolutePath ProjectDirectory { get; }
    public AbsolutePath GetAbsolutePath(string path);
    public string GetEnvironmentVariable(string name);
    public IReadOnlyDictionary<string, string> GetEnvironmentVariables();
    public void SetEnvironmentVariable(string name, string value);
    public AbsolutePath GetTempPath();
    public ProcessStartInfo GetProcessStartInfo();
}

public readonly struct AbsolutePath : IEquatable<AbsolutePath>
{
    public AbsolutePath(string path);
    public AbsolutePath(string path, AbsolutePath basePath);
    public string Value { get; }
    public string OriginalValue { get; }
    public AbsolutePath GetCanonicalForm();
    public static implicit operator string(AbsolutePath path);
    // Equals, GetHashCode, ToString, ==, !=
}

public interface ITaskItem<T> : ITaskItem2 { T Value { get; } }
```

`Microsoft.Build.Utilities.Task` has `static RegisterTask(...)` overloads, including `RegisterTask<TTask>(Func<TaskEnvironment, TTask>)`. `ToolTask` implements `IMultiThreadableTask` and has a `TaskEnvironment` property.

### Builds and runs

`Probe.Tasks` builds clean under the repository's settings (0 warnings, 0 errors). Its tasks ran in every mode below. A task with only a `ctor(TaskEnvironment)` plus a parameterless constructor chaining to `TaskEnvironment.Fallback` satisfies 0011 and code style.

### Where tasks run (`results/modes-summary.txt`, 8 leaves × 3 tasks × 300 ms sleep, 5 repetitions)

| Mode | `ThreadSafeProbe` (attribute + interface) | `LegacyProbe` (plain) and `InterfaceOnlyProbe` (interface only) |
|---|---|---|
| `-m:1` | entry `dotnet` process, one at a time | same |
| `-m:8` | entry process + 7 worker nodes (`dotnet`, `/nodemode:1`) | same |
| `-m:8 -mt` | **entry process, 8 concurrent threads** (8/8 in the entry PID) | **8 TaskHost processes** (`MSBuild`, `/nodemode:2`) |
| `TaskHostFactory`, with or without `-mt` | 8 TaskHost processes | 8 TaskHost processes |

- Under `-mt` only `[MSBuildMultiThreadableTask]` keeps a task in-process; `IMultiThreadableTask` alone still goes to a TaskHost.
- With 32 leaves, `-mt` still used 8 TaskHosts, one per thread node, reused across projects.
- In every mode each task saw its own leaf as `TaskEnvironment.ProjectDirectory`. `LegacyProbe` saw its own leaf as the process working directory too, in the entry process, in worker nodes and in TaskHosts. `PROBE_VAR` set in the launching shell was visible through `TaskEnvironment` and `Environment` in every host.

### Time (wall clock of `dotnet msbuild`, ms, 5 repetitions)

| Workload | `-m:1` | `-m:8` | `-m:8 -mt` | `-m:8` + TaskHostFactory | `-m:8 -mt` + TaskHostFactory |
|---|---|---|---|---|---|
| 8 leaves, 3 tasks of 300 ms | 7686–7781 | 1401, 1415 (3 stalls ≈ 6380) | 1363–1374 (2 stalls ≈ 6350) | 1614–1670 (1 stall 6637) | 1418–1517 |
| 32 leaves, `ThreadSafeProbe` only, 0 ms | – | 474, 514 (3 stalls ≈ 5475) | **255–262** | 668–684 (1 stall 5665) | 461–479 (2 stalls ≈ 5440) |
| 32 leaves, `LegacyProbe` only, 0 ms | – | 466–497 (1 stall 5459) | 436–478 | – | – |

- **With only thread-safe tasks, `-mt` started no process and ran in 255–262 ms**, against 474–514 ms for `-m:8` and 461–479 ms when the same tasks go to TaskHosts.
- Binlog of `-m:8 -mt` with 100 ms tasks (`results/binlog-summary.txt`): `ThreadSafeProbe` averaged 107 ms (max 110), `InterfaceOnlyProbe` 115 ms, `LegacyProbe` 154 ms (max 300, the first call into each new TaskHost).

### Process launch stalls (observed, cause not found)

13 of the 45 runs that launched MSBuild processes (worker nodes or TaskHosts) took about 5 s longer. Runs that launched none never did (`-m:1` 0/5, `-m:8 -mt` with thread-safe tasks only 0/5). In the diagnostic log of a slow run (`results/stall/m8-diag-8.log`, 1 of 8 attempts), the scheduler assigned projects to nodes 2–8 at 15:18:48.594, and the first task on those nodes started at 15:18:53.870, all within 25 ms of each other. The analyzer and the tasks are not involved. Timing comparisons must report the range, not one number.

### Strict mode (`results/strict-summary.txt`)

MSBuild 18.12.2 has multithreaded strict mode, and `-mt` turns it on by default:

| Case | Process working directory seen by an in-process task | `File.Exists("Leaf.proj")` (relative) |
|---|---|---|
| `-m:4` | the project's own folder | true |
| `-m:4 -mt` | `%TEMP%\MSBuildTemp…\Temporary…\MT-sentinel-CWD` | **false** |
| `-m:4 -mt`, `MSBUILDDISABLEFEATURESFROMVERSION=18.12` | the folder the build started in | false |

- A task that changes the working directory fails the build with **error MSB4286** ("Use TaskEnvironment instead of changing the process directory").
- A file written through a relative path lands in the sentinel and gives **warning MSB4287** at project completion. Four leaves doing it concurrently also produced `MSB4018 IOException` on the shared file.
- The sentinel folders were gone after each build.
- A relative path that works under `-m` (the working directory is the project) silently resolves elsewhere under `-mt`. That is the failure the analyzer's 0002/0003 prevent.

### Typed parameters (`results/typed-summary.txt`)

`AbsolutePath`, `FileInfo`, `DirectoryInfo`, `ITaskItem<AbsolutePath>[]` and `ITaskItem<bool>` parameters bind in MSBuild 18.12.2 in-process (`-m:1`), under `-mt` and in a TaskHost. Relative values (`obj`, `obj\file.txt`, items `obj\a.txt;sub\b.txt`) resolved against each leaf's project directory. `AbsolutePath.OriginalValue` kept `obj` in-process, but in the TaskHost it held the already-rooted path.

## Answer 4: the analyzer in Spar's shape, and its cost

- **Works unchanged** with `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, the root `.editorconfig` and central package management: 0 warnings for the clean task project, 0 NuGet warnings. No conflict with the root `.editorconfig` (it says nothing about `MSBuildTask*`; CA2016 stays an error; code style accepted the recommended task shape).
- The package has `requireLicenseAcceptance=true` (MIT). `PackageReference` restore showed no prompt and no warning.
- NativeAOT is not involved. The task assembly is a plain JIT `net11.0` library.

Cost (`results/cost-summary.txt`; full rebuilds, `--no-restore --no-incremental`, 7 repetitions after a warm-up):

| Project | Analyzer | Wall ms (median, range) | Csc ms (median) | TaskAuthoring analyzer ms (Roslyn `ReportAnalyzer`) |
|---|---|---|---|---|
| `Probe.Tasks` (3 files) | with | 1386 (1368–1407) | 838 | 36 (34–37) |
| `Probe.Tasks` | without | 1385 (1350–1402) | 847 | – |
| `Probe.Rules` (10 source files, 81 diagnostics) | with | 1302 (1286–1336) | 765 | 72 (67–75) |

The difference is below the noise. For comparison, the code-style analyzers took 972 ms of the 1.18 s total analyzer time in `Probe.Tasks`. Within the package, `TransitiveCallChainAnalyzer` is the largest part (17 of 34 ms). It scans the whole compilation, so its cost grows with the task assembly.

## Observed, skipped, not tested

**Observed:** everything in the tables above.

**Skipped or not tested:**

- Visual Studio. Not tested: the analyzer in the IDE (it needs `System.Collections.Immutable` 10.0 from the IDE's Roslyn host), the absence of light-bulb fixes, and tasks under the .NET Framework `MSBuild.exe` with the .NET TaskHost.
- Design-time builds, `dotnet build` with the MSBuild server, and Linux.
- A packaged `Spar.Sdk` consumer: tasks here were loaded from `bin/` with `nodeReuse:false`, not from a package.
- `TempFileCollection` (not in the shared framework).
- The root cause of the 5 s process-launch stalls.
- `-warnAsError:MSB4286;MSB4287;MSB4288` as a lane setting (MSB4286 was already an error; MSB4287 was reported as a warning).
- Applying the code fixes in an editor. They were applied only through the harness, because no host can discover them in 18.13.0.

## Recommendation for Spar's task project

1. **Reference the analyzer.** Add `Microsoft.Build.TaskAuthoring.Analyzer` 18.13.0 to `Directory.Packages.props` and reference it from the task project with `PrivateAssets="all"`. It costs about 36–72 ms per compile, raises no NuGet warnings and conflicts with nothing in the repository.
2. **Keep `Microsoft.Build.Framework` and `Microsoft.Build.Utilities.Core` at 18.12.2** (the SDK's MSBuild) with `ExcludeAssets="runtime" PrivateAssets="all"`. Move them only when the minimum supported SDK moves. The analyzer version is independent of them.
3. **Use the full shape now:** `[MSBuildMultiThreadableTask]`, `IMultiThreadableTask`, a public `ctor(TaskEnvironment)` plus a parameterless constructor chaining to `TaskEnvironment.Fallback`. That is the only shape that runs in-process under `-mt` (255 ms against 461–514 ms in the probe), and it raises no analyzer diagnostic. Under `-mt`, a task with the interface but not the attribute costs a TaskHost.
4. **Analyzer configuration to agree with the user** (no change made here):
   - `msbuild_task_analyzer.run_mt_analyzers_on_all_tasks = true`, scoped to the task sources. A task that forgets the attribute and the interface then still gets 0002–0005. Without it, such a task is checked for 0001 only, and 0001 is invisible on the command line.
   - Severity overrides (each needs agreement and an `.editorconfig` entry with its reason):
     - `MSBuildTask0001` → `warning`. Today it is Info and never shows in a CLI build.
     - `MSBuildTask0013` → `warning`. Every Spar task must carry the attribute.
     - `MSBuildTask0011` → `warning`. Constructor injection.
     - Optionally `MSBuildTask0009`/`0010` → `warning`, if typed parameters are used.
   - Do not use `msbuild_task_analyzer.scope`; it does nothing.
5. **Do not rely on the code fix.** It is unavailable in 18.13.0, and applied by hand it breaks some code (CS0200, CS1501) or fixes only half a call.
6. **Keep the static-state rule as review and test.** The analyzer does not see static fields, `AppContext` switches, culture setters or assembly-load hooks.
7. **The `-mt` build-test lane:** strict mode in 18.12.2 is the run-time counterpart of the analyzer. Run the lane with `-mt` and `-warnAsError:MSB4286;MSB4287;MSB4288`, as the MSBuild migration guide does. `TreatWarningsAsErrors` does not cover MSB warnings.
8. **Typed parameters** (`AbsolutePath`, `ITaskItem<AbsolutePath>`) work in 18.12.2 in every host and remove manual rooting (0006/0007). They are optional; plain `string` plus `TaskEnvironment.GetAbsolutePath` is equally safe.

## Follow-up: an `.editorconfig` section instead of a global config (2026-11-14)

Spar keeps scoped analyzer settings as path sections in the root `.editorconfig`, so the spike was rerun with a temporary `Probe.Rules/.editorconfig`. It is kept as `results/editorconfig-probe/probe.editorconfig` and was deleted after the run. Its `[*.cs]` section held:
- `msbuild_task_analyzer.run_mt_analyzers_on_all_tasks = true`;
- MSBuildTask0001, 0011 and 0013 at warning.

Results (`results/editorconfig-probe/`):
- **The option works from an `.editorconfig` section.** MSBuildTask0002 rose from 17 to 19, 0003 from 16 to 17 and 0005 from 3 to 4, the same counts as the `all-tasks` global config.
- **The severities work too.** 0001 (14), 0011 (10) and 0013 (1) now appear as warnings in the command-line build log. In the default run 0001 and 0011 appeared only in SARIF, and 0013 not at all.
