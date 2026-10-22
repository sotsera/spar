# Proportional CI execution evidence

Implementation: [PR #35](https://github.com/sotsera/spar/pull/35), for [issue #30](https://github.com/sotsera/spar/issues/30). Classifier/workflow implementation commit: `2294bcd`. The [policy and coverage map](../../21-continuous-integration.md) records retained scenarios and manual acceptance responsibilities. This record does not claim native pixel review, IDE acceptance, or clean-machine qualification.

## Observed durations

Durations below are job start to completion, excluding queue time. Each is one hosted Windows observation, not a runtime promise or a controlled multi-run benchmark. The previous workflow ran full coverage regardless of input category; the three selective probes use the same new implementation as their base and add one harmless comment to the named input.

| Input / execution | Run | Duration | Observed coverage |
| --- | --- | --- | --- |
| Previous full main job | [39492172536](https://github.com/sotsera/spar/actions/runs/39492172536) | 19m29s | Existing full milestone CI programme; evidence upload 20s |
| Documentation (`README.md`) | [39501368875](https://github.com/ghidello/spar/actions/runs/39501368875) | 25s | Selection/gate tests, guide references and skills; .NET setup and all integration groups skipped |
| Skills (`eng/Test-AgentSkills.mjs`) | [39501382341](https://github.com/ghidello/spar/actions/runs/39501382341) | 24s | Same lightweight checks |
| Managed test (`BridgeTests.cs`) | [39501398646](https://github.com/ghidello/spar/actions/runs/39501398646) | 2m29s | Lightweight checks, build, managed tests, log framing, generated client, SPA and notices; integration groups skipped |

All three selective runs passed `Windows managed probe`, including its final audit. Their temporary fork PRs [#13](https://github.com/ghidello/spar/pull/13), [#14](https://github.com/ghidello/spar/pull/14), and [#15](https://github.com/ghidello/spar/pull/15) were closed without merging; validation branches were retired. Fork runs prove selection and artifacts, not upstream branch protection. The corresponding `*-run.json` records include source SHAs, event type, job/step times and outcomes; `*-results.json` retain the actual selection and audited steps.

## Required failure and recovery

The upstream, non-draft implementation PR temporarily added one missing local documentation reference at `d5d223a`. [Run 39501125953](https://github.com/sotsera/spar/actions/runs/39501125953) failed the real documentation validator. The final audit also failed because other selected checks had not run. GitHub reported **Windows managed probe** as a required failure and the non-draft PR as **BLOCKED**, captured in [blocked-pr.json](blocked-pr.json) and [required-failure.json](required-failure.json). No merge was attempted, and no ruleset was changed.

The broken reference was removed at `be49967`, restoring the guide content. Full recovery qualification runs on the implementation PR because workflow/classifier changes select every group. The PR's current check result is the authoritative recovery status: this committed record does not predeclare a pending or subsequently superseded full run successful. Final recovery links and remaining work are maintained in the PR and issue. The final head must pass before merging.

The 32 [classifier/gate tests](https://github.com/ghidello/spar/blob/2294bcd8a4ecac62f3cdd5b6c955924dc6dafada/eng/ci/selection.test.mjs) cover documentation, skills, frontend, runtime, contracts/generator, coordinator/editor, packaging, shared configuration, toolchain and unknown paths; unions; real Git rename and merge-base/multi-commit ranges; missing history; malformed-event fallback; explicit/tag qualification; and selected failure/cancellation/skipped/missing outcomes. The gate rejects a failed outcome even if its conclusion says success. The workflow wiring test checks the selection for every audited step and the build/client and production/probe ordering.

## Artifacts actually inspected

Downloaded the documentation, skill, baseline and intentional-failure artifacts. [downloaded-artifacts.json](downloaded-artifacts.json) records each extracted file's size and SHA-256. Successful lightweight/baseline artifacts contain `ci/selection.json` and `ci/results.json`; their selections, skipped outcomes and passing audits were inspected. They contain no fabricated native receipts for suites that did not run.

The successful runs each published `managed-probe-results` with an expiry 14 days after creation. The failed run published both `managed-probe-results` and `managed-probe-diagnostics` with 30-day expiry. See the retained `*-artifacts.json` API metadata. This intentionally early failure has only selection/audit JSON in its diagnostic artifact; the validator's exact missing-link error is in the workflow log. The detailed integration logs/hashes/snapshots/receipts retain the previous upload paths and will be produced when those suites execute and fail. Cancellation uploads remain best effort if the runner is terminated before upload.

## Local verification and limits

[local-checks.json](local-checks.json) records Node 24.21.0 and SDK 10.0.402; 32 classifier/gate tests; six skills and 14 case definitions; 191 local references in 23 authored guides; a successful solution build with zero warnings/errors; and all 65 managed tests passed, zero skipped. The initial sandboxed build exited without diagnostic errors; only the successful unrestricted retry is counted. [implementation-hashes.json](implementation-hashes.json) identifies the CI script bytes used by these checks.

No runtime, package, integration-driver or dependency implementation was changed. The manual desktop/editor responsibilities remain those in the milestone acceptance matrix. Main-push range handling is tested with an actual local Git history; the new post-merge path requires an eventual merge to execute against the integrated commit. CLI manual dispatch against the feature ref successfully created [run 39502086577](https://github.com/ghidello/spar/actions/runs/39502086577) at `5c0da39`; creation alone does not establish successful qualification. The Actions UI exposes manual dispatch after default-branch integration. Version tags also request all groups. Neither event publishes a release.
