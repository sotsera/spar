# Cancellation callback test correction — 2026-10-21

The fork [main run 39667782254](https://github.com/ghidello/spar/actions/runs/39667782254) failed on
`e908e1a0aa5cbff9611a8848995ebc5d781055e0`: build passed, then 72 of 73 managed tests passed.
`CancellationCallbacksAreAlsoOwnedUntilTheyReturn` failed its final assertion at `BridgeTests.cs:149`,
which expected an observer event whose `Code` was `cancelled`. The reported events were `connected`,
`admitted` and `completed`, all without that code. Its earlier blocked-callback ownership assertion passed.

## Cause and scope

The bridge supports two relevant outcomes after cancellation: cancellation wins `Task.WhenAny` and
`Reject` emits an observer event plus the cancelled reply; or the invocation-result path observes the
cancelled token and returns that same error reply without a separate rejected event. The old assertion
required one scheduling path rather than the wire contract. The CI trace is consistent with the latter
path; that run did not retain wire replies, so their original contents cannot be reconstructed.

The corrected test requires exactly one wire reply with `status: error` and `code: cancelled`, completed
lifecycle observation and one backend entry. It retains the assertion that call ownership is unfinished
while the cancellation callback is blocked. A second theory case waits for the cancellation reply before
releasing the handler, explicitly controlling the cancellation-first schedule. The original concurrent
schedule remains covered; it does not guarantee that the invocation-result path wins in a given repetition.
No runtime code, deadline, dependency, test skip, workflow or branch rule changed.

## Verification

Base: merged review PR #43, `4e82ce4160c466449f3cbefac34b4207c866fd57`.
The [receipt](receipt.json) records the modified test hash, original log hash and actual test counts.

```powershell
dotnet build Spar.slnx --disable-build-servers
dotnet test --solution Spar.slnx --no-build
dotnet test --project tests/Sotsera.Spar.Bridge.Tests --no-build --filter-method 'Sotsera.Spar.Bridge.Tests.BridgeTests.CancellationCallbacksAreAlsoOwnedUntilTheyReturn'
node eng/ci/Test-Documentation.mjs
node eng/Test-AgentSkills.mjs
node --test eng/ci/selection.test.mjs
git diff --check
```

The build had zero warnings/errors and all 74 managed cases passed, zero failed/skipped. Repetition counts
for the two-case filter are in the receipt. Twenty executions of the old test during diagnosis passed;
the original CI failure was not reproduced locally. Repetition checks add evidence but are not a proof
that every possible schedule executed. Native/editor/AOT suites were not repeated for this test-only change.

## Merge gate reconciliation

PR #42's [required upstream run](https://github.com/sotsera/spar/actions/runs/39637943865) passed before its
merge at 20:06:49 UTC. The failed fork push run began at 20:09:02 UTC, after that merge. The exact merged
commit's [upstream main run](https://github.com/sotsera/spar/actions/runs/39667475257) also passed, including
the managed-test step. The active upstream `protect-main` ruleset required the GitHub Actions
`Windows managed probe`, strict up-to-date checks and no bypass actors when inspected. The fork's
post-merge push run was a separate check, not an upstream pre-merge gate. No protection bypass or gate
weakening explains this merge. The original failed attempt remains intact.

Original logs/artifacts are retained locally under ignored `artifacts/ci-39667782254`; corrected test
logs are under `artifacts/bridge-cancellation-fix`. The excerpts above preserve the useful first-failure
evidence independently of GitHub artifact retention. The following D1 implementation remains separate.
