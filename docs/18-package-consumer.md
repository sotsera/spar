# External package consumer

Local packages `0.1.0` expose the runtime, contract generation, build integration and development coordinator. They are a milestone baseline, not a public NuGet release. The [execution record](evidence/package-consumer-2026-10-20/README.md) retains package and external VS Code acceptance; the [Visual Studio walkthrough](evidence/milestone-acceptance-2026-10-20/README.md) completes the other editor lane. The [milestone matrix](19-milestone-1-acceptance.md) links the integrated evidence and remaining release-readiness work.

The default scaffold now uses the ordinary [minimal application](23-minimal-application.md), with greeting/counter operations and no probe reporting. Historical editor acceptance above exercised the instrumented milestone application, now available explicitly with `-IntegrationFixture`.

## Prerequisites and a fresh application

Use the [selected prerequisites](23-minimal-application.md#prerequisites), including native build tools for publication. The generated `global.json` retains the selected SDK. Internet access is needed for ordinary SDK/runtime NuGet dependencies and the frontend lock file; Spar itself restores from the copied local feed.

From the framework checkout:

```powershell
pwsh -NoProfile -File eng/Pack-Local.ps1
pwsh -NoProfile -File eng/New-PackageConsumer.ps1 -Directory C:/work/SparConsumer
```

Choose a new directory outside the checkout. The script copies application-owned sources, five local packages, and the small VS Code adapter. It writes ordinary projects, a solution containing both app and contract projects, central explicit package versions, and source mapping that resolves `Sotsera.Spar.*` only from `.feed`. There are no framework project references, absolute checkout imports, directory junctions, or references to the framework's output folders. The sole application project reference points to its own contract project.

The scaffold command is a repository convenience, not a prerequisite for later builds. Its output is the inspectable integration example; the consumer can be moved with its `.feed`, then rebuilt. Regenerate the absolute paths in `backend/Properties/launchSettings.json` after moving it. Keep one canonical full directory path when opening the editor: mixing Windows 8.3 aliases with full project paths can make C# Dev Kit treat an edited file as a separate file-based program.

In `spar.dev.json`, the scaffold sets `frontend.workingDirectory` to `backend/frontend` and uses the existing
defaults for `fileName` (`cmd.exe`), `arguments` (`/d /c npm run dev`) and `port` (`0`, chosen dynamically).
Explicit values remain supported for a different frontend command or port. Paths remain relative to the
configuration file; backend watching, contract/startup files and output paths are still explicit.

From the consumer directory:

```powershell
dotnet build backend/Spar.NativeSample.csproj --disable-build-servers
Push-Location backend/frontend
npm ci --ignore-scripts --include=optional
Pop-Location
./.tools/Spar.Dev.exe spar.dev.json
```

The first direct application build restores the entire graph, generates C#/JavaScript/TypeScript bindings and prepares `.tools/Spar.Dev.exe` plus the Vite session adapter. No generated bindings or warmed NuGet cache are copied into the fixture. The terminal session builds before launch; edit `backend/frontend/src/styles.css` or `backend/SampleBackend.cs`, then use `restart` or `stop`. Supported updates retain document identity and state. A coordinated restart waits for actual retirement and resets backend state. See the [development ownership rules](12-backend-updates.md).

## Package responsibilities

| Package | Integration |
| --- | --- |
| `Sotsera.Spar.Bridge` | Public contract/transport APIs used by the consumer contract assembly. |
| `Sotsera.Spar.Windows` | Release native host, x64 WebView2 loader and its notices; inspection code is excluded. |
| `Sotsera.Spar.Windows.Development` | Debug native host, transitively selected by the development package; rejected in non-Debug builds. It has the same assembly identity as the Release host, so select only one per configuration. |
| `Sotsera.Spar.Development` | Debug-only public session/bootstrap APIs, coordinator files and frontend adapter. Reference conditionally with `PrivateAssets="all"`. |
| `Sotsera.Spar.Build` | Private build dependency containing contract generator, SDK Roslyn binaries, asset builder, delivery policy and receipt writer. No runtime library dependency. |

The app sets `SparApplication`, `SparFrontendDirectory`, and `SparContractProject`. It declares an ordinary project reference to the contract except during `DotNetWatchBuild`; package targets then use the immutable contract assembly already copied into the application output. The contract project sets `SparContractFile` and `SparClientOutputDirectory` and disables Debug symbols so editor Hot Reload cannot patch wire metadata in place.

Ordinary SDK restore properties (`EnableAotAnalyzer`, `EnableTrimAnalyzer`, conditional Release `PublishAot` and `SelfContained`) are declared in the application before the first restore. Package targets are not available early enough to establish the first restore graph on their own. Delivery policy still rejects inconsistent mode settings. `SparDevelopmentToolsDirectory` chooses the prepared coordinator directory; `SparSourceDirectory` bounds receipt source hashing. External receipts work without Git and omit caches/build outputs and reparse points.

Build/development tooling never belongs in the Release payload. The production boundary check examines the actual package host assembly before the copied application test. The separate Debug host package avoids depending on a consumer's build configuration to change an already compiled Release library.

## VS Code

Install the C# and C# Dev Kit extensions. After the initial build/npm restore, launch the copied local adapter from the consumer:

```powershell
code --new-window --extensionDevelopmentPath "$PWD/.vscode/spar-extension" .
```

Open `Consumer.slnx`, choose **Spar development**, set a breakpoint in `GreetAsync`, and press F5. Continue to typed readiness, remove the breakpoint, then edit the CSS and greeting. Enable C# Hot Reload on save as described in the [debugger guide](13-vscode-development.md). Use **Spar: Restart backend** for coordinated restart, and debugger Stop to end the owned session. C# Dev Kit is the sole backend Hot Reload owner; the terminal watcher is not running alongside it.

The reusable acceptance driver supports `eng/Test-VSCodeDevelopment.ps1 -ConsumerDirectory <fresh-consumer> -MinimalApplication -VerifyRender` for the default application. Omit `-MinimalApplication` only for a fresh consumer created with `New-PackageConsumer.ps1 -IntegrationFixture`. It deliberately edits the disposable consumer and uses an isolated profile. Do not run it on work you want to preserve. The recorded external pass used VS Code `1.142.0`, C# `2.160.4`, and C# Dev Kit `3.40.215`; it covers real breakpoints, updates, compiler recovery, restart, retirement and process exit. A failed or unavailable run does not establish acceptance.

## Visual Studio

Open `Consumer.slnx`, set `Spar.NativeSample` as startup project, select **Debug** and **Spar development**, then F5 with a breakpoint in `GreetAsync`. The initial build prepares the packaged coordinator; the ordinary project profile supplies its path. Continue to native readiness, remove the breakpoint, edit the frontend and greeting, and use **Apply Code Changes**. Visual Studio owns backend Hot Reload.

For startup, contract or unsupported changes, close the native window, wait for session cleanup, rebuild both consumer projects, then F5. Debugger Stop is forced termination, separately recorded from graceful retirement. The [Visual Studio guide](14-visual-studio-development.md) explains these differences from VS Code and the manual scenarios. The [external walkthrough](evidence/milestone-acceptance-2026-10-20/README.md) passed on Visual Studio Community 2026 18.11.0 Stable: actual F5/breakpoint, CSS HMR, backend Hot Reload, invalid-edit recovery, unsupported/contract rebuild and relaunch, and Shift+F5. All 39 retained process handles exited across three sessions, and all three profile leases reopened exclusively.

## Publish

Stop development before publishing. From the consumer:

```powershell
dotnet publish backend/Spar.NativeSample.csproj -c Release -o artifacts/aot --disable-build-servers
```

This restores/builds the frontend, generates matching contracts, embeds production assets and publishes NativeAOT by default. Copy the published payload to a separate directory and run `Spar.NativeSample.exe`; no frontend server or Node process is needed. `spar-publish.json` records actual/requested mode, tool versions, contract fingerprint, source and payload hashes. A developer machine run does not establish no-SDK-machine qualification.

For an actual incompatible dependency, place these properties **before** the application's conditional `PublishAot` setting:

```xml
<SparDeliveryMode>CoreCLR</SparDeliveryMode>
<SparCompatibilityDependency>Your incompatible dependency and version</SparCompatibilityDependency>
<SparCompatibilityReason>The attributable unsupported mechanism/diagnostic</SparCompatibilityReason>
```

Publish to a separate `artifacts/coreclr` directory. Use `-p:SparStrictAot=true` to require NativeAOT despite the persisted compatibility selection. The acceptance fixture adds a consumer-owned dependency which actually emits and executes `DynamicMethod` IL; strict publication fails at its IL3050 call and explicit CoreCLR publication runs that code through the typed greeting. See the [compatibility guide](17-dependency-compatibility.md). There is no automatic fallback.

## Verification and licenses

```powershell
dotnet build Spar.slnx --disable-build-servers
dotnet test --solution Spar.slnx --no-build
pwsh -NoProfile -File eng/Pack-Local.ps1
pwsh -NoProfile -File eng/Test-MinimalConsumer.ps1 -VerifyRender
pwsh -NoProfile -File eng/Test-ExternalConsumer.ps1 -VerifyRender
```

The minimal suite verifies the default scaffold through public inspection, updates/restart, Release exclusion and copied AOT execution. The comprehensive external suite explicitly selects the integration fixture and creates a new isolated directory, verifies its restore graph, runs frontend/backend updates and restart, checks Release exclusion, then executes copied AOT and dependency-driven CoreCLR publications. Review the native captures. CI runs the same package suite without desktop pixel assertions under the existing **Windows managed probe** check; it does not establish IDE or interactive rendering acceptance.

The package baseline contains newly authored Spar code. WebView2 loader provenance and license are unchanged from the [native host guide](09-native-host.md#webview2-source-and-license-provenance); the loader's license and notice follow both host packages into application outputs. The build-only generator redistributes the unmodified `Microsoft.CodeAnalysis.dll` and `Microsoft.CodeAnalysis.CSharp.dll` selected from SDK `10.0.402` (`Roslyn/bincore`), with that installation's `LICENSE.txt` and `ThirdPartyNotices.txt`. These are tooling binaries, not imported upstream source or runtime payload dependencies.

The generated consumer includes the same frontend notice build as the sample: complete locked React/React DOM/scheduler licenses and the emitted Vite helper's core license. The private build package requires `dist/frontend-LICENSES.txt`, embeds it with the assets, and copies it alongside AOT and CoreCLR publications. The [production guide](16-production-delivery.md) describes verification and obligations when replacing or extending the frontend. Broader package/release review and public-release readiness remain separate work.

The [separation execution record](evidence/minimal-application-2026-10-21/README.md) verifies both current package variants and both VS Code lanes. The scaffold clears inherited NuGet fallback folders as well as setting its private package cache. No new Visual Studio walkthrough or clean-machine pass is claimed.
