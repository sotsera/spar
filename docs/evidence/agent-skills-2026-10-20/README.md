# Focused agent skills validation — 2026-10-20

Validated the instruction changes on `chore/2-focused-agent-skills`, based on upstream main `9401f3ae483a7ffd39a6c3a01472e68a787e56dd`. The working tree contained the proposed skills, bindings, validator, documentation, and CI step. [source-hashes.json](source-hashes.json) identifies the tested instructions and validation inputs using SHA-256 over UTF-8 text with CRLF normalized to LF. Upstream provenance is recorded separately in [UPSTREAM.json](../../../.agents/skills/UPSTREAM.json).

## Executed checks

| Check | Observed result | Evidence |
| --- | --- | --- |
| Bundled skill-creator frontmatter validator | All six skills valid | [frontmatter.log](frontmatter.log) |
| Node structural check, including original hashes against the pinned upstream checkout | Six skills and 14 case definitions valid | [structural.log](structural.log) |
| `dotnet build Spar.slnx --disable-build-servers` with a unique binlog and `ProjectImports=None` | Success, zero warnings/errors | [build.log](build.log) |
| Bridge test project with `--no-build --filter-method Sotsera.Spar.Bridge.Tests.BridgeTests.RejectionPreventsHandlerEntry` | 12 theory cases passed, none skipped | [filter.log](filter.log) |
| `dotnet test --solution Spar.slnx --no-build` | 65 passed, none failed or skipped | [test.log](test.log) |
| Existing binlog replay | Recovered the intentional missing-input diagnostic; the marker-writing build task did not execute again | [results](examples.json), [capture](examples/failure/capture.log), [replayed errors](examples/failure/replay-errors.log) |
| Generated source and cleanup example | Evaluation-time glob failed on the first build and passed on the second. Adding Compile/FileWrites items during generation passed with the generated source absent initially, printed `42`, and Clean removed that source | [results](examples.json), [first](examples/generated/first-build.log), [second](examples/generated/second-build.log), [fixed](examples/generated/fixed-build.log), [clean](examples/generated/clean.log) |

The Python validator used PyYAML 6.0.3 installed only in ignored `artifacts/agent-skills/python-tools`; neither CI nor shipping packages gained that dependency. Raw binlogs remain ignored local artifacts. Only the reviewed text diagnostics are retained here.

The [executed example driver](executed-driver.ps1.txt), [failure project](examples/failure/MissingInput.proj), and [generated-source project](examples/generated/GeneratedFixture.csproj) preserve the actual setup. The driver is historical text containing the original artifact paths. Reproduction must use a fresh ignored fixture directory and fresh log paths; do not execute against this evidence directory. The generated-source example establishes Compile/FileWrites behavior, not the separate resource-processing or package-consumer paths.

## Independent simulated review

A separate evaluator received only the 14 prompts, without expected selections or grading criteria. It read the six names/descriptions first, then selected skill bodies and their referenced repository bindings. It was instructed to simulate the responses, not execute the scenarios or edit product files. Its sole output was [evaluator-results.json](evaluator-results.json).

All 14 selections matched the expected sets: ten positive/boundary cases and four cases where no skill should activate. Review of the responses found the intended core safeguards: native MTP syntax, no-build preservation, static versus executed test evidence, no manufactured red result for a refactor, private binlogs, replay without rebuilding, target/import ordering, execution-time generated items, and no inference of rendering acceptance from managed tests.

The evaluator noted that the test-evidence description emphasizes defect detection, while the native rendering evidence case relies on its broader PR-evidence scope. The body explicitly covers that case and the evaluator selected it correctly. The generated-resource response covered timing and owned cleanup, but did not independently demonstrate a fresh package consumer. Execution-request cases remained simulations under the evaluation protocol; actual method filtering and SDK replay were checked separately above.

This is one independent simulated routing pass, not a statistical benchmark or proof of automatic skill discovery/activation by the host. Source hashes bind the retained results to the reviewed inputs; future instruction changes require appropriate reevaluation.

## Scope and remaining checks

No product runtime or production build behavior changed. Native rendering, AOT publishing, and IDE walkthroughs were not repeated for this instruction-only change. Their existing evidence remains separate. Clean-machine verification remains deferred by the user and is not marked complete. CI results for the resulting PR are tracked on GitHub rather than inferred from these local runs.
