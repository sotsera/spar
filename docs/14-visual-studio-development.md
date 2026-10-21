# Visual Studio project launch

This integration guide exercises the explicit acceptance application under `fixtures/`. For ordinary application startup and publication, start with the [minimal application guide](23-minimal-application.md). Historical captures retain their original paths.

Status: the [manual native acceptance](evidence/project-launch-2026-10-19/README.md) covers issue #9 / A13: F5 and breakpoint, frontend HMR, supported backend Hot Reload, compiler recovery, documented unsupported/contract relaunch, and debugger Stop with actual process cleanup. Lifecycle automation separately covers owner loss and coordinator crash.

The [external package-consumer walkthrough](evidence/milestone-acceptance-2026-10-20/README.md) also passed on Visual Studio Community 2026 18.11.0 Stable. It loads only the consumer application and its contract project, uses the packaged development coordinator, and verifies the same update/relaunch/Stop path. Use the [package guide](18-package-consumer.md) for that setup.

## Setup

Use the repository's SDK `10.0.402`, Node `24.21.0` / npm `11.19.0`, and installed WebView2 Runtime. The native pass used Visual Studio Community 2026 stable (installed `devenv.exe` version `18.11.12306.390`, MSBuild `18.11.0-1.26475.11`). Prepare from the repository root:

```powershell
dotnet build Spar.slnx --disable-build-servers
Push-Location fixtures/Spar.NativeSample/frontend
npm ci --ignore-scripts --include=optional
Pop-Location
```

Open `Spar.slnx`, select `Spar.NativeSample` as the startup project, select **Debug** and the **Spar development** launch profile, then F5. The profile is an ordinary `commandName: Project` profile. Visual Studio builds the application; its debugger owns managed updates. Spar starts the shared coordinator and frontend through `DevelopmentProjectLaunch`, which is referenced only in Debug. No VSIX, second C# compiler, or watcher runs in this lane. The plain **Spar native host** profile remains available for the embedded-document host probe.

Enable Visual Studio's **Hot Reload and Edit and Continue when debugging**. The walkthrough uses explicit **Apply Code Changes** (flame button), so apply-on-save is optional. Keep automatic restart disabled for the acceptance pass. Never choose to launch the last successful build after a failed build. See Microsoft's [project launch profiles](https://github.com/dotnet/project-system/blob/main/docs/launch-profiles.md) and [Hot Reload guide](https://learn.microsoft.com/en-us/visualstudio/debugger/hot-reload).

## Ownership and restart

The application starts a hidden coordinator with redirected private streams. The coordinator authenticates the application pipe, executable path, PID, and retained process creation identity before adding it to its kill-on-close Windows job. Launch capabilities stay in the private stream; public events contain no token. The initial source snapshot is an observation after the IDE build, not an independently verified build receipt. Compiled backend and generated client fingerprints must still match before admission.

The application owns the coordinator's input connection. Debugger Stop closes it and requests forced cleanup. Normal native completion sends an explicit completion frame, then exits; the coordinator waits for actual application exit and cleans the frontend. The bootstrap must not wait for coordinator exit inside application disposal: that would deadlock retirement. A coordinator crash closes its job, terminating its admitted application and owned descendants. Startup failure also cleans the owned frontend.

Supported body edits retain the session, native window, frontend, and state. The existing metadata update callback refreshes SPA read data. Compiler/rude-edit diagnostics remain in Visual Studio; this lane does not invent a public IDE diagnostic feed.

Contract, configured startup, and dependency changes request graceful native retirement and end the entire project session. Wait for `session-stopped` and actual process exit, then rebuild and F5. An unsupported edit uses the same **close native window → wait for session cleanup → rebuild → F5** flow. Close waits for outstanding native/backend work; if explicit debugger Stop is needed instead, it is forced termination. Never claim cancellation completed a mutation or replay one.

This first Visual Studio path restarts Vite and resets SPA state on whole-session relaunch. VS Code's **Spar: Restart backend** retains Vite across its coordinated generations. The Visual Studio restart popup and toolbar Restart are not verified replacements for this documented flow. No debugger attach-time Hot Reload claim is made.

There is no public pause notification in this project-launch adapter. After application admission, an initial backend breakpoint can wait for Continue without expiring native-readiness time. Frontend startup and private connection setup still have their configured deadlines. Keep initial breakpoints in consumer handlers after `DevelopmentApplication.Connect`; pausing inside bootstrap/connection setup is not covered. Owner loss remains a cleanup signal while the application is paused.

Bridge requests retain their bounded 30-second timeout while the debugger is paused. A long breakpoint can therefore produce `transport_timeout` in the SPA even though the session is still alive. Continue execution, disable the breakpoint if appropriate, then use **Refresh backend data**. This starts a new greeting/state read, preserves local frontend state, and never replays mutations. Timeout does not prove the old operation stopped; a late reply is discarded. The sample shows a recovery message and keeps the refresh control disabled while its read is pending.

Public JSON events are written to `project-<id>.jsonl` in the configured session directory. The Debug output prints that path and events; stderr is retained beside it. `session-stopped` distinguishes graceful from forced cleanup. Process creation times and contract/document identities allow the evidence to be checked independently.

## Isolated manual acceptance

Create disposable source copies, an absolute-path launch profile, and a solution containing the complete project reference graph:

```powershell
pwsh -NoProfile -File eng/Test-ProjectLaunch.ps1 -PrepareOnly
```

Open the printed fixture's `Fixture.slnx` and set `Spar.NativeSample` as the startup project. The contract project must be loaded alongside the app so IDE builds compile its edited source. The first fixture omitted it, and Visual Studio rebuilt the app against an old contract DLL without the expected compiler error; that failure is retained in the evidence. Shared framework/tool projects are included for build ordering; all deliberate edits below belong inside the fixture's backend/contracts/frontend copies. Its frontend uses a `node_modules` junction; never recursively delete the fixture as routine cleanup.

The generator resolves Roslyn from the selected .NET SDK after SDK imports, independent of whether MSBuild runs inside Visual Studio or `dotnet`. These compiler references belong only to build tooling and do not ship with the native application. If an open IDE retains the old generator reference configuration after an update, reload the solution before rebuilding. The acceptance pass used the fixture's absolute-path profile; the repository profile's MSBuild-property expansion was not separately exercised in the IDE.

1. Record **Help → About** version. Open `backend/SampleBackend.cs`, set a breakpoint on the return in `GreetAsync`, select **Spar development**, then F5. Record the actual breakpoint and Continue. Confirm the native SPA says **Connected** and displays **Hello, Spar**. Remove/disable the breakpoint after the first hit.
2. Click both counters twice and enter distinctive text. Change `#70e3bd` to `#e9b44c` in `backend/frontend/src/styles.css`. Save and confirm yellow controls with retained counters/text. Change `"Hello, "` to `"Hello from Visual Studio, "` in `SampleBackend.cs`, save and apply the flame button. Confirm the updated native greeting with the same PID/document and counters. The event log must record a metadata update revision.
3. Replace that greeting literal with `MissingGreetingSymbol`, save and apply. Record CS0103 and retained previous behavior. Restore a valid greeting, save and apply; confirm correction without a process restart.
4. Change `InitialBackendBase` to `ReplacementBackendBase` in the class declaration only. Save and apply; retain the actual unsupported-edit diagnostic. Dismiss automatic restart, close the native window, and wait for session cleanup. Rebuild and F5; confirm a new process/session, reset counters, and the breakpoint still works.
5. Change `record CounterReply(int Value)` to `record CounterReply(int Value, int Revision)` in `contracts/BackendContract.cs`. Save. The project session must retire and stop. Rebuild and record CS7036. Correct `new CounterReply(Interlocked.Add(ref _counter, request.Delta))` in the backend to `new CounterReply(Interlocked.Add(ref _counter, request.Delta), 7)`. Rebuild, then F5 only after a successful build; confirm a changed matching fingerprint and native readiness.
6. Use Visual Studio **Stop Debugging**. Verify the native window, owned frontend, coordinator and observed descendants exit. Retain the event log and process-handle checks. Separately record normal native close if not already observed in step 4.

The operator's debugger gestures and diagnostics, native screenshots, actual process checks, source hashes, public event logs, and failures must all be retained. No checklist item is complete solely because the same code passed VS Code acceptance.

## Automated checks

```powershell
dotnet build Spar.slnx --disable-build-servers
dotnet test --solution Spar.slnx --no-build
pwsh -NoProfile -File eng/Test-ProjectLaunch.ps1 -VerifyRender
pwsh -NoProfile -File eng/Test-VSCodeDevelopment.ps1 -VerifyRender
```

`Test-ProjectLaunch` launches the real native project bootstrap and verifies native close, simulated debugger termination, contract-triggered session retirement, and coordinator crash. It retains actual OS process handles and checks exit. `-VerifyRender` plus image inspection supplies native rendering evidence. The CI form omits pixel checks and does not run Visual Studio. The VS Code test remains a separate regression lane.
