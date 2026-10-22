# Full-build and SDK watcher startup costs

The maintainer selected this investigation after merging [PR #50](https://github.com/sotsera/spar/pull/50)
as `92772675c4a82fc13a747d1f8ea1470f5d7e479c`. Its
[integrated-main run](https://github.com/sotsera/spar/actions/runs/39842709784) passed all five suites
and Windows managed probe. The [earlier latency baseline](../development-latency-2026-10-22/README.md)
remains bound to its own instrumented source fixture.

## Findings and selected change

A fresh ordinary package consumer's first Debug build took **83.97 s**: RestoreTask accounted for
79.725 s, Csc 2.358 s and Exec 1.093 s. Task durations are inclusive diagnostics, not additive critical
paths. Its isolated cache acquired ILLink and Windows/.NET/ASP.NET runtime packs 10.0.13. Requests
reported short HTTP response times before much later installation completion. These logs do not
separate transfer, extraction, signature checks and host contention; cached vulnerability responses
do not demonstrate an audit delay. The consumer had SelfContained=false. No dependency checks,
analyzers or restore requirements were disabled.

The first ordinary source-reference build took **6.08 s**: RestoreTask 0.252 s, Csc 3.042 s and Exec
0.830 s. Source references and a consumer's empty isolated package cache are different populations.
Neither first build is a cold-machine benchmark. Shared tools, npm packages and machine caches
were already present. Raw binlogs and their diagnostic replays remain under ignored
`artifacts/watcher-startup`, including the original long restore.

Contract generation ran on every full build, even with unchanged inputs. The change uses MSBuild
incrementality for that generator invocation. It tracks the contract, generator payload, owning
target, SDK version and input identities. A completion marker becomes current after successful
generation even when the generator correctly preserves identical output bytes. Missing any of
the three generated files invalidates completion. Compilation/resource registration and frontend
copying still run every build; Clean registers only the five owned intermediate files.

## Controlled build and native comparison

[All individual samples and phase/cleanup receipts](measurements.json) retain the baseline/candidate/
baseline sequence on the same ordinary source-reference project. After each target switch, a prime
build completed before five unchanged full builds and two real native sessions. Each native session
also performed an unchanged-source command rebuild. Only the contract target changed; the coordinator
and runtime binaries were identical. No build/pack ran concurrently with owned native sessions.

| Series | Full-build median (5 samples) | Native startup (2 samples) | Command rebuild (2 samples) |
| --- | ---: | --- | --- |
| Baseline A | 2.012 s | 9.118, 18.718 s | 8.002, 8.051 s |
| Incremental generation | 1.819 s | 8.501, 8.398 s | 7.660, 7.635 s |
| Baseline B | 1.985 s | 8.871, 8.707 s | 8.004, 7.986 s |

The build saving is **0.17–0.19 s**, about 8–10%, in this warmed source graph. Command rebuilds
improved by about 0.35–0.4 s in this small comparison. This is a modest improvement, not removal of
the SDK startup cost. The 18.718 s startup and 8.101 s standalone build remain included; medians do
not erase them or resolve their cause. All six sessions passed and all **186 retained process
instances exited**, with owned-job completion and unrelated-sentinel survival.

Measurements use SDK 10.0.402 on one Windows developer machine. Stopwatch intervals include launch,
logging and the existing 50 ms observer polling. Startup waits for native/typed and watcher readiness;
restart waits for the replacement's native/typed readiness after actual retirement. These are not
pixel, editor, source-save or clean-machine timings. Every session uses a fresh browser profile,
while project outputs and dependency caches remain warm. No automatic timing budget is introduced.

## Rejected watcher experiment

The SDK's second restore/build is not redundant merely because Spar already built the application.
Passing the selected `--framework net10.0-windows` to `dotnet watch ... run` still left a separate
restore. The pinned SDK also selects a device in this path. Package-consumer return controls showed
essentially identical warmed startup/rebuild times with and without the hint; it was discarded.

The exploratory `source-after` and `package-after` series in the JSON refer to this **rejected
framework hint**, not the delivered incremental target. Their first and repeated starts remain
separate observations; `source-before` includes a 53.518 s startup outlier. The package return control
uses the original packaged coordinator on the same already prepared consumer. It is not a repacked
or fresh-cache comparison. Source/package exploration is attribution evidence, not a package speed claim.

The inspected SDK installation reports VMR revision
`a8dd0577c5c13f43a2f57fe33b4ff11b90f6371e`.
[Its watcher source](https://github.com/dotnet/dotnet/blob/a8dd0577c5c13f43a2f57fe33b4ff11b90f6371e/src/sdk/src/Dotnet.Watch/Watch/HotReload/HotReloadDotNetWatcher.cs)
selects RestoreOnly/BuildOnly when framework **or device** selection is needed (around line 1045).
Its non-interactive path also enables automatic rude-edit restart (around line 276). That option
was rejected by source inspection and was not run: it would violate Spar's retirement ownership.
No watcher flags or runtime code from the experiment are delivered. SDK graph/device-selection
and evaluation remain targets for later supported-tooling investigation.

## Regression checks and reproduction

`eng/Test-ContractIncrementality.ps1` runs the real generator through a small invocation-counting
forwarder in an isolated project. It checks compiled bridge metadata and the embedded client after
deleting the output assembly, unchanged inputs, each missing generated file, missing frontend copies,
unchanged generated bytes after a source edit, actual DTO changes, repeated invalid-contract failure
and recovery, changed generator dependencies, older tool/contract inputs at new paths, design-time
exclusion, Clean ownership and rebuilding after Clean. It adds no test dependency to shipping packages.

The same driver fails against the original target on its second unchanged build: expected one
generator invocation, observed two. This is executed detection of the old behavior after a successful
first real compilation, not a missing-symbol or setup failure. The original target is reconstructable
from the base commit above. Initial sandboxed MSBuild attempts failed during restore and are retained
as setup failures; normal SDK/NuGet access was required. Initial marker/wildcard prototypes that still
reran generation were corrected before the paired result; none is counted as a pass.

Commands from the repository root, with native sessions stopped before rebuilding shared tools:

```powershell
pwsh -NoProfile -File eng/Test-ContractIncrementality.ps1 -EvidenceDirectory artifacts/contract/source
pwsh -NoProfile -File eng/Pack-Local.ps1 -Output artifacts/contract/feed
pwsh -NoProfile -File eng/Test-ContractIncrementality.ps1 -Feed artifacts/contract/feed -EvidenceDirectory artifacts/contract/package
dotnet build Spar.slnx --disable-build-servers
dotnet test --solution Spar.slnx --no-build -p:IncludeLongRunningTests=true --minimum-expected-tests 1 --fail-skips on
pwsh -NoProfile -File eng/Test-MinimalConsumer.ps1 -Feed artifacts/contract/feed -EvidenceDirectory artifacts/contract/minimal -VerifyRender
pwsh -NoProfile -File eng/Test-BackendUpdates.ps1 -EvidenceDirectory artifacts/contract/backend
```

For timing reproduction, prepare one ordinary source or package project, run
`eng/Measure-StartupCost.ps1` as documented in the [session guide](../../11-development-session.md),
and keep configurations, outputs, caches and tool binaries fixed between variants. The manual
driver retains source/binary/driver identities, every phase and cleanup outcome. No timing or
source-path selection was added to CI: the fixed Processes and Packages suites each gain one
explicit incrementality check. The window-selection regression below adds a third new entry,
making 52 fixed catalog checks in the final change.

## Local implementation qualification

The initial optimization runs used the base commit above plus the working changes bound by
[source hashes](sources.sha256), committed as `4d15b935eb801eacb7df9f93780ca69034592a2c`.
[Compact checks and package identities](checks.json) retain 21 passing incrementality checks in each
of the source and fresh-package variants. Both loaded target SHA-256
`91D7B51442D64A9AD4E7D82FCB5D3DFB47A56AE70FB5B057B16F07012E7CE85F`.
The matching solution build passed without warnings; all **75 managed cases passed**, none skipped.

The fresh ordinary package app passed public-inspection HMR/state and rebuilt replacement checks,
with all 31 retained process instances exited. Its empty-cache Debug build still took **78.87 s**;
the incremental target does not resolve the initial restore cost. Release built the frontend,
passed the production boundary, and the copied NativeAOT payload executed with its hash/mode checked.
All seven retained descendants exited. Reviewed development update/restart PNGs show retained
counters/input after HMR and reset counters after restart; the reviewed published BMP shows the
connected native window and expected greeting. Capture hashes remain in the compact record;
routine images and raw logs remain ignored local artifacts.

All three source backend scenarios passed: patch/compiler recovery and matching-contract replacement;
startup/dependency edits and superseded successful/failed full-build races; explicit forced stop
during retirement with no replacement. All **150 retained process instances exited** and unrelated
sentinels survived. These are native/typed process checks; the backend driver did not request pixels.
Documentation checked 376 references; six skills/14 case definitions and all six gate tests passed.

Hosted CI qualifies the committed PR head separately. No new editor gesture, no-SDK machine result
or machine-independent latency promise is inferred.

## First CI failure and test-helper repair

[Run 39849395183](https://github.com/sotsera/spar/actions/runs/39849395183), head `4d15b93`, tested merge
`6f9a38d96fbf47e3a8e83557cf8f86f24a50f20e`, passed Fast, Processes, Development and Delivery. Packages
passed the new incrementality check and full instrumented consumer, then failed the ordinary copied
AOT app's close check. The required aggregate correctly failed. [Retained failure details](ci-repair.json)
include its suite status, production receipt, stderr and original assertion. The old receipt conflates
a timeout and nonzero/unavailable exit code and records no selected window class. The exact original
shutdown cause is unknown; the WebView class-unregistration stderr alone does not establish it.

Investigation reproduced a separate concrete defect in the close helper: it selected the first window
owned by the PID, including same-process helper windows. A deterministic Win32 fixture creates two
hidden windows, puts the decoy first, and calls the actual selection helper. The same regression
fails its selected-handle assertion with the original helper and passes with the repair. It selects
the owned `Sotsera.Spar.*` class; both fixture windows are explicitly destroyed. No production/runtime
code or generator target changed in this repair.

The publication driver now records PID/start identity, selected handle/class, close-wait completion,
observed exit/code before forced cleanup, failure and actual cleanup outcomes. Timeout, unavailable
code and nonzero code remain failures with separate messages and retained log paths. Failure teardown
waits for the explicitly retained root/descendants and records forced termination separately from
graceful close. No timeout increase or automatic retry was added.

[Repair source hashes](repair-sources.sha256) bind the changed helper, fixture, driver and 52-check
catalog separately from the initial measurements. Helper ownership checks passed. Three local checks
of the **same unchanged AOT executable hash** selected Spar's actual class, observed exit code 0,
verified all seven retained descendants exited and required no forced cleanup. The second capture
was reviewed with the expected connected app/greeting. The first and third final captures were blank
and are explicitly not visual acceptance; all samples remain recorded. Earlier reviewed images bind
the same product payload. These process passes do not prove the cause of the original CI exit.
