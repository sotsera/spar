# CI reliability after the D1 timeout

Reviewed on 2026-10-22 against PR #45 head `a4c8d1e`, including merged PR #44. This is a measured
review and a focused install-network repair, not authorization for a general test rewrite.
The [CI policy](21-continuous-integration.md) remains authoritative for coverage and the required gate.

## What failed

[Run 39737327607](https://github.com/sotsera/spar/actions/runs/39737327607) exceeded the job's 30-minute
limit. GitHub explicitly annotated that timeout. The compatibility scenarios passed; cancellation
interrupted the external consumer's npm restore. The final audit correctly rejected the cancelled and
unexecuted selected checks. It is a secondary failure, not the original cause.

| Observation | Previous passing D1 run | Timed-out run |
| --- | --- | --- |
| Run | [39678014157](https://github.com/sotsera/spar/actions/runs/39678014157), `ae931df` | [39737327607](https://github.com/sotsera/spar/actions/runs/39737327607), `a4c8d1e` |
| Entire job | 14m55s | 30m42s including final evidence handling |
| Backend Hot Reload / retirement | 2m53s | 5m32s |
| WebView inspection | 56s | 1m35s |
| Compatibility | 1m49s | 14m46s |
| External / minimal consumers | 1m44s / 1m11s | First cancelled; second skipped |

The compatibility receipt records 318.4s for missing-toolchain and 321.1s for native-link-error.
Both logs say `added 24 packages in 5m` before reaching their intended compiler/linker errors. These
are expected negative tests, and neither should be removed or converted into a fallback success.
The surviving logs establish the npm delay; they do not identify its exact HTTP request.

## Focused repair

Keep `npm ci --ignore-scripts --include=optional`, frontend rebuilding, dependency failures, all selected
checks and the 30-minute limit. In the hosted job only, set `NPM_CONFIG_AUDIT=false`, retain npm timing
JSON on success, and retain npm diagnostic/timing logs on failure. Logs use a central absolute path so
commands in fresh external consumers are also observed; the log limit covers repeated invocations.

[`npm ci` submits an audit request by default](https://docs.npmjs.com/cli/v11/commands/npm-ci/).
The documented [HTTP timeout defaults to five minutes](https://docs.npmjs.com/cli/v11/using-npm/config/#fetch-timeout).
Advisory-service waiting is a plausible explanation for the two delays, not a retrospectively proven
endpoint diagnosis. Capturing timing/debug data closes that evidence gap on a future occurrence.

A controlled real-npm experiment used a local tarball and a loopback registry whose advisory endpoint
never replied. With a one-second fixture-only HTTP timeout, audit enabled still returned success after
waiting and sending the advisory POST. The same locked install with the job's audit-disabled setting
sent no advisory request and installed the package. A missing package still made a real GET and failed
with exit 1. This verifies separation of advisory-service availability from dependency correctness.
It does not prove the original hosted endpoint failure or promise a fixed runtime reduction.

There was no dedicated vulnerability gate in this workflow, and the experiment confirms that install
audit failure can coexist with exit 0. This repair makes no dependency-security qualification claim.
If a vulnerability gate is wanted, scope it explicitly with its own result and network-failure policy.

The [execution record](evidence/ci-timeout-2026-10-22/README.md) retains commands, receipts, first-failure
excerpts and the exact repair run. Public npm timings are not native/editor acceptance evidence.

## Proposed next scopes

1. **Isolate independent CI lifetimes.** Split baseline/frontend, development, delivery/compatibility,
   and fresh-package checks onto separate Windows jobs. Each job builds its own required outputs;
   native scenarios remain sequential within that runner. Keep the required **Windows managed probe**
   name as an always-present aggregate that fails on any selected failure, cancellation, skip or missing
   report. Do not change the branch rule until an actual failed-child proof and a passing full run
   verify the replacement. This reduces the critical path and prevents a slow compatibility run from
   spending the consumers' budget. Runner/provisioning cost and repeated builds are the tradeoff.
2. **Make restore cost observable before reusing work.** Use the new npm timers and per-case receipts
   to distinguish network, compilation and waits. Consider a lock-keyed npm download cache; preserve
   clean installs and fresh consumer package-cache isolation. Do not cache `node_modules`, generated
   contracts, `dist` or native outputs as acceptance inputs. Any later frontend incrementality needs
   complete dependencies, clean/changed-input tests and current generated-client binding.
3. **Review slow or unstable assertions individually.** Backend/inspection times varied substantially
   across these two runs, but that alone does not establish flaky assertions. Use their existing
   scenario logs and retained process identities to find the waits. Keep cancellation distinct from
   actual exit and readiness distinct from process/window creation. Retain first failures; do not use
   automatic retries, longer sleeps or broader timeouts as substitutes for diagnosing a race.

The local repair check also retained a concrete timing failure: cold CoreCLR/WebView startup took
26.7s to create the window; the generated greeting and bridge completion arrived around the driver's
30s deadline. That attempt failed. The exact already-built payload passed a separate fresh-copy run
without edits, so no runtime fix or resolved root cause is claimed. Record phase timestamps and test
the wait boundary before changing a deadline; the driver currently mixes cold startup and greeting
work under one budget. See the retained first-failure trace and both receipts in the execution record.

The dependency classifier already reduces documentation and managed-only work. Unknown inputs correctly
select full coverage. New standalone drivers should receive explicit mappings with dependency tests;
do not narrow runtime/publication checks merely because the full path is expensive. PR updates also
invalidate the previous combined result today; job isolation can preserve useful diagnosis even when
a new revision still requires fresh qualification.

The next substantial implementation should be the job/aggregate split, separately reviewed after the
current repair is validated. D2/D3 product diagnostics remain separate from CI infrastructure work.

Acceptance for that split needs actual hosted proof: an injected selected-child failure must block the
unchanged required gate; cancellation/missing results must also fail; documentation-only and unknown
input selections must retain their existing meanings. Verify normal production exclusion before the
separate probe build, actual process cleanup and fresh consumer isolation. Compare the critical-path
duration and total runner minutes across several full runs rather than promising a speedup from this
single timeout. Maintain revision-specific PR and integrated-main qualification.
