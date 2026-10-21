# Minimal application separation — 2026-10-21

Local Windows validation of issue #32, based on merged `75885b2b22bed832e24cbdb665644fe370491989`.
The implementation was validated before its commit: use the normalized source hashes in
[validation.json](validation.json), not the baseline `sourceRevision` fields in individual
receipts, to identify this change. Raw logs, copied payloads, isolated sources and captures
remain in ignored `artifacts/issue32`, the named project/editor directories, and fresh
external temporary consumers. The compact record retains receipt and reviewed-capture hashes.

## Observed results

- Solution build: zero warnings/errors. Managed tests: 65 passed, none failed or skipped.
  CI selector/gate: 34 passed. Helper import/ownership checks, generated TypeScript client,
  both frontend checks/builds and complete frontend license notices passed.
- Migration audit: all 22 original application files preserved byte-for-byte in `fixtures/`;
  all 141 existing assertion/wait call sites retained across the six migrated drivers/helpers.
- Relocated acceptance suites: 10 SPA, 3 backend update/race/retirement, 3 inspection and
  4 project-launch scenarios passed. Source fixture VS Code debugger acceptance passed,
  with all 45 retained processes exited. Native images were reviewed.
- Ordinary source application: typed interaction, frontend/backend updates with retained
  state, coordinated restart with reset state, and cleanup passed through public inspection.
  No probe command or fixture report was used. Source VS Code breakpoints, patching,
  compiler error/recovery, coordinated relaunch and Stop passed; all 22 retained processes exited.
- Readiness regression: an actually entered failing handler and a forged frame signal did
  not declare readiness. A successful call/frame did; reload established a new native
  document and rejected the previous target's mutation. All 18 retained processes exited.
- Relocated native and bridge suites passed against CoreCLR and a copied explicit AOT probe.
  Normal production boundaries ran before the separate probe build. Ordinary and fixture
  copied AOT publications passed; reviewed images show the typed greeting.
- Five local packages built. A fresh default minimal consumer used only its own `.packages`
  folder, built/developed/published through public APIs, passed Release exclusion, and ran
  as a copied AOT application. The explicit integration-fixture consumer retained its existing
  updates/restart, copied AOT/CoreCLR and intentional strict-AOT rejection coverage.
- Fresh package debugger lanes passed separately: minimal app retained/retired 22 processes;
  full fixture retained/retired 45. Both had reviewed native captures.
- The relocated dependency-compatibility driver passed its complete set of positive and
  expected-negative cases, including attributable strict-AOT rejection, dependency removal,
  syntax/tool/link failures and framework AOT regressions. Both copied modes were reviewed.

## First failures and limits

The first minimal source run timed out because the coordinator required the old application
`Report` export. The development-only native readiness path removed that dependency; final
ordinary-app and negative-boundary runs passed. This signal establishes a successful typed
result and frame progress, while visual claims still depend on image review.

The new publication driver initially could not find a hidden window, then rejected its
owned console host. Explicit retained-owner window enumeration and console/browser tracking
corrected those driver assumptions. Both failed attempts remain recorded. A fresh package
cache assertion exposed Visual Studio's inherited fallback cache; the scaffold now clears
fallback folders and a new consumer passed isolation.

Final package project formatting was checked as XML-equivalent to the validated project.
No runtime cleanup-race fix is claimed. Visual Studio walkthroughs were not rerun for the
minimal app, and historical fixture acceptance is not promoted to new evidence. Clean-machine
qualification remains deferred. Hosted PR CI was pending when this local record was written.

Commands and the scenario migration map are in the [minimal application guide](../../23-minimal-application.md).
