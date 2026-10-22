# D3 progress and document readiness

Validated on 2026-10-22 as a separate change on D2 implementation `0e56537` (PR #48).
D3 depends on D2 and must follow its upstream merge. `sources.sha256` binds the exercised files;
local evidence was collected with the D3 working changes present, not claimed as a passing upstream SHA.

The coordinator emits progress at the actual selected build, frontend start, application launch,
debugger handoff and wait for native/typed readiness. Terminal progress uses bounded, control-safe
stderr while stdout remains the structured owner channel. VS Code presents those facts and diagnostics.
A new bridge or application-not-ready event withdraws ready/updated; metadata updates and old generations
cannot restore it. Debugger pause is distinct from readiness. No lifetime or admission policy changes.

Observed evidence:

- Warning-free solution/tool builds and 67 ordinary managed passes, no skips.
- Nine Node tests: six gate/entry-point cases and three presentation cases, including withheld readiness,
  stale generation, paused startup, failed rebuild/correction and bounded control-safe display output.
- [Twelve real native scenarios](native.json), including exact phase ordering, [readable stderr](terminal.txt),
  startup failure/timeout, native close, owner loss, actual process cleanup and unrelated sentinel survival.
- [Actual VS Code acceptance](vscode.json), with C# and C# Dev Kit versions in the receipt: breakpoint and
  Continue, explicit inspection click to reload a document, new bridge withdrawing displayed readiness,
  renewed readiness, CSS/backend patches with retained state, compiler rejection/correction, unsupported
  edit and delayed retirement, new debugger generation, failed contract rebuild and correction, Stop,
  and graceful native close in a second session. All 46 retained process instances actually exited.
- Reviewed [paused editor](paused.png), [failed-build status and readable output](build-failed-output.png),
  and [patched native app](native-patched.png). The native capture shows changed greeting/colors and
  retained counters/text. The failed build shows source location, build exit 1 and exact retained logs.
- [Four project-launch cases](project-launch.json) passed, including actual native exit and owner loss.
- The [fresh-package suite](packages-suite.json) passed its three checks: a fresh feed, the
  [instrumented consumer](instrumented-package.json) and the [ordinary consumer](minimal-package.json).
  Actual HMR/patch/restart, copied AOT/CoreCLR, attributable strict dependency failure and production
  boundaries passed. The copied VS Code adapter contains the new presentation module.

Those package binaries and reviewed captures precede the final cancellation guards, and are bound by
`before-guards.sha256`; their sourceRevision fields identify HEAD plus the recorded working changes.
They are not exact-final-binary package qualification. Final review added cancellation checks before
launching a child/watcher/debugger after an output observer requests forced stop. The actual Windows-job
[regression test](owner-output-test.txt) passes with no child launch and verified owned-job completion.
Final `sources.sha256` includes those guards and their test. Current PR CI must qualify its exact source;
no pre-guard package result is promoted to that later revision.

The editor test's optional review pause initially held an in-flight greeting longer than its existing
30-second bridge deadline. Continue therefore could not establish readiness; the attempt failed and
retained its timeout/cleanup receipt under `artifacts/vscode-development/debugger-188bd5f1e0684bf0b9cb68317de93774`.
The harness now reviews after a completed read, using explicit Pause/Continue. A second run was interrupted
when the maintainer closed all VS Code windows. It has no completed receipt and is not counted as a pass;
its observed process identities were independently checked ended before relaunch.

During the completed review, Pause selected generated framework source. The operator restored the consumer
backend editor through Quick Open so its public C# diagnostics were observed. That same restoration is now
explicit in the optional review harness with showTextDocument. No private Dev Kit log is parsed by product
control; the harness retains private-log unsupported-edit evidence as before. No runtime timeout increase,
automatic request replay or runtime race fix is attributed to these harness corrections.

Commands: matching solution/tool build, ordinary no-build managed tests, `eng/Test-DevelopmentSession.ps1`,
`eng/Test-VSCodeDevelopment.ps1 -ReviewPresentation -VerifyRender`, Node presentation/gate tests and current
documentation/skills checks. The new presentation tests are a fixed Fast catalog check. Native, debugger,
project-launch and fresh-package checks remain separate responsibilities. Actual Visual Studio gestures,
clean-machine qualification and release acceptance are not inferred from VS Code or lifecycle automation.

Final guard validation: matching warning-free solution build, the one closed-output Windows-job test,
a fresh complete 12-case native run (native.json) and an unattended actual VS Code regression
(vscode-final.json) passed. The final editor run retained and verified exit of 45 process instances.
Its unchanged presenter handled actual bridge withdrawal/recovery, failed rebuild/correction and Stop.
The optional visual-review harness restoration is separate from this unattended run. The two copied
package adapters match the tested presenter byte-for-byte (copied-adapter.json); no actual package
editor gesture is inferred from the copy check. Exact final package qualification remains hosted CI.
