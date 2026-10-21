# Change-proportional continuous integration

Issue [#30](https://github.com/sotsera/spar/issues/30) preserves the milestone checks while selecting them by their inputs. `Windows managed probe` remains one always-present, sequential Windows job. Its final audit requires every selected check to succeed; skipped selected checks are failures. No branch-protection change is needed.

## Coverage map

The map includes every check in the previous workflow. Groups are additive, and each non-lightweight group requires the baseline and frontend prerequisites. Full qualification selects every group. The scripts retain their existing scenarios, expected-failure cases and ownership assertions.

| Group | Checks / retained execution path | Protected behavior and inputs |
| --- | --- | --- |
| Always | `Test-AgentSkills.mjs`, documentation reference validation, classifier and gate tests | Skill inventory/provenance/references; authored guide references; selection and result auditing |
| Baseline | `dotnet build Spar.slnx --disable-build-servers`; `dotnet test --solution Spar.slnx --no-build`; `Test-IntegrationHelpers.ps1`; `Test-ProbeEvents.ps1` | All managed bridge/generator/asset/development tests and real watcher tests; helper import/ownership checks and shared log framing. Ordinary code changes retain all of these. |
| Frontend | `eng/bridge-client-tests`: install and test; integration fixture SPA: install, check and build; `Test-FrontendNotices.mjs` | Generated JS/TypeScript contracts, React/Vite compilation, complete bundled licenses. Build must precede generated-client checks. |
| Development | `Test-DevelopmentSession.ps1`; `Test-BackendUpdates.ps1`; `Test-ProjectLaunch.ps1`; `Test-DevelopmentInspection.ps1`; `Test-MinimalApplication.ps1`; `Test-DevelopmentReadiness.ps1` | Ordinary app startup/update/restart without test reports; native readiness rejection/renewal; owned SPA/HMR, backend patches and actual retirement, restart/compile recovery, IDE bootstrap lifetime, actual WebView inspection and bounded asynchronous actions |
| Native | `Test-Bridge.ps1` and `Test-NativeHost.ps1` against Debug | Native document/permission/contract authority, cancellation and response lifetime, COM/window/profile ownership, CoreCLR lifecycle and errors |
| Production | Normal Release publish; `Test-ProductionBoundary.ps1` (includes `Test-InspectionBoundary.ps1`); `Test-Production.ps1`; ordinary sample publication/boundary and `Test-MinimalPublication.ps1` | Real SPA/assets/notices, copied AOT execution, contract matching, missing-runtime diagnostics, exclusion of probe/inspection/development code |
| Compatibility | `Test-Compatibility.ps1` | Attributable dynamic dependency, copied CoreCLR execution, strict AOT rejection, dependency removal and unrelated-failure rejection |
| Packages | `Pack-Local.ps1`; `Test-ExternalConsumer.ps1`; `Test-MinimalConsumer.ps1` | Default ordinary consumer and explicit instrumented fixture; fresh external restore/build, public package references, packaged coordinator and contracts, development updates/restart, copied AOT/CoreCLR execution and boundary checks |
| AOT probe | Separate `SparNativeProbe=true` publish; copied payload inventory; `Test-NativeHost.ps1` and `Test-Bridge.ps1` | Strict native test payload, copied AOT lifecycle and bridge scenarios. Runs after the normal production boundary check. |

## Selection policy

- Recognized documentation and repository skill changes use lightweight checks. Historical evidence under `docs/evidence/` is retained documentation, not an executable test input.
- Managed test/precursor probe changes use baseline plus frontend checks.
- Sample or fixture frontend changes use baseline/frontend, development, production and package checks: the native drivers and external scaffold consume that frontend.
- Development coordinator, CLI, editor and session configuration changes also use development, production and packages. The coordinator and Vite adapter ship in the development package; development exclusion remains a Release assertion.
- Runtime, bridge, contract generator, sample backend/contracts, build targets, third-party notices, dependency manifests/locks, and shared fixtures select full coverage. These feed multiple development and delivery paths.
- A known standalone test driver selects its group and prerequisites. Shared helpers (`Read-ProbeEvents`, native desktop helper, development fixture helpers and the development-session library) select full coverage because other drivers call them.
- CI/classifier changes, shared configuration, unknown paths and failed classification select full coverage. New files are not silently assumed to be harmless. Mixed changes take the union.

PR classification uses the merge base of the event's base/head SHAs and the PR head. Main pushes use the event's before/after range, including every commit in a push. Rename detection is disabled so both the deleted source and added destination contribute. Missing commits, empty/unusable ranges and unsupported events select full coverage. Paths are passed as data, never interpolated into shell code.

Main retains the same selective checks on the actual integrated squash commit. Feature pushes do not duplicate PR runs. PR updates cancel superseded runs; independent main pushes retain independent validation.

## Full qualification and acceptance responsibilities

Use `gh workflow run managed-probe.yml --repo sotsera/spar --ref <ref>` to qualify an exact ref. The **Actions → Managed probe → Run workflow** UI becomes available when the default branch declares manual dispatch. Manual dispatch always runs the full programme. Version tags (`v*`) also run full qualification. Toolchain, dependency, workflow and shared-configuration changes select full coverage automatically.

Before distributing a release, the maintainer must verify a successful full run for the exact release SHA, inspect its receipts, and execute the relevant desktop/editor walkthroughs from the [acceptance matrix](19-milestone-1-acceptance.md). A tag run is qualification, not an automatic package publication. There is currently no release publisher to gate. CLI dispatch to the feature ref was exercised in the fork before integration; the required upstream PR run remains separate.

CI native tests do not establish reviewed pixels or debugger acceptance. `Test-VSCodeProbe.ps1`, `Test-VSCodeSession.ps1`, `Test-VSCodeDevelopment.ps1`, their external-consumer lane, `-VerifyRender` image review, and the manual Visual Studio walkthrough remain available through their existing guides. They were not in the old hosted workflow and are not silently promoted to hosted acceptance. Clean-machine verification remains deferred.

## Results and artifacts

The job summary and `managed-probe-results` record source/event/range, changed paths, classification reasons, selected groups, and each check's success/failure/skipped outcome. Success artifacts retain compact native/integration/package/publication receipts for 14 days. Failed or cancelled runs retain these results and the previous detailed evidence paths as `managed-probe-diagnostics` for 30 days (logs, source hashes, test results, inspection snapshots/images and isolated failure evidence). Selection and gate reports never include environment dumps or launch credentials. Runner logs remain available under GitHub's repository retention policy.

The [execution evidence](evidence/proportional-ci-2026-10-20/README.md) records actual 25s documentation, 24s skill and 2m29s managed-baseline runs, inspected artifact contents/retention, and upstream required-check failure blocking. Full recovery status belongs to the implementation PR's current check. Evidence upload is not the primary optimization: the pre-change [run 39492172536](https://github.com/sotsera/spar/actions/runs/39492172536) took 19m29s, including backend updates 5m16s, compatibility 3m20s, external consumer 2m26s and inspection 1m44s; upload took 20s. These are individual observations, not performance guarantees.


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
