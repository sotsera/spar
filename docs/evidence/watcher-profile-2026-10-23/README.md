# First C# update and watcher launch profiling

Measured 2026-10-23 on clean merged main `1d5e05eecf12a29d1873f3b332eccd492ae79c66`
(PR #55, whose [integrated-main run](https://github.com/sotsera/spar/actions/runs/39968791351)
passed all five suites and Windows managed probe). This follows the
[latency comparison](../development-latency-comparison-2026-10-23/README.md).
There is no product, dependency, timeout or readiness change in this profiling delivery.

## Findings and next scope

The first supported C# update spends substantial CPU in the **SDK worker's compiler and source
generator pipeline before the runtime callback**. The source fixture's four-project watch workspace
costs more than the fresh package fixture's one-project workspace. SDK/native readiness does not
mean background compiler preparation has finished. A ten-second diagnostic idle moves much of
that work earlier, but still leaves a slower first update. Adding a startup delay would conceal the
cost rather than remove it.

Watcher startup also needs two separate investigations. Most source launches spend about five
seconds before the SDK starts its `dotnet run --no-build` child; the package fixture spends about
2.6 seconds. Independently, three untraced launches spend **9–18 seconds after native process
creation and before authenticated admission**. These native waits are not SDK restore/evaluation
time, and this trace does not establish their cause.

The recommended next experiment is to capture a reproduced **native pre-admission outlier** with
process creation, loader/CPU/wait evidence and Spar admission markers. It is the largest remaining
unattributed delay here. Use vendor or kernel evidence before attributing it to security scanning.
Separately, evaluate a controlled source-workspace reduction only if it preserves framework source
edits, coordinated restart, contract identity and the single SDK Hot Reload owner. The package
comparison motivates that hypothesis; it does not validate a source-reference redesign. No generator
is disabled, no build is skipped, and no synthetic update or second Hot Reload owner is introduced.

## Method and qualification

Windows 10.0.26220, .NET SDK 10.0.402, Node 24.21.0, npm 11.19.0 and PowerShell 7.6.5;
the same developer machine as the preceding record. Each case uses a new isolated instrumented
fixture, two watcher generations, three CSS saves and three supported C# body edits per generation.
A real startup-source save causes replacement between generations. Frontend dependencies and shared
tools are prepared before timing. There is no OS, antivirus or global dependency-cache reset.

[All 30 C# observations](runs.json), including first/later order, SDK-reported update durations,
background CPU observation boundaries and cleanup, remain separate by case. These small sequential
diagnostic runs are not an interleaved A/B benchmark or a new comparison against the original baseline.
Different project topology, cache/background state and tracing affect comparisons. Every source case
uses four projects in the SDK workspace; the package case uses one. The latter is the **same integration
fixture supplied through packages**, not the ordinary minimal app or a representative consumer population.

Edits start immediately after readiness, seeded state and a CSS update unless the case explicitly
waits ten seconds. Elapsed time includes the source write, event/log observation and 30 ms observer
polling, ending at a typed DOM snapshot. CPU is the retained SDK worker's `TotalProcessorTime` delta
over that interval, including any concurrent background work. CPU can exceed elapsed time through
parallel work. Worker discovery verifies the pinned `dotnet-watch.dll`, project path, ancestry and
creation time against retained process handles. The outer `dotnet watch` CLI launcher is a different,
mostly idle process; its initial control's CPU values are excluded rather than treated as worker zeros.

Native lifetime/state checks passed in all five successful cases: **30 CSS saves, 30 supported C#
saves and five startup-source replacements**. Supported edits retained document/mount and both counters
without restarting the application. Old native applications exited before replacement watcher launch.
Each session recorded only one frontend start and one frontend-ready event across its two generations.
All **155 independently retained process instances exited**, all five coordinators exited 0, owned-job
completion was observed, no explicit forced cleanup was requested and unrelated sentinels survived.
Both successful trace collectors also exited 0 with completed output. A final process inventory found
no matching fixture/collector process. These are terminal/native DOM and lifecycle checks, not reviewed
pixels, editor/debugger gestures, production or AOT acceptance.

## First C# update

Times below are individual save-to-DOM observations, not medians. Generation 2 follows the real
startup-source replacement. The first control is retained despite its invalid SDK CPU target.

| Case | First save, generation 1 / 2 | Later saves, all four | Worker CPU during first save, generation 1 / 2 |
| --- | --- | --- | --- |
| Source immediate, launcher counter excluded | 1,101 / 2,347 ms | 79–214 ms | unavailable |
| Source immediate, worker counter | 2,054 / 2,239 ms | 118–208 ms | 2,219 / 2,453 ms |
| Source with worker trace | 2,077 / 2,314 ms | 149–208 ms | 2,297 / 2,484 ms |
| Source after ten-second idle | 894 / 997 ms | 161–206 ms | 750 / 828 ms |
| Fresh package fixture, immediate | 926 / 1,194 ms | 139–213 ms | 750 / 1,219 ms |

The source immediate worker control reports 1,801 / 1,788 ms inside the SDK's first update, compared
with 28–49 ms for its later updates. Across all cases the SDK's first updates took 700–2,119 ms,
later ones 24–69 ms. Runtime-update event to DOM snapshot took **3.47–25.63 ms** across all 30 edits.
This puts most first-update latency before runtime notification, rather than in bridge publication
or the rendered read. Event timestamps and the parent Stopwatch have different observation boundaries;
their intervals should not be treated as exact internal function timings.

The idle comparison has a concrete mechanism, not just elapsed correlation. In the pinned SDK
[ManagedCodeWorkspace.PrepareCompilation](https://github.com/dotnet/sdk/blob/d037e5f857b35e7260f575ae026ef30511776c0f/src/Dotnet.Watch/Watch/HotReload/ManagedCodeWorkspace.cs),
`GetCompilationAsync` is started without awaiting completion. The
[watch loop](https://github.com/dotnet/sdk/blob/d037e5f857b35e7260f575ae026ef30511776c0f/src/Dotnet.Watch/Watch/HotReloadDotNetWatcher.cs)
calls it after launching the application, before waiting for changes. This is SDK 10.0.402's source,
tag `v10.0.402`, commit `d037e5f857b35e7260f575ae026ef30511776c0f`; it is not a contract about future SDKs.

The ten-second idle records **1,844 / 3,156 ms of SDK CPU** between its first and last CPU samples
after DOM readiness and before the first C# edit, versus 125 / 141 ms for the immediate worker control.
The sampled boundaries omit the short edges between readiness/save and CPU sampling; exact boundaries
are retained in `runs.json`. This supports background preparation as a contributor. Separate fresh
fixtures and uncontrolled host activity prevent assigning the entire difference to that one cause.
Readiness should continue to describe application availability, not an unobservable compiler warm-up.

## Worker stack evidence

Two completed 45-second EventPipe captures target the actual SDK worker, using `dotnet-trace`
10.0.750501 and its TraceEvent 3.1.23 reader:

```powershell
dotnet-trace collect --process-id <verified-worker-pid> --profile dotnet-common,dotnet-sampled-thread-time --buffersize 64 --duration 00:00:45 --output <unique-generation.nettrace>
```

[Compact stack evidence](stack-evidence.json) preserves coverage timestamps, the sole expected PID
per capture, **zero events lost**, edit windows, sample types and named generator frames. Both captures
cover all three edit windows. Their attachment occurs after worker launch, so their startup window
is incomplete. They do not capture the native application's loader or the separate restore/build processes.

| First-edit window | Managed samples | External samples | Managed samples with generator pipeline | Managed samples with Edit and Continue |
| --- | ---: | ---: | ---: | ---: |
| Generation 1 | 115 | 3,518 | 62 | 23 |
| Generation 2 | 151 | 5,880 | 75 | 40 |

These are inclusive **sample counts, not CPU percentages**. `dotnet-sampled-thread-time` also samples
blocked/external threads; only samples marked `Managed` enter the stack analysis. The two work buckets
can overlap. All counted managed samples had stacks. Named first-update frames include
`Microsoft.Interop.SignatureContext.Create`, `MarshallingInfoParser`, `LibraryImportGenerator` and
`System.Text.Json.SourceGeneration.JsonSourceGenerator`. Subsequent updates contain only 2–7 managed
samples per window. This supports cold compiler/generator work in the observed worker; it neither
proves a particular generator is defective nor fully divides managed/native JIT or loader cost.
The Info-level profile does not supply complete verbose JIT-start/assembly-loader spans. Zero such
events in the exploratory decoder are **unobserved**, not proof that JIT/loading costs are zero.

## Launch, native creation and admission

[All ten launch decompositions](launch-breakdown.json) use retained process creation timestamps for
the watcher launcher, SDK `dotnet run --no-build` child and native application; authenticated admission
is the coordinator's application `process-started` observation. The end is `application-ready`, which
includes the DOM readiness check. These are consecutive boundaries. Numeric SDK restore/build/graph/
design-time diagnostics are also retained, but those internal durations can overlap and must not be
added as if they were an independent critical path.

| Case / generation | Watcher creation → run child | Run child → native creation | Native creation → admission | Admission → DOM |
| --- | ---: | ---: | ---: | ---: |
| Source initial control / 1 | 5.090 s | 0.540 s | **17.818 s** | 0.691 s |
| Source initial control / 2 | 5.079 s | 0.549 s | 0.956 s | 0.463 s |
| Source traced / 1 | **17.195 s** | 0.553 s | 0.991 s | 0.645 s |
| Source traced / 2 | 5.245 s | 0.553 s | 0.962 s | 0.437 s |
| Source ten-second idle / 1 | 4.969 s | 0.546 s | 2.717 s | 0.639 s |
| Source ten-second idle / 2 | **9.391 s** | 0.678 s | 1.048 s | 0.541 s |
| Source worker control / 1 | 5.094 s | 0.559 s | 0.970 s | 0.610 s |
| Source worker control / 2 | 5.064 s | 0.573 s | **9.086 s** | 0.482 s |
| Package immediate / 1 | 2.662 s | 0.553 s | **10.958 s** | 0.692 s |
| Package immediate / 2 | 2.602 s | 0.553 s | 0.882 s | 0.437 s |

The ten-second idle happens **after** readiness; it is not part of the launch intervals in this table.
Usual source restore/build processes took about 1.0–1.1 / 1.5–1.6 s, graph loading 0.3–0.4 s and
design-time work 0.6–0.8 s. The slow traced launch instead recorded 6.540 / 7.666 s for restore/build;
the slow idle-case launch recorded 3.980 s build and 2.2 s design-time work. Both are retained as
diagnostic outliers, not a typical launch estimate or attribution to tracing. Package restore/build
took 623–624 / 653–663 ms, graph loading about 0.3 s and design-time work about 0.2 s.

The roughly 0.55 s `dotnet run` interval is measurable, but replacing the SDK's launch mechanism would
need separate public-API/lifetime evidence. The native pre-admission outliers also occur without
tracing and across source/package topologies. Previous
[Defender evidence](../startup-diagnostics-2026-10-23/README.md) is a separate capture and does not
establish antivirus attribution for these runs.

## Reproduction and retained inputs

[Profile.ps1](Profile.ps1), [Analyze.ps1](Analyze.ps1) and [Launch-Breakdown.ps1](Launch-Breakdown.ps1)
are archived diagnostic sources, run from the repository root against the recorded revision. They
are not a new CI suite or supported product command. Shared helper [hashes](sources.sha256) identify
the existing fixture, process, session, timing and package preparation implementations.

Prepare a matching solution build and frontend dependencies before sessions; the recorded build
used `--disable-build-servers` and a unique `-bl:<path>;ProjectImports=None` binlog. It passed with
zero warnings/errors. The final reproduction driver locates the worker, reads live SDK output with
shared read/write access, and retains only safe phase observations. For traces it expects the pinned
local tool at `artifacts/coreclr-startup/tools/dotnet-trace.exe`; the decoder expects the corresponding
10.0.750501 tool store. These paths may be prepared with `dotnet tool install dotnet-trace --version
10.0.750501 --tool-path artifacts/coreclr-startup/tools`. SDK source was inspected from the official
revision above under MIT; no upstream implementation is copied into a shipping project.

```powershell
# Choose fresh names; the driver refuses existing evidence directories.
pwsh -NoProfile -File docs/evidence/watcher-profile-2026-10-23/Profile.ps1 -Name source-control
pwsh -NoProfile -File docs/evidence/watcher-profile-2026-10-23/Profile.ps1 -Name source-trace -Trace
pwsh -NoProfile -File docs/evidence/watcher-profile-2026-10-23/Analyze.ps1 -Name source-trace
pwsh -NoProfile -File docs/evidence/watcher-profile-2026-10-23/Profile.ps1 -Name source-idle -IdleMs 10000
```

The launch extractor enumerates the five historical case names and uses their retained coordinator
logs plus the external consumer path in `artifacts/watcher-profile/consumer-path.txt`; adapt those
inputs for newly named runs. The final driver includes CRLF phase parsing and optional package
input added during the investigation. Earlier successful source runs used its earlier live parser;
their launch splits were recovered offline from complete logs and retained identities.

For the package case, only after source sessions/collectors had actually exited, `eng/Pack-Local.ps1`
created five fresh packages ([package hashes](packages.json)). `eng/New-PackageConsumer.ps1
-IntegrationFixture` created a new consumer outside the checkout, using its own `.feed` and `.packages`.
The initial cold `dotnet build backend/Spar.NativeSample.csproj --disable-build-servers` (40.65 s,
zero warnings/errors) and `npm ci --prefix <consumer>/backend/frontend --ignore-scripts --include=optional`
passed before timed launch. The assets file resolved Spar through packages, with only the consumer's
own contract project reference. Run the driver with `-ConsumerDirectory <fresh-consumer>`. The private
cache is warm during the measured watcher launches; this is not a cold NuGet/npm restore result.

First failures remain in ignored local evidence: an initial PowerShell parse failure before launch,
a live log-sharing error (followed by session retirement and a zero-matching-process inventory), and
a collector-ready check that incorrectly expected interactive redirected output and targeted the
launcher. Its partial trace is excluded. An initial offline decoder compilation missed its
FastSerialization reference and was corrected before analysis. None is counted as successful SDK
profiling or erased by a passing rerun. Large raw traces/binlogs, absolute fixture paths, full SDK
logs containing private launch arguments, caches and generated files remain ignored locally. Durable
records retain public observations and excerpts rather than those private inputs.
