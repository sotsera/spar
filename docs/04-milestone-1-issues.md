# Milestone 1 issues

**Historical issue specifications, published 2026-10-18.** The unchecked lists below preserve the original scope, not current issue state. Tracker #1 and #2–#12 are now closed. See the [acceptance matrix](19-milestone-1-acceptance.md) and [current status](README.md#status-and-deferred-work).

Target repository: `sotsera/spar` (upstream). Verified GitHub CLI account: `ghidello`.

GitHub milestone: [Milestone 1: prove the Spar development loop](https://github.com/sotsera/spar/milestone/1). No due date or assignees set.

Published one tracking issue and eleven implementation issues as GitHub issues #1–#12 in `sotsera/spar`, using the verified account above. These were recreated upstream on 2026-10-18 from the approved fork issue set. The duplicate fork issues and milestone were subsequently closed as superseded, with links to upstream; their closure records migration, not implementation completion. Use upstream for ongoing milestone work. The sections below record their publication content with linked dependencies. Track subsequent implementation progress and acceptance evidence on GitHub; the product direction and milestone plan remain the planning sources.

## [M1-00](https://github.com/sotsera/spar/issues/1) — Prove the Spar development loop

### Outcome

A developer or AI agent can launch one small Windows application, edit its SPA and C# backend, observe the result, recover from an invalid edit, debug it in VS Code, and publish the same application as NativeAOT. Visual Studio supports the same application. A deliberately incompatible application dependency demonstrates an attributable CoreCLR compatibility route.

Start fresh while retaining Spar / `Sotsera.Spar` identity. Windows only; CoreCLR for development and NativeAOT by default for production. The milestone is an integrated proof. Historical results from the previous implementation do not establish completion.

Planning sources: `docs/02-product-direction.md` and `docs/03-milestone-1.md`.

### Implementation issues

- [ ] [M1-01](https://github.com/sotsera/spar/issues/2) — Establish the toolchain and prove backend Hot Reload integration
- [ ] [M1-02](https://github.com/sotsera/spar/issues/3) — Implement the minimal Windows host and prove NativeAOT execution
- [ ] [M1-03](https://github.com/sotsera/spar/issues/4) — Generate an AOT-compatible C# and TypeScript bridge
- [ ] [M1-04](https://github.com/sotsera/spar/issues/5) — Coordinate frontend startup, readiness, HMR, and session cleanup
- [ ] [M1-05](https://github.com/sotsera/spar/issues/6) — Integrate backend Hot Reload and coordinated application restart
- [x] [M1-06](https://github.com/sotsera/spar/issues/7) — Expose structured development controls and WebView automation
- [ ] [M1-07](https://github.com/sotsera/spar/issues/8) — Deliver and test the VS Code development workflow
- [ ] [M1-08](https://github.com/sotsera/spar/issues/9) — Validate the Visual Studio development workflow
- [ ] [M1-09](https://github.com/sotsera/spar/issues/10) — Publish and execute a NativeAOT application with production assets
- [ ] [M1-10](https://github.com/sotsera/spar/issues/11) — Implement attributable dependency-driven CoreCLR compatibility
- [ ] [M1-11](https://github.com/sotsera/spar/issues/12) — Validate local packages through an external consumer application

### Integrated acceptance

Record evidence links beside each scenario before checking it off. The implementation issues identify the primary owners; some scenarios require more than one issue.

- [ ] A01: Fresh documented setup reaches visible SPA and typed backend readiness. Owners: [M1-04](https://github.com/sotsera/spar/issues/5), [M1-11](https://github.com/sotsera/spar/issues/12).
- [ ] A02: CSS/component edits update the real WebView through HMR, retain supported local state, and do not restart the backend. Owner: [M1-04](https://github.com/sotsera/spar/issues/5).
- [ ] A03: A supported backend body edit changes behavior with the same process/generation and retained backend counter. Owners: [M1-05](https://github.com/sotsera/spar/issues/6), [M1-07](https://github.com/sotsera/spar/issues/8).
- [ ] A04: The frontend receives a backend update signal and refreshes relevant data. Owner: [M1-05](https://github.com/sotsera/spar/issues/6).
- [ ] A05: An exported API/DTO edit regenerates matching bindings, restarts the app, and rejects the old fingerprint. Owners: [M1-03](https://github.com/sotsera/spar/issues/4), [M1-05](https://github.com/sotsera/spar/issues/6).
- [ ] A06: Startup/unsupported edits produce an explicit restart and observable new process state. Owner: [M1-05](https://github.com/sotsera/spar/issues/6).
- [ ] A07: Broken edits show actionable diagnostics without partial activation; correction restores usability. Owner: [M1-05](https://github.com/sotsera/spar/issues/6).
- [ ] A08: Rapid edits cannot activate stale builds or overwrite current status. Owner: [M1-05](https://github.com/sotsera/spar/issues/6).
- [ ] A09: Retirement accounts for unfinished operations, prevents stale delivery, and never replays mutations. Owners: [M1-03](https://github.com/sotsera/spar/issues/4), [M1-05](https://github.com/sotsera/spar/issues/6).
- [ ] A10: Wrong origins, permissions, and contracts are rejected before handler entry. Owner: [M1-03](https://github.com/sotsera/spar/issues/4).
- [ ] A11: VS Code F5, breakpoint, Hot Reload, restart/relaunch, and Stop work in the real C# debugger lane. Owner: [M1-07](https://github.com/sotsera/spar/issues/8).
- [x] A12: Structured AI commands and real UI inspection/actions verify an edit with unambiguous session/version identity. Owner: [M1-06](https://github.com/sotsera/spar/issues/7).
- [ ] A13: Visual Studio F5, breakpoint, frontend/backend updates, restart, and Stop are executed and recorded. Owner: [M1-08](https://github.com/sotsera/spar/issues/9).
- [ ] A14: A copied NativeAOT app visibly renders and calls C# without Node or a development server; execution mode is recorded. Owner: [M1-09](https://github.com/sotsera/spar/issues/10).
- [ ] A15: An incompatible dependency fails strict AOT with attributable diagnostics and functions in explicit CoreCLR compatibility mode. Owner: [M1-10](https://github.com/sotsera/spar/issues/11).
- [ ] A16: Compile, tooling, and framework failures remain failures instead of silently selecting CoreCLR. Owner: [M1-10](https://github.com/sotsera/spar/issues/11).
- [ ] A17: Shutdown and failed startup account for owned work, processes, and profile leases; graceful and forced outcomes are distinct. Owners: [M1-02](https://github.com/sotsera/spar/issues/3), [M1-04](https://github.com/sotsera/spar/issues/5), [M1-05](https://github.com/sotsera/spar/issues/6).
- [ ] A18: An external application restores local packages and builds/runs through public APIs without framework source imports. Owner: [M1-11](https://github.com/sotsera/spar/issues/12).

### Evidence and completion

For each required scenario, record source revision or hashes, tool versions, command/configuration, application mode, session/generation/contract identity where applicable, expected and observed result, and artifact/log locations. Retain first-failure artifacts and screenshots for visible outcomes. A mock, timeout, skipped scenario, or unavailable desktop lane does not count as a pass.

Collect repeated measurements for cold/warm startup, save-to-HMR, save-to-backend-update, restart, failed-edit recovery, publication time, payload size, and separate host/browser resource use. Record workload and sample count before setting numerical targets.

Complete only when A01–A18 have evidence on the selected baseline and the sample follows its own guide. Carry clean-machine/no-.NET verification into release-readiness follow-up if unavailable. Multi-platform support, installers, signing/updating, multi-window support, and general production-readiness claims remain outside this milestone.

## [M1-01](https://github.com/sotsera/spar/issues/2) — Establish the toolchain and prove backend Hot Reload integration

Tracking issue: [M1-00](https://github.com/sotsera/spar/issues/1). Dependencies: none. Plan: M1.1. Supplies the tooling decisions required by A03, A06, A11, and A13.

### Outcome and scope

Prove supported C# editing and restart mechanisms before building substantial framework infrastructure. Use a minimal CoreCLR probe with observable process identity and mutable state. Select a compatible Windows/x64, SDK/C#, WebView2, frontend, VS Code/C# extension, and Visual Studio baseline.

Establish minimal repository build/test configuration using ordinary .NET projects, explicit central NuGet versions, xUnit v3, Microsoft.Testing.Platform selection in `global.json`, and AwesomeAssertions. Keep the probe small; it may later be replaced by the integrated sample.

### Acceptance criteria

- [ ] A method-body edit changes subsequent behavior while retaining process and state under terminal `dotnet watch`, VS Code F5, and Visual Studio F5.
- [ ] An unsupported edit is exercised in each lane; the reporting and supported restart/debugger relaunch path are recorded.
- [ ] Each lane has one backend update owner. No process runs two Hot Reload engines.
- [ ] A supported update notification mechanism is demonstrated, or a tested alternative and its limits are documented. No private debugger protocol or custom delta compiler is assumed.
- [ ] Tool versions, launch commands, repository configuration, and compatibility decisions are recorded. Test discovery/execution works through the selected runner.
- [ ] Constraints that affect later issues are reflected in the plan before substantial infrastructure depends on them.

### Evidence

Retain commands, tool versions, before/after process and state observations, debugger evidence, unsupported-edit diagnostics, and the selected notification/restart strategy. An editor command unit test cannot prove debugger Hot Reload.

## [M1-02](https://github.com/sotsera/spar/issues/3) — Implement the minimal Windows host and prove NativeAOT execution

Tracking issue: [M1-00](https://github.com/sotsera/spar/issues/1). Depends on: [M1-01](https://github.com/sotsera/spar/issues/2). Plan: M1.2. Acceptance coverage: early A14 proof and native portion of A17.

### Outcome and scope

Provide one public consumer entry point that creates a Win32/WebView2 window, loads a local document, and shuts down with explicit ownership. Keep the runtime independent of inspection and development tooling. Demonstrate the selected interop/loader path under NativeAOT immediately.

### Acceptance criteria

- [ ] An ordinary consumer application visibly renders a local page under CoreCLR and a real copied NativeAOT executable.
- [ ] UI-thread dispatch, COM/native handles, callbacks, and startup/shutdown transitions have explicit ownership.
- [ ] Normal close and failed startup release or account for owned work and profile leases; application-owned work is distinguished from browser processes.
- [ ] Missing WebView2 and native initialization failures produce actionable outcomes.
- [ ] Browser profile data is preserved by default; profile deletion is not a shutdown side effect.
- [ ] Runtime dependencies exclude development/measurement tools.

### Evidence

Record publication/run commands, execution mode, visible screenshots, focused lifecycle checks, and process/resource observations. Copy and execute the output outside its build directory. Do not claim clean-machine/no-.NET certification from a developer-machine run.

## [M1-03](https://github.com/sotsera/spar/issues/4) — Generate an AOT-compatible C# and TypeScript bridge

Tracking issue: [M1-00](https://github.com/sotsera/spar/issues/1). Depends on: [M1-02](https://github.com/sotsera/spar/issues/3). Plan: M1.3. Acceptance coverage: A10 and bridge portions of A05/A09.

### Outcome and scope

Generate direct dispatch, JSON metadata, a consumer adapter, and typed TypeScript bindings from explicit application exports. Support a small documented DTO surface and greeting, backend counter, and cancellable delayed operations. The application must not require handwritten repetitive dispatch glue.

### Acceptance criteria

- [ ] All three operations execute through the actual WebView under CoreCLR and NativeAOT.
- [ ] Explicit permissions, contract fingerprints, bounded inputs/outputs, and captured native document authority are enforced before handler entry.
- [ ] Unknown/denied operations, malformed input, stale fingerprints, and wrong origins are rejected through observable tests.
- [ ] Navigation or document retirement prevents delivery into a replacement document.
- [ ] Cancellation/timeout does not release resources still in use or claim handler completion. Test an operation that ignores cancellation.
- [ ] Native authority and navigation policy distinguish the packaged document from the later session-scoped development origin.

### Evidence

Retain generated consumer output examples, focused contract tests, actual CoreCLR/AOT roundtrips, rejection evidence, and navigation/retirement observations. Application handler-entry counters should establish that denied requests never execute application code.

## [M1-04](https://github.com/sotsera/spar/issues/5) — Coordinate frontend startup, readiness, HMR, and session cleanup

Tracking issue: [M1-00](https://github.com/sotsera/spar/issues/1). Depends on: [M1-03](https://github.com/sotsera/spar/issues/4). Plan: M1.4. Acceptance coverage: A01, A02, and session lifecycle portion of A17.

### Outcome and scope

One session start action launches a configured frontend command and the application. React/Vite is the reference adapter; the runtime/client remain framework-neutral. Use the SPA tool's actual HMR rather than implementing a competing module update system.

### Acceptance criteria

- [ ] Start discovers or receives the actual loopback URL, waits for frontend readiness, and reaches visible render plus a typed backend handshake/call.
- [ ] CSS/component edits update the actual WebView without rebuilding/restarting C#; supported counter/text-field state is retained.
- [ ] Frontend command, package manager, and port are configurable; no fixed port becomes a runtime requirement.
- [ ] Startup timeout, port conflict, frontend exit, and application exit produce distinct actionable states.
- [ ] Structured events identify the owned session and application generation; the control channel is authenticated and bridge authority is session/document-scoped.
- [ ] Stop cleans up owned processes/work. Process ownership checks include creation identity, not PID alone; unrelated processes remain untouched.

### Evidence

Retain a fresh-start walkthrough, visible HMR/state observations, failure scenarios, structured lifecycle logs, and cleanup observations. A listening port or navigation event alone is insufficient readiness evidence.

## [M1-05](https://github.com/sotsera/spar/issues/6) — Integrate backend Hot Reload and coordinated application restart

Tracking issue: [M1-00](https://github.com/sotsera/spar/issues/1). Depends on: [M1-01](https://github.com/sotsera/spar/issues/2), [M1-04](https://github.com/sotsera/spar/issues/5). Plan: M1.5. Acceptance coverage: A03–A09 and restart portion of A17.

### Outcome and scope

Apply the proven update mechanism to the real Spar application. Preserve process/backend state for supported implementation edits and notify the frontend so application code can refetch relevant data. Coordinate full application replacement for contract, startup, dependency, and unsupported edits.

### Acceptance criteria

- [ ] Editing the greeting changes behavior with the same process/generation and retained backend counter; a separate update signal refreshes relevant displayed data.
- [ ] Export/DTO changes regenerate and build matching outputs, restart the application, and reject stale client fingerprints.
- [ ] Startup/dependency/unsupported edits produce explicit restart outcomes and observable new process state.
- [ ] Compile errors expose file/line diagnostics and never activate partial or mismatched output; correction recovers to a usable generation.
- [ ] Rapid edits are coalesced; outdated build completions/results cannot replace the selected generation or its status.
- [ ] Retirement stops admission and stale delivery, requests owned shutdown, and waits for actual exit before launching a replacement.
- [ ] Work that ignores cancellation remains accounted for. A timeout does not authorize overlapping mutation owners; explicit forced termination is distinct from graceful completion. Mutations are never replayed automatically.

### Evidence

Record visible results and correlated update/build/restart sequences, process identity, backend state, contract identity, failed-edit recovery, rapid-edit races, and in-flight operation outcomes. Distinguish SPA HMR, backend patch, page refresh, and application restart in status output.

## [M1-06](https://github.com/sotsera/spar/issues/7) — Expose structured development controls and WebView automation

Tracking issue: [M1-00](https://github.com/sotsera/spar/issues/1). Depends on: [M1-04](https://github.com/sotsera/spar/issues/5); final edit/restart walkthrough also requires [M1-05](https://github.com/sotsera/spar/issues/6). Plan: AI portion of M1.6. Acceptance coverage: A12.

### Outcome and scope

An agent can start a session, wait for actual readiness, inspect failures, operate the rendered UI, verify a changed result, request restart, and stop the owned session. CLI and test drivers share coordinator/inspection services. Any MCP adapter is a thin additional surface over those same services.

### Acceptance criteria

- [x] Structured start/status/inspect/restart/stop operations include session, generation, process, document/contract, and correlation identity where applicable.
- [x] Command acceptance and operation completion are distinct; timeouts identify unfinished work.
- [x] Frontend console/errors and backend/build diagnostics can be collected alongside a DOM or accessibility snapshot and screenshot.
- [x] Bounded click/type actions operate against the actual development WebView and cannot silently target a retired document.
- [x] An agent-driven scenario verifies an edit through rendered UI inspection/actions and retains failure artifacts.
- [x] Inspection/control is development-only and does not broaden renderer permissions or expose arbitrary shell/filesystem execution.

### Evidence

Retain structured transcripts, screenshots/snapshots, actual UI actions, correlation identities, timeout/failure outcomes, and the agent-driven verification scenario. Browser mocks alone do not establish completion. Production exclusion is verified in [M1-09](https://github.com/sotsera/spar/issues/10).

Observed implementation: [structured inspection guide](15-development-inspection.md) and [native acceptance evidence](evidence/development-inspection-2026-10-19/README.md). Generic operations run without the sample probe. Debug opt-in and Release metadata exclusion are verified; full production-SPA delivery remains #10. Optional memory observation and an MCP adapter remain follow-ups.

## [M1-07](https://github.com/sotsera/spar/issues/8) — Deliver and test the VS Code development workflow

Tracking issue: [M1-00](https://github.com/sotsera/spar/issues/1). Depends on: [M1-05](https://github.com/sotsera/spar/issues/6) and shared controls from [M1-06](https://github.com/sotsera/spar/issues/7). Plan: editor portion of M1.6. Acceptance coverage: A11.

### Outcome and scope

Provide the smallest VS Code adapter/configuration needed for launch/debug, diagnostics, reload/restart status, and Stop. Reuse the coordinator and ordinary C# debugging. Preserve a single backend update owner per application process.

### Acceptance criteria

- [ ] F5 launches both sides to visible/typed readiness and a real backend breakpoint can be hit and continued.
- [ ] Frontend HMR and supported backend Hot Reload are observed in the real installed C# debugger lane.
- [ ] Restart/relaunch follows the strategy proven in [M1-01](https://github.com/sotsera/spar/issues/2) and restores useful debugging through the documented flow.
- [ ] Build errors navigate to relevant source locations and status distinguishes update rejection from restart and completion.
- [ ] Stop accounts for owned session processes/work.
- [ ] Adapter behavior is automated in an Extension Development Host using supported APIs. Required human interactions and automation limits are explicit.

### Evidence

Retain automated adapter results and a real debugger scenario covering F5, breakpoint, continue, edit, observed behavior, restart/relaunch, and Stop. Extension command tests alone cannot prove debugger correctness.

## [M1-08](https://github.com/sotsera/spar/issues/9) — Validate the Visual Studio development workflow

Tracking issue: [M1-00](https://github.com/sotsera/spar/issues/1). Depends on: [M1-05](https://github.com/sotsera/spar/issues/6). Plan: editor portion of M1.7. Acceptance coverage: A13.

### Outcome and scope

The same consumer application supports a pleasant Visual Studio path through ordinary launch/build/debug integration. Share the coordinator and keep editor-specific configuration small. A recorded manual Windows acceptance pass is sufficient initially.

### Acceptance criteria

- [ ] F5 reaches visible SPA/typed readiness and a backend breakpoint can be hit and continued.
- [ ] Frontend HMR and a supported backend edit exhibit the expected state behavior with Visual Studio owning backend updates.
- [ ] Unsupported and contract edits use the documented restart/debugger flow with matching bindings.
- [ ] Stop cleans up owned session work/processes.
- [ ] Setup, selected versions, differences from VS Code, and any limitations are documented from actual execution.

### Evidence

Retain the executed walkthrough, debugger/update/restart observations, diagnostics, and cleanup outcomes. Shared source or passing VS Code checks do not establish Visual Studio acceptance.

## [M1-09](https://github.com/sotsera/spar/issues/10) — Publish and execute a NativeAOT application with production assets

Tracking issue: [M1-00](https://github.com/sotsera/spar/issues/1). Depends on: [M1-03](https://github.com/sotsera/spar/issues/4); final development-control exclusion check also requires [M1-06](https://github.com/sotsera/spar/issues/7). Plan: production portion of M1.7. Acceptance coverage: A14.

### Outcome and scope

Build the frontend, generate matching contracts, package local assets, and publish the same consumer application through the default NativeAOT route. The installed application requires neither Node nor a production UI HTTP server.

### Acceptance criteria

- [ ] A normal production publish produces NativeAOT output with a receipt identifying mode, source, tooling, and configuration.
- [ ] The copied payload runs outside the checkout, visibly renders packaged assets, and executes a typed backend call without a development server or Node process.
- [ ] Packaged asset authority and matching client/server contracts are verified.
- [ ] Development endpoints, inspection, and automation controls are absent or inaccessible through a verifiable production build boundary.
- [ ] Missing WebView2 produces an actionable result; browser runtime/profile requirements and delivery shape are documented.
- [ ] Publication time, payload size, startup, and separate host/browser resource observations are recorded with workload and sample count.

### Evidence

Retain publish/run commands, receipt, screenshots, bridge output, endpoint exclusion checks, and copied-payload observations. Attempt a Windows machine without the .NET SDK/runtime when available; otherwise record the gap for release readiness rather than claiming certification. Installers, signing, and updates are deferred.

## [M1-10](https://github.com/sotsera/spar/issues/11) — Implement attributable dependency-driven CoreCLR compatibility

Tracking issue: [M1-00](https://github.com/sotsera/spar/issues/1). Depends on: [M1-09](https://github.com/sotsera/spar/issues/10). Plan: compatibility portion of M1.7. Acceptance coverage: A15, A16.

### Outcome and scope

Provide an observable, reproducible CoreCLR production route when an application-added dependency requires behavior unavailable under NativeAOT. Keep strict AOT available and preserve failures unrelated to dependency incompatibility.

For milestone 1, use an explicit persisted compatibility selection. Decide its concrete configuration during implementation. Any automatic selector is additional scope requiring evidence of cause classification; it cannot be a catch-all publish-failure handler.

### Acceptance criteria

- [ ] A dedicated fixture adds an actually incompatible dependency and executes its unsupported mechanism; strict AOT rejects it with diagnostics attributable to the dependency/API.
- [ ] The documented explicit CoreCLR compatibility selection publishes and runs that fixture successfully, with a delivery-mode receipt.
- [ ] Ordinary syntax errors, missing toolchains, unrelated native link errors, and a deliberate framework AOT regression remain failures rather than producing unexpected CoreCLR artifacts.
- [ ] Strict AOT can be requested in CI regardless of compatibility selection.
- [ ] Removing the dependency permits reevaluating AOT; incompatibility is not cached forever.
- [ ] Development surfaces useful compatibility diagnostics; CoreCLR development execution is not treated as evidence of AOT compatibility.

### Evidence

Retain the fixture, executed unsupported path, strict-AOT diagnostics, CoreCLR execution, mode receipts, and each unrelated-failure scenario. Update product direction with the final configuration and packaging decisions.

## [M1-11](https://github.com/sotsera/spar/issues/12) — Validate local packages through an external consumer application

Tracking issue: [M1-00](https://github.com/sotsera/spar/issues/1). Depends on: [M1-07](https://github.com/sotsera/spar/issues/8), [M1-08](https://github.com/sotsera/spar/issues/9), [M1-09](https://github.com/sotsera/spar/issues/10), [M1-10](https://github.com/sotsera/spar/issues/11). Plan: consumer/documentation portion of M1.7. Acceptance coverage: A18 and final A01 walkthrough.

### Outcome and scope

Prove that the public consumer experience works through packages and its own guide. Restore Spar packages from a local feed into an application outside the framework checkout, without source imports, friend-assembly assumptions, or private output-directory conventions.

### Acceptance criteria

- [ ] Local packages expose the required runtime, generation/build, and development functionality through documented public integration.
- [ ] An external consumer restores from the local feed and generates bindings, builds, runs, and publishes without framework source-project references.
- [ ] The documented start/edit/debug/publish guide is followed with the selected prerequisites and both editor lanes; record tested differences and limitations.
- [ ] Default AOT and explicit dependency-driven CoreCLR delivery can be exercised through public consumer configuration.
- [ ] The README and milestone status accurately identify implemented behavior and unavailable checks.
- [ ] Final integrated evidence is linked into [M1-00](https://github.com/sotsera/spar/issues/1), including outstanding acceptance scenarios and release-readiness gaps.

### Evidence

Retain package identities/versions, local feed setup, external-directory commands, consumer source/configuration, run/publication results, and guide walkthrough. Public NuGet publication is not required. Do not close the tracking issue while a required acceptance scenario lacks execution evidence.
