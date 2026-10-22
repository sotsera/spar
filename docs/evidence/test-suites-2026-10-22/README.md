# Fixed test-suite execution evidence

Implementation: [PR #46](https://github.com/sotsera/spar/pull/46), starting from merged PR #45,
`4331e53d303bdef878c58c6ce67047f139f9e635`. Implementation commit `57e8284` replaces changed-path
selection with five fixed automatic suites, explicit local commands, shared xUnit/MTP selection
and an always-running aggregate under the unchanged required check name.

## Local selection and ownership

- Fast passed: 30 bridge/generator and 37 ordinary development cases, none skipped, plus generated
  TypeScript/frontend/notices, framing, documentation, skill and gate checks.
- Processes passed: four development process cases and three real watcher cases, none skipped;
  helper forced-exit/unfinished-ownership checks and all 34 actual CLI startup cases passed.
- TRX names establish disjoint selections: 67 ordinary + seven process cases, no overlap.
- After matching builds, solution commands with `--no-build` selected 67 by default, all 74 with
  `-p:IncludeLongRunningTests=true`, and seven with the explicit long-only trait filter. No skips.
- An isolated test project importing the actual shared policy verified default exclusion (exit 0),
  a global minimum on an empty selection (exit 9), and an enabled real assertion failure (exit 2).
  Its initial setup had a namespace analyzer error and a duplicate import before any test ran;
  correcting the fixture setup did not change the shared policy. The failed build log remains local.
- Five focused Node tests verify missing/malformed/duplicate/stale reports, failed/cancelled/skipped
  outcomes, earlier successful attempts of the same run, workflow wiring and a real failing child
  whose later checks remain unexecuted. Cancellation/missing-report inputs were tested locally;
  the hosted experiment below is an actual failed-child proof, not a hosted cancellation experiment.

These local checks span the working implementation and implementation commit; the receipt records
final relevant file hashes. A first restricted build exited during restore without useful diagnostics;
the normal-permission run passed without a source change for that restore. Raw outputs remain ignored
under `artifacts/`. Hosted recovery must independently qualify the final committed source.

## First startup failure and bounded correction

The first Processes run passed all seven managed cases, then the original startup driver reported
PID 27820 as an application child during `unknown-before-events`. The CLI emitted the expected
`invalid_arguments` error and exited 1. That run did not record the child's image, so its exact cause
remains unproven. Thirteen instrumented runs (442 preflight processes) did not reproduce it.

Static review found that `Process.MainModule` returning null after exit was classified as an
application child, whereas the existing exception paths classified the same unavailable identity
separately. The driver now records observations, handles a missing image only after actual exit
under that existing policy, and still fails for a live child whose image is unavailable. Positively
identified application children and nonempty jobs still fail.

An isolated controlled input waited for each observed child's actual exit and supplied an unavailable
image before classification. The old observer failed the usage case; the corrected observer passed
all 34 cases, recorded 34 unavailable images and observed every owned job become empty. No runtime
source or backend mutation was changed. This detects the classification defect; it does not prove
the original unobserved child's identity. A subsequent ordinary Processes suite passed all checks.

## Actual required-gate failure

Signed temporary commit `612fa8ad3ebbf29ae64893f2c74f8161bd7fc02b` added one deliberately missing
README reference. [Run 39782199572](https://github.com/sotsera/spar/actions/runs/39782199572) tested
merge `0e4c0efb726e33abd990a30f0c2720374f5e2390`:

| Job | Observed outcome | Job duration |
| --- | --- | --- |
| Fast | Expected documentation failure; later checks not run | 32s |
| Processes | Success | 3m09s |
| Development | Success | 11m40s |
| Delivery | Success | 6m49s |
| Packages | Success | 6m55s |
| Windows managed probe | Failure from the failed child/report | 29s |

The aggregate downloaded exactly five reports and rejected Fast's failed/unexecuted checks.
GitHub reported the required check as **FAILURE**, the PR as non-draft and **BLOCKED**, with head
`612fa8a`. Ruleset 25345567 still required `Windows managed probe` from GitHub Actions integration
15368, strict/up-to-date, with no bypass actors. No merge was attempted and no rule changed.
The [compact receipt](receipt.json), [required failure](required-failure.json) and
[blocked PR snapshot](blocked-pr.json) preserve those observations.

The temporary reference is removed in the recovery commit. The exact recovery run/result is maintained
in PR #46; this record does not predeclare that later head successful. PR and integrated-main runs are
separate revision-specific evidence. Native/editor reviewed pixels and clean-machine qualification
are not established here. Existing native/package assertions and extra production sampling remain.
