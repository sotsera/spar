# Development latency baseline

Measured on 2026-10-22 against merged D3 `0f65de55fa4bf6e2817df6cd144f9d2264321754`
after the maintainer selected startup/save-to-change measurement. The matching solution build passed
without warnings. The working changes add the measurement driver and documentation; product sources
and binaries were not changed between the two sweeps.

## Method and limits

`eng/Measure-DevelopmentLatency.ps1` uses the existing isolated backend fixture and retained session
helpers. Each session builds the project, starts the real frontend/SDK watcher/native app, seeds state,
saves CSS and C# method edits, then saves startup C# to select a real rebuild/replacement. It observes
the changed typed DOM snapshot after the SPA's layout opportunity. Supported edits must retain the
document/mount and backend/frontend counters. Rebuilds must retire the previous process, create a new
document with reset backend state and preserve the original frontend process.

The [baseline](baseline.json) has three starts, nine CSS saves, nine backend saves and three startup
saves. The [confirmation](confirmation.json) has two starts, six saves of each patch kind and two
startup saves. All five sessions passed; all **155 independently retained process instances exited**,
owned-job completion was observed and unrelated sentinels survived. Fixtures/junctions remain for
inspection under `artifacts/development-latency`; they were not recursively deleted.

The first sweep printed an empty console summary because its summary rows were hashtables; its JSON
receipt is complete. The confirmation uses object rows and also retains diagnostic SDK restore/build
durations after exit. The [initial-driver patch](initial-driver.patch) reconstructs the exact original
driver, verified against its receipt's hash. These harness changes alter no timed product path.
[Source hashes](sources.sha256), binary hashes and tooling versions bind the records.

This is one Windows developer machine, SDK 10.0.402, with cached NuGet/npm dependencies and prebuilt
shared tools. Each sweep starts with a new isolated app output; later sessions reuse that project's
outputs. Every session has a fresh WebView profile. No OS/tool cache was cleared: “first” does not
mean a cold machine. The different first-start observations remain separate raw samples.
Stopwatch timings include the file write, public-event logging and 50 ms observer polling; they are
observed DOM latency, not pixel timing or precise sub-50 ms costs. The instrumented source fixture
does not qualify ordinary-package, debugger/editor, production, AOT or clean-machine performance.

## Observations

The [combined descriptive summary](combined.json) retains the sample counts. These are observations,
not confidence bounds or performance budgets; no percentile is inferred from the small sample set.

| Operation | Samples | Minimum | Median | Maximum |
| --- | ---: | ---: | ---: | ---: |
| First project start in a sweep | 2 | 12.80 s | 17.48 s | 22.16 s |
| Repeated project start | 3 | 8.67 s | 8.78 s | 8.81 s |
| CSS save → changed DOM style | 15 | 53.57 ms | 62.55 ms | 72.43 ms |
| C# method save → changed backend read in DOM | 15 | 129.34 ms | 194.15 ms | 872.87 ms |
| Startup C# save → replacement DOM/typed readiness | 5 | 9.99 s | 10.08 s | 16.67 s |

In the first sweep's repeated starts, coordinator build selection took about 1.98–2.00 s;
frontend startup took 0.57–0.58 s; application launch through the SDK watcher to authenticated
admission took about 4.93 s. Native/typed readiness followed roughly 0.52–0.56 s later.
The confirmation trace records each watcher performing another restore (1.04–1.06 s) and build
(1.63–1.70 s), plus graph loading, design-time evaluation and application launch.
These measured restore/build durations explain part of the watcher phase, not all of it.

The 16.67 s rebuild is retained. Its coordinator build-selection interval was 8.29 s versus
3.23 s in the preceding session (build stdout: 8.06 s versus 3.02 s), and watcher admission took
7.15 s versus 5.99 s. Watcher's own recorded restore/build durations stayed near 1.05/1.63 s.
The [phase events](phase-events.json) and local original logs preserve this attribution.
The cause of that extra build/launch delay remains unresolved; no code fix or stable timing claim
is inferred from a later faster sample.

## Next focused target

Target the **full-build and SDK watcher startup path** first. Repeated startup and source-triggered
replacement are the largest observed waits. Keep the existing supported patch path, readiness,
source selection, immutable contracts, single Hot Reload owner and actual retirement boundaries.

1. Profile the selected coordinator build and the SDK's subsequent restore/build/evaluation/launch
   separately, with actual MSBuild timing/binlogs where needed. Repeat the slow case and retain it.
2. Compare an ordinary package consumer with this source-reference fixture before attributing
   repository project-graph cost to application authors. Keep first and repeated starts separate.
3. Evaluate one supported reuse/incremental-build change in an isolated A/B experiment. The
   [SDK watcher](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-watch) owns its build/update
   pipeline; flags understood by `dotnet run` must not be assumed to bypass the watcher's own restore
   or compilation. Do not remove required builds, dependency checks or contract generation on that assumption.
4. Accept a change only with repeatable startup/rebuild improvement and preserved patch state,
   dependency/contract rebuild selection, failed-build recovery, actual cleanup and fresh-consumer
   behavior. Set numerical budgets only after broader observations. Native waiting-window feedback
   remains a separate product scope.

This PR supplies the reproducible baseline and selects the next investigation. It makes no production
optimization, SDK upgrade, timeout increase or automatic performance gate.

Commands: matching solution build; `eng/Measure-DevelopmentLatency.ps1` with default 3×3 samples,
then `-Sessions 2 -EditsPerSession 3` in a separate evidence directory. Original logs, inputs and
all individual source edits remain in ignored local artifacts. Documentation/skills and gate checks
validate the authored guidance; current PR CI qualifies its exact source separately.
