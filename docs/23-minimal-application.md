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
- `Program.Development.cs` uses the optional `DevelopmentApplication.Run` helper to
  connect to the owning coordinator and run the native window. The same helper supports project F5.
- `Program.Production.cs` loads the embedded assets and runs the native window.
- `frontend/src/App.tsx` displays typed results, local state and development diagnostics.
  Backend updates invalidate reads; mutations are never replayed automatically.

Generate bindings by building the application; do not handwrite dispatchers or edit
`frontend/src/generated`. The separate contract assembly stays immutable during Hot Reload.

The Debug entry point calls the helper synchronously on its `[STAThread]` thread:

```csharp
DevelopmentApplication.Run(
    title: "Spar development",
    fingerprint: new SampleBackendBridge(null!).Fingerprint,
    contractType: typeof(ISampleBackend),
    createBridge: CreateBridge);
```

`CreateBridge` retains the application's backend and explicit permission grants. The helper verifies
the owning session and compiled/generated contract before invoking that factory. It uses the session's
loopback content and profile, enables readiness, and enables inspection only when the owner requests it.
It waits for the native window's actual completion, disposes the connection, then completes any project
bootstrap. It does not wait for the coordinator to exit inside the application.

Existing applications need no migration. Keep `DevelopmentApplication.Connect` and `SparApplication.Run`
for custom window options or callbacks, as the integration fixture does. Reference development tooling
only in Debug and keep `Program.Development.cs` excluded from Release.

## Prerequisites

Use Windows x64, .NET SDK `10.0.402` (selected by [global.json](../global.json)),
Node `24.21.0` / npm `11.19.0`, PowerShell 7.4 or newer and installed WebView2 Runtime.
NativeAOT publication also requires Visual Studio C++ native build tools and the Windows SDK.
These are the selected baseline, not a supported-machine matrix. See the [native host guide](09-native-host.md)
for runtime distribution and tested-machine limits. Frontend dependency versions and lock files live with each app.

## Start, edit and publish

From the checkout root:

```powershell
dotnet build tools/Spar.Dev --disable-build-servers
Push-Location samples/Spar.NativeSample/frontend
npm ci --ignore-scripts --include=optional
Pop-Location
./tools/Spar.Dev/bin/Debug/net10.0-windows/Spar.Dev.exe samples/Spar.NativeSample/spar.dev.json
```

Edit `frontend/src/App.tsx`, `frontend/src/styles.css` or the greeting in `SampleBackend.cs`.
The configured watcher owns backend Hot Reload; Vite owns frontend HMR. Use `restart` for a coordinated
rebuild/restart, and `stop` or Ctrl+C to end the session. VS Code's **Spar: development session** task
starts this app with a terminal owner. Stop it before starting an editor-owned session or publishing.

```powershell
dotnet publish samples/Spar.NativeSample -c Release -o artifacts/minimal/publish --disable-build-servers
```

Copy the output directory and run `Spar.NativeSample.exe`. The SPA assets are embedded; Node and a frontend
server are unnecessary. The installed WebView2 Runtime remains required. See [production delivery](16-production-delivery.md)
for payload/boundary rules and [compatibility](17-dependency-compatibility.md) for an attributable CoreCLR choice.

### Editors

Use the prerequisites and commands above. The coordinator builds
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
edits, close, wait for cleanup, rebuild and F5. The
[startup-helper record](evidence/development-startup-2026-10-21/README.md) supplies actual source/package
minimal-app Visual Studio acceptance, with native captures and retained process exits.

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
