# Owned SPA development session

This integration guide exercises the explicit acceptance application under `fixtures/`. For ordinary application startup and publication, start with the [minimal application guide](23-minimal-application.md). Historical captures retain their original paths.

This guide owns SPA/session configuration, authority and process lifetime. The default fixture configuration enables the [backend watcher](12-backend-updates.md); manual mode uses `watchBackend: false`. The [VS Code](13-vscode-development.md) and [Visual Studio](14-visual-studio-development.md) lanes instead assign Hot Reload to the editor.

## Start and edit

Use the [selected prerequisites](23-minimal-application.md#prerequisites). The commands below prepare the instrumented fixture, while the ordinary app has its own [quick start](23-minimal-application.md#start-edit-and-publish).

From the repository root, prepare the development tool and frontend dependencies:

```powershell
dotnet build tools/Spar.Dev --disable-build-servers
Push-Location fixtures/Spar.NativeSample/frontend
npm ci --ignore-scripts --include=optional
Pop-Location
```

Then one start action builds the consumer and contracts, starts the configured SPA command, verifies its actual readiness, and opens the native application:

```powershell
./tools/Spar.Dev/bin/Debug/net10.0-windows/Spar.Dev.exe fixtures/Spar.NativeSample/spar.dev.json
```

VS Code’s **Spar: development session** task targets the ordinary `samples/` application and proves terminal ownership. It is separate from the [integrated F5 debugger workflow](13-vscode-development.md). Use the explicit fixture command above for these acceptance scenarios.

Edit `fixtures/Spar.NativeSample/frontend/src/styles.css` or `App.tsx`. Vite and the React plugin perform normal CSS HMR/Fast Refresh. Supported local state survives. Backend mutations run only when requested; HMR does not replay them. Changing Vite/server configuration currently ends the session; start it again after that change.

Type `status`, `restart`, or `stop`, followed by Enter. The first Ctrl+C requests graceful stop; a second requests explicit forced termination. Closing the native app normally ends the session. In the default watcher configuration, restart rebuilds current sources after retirement and activates matching outputs; [backend updates](12-backend-updates.md) owns the input/recovery policy. In manual mode (`watchBackend: false`), restart launches the same compiled executable with a new generation/profile and does not rebuild C#. Vite survives a coordinated backend replacement.

The initial implementation waits for verified frontend readiness before opening the window. There is no waiting-window UI yet. An instrumented fixture Debug launch without `--spar-dev` opens the local bridge probe; development coordination is explicit.

## Configuration and generated output

[`spar.dev.json`](../fixtures/Spar.NativeSample/spar.dev.json) resolves paths relative to itself. Configure the consumer project, built executable, generated JavaScript path, session output directory, frontend executable/arguments/working directory/environment, and startup/shutdown limits. Startup defaults to 30 seconds; helper shutdown defaults to 10 seconds. Configuration is limited to 64 KiB and rejects unknown fields, invalid limits, and malformed command/environment inputs.

The reference command is `cmd.exe /d /c npm run dev`; package scripts are not assumed to be native executables. Another package manager can be configured explicitly. Port `0` asks Vite for an available port; a fixed port is optional and uses strict binding. The adapter reports the actual bound address through its private control connection. There is no fixed-port polling or scraping of terminal output. A detected conflict, timeout, unexpected frontend exit, application launch failure, contract mismatch, and denied client capability have distinct event codes.

The contract target can copy `client.js` and `client.d.ts` to `SparClientOutputDirectory`. The sample imports these ignored generated files from `frontend/src/generated`. Build the C# consumer before TypeScript checks. Unchanged generated outputs are not rewritten. The ordinary C# compile still validates the application after generation. Contract changes use the [coordinated rebuild/restart path](12-backend-updates.md); they are never patched into the live contract assembly.

The frontend baseline is React/React DOM `19.3.1`, their types `19.3.0`, Vite `8.3.4`, React plugin `6.1.2`, and TypeScript `7.0.2`. Direct versions are explicit and `package-lock.json` records the graph. CI uses `npm ci`, including optional platform bindings. This sample and its adapter do not make React or Vite a runtime requirement for Spar.

## Authority and readiness

`Sotsera.Spar.Windows` exposes explicit numeric-loopback document authority through `SparLoopbackContent`. It does not launch processes or depend on the development library. GET resources must remain in that origin/private path; the development CSP constrains WebSocket access to the actual Vite endpoint. Existing native navigation identity, document token, explicit operation permissions, and contract fingerprint still apply. A framework full-page reload retires admission and drains backend work before receiving a fresh native document URI; component/CSS HMR keeps the current document.

The coordinator creates separate random values for its control token, private HTTP path, and renderer bridge capability. It authenticates control peers with CurrentUserOnly named pipes, native client PID, retained process handle/creation identity, and membership in its owned job. It verifies the server's private HTTP readiness proof before configuring the application. URLs/capabilities are never included in coordinator events. The sample proves its capability during bridge hello before backend admission. This is local development isolation, not a sandbox against code already running as the same Windows user.

`application-ready` requires a matching compiled/generated fingerprint, native document-bound bridge connection, successful typed greeting, and consumer-reported DOM commit followed by animation frames. This receipt is **not proof that desktop pixels rendered**. The interactive probe additionally checks actual pixels and requires visual review. Losing application control withdraws readiness without treating EOF as process exit.

## Lifetime and structured control

Development-only projects own the coordinator and app adapter. The sample references `Sotsera.Spar.Development` only in Debug and excludes `Program.Development.cs` from Release. Build/inspection/control code and Node dependencies do not enter the production payload.

The coordinator owns a non-inherited Windows job with `KILL_ON_JOB_CLOSE`. Children join atomically through `PROC_THREAD_ATTRIBUTE_JOB_LIST` at creation, before user code runs; stdio handles use an explicit inheritance whitelist. npm helpers, native application, and WebView descendants remain owned. The kernel safeguard applies if the coordinator crashes. No process is located or killed by executable name. Observations retain handles and PID plus creation time; short-lived processes may exit before independent observation, so logs are not an exhaustive process history.

Ordinary stop requests native close and waits for actual backend/application completion. A restart never overlaps old/new application ownership. A timeout cannot authorize abandoning an application operation. After the application ends, the frontend is asked to stop; remaining owned helpers can be terminated after the configured shutdown limit, with an explicit forced receipt. Abrupt owner loss is a separate forced policy. A blocked application can require a second Ctrl+C or `{"command":"stop","force":true}`; it is not reported as graceful completion.

Automation sends bounded UTF-8 JSON lines on stdin:

```json
{"command":"status","id":"inspect-1"}
{"command":"restart","id":"restart-1"}
{"command":"stop","id":"stop-1","force":false}
```

Stdout events identify session, generation, timestamps, process creation identity, contract/document, and command correlation when applicable. Child logs live in the session directory. `--events-file <new-path>` mirrors events to an exclusively created, shared-readable file. `--no-build` requires an already matching build. `--owner-stdin` makes EOF or invalid owner input end ownership with the forced policy; use it for editor/AI pipe ownership. Without it, stdin EOF alone does not end a session.

Test-only `--probe` enables the fixture's fixed seed/close HMR events. General UI automation uses the separate, explicit [inspection capability](15-development-inspection.md). Optional memory observation is still a proposal. External frontend servers are not supported by this adapter; an explicit observed/non-owned mode is needed before supporting that scenario.

## Verification and evidence

```powershell
dotnet build Spar.slnx --disable-build-servers
dotnet test --solution Spar.slnx --no-build
Push-Location fixtures/Spar.NativeSample/frontend
npm run check
npm run build
Pop-Location
pwsh -NoProfile -File eng/Test-DevelopmentSession.ps1 -VerifyRender
pwsh -NoProfile -File eng/Test-VSCodeSession.ps1
```

The session driver edits isolated fixtures and runs actual Vite/React/WebView processes. Its ten scenarios cover the initial owned build, HMR/state retention, planned replacement with the original frontend retained, native close, owner EOF, malformed owner UTF-8, abrupt coordinator termination with long-lived helper/grandchild processes, frontend death, application crash, occupied port, startup timeout, missing application, and incorrect renderer capability. Expected failures must include successful actual cleanup. An unrelated Node process must survive every session scenario. The VS Code driver uses public task APIs in isolated profiles and separately checks task termination and editor window close; this does not establish C# debugger Hot Reload.

The [CI coverage map](21-continuous-integration.md#coverage-map) defines the selected hosted checks. VS Code window tests and pixel review remain separate desktop checks. Normal Release publishing builds and packages the SPA as described in [production delivery](16-production-delivery.md).

Historical M1.4 results, source hashes, process receipts and reviewed before/after captures are in [the dated evidence record](evidence/development-session-2026-10-19/README.md). Later integrated acceptance is reconciled in the [milestone matrix](19-milestone-1-acceptance.md); current qualification limits belong to [status and deferred work](README.md#status-and-deferred-work).

First failures remain under ignored `artifacts/development`: omitted configuration defaults failed with source-generated `init` property construction, a local npm install lacked the optional Windows Rolldown binding, an earlier driver checked final output before stdout reached EOF, and the new crash test initially collided with a typed PowerShell parameter name. Configuration setters preserve defaults with a regression test, locked `npm ci --include=optional` supplies platform dependencies, the driver drains actual EOF, and the test uses a separate event variable. Failed runs are not counted as passes.

## Source and license provenance

This slice is authored afresh; no implementation was copied from Tauri, Reactor, NeoAstra, or old Spar. Interop declarations follow the installed Windows SDK `10.0.26100.0` headers (`um/winbase.h` job-list attribute; `um/winnt.h` job structures) and public APIs: [job objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects), [process attributes](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute), and [named-pipe client identity](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeclientprocessid). No SDK implementation is vendored. Existing [WebView2 notices](09-native-host.md#webview2-source-and-license-provenance) ship unchanged.

The adapter uses [Vite's plugin API](https://vite.dev/guide/api-plugin.html) and [server options](https://vite.dev/config/server-options.html); editor tests use the [VS Code task API](https://code.visualstudio.com/api/references/vscode-api#tasks). Pinned direct npm metadata declares React, React DOM, their types, Vite, and the React plugin MIT; TypeScript declares Apache-2.0. Installed package licenses and the locked transitive graph belong to the development toolchain. Frontend distribution retains applicable notices through the [production notice build](16-production-delivery.md).
