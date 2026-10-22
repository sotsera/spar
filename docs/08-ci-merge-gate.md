# Upstream CI merge gate

**Historical enforcement proof.** The recorded API responses establish the policy at that revision. Current contributor obligations are in [PR workflow](24-contributing.md#pull-requests-and-upstream-history), and check selection/retention is in [CI policy](21-continuous-integration.md).

Date: 2026-10-18. Upstream protection configured, failed-check blocking observed, and source recovery verified in [PR #15](https://github.com/sotsera/spar/pull/15). The PR's latest head must also pass the required check before merging.

## Enforced policy

Upstream `sotsera/spar` ruleset [protect-main](https://github.com/sotsera/spar/rules/25345567), ID `25345567`, requires **Windows managed probe** from **GitHub Actions**, integration ID `15368`, on the default branch. Branches must be up to date with the target branch before merging. The rule uses `strict_required_status_checks_policy: true` and `do_not_enforce_on_create: false`.

The existing PR requirement, signed commits, linear history, conversation resolution, force-push/deletion protection, and empty bypass list were preserved. Required external review approvals remain zero for the sole-maintainer workflow. Repository merge settings remain squash-only.

The workflow runs on pull requests before merging and on `main` pushes afterward. The post-merge run is an additional check; it is not the merge gate. Detailed logs are under upstream **Actions → Managed probe → Windows managed probe**. Fork branch runs remain separate from upstream PR runs.

The current [fixed-suite CI policy](21-continuous-integration.md) keeps that required name as an
always-present aggregate over five Windows suite jobs. Every PR, main push, tag and manual run executes
the same automatic coverage. Failed/cancelled/skipped jobs and missing or unsuccessful suite reports
block the aggregate. The historical proof below describes the earlier single-job gate; the ruleset
and required check identity remain unchanged.

## Observed evidence

- [Upstream main run 39146637086](https://github.com/sotsera/spar/actions/runs/39146637086) passed before configuring the gate. Its actual check identity was `Windows managed probe`, GitHub Actions integration `15368`.
- Draft PR #15 initially changed the greeting statement to `return;` on its own branch. [Upstream PR run 39147095673](https://github.com/sotsera/spar/actions/runs/39147095673) failed the real build with `CS0126`. This established the upstream PR trigger and an actual failure; the test step did not run after the failed build.
- After the required check was configured, the PR was marked ready. GitHub's public API reported `isDraft: false`, `mergeStateStatus: BLOCKED`, and the upstream check with `conclusion: FAILURE`, `isRequired: true`. This isolates the CI block from a draft restriction. No merge request was attempted while the source was invalid.
- The greeting was restored exactly to the baseline source. [Upstream recovery run 39147429145](https://github.com/sotsera/spar/actions/runs/39147429145) passed the build, all three real watcher tests with none skipped, and evidence upload. The final documentation/evidence-only head must independently satisfy the same required check; see [PR #15 checks](https://github.com/sotsera/spar/pull/15/checks) and its final validation summary.

The [structured receipt](evidence/ci-merge-gate-2026-10-18.json) preserves the rule identity, check/run identities, blocked-state response, and source restoration check. The intentional failure remains only in the validation branch's intermediate history; the final source must match `main`. `main` was not changed by this validation.

## Ongoing maintenance

Keep the job name stable. Before renaming or replacing a required check, prove its successor on upstream PRs and update the ruleset deliberately. AOT and external-consumer checks are now included in the [CI coverage map](21-continuous-integration.md#coverage-map). Do not make unavailable desktop scenarios look successful or introduce a bypass to merge a failed check.

If a required PR check fails, fix the feature branch and let CI rerun. If the additional `main` run fails, investigate promptly and submit a fix or revert through a PR; do not bypass protection or treat the post-merge run as the first validation.
