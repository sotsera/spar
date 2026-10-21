# Minimal application and integration fixtures

The ordinary application is `samples/Spar.NativeSample`, with its contract in
`samples/Spar.NativeSample.Contracts`. Start here when writing an application. The
`fixtures/` application is the relocated milestone acceptance harness, not a second
recommended application architecture. Both use public Spar APIs; the runtime remains
independent of React and Vite.

## Application code

- `BackendContract.cs` exports only `greet` and `counter`, with their DTOs and permissions.
- `SampleBackend.cs` implements those operations and owns the backend counter.
- `Program.cs` selects Debug development or Release production startup.
- `Program.Development.cs` connects to the owning coordinator, configures the native
  bridge and attaches the window. The same bootstrap supports project F5.
- `Program.Production.cs` loads the embedded assets and runs the native window.
- `frontend/src/App.tsx` displays typed results, local state and development diagnostics.
  Backend updates invalidate reads; mutations are never replayed automatically.

Generate bindings by building the application; do not handwrite dispatchers or edit
`frontend/src/generated`. The separate contract assembly stays immutable during Hot Reload.

## Start, edit and publish

Use the prerequisites and commands in the [README](../README.md). The coordinator builds
before launch. A bare Debug executable asks you to use the development entry point; it
no longer starts the old static bridge probe. `-p:SparNativeProbe=true` belongs only to
`fixtures/Spar.NativeSample`, where the native/bridge drivers still require it.

For VS Code, load the local adapter as described in the [debugger guide](13-vscode-development.md),
then use the repository's **Spar: native development (local adapter)** launch configuration.
It targets the ordinary sample. Set a breakpoint in `GreetAsync`, continue, then edit the
frontend or greeting. Use **Spar: Restart backend**. The debugger is the sole backend
Hot Reload owner in this lane.

For Visual Studio, choose the project under the solution's **samples** folder (the
**fixtures** folder has the same preserved assembly name), then **Spar development**.
Build the development tool and install frontend dependencies first. For restart-required
edits, close, wait for cleanup, rebuild and F5. Historical fixture walkthroughs do not
establish a new minimal-app Visual Studio acceptance pass.

For an ordinary package application, `eng/New-PackageConsumer.ps1` defaults to the minimal
source and writes `consumer.json` identifying that choice. Follow the [package guide](18-package-consumer.md)
for the isolated feed/cache, public API projects, debugger setup and publication. Select
`-IntegrationFixture` only when creating a disposable consumer for the older comprehensive
acceptance drivers; their failure operations are intentional test instrumentation.

## Readiness without a test export

The ordinary Debug host opts into `EnableDevelopmentReadiness`. After posting a successful
typed operation result, it schedules two browser animation frames. A one-use random signal
is checked against the current native document, navigation and source before the owning
session receives readiness. It cannot invoke backend operations or confer permissions.
Navigation retires the previous identity; a stale signal cannot ready its replacement.
The native callback remains owned until scheduling actually completes. This observer is
compiled out of Release and does not enable inspection.

This establishes typed-call and frame progress, not a visual acceptance claim. Native
captures and image review are still required to prove the visible result. The older
fixture's application-owned `Report` path remains for its detailed scenario snapshots;
ordinary application startup no longer depends on it.

## Scenario migration map

| Previous application content | Current location | Assertions/drivers retained |
| --- | --- | --- |
| `Program.cs` native switches, static HTML/JS, `Probe.js`, `SampleDocument.cs` | `fixtures/Spar.NativeSample`, unchanged | `Test-NativeHost.ps1`, `Test-Bridge.ps1`: navigation, permission/contract rejection, startup/callback failures, worker close, leases, process cleanup; CoreCLR and explicit AOT probe builds |
| Delay/denied/report operations and DTOs | Fixture backend and `fixtures/Spar.NativeSample.Contracts` | Bridge, backend retirement and stale-client tests; cancellation does not imply completion |
| React probe commands and DOM reports | Fixture `frontend/src/App.tsx`, unchanged | SPA HMR/state, backend patch/recovery/races, forced retirement, owner loss, editor Stop and native close |
| Structured inspection scenarios | Same executable driver; generic request functions moved to `eng/integration/Inspection.ps1` | Target identity, bounded actions, timeouts, duplicate IDs, actual asynchronous completion and cleanup |
| Instrumented production entry point | Fixture `Program.Production.cs`, unchanged | Copied AOT/CoreCLR execution, missing-runtime errors, payload and production-boundary checks |
| Package acceptance source | `New-PackageConsumer.ps1 -IntegrationFixture` | Existing external package and debugger assertions, isolated feed/cache and public API references |
| Ordinary greeting/counter application | `samples/`, reduced | New public-inspection development test, copied publication test and default minimal package consumer |

The existing executable driver names remain entry points. Source-project references,
frontend contract tests, CI commands and current integration guides now select `fixtures/`
where they need test operations. Historical evidence paths retain their original meaning.
No old scenario or assertion is replaced by the new minimal smoke tests.

## Verification

Build first. Run native suites sequentially; never rebuild shared tools while another
suite owns processes. The minimal development driver edits an isolated source copy or a
supplied disposable package consumer, then restores its edits. It uses public inspection,
never `--probe`, a report export, or a fixture UI snapshot.

```powershell
pwsh -NoProfile -File eng/Test-MinimalApplication.ps1
pwsh -NoProfile -File eng/Test-DevelopmentReadiness.ps1
# Review updated.png and restarted.png in its printed evidence directory.
dotnet publish samples/Spar.NativeSample -c Release -o artifacts/minimal/publish --disable-build-servers
pwsh -NoProfile -File eng/Test-ProductionBoundary.ps1 -MinimalApplication -Assembly samples/Spar.NativeSample/bin/Release/net10.0-windows/win-x64/Spar.NativeSample.dll
pwsh -NoProfile -File eng/Test-MinimalPublication.ps1 -PublishDirectory artifacts/minimal/publish -VerifyRender
pwsh -NoProfile -File eng/Pack-Local.ps1
pwsh -NoProfile -File eng/Test-MinimalConsumer.ps1 -VerifyRender
pwsh -NoProfile -File eng/Test-VSCodeDevelopment.ps1 -MinimalApplication -VerifyRender
```

The publication test reads the generated build receipt, validates the copied AOT payload,
retains native browser processes, closes the window and verifies their actual exit and
profile release. The application emits no test receipts. Without `-VerifyRender`, CI
checks lifecycle and payload only; with it, review `published.bmp` for the typed greeting.
The [helper guide](22-integration-test-helpers.md) lists the comprehensive fixture suites.
Clean-machine qualification remains deferred.

The [local execution record](evidence/minimal-application-2026-10-21/README.md) binds results to implementation hashes, records first failures, and separates new minimal-app checks from historical acceptance.
