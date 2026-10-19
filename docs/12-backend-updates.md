# Backend updates in the native SPA session

Date: 2026-10-19. M1.5 terminal/AI watcher lane, including explicit startup/dependency input policy and selected full-build activation. This extends the [owned SPA session](11-development-session.md) and uses the same native consumer. Debugger-owned lanes remain #8/#9 work.

## Run and edit

The sample's `spar.dev.json` selects `watchBackend: true`. Use the existing prerequisites and start command:

```powershell
dotnet build tools/Spar.Dev --disable-build-servers
./tools/Spar.Dev/bin/Debug/net10.0-windows/Spar.Dev.exe samples/Spar.NativeSample/spar.dev.json
```

Install frontend dependencies as described in the [SPA guide](11-development-session.md#start-and-edit). VS Code's development task uses this configuration too. It owns a watcher session; it does not attach the C# debugger. Never attach a second update engine to that application.

Edit the greeting expression in `SampleBackend.GreetAsync`. .NET applies a supported patch; a runtime notification reaches the SPA, which calls the read-only greeting operation again. The window, document, local counter/text, backend counter, and application process remain. CSS/component HMR still belongs to Vite/React. The sample excludes `frontend/**` from .NET's default items so frontend changes do not drive backend rebuilds.

An invalid method edit displays the compiler's file/line diagnostic and **Edit not applied** in the SPA. Its existing process continues previous behavior. Correcting that edit can patch the same process. A callback revision identifies an observed runtime update, not an editor save or a transaction over all source files. Application code can execute updated methods before the callback arrives; status and read refresh do not promise atomic source activation. Mutations are never retried by the update hook.

When the pinned watcher requests restart for an unsupported edit, Spar closes admission and requests native shutdown. It waits for actual application completion, stops that watcher's owned helpers, builds matching outputs, and starts another generation while retaining Vite. In-process revisions and process generations are separate identities. Old-generation UI/update frames cannot restore readiness in the replacement.

The export interface, DTOs, generated dispatcher, and generated JSON metadata live in the sample's separate `Spar.NativeSample.Contracts` project. A normal full build references that project. The watch workspace instead references the application's existing copied contract DLL, excluding its source project from delta compilation. Spar watches the explicit `contractFile` input and owns a full rebuild/restart when it changes. This prevents a DTO delta from changing wire metadata before an eventual callback: the observed delta engine accepted a record constructor change even though a fresh compile failed at an unchanged call site. `Watch=false` alone did not remove compile items from the inspected SDK's hot-reload workspace, so it is not used as a safety boundary.

Before native admission, development-only reflection computes the compiled contract's schema fingerprint and compares it with both generated wire metadata and the coordinator's frontend fingerprint. A mixed generated/compiled schema fails admission. Two managed tests compare actual generator output with compiled contracts, including nullable fields and an unused DTO. This does not copy service graphs or redefine production serialization metadata. Contract-type callbacks remain a conservative restart fallback; they are not the boundary that prevents source contract patches.

If a full rebuild fails after the old app has stopped, it remains **stopped**, with build diagnostics retained. Correct a monitored backend source input to automatically build and start a usable generation. `restart` also retries explicitly. There is no second running backend, rollback store, or silent launch of stale/mismatched full-build output. A fresh application proves its compiled fingerprint against the generated frontend before typed readiness; a stale fingerprint is rejected before handler entry.

Type `restart` to request a full build/relaunch in watch mode. Startup/configuration and dependency inputs use the policy below to request this path automatically. Setting `watchBackend: false` preserves the earlier manual launch mode: no watcher, and `restart` relaunches the existing compiled executable. That setting alone is not a complete debugger adapter.

## Restart inputs and full-build selection

The coordinator monitors the application project directory and the separate contract source directory. Its bounded manifest includes `.cs`, `.csproj`, `.props`, `.targets`, `.json`, and `.config` files. It excludes `bin`, `obj`, `node_modules`, `.git`, the frontend working directory, the session evidence directory, and the loaded session configuration. It does not descend through junctions or other reparse points. Ancestor `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, `global.json`, and `NuGet.Config` paths are included even when initially absent.

- `restartFiles` names initialization sources or additional inputs that require process replacement. The sample lists `Program.cs` and `Program.Development.cs`; applications must also list files containing their own initialization or dependency-registration code. C# syntax alone cannot reliably classify whether a method has already run.
- `backendSourceDirectories` adds source roots for application-owned project dependencies outside those two default directories. Project/build/configuration changes in these roots request a full rebuild. Ordinary implementation `.cs` changes continue through the SDK patch engine while the application is ready.
- Contract-directory inputs request full rebuilds, preserving the separate compiled-contract boundary. For custom imported build files, native DLLs, or other inputs outside the manifest, list the exact paths in `restartFiles`. This is an explicit consumer input boundary, not a general MSBuild graph evaluator.

The coordinator configuration is loaded once. Changing frontend commands, source-root lists, or other `spar.dev.json` settings requires stopping and starting the session. Existing watch sessions do not silently adopt new ownership settings. Watch mode rejects `--no-build`, because a previously compiled executable has no verified selection for the current source manifest. Manual non-watch mode retains its existing no-build behavior.

Each full build selects an input hash and filesystem notification revision after a 300 ms settling interval. Notifications also invalidate an edit that was reverted to its original bytes during a build. After the build exits, a changed selection yields `build-superseded`; neither that build's successful output nor its failure becomes current. The coordinator waits for the actual build process to finish before starting the next attempt. It checks selection again after watcher startup and before sending native configuration, and before accepting initial UI readiness. Superseded attempts consume generation IDs, so gaps are expected.

Events carry `buildSourceHash` and `sourceRevision` for the selected full build. These identify build provenance, not a transaction over later in-process Hot Reload patches. Runtime update revisions remain separate. `source-selected`, `build-selected`, and `build-superseded` distinguish selection, completion, and discarded work. A selected failed rebuild emits a bounded `build-diagnostic` preview with file/line errors and `backend-stopped/build_failed`; a subsequent monitored source change retries automatically. The initial build still fails the whole startup if it cannot produce a consumer and generated bindings. An invalid SDK or failed application startup also remains a session failure.

Limits are 32 explicit restart files, 16 additional source roots, 2,048 source files, 8,192 traversed entries, 32 directory levels, 4 MiB per input, and 32 MiB total. Atomic-save/sharing conflicts remain unsettled and cannot authorize activation. A notification overflow ends the session rather than silently treating an unknown revision as current. No immutable source checkout or transactional rollback of SDK patches is promised.

## Update owner and retirement

The coordinator launches the public `dotnet watch ... run` command inside a nested Windows job owned by the session job. The watcher supplies the startup agent and applies deltas. Spar never reads its private patch pipe or generates compiler deltas. The public `MetadataUpdateHandler` callback in `Sotsera.Spar.Development` supplies positive runtime notifications, coalesced by the coordinator with monotonic revision checks before SPA publication.

Automatic SDK restart is disabled: no `--non-interactive`, `DOTNET_WATCH_RESTART_ON_RUDE_EDIT=0`, and `HotReloadAutoRestart=false`. Redirected input cannot answer the restart prompt. The SDK's public console prompt requests Spar's own retirement path; Spar never confirms it inside the SDK. Explicit `--verbose` also avoids the inspected SDK's quiet-option prompt path, which can auto-confirm a restart. A separate version-specific safeguard overrides inherited `DOTNET_WATCH_LOG_LEVEL` with `Debug` so logger overrides cannot hide compiler diagnostics/readiness. The real test deliberately inherits `Critical` to exercise diagnostic visibility while verifying that retirement still waits.

The adapter verifies SDK `10.0.402` before each watcher launch. It uses English CLI output, a bounded log tail, the known restart prompt, and ordinary compiler diagnostic lines. This is an explicitly pinned console-output adapter, not a stable compiler/debugger status API. Unknown SDK versions fail with `watch_sdk_not_verified`; do not upgrade the pin without repeating the integration tests. Unknown/new message forms are not interpreted as successful patches. Actual runtime notifications plus typed observations establish successful behavior, not source saves or log text alone.

Retirement keeps draining lifecycle receipts while waiting for actual exit and control-reader EOF. Cancellation/timeout does not release native/backend ownership. Retired UI frames are suppressed, while completion receipts remain observable. Only **after** the app has exited may its nested job terminate remaining SDK/browser helpers. `watcher-stopped` labels this as `owned_tool_stop`; it does not claim those helpers exited gracefully. The overall graceful receipt means application work was completed before cleanup. The frontend's separate scope survives a planned replacement.

For an operation that ignores cancellation, a normal restart waits. A second Ctrl+C or `{"command":"stop","force":true}` explicitly ends owned work instead; it is recorded as forced cleanup and starts no replacement. The session's non-inherited job handles still contain watcher/app descendants if the coordinator or editor disappears. No process is killed by executable name.

`DevelopmentApplication.Connect(fingerprint, typeof(ISampleBackend))` supplies the explicit compiled contract. The adapter, callback, reflection, watcher, and console parser stay outside Release application references. The contract project's generator reference is build-only and removes inherited framework/RID/AOT properties. The legitimate contract/dispatcher assembly remains an application dependency and is compiled into NativeAOT. Strict AOT publication remains a separate gate.

## Verification

```powershell
dotnet build Spar.slnx --disable-build-servers
dotnet test --solution Spar.slnx --no-build
pwsh -NoProfile -File eng/Test-BackendUpdates.ps1 -VerifyRender
pwsh -NoProfile -File eng/Test-DevelopmentSession.ps1
pwsh -NoProfile -File eng/Test-VSCodeSession.ps1 -WatchBackend
```

The backend driver copies the ordinary consumer and frontend into an isolated layout, references public framework APIs, and edits only those copies. Its first scenario covers frontend HMR beside watch, same-process greeting patches and read refresh with retained state, CS0103 recovery, rapid method edits, unsupported-edit retirement around a 12-second cancellation-ignoring operation, failed contract rebuild staying stopped, automatic correction into matching contracts, stale-fingerprint rejection, and preservation of the original frontend process. A separate scenario reruns initialization, rebuilds a referenced dependency, and gates real MSBuild completion while newer valid/invalid source is saved. It rejects obsolete successful and failed builds, then recovers from the selected compiler failure without a command. The final scenario explicitly forces shutdown during retirement. An unrelated Node process must survive all three flows, and retained process handles/creation times must actually end.

The 12-second limit exists only in the isolated test consumer; the repository sample retains its normal delay bound. Pixel verification plus review of the captured native window establishes visible behavior. The CI lane runs these same actual watcher/WebView operations without claiming desktop pixel evidence. The editor driver verifies task/window ownership with watch mode enabled; it does not establish debugger F5, breakpoints, or debugger restart.

See [retained evidence](evidence/backend-updates-2026-10-19/README.md) for commands, snapshots, hashes, process sequencing, and exact scope. The [restart-policy evidence](evidence/restart-policy-2026-10-19/README.md) records the explicit input policy and deterministic full-build races that extend that first delivery. Installed debugger lanes remain open. Source selection covers the declared inputs; it does not infer arbitrary MSBuild dependencies or make in-process patches transactional. Implementation-update callbacks remain eventual notifications; this slice does not claim transactional rollback of arbitrary .NET patches.

## Source review and first failures

No upstream implementation was copied. Public .NET callback and CLI behavior informed this authored adapter. The inspected SDK is tag `v10.0.402`, revision `d037e5f857b35e7260f575ae026ef30511776c0f`, in the MIT-licensed [dotnet/sdk](https://github.com/dotnet/sdk/tree/v10.0.402/src/Dotnet.Watch) source. Relevant paths are `Watch/HotReload/HotReloadDotNetWatcher.cs`, `ProjectUpdatesBuilder.cs` in that directory, `Watch/UI/RestartPrompt.cs`, `ConsoleInputReader.cs`, `Watch/Process/ProcessRunner.cs`, `Watch/Build/ProjectGraphUtilities.cs`, and `dotnet-watch/Program.cs`. This review established the timeout/force behavior to avoid, the project restart preference, and the inherited logging hazard. It is not a support promise for other SDK releases.

References: [dotnet watch CLI](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-watch), [MetadataUpdateHandler](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.metadata.metadataupdatehandlerattribute), and [the earlier Reactor/NeoAstra/Tauri research](06-backend-reload-research.md). No private debugger service, SDK test-input hook, or custom delta engine is used.

Failed runs are preserved under ignored `artifacts/backend-loop`: the watcher's forwarded framework caused NETSDK1005 in the generator reference; retirement initially dropped queued completion receipts; and the record test incorrectly expected a full-compilation diagnostic from the delta engine. An intermediate fixture also placed SDK logs/browser profiles inside its watched project, creating noisy self-observation; final fixtures keep evidence outside the source root. These failures drove the reference isolation, draining, contract classification, and fixture layout fixes. None is counted as a pass.
