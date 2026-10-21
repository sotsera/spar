# VS Code development adapter

The repository F5 configuration targets the ordinary `samples/` app. The comprehensive debugger driver uses the instrumented `fixtures/` app by default; `-MinimalApplication` selects the ordinary app. Historical captures retain their original paths.

The local `tools/Spar.VSCode` extension connects ordinary C# Dev Kit debugging to the same coordinator used by the terminal lane. It uses public VS Code configuration providers, debug-session events, adapter trackers, diagnostics, and `startDebugging`/`stopDebugging`. It does not implement a debugger or a Hot Reload compiler.

API references: [VS Code debug configuration providers](https://code.visualstudio.com/api/references/vscode-api#DebugConfigurationProvider) and [C# debugger settings](https://code.visualstudio.com/docs/csharp/debugger-settings). The installed-version evidence below verifies the composed behavior; API availability alone does not prove it.

## Start and operate

Use the [selected prerequisites](23-minimal-application.md#prerequisites), an interactive Windows desktop with WebView2, VS Code 1.142.0, C# 2.160.4, and C# Dev Kit 3.40.210. This is a local development extension, not a published marketplace package.

Build `Spar.slnx` and install the sample frontend dependencies as described in the [minimal application guide](23-minimal-application.md#start-edit-and-publish). Open a development-extension window from the repository root:

```powershell
code --new-window --extensionDevelopmentPath "$PWD/tools/Spar.VSCode" "$PWD"
```

Enable `csharp.experimental.debug.hotReload` and `csharp.debug.hotReloadOnSave` in **user** settings. Dev Kit declares these settings at machine scope; workspace settings do not enable them. Select **Spar: native development (local adapter)** and press F5. Set a breakpoint in `SampleBackend.GreetAsync`, continue, and edit its greeting or the frontend CSS. The application must report matching typed readiness; a successful debugger launch alone is insufficient.

The adapter explicitly selects debugger ownership even though the shared sample configuration defaults to terminal watch mode. It starts no `dotnet watch` process. Dev Kit supplies patches; the existing metadata callback tells Spar to refresh frontend read data. Mutations are never replayed. Spar readiness is not a guarantee that Dev Kit has finished initializing Hot Reload; allow the editor to settle before the first save.

Use **Spar: Restart backend** (also available from the Spar status item) after an unsupported edit. Spar retires the old bridge/work, waits for actual process exit, builds current sources, and starts a new ordinary C# debug session. Vite remains alive. Startup and contract edits also request this coordinated path. A failed replacement build stays stopped until a source correction triggers another build. Breakpoints remain ordinary VS Code breakpoints across sessions.

Use the debugger's **Stop**, or **Spar: Stop development session**, to force termination of owned work and processes. This is explicitly recorded as forced cleanup; it is different from coordinated retirement. Native window close uses the ordinary graceful application-close path. The debugger toolbar's Restart is not the coordinated restart command: do not use it to preserve retirement semantics. The adapter treats a debugger restart/disconnect request as a stop when it observes one; only Spar's command supplies the verified relaunch flow.

The **Spar development** output retains structured public events; the status item distinguishes ready, rejected edits, updating/restarting, and failed builds. C# diagnostics retain their normal source navigation in Problems. Full-build compiler diagnostics are also placed in the **Spar build** diagnostic collection with file/line/column. Unsupported Hot Reload edits may be reported only in Dev Kit's **.NET Hot Reload** output: inspect that output and invoke the explicit Spar restart command. Spar does not parse Dev Kit's private logs or certify its interactive modal decisions.

## Ownership and immutable contracts

The coordinator first builds and starts the authenticated frontend. A private stdout handoff gives the editor the application project, generation, arguments, and capability environment. These capability frames are excluded from `--events-file` and extension output. Do not record the raw debugger-owner stdout stream or resolved launch configuration as public evidence.

The `spar` F5 configuration is a launcher: it starts the coordinator and ends its own configuration resolution, then uses `startDebugging` for an ordinary `dotnet` session when the private handoff arrives. During the build, use **Spar: Stop development session** to cancel. This avoids registering a competing `dotnet` configuration resolver whose order relative to Dev Kit would change behavior.

Dev Kit resolves the real `dotnet` launch settings to `coreclr`. The final public configuration provider restores Spar's arguments/environment after that resolution. On application hello, the coordinator verifies the actual pipe-client PID, capability, executable path, and creation time, then assigns the process to its kill-on-close Windows job before issuing configuration. The ordinary sample's `DevelopmentApplication.Run` helper calls `Connect` before constructing the backend or native window; the integration fixture retains its explicit startup. Debugger-paused startup suspends the readiness deadline; a timeout never authorizes overlap with retiring execution.

Contracts are separately compiled with `DebugType=none` in Debug. The development adapter rejects contract/DTO assemblies containing CodeView or embedded portable PDB entries before admitting the application. This deliberately removes contract debugging/Hot Reload: changes must rebuild the contract, generated glue/client, and application, then establish a new handshake. Runtime implementation assemblies retain ordinary symbols and Hot Reload. Release/AOT settings are unchanged.

The sample also disables Git source-root mapping in Debug to keep compiled source paths consistent with the local editor workspace. The extension normalizes the Windows drive letter used for launch. These are tested local settings, not a general SDK template or a guarantee for every consumer project layout.

## Verification and limits

```powershell
dotnet build Spar.slnx --disable-build-servers
dotnet test --solution Spar.slnx --no-build
pwsh -NoProfile -File eng/Test-VSCodeDevelopment.ps1 -VerifyRender
```

The test copies source/contracts/frontend into an isolated fixture and launches the actual installed extensions in an Extension Development Host. It exercises backend breakpoints before and after relaunch, frontend HMR, debugger-owned method patch, state retention, compiler rejection/recovery, source-located full-build errors, delayed retirement, a failed contract rebuild and correction, debugger Stop, and graceful native close in a second session. A separate PowerShell supervisor retains OS process handles and checks actual exit. Fixtures contain a `node_modules` junction; do not recursively delete them as routine cleanup.

`-VerifyRender` requires accent pixels from the actual native window and a retained BMP for visual review. Without it, the receipt explicitly says rendering was not exercised. A two-second settling interval after initial readiness and the CSS edit avoids saving immediately into Dev Kit initialization; this is a harness interval, not a public Hot Reload-ready signal. Failed attempts remain under `artifacts/vscode-development` and must not be counted as passing evidence.

Hosted coverage is defined by the [CI policy](21-continuous-integration.md); the installed interactive debugger lane remains separate local evidence. [Visual Studio](14-visual-studio-development.md) and [inspection](15-development-inspection.md) have their own procedures. Broader project layouts and interactive rude-edit dialogs are not certified by this harness. See [current status](README.md#status-and-deferred-work) for qualification limits.
