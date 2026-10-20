# Milestone 1 acceptance

Date: 2026-10-20. A01–A18 have execution evidence on the selected Windows development baseline. The final missing package-consumer editor walkthrough now has an [actual Visual Studio pass](evidence/milestone-acceptance-2026-10-20/README.md). The implementation baseline is merged PR #26, `d13238f70d59168891630e8ec0081f5f431aea42`; this follow-up records acceptance without changing runtime or tooling behavior. Issue closure follows integration of this evidence.

The matrix combines explicitly recorded runs from the fresh implementation. It does not imply every suite was repeated in one command or that ordinary CI verified desktop pixels and debugger gestures. Each evidence record identifies its tested revision, failures and limits. PR #26's [required Windows managed probe](https://github.com/sotsera/spar/actions/runs/39377913914) passed after the partial-log-reader correction.

| ID | Evidence and observed pass |
| --- | --- |
| A01 | Fresh external feed/cache, direct application restore/build, both consumer projects loaded, actual F5/breakpoint/Continue and visible typed readiness in the [external Visual Studio walkthrough](evidence/milestone-acceptance-2026-10-20/README.md). The [package record](evidence/package-consumer-2026-10-20/README.md) independently covers terminal and external VS Code setup. |
| A02 | [Owned SPA session](evidence/development-session-2026-10-19/README.md): actual CSS/component HMR with unchanged backend/document/mount and retained state. Repeated through external packages and both editor lanes. |
| A03 | [Backend updates](evidence/backend-updates-2026-10-19/README.md): supported body edit changes behavior with the same process and counter. External Visual Studio now independently records revisions 1 and 2 with retained state. |
| A04 | The same backend/native events and reviewed UI show metadata-update-driven data refresh; no initialization rerun is claimed. |
| A05 | [Backend/contract proof](evidence/backend-updates-2026-10-19/README.md) and [external editor evidence](evidence/package-consumer-2026-10-20/README.md#external-vs-code-debugger): regenerated agreement, changed process/fingerprint, old fingerprint rejected. External Visual Studio adds retirement, CS7036, corrected rebuild and a typed mutation. |
| A06 | [Restart policy](evidence/restart-policy-2026-10-19/README.md) and both actual debugger lanes: explicit unsupported/startup restart and new process state. Visual Studio uses native close, cleanup, rebuild and F5. |
| A07 | Watcher and IDE diagnostics reject broken source; correction restores valid behavior. [External Visual Studio captures](evidence/milestone-acceptance-2026-10-20/README.md) additionally show the retained native behavior during the invalid edit. |
| A08 | [Restart policy](evidence/restart-policy-2026-10-19/README.md): overlapping source/build changes select only the latest valid generation; stale completion cannot activate. |
| A09 | [Backend retirement](evidence/backend-updates-2026-10-19/README.md) and [typed bridge](10-typed-bridge.md#verification): cancellation-ignoring work remains owned until actual completion, stale replies are suppressed, mutations are not replayed, and explicit force is separate. |
| A10 | Native CoreCLR/copied-AOT bridge probes reject foreign navigation, permissions, stale contract/document and malformed calls; denied handler count is zero and mutation count remains exactly two. Real bridge unit tests additionally reject an incorrect native source before handler entry. See the exact coverage below. |
| A11 | [External VS Code debugger](evidence/package-consumer-2026-10-20/README.md#external-vs-code-debugger): real breakpoint, HMR/Hot Reload, diagnostic recovery, coordinated unsupported/contract restart, native close and debugger Stop; 45 retained processes exited. |
| A12 | [Structured inspection](evidence/development-inspection-2026-10-19/README.md): actual WebView snapshots/actions/captures verify edits; stale session/generation/document/contract and replayed mutations are rejected; unfinished timed-out work retains ownership. |
| A13 | [Source-consumer IDE acceptance](evidence/project-launch-2026-10-19/README.md) and the new [external Visual Studio walkthrough](evidence/milestone-acceptance-2026-10-20/README.md): actual debugger and update/relaunch/Stop actions with reviewed native captures and retained-handle cleanup. |
| A14 | [Production SPA delivery](evidence/production-delivery-2026-10-19/README.md) and [external package publication](evidence/package-consumer-2026-10-20/README.md): copied default NativeAOT app visibly renders and calls C# without a frontend server; actual mode and payload recorded. |
| A15 | [Dependency compatibility](evidence/dependency-compatibility-2026-10-19/README.md): attributable IL3050 rejection and actual generated-IL execution in explicit CoreCLR. Repeated through public package-consumer configuration. |
| A16 | The compatibility record retains syntax, framework AOT, compiler and linker failures with no success receipt or silent fallback; strict override and dependency-removal AOT reevaluation pass. |
| A17 | Native lifecycle/bridge, owned SPA, watcher and IDE records account for unfinished work, actual process exit and profile leases, distinguishing graceful and forced outcomes. See the cleanup reconciliation below. |
| A18 | [External package record](evidence/package-consumer-2026-10-20/README.md): five local packages, isolated restore/build/generation, public APIs, no framework source imports, actual development and copied AOT/CoreCLR delivery. Both editor walkthroughs are now executed. |

## A10: authority before handler entry

The latest [CoreCLR bridge receipt](evidence/package-consumer-2026-10-20/ci-framing/CoreCLR-Bridge.json) and [copied NativeAOT receipt](evidence/package-consumer-2026-10-20/ci-framing/NativeAOT-Bridge.json) each pass inline-document, packaged-resource and close-with-work scenarios without forced cleanup. These are actual WebView runs, not mocked rendering.

`samples/Spar.NativeSample/Probe.js` attempts navigation to `https://untrusted.invalid/`; native policy rejects it and retires the old document before a newly trusted document is admitted. It sends denied-permission and stale-contract/document calls through the generated/native transport. `eng/Test-Bridge.ps1` requires the corresponding rejection events, zero `denied-entered` events, exactly two successful counter entries and three trusted document loads.

`BridgeTests.RejectionPreventsHandlerEntry` separately supplies a wrong native source to the real bridge and generated dispatcher, and asserts zero application handler entries. The host reads that source from WebView2's native event, never from renderer-supplied JSON. The evidence therefore combines native foreign-navigation denial with direct bridge source-admission coverage; it does not claim an untrusted page was loaded and allowed to send privileged calls. The development-session credential-denial case adds actual `client_unauthorized` rejection.

## A17: actual lifetime and cleanup

The latest [CoreCLR host receipt](evidence/package-consumer-2026-10-20/ci-framing/CoreCLR-NativeHost.json) and [NativeAOT host receipt](evidence/package-consumer-2026-10-20/ci-framing/NativeAOT-NativeHost.json) cover normal close, close during startup, worker dispatch, document-callback failure, missing runtime, redirected profile and invalid profile. The normal case also rejects concurrent profile ownership. They retain attributed browser exits, profile preservation and exclusive lease reopening. Unsupported platform override behavior is recorded separately where applicable.

The bridge close-with-work case requires the uncancellable handler to finish after close is requested and before the host closes, then reopens its profile lease. [Ten owned-session scenarios](evidence/development-session-2026-10-19/README.md) cover normal stop, owner loss/crash, frontend/application exit, occupied port, startup timeout, missing application and denied credentials; retained process identities exit and an unrelated process survives. Later [inspection regressions](evidence/development-inspection-2026-10-19/README.md#regressions-and-production-boundary) repeat that suite and project-launch lifecycle cases.

Watcher retirement distinguishes drained work from explicit forced termination. The new external Visual Studio pass independently observes 39 retained handles exiting across normal close, contract retirement and real debugger Stop, then opens all three profile leases exclusively. Short-lived processes absent before observation are explicitly excluded from retained-handle counts. Neither cancellation, timeout, a UI click nor a final log line alone is treated as proof of cleanup.

## Release-readiness follow-up

Milestone acceptance is the demonstrated development loop on the selected Windows baseline. The following work remains:

- Execute copied AOT on a clean Windows machine without the .NET SDK/runtime. No such machine was available; developer-machine execution is not that certification.
- The sample and fresh consumer now generate and carry their [frontend distribution notices](16-production-delivery.md). Complete the broader package/release review before distributing a public product; local `0.1.0` packages are not a public NuGet release.
- Expand supported-machine, Visual Studio/VS Code version and WebView2 coverage. The current IDE versions and absolute launch-profile paths are recorded, not universal compatibility claims.
- Define installation/signing/update delivery, repeatable performance budgets and broader consumer ergonomics as subsequent scope. Current payload/memory observations are single-fixture measurements.

These are explicit follow-ups rather than skipped required A01–A18 scenarios. No installer, cross-platform, general performance or production-readiness claim follows from this milestone.
