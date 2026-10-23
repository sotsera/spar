# Cold .NET restore and repeated frontend installation

Investigation and implementation on Windows, **2026-10-23**, from merged PR #52,
`b2418796a3e4b956a40e50e5b523cce9056f4853`. The maintainer authorized adoption only if cold
restore improved and checks passed, plus the demonstrated npm fixes. This is build/development
tooling; production runtime code, SDK/package versions and Hot Reload ownership are unchanged.

## Cold restore comparison

Six new ordinary package consumers used the same five baseline packages, SDK 10.0.402 and source.
Every sample had a new empty private NuGet packages directory and a new empty HTTP-cache directory.
No shared cache was cleared, and audit/signature verification remained enabled. Sample order was
normal/reduced/reduced/normal/normal/reduced; OS, certificate and upstream service caches were not reset.

| Variant | Individual restore wall times | Median | Private package-cache files |
| --- | --- | --- | --- |
| Original downloads | 62.46, 30.29, 37.91 s | 37.91 s | 366.8 MB |
| Reduced downloads | 33.08, 22.70, 20.66 s | 22.70 s | 185.7 MB |

The measured median improved about **40%**. These are three observations per variant on one machine,
not a latency budget or attribution of every second. Cache byte counts include package archives and
expanded files; they are not measured network bytes. [Retained samples and identities](measurements.json)
include all observations. Raw standalone binlogs/logs stay under ignored `artifacts/restore-costs`.

The pinned SDK's `ProcessFrameworkReferences` treats the analyzer-required ILLink pack as a reason
to process runtime components, even with `SelfContained=false`. Default discovery requested CoreCLR,
ASP.NET Core and Windows Desktop packs for this app, whose sole framework reference was CoreCLR.
The documented [DisableTransitiveFrameworkReferenceDownloads property](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#disabletransitiveframeworkreferencedownloads)
removed the latter two packs. The installed SDK source is the MIT-licensed
[dotnet/dotnet VMR revision a8dd057](https://github.com/dotnet/dotnet/blob/a8dd0577c5c13f43a2f57fe33b4ff11b90f6371e/src/sdk/src/Tasks/Microsoft.NET.Build.Tasks/ProcessFrameworkReferences.cs).
No upstream implementation was copied.

## Rejected all-configuration setting and guarded delivery

A realistic dependency counterexample prevented shipping the property unconditionally. A console
application referenced a library that used `DefaultHttpContext` and declared an ASP.NET Core framework
reference. A warm-cache publish initially passed. Repeating with an empty package cache failed
**NETSDK1185**, missing `Microsoft.AspNetCore.App.Runtime.win-x64`. Warm caches had masked the problem.
The failed exploratory logs remain under `artifacts/restore-costs/framework-probe`.

The delivered default therefore applies only when **Debug**, not self-contained, not ReadyToRun,
not publishing AOT, and the user has not already selected a value. Source delivery targets and the
fresh consumer's early project properties agree. Release and explicit deployment configurations
retain ordinary discovery; `false` is an explicit development opt out. Development AOT/trimming
analyzers remain enabled. There is no unsupported-API fallback or altered production selection.

`Test-RuntimePackRestore.ps1` checks source and freshly packed consumer graphs: one Debug runtime
pack, three on explicit opt out, preserved Release packs, three self-contained Debug packs, and four
ReadyToRun packs including Crossgen2. It also cold-publishes and executes a real transitive ASP.NET
Core dependency together with the existing DynamicMethod dependency under attributable CoreCLR
selection. Expected output is `Hello from generated code, Spar/200`.

The same graph assertion fails against the original target: Debug still requests all three packs.
An initial regression-harness expectation omitted Crossgen2 from ReadyToRun; its failure is retained
under `runtime-source`. The corrected six-check source and package executions pass. Neither that
harness correction nor the rejected global setting is counted as successful qualification.

## npm installation reuse

The earlier package-suite trace performed six clean installs: roughly 18 s initially and 8 s each
thereafter. Local `npm install` over an existing tree was faster, but changing the installer would
permit lockfile repair during publication. The delivered installer remains
`npm ci --ignore-scripts --include=optional`.

`Restore-Frontend.ps1` hashes manifests/lockfiles, effective npm configuration, npm-related environment,
local/ancestor/user/global configuration files, Node/npm identity and its own payload. Raw configuration
and credentials stay in memory. Before reusing an installation it hashes the paths and contents of
all ordinary installed files. Missing/modified files or changed inputs cause clean installation.
Linked/workspace trees conservatively keep clean installs; the root `node_modules` cannot be a junction
or symlink. An interrupted/failed installer cannot leave reusable state. Clean removes only the owned
intermediate receipt. `SparForceFrontendRestore=true` requires installation explicitly.

Contract resolution still runs before frontend compilation. Every production build still executes
the frontend build, verifies notices and creates a current matching archive. Dependency reuse never
trusts `dist`. The helper is shipped only as a private build tool. CI also uses setup-node's npm
download cache, keyed by the three committed lockfiles; `node_modules`, consumer NuGet caches and
compiled/generated outputs are not cached. Cache restoration is not a test result.

On the ordinary frontend's warmed dependency tree, the new helper measured 0.959, 0.959 and 0.967 s
for reuse, versus 2.749, 2.658 and 2.675 s with its forced-install path. This includes helper startup,
configuration and file verification. It is not an original-target A/B comparison: the prior bare
`npm ci` comparison was about 1.34 s locally. First installs pay the new verification overhead.
No hosted cache-hit speedup is claimed from local timings.

`Test-FrontendRestore.ps1` runs real npm against a local locked tarball through the actual source or
packaged asset target. Fourteen assertions verify initial/repeated/source-edited builds, manifest/
lock/configuration/tool changes, missing/corrupt installed files, explicit force, failed-install recovery,
design-time suppression and clean ownership. The produced archive must reflect the changed frontend
source. Npm's own timing records establish actual `ci` invocations. The original target fails the same
second-build assertion with two installs instead of one. No missing-symbol failure is used as detection.

## Validation and limits

The source and newly packed build targets have matching hashes. Four new fixed catalog entries run
source/package frontend-restore and runtime-pack checks, for **56 catalog checks**. Suite selection,
required gate, fatal dependency failures and existing native checks remain. The compatibility driver's
explicit source-copy list includes the new helper, so its fixtures use the actual integrated build.

Matching solution compilation passed without warnings; **75 managed cases passed**, none skipped.
The ordinary source and fresh-package native checks passed typed greeting, frontend/backend HMR,
retained state, matching rebuilt generation, actual retirement and unrelated-process survival.
The fresh package's copied NativeAOT payload passed production-boundary and exit checks; reviewed
images show retained update state, reset restart state and the connected published greeting.
The instrumented external consumer also passed public package HMR/restart, copied NativeAOT and
attributable CoreCLR execution, expected strict IL3050 rejection, notices and production exclusion.
Reviewed captures show connected AOT and dynamic-code CoreCLR greetings.

Two full local source compatibility attempts stopped at the first copied CoreCLR greeting deadline.
The first emitted no probe event before the unchanged 30-second limit. The second reported its window
at 27.43 s, bridge admission at 27.98 s and document loading at 27.99 s, but no completed greeting before
the deadline. Their failure receipts and forced cleanup remain retained. Root exit and absence of
remaining processes matching each failed copy/profile were checked separately. Neither cancellation
nor the timeout itself is counted as cleanup completion. The missing first event does not establish
that managed entry had not executed; its exact delay is unknown.

A separate diagnostic run of the first unchanged publication, with public .NET host tracing enabled,
passed the same 30-second checks and completed its greeting in 4.28 s. This does not resolve the earlier
delay or turn the failed source suite into a pass. No automatic retry, longer deadline, disabled check
or runtime workaround was added. Full hosted source compatibility qualification remains separate.
Actual VS Code source and package debugger lanes passed on VS Code 1.143.0, C# 2.160.4 and
C# Dev Kit 3.40.215. Each verified 22 retained process exits. Reviewed captures show yellow frontend
updates, `Hello debugger, Spar`, retained counters and connected state. Hosted results are recorded
with the final PR; no new Visual Studio walkthrough or clean-machine execution is claimed.

### First hosted run and standalone source-fixture repair

[Run 39938407432](https://github.com/sotsera/spar/actions/runs/39938407432), signed head `c860ea4`,
tested merge `3fcf7f0db93072413be69941458451aa8c8d6e55`, passed Fast, Packages and Delivery,
including full source compatibility. All 75 managed cases passed without skips. Processes passed
its new npm check but the source runtime-pack check failed before assertions: `New-BackendFixture`
tried to create a junction to absent sample `node_modules`. Processes deliberately has no SPA install.
This was an accidental harness dependency, not runtime-pack regression evidence.

The repaired source check copies only the ordinary SDK project and resolves its imports/references
to the source graph. Restore does not require a frontend fixture or junction. Its six checks pass
locally, and the same original-target comparison still fails on the extra Debug runtime packs.
No assertion, suite membership or installer requirement was weakened. The first run's failed status
and diagnostics are retained; a new exact-head run must qualify the repair. Development and the
aggregate had not finished at repair publication; normal PR concurrency may cancel superseded work.

Normal reproduction uses the [suite entry points](../../21-continuous-integration.md). Focused commands:

```powershell
pwsh -NoProfile -File eng/Test-FrontendRestore.ps1 -EvidenceDirectory artifacts/frontend/source
pwsh -NoProfile -File eng/Test-RuntimePackRestore.ps1 -EvidenceDirectory artifacts/packs/source
pwsh -NoProfile -File eng/Pack-Local.ps1 -Output artifacts/restore/feed
pwsh -NoProfile -File eng/Test-FrontendRestore.ps1 -Feed artifacts/restore/feed -EvidenceDirectory artifacts/frontend/package
pwsh -NoProfile -File eng/Test-RuntimePackRestore.ps1 -Feed artifacts/restore/feed -EvidenceDirectory artifacts/packs/package
```

For cold comparison, scaffold a new consumer per observation and give each process a new
`NUGET_HTTP_CACHE_PATH`; the scaffold supplies its private empty packages directory. Run `dotnet restore`
with the same SDK/configuration and add `-p:DisableTransitiveFrameworkReferenceDownloads=false` only for
the original-discovery control. Keep normal audit and signature verification. Never clear another
session's caches or recursively delete fixture junctions. [Source hashes](sources.sha256) and the compact
measurements bind these claims; raw profiles, captures and diagnostic output remain local artifacts.
