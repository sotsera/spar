# Backend reload: Reactor, NeoAstra, and Tauri

Reviewed: 2026-10-18. Status: source inspection and proposed Spar adaptations; upstream applications and tests were not executed.

This supplements the [reference comparison](01-reference-projects.md) and [implementation kickoff](05-implementation-kickoff.md). HMR means frontend module replacement here; backend Hot Reload means applying supported managed code edits to an existing process. Page reload and process restart are separate outcomes.

## Reviewed source revisions

| Project | Revision |
| --- | --- |
| Microsoft.UI.Reactor | `e16819520b6452d93fdeba9f59e2b81a47febd1c` |
| NeoAstra | `7220b91b7c860c4bbee9488b19146918d9afe5d7` |
| Tauri | `136a28eaa879e8aa706f9b2e05a050c46dcc1f4f` |

These are the default-branch HEADs resolved during this inspection and match the earlier license review. The conclusions below are bounded to the inspected workflows. No upstream source was copied into Spar.

## What each workflow actually does

| Project/workflow | Frontend update | Backend update | State implications |
| --- | --- | --- | --- |
| Reactor runtime | Re-renders its native WinUI component tree | Receives .NET metadata-update callbacks after the runtime/tool applies supported edits | Existing hook state can survive; additional code migrates selected renderer-managed values or resets affected contexts |
| Reactor VS Code preview | Requests rendering in the preview host | Extension launches `dotnet watch run` | Establishes a watcher-based preview design, not the behavior of the C# Dev Kit F5 debugger |
| NeoAstra development CLI | External frontend tool owns HMR | Default backend command is `dotnet watch run` | Supported .NET edits can patch; restart-required changes depend on watch/runtime behavior. Inspected orchestration does not itself provide Reactor's hook-state migration |
| Tauri desktop development CLI | Configured frontend server owns HMR; built-in static server has page autoreload | Watches Rust/configuration paths, stops the existing child, waits, and invokes its build/run path again | Process-local backend state is recreated; frontend/plugin HMR preservation does not imply backend preservation |

## Reactor: the most useful backend feedback reference

Its [HotReloadService](https://github.com/microsoft/microsoft-ui-reactor/blob/e16819520b6452d93fdeba9f59e2b81a47febd1c/src/Reactor/Hosting/HotReloadService.cs#L4) registers an assembly-level `MetadataUpdateHandler`. The inspected service consumes updates; it does not generate compiler deltas or replace Microsoft's update engine.

`UpdateApplication(updatedTypes)` records the changed types, publishes a pending-update flag, and requests a forced render. Atomic capture/clear coalesces pending work for the UI thread. A pass scope identifies reload-related rendering and restores its state on disposal. The host then forces updated code to run even when normal memoization would skip rendering. See [host processing](https://github.com/microsoft/microsoft-ui-reactor/blob/e16819520b6452d93fdeba9f59e2b81a47febd1c/src/Reactor/Hosting/ReactorHostControl.cs#L723).

State preservation has several layers. Ordinary method-body updates retain live hook contexts. [RenderContext](https://github.com/microsoft/microsoft-ui-reactor/blob/e16819520b6452d93fdeba9f59e2b81a47febd1c/src/Reactor/Core/RenderContext.cs#L2475) also cleans up and resets incompatible hook sequences. For selected edited value types, the [copier](https://github.com/microsoft/microsoft-ui-reactor/blob/e16819520b6452d93fdeba9f59e2b81a47febd1c/src/Reactor/Hosting/ReactorHotReloadCopier.cs#L9) creates replacement values and copies matching fields, with handling for incompatible fields and cycles. Those paths depend on the runtime having accepted the code update; they do not make unsupported compiler edits patchable.

The service uses `MetadataUpdater.IsSupported` and reload-pass state to gate its reflection-bearing migration path. Spar should preserve the development/production separation while verifying its own published payload; another project's trim guards and suppressions are not sufficient evidence for ours.

The [VS Code extension](https://github.com/microsoft/microsoft-ui-reactor/blob/e16819520b6452d93fdeba9f59e2b81a47febd1c/src/vscode-reactor/src/extension.ts#L328) constructs `dotnet watch run --project ... -- --devtools run --vscode` for preview, with a legacy preview option. Therefore the preview's behavior cannot settle whether our debugger-owned VS Code lane invokes `MetadataUpdateHandler`. Reactor's [Visual Studio testing guide](https://github.com/microsoft/microsoft-ui-reactor/blob/e16819520b6452d93fdeba9f59e2b81a47febd1c/src/vs-reactor/TESTING.md#L35) separately distinguishes in-place rendering from child respawn; these are documented test expectations, not execution evidence collected for Spar.

The [reload unit tests](https://github.com/microsoft/microsoft-ui-reactor/blob/e16819520b6452d93fdeba9f59e2b81a47febd1c/tests/Reactor.Tests/HotReload/HotReloadUpdatePassTests.cs) exercise pass-scoping primitives directly. Useful inspiration for deterministic tests, but such tests cannot establish real debugger/runtime update delivery. The design spec still labels itself proposed while migration implementation is present in source, so source behavior takes precedence over that status header.

### What Spar should borrow

- A small metadata-update adapter, leaving compilation and patch application with the supported .NET tool/debugger.
- Thread-safe, coalesced notification followed by work on the appropriate dispatcher/session context.
- Explicit recovery and refresh controls, with status distinguishing successful patch, failed refresh, page refresh, and process restart.
- Development-only behavior with an independently verified production boundary.
- Deterministic recovery tests alongside real watch/debugger acceptance scenarios.

Translate Reactor's forced native re-render into an explicit backend-update event that lets the SPA invalidate/refetch relevant read data. Preserve ordinary live backend objects when .NET patches method bodies. Keep process generation distinct from an in-process update revision and contract identity.

Defer a generic field-copy migration system. Renderer-managed hook values are narrower than backend service graphs containing DI services, event delegates, native/COM handles, streams, tasks, transactions, and pending mutations. Generic copying would not establish safe ownership. Contract/startup edits can use the already planned explicit restart route.

## NeoAstra: ordinary .NET updates inside a coordinated dev session

The [configuration resolver](https://github.com/NeoAstra/NeoAstra/blob/7220b91b7c860c4bbee9488b19146918d9afe5d7/src/NeoAstra.Tool/Tooling/NeoProjectConfiguration.cs#L348) defaults `backendCommand` to `dotnet watch run` and `contractCommand` to `dotnet build --no-restore`.

The [development orchestrator](https://github.com/NeoAstra/NeoAstra/blob/7220b91b7c860c4bbee9488b19146918d9afe5d7/src/NeoAstra.Tool/Tooling/NeoProcessOrchestration.cs#L351) runs contract generation/build, starts the frontend, awaits an HTTP readiness probe, then starts the backend with the development URL/environment. It labels child logs and observes child exits/cancellation. `NeoAstraBuildFrontend=false` avoids running a competing production frontend build while the frontend dev server is active. The [guide](https://github.com/NeoAstra/NeoAstra/blob/7220b91b7c860c4bbee9488b19146918d9afe5d7/doc/frontend-tooling-and-assets.md#L68) explicitly delegates frontend HMR to Vite and C# restart to watch.

This is not evidence that NeoAstra always restarts for every C# edit: the default command uses .NET's patch-or-restart behavior. Nor did this inspection establish a custom backend migration/update-notification layer analogous to Reactor's renderer.

Its [ReloadTests](https://github.com/NeoAstra/NeoAstra/blob/7220b91b7c860c4bbee9488b19146918d9afe5d7/src/NeoAstra.Tests/ReloadTests.cs#L10) exercise document reload, including loss of a JavaScript marker. They should not be cited as C# Hot Reload tests.

Borrow explicit startup sequencing, configurable argument-array commands, labeled diagnostics, separation of frontend production preparation from development, and child-process failure observation. Spar needs stronger application readiness than HTTP alone: actual visible render plus typed bridge readiness. Its retirement/termination policy also needs its own implementation; NeoAstra's child cleanup may escalate to process-tree kill after graceful shutdown attempts, whereas Spar distinguishes forced exit explicitly.

## Tauri: backend rebuild/restart, frontend HMR

The desktop [development setup](https://github.com/tauri-apps/tauri/blob/136a28eaa879e8aa706f9b2e05a050c46dcc1f4f/crates/tauri-cli/src/dev.rs) launches `beforeDevCommand` and waits for the configured development endpoint. Its Rust [watcher](https://github.com/tauri-apps/tauri/blob/136a28eaa879e8aa706f9b2e05a050c46dcc1f4f/crates/tauri-cli/src/interface/rust.rs#L506) debounces events, applies ignore rules, handles configuration changes, calls `child.kill()`, waits for exit, and starts the build/run path again. The [desktop runner](https://github.com/tauri-apps/tauri/blob/136a28eaa879e8aa706f9b2e05a050c46dcc1f4f/crates/tauri-cli/src/interface/rust/desktop.rs#L46) launches the configured Cargo/runner command.

For this workflow, wording such as Rust hot reload means rebuild/restart rather than applying Rust method changes to a running process. The old child is stopped before the replacement build/run attempt, so this is also not a guarantee of keeping the old app usable through a bad Rust edit.

Frontend framework HMR remains the external server's responsibility. Tauri also has a [static-page autoreload script](https://github.com/tauri-apps/tauri/blob/136a28eaa879e8aa706f9b2e05a050c46dcc1f4f/crates/tauri-cli/src/dev/auto-reload.js) that uses a WebSocket and `window.location.reload()`; page refresh is a separate mechanism from component HMR.

Borrow cohesive commands, watcher filtering/debouncing, separate frontend/backend lifetimes, and explicit stop/wait/relaunch sequencing. Spar can improve supported C# implementation edits with managed Hot Reload; adopting the Tauri product shape does not require restricting Spar to backend restarts.

## Changes to the first Spar probe

In upstream issue #2, reimplement a minimal version of the Reactor callback pattern without importing its renderer. Measure runtime callbacks and observable behavior independently in watch, VS Code F5, and Visual Studio F5. Also distinguish Reactor-style watcher-backed preview from debugger-backed development.

Probe rapid updates, callback thread/dispatch behavior, compile errors, and edits the runtime rejects. Treat a pending flag as coalesced work, not an exact count of applied edits; design Spar's update revision and payload publication consistently. A saved source file or emitted build diagnostic is not confirmation that patched behavior is active.

In issue #6, connect confirmed in-process updates to the SPA's refresh/invalidation hook. Never automatically replay application mutations. Regenerate and restart for changed contracts; account for the retiring generation before replacement. Unsupported notification behavior in an editor requires a tested supported alternative, not an inferred private integration.

No new Hot Reload engine, arbitrary backend graph migration, or permanent worker-host architecture is selected by this review. The concrete recommendation is to combine Microsoft's patching engine with Reactor-inspired live-update feedback and the explicit orchestration lessons from NeoAstra and Tauri.
