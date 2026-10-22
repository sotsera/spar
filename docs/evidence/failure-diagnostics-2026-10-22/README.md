# D2 failure diagnostics

Validated on 2026-10-22 from upstream PR #47 baseline `ce290daaaf35208026f34bc68909bfeff148f6b2`
with the implementation in this record's PR. `sources.sha256` binds the exercised implementation.

Failures retain the existing codes and session/process identity, with additive structured diagnostics:
role, exit code only if observed, actual stdout/stderr locations and next action. Existing views receive
the locations in detail text. Timeouts never imply exit, failed executable resolution never invents logs,
and debugger output is not claimed as coordinator-captured. Raw child output remains in local logs.

Validation:

- Warning-free solution build; 67 ordinary managed tests passed, zero skipped.
- [Twelve real session scenarios](native.json): frontend exit 23 before readiness, timeout with no exit
  code at observation, selected build failure, invalid executable without logs, post-ready frontend and
  application failure, HMR/restart/native close, owner loss, malformed input, abrupt owner kill, port
  conflict and client rejection. Actual process/job completion and unrelated sentinel survival verified.
- [Actual VS Code 1.143.0](vscode.json): four existing preflight failures plus real frontend session
  failure. No application debug session started. The automatically revealed Output panel was visually
  reviewed: frontend role, exit 23 and exact retained logs are visible in [the capture](vscode-output.png).
- [Four project-launch lifecycle cases](project-launch.json) passed. This exercises the real project
  bootstrap, not new Visual Studio gestures or its Debug Output UI.
- Release build and production/inspection boundary checks passed: no development references, inspection
  implementation or frontend development code in the checked production outputs. No new copied-AOT,
  clean-machine or native-pixel acceptance is claimed locally; hosted suites qualify their exact run.
- Six gate/entry-point tests, documentation references and skill/provenance checks passed.

Commands: `dotnet build Spar.slnx --disable-build-servers`; `dotnet test --solution Spar.slnx --no-build
--minimum-expected-tests 1`; `pwsh -NoProfile -File eng/Test-DevelopmentSession.ps1`; `pwsh -NoProfile
-File eng/Test-VSCodeStartup.ps1 -IncludeSessionFailure -PauseForVisualReview`; `pwsh -NoProfile -File
eng/Test-ProjectLaunch.ps1`; Release fixture build and `eng/Test-ProductionBoundary.ps1`.

First native attempt reached the invalid-executable assertion and failed because the test assumed logs
already existed. The executable resolver rejects a missing file before opening output. The implementation
correctly returned absent log fields; the assertion was corrected to require that absence. The original
logs/receipt remain under ignored `artifacts/d2-native`; the complete corrected run is `d2-native-confirm`.
No production fix or unexplained retry is attributed to this test correction.
