# Optional development startup helper — 2026-10-21

C2 from the [accepted review](../../25-consumer-coordinator-review.md) adds `DevelopmentApplication.Run`
and migrates the ordinary sample/default package consumer. The explicit integration fixture remains unchanged.
**Acceptance is incomplete:** package VS Code and both actual Visual Studio walkthroughs remain pending.
This record does not promote lifecycle automation or historical IDE results into those missing checks.

The [receipt](receipt.json) binds execution to base `d68715b01cd0e3d63ade428ddb7e4ba42ae1f977` plus hashes of
the changed implementation/test drivers, and records the five freshly built package hashes. Runs preceded
the implementation commit. Commands ran on Windows x64, .NET SDK 10.0.402 and PowerShell 7.6.5.
The executed source debugger lane used VS Code 1.142.0, C# 2.160.4 and C# Dev Kit 3.40.215.
After strengthening the caller-thread identity assertion, the solution rebuilt cleanly and the five
startup cases were rerun successfully. No runtime or native-driver code changed after their executed checks.

## Executed checks

| Check | Observed result |
| --- | --- |
| Solution build and managed tests | Zero build warnings/errors; 70 tests passed, zero failed/skipped. Five new cases check MTA rejection, owner/contract rejection before factory invocation, same-STA factory execution, and real pipe EOF after throwing/null factories. Inherited project-launch settings do not override an existing owner. |
| Source readiness/authority | Failed typed call and forged signal cannot declare readiness; successful call/frame can; reload retires the old target. A separate helper-started session becomes ready with inspection disabled and rejects inspection requests. Actual cleanup passed for 18 and 13 retained processes respectively. |
| Source development | Frontend/backend updates retain both counters and typed input; coordinated restart resets state. All 31 retained processes exited and an unrelated process survived. Reviewed [update](source-updated.png) and [restart](source-restarted.png). |
| Source VS Code | Actual breakpoint/Continue, CSS and debugger-owned backend patch, compiler rejection/recovery, coordinated restart and breakpoint after relaunch, then debugger Stop passed. All 22 retained processes exited. Reviewed [patched window](source-debugger.png); no fixture reports were used. |
| Minimal project-launch lifecycle | Native close, simulated debugger termination, contract-triggered session retirement and coordinator crash passed; 13 retained processes per scenario exited. No watcher owner started. Reviewed all four local captures; [one ready window](project-ready.png) is retained. These are automated lifecycle cases, not Visual Studio gestures. |
| Source Release / copied NativeAOT | Production-boundary checks exclude development references, inspector APIs/scripts and probe resources. Copied AOT displayed the [typed greeting](source-production.png); retained descendants exited. |
| Fresh minimal package consumer | New directory outside the checkout, copied private feed and isolated cache; direct app build, frontend checks, updates/restart, Release boundary and copied AOT passed. Reviewed [update](package-updated.png), [restart](package-restarted.png) and [production](package-production.png). All 31 development processes exited. |
| Fresh explicit fixture consumer | Existing low-level startup passed updates/restart and 31 retained process exits, Release exclusion, copied AOT, expected strict-AOT rejection of its DynamicMethod dependency, and explicit CoreCLR execution. Three local captures were reviewed; routine detailed artifacts remain local. |

The retained PNGs are original PNGs or lossless conversions of the reviewed BMPs. Raw browser profiles,
private debugger launch capabilities and routine full logs are not included. The aggregate receipt retains
the individual driver's original rendering labels; the visual review above supplies the separate image review.

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
```

## First failures and pending acceptance

- Initial build rejected the xUnit collection marker name with CA1711. Renaming the marker resolved it;
  no analyzer suppression was added. The successful build and 70-test run followed that correction.
- The package VS Code attempt built its fresh consumer, but VS Code refused to launch: `vscode-updating`
  was held and the editor reported that an update was in progress. The supervisor consequently reported
  insufficient retained processes. No Spar session or debugger acceptance receipt was produced. The updater
  mutex was still present on inspection; no editor window was closed and no update was interrupted.
- Visual Studio control returned `Computer Use app approval timed out` before any walkthrough.
  Fresh source and package solutions have been prepared and built locally, including the isolated base-class
  change used by the documented unsupported-edit case. Preparation establishes no IDE acceptance.
- Complete actual package VS Code acceptance after the update. If the installed editor changes, repeat
  the source debugger lane on that version too. Complete both source/package Visual Studio walkthroughs
  from the [guide](../../14-visual-studio-development.md#isolated-manual-acceptance), retaining actual gestures,
  native captures, diagnostics and process exits before treating C2 as accepted.
- Clean Windows/no-SDK-machine qualification remains unavailable. This change supplies no new supported-machine
  matrix or public package release.

Ignored local evidence is under `artifacts/issue34-c2`; the source debugger/project fixtures are under
`artifacts/vscode-development` and `artifacts/project-launch`. Fresh external directories are recorded in the
local path files. Preserve them for follow-up; source fixtures contain `node_modules` junctions.
