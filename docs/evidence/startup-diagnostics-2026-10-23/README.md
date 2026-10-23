# Production startup diagnostics and fixture identity — 2026-10-23

Baseline: merged PR #53, `e8cfc7d66c7e647689705917cf23a3e3b488ea6f`.
Implementation branch: `codex/coreclr-startup-timeouts`; the tested driver/helper bytes are in
[implementation.sha256](implementation.sha256). Windows 10.0.26220, SDK 10.0.402,
published runtime 10.0.13, PowerShell 7.6.5, WebView2 155.0.4283.62.
No security settings, exclusions, application startup behavior, deadlines or retry policy changed.
Raw traces remain private under ignored local artifacts.

## Reproduced delay and attribution

The two original compatibility failures reached the unchanged 30-second greeting deadline:
one emitted no application events; the other created its window at 27.43 seconds and loaded
its document at 27.99 seconds. Repeating the first failed payload passed at 1.316 seconds.
The unchanged complete compatibility driver then passed all 13 cases and copied CoreCLR/AOT
execution; its fresh CoreCLR publication greeted at 12.340 seconds. These results establish
intermittency, not a successful original run or its exact cause.

A diagnostic-only fresh fixture reproduced the timeout before the first instruction of `Main`.
Its partial EventPipe trace recorded a 16,007 ms Windows assembly load and a subsequent Bridge
load still unfinished after at least 7,143 ms. Forced termination left a truncated trace; it is
not complete evidence. Separately observed cleanup found no root or matching copy/profile process.
A native sample caught `NtCreateSection` under DLL loading; the presence of a Citrix hook did
not prove causation. A read-only executable-image mapping probe independently reproduced
1,542/10,866 ms first mappings versus 5.52/5.45 ms repeat mappings of Windows/Bridge DLLs.

A complete approved 45-second Defender recording of another diagnostic fixture captured zero
lost events. CoreCLR greeted at 28.313 seconds, entering `Main` at 19.074 seconds; asset reading
took 83 ms. Matching Defender trust checks overlapped 99.32–99.95% of four CLR loader intervals,
including 8,571 ms contracts and 8,507 ms dynamic-dependency loads. See
[loader/scan correlation](loader-scan-correlation.json), which preserves UTC intervals and
clipped overlap unions. This attributes waits in that reproduction; the two original untraced
failures cannot be attributed retroactively. Matching a ten-second duration does not prove a
cloud lookup or a particular Defender policy.

First failures remain in ignored artifacts: normal-token recording failed with OS access denied;
the first elevated orchestration queried WPR from an unelevated token and missed recorder readiness,
so its application capture was partial. The corrected workflow waits for the recorder's actual
successful-start message. Neither failure changed security settings or test expectations.

## Independent builds and repeated scans

Six separate source copies restored, compiled and published independently. The experimental
mapping normalized the unique fixture root to `/_/spar-compatibility/`; all measured builds were
Release. The delivered helper applies it only to private Release fixtures, preserves explicit
overrides and keeps Debug physical paths. Sources were copied separately and no compiled output
was reused. NuGet used its ordinary existing caches; this was not a cold-restore comparison.

| Build identity | Process-to-greeting observations | Unique hashes per application DLL |
| --- | --- | --- |
| Ordinary unique physical paths | 4.146, 8.937, 4.556 seconds | 3 of 3 |
| Mapped fixture paths | 15.408, 1.200, 1.377 seconds | 1 of 3 |

All six passed without forced cleanup. The five assemblies were Windows, Bridge, contracts,
dynamic dependency and application. [Determinism observations](determinism.json) retain hashes,
CodeView paths and run order. The first mapped observation was slower: there is no cold-start
guarantee, distribution claim or CI speed threshold.

Two further fresh publications tested the final Release-only helper under one approved read-only
45-second Defender recording. The mapped publication reproduced all five prior mapped DLL hashes;
the ordinary publication differed in all five. Both applications ran with normal permissions.

| Observation | Mapped repeat identity | Ordinary new identity |
| --- | ---: | ---: |
| Process to typed greeting | 1,282.54 ms | 13,689.35 ms |
| Largest application trust check | 56.12 ms | 9,625.24 ms |
| Windows / Bridge trust checks | 1.60 / 1.53 ms | 566.29 / 1,007.06 ms |
| Contracts / dynamic-dependency trust checks | 1.40 / 1.31 ms | 605.83 / 597.92 ms |

[Scan comparison](scan-comparison.json) retains exact hashes, process creation identities,
matched file intervals and trace coverage: 12:41:15.537–12:42:01.205 UTC, zero lost events.
The expanded report returned 6,109 scans below its 10,000 limit, down to 0.0357 ms.
Both startup intervals fit in the recording. Nested trust checks are not summed as startup time.
No EventPipe instrumentation was used in this comparison. This supports reducing repeated expensive
checks on this machine; it does not disable scans, promise security-cache retention, or establish
the same behavior for every antivirus or CI runner. Other machine activity was present.

## Diagnostic contract and regression detection

The driver retains phase, sanitized event bounds, bounded CPU/working-set observations and actual
root/browser teardown observations in a sidecar, including on timeout. No-event, window-created,
document-loaded and typed-greeting completion remain distinct. Process observations alone leave
antivirus attribution unknown. Rendering is claimed only after actual capture.

An isolated diagnostic fixture deliberately held before any event and after an actual window event
but before document creation. Both failed at the unchanged deadline. Each final receipt retained
the correct phase, at least 20 CPU observations, forced cleanup with observed root exit, no claimed
render, and no invented antivirus cause. The frozen original driver failed the same receipt
assertion: “Missing timeout phase and CPU observations.” See [timeout detection](timeout-detection.json).
A normal warm qualification passed before injection and its capture was visually reviewed.
These diagnostic-only holds do not exist in the shipping application.

The focused build-path check compiles two ordinary projects independently, compares assembly bytes
and CodeView paths, changes and executes one implementation, and evaluates Debug/explicit overrides.
A buildable no-op mapping variant failed the independent-assembly identity assertion. An initial
test-source setup omitted a namespace and correctly failed CA1050; the corrected source preserves
the analyzer policy. The test does not accept build failure as defect detection.

## Final local validation

- `pwsh -NoProfile -File eng/Test-Compatibility.ps1 -EvidenceDirectory artifacts/startup-diagnostics/compatibility-final -VerifyRender`:
  all 13 expected-success/failure build and publication cases passed, as did copied CoreCLR and
  NativeAOT execution, production boundaries, typed greetings, actual root/retained-browser exit
  and exclusive profile-lease acquisition. Neither positive lane needed forced cleanup.
  [Compact receipt](compatibility.json); reviewed [CoreCLR](coreclr-run.png) and [AOT](aot-run.png) captures.
- `Test-ProductionDiagnostics.ps1`, `Test-CompatibilityBuildPaths.ps1` and
  `Test-IntegrationHelpers.ps1` passed after the final edits. [Build-path receipt](build-paths.json).
  The two new checks are explicit entries in Processes; the fixed catalog now contains 58 checks.
- The two held-startup tests and frozen-original comparison above establish executed detection;
  [before-event](timeout-before-event.json) and [after-window](timeout-after-window.json) sidecars
  retain the bounded observations. The private fault was an infinite sleep at managed entry or
  immediately after the real window event, respectively. The mapping fault replaced the helper
  with a definition-only no-op; both independent projects still compiled before the equality
  assertion failed. These faults remain outside the repository's delivered code.
- Documentation link validation, six skills/provenance with 14 case definitions, all six gate
  tests and `git diff --check` passed. A link check first failed while this new evidence document
  was not yet written; it passed after the document was completed.
- A separate [cleanup observation](cleanup.json) found zero matching executable/profile processes
  for all eight measured or deliberately failed copy paths. This supplements, rather than replaces,
  retained-handle exit evidence. The bounded recorder completed and saved its trace/report.

Hosted full-suite results are separate and reported by the implementation PR's current checks.
Actual editor debugging and a machine without the SDK are not part of this change's acceptance.
The [current guide](../../16-production-delivery.md#startup-delays-and-antivirus-attribution)
describes the vendor-independent observations and vendor-specific attribution workflow.
