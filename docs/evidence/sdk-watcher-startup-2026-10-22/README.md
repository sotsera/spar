# SDK watcher restore and evaluation investigation

Windows developer-machine investigation on **2026-10-22**, based on merged PR #51,
`2ef5c462d46121ef3bbbddb9eb94c21ac4c305ba`. No runtime, watcher launch arguments, restore
policy, SDK pin or CI selection changed. The delivered change adds diagnostic stage attribution
to the existing manual `Measure-StartupCost.ps1` receipt and records a rejected candidate.

## Result and decision

The supported `RestoreUseStaticGraphEvaluation=true` candidate is **rejected**. Four interleaved
warm restores per variant on a freshly packed ordinary consumer regress from **0.625 s** median
to **0.977 s**. The source graph improves only about **0.03 s** (1.018 to 0.989 s). Neither result
supports changing Spar's restore policy. NuGet documents this option as experimental, intended
to reduce MSBuild graph-reading cost, with possible reference omissions in unusual projects:
[official NuGet guidance](https://learn.microsoft.com/en-us/nuget/reference/msbuild-targets#restoring-with-msbuild-static-graph-evaluation).
Only the root assets hash was compared; unchanged bytes are not dependency-change qualification.
The rejected candidate was not integrated into the coordinator or qualified for Hot Reload.

The actual baseline watcher has separate costs. Final two-session source and fresh-package runs
each cover startup and a rebuilt generation, with four watcher instances per variant:

| Initial watcher work | Source project | Ordinary package consumer |
| --- | --- | --- |
| Separate restore process | 1.015–1.090 s | 0.618–0.637 s |
| Separate build process, `--no-restore` | 1.528–2.817 s | 0.649–0.663 s |
| Project graph loading | approximately 0.3–0.4 s | approximately 0.3 s |
| Design-time build | approximately 0.4–0.6 s | approximately 0.2 s |
| Design-time projects | 4 | 1 |

These are SDK-reported process wall times and rounded in-process observations, not an additive
decomposition of session startup. Restore/build process times already include their MSBuild work.
Do not label the entire restore process as network or NuGet dependency resolution. The source
graph watches framework source; an ordinary package consumer does not evaluate those framework
projects in its Hot Reload workspace. Source-only changes cannot establish a consumer improvement.

The watcher subsequently starts `dotnet run --no-build`, creates its Hot Reload session and
waits for the application. Its eventual run-process lifetime includes application execution and
shutdown; **it is not launch latency**. The new parser deliberately does not report that lifetime
as startup cost. Workspace initialization, `dotnet run` evaluation, native startup and host effects
remain unseparated. They require timestamped profiling before another optimization is selected.

## Restore attribution and first build

An initial exploratory comparison used the same current source graph and the retained earlier
package fixture from the previous investigation. Its binlogs were replayed with the pinned SDK,
including `PerformanceSummary`; replay does not execute a build. The warm normal source restore
reported six skipped restores, a **87 ms RestoreTask**, and six projects' evaluation entries totaling
536 ms. The warm earlier package graph reported two skipped restores, an **87 ms RestoreTask**
and evaluation entries totaling 238 ms. MSBuild task/target totals include nested work and are not
an additive critical path. Static-graph task attribution moves work into `RestoreTaskEx` (615 ms
source, 574 ms earlier package); its smaller outer evaluation summary does not mean that work vanished.

The new, isolated package consumer was scaffolded from five packages built at the baseline revision,
outside the checkout with a private empty NuGet cache. Its first ordinary Debug build took **73.38 s**
inside MSBuild: **68.747 s RestoreTask**, 2.805 s Csc and 0.901 s generator Exec. All succeeded without
warnings. Package index/content responses and vulnerability-cache messages are present; the log does
not separate full body transfer, installation, signature verification, filesystem or host overhead.
This reproduces the earlier cold-restore problem, not a 68-second warm watcher restore. No audit,
signature verification, AOT settings or restore checks were disabled.

The next larger investigation should attribute that cold package acquisition/installation and the
unseparated workspace/launch interval. Changing NuGet evaluation or bypassing SDK startup is not
supported by these measurements. There is no machine-independent performance budget or speedup claim.

## SDK source review

Installed SDK **10.0.402**, runtime **10.0.13**. Its `.version` identifies dotnet/dotnet VMR revision
`a8dd0577c5c13f43a2f57fe33b4ff11b90f6371e`; it is not a commit in dotnet/sdk. The inspected MIT-licensed
sources are under [this exact VMR revision](https://github.com/dotnet/dotnet/tree/a8dd0577c5c13f43a2f57fe33b4ff11b90f6371e/src/sdk/src/Dotnet.Watch):

- `Watch/HotReload/HotReloadDotNetWatcher.cs`: interactive framework/device selection takes restore,
  graph loading and build-only paths, even for this ordinary non-device app. The previous framework
  hint experiment already showed no benefit; it was not repeated as a new candidate.
- `Watch/Build/ProjectGraphFactory.cs`, `ProjectBuildManager.cs` and `EvaluationResult.cs`: graph
  construction and design-time targets prepare the Roslyn workspace. Reusing a collection inside
  one watcher does not provide cross-process/session reuse.
- `Watch/Process/ProjectLauncher.cs`: launch explicitly invokes the selected command with
  `--no-build`, selected framework and Hot Reload launch environment. No direct-executable public
  Hot Reload launch alternative was identified in this review.
- `dotnet-watch/CommandLine/CommandLineOptions.cs` and `Watch/Build/BuildReporter.cs`: public `-bl`
  is recognized, but the internal design-time logger reconstructs only the filename and does not
  preserve `ProjectImports=None`. No internal watcher binlog was collected here. The standalone
  restore/build captures did use `ProjectImports=None`; raw logs remain private local artifacts.

No SDK source was copied into Spar. `--non-interactive` would permit SDK restart on rude edits
([official CLI behavior](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-watch#options));
it cannot replace the coordinator's drain/retirement policy. Persistent build servers, private SDK
test flags and shared compiler processes were not enabled as performance shortcuts. Reusing a watcher
across generations would also need a design for fresh session credentials, immutable contract selection
and actual retirement; it is not a measured or accepted small change.

## Reproduction and retained scope

Prepare source dependencies or a fresh consumer using the [current package guide](../../18-package-consumer.md).
Run [the manual startup driver](../../11-development-session.md#measure-development-latency) with
`-Sessions 2` and a new evidence directory for each variant. The receipt retains the tool/driver hashes,
all samples, phase events, per-generation SDK observations and actual cleanup. The diagnostic parser
accepts dot/comma decimal separators, stops at the first watcher-ready marker and returns incomplete
observations for unknown/truncated logs. Timing completeness does not establish lifecycle acceptance.

For the candidate screen, run this command from each prepared project's directory, first normally,
then with the additional property. Use interleaved normal/static/static/normal twice, retaining every
wall-time sample and exit code; keep project, cache, SDK and binaries fixed:

```powershell
dotnet restore ./Spar.NativeSample.csproj --property:UseSharedCompilation=false --property:HotReloadAutoRestart=false --property:NuGetInteractive=false --property:UseRazorBuildServer=false /nodeReuse:false -consoleLoggerParameters:NoSummary
# Candidate only: append --property:RestoreUseStaticGraphEvaluation=true
```

The final 16-restore comparison has no binlog instrumentation. A separate exploratory 16-restore
comparison captured the first normal/static restore for each project with a unique
`'-bl:<path>.binlog;ProjectImports=None'`. Its 6.032-second source normal sample is retained with
unknown cause; it was not silently removed or counted as a benefit. All 32 restore commands exited 0.

[Measurements](measurements.json) retain both comparisons, every native-session result, package/source
identities and SDK-source hashes. Raw binlogs, replay, process logs, profiles and disposable consumers
remain under ignored `artifacts/sdk-watcher-startup` or its recorded temporary consumer path. The initial
source run used the same attribution logic before adding the helper-hash receipt field; final source
and package runs include the complete metadata. Both initial source samples remain in the record.

All six native sessions verified actual retirement before replacement, frontend survival, typed readiness,
owned exit (186 independently retained processes) and unrelated-sentinel survival. Definition-only helper
imports, actual forced teardown and unfinished-handle retention passed `Test-IntegrationHelpers.ps1`.
Manual missing/truncated-log, dot-decimal and private-argument canary checks passed; actual source/package
traces cover comma decimals. Documentation links, six skills/14 definitions and all six CI-gate tests passed.
The [source hashes](sources.sha256) bind the diagnostic helper, driver and current-guide changes.
No visual/editor/AOT acceptance was requested or inferred from
these timing runs. The product remains the already merged #51 baseline, whose
[main run](https://github.com/sotsera/spar/actions/runs/39854037011) passed all six checks. New hosted checks
qualify this diagnostic/documentation change separately.
