# Integration test helpers

The executable drivers in `eng/Test-*.ps1` own their scenarios. Shared operations live in
`eng/integration/`; dot-sourcing these files only defines functions. Imports do not resolve
application paths, create fixtures, load native capture support, start processes, or reset results.

| Helper | Explicit inputs and ownership |
| --- | --- |
| `Fixtures.ps1` | `New-Fixture` and `New-BackendFixture` receive the repository, application and evidence paths. They return the isolated fixture and configuration. `Replace-Source` requires the expected original text. |
| `Sessions.ps1` | `Start-Session` receives the fixture, coordinator executable and working directory. The caller immediately retains the returned session. Event reading, waiting and commands receive that session. `Confirm-Exit` receives the unrelated process and returns a receipt for the caller to retain. |
| `Inspection.ps1` | Target-bound requests and correlated results receive the session explicitly; callers import `Sessions.ps1` and retain its command log. |
| `Processes.ps1` | `Start-UnrelatedProcess` returns the sentinel owned by the scenario. `Stop-TestProcess` forces only the supplied process to exit. `Close-TestSessions` takes the caller's retained sessions, closes coordinators and observes the previously retained child handles. |

`Confirm-Exit` still requires actual coordinator exit, stdout EOF, the expected exit code,
no cleanup-failure event, an owned-job completion receipt, exit of independently retained
processes and survival of the unrelated sentinel. Abrupt-owner tests explicitly select their
different receipt policy. A successful wait does not establish desktop rendering.

Failure teardown is separate from scenario acceptance. It does not add successful receipts.
It attempts all retained sessions, reports unfinished cleanup, and does not dispose handles
for processes whose exit remains unverified. A timeout does not transfer ownership or prove
completion. Fixtures and their `node_modules` junctions remain for inspection; helpers never
recursively delete them.

The default fixtures come from `fixtures/Spar.NativeSample`. `-MinimalApplication` selects the ordinary source without adding failure operations. See the [migration map](23-minimal-application.md#scenario-migration-map).

## Driver map and commands

| Driver | Shared operations | Retained scenarios |
| --- | --- | --- |
| `Test-DevelopmentSession.ps1` | Fixtures, sessions, processes | SPA HMR/state, restart, native close, owner loss, crashes, startup failures and credential rejection |
| `Test-BackendUpdates.ps1` | Backend fixtures, sessions, processes | Managed patch/recovery, rebuild/restart races and explicit force during retirement |
| `Test-DevelopmentInspection.ps1` | Backend fixtures, sessions, processes | Actual WebView actions, target identity, unfinished work, disabled inspection and failed restart |
| `Test-ProjectLaunch.ps1` | Backend fixtures, shared reading/assertions | Project bootstrap, native close, debugger stop, contract change and coordinator crash |
| `Test-VSCodeDevelopment.ps1` | Backend fixtures, shared reading | Actual debugger ownership, updates/recovery, coordinated restart and process exit; also accepts an external consumer |
| `Test-ExternalConsumer.ps1` | Sessions, processes | Fresh package consumer updates/restart followed by copied AOT and explicit CoreCLR delivery |

Existing executable commands and evidence destinations remain available. The internal
`-LibraryOnly` switches were removed and are rejected before setup: import the helper files
directly and supply their arguments instead of importing another executable scenario.
Project/editor supervisors keep their distinct process supervision and receipt checks in their own scripts.

From the repository root, build before the managed tests and run native suites sequentially:

```powershell
dotnet build Spar.slnx --disable-build-servers
dotnet test --solution Spar.slnx --no-build
pwsh -NoProfile -File eng/Test-IntegrationHelpers.ps1
pwsh -NoProfile -File eng/Test-DevelopmentSession.ps1 -VerifyRender
pwsh -NoProfile -File eng/Test-BackendUpdates.ps1 -VerifyRender
pwsh -NoProfile -File eng/Test-ProjectLaunch.ps1 -VerifyRender
pwsh -NoProfile -File eng/Test-DevelopmentInspection.ps1
pwsh -NoProfile -File eng/Test-VSCodeDevelopment.ps1 -VerifyRender
pwsh -NoProfile -File eng/Pack-Local.ps1
pwsh -NoProfile -File eng/Test-ExternalConsumer.ps1 -VerifyRender
```

The helper check verifies definition-only imports, absence of executable scenario imports,
real forced process exit and retention of a live handle after an unfinished-cleanup timeout.
It does not replace the actual native/editor suites. Shared helper changes conservatively
select full [CI coverage](21-continuous-integration.md); CI runs the helper check with the
existing baseline checks.

Review native captures as required by the [SPA](11-development-session.md),
[backend](12-backend-updates.md), [debugger](13-vscode-development.md),
[inspection](15-development-inspection.md) and [package](18-package-consumer.md) guides.
Do not rebuild or pack shared tool outputs while an earlier suite owns running processes.
Actual Visual Studio walkthroughs and clean-machine qualification remain separate checks.

The [extraction validation record](evidence/integration-helpers-2026-10-21/README.md) records
the executed suites, reviewed captures, first failure and unavailable checks.
