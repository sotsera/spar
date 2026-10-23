# Fixed test suites and continuous integration

Every PR, upstream (`sotsera/spar`) `main` push or version tag, and manual dispatch runs the same five
automatic suites. Fork `main` and tag pushes skip both jobs before allocating runners; GitHub may
still display a skipped workflow entry. Manual dispatch remains available in forks. PRs from forks
continue to run in the target repository. CI does not inspect changed paths or infer dependencies.
The required **Windows managed probe**
check remains the merge gate; it now audits independent Windows jobs.

## Local entry points and execution requirements

Run from the repository root with the [current prerequisites](23-minimal-application.md#prerequisites):

```powershell
# Default: build the relevant managed tests, then run fast managed/frontend/tooling checks.
pwsh -NoProfile -File eng/Test.ps1
# Inspect membership without building or starting a scenario.
pwsh -NoProfile -File eng/Test.ps1 -Suite all -List
# Each suite owns its preparation and runs its scenarios sequentially.
pwsh -NoProfile -File eng/Test.ps1 -Suite processes
pwsh -NoProfile -File eng/Test.ps1 -Suite development
pwsh -NoProfile -File eng/Test.ps1 -Suite delivery
pwsh -NoProfile -File eng/Test.ps1 -Suite packages
# Run all automatic suites sequentially in one checkout.
pwsh -NoProfile -File eng/Test.ps1 -Suite all
```

[eng/test-suites.json](../eng/test-suites.json) is the executable inventory: fixed commands, arguments
and check identities, shared by the local entry point and hosted jobs. Build, install and publish
failures propagate immediately; later checks remain `not-run`, and the suite fails. Each invocation
uses a fresh evidence directory and writes `artifacts/ci/suite-<name>.json` with source/run identity,
catalog hash, per-check outcomes and timings. No test framework migration is implied for native or
editor drivers. Their assertions and process ownership remain in their existing scripts.

| Suite | Requirements and retained coverage |
| --- | --- |
| Fast | .NET/Node on Windows; bridge/generator, configuration, assets, contract and bounded-frame tests; generated TypeScript, fixture SPA compilation/notices, documentation, skills and gate checks. Builds the two relevant managed test projects, not the whole solution. |
| Processes | Source contract-generation and frontend-restore incrementality with real compilation/clean; runtime-pack guards and cold transitive-framework publication; hidden native-window selection; actual watchers and Windows jobs; managed process ownership/retirement, the three precursor watcher cases, helper import/teardown, suspended-child image identity and actual CLI startup-error cases. No editor or WebView session. |
| Development | Actual native WebView sessions; ordinary application HMR/state, readiness rejection/renewal, fixture SPA/owner lifetime, backend patches and rebuild races, project-launch lifetime and bounded inspection. |
| Delivery | Actual CoreCLR native bridge/lifecycle; normal Release assets/boundary/copied AOT; ordinary sample publication; attributable compatibility and fatal unrelated failures; separate copied AOT probe bridge/lifecycle. Normal production boundary runs before the explicit probe publication. |
| Packages | One newly packed feed, then fresh consumers outside the checkout with isolated NuGet caches; contract-generation/frontend-restore incrementality, runtime-pack guards, public package references, packaged tools/contracts, native updates/restart and copied AOT/CoreCLR delivery. |

Integration is a coverage level, not a synonym for slow or manual. Source/package and CoreCLR/AOT are
distinct variants. Automatic checks include native integration; actual debugger gestures, Visual Studio
walkthroughs and reviewed pixels remain separate [acceptance responsibilities](19-milestone-1-acceptance.md).
Use the existing [VS Code](13-vscode-development.md), [Visual Studio](14-visual-studio-development.md)
and [integration helper](22-integration-test-helpers.md) procedures for explicit desktop runs. Hosted
project-launch tests do not establish debugger acceptance, and captures alone do not establish reviewed pixels.

Processes also checks production-observation identity, privacy, bounded CPU samples and actual
process exit, plus independently compiled Release fixture identity, changed implementation, Debug
paths and explicit overrides. These are fixed catalog entries, not runtime test classification.

## xUnit selection

All test projects remain in `Spar.slnx`. Shared [test properties](../tests/Directory.Build.props) preserve
the root properties and supply native MTP configuration. Ordinary `dotnet test` excludes
`LongRunning=true`; expensive process tests use method/class traits, and the wholly expensive watcher
project uses `LongRunningTestProject=true` to generate the assembly trait.

```powershell
dotnet build Spar.slnx --disable-build-servers
# Ordinary managed tests; this does not run the PowerShell/Node integration suites.
dotnet test --solution Spar.slnx --no-build --minimum-expected-tests 1
# All managed tests, including real process/watcher tests.
dotnet test --solution Spar.slnx --no-build -p:IncludeLongRunningTests=true --minimum-expected-tests 1
# Only long-running managed tests; empty ordinary modules are intentional.
dotnet test --solution Spar.slnx --no-build -p:IncludeLongRunningTests=true --filter-trait LongRunning=true --ignore-exit-code 8 --minimum-expected-tests 1
```

The checked-in baseline initially partitions 74 cases into 67 ordinary and seven process cases.
The catalog names each managed module explicitly, enables TRX output, treats skips as failures and
requires at least one selected test per invocation. Counts may grow; the initial numbers are evidence,
not permanently fixed test-count thresholds.

`IncludeLongRunningTests=true` includes both kinds; it does not mean long-only. The default
exit-code-8 exception applies only to a wholly long-running project deliberately excluded by policy.
An explicitly enabled module must fail if empty. Long-only solution execution requires the explicit
exception plus a global minimum before any `--` separator. A minimum of one does not verify every
intended module; hosted suite membership and individual module invocations provide that additional guard.

Filtering does not avoid project/dependency builds. The fast suite limits its builds explicitly.
Filter arguments are reevaluated with `--no-build`; trait changes need a rebuild. Shared
`testconfig.json` controls the default result directory; the catalog explicitly enables reports and
assigns distinct filenames in its fresh evidence directory. Direct executables, `dotnet run`,
`--test-modules` and IDE runners do not automatically inherit the project-based selection policy.
Use explicit equivalent filters and verify discovery/execution before claiming parity.

## Hosted jobs and required gate

The matrix runs all five suites on separate Windows runners with `fail-fast: false`. Native scenarios
within a job stay sequential. Do not launch suites concurrently in one checkout, rebuild tools while
they own processes, or share mutable build outputs between jobs. PR updates cancel superseded runs;
independent main pushes retain independent validation.

The Node setup step caches npm's download cache using the three committed lockfiles. It does not
cache `node_modules`, generated contracts, frontend `dist`, NuGet consumer caches or native outputs.
Fresh-consumer and clean-install checks still execute real installers. Restored cache contents are
installation inputs, not acceptance results; the fixed suites and fatal dependency failures remain.

For enabled runs, the aggregate always checks the matrix result and exactly one report per expected suite,
even after suite failure or cancellation. Fork push runs skip the aggregate as well as the matrix.
Every catalog check must have succeeded in order. Failed/cancelled/skipped jobs, missing/malformed
reports, mismatched source/run/catalog or unexecuted checks fail the gate. No `continue-on-error`,
changed-path classifier or fallback coverage mode exists. The branch-protection name and GitHub
Actions identity are unchanged.

Rerunning failed jobs can reuse a successful suite from an earlier attempt of the same run, source and
catalog. Its original attempt stays recorded. Current status/results artifacts are replaced on a rerun;
failure diagnostics include the attempt number and preserve the first failure separately.

This trades additional provisioning/build work and full execution on documentation PRs for predictable
coverage and shorter independent lifetimes. Compare critical-path time and total runner minutes across
several runs before claiming a performance improvement. The prior
[timeout review](27-ci-reliability-review.md) explains the measured motivation; its selection-preservation
proposal is superseded by this fixed-suite policy.

Use manual dispatch to qualify an exact ref:

```powershell
gh workflow run managed-probe.yml --repo sotsera/spar --ref <ref>
```

Before distribution, verify a full successful run for the exact release SHA and perform relevant desktop
acceptance. Tags qualify; there is no automatic release publisher.

## Repetition and precursor tests

The classifier and its path-mapping tests are retired; focused aggregate/entry-point failure tests
replace the gate coverage. Native scripts are retained with their current assertions. The copied probe
step is now an ordinary executable driver rather than workflow-only PowerShell.

The early watcher and VS Code probes are toolchain evidence, not substitutes for Spar's actual native
coordinator/debugger tests. The watcher cases still run in Processes; explicit editor probes remain
available. Their metadata callback and unsupported-edit observations need an assertion map before
retirement. Task Stop/window-close tests likewise cover a distinct editor owner lifetime.

Keep source/package, ordinary/fixture and CoreCLR/AOT boundaries. Production sampling currently
retains fresh/reused profiles and the additional measurement sample. Any later reduction must preserve
those distinct behaviors and separate extra performance sampling deliberately. Reusing build/download
work must not bypass real rebuild races, clean consumer restores or production's deliberate frontend
rebuild. No broad scenario deletion or speculative build cache is included here.

The [execution record](evidence/test-suites-2026-10-22/README.md) retains local selection/ownership
results, the first startup observation failure and the actual required-gate failure proof. Final
recovery belongs to the implementation PR's exact current run; an earlier passing suite is not a
pass for a later revision.

## Results and artifacts

Hosted installs retain `NPM_CONFIG_AUDIT=false` and npm timing logs from the focused network repair.
Dependency installation/compilation failures remain fatal. This is not a vulnerability gate.

Each suite uploads its status separately for the aggregate, plus concise receipts, publish/package
manifests, managed TRX reports and npm timings. Failure diagnostics retain logs, source hashes,
inspection evidence and the original failed status. Success retention is 14 days; failures/cancellation
use 30 days. No environment dumps, resolved launch credentials, browser profiles or dependency caches
are uploaded. Routine local outputs remain ignored. The aggregate publishes its own report and summary.

## Evidence retention policy

This section owns evidence retention. Topic guides specify what must be executed; the policy does not
weaken their acceptance requirements or change the workflow's retention settings.

| Evidence | Where and how long | What to preserve |
| --- | --- | --- |
| Routine successful run | CI summary and compact results: 14 days; detailed runner logs follow repository retention | Exact source/event/range, selected checks, outcomes and compact receipts. Do not commit every success log or screenshot. |
| Failure, cancellation or unavailable check | CI diagnostics/results: 30 days; local ignored artifacts retained for investigation | First failure, diagnostics and the inputs needed to reproduce; distinguish expected negative tests from failures and skipped checks. Link the resolution/rerun without replacing the original outcome. |
| Durable acceptance or decision | Concise dated record in Git under `docs/evidence/`, retained with repository history | Source revision or hashes, tested tooling/configuration, commands, expected/observed result, actual versus unavailable checks, first-failure explanation, and links to exact run/attempt and supporting files. |
| Native/editor claims | Minimal reviewed captures/receipts needed to support the durable claim | Identify the review and source binding; a CI lifecycle result or DOM snapshot alone does not establish reviewed pixels or real debugger gestures. |
| Reproducibility and provenance | Relevant sources, lock files, upstream revision/path/license notices and durable evidence | Keep enough to reproduce the scenario and understand its limits; do not turn a past tested version into an unsupported current compatibility claim. |

Before an expiring artifact becomes the sole support for a durable claim, preserve the minimal relevant
receipt, reviewed capture or diagnostic excerpt in Git with source/run identity and a content hash where useful.
A checksum and expired URL identify missing bytes; they do not preserve evidence. If those bytes are already
unavailable, mark the supporting artifact unavailable and narrow the claim or execute a fresh check. Never
reconstruct a pass from memory. Keep large routine outputs out of Git; any later evidence migration or pruning
needs an explicit scope and link-preservation plan. Existing acceptance evidence stays in place in this cleanup.

Record only necessary public diagnostics. Exclude tokens, resolved debugger launch capabilities, environment
dumps, browser profiles, dependency caches and unrelated local data from new durable records. Inspect retained
files before committing them. Keep private raw evidence separate and name the omission without exposing it.

Documentation changes require link/command inspection and lightweight validation from the
[contributor guide](24-contributing.md#prepare-and-choose-checks), not a new native/AOT acceptance run.
Do not claim that editing instructions re-executed their examples. New runtime, toolchain or packaging changes
still require the affected integration evidence on their own revision.
