# Development latency after the startup-cost changes

Measured 2026-10-23 on clean merged main `f0def53bbae499ff4096e428bf5270aa0497f226` (PR #54).
Its [integrated-main run](https://github.com/sotsera/spar/actions/runs/39960760572) passed all five
suites and Windows managed probe. The comparison is against the
[original 2026-10-22 baseline](../development-latency-2026-10-22/README.md) on merged D3
`0f65de55fa4bf6e2817df6cd144f9d2264321754`. No product or measurement code changed for this comparison.

## Method and qualification

The original confirmation driver and current `eng/Measure-DevelopmentLatency.ps1` have identical
SHA-256 `D7481B5713958FCB6C6A931678F2BFF27FECF1E0594D0808956D584FAF766028`.
The original first sweep used its recorded earlier summary/parser variant; neither changed a timed
product path. Current measurements repeat the same three-session and two-session sweeps, each with
three CSS saves and three supported C# saves per session, followed by one startup-source save.

```powershell
dotnet build Spar.slnx --disable-build-servers
npm ci --prefix fixtures/Spar.NativeSample/frontend --ignore-scripts --include=optional
pwsh -NoProfile -File eng/Measure-DevelopmentLatency.ps1 -Sessions 3 -EditsPerSession 3 -EvidenceDirectory artifacts/development-latency/main-2026-10-23/baseline
pwsh -NoProfile -File eng/Measure-DevelopmentLatency.ps1 -Sessions 2 -EditsPerSession 3 -EvidenceDirectory artifacts/development-latency/main-2026-10-23/confirmation
```

The matching solution build passed with zero warnings/errors. The same Windows machine, OS
10.0.26220, processor, SDK 10.0.402, Node 24.21.0, npm 11.19.0 and PowerShell 7.6.5 match the
original receipts. Current [baseline](baseline.json) and [confirmation](confirmation.json) retain
the clean source revision, driver/coordinator/development assembly hashes, all samples, timings
and cleanup. [Source hashes](sources.sha256) bind the measurement helpers.

Both current sweeps passed: five starts, 15 CSS saves, 15 supported backend saves and five real
startup-source rebuilds. All **155 independently retained process instances exited**; all five
coordinators exited 0, owned-job completion was observed, no explicit forced cleanup was requested,
and unrelated sentinels survived. Supported edits retained native document/mount and both counters;
replacements retired the old process, reset backend state and retained the original frontend.
No failed measurement or skipped session was replaced by a passing rerun.

Shared tools and dependencies were prepared before timing. Each sweep creates a new isolated app
output, later sessions reuse that project, and every session has a fresh WebView profile. No OS,
NuGet, npm or antivirus cache was reset. This is a historical comparison, not an interleaved A/B
experiment: background activity and host/cache state were not controlled. The matching recorded
versions do not establish identical system conditions. Timings include the source write, public
event logging and 50 ms observer polling, and end at a typed DOM snapshot after a layout opportunity.
They are not pixel, editor/debugger, package-consumer, production, AOT or cold-machine timings.

## End-to-end comparison

[Comparison data](comparison.json) preserves original/current counts, minima, medians and maxima.
Positive changes below mean a longer observed median; these are descriptive small-sample results,
not confidence bounds, a regression threshold or a performance budget.

| Operation | Samples per revision | Original median | Current median | Change |
| --- | ---: | ---: | ---: | ---: |
| Repeated project start → DOM readiness | 3 | 8.780 s | 9.447 s | +7.6% |
| CSS save → changed DOM style | 15 | 62.55 ms | 67.18 ms | +7.4% |
| Supported C# save → changed read in DOM | 15 | 194.15 ms | 194.57 ms | +0.2% |
| Startup-source save → replacement DOM readiness | 5 | 10.084 s | 10.527 s | +4.4% |

These sweeps do **not demonstrate an end-to-end speedup**. Supported C# edit medians are effectively
unchanged; the CSS difference is 4.63 ms, much smaller than the observer's polling interval.
The recorded startup/rebuild medians are longer, but this historical comparison cannot establish
that a particular merged change caused a regression.

First starts remain separate: original **22.157 / 12.795 s**, current **34.270 / 13.145 s**.
Two observations per revision do not support a stable cold-start claim. All repeated-start samples
are also retained: original **8.809 / 8.670 / 8.780 s**, current **15.156 / 9.223 / 9.447 s**.
The 15.156 s repeated start is not omitted from the median or maximum.

An exploratory split by edit order exposes a difference hidden by the aggregate C# median.
[All first/later edit samples](backend-edit-order.json) are retained, with the same five first
edits and ten later edits per revision:

| Supported C# save | Original range / median | Current range / median |
| --- | --- | --- |
| First in each session (5) | 641–873 ms / 708.52 ms | 1,013–2,089 ms / 1,968.85 ms |
| Later in the same session (10) | 129–221 ms / 185.88 ms | 163–227 ms / 192.80 ms |

The current first update is slower in all five observed sessions than the original first-update
range. This is a repeatable pattern in these sweeps and a profiling target, not attribution to a
particular change. The ten later saves dominate the aggregate median. Their 6.92 ms median
difference is smaller than the observer polling interval. The original 16.665 s replacement
outlier remains in the comparison; current replacement samples range from 10.357 to 10.894 s.

## Where the elapsed time went

[Phase events](phase-events.json), [individual intervals](phases.json) and
[phase summaries](phase-summary.json) compare the same public event boundaries. Initial-stage
medians exclude the first project start in each sweep; replacement medians include all five saves.

| Interval | Original median | Current median |
| --- | ---: | ---: |
| Repeated-start coordinator build selection | 2.002 s | 1.835 s |
| Replacement coordinator build selection | 3.245 s | 3.021 s |
| Repeated-start watcher launch → authenticated admission | 4.932 s | 5.732 s |
| Replacement watcher launch → authenticated admission | 5.775 s | 6.435 s |
| Repeated-start admission → DOM readiness | 0.542 s | 0.583 s |
| Replacement admission → DOM readiness | 0.425 s | 0.399 s |

The build-selection saving is consistent with the earlier
[controlled incremental-generation experiment](../watcher-startup-cost-2026-10-22/README.md),
but these historical observations do not independently isolate its cause. The longer watcher-launch
interval masks that smaller saving in end-to-end startup/rebuild observations.

[SDK diagnostics](watcher-timings.json) retain numeric results parsed after exit with the existing
`Read-WatcherTimings` helper. All 20 original/current watcher logs had complete first-ready evidence.
Most current SDK builds took 1.461–1.522 s versus roughly 1.629–1.695 s in ordinary original samples;
one current repeated start instead spent **7.440 s** in the SDK build and **11.454 s** from watcher
launch to admission. The original first start likewise retained a 6.677 s SDK build. Current restore
processes took 1.001–1.090 s, graph loading approximately 0.3–0.4 s and design-time work approximately
0.5–0.7 s. Process/task/graph timings may overlap or contain one another; do not add them as a
complete startup critical path. Raw logs, including private launch arguments, remain ignored locally.

The 34.270 s first start also contains an **8.738 s build-selected → starting-frontend gap**,
versus approximately 0.143–0.149 s in the other four current starts. Its parent Stopwatch interval
exceeds the retained process-creation → application-ready timestamp interval by **8.940 s**.
[Startup gaps](startup-gaps.json) retains this residual for every run. It includes launch/parent
and observer work and is not a direct measurement of a particular function or file wait. No new
CPU, EventPipe or vendor/kernel trace was captured during these timing sweeps, so neither this
gap nor the slow SDK build is attributed to antivirus. The separate
[Defender investigation](../startup-diagnostics-2026-10-23/README.md) does not establish that cause here.

## Next measured target

Keep the build/restore improvements, whose separate experiments cover their specific costs.
These cached sweeps do not exercise the earlier empty-cache package restore savings or repeated
production npm installation. Their results do not invalidate those differently scoped measurements.

The largest remaining measured stage is **SDK watcher launch through admission**, especially during
source-triggered replacement. The **first supported C# update in each session** is a second concrete
target that the aggregate median obscures. A next investigation should split restore/build/graph/
design-time work, actual application launch and first-update preparation, using a controlled
comparison and a trace of any reproduced long wait before choosing another change. The first-launch
gaps also need bounded parent/child observations if they recur. No timeout increase, automatic retry,
skipped build, new Hot Reload owner, security-policy change or production change is implemented here.
