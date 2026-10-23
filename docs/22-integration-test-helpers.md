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
| `ProcessImage.cs` | Explicitly loaded by the startup/image drivers. Queries the executable through the caller's retained process handle without waiting for its module loader; a failed query on a live process is fatal. |
| `WatcherTimings.ps1` | Reads a caller-supplied, completed watcher stderr log for diagnostic timing attribution. Returns only numeric observations, completeness and the log path; it starts no processes and makes no lifecycle decisions. |

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

`integration/ProductionObservation.ps1` owns sanitized, bounded progress observations only. The
production driver owns readiness, deadlines, retained handles and teardown. Importing the helper
starts no process and does not infer an antivirus cause. `integration/CompatibilityBuildPaths.ps1`
changes only a caller-supplied private fixture's Release compiler paths; it does not build or cache.

## Driver map and commands

| Driver | Shared operations | Retained scenarios |
| --- | --- | --- |
| `Test-DevelopmentSession.ps1` | Fixtures, sessions, processes | SPA HMR/state, restart, native close, owner loss, crashes, startup failures and credential rejection |
| `Test-BackendUpdates.ps1` | Backend fixtures, sessions, processes | Managed patch/recovery, rebuild/restart races and explicit force during retirement |
| `Test-DevelopmentInspection.ps1` | Backend fixtures, sessions, processes | Actual WebView actions, target identity, unfinished work, disabled inspection and failed restart |
| `Test-ProjectLaunch.ps1` | Backend fixtures, shared reading/assertions | Project bootstrap, native close, debugger stop, contract change and coordinator crash |
| `Test-VSCodeDevelopment.ps1` | Backend fixtures, shared reading | Actual debugger ownership, updates/recovery, coordinated restart and process exit; also accepts an external consumer |
| `Test-ExternalConsumer.ps1` | Sessions, processes | Fresh package consumer updates/restart followed by copied AOT and explicit CoreCLR delivery |
| `Test-ProcessImage.ps1` | Process image query, suspended native fixture | Exact image paths for live system console and ordinary executable children before their loaders run; termination followed by actual exit |
| `Measure-DevelopmentLatency.ps1` | Backend fixtures, sessions, processes | Explicit startup/CSS/backend/rebuild latency samples, preserved patch state and actual replacement/session exit; no automatic timing threshold |
| `Measure-StartupCost.ps1` | Sessions, processes; caller-prepared source or package config | Repeated native/typed startup and explicit-command rebuild, retained phase events and actual exit; no timing threshold |
| `Test-ContractIncrementality.ps1` | Isolated contracts and forwarding generator probe | Source or fresh-package repeated builds, generated assembly/resource contents, changed inputs, missing outputs, design-time and clean behavior |
| `Test-FrontendRestore.ps1` | Local npm tarball, real MSBuild asset target and npm timing records | Source/packaged reuse, changed inputs and installed files, failed-install recovery, forced install, clean and current frontend output |
| `Test-RuntimePackRestore.ps1` | Ordinary source/package restore graphs and a transitive framework dependency | Debug reduction, explicit opt out, production/ReadyToRun/self-contained guards and cold CoreCLR publication/execution |
| `Test-ProductionDiagnostics.ps1` | Retained sleeping process and application-event observations | Creation identity, phase, privacy, bounded samples, unknown cause and actual exit |
| `Test-CompatibilityBuildPaths.ps1` | Two independently compiled fixture roots | Equal Release bytes, changed executable behavior, physical Debug paths and explicit override |
| `Test-NativeWindowSelection.ps1` | Desktop helper, two hidden fixture windows | Select the actual Spar window with a same-process decoy first; destroy only the fixture's two windows |

Existing executable commands and evidence destinations remain available. The internal
`-LibraryOnly` switches were removed and are rejected before setup: import the helper files
directly and supply their arguments instead of importing another executable scenario.
Project/editor supervisors keep their distinct process supervision and receipt checks in their own scripts.

From the repository root, build before the managed tests and run native suites sequentially:

```powershell
dotnet build Spar.slnx --disable-build-servers
dotnet test --solution Spar.slnx --no-build -p:IncludeLongRunningTests=true --minimum-expected-tests 1
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
It does not replace the actual native/editor suites. All changes run the fixed
[CI suites](21-continuous-integration.md); Processes runs the helper check.

The startup driver allows a console helper only after positively identifying the full system
`conhost.exe` path. It queries `QueryFullProcessImageNameW` through a retained process handle:
`MainModule` can be unavailable while a live child's loader has not run. Only actual exit permits
unavailable identity; a live query failure still fails the check. `Test-ProcessImage.ps1` holds
two real children suspended to cover this window deterministically, then terminates and waits
for both. Processes runs this regression explicitly before the startup matrix; no path-based
selection, retry delay or timeout increase is involved.

Review native captures as required by the [SPA](11-development-session.md),
[backend](12-backend-updates.md), [debugger](13-vscode-development.md),
[inspection](15-development-inspection.md) and [package](18-package-consumer.md) guides.
Do not rebuild or pack shared tool outputs while an earlier suite owns running processes.
Actual Visual Studio walkthroughs and clean-machine qualification remain separate checks.

The [extraction validation record](evidence/integration-helpers-2026-10-21/README.md) records
the executed suites, reviewed captures, first failure and unavailable checks.
