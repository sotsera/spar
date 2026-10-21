# Optional development startup helper — 2026-10-21

C2 from the [accepted review](../../25-consumer-coordinator-review.md) adds `DevelopmentApplication.Run`
and migrates the ordinary sample/default package consumer. The explicit integration fixture remains unchanged.
Local acceptance passed, including actual source/package VS Code and Visual Studio walkthroughs.
Clean Windows/no-SDK qualification remains unavailable. PR #41 awaits review/merge.

The [receipt](receipt.json) binds execution to base `d68715b01cd0e3d63ade428ddb7e4ba42ae1f977` plus hashes of
the changed implementation/test drivers, and records the five freshly built package hashes. Runs preceded
the implementation commit. Commands ran on Windows x64, .NET SDK 10.0.402 and PowerShell 7.6.5.
The initial source debugger lane used VS Code 1.142.0, C# 2.160.4 and C# Dev Kit 3.40.215.
After strengthening the caller-thread identity assertion, the solution rebuilt cleanly and the five
startup cases were rerun successfully. No runtime or native-driver code changed after their executed checks.

The [editor follow-up receipt](editor-receipt.json) binds both completed editor lanes to implementation
`584c3d8ce4d0555bdf030904712d602155b38b76`. After the update, both source and fresh package consumers passed
on VS Code 1.143.0 with the same C# extensions. Actual Visual Studio Community 2026 18.11.0 Stable
walkthroughs used a disposable six-project source solution and a fresh two-project package consumer.
The [installed Visual Studio version](visual-studio-version.png) was inspected in Help/About.
The implementation's required [Windows managed probe](https://github.com/sotsera/spar/actions/runs/39601015568/attempts/1)
passed on attempt 1. These local editor results are separate from CI lifecycle automation.

## Executed checks

| Check | Observed result |
| --- | --- |
| Solution build and managed tests | Zero build warnings/errors; 70 tests passed, zero failed/skipped. Five new cases check MTA rejection, owner/contract rejection before factory invocation, same-STA factory execution, and real pipe EOF after throwing/null factories. Inherited project-launch settings do not override an existing owner. |
| Source readiness/authority | Failed typed call and forged signal cannot declare readiness; successful call/frame can; reload retires the old target. A separate helper-started session becomes ready with inspection disabled and rejects inspection requests. Actual cleanup passed for 18 and 13 retained processes respectively. |
| Source development | Frontend/backend updates retain both counters and typed input; coordinated restart resets state. All 31 retained processes exited and an unrelated process survived. Reviewed [update](source-updated.png) and [restart](source-restarted.png). |
| Source VS Code | Actual breakpoint/Continue, CSS and debugger-owned backend patch, compiler rejection/recovery, coordinated restart and breakpoint after relaunch, then debugger Stop passed. All 22 retained processes exited. Reviewed [patched window](source-debugger.png); no fixture reports were used. |
| Source and package VS Code after update | Both actual debugger lanes passed on 1.143.0, including breakpoint/Continue, state-preserving updates, compiler rejection/recovery, coordinated restart/relaunch and Stop. Each verified 22 retained process exits and 115 events. Reviewed [source](source-debugger-1.143.png) and [package](package-debugger-1.143.png). |
| Actual source Visual Studio | F5/breakpoint/Continue, CSS HMR, two supported backend patches, compiler rejection/recovery, unsupported base-class edit, native close, IDE rebuild/F5, contract retirement and failed/corrected IDE builds, fresh matching-contract F5 and Shift+F5 passed. All 40 retained processes exited across three sessions; three profile leases reopened exclusively. |
| Actual package Visual Studio | The same gestures passed in the fresh two-project consumer using the packaged coordinator. All 40 retained processes exited across three sessions; three profile leases reopened exclusively. |
| Minimal project-launch lifecycle | Native close, simulated debugger termination, contract-triggered session retirement and coordinator crash passed; 13 retained processes per scenario exited. No watcher owner started. Reviewed all four local captures; [one ready window](project-ready.png) is retained. These are automated lifecycle cases, not Visual Studio gestures. |
| Source Release / copied NativeAOT | Production-boundary checks exclude development references, inspector APIs/scripts and probe resources. Copied AOT displayed the [typed greeting](source-production.png); retained descendants exited. |
| Fresh minimal package consumer | New directory outside the checkout, copied private feed and isolated cache; direct app build, frontend checks, updates/restart, Release boundary and copied AOT passed. Reviewed [update](package-updated.png), [restart](package-restarted.png) and [production](package-production.png). All 31 development processes exited. |
| Fresh explicit fixture consumer | Existing low-level startup passed updates/restart and 31 retained process exits, Release exclusion, copied AOT, expected strict-AOT rejection of its DynamicMethod dependency, and explicit CoreCLR execution. Three local captures were reviewed; routine detailed artifacts remain local. |

The retained PNGs are original PNGs or lossless conversions of the reviewed BMPs. Raw browser profiles,
private debugger launch capabilities and routine full logs are not included. The aggregate receipt retains
the individual driver's original rendering labels; the visual review above supplies the separate image review.

## Actual Visual Studio observations

Both walkthroughs followed the [isolated manual procedure](../../14-visual-studio-development.md#isolated-manual-acceptance).
Source edits were confined to disposable copies; F5, Continue, Apply Code Changes, Build Solution and
Shift+F5 were actual IDE gestures. Native clicks set both counters to 2 and distinctive typed input.
CSS changed the controls to yellow. Supported greeting patches produced revisions 1 and 2 in the same
process/document with counters/input retained. The undefined `MissingGreetingSymbol` was rejected;
the prior native behavior remained usable until correction. The source IDE output records
[CS0103 and ENC0014](vs-source-ide-diagnostics.png).

The unsupported base-class edit was dismissed with **Edit**, then native close completed graceful retirement.
An IDE rebuild and fresh F5 hit a new breakpoint and reset native state. Adding `Revision` to `CounterReply`
retired that session; the actual IDE build rejected the missing constructor argument with CS7036. Adding
argument `7` and rebuilding allowed a third F5 with a changed, matching contract fingerprint. Actual
debugger Stop ended this session with `forced_cleanup`, distinct from the first two graceful stops.
Independent process-handle observations and exclusive profile-lease opens establish the recorded cleanup.

| Observation | Source | Package |
| --- | --- | --- |
| Initial breakpoint | [Capture](vs-source-breakpoint.png) | [Capture](vs-package-breakpoint.png) |
| First backend patch and retained native state | [Capture](vs-source-backend-patched.png) | [Capture](vs-package-backend-patched.png) |
| Compiler rejection | [Capture](vs-source-compiler-rejection.png) | [Capture](vs-package-compiler-rejection.png) |
| Corrected patch and retained state | [Capture](vs-source-recovered.png) | [Capture](vs-package-recovered.png) |
| Unsupported edit | [Capture](vs-source-unsupported-edit.png) | [Capture](vs-package-unsupported-edit.png) |
| Breakpoint after IDE rebuild/F5 | [Capture](vs-source-relaunch-breakpoint.png) | [Capture](vs-package-relaunch-breakpoint.png) |
| Contract build rejection | [Capture](vs-source-contract-build-rejection.png) | [Capture](vs-package-contract-build-rejection.png) |
| Matching changed-contract native window | [Capture](vs-source-contract-ready.png) | [Capture](vs-package-contract-ready.png) |

## Commands

```powershell
dotnet build Spar.slnx --disable-build-servers
dotnet test --solution Spar.slnx --no-build
pwsh -NoProfile -File eng/Test-DevelopmentReadiness.ps1 -EvidenceDirectory artifacts/issue34-c2/readiness
pwsh -NoProfile -File eng/Test-MinimalApplication.ps1 -EvidenceDirectory artifacts/issue34-c2/minimal-source
pwsh -NoProfile -File eng/Test-ProjectLaunch.ps1 -MinimalApplication -VerifyRender
pwsh -NoProfile -File eng/Test-VSCodeDevelopment.ps1 -MinimalApplication -VerifyRender
dotnet publish samples/Spar.NativeSample -c Release -o artifacts/issue34-c2/source-publish --disable-build-servers
pwsh -NoProfile -File eng/Test-ProductionBoundary.ps1 -MinimalApplication -Assembly samples/Spar.NativeSample/bin/Release/net10.0-windows/win-x64/Spar.NativeSample.dll
pwsh -NoProfile -File eng/Test-MinimalPublication.ps1 -PublishDirectory artifacts/issue34-c2/source-publish -EvidenceDirectory artifacts/issue34-c2/source-production -VerifyRender
pwsh -NoProfile -File eng/Pack-Local.ps1 -Output artifacts/issue34-c2/packages
pwsh -NoProfile -File eng/Test-MinimalConsumer.ps1 -Feed artifacts/issue34-c2/packages -EvidenceDirectory artifacts/issue34-c2/minimal-consumer -VerifyRender
pwsh -NoProfile -File eng/Test-ExternalConsumer.ps1 -Feed artifacts/issue34-c2/packages -EvidenceDirectory artifacts/issue34-c2/external-consumer -VerifyRender
# After the editor update, repeat the source debugger command above, then use a fresh external directory:
pwsh -NoProfile -File eng/New-PackageConsumer.ps1 -Feed artifacts/issue34-c2/packages -Directory <fresh-consumer>
# Run npm ci --ignore-scripts --include=optional in <fresh-consumer>/backend/frontend.
pwsh -NoProfile -File eng/Test-VSCodeDevelopment.ps1 -ConsumerDirectory <fresh-consumer> -MinimalApplication -VerifyRender
```

## First failures, resolution and remaining limits

- Initial build rejected the xUnit collection marker name with CA1711. Renaming the marker resolved it;
  no analyzer suppression was added. The successful build and 70-test run followed that correction.
- The package VS Code attempt built its fresh consumer, but VS Code refused to launch: `vscode-updating`
  was held and the editor reported that an update was in progress. The supervisor consequently reported
  insufficient retained processes. No Spar session or debugger acceptance receipt was produced. The updater
  mutex was still present on inspection; no editor window was closed and no update was interrupted.
  After the user completed the update, fresh source/package lanes passed on VS Code 1.143.0.
- Visual Studio control returned `Computer Use app approval timed out` before any walkthrough.
  The user authorized a retry, desktop access succeeded, and both actual walkthroughs passed as recorded above.
- Clean Windows/no-SDK-machine qualification remains unavailable. This change supplies no new supported-machine
  matrix or public package release.

Ignored local evidence is under `artifacts/issue34-c2`; the source debugger/project fixtures are under
`artifacts/vscode-development` and `artifacts/project-launch`. Fresh external directories are recorded in the
local path files. Preserve them for follow-up; source fixtures contain `node_modules` junctions.
