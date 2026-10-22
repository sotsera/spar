# CI timeout diagnosis and install-network repair

Source: PR #45 `a4c8d1ed40d92175654d701785743bae23f01de3`, a merge of the original D1 head and upstream
PR #44. [Run 39737327607](https://github.com/sotsera/spar/actions/runs/39737327607) timed out; first-failure
logs and both original artifacts remain locally under `artifacts/ci-39737327607`.
The [review](../../27-ci-reliability-review.md) distinguishes confirmed measurements from inference.

Retained [receipt.json](receipt.json) contains original job/case durations and the controlled npm
experiment. [npm-delay-excerpts.txt](npm-delay-excerpts.txt) preserves the timeout annotation and the
two original slow installs reaching their expected native errors. The full original diagnostic artifact
is not replaced by this compact excerpt. No MSBuild binary log existed in that artifact; this npm-wait
diagnosis uses its surviving console logs and case receipt.

The workflow repair changes only hosted npm configuration and log retention. It keeps the 30-minute
limit, all checks, normal restore/build failure behavior, exclusive consumer caches and native ownership.
Dependency installs still use their lock files and production builds still rebuild the frontend.

Controlled verification: the retained [npm network experiment](npm-network-probe.mjs), invoked with
`node docs/evidence/ci-timeout-2026-10-22/npm-network-probe.mjs <installed-npm-cli.js>`, used Node's
built-in HTTP/process/file APIs and the installed npm, with a local tarball and a 127.0.0.1 HTTP server.
Each fixture had a fresh package lock and actual `npm ci --ignore-scripts --include=optional`. A short
one-second request timeout and zero retries applied only to this experiment. Audit enabled waited on
an unanswered advisory POST and returned 0; audit disabled sent no advisory request and returned 0;
a deliberately unavailable tarball still returned 1. No unpinned test package or external registry
was needed by this three-case experiment.

Local integration command uses the job environment (`NPM_CONFIG_AUDIT=false`, `NPM_CONFIG_TIMING=true`,
`NPM_CONFIG_LOGS_MAX=200`, absolute `NPM_CONFIG_LOGS_DIR`) with:

```powershell
pwsh -NoProfile -File eng/Test-Compatibility.ps1 -EvidenceDirectory artifacts/ci-39737327607/compatibility-repair
```

The driver includes real development/strict diagnostics, dependency-driven copied CoreCLR and copied
NativeAOT execution, boundary checks, rejected configuration/syntax/framework regressions, and real
missing-compiler/native-link failures. Native rendering was not requested by this CI-only repair.
The first local attempt passed its three build/publication cases, then failed copied CoreCLR greeting
readiness at the production driver's 30s deadline. Its [public trace](local-first-timeout.jsonl) records
window creation at 26.7s and bridge completion at about 30.1s. Forced cleanup is recorded as such; this
attempt is not a successful suite. The exact existing publish payload passed a separate fresh-copy
`Test-Production.ps1 -ExpectedMode CoreCLR -Samples 1` run without edits. Both receipts remain in
`receipt.json`; the startup-delay cause remains unresolved and no timeout was increased.

The diagnostic reproduction and any later full-suite/hosted result are distinct executions; an
unchanged rerun never replaces the initial failure. Hosted status belongs to PR #45's exact head.

The subsequent fresh full compatibility fixture passed all 13 cases with the repaired npm environment,
including both actual native failure injections and copied CoreCLR/AOT runs. Repeated local install
timings and both attempts are retained in `receipt.json`. This establishes local compatibility checks;
it does not resolve the first cold-start timeout or prove that npm's original hosted delay used the
advisory endpoint. All 34 selector/gate tests, documentation links, six skills/provenance with 14
structural cases and diff checks passed. The hosted full workflow must still qualify the repair head.
