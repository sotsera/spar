# Managed Hot Reload probe

**Historical precursor experiment with reproduction commands.** This console proof is separate from the integrated app. Use the [VS Code guide](13-vscode-development.md) or [Visual Studio guide](14-visual-studio-development.md) for current application debugging, and [current status](README.md#status-and-deferred-work) for issue completion. Then-open statements below describe the original experiment.

Date: 2026-10-18. Scope: the first part of [issue #2](https://github.com/sotsera/spar/issues/2) / M1.1. This console experiment precedes the native host; it does not complete an integrated milestone acceptance scenario.

## What exists

`experiments/Spar.HotReloadProbe` emits a JSON observation every 250 ms: process ID/start time, instance ID, mutable counter, greeting, and metadata update revision/thread/types. .NET owns compilation and patches; the observer uses the public `MetadataUpdateHandler` mechanism. There is no compiler, rollback store, SPA, WebView, or production framework dependency.

`tests/Spar.HotReloadProbe.Tests` copies that source into ignored, unique directories under `artifacts/hot-reload`, records initial SHA-256 hashes, starts real `dotnet watch`, edits the copies, and asserts observed behavior. Tests run serially, bound waits and sample buffering, and preserve `watch.log`. Teardown deliberately forces the fixture's owned process tree to exit and waits for the watcher and observed application handles. This proves test cleanup, not the future coordinator's graceful retirement or crash-containment policy.

`eng/vscode-probe` is a development-only extension-host test driver, not a product editor extension. It uses VS Code's public workspace/debug/task APIs and standard DAP Continue, with the installed Microsoft C# debugger. It waits for project tasks before opening C# files, hits a real breakpoint, saves edits, and observes the application's actual output. The PowerShell launcher uses an isolated profile and copies; it verifies application process exit after Stop. It neither installs extensions nor changes the user's profile.

No upstream source was copied or adapted for this slice. Reactor's metadata-update observation pattern informed the experiment; the code is independently written against public .NET/VS Code APIs.

## Commands

Run from the repository root on Windows, with the exact SDK in `global.json` installed:

The commands below record the precursor's original default. Current managed runs exclude expensive
cases unless `-p:IncludeLongRunningTests=true --minimum-expected-tests 1` is supplied. Use
`pwsh -NoProfile -File eng/Test.ps1 -Suite processes` for the current automated process suite; see
the [suite guide](21-continuous-integration.md). The old .NET 11 preview comparison remains research,
not an additional supported production baseline.

```powershell
dotnet build Spar.slnx --disable-build-servers
dotnet test --solution Spar.slnx --no-build
dotnet format Spar.slnx --verify-no-changes --no-restore
```

For an interactive terminal experiment, start `dotnet watch --project experiments/Spar.HotReloadProbe run`, edit `GreetingProvider.cs`, and restore your edits afterward. Ctrl+C ends the session. The automated tests modify disposable copies instead. Their rude-edit auto-restart environment flag is confined to that fixture; it is not a decision to bypass Spar's future retirement protocol.

For automated editor acceptance, install the recommended C# Dev Kit and its dependencies, then run in an interactive desktop session:

```powershell
pwsh -NoProfile -File eng/Test-VSCodeProbe.ps1
pwsh -NoProfile -File eng/Test-VSCodeProbe.ps1 -TargetFramework net11.0
```

The default lane uses the pinned stable SDK `10.0.402` / `net10.0`. The comparison lane selects installed SDK `11.0.100-rc.2.26504.105` / `net11.0` only in its disposable copy. Each prints its evidence directory under `artifacts/vscode-probe`: `fixture/receipt.json`, `fixture/debugger.ndjson`, and the editor's `profile/logs`. Test timeouts and unavailable tooling are failures, not skipped passes.

For ordinary VS Code F5, open this checkout, allow C# Dev Kit to load `Spar.slnx`, and select **Spar: Hot Reload probe (C# Dev Kit)**. Enable these in **User/profile settings**, then start a fresh debug session:

```json
{
  "csharp.experimental.debug.hotReload": true,
  "csharp.debug.hotReloadOnSave": true
}
```

The installed Dev Kit declares both settings at machine scope; workspace values are ignored. Workspace extension recommendations do not enable those user settings. Run F5 directly without a watcher against that process. The isolated automation configures its own profile explicitly.

## Observed results

See the [recorded receipts and source hashes](evidence/managed-probe-2026-10-18.json). Full first-failure and successful logs remain in the local ignored artifact directories named there; these are not durable remote artifacts. CI uploads its watcher logs/hashes separately.

| Lane | Observed result |
| --- | --- |
| SDK 10.0.402 / net10.0 / CoreCLR / Debug | Selected solution builds with zero warnings/errors; all three real watcher tests pass, none skipped; formatting passes |
| SDK 11.0.100-rc.2.26504.105 / net11.0 / CoreCLR / Debug | Earlier experiment also built and passed all three real watcher tests, none skipped |
| Terminal: method body and rapid edits | Same process/start time/instance, increasing counter; final greeting applied; callback revision and updated type observed |
| Terminal: compilation failure | Real C# error, same process/instance continues its previous greeting; correction patches that process |
| Terminal: base-class edit | Watcher restarts the owned application; new process/instance and reset counter/revision |
| VS Code 1.142.0 / C# 2.160.4 / Dev Kit 3.40.210 / net11.0 | Breakpoint, saved method-body update, retained process/state, metadata callback, compiler-error recovery, ENC0014, explicit Stop/relaunch, and process exit verified |
| Same VS Code extensions / SDK 10.0.402 / net10.0 | Same complete debugger scenario passed, including metadata callback, ENC0014, explicit relaunch, and process-exit checks |
| Visual Studio Community 2026 stable, About version 18.11.0 / net10.0 project | Manual breakpoint, flame-applied update with retained state, metadata callbacks, CS0126 recovery, popup restart for an unsupported base-class edit, and subsequent process absence verified; [receipt and screenshots](evidence/visual-studio-2026-10-18/receipt.json) |
| Windows CI | Fork and upstream main runs passed. A real upstream PR build failure was observed and the non-draft PR was blocked with the check marked required; **Windows managed probe** is enforced on `main`. See the [CI gate proof](08-ci-merge-gate.md) for exact runs, recovery, and policy details |
| Native window, SPA HMR, coordinator cleanup, NativeAOT | Not implemented or verified by this probe |

The installed environment is recorded in the [kickoff matrix](05-implementation-kickoff.md#local-observations). These are compatibility observations on one development machine, not a supported Windows floor or a clean-machine certification.

## Findings and limits

- Both update owners compile edits before applying them. Compilation errors leave the existing process on previously applied code; the probe demonstrates this directly.
- VS Code can execute the patched method before `UpdateApplication` becomes visible in the next observation. Recorded callback thread IDs varied between updates; this console probe establishes no UI-thread affinity. Treat it as an eventual invalidation signal, dispatch native work onto the UI thread, and do not promise atomic data/contract activation or use callback timing to replay operations.
- First editor attempts failed: launching immediately after extension activation raced project initialization; mixed source/PDB paths produced “no document” and ENC1008 diagnostics. The working harness waits for project tasks, uses consistent launch-path casing, and disables Git source-root mapping only for this probe's local Debug builds. These are pinned-environment observations, not a proven general consumer-template fix. Recheck the normal consumer workflow before adopting a workaround in Spar's SDK.
- The first working VS Code run recorded changed behavior with revision 0, followed by a later callback. The driver now records both separately instead of falsely concluding callbacks are unsupported.
- The unsupported base-class change reports ENC0014 in **.NET Hot Reload** output. The driver reads that diagnostic log only as test evidence; its file format is not a product status API. The extension-host test environment refuses modal dialogs, and Dev Kit also logged an unexpected-error/connection-loss message on this path. Explicit Stop/relaunch recovered; automatic restart and the normal interactive dialog flow are not certified by this test.
- Debugger Stop reported exit code -1; OS process checks confirmed exit. This does not establish graceful application shutdown. The watcher tests also explicitly label forced cleanup.
- The project-task wait tests ordinary project loading without private C# service APIs. It does not establish a public automatic “edit failed/restart required” event for the future Spar editor adapter. Keep diagnostics in the editor and implement explicit relaunch/status controls where no supported event exists.

## Visual Studio manual evidence

The operator confirmed the initial breakpoint and supplied JSON samples, compiler output, and screenshots on 2026-10-18. The [receipt](evidence/visual-studio-2026-10-18/receipt.json) distinguishes operator evidence from the agent's direct process checks. Original screenshots document [settings](evidence/visual-studio-2026-10-18/settings.png), [CS0126](evidence/visual-studio-2026-10-18/compilation-error.png), [the unsupported-edit restart dialog](evidence/visual-studio-2026-10-18/unsupported-edit.png), and [the displayed version](evidence/visual-studio-2026-10-18/version.png).

Apply-on-save was disabled; the operator explicitly clicked the flame button. PID `42560`, its creation time, and instance stayed unchanged as the counter progressed `268 → 639 → 1219 → 1398`. The supported edit raised revision to 1, the compilation error retained revision 1 and the previous greeting, and correction raised revision to 2. Both callbacks recorded thread 12 and `GreetingProvider` as the updated type. These snapshots do not establish precise callback timing or UI-thread affinity.

Changing the base class produced the **Changes not supported** popup. The operator chose **Restart affected projects**, producing PID `17984`, a new creation time/instance, counter 9, and revision 0. A direct Windows lookup at `2026-10-18T16:29:25.5667354Z` confirmed both recorded PIDs absent. The exact Stop gesture and exit codes were not independently supplied; no graceful-shutdown claim follows. The two deliberate source edits were then restored. The selected SDK comes from repository `global.json`; a Visual Studio SDK build-log excerpt was not captured.

This completes the exercised console probe's Visual Studio path. Save-triggered updates, Visual Studio on .NET 11, the future native/SPA consumer, and full A13 acceptance remain unverified.

## Reproduction and next slice

Run the same script manually in Visual Studio: open `Spar.slnx`, select the probe startup project, build Debug, set a breakpoint in `GetGreeting`, F5/Continue, save a greeting edit and apply Hot Reload, introduce/correct `return ;`, change the base class and use the IDE's restart flow, then Stop. Record versions, process/instance/counter observations, metadata callback/thread, actual diagnostic and process exit. Restore source edits afterward. Installed tooling alone is not evidence.

Stable .NET 10 is the initial implementation baseline: the probe demonstrated the same VS Code debugger capabilities on both releases and no necessary .NET 11 advantage, and the Visual Studio console path now has a real manual pass. Keep the RC comparison reproducible and revisit the requirement if native/process-lifetime work demonstrates a concrete improvement. The [upstream CI gate](08-ci-merge-gate.md) is configured; the future consumer's supported status/diagnostic integration remains open, so issue #2 and the integrated milestone stay open. The managed editor proof now permits [M1.2 / issue #3](03-milestone-1.md#m12--build-the-smallest-windowsaot-application): one Windows window/WebView2 and an immediately executed NativeAOT publish, before adding the SPA coordinator.
