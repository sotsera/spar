---
name: spar-test-value-evidence
description: "Assess whether tests detect the behavior changed by a specific Spar fix, feature, PR, or commit. Use for revision-bound regression evidence; not for general coverage percentages, unrelated suite audits, or routine test execution."
license: MIT
---

# Test-value evidence for Spar

Identify the requested change before evaluating tests. Follow root `AGENTS.md` and the relevant integration guide. A read-only review remains read-only; do not modify source or run builds/tests unless the task permits execution.

Bind evidence to the parent/base and tested head SHA. For uncommitted work, retain a patch/hash including relevant new files. Map each changed contract to an input or sequence, expected public outcome, real entry point, named test, and distinguishing assertion. Derive the expected result from the requirement or protocol, not from the implementation's own calculation. Existing unchanged tests may already protect the change.

Choose a meaningful comparison:

| Change | Evidence to seek |
| --- | --- |
| Bug fix | The same regression test fails for the original defect and passes with the fix. |
| New behavior | A buildable missing/wrong implementation is detected by an assertion. |
| Stronger tests | A plausible wrong public outcome is detected by the strengthened test. |
| Refactor | Preserved behavior passes before and after; a failure before is not required. |
| Documentation | Explain whether an executable contract changed; generated/build metadata can affect behavior. |

A missing-symbol compiler error does not prove feature coverage. An expected compiler diagnostic is valid evidence when a harness explicitly asserts it. Setup errors, zero selection, unrelated failures, timeouts, and skips are not proof that a test caught the defect.

When paired execution is authorized, use an isolated fixture or checkout with its own effective outputs/caches. Run the correct behavior, introduce only the selected defect, rebuild and rerun the same assertions, then restore and confirm the correct behavior. Record the fault patch, exact commands, counts, expected/actual failure, and actual process retirement. Never weaken assertions or deliver the fault patch. Do not touch another session's outputs, kill unrelated processes, or recursively delete fixture junctions.

For package/build changes, repack each variant and prove which package the fresh consumer loaded. For lifecycle changes, exercise the real admission/retirement path: cancellation or timeout is not proof that work ended. For native/editor/AOT changes, use the relevant integration driver and image review; mocks and source compilation alone do not establish acceptance. Do not replay backend mutations automatically.

Report only the level obtained: executed detection, executed survival of a non-equivalent fault, static prediction, unverified, or not applicable with a reason. Keep the scope proportional to the change; no full-suite mutation campaign or new mutation dependency is implied. Relevant source, helper, configuration, or dependency changes invalidate earlier evidence until rechecked.

Adapted from dotnet/skills; see [provenance](../UPSTREAM.json) and [MIT notice](../dotnet-skills-LICENSE.txt).
