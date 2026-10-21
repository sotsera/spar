# Watcher resource ownership — 2026-10-21

K1 from the [accepted review](../../25-consumer-coordinator-review.md) replaces five independently
mutable coordinator fields with one internal `WatcherProcess`. It owns the nested job, retained watcher
process and exit task, both bounded log readers and first-ready observation. The coordinator registers
the owner before launch. If either reader fails afterward, the same owner retains resources until cleanup.
The session-scoped input monitor remains separate.

Build selection, restart/admission, update publication and application retirement stay in the coordinator.
The owner returns bounded observations and has no session reference or callbacks. Its retirement method
rejects an unfinished application exit task, terminates remaining owned helpers, then waits for an empty
nested job and actual watcher exit. Final disposal remains a kernel safeguard, without claiming normal
retirement. Public APIs, events, SDK pin, dependencies and production startup are unchanged.

## Verification status

Execution started from merged PR #41, `b9c2500f685d9fa4b441046d8c202ab9add5d4bd`, on Windows x64,
.NET SDK 10.0.402 and PowerShell 7.6.5. The implementation is commit
`374b17f070353009ff95d7a79403bbf134c4b675`; early suites ran before that commit on the same source bytes.
The [compact receipt](receipt.json) retains source/package/capture hashes, individual driver results and
retirement sequences. All checks below passed. This records local acceptance; PR CI has its own result.

| Check | Observed result |
| --- | --- |
| Build and managed tests | Zero warnings/errors; 73 passed, zero failed/skipped. Three new real-process cases deny either stdout or stderr reader after launch and reject retirement with an unfinished application barrier. Retained process handles and job membership remain valid through the failure; successful retirement verifies actual exit and empty job membership. |
| Native watcher updates | Patch/error/recovery, rapid edits, unsupported-edit retirement around cancellation-ignoring work, contract failure/correction and stale fingerprint rejection passed; 45 retained processes exited. Reviewed [patch](backend-patched.png). |
| Startup/dependency/build races | Selected/superseded success and failure, initialization/dependency rebuild and correction passed; 87 retained processes exited. Reviewed [latest recovered generation](backend-recovered.png). |
| Explicit force | Forced stop during unfinished retirement passed, with 18 retained process exits. No replacement started; the unrelated process survived all three watcher flows. |
| Development lifecycle | All ten scenarios passed, including owner EOF/crash, application/frontend exit, malformed input, port conflict, startup timeout/failure and credential rejection. Retained process counts are recorded per case. Reviewed [state-preserving frontend update](frontend-updated.png). |
| Structured inspection | Native DOM actions, logs, screenshots, rejection/replay boundaries, timeout during retirement, restart and failed-build correlation passed. Retained exits: 31, 13 and 19. Reviewed [updated](inspection-updated.png) and [restarted](inspection-restarted.png) windows. |
| Minimal project launch | Native close, simulated debugger Stop, contract retirement and coordinator crash passed; 13 retained exits each. No SDK watcher started in these editor-owned sessions. Reviewed all four local captures; [ready window](project-ready.png) retained. |
| VS Code task ownership | Actual task stop and window close with `-WatchBackend` passed; 19 retained exits each. These scenarios do not claim debugger Hot Reload. |
| Actual VS Code debugger | VS Code 1.143.0, C# 2.160.4, Dev Kit 3.40.215: breakpoint/Continue, frontend/backend updates, compiler rejection/recovery, coordinated restart/relaunch and Stop passed; 22 retained exits, 117 events. Reviewed [patched window](debugger-patched.png). |
| Fresh external package consumer | Outside the checkout with an isolated feed/cache: direct build, frontend checks, actual watcher updates and restart passed; all 31 retained development processes exited. Reviewed [package patch](package-patched.png). |
| Production boundary and compatibility | Release exclusion passed before probe builds; copied [NativeAOT](package-aot.png) and attributable [CoreCLR](package-coreclr.png) runs passed, with native captures reviewed. Strict AOT correctly rejected the DynamicMethod dependency with IL3050. Five local package hashes are retained. |

Ten representative captures are retained. All four project-launch captures and both before/after development
captures were also reviewed locally. PNGs are original screenshots or lossless conversions of reviewed BMPs.
The driver receipts retain their original rendering labels; these reviews supply the separate visual evidence.
Raw browser profiles, debugger launch capabilities and routine full logs are excluded from the durable record.

## Commands

```powershell
dotnet build Spar.slnx --disable-build-servers
dotnet test --solution Spar.slnx --no-build
pwsh -NoProfile -File eng/Test-BackendUpdates.ps1 -EvidenceDirectory artifacts/issue34-k1/backend -VerifyRender
pwsh -NoProfile -File eng/Test-DevelopmentSession.ps1 -EvidenceDirectory artifacts/issue34-k1/development -VerifyRender
pwsh -NoProfile -File eng/Test-DevelopmentInspection.ps1 -EvidenceDirectory artifacts/issue34-k1/inspection
pwsh -NoProfile -File eng/Test-ProjectLaunch.ps1 -MinimalApplication -VerifyRender
pwsh -NoProfile -File eng/Test-VSCodeSession.ps1 -WatchBackend -EvidenceDirectory artifacts/issue34-k1/vscode-tasks
pwsh -NoProfile -File eng/Test-VSCodeDevelopment.ps1 -MinimalApplication -VerifyRender
pwsh -NoProfile -File eng/Pack-Local.ps1 -Output artifacts/issue34-k1/packages
pwsh -NoProfile -File eng/Test-ExternalConsumer.ps1 -Feed artifacts/issue34-k1/packages -EvidenceDirectory artifacts/issue34-k1/consumer -VerifyRender
node eng/ci/Test-Documentation.mjs
node eng/Test-AgentSkills.mjs
node --test eng/ci/selection.test.mjs
git diff --check
```

## First failures and limits

The first restricted build exited with no compiler diagnostics. A full build exposed xUnit1069 in the new
timeout-bearing tests. Their normal retirement waits now observe the test cancellation token; the `finally`
cleanup still awaits actual retirement. No analyzer suppression or production timeout was added. The corrected
clean build and 73-test run passed. Original logs remain under ignored `artifacts/k1-build*.log` and
`artifacts/issue34-k1`; native fixture directories contain junctions and must not be recursively deleted.

Clean Windows/no-SDK qualification remains unavailable. Local package checks do not publish a NuGet release.
Historical Visual Studio walkthroughs remain bound to their tested revisions; project-launch automation
does not establish a new actual IDE walkthrough. This slice changes watcher ownership, with debugger/project
paths checked for regression as specified by the accepted K1 scope.
