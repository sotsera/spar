# Native backend update evidence

Observed 2026-10-19, Windows/x64, SDK 10.0.402, Node 24.21.0, npm 11.19.0, VS Code 1.142.0. See [environment](environment.json), [framework/test input hashes](sources.sha256), and each isolated consumer's initial hashes below. The source manifest hashes staged Git bytes after line-ending normalization. [The guide](../../12-backend-updates.md) explains the pinned SDK console boundary and remaining work.

## Commands and results

```powershell
dotnet build Spar.slnx --disable-build-servers
dotnet test --solution Spar.slnx --no-build
pwsh -NoProfile -File eng/Test-BackendUpdates.ps1 -EvidenceDirectory artifacts/backend-loop/desktop-isolated-final -VerifyRender
pwsh -NoProfile -File eng/Test-DevelopmentSession.ps1 -EvidenceDirectory artifacts/backend-loop/regression-isolated-final
pwsh -NoProfile -File eng/Test-VSCodeSession.ps1 -WatchBackend -EvidenceDirectory artifacts/backend-loop/vscode-isolated-final
dotnet publish samples/Spar.NativeSample -c Release -o artifacts/backend-loop/aot-isolated-final --disable-build-servers
```

All passed, including 42 managed tests with none skipped, generated-client TypeScript, sample TypeScript/build, and real native host/bridge checks against CoreCLR and copied NativeAOT. The copied [AOT payload](aot-copy.json) is still exactly the executable, WebView2 loader, and two license/notice files. Development callbacks, reflection, watcher/inspection code, and managed runtime configuration do not ship in it. Its UI remains the local bridge probe; this is not production React asset packaging.

## Native watch flows

[Backend receipts](backend-sessions.json) and [typed/DOM snapshots and sequencing](steps.json) record two actual watcher/WebView sessions:

| Flow | Observed result |
| --- | --- |
| [Patch/error/restart](patch-error-restart.jsonl) | 45 independently retained process identities ended. Same-process patches and read refresh, compiler recovery, rapid body edits, 12-second graceful retirement, failed contract rebuild, correction/restart, and stale-fingerprint rejection all passed. Original Vite process survived every application generation; unrelated Node survived. |
| [Explicit force during retirement](explicit-force-during-retirement.jsonl) | 18 retained identities ended. A cancellation-ignoring operation was active when the user/test explicitly forced Stop. Cleanup was labeled forced, no replacement started, and unrelated Node survived. No successful completion of killed application work is claimed. |

Initial consumer inputs: [main flow](patch-error-restart.initial.sha256), [forced flow](explicit-force-during-retirement.initial.sha256). The committed driver records each source transformation. Only its isolated delay bound is extended to 15 seconds to run a 12-second operation beyond the SDK's ordinary five-second cleanup timeout. The repository sample keeps its normal bound. The driver deliberately inherits `DOTNET_WATCH_LOG_LEVEL=Critical`; Spar's safeguard still leaves the SDK awaiting retirement rather than auto-confirming.

The main flow starts at process **37172**, creation time `2026-10-19T12:08:27.9943718Z`, generation 1. React mount `fef7f353-957e-4dd2-a504-919a7126bb70` and document `984fe7c9e7fd479f8c5a90f8c34defe4` remain through patch and compile-error recovery. Typed snapshots show local counter 7, retained text, and backend counter 2. Greeting changes to **Hello after patch, Spar**, with runtime revision 1; CS0103 retains that behavior/revision, and correction patches the same process. Two rapid body edits reach **Hello rapid final, Spar**. Source saves are not treated as confirmation of activation.

CSS HMR also changes the accent to yellow while the C# watcher is active. The actual `PrintWindow` capture passed its accent pixel check and was visually reviewed at original resolution. This PNG is a lossless conversion, with no content edits. It shows the changed C# greeting, retained frontend/backend state, and update revision. Combined with process/document receipts, this establishes **A03/A04** for the terminal/AI lane, alongside A02's frontend proof.

![Native SPA after a backend patch](patched.png)

Changing the backend base class requests a coordinated restart. The delay completion receipt is drained before `application-stopped`; the replacement starts only afterward. No graceful timeout kills the operation, and it is entered only once. Application generation 2 starts with counter 0 while the same frontend PID **25076**, creation time `2026-10-19T12:08:17.4848057Z`, remains alive.

The contract source monitor requests a full rebuild; the DTO source project and generated wire glue are excluded from the patch workspace by a compiled assembly reference. The retained events confirm one source-change receipt and no contract delta callback. Adding a required record parameter while its call site remains unchanged fails that full build; generation 3 is never ready and the app stays stopped. Correcting the call site and requesting restart produces generation 4, with fingerprint `9ed337f2bfae07213041ca561feab350835c41df4abbeaf99ed1c3d44e8ca43e` instead of `5f384a796cdfac56662f2a8e4e88daceef82abfdf9d3720b904da302e912ca0d`. Sending the old fingerprint is rejected before the counter handler enters. This verifies matching activation and failed-build recovery on the exercised path; it does not establish every concurrent source/build race.

## Editor and regression scope

[VS Code watcher ownership receipts](vscode-watch.json) show actual task termination and editor-window close, each ending all 18 retained coordinator/child identities. [Task actions](vscode-task-stop-editor.jsonl), [task events](vscode-task-stop-coordinator.jsonl), [window actions](vscode-window-close-editor.jsonl), and [window events](vscode-window-close-coordinator.jsonl) are retained. No installed C# debugger was attached in this lane; F5, breakpoints, debugger updates/relaunch and debug Stop remain open.

[Ten existing SPA regressions](spa-regressions.json) passed. Native regressions: [CoreCLR host](coreclr-native.json), [CoreCLR bridge](coreclr-bridge.json), [AOT host](aot-native.json), [AOT bridge](aot-bridge.json). Those host/bridge regression runs did not request pixel verification; the native patch image above supplies this slice's separate desktop evidence. Process counts are retained observations, not a complete historical census of short-lived helpers.

First-failure logs remain under ignored `artifacts/backend-loop/desktop-v1` through `desktop-v3`: target-framework propagation into the generator, missing drained retirement receipts, and an incorrect expectation that the delta engine would report a full-compilation record error. The intermediate v3 fixture also observed its own logs/profile inside the source root; final fixtures keep those outside. The first isolated-assembly run also exposed an obsolete fixture property assignment before its second scenario; that run is excluded. The complete final isolated-assembly run supplies all receipts above.

Issue #6 stays open for startup/dependency input policy, latest-selected-source full-build activation under overlapping edits, and broader stopped-state recovery automation. The two-edit method test does not complete all A08 races. Integrated debugger lanes, generic AI UI automation, production SPA delivery, clean-machine qualification, and external packages retain their separate acceptance requirements.

The first upstream run on the isolated-contract commit, [39250272714](https://github.com/sotsera/spar/actions/runs/39250272714), passed the build and all 42 managed tests, then rejected an obsolete TypeScript import into the application's former generated directory. Existing local output had masked that path. The check now imports the contract project's output; the old generated directory was moved aside before repeating it successfully. Later skipped CI steps from that failed run are not counted as evidence.
