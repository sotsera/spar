# Milestone 1: prove the SPAR development loop

Date: 2026-10-19. Status: the [owned SPA session](11-development-session.md) supplies actual HMR/state retention (A02). The [native backend watcher lane](12-backend-updates.md) supplies same-process edits and SPA read refresh (A03/A04), compiler recovery, contract rebuild, and actual retirement evidence. The [VS Code debugger adapter](13-vscode-development.md) and [Visual Studio lane](14-visual-studio-development.md) have integrated acceptance (A11/A13). [Structured inspection](15-development-inspection.md) establishes A12. [Production SPA delivery](16-production-delivery.md) adds copied NativeAOT application evidence for A14. [Explicit compatibility](17-dependency-compatibility.md) adds dependency execution, strict rejection, and unrelated-failure evidence for A15/A16. External consumption and remaining integrated acceptance stay open.

Inputs: [product direction](02-product-direction.md), [reference projects and license review](01-reference-projects.md).

## Outcome

A developer or AI agent can launch one small Windows application, edit its SPA and C# backend, observe the result, recover from an invalid edit, debug it in VS Code, and publish the same application as NativeAOT. A supported Visual Studio path exercises the same application. An intentionally incompatible application dependency demonstrates the CoreCLR compatibility route without weakening framework AOT requirements.

The milestone is an integrated product proof, not a checklist of individually working libraries. The previous SPAR's tests and recorded passes do not count as completion.

## Demonstration application

Use one ordinary consumer .NET project with a small React/Vite SPA, plus a minimal package fixture only where isolation is needed. The SPA contains:

- A local counter and text field to observe frontend state across HMR.
- A typed backend greeting operation whose implementation can be edited.
- A backend counter to distinguish an in-process patch from a restart.
- A cancellable delayed operation for retirement/shutdown tests.
- A small development status surface showing generation, contract, and reload outcome.

Readiness means a visible rendered page and a successful typed handshake/call, not merely a process ID, open port, or navigation event. The frontend has no production development-status dependency.

## Work sequence

Each step produces a small demonstrable result and a short evidence record. Do not mark a step complete based only on source inspection. The sequence is provisional where the initial tooling experiment exposes a constraint.

### M1.1 — Pin tooling and prove Hot Reload integration

Select the supported Windows/x64 baseline and SDK/editor/frontend versions. Start with a minimal CoreCLR application before building substantial framework infrastructure.

Verify a method-body edit under terminal `dotnet watch`, VS Code F5 with the C# debugger, and Visual Studio F5. Observe changed behavior on subsequent calls and retained process/state. Verify an unsupported edit and identify how each lane reports it and permits restart. Determine a supported way to notify the coordinator/frontend after an update; if an event is unavailable, document and test the alternative instead of inventing a private debugger contract.

Deliverables: version matrix, minimal reproducible probe, selected update owner for each lane, restart/debugger strategy, and precise supported limitations. Stop expanding this probe once it answers those questions. Do not develop a custom Hot Reload compiler as an incidental workaround.

Progress: [managed probe](07-managed-probe.md), with xUnit integration tests, an actual VS Code extension-host driver, a manual Visual Studio 2026 stable 18.11.0 pass, and successful fork/upstream Windows CI. Stable .NET 10 is the implementation baseline. The [upstream CI gate](08-ci-merge-gate.md) enforces the real check. The later [VS Code](13-vscode-development.md) and [Visual Studio](14-visual-studio-development.md) native consumer evidence resolves the source/PDB, one-owner notification, and documented restart handoff gate from issue #2. Metadata callbacks remain eventual invalidation signals; interactive debugger toolbar restart is not the accepted coordinated restart flow.

### M1.2 — Build the smallest Windows/AOT application

Implement native window/WebView ownership, UI-thread dispatch, initial local document loading, native startup errors, profile ownership, and shutdown. Keep runtime code independent of developer tools.

Publish and run a real NativeAOT copy at this stage. Establish that the chosen WebView interop/loader and startup path work with AOT before the development coordinator grows around them. Verify visible render, normal close, startup failure, and no unaccounted application-owned work after shutdown. Record browser-process observations separately.

Deliverables: one public host entry point, CoreCLR development run, copied AOT run, and focused native lifecycle checks.

Progress: the [minimal host](09-native-host.md) provides the public entry point, local HTML consumer, explicit STA/COM ownership, UI dispatch, deferred close during creation, and profile preservation. CoreCLR and copied NativeAOT desktop runs visibly rendered and resized; lifecycle/error checks passed. This establishes early native/AOT evidence, not A14's typed-call/production-asset acceptance or all of A17's later coordinator cleanup scenarios.

### M1.3 — Add a typed, document-bound call

Generate a small C#/TypeScript contract from explicit application exports. Use direct dispatch and generated JSON metadata, explicit permissions, contract fingerprints, bounded messages, and captured native document authority.

The greeting, counter, and delayed operation must work through the actual WebView. Verify unknown/denied operations, malformed input, stale fingerprints, cancellation, and response delivery after navigation or retirement. Keep the DTO surface small and documented.

Deliverables: generated consumer adapter, typed frontend call, focused contract tests, and real CoreCLR/AOT roundtrips. Consumers should not write repetitive framework dispatch glue.

Progress: the [typed bridge](10-typed-bridge.md) generates direct C# dispatch, JSON metadata, and a framework-neutral JavaScript module with TypeScript declarations from an explicit C# contract. Real CoreCLR and copied NativeAOT probes verify greeting/counter/delay, rejection, cancellation/timeout ownership, retirement, and close; desktop success captures were inspected. Contract and lifetime tests plus TypeScript call-site checks extend the existing required CI job. This is the local-document bridge proof, not SPA session admission, full production assets, debugger integration, or completion of A01–A18.

### M1.4 — Coordinate the SPA server and HMR

Provide one session start/stop path. Launch the configured frontend command, wait for its actual readiness, then load its development URL in the application. Let Vite and its framework plugin apply HMR.

Track the frontend and application separately. Handle startup timeout, port conflicts, frontend exit, and application exit. Frontend edits must not rebuild the C# application unnecessarily. Development origins and control authority must be scoped to the owned session.

Deliverables: one terminal/AI launch path, structured lifecycle events, CSS/component HMR demonstrations, and verified cleanup of owned processes. Full process-tree observations need PID plus creation identity, not an unqualified PID reuse assumption.

Exercise terminal close, normal app close outside a planned restart, editor workspace/debug Stop, failed startup, and abrupt coordinator/owner exit. Keep the frontend alive across expected backend restarts, then observe all owned dev-server helpers exiting when the session ends. Test the process-lifetime safeguard independently from graceful shutdown; no unrelated Node/browser processes may be terminated. The early-window waiting/error page is polish after basic sequencing works, and does not count as application readiness.

Progress: the [development session guide](11-development-session.md) and [desktop evidence](evidence/development-session-2026-10-19/README.md) cover the owned build/start path, private loopback/bridge authority, actual CSS/React HMR with retained state, frontend retention across explicit application restart, and actual process cleanup under native close, editor task Stop/window close, owner loss, and startup failure. A02 has direct evidence. The CLI restart relaunches the same compiled application; automatic backend updates, debugger Stop/relaunch integration, externally supplied servers, and waiting-window polish are not claimed.

### M1.5 — Integrate backend patches and coordinated restart

Connect the mechanism selected in M1.1 to the actual SPAR application. For a supported implementation edit, preserve process/backend state and notify the frontend to refresh relevant data. Do not promise preservation for initialization changes or unsupported edits.

For a contract change, regenerate both sides and build matching outputs. Retire the old document/admission, request shutdown, establish that old execution has ended, then launch the new application and handshake. Coalesce rapid edits. Maintain generation IDs so old builds/results cannot become current after a newer edit wins.

Compile failure must remain visible and must not activate mismatched outputs. An operation that ignores cancellation prevents a graceful restart until it finishes or the user explicitly chooses process termination; a timeout is not permission to overlap old and new mutation owners. Never replay a mutation to recover a missing response.

Use one running backend and a failed-update status for compile errors; do not introduce concurrent version hosting, backend-state snapshots, or a rollback store. Retaining the current app is conditional on contract compatibility. A stopped/error outcome is acceptable when matching client/server activation cannot be maintained simply, with correction leading back to a usable app.

Deliverables: supported body patch, contract restart, startup/dependency restart, compile-error recovery, rapid-edit behavior, and in-flight-operation retirement demonstrations.

Progress: the [backend guide](12-backend-updates.md) and [native evidence](evidence/backend-updates-2026-10-19/README.md) connect the selected .NET watcher to the ordinary SPA consumer. Method patches and SPA read invalidation retain process/document/state; errors and corrections are visible. Unsupported edits and separately monitored contract source changes retire before full rebuild, failed rebuilds remain stopped, and correction plus explicit restart establishes matching contracts. A 12-second cancellation-ignoring operation finishes before replacement; explicit forced stop is distinct and launches no replacement. A03/A04 have direct native evidence. The restart-policy follow-up adds explicit initialization inputs, monitored dependency roots, source hash/revision selection around full builds and admission, and automatic stopped-state recovery. Deterministic MSBuild gates exercise overlapping valid/invalid source and obsolete success/failure results; see the [restart-policy evidence](evidence/restart-policy-2026-10-19/README.md) and backend guide for observed results and the declared input boundary. Task/window tests do not establish C# debugger lanes.

### M1.6 — Make VS Code and AI operation first-class

Keep coordinator logic independent of the editor. Add only the VS Code integration needed for launch/debug, diagnostics, reload/restart status, and stop. Use ordinary C# debugging rather than replacing it.

Provide structured start/status/inspect/restart/stop operations. Add screenshot, snapshot, click/type, and log/error collection against the actual development WebView. Reuse these services in the test driver. The CLI/structured interface is required; an MCP adapter can be thin once the shared operations work and is not a second orchestration implementation.

Automate the VS Code adapter in an Extension Development Host. Also run an installed C# debugger integration scenario: launch, hit a backend breakpoint, continue, apply an edit, verify changed behavior, restart, and verify the debugger reconnects or relaunches through the documented flow. Extension command tests alone cannot establish debugger Hot Reload correctness.

Deliverables: repeatable VS Code workflow, automated editor checks, agent-driven UI verification, and retained failure artifacts. Use supported VS Code APIs; any required human interaction is reported explicitly.

Progress: the [VS Code guide](13-vscode-development.md) and [debugger evidence](evidence/vscode-development-2026-10-19/README.md) cover the local adapter's ordinary C# Dev Kit launch, native breakpoints before and after coordinated relaunch, actual SPA HMR and debugger-owned method patches, state retention, compiler recovery/source diagnostics, contract rebuilds, native pixels, and explicit Stop. Contract assemblies have no patchable symbols, with admission enforcing that boundary. Public editor APIs drive the adapter; private Hot Reload logs are read only by acceptance tests to retain rude-edit evidence. The documented flow uses **Spar: Restart backend**, not the debugger toolbar's Restart. The [structured inspection guide](15-development-inspection.md) and [native evidence](evidence/development-inspection-2026-10-19/README.md) now establish A12 with generic DOM/click/type/screenshot/log operations, an edit/verify walkthrough, stale-target rejection, honest unfinished timeouts, and actual cleanup. These services use the existing owner channel; the Debug host requires explicit opt-in. Broader consumer layouts, an MCP adapter, and optional memory observation remain follow-ups.

### M1.7 — Verify Visual Studio and production delivery

Production progress: the [production guide](16-production-delivery.md) and [execution evidence](evidence/production-delivery-2026-10-19/README.md) cover built React assets, contract matching, bounded native resource serving, the default copied AOT application, typed UI interaction, development-control exclusion, missing-runtime diagnostics, and measured publication/payload/startup/resource observations. A14 has direct desktop evidence. The separate native/bridge test build is explicitly opted into and never confused with the production payload. [Compatibility evidence](evidence/dependency-compatibility-2026-10-19/README.md) covers A15/A16: an application-added dependency emits and executes runtime IL through the typed greeting in a copied CoreCLR SPA; strict AOT rejects the attributable call. Dependency removal returns to AOT, while syntax, missing tools, native linking and an injected framework AOT regression remain failures. External packages (#12) and no-SDK-machine qualification remain separate work.

Progress: the [Visual Studio project-launch guide](14-visual-studio-development.md) adds an ordinary project profile and Debug-only bootstrap over the shared coordinator. The [manual native evidence](evidence/project-launch-2026-10-19/README.md) covers F5/breakpoint, frontend HMR, IDE-owned backend patches, compiler recovery, unsupported/contract relaunch, and actual debugger Stop with retained-process cleanup (A13). Automated lifecycle cases separately cover native close, simulated debugger termination, contract retirement, and coordinator crash. This initial lane rebuilds and relaunches the whole session for restart-required edits. Production delivery and the other M1.7 scenarios below remain separate acceptance work.

Exercise Visual Studio F5, backend breakpoint, frontend HMR, supported backend edit, unsupported/contract change, and Stop using the same consumer project. Keep editor-specific configuration small and explain any difference from VS Code. A recorded manual Windows acceptance pass is acceptable for this lane initially; it must be real execution.

Build the frontend for production, generate matching contracts, embed/package the assets, and publish the default AOT application. Run the copied payload outside the checkout with no frontend server and no Node process. Verify that development control/inspection capabilities are absent or inaccessible.

Add one deliberately incompatible dependency fixture that actually executes an unsupported mechanism, such as runtime code generation. Strict AOT must reject it with attributable diagnostics; the documented CoreCLR compatibility selection must execute it successfully. Syntax errors, missing tools, and a deliberately introduced framework AOT regression must not trigger compatibility fallback.

Restore dependencies and build the demonstration through local package references from an external directory. This catches hidden source-project/friend-assembly dependencies. A local feed is sufficient; public package release is not required.

Deliverables: AOT and CoreCLR artifacts with mode receipts, local external consumer, Visual Studio evidence, and a short start/edit/debug/publish guide.

## Acceptance matrix

| ID | Scenario | Observable pass condition |
| --- | --- | --- |
| A01 | Fresh documented setup | Selected prerequisites plus the documented start action reach visible SPA and typed backend readiness |
| A02 | CSS/component edit | Actual WebView changes through HMR; no backend restart; supported local SPA state retained |
| A03 | Backend body edit | New greeting behavior with the same process/generation and retained backend counter |
| A04 | Data refresh after backend update | SPA receives the update signal and displays fresh data without claiming initialization was rerun |
| A05 | DTO/export change | Regenerated client/server agree; application restarts; old fingerprint is rejected |
| A06 | Startup/unsupported edit | Restart outcome is explicit; new process state and matching bindings are observed |
| A07 | Broken edit then correction | Diagnostic identifies the error; no partial activation; corrected build becomes usable |
| A08 | Rapid edits | Only the selected latest valid generation activates; stale completion cannot overwrite current status |
| A09 | In-flight operation on retirement | No stale reply/mutation replay; old work remains accounted for until actual completion or explicit forced exit |
| A10 | Wrong origin/permission/contract | Calls are rejected before application handler entry |
| A11 | VS Code debugger | F5, breakpoint, supported Hot Reload, restart/relaunch, and Stop verified in the real C# debugger lane |
| A12 | AI control | Structured commands plus actual UI snapshot/action verify an edit; session/version identity is unambiguous |
| A13 | Visual Studio | F5, breakpoint, frontend/backend update paths and Stop executed and recorded |
| A14 | NativeAOT publication | Copied app visibly renders and calls C# without Node/dev server; actual execution mode recorded |
| A15 | Dependency compatibility | Incompatible dependency rejected under strict AOT and functional in explicit CoreCLR compatibility mode |
| A16 | No unrelated fallback | Compile/tooling/framework failures remain failures rather than producing an unexpected CoreCLR artifact |
| A17 | Shutdown and failed startup | Owned work/processes and profile leases accounted for; forced and graceful outcomes distinct |
| A18 | External consumer | Local packages build/run outside the framework checkout using public APIs; no framework source imports |

A scenario is not passed when skipped, timed out, or verified only against a mock. Tests that intentionally force a failure record the expected failure separately from successful cleanup. Native/rendering checks require an interactive Windows session; CI should label an unavailable desktop lane as unavailable rather than successful.

## Evidence and measurements

Record source revision or source hashes, tool versions, command, configuration, application mode, session/generation/contract identity, expected result, observed result, and artifact/log location. Preserve first-failure evidence. Capture screenshots for visible outcomes and structured events for sequencing.

Measure cold/warm start, save-to-HMR, save-to-backend-update, contract restart, failed-edit recovery, publication time, installed payload size, and separate host/browser resource use. Use repeated runs and report the workload and sample count. This milestone establishes a baseline; select numerical targets after the mechanism works rather than inheriting the old attempt's limits.

Optional small addition under the shared inspection/status work (#7): sample backend/host and WebView2-environment process memory at a low frequency, with separately labeled private bytes and working set, process counts, and sample/session/generation identity. Attribute browser processes through WebView2 discovery, deduplicate identities, and treat unavailable samples honestly. Keep dev-tool overhead separate and measure sampler overhead before enabling it by default. This is process-level visibility, not managed/JavaScript heap profiling or exact per-page physical RAM accounting.

A copied application on a developer machine is not a clean-machine/no-.NET certification. AOT execution on a Windows machine without the .NET SDK/runtime installed should be attempted when available, and its absence must be carried into the release-readiness backlog. Missing WebView2 needs an actionable startup result, not an implicit claim that the browser is bundled.

## CI and branch protection follow-up

Upstream `sotsera/spar` has an active `protect-main` ruleset requiring PRs and the **Windows managed probe** check from GitHub Actions (integration ID `15368`), with up-to-date branches and no bypass entries. Existing signature, linear-history, conversation-resolution, and force-push/deletion protections remain in place. See the [CI gate proof](08-ci-merge-gate.md).

The first workflow defines **Windows managed probe** for PRs and development-branch pushes. Initial PR #14 had no upstream check suite before the workflow landed; fork CI supplied the bootstrap evidence. After merging, upstream `main` CI passed. Validation PR #15 then produced a real upstream build failure; once the rule was enabled, the non-draft PR reported the failed check as required and merging as blocked. The source was restored and upstream recovery run `39147429145` passed build, three real watcher tests (none skipped), and evidence upload. Each later PR head must pass its own required check.

Current trigger policy (2026-10-19): run the same required job for pull requests and pushes to `main`. Feature-branch push runs are disabled to avoid duplicating the upstream PR run in the development fork; pre-PR pushes therefore do not start CI. The check name and branch-protection requirement remain unchanged. The `main` run validates the actual integrated commit after squash merge.

New PR runs cancel superseded runs for the same PR. Each `main` push has its own concurrency group so another push does not replace its pending or running validation. Actions are pinned to full commit SHAs with readable version comments, and checkout does not persist credentials. Update the SHA and version comment together after verifying the upstream release. Full test coverage remains enabled for both events.

[Fork run 39143727813](https://github.com/ghidello/spar/actions/runs/39143727813) passed the build, three real watcher tests (none skipped), and evidence upload on implementation commit `0be3b41`. Later upstream evidence and ruleset details are in the CI gate proof; a fork run alone does not establish upstream enforcement.

When introducing CI during milestone 1, treat enforcement as part of delivering the workflow:

- [x] Establish stable, unique check names and verify the workflows run on pull requests from the development fork.
- [x] After observing real successful and failing runs, add the applicable build/test check names to upstream `protect-main` as required status checks. Enforced name: **Windows managed probe**, GitHub Actions integration `15368`, strict up-to-date requirement.
- [x] Extend the existing required **Windows managed probe** job with native CoreCLR lifecycle, strict AOT publication, and copied AOT lifecycle execution. [Upstream run 39153778439](https://github.com/sotsera/spar/actions/runs/39153778439) passed all of these on the native-host implementation; see the [native guide](09-native-host.md). This does not claim CI desktop rendering.
- [ ] Add external-package consumer coverage when available. Revisit the ruleset whenever required jobs are renamed or replaced; the existing required check name remains stable.
- [ ] Keep interactive Windows rendering/debugger checks explicit about desktop availability. An unavailable or skipped acceptance scenario does not establish milestone completion; report its remaining evidence requirement separately from ordinary CI results.
- [x] Verify the resulting policy blocks merging a PR with a failed required check without introducing a bypass. PR #15 was non-draft with `isRequired: true`, `conclusion: FAILURE`, and `mergeStateStatus: BLOCKED`; PR-only and squash-only policies remain intact.

This follow-up initially belongs with the tooling/build foundation (upstream issue #2), then the issues introducing production AOT and external consumer verification (#10 and #12). Repository settings should be updated deliberately when those checks exist.

## Completion and follow-up

Milestone 1 is complete when A01-A18 have evidence on the selected baseline, the public sample follows its own guide, and known limits are documented. A remaining required scenario keeps the milestone open. No performance, installer, cross-platform, or general production-readiness claim follows automatically.

The next milestone can address consumer polish, supported-machine coverage, workload budgets, file pickers/desktop services, and packaging. Add those only after the development loop is demonstrated. The milestone remains open for its remaining required acceptance evidence.
