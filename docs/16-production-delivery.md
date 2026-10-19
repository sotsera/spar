# Production SPA delivery

Issue [#10](https://github.com/sotsera/spar/issues/10), milestone M1.7 / A14. The ordinary native sample now publishes the same React application used in development, with generated bindings and local assets embedded in the NativeAOT executable. [Execution evidence](evidence/production-delivery-2026-10-19/README.md) records the actual copied application, screenshots, tests, measurements, and limitations.

## Build and run

Build prerequisites remain the pinned .NET SDK 10.0.402, Node 24.21.0/npm 11.19.0, PowerShell 7.4 or later, and the Visual Studio C++/Windows SDK NativeAOT tools. The installed application requires Windows x64, WebView2 Evergreen Runtime, and a writable browser profile. Node, npm, the .NET SDK, and a frontend server are build/development prerequisites, not application processes or payload dependencies.

```powershell
dotnet publish samples/Spar.NativeSample -c Release -o artifacts/production/publish --disable-build-servers
pwsh -NoProfile -File eng/Test-ProductionBoundary.ps1
pwsh -NoProfile -File eng/Test-Production.ps1 -VerifyRender
```

The normal Release build resolves the contract project first, regenerates C#/JavaScript/TypeScript, restores the locked frontend dependency graph with `npm ci --ignore-scripts --include=optional`, type-checks/builds Vite output, and embeds its archive. It rebuilds the frontend deliberately rather than trusting a previous `dist` directory. Build/publish failures remain failures; no compatibility fallback is introduced. Use a normal publish with its build enabled.

Copy the non-PDB files from the publish directory to a directory outside the checkout and run `Spar.NativeSample.exe` there. The payload contains:

- `Spar.NativeSample.exe`, including the application, generated bridge, and production assets.
- `WebView2Loader.dll`, `WebView2-LICENSE.txt`, and `WebView2-NOTICE.txt`.
- `spar-publish.json`, identifying selected mode, configuration, RID, source revision and hashes, dirty-tree status, tool versions, archive hash, and payload hashes. Actual execution receipts independently report runtime mode.

Symbols are optional. The browser engine is installed separately; this is neither a single-process application nor an installer. The default persistent profile is `%LOCALAPPDATA%/Sotsera/Spar.NativeSample`. An optional first argument selects a different profile directory; profiles are leased exclusively and preserved on exit. `SPAR_WEBVIEW2_RUNTIME_FOLDER` selects an explicit runtime folder, primarily for a fixed-runtime deployment or the missing-runtime fixture. A redistributed fixed runtime has not been qualified. Missing WebView2 produces an error naming the Evergreen Runtime and native HRESULT.

## Public runtime and authority

`SparAssetBundle.Read(stream)` loads a caller-owned seekable ZIP. Pass the result through `SparWindowOptions.Assets`, with a bridge using the matching generated contract. Do not also set `Html` or `LoopbackContent`. The bundle contains `index.html`, `spar-contract.txt` holding the exact generated fingerprint, and canonical relative asset paths. The fingerprint file is consumed as metadata and never served. A mismatch fails before native creation; the generated client also verifies the backend fingerprint during its real handshake.

The runtime owns a bounded immutable copy: at most 256 files, 16 MiB per decompressed file, and 64 MiB total archive/decompressed content. It rejects duplicate names, empty/dot path segments, encoded/non-ASCII names, backslashes, query/fragment characters, and unsupported extensions. Supported MIME types cover HTML, JS, CSS, JSON, text, SVG, PNG, JPEG, WebP, ICO, WOFF, and WOFF2. No path is mapped to the filesystem. This initial contract deliberately excludes arbitrary asset providers and route fallback.

Each native document still receives a fresh `https://<random>.spar.invalid/index.html` address. WebView2 resource interception serves exact GET resources from that document's bundle, with MIME types, `nosniff`, and no caching. Unknown/query-alias/foreign resources are denied. A restrictive response CSP permits local scripts/styles/images/fonts and denies network connections, frames, objects, forms, and base-URL overrides. Resources never require DNS, a TLS connection, or a local HTTP server. Existing native top-level navigation, popup, permission, document retirement, and reply-delivery rules remain in force.

The build helper is framework-neutral: the sample configures its frontend directory and build command through its ordinary project/targets. No Vite, Node, compiler, or ZIP-build tool is a production runtime dependency. Packaging these targets for external NuGet consumers belongs to #12.

## Production boundary and test fixtures

The production entry point accepts no development or probe switches. It grants only greeting, counter, and delay permissions. The sample contract still contains test operations for its development/probe fixture, but production does not grant them. The production frontend omits HMR event listeners, diagnostic/report effects, and probe actions. Release host compilation excludes inspection APIs, callbacks, and scripts; production browser developer tools/context menus are disabled.

`Test-ProductionBoundary.ps1` inspects the actual Release PE metadata and embedded ZIP: no development/compiler assembly references, no probe document/resources, and no development frontend markers. `Test-Production.ps1` verifies copied payload hashes/mode, actual typed calls, rejected switches, missing-runtime diagnostics, retained-process cleanup, released profile leases, and separate host/browser memory samples. Three runs distinguish a fresh profile from reused profiles; these are small baseline observations, not performance promises. `-VerifyRender` additionally captures pixels, which require visual review. CLI completion alone does not prove that React has rendered a reply.

The old native/bridge probes are now an explicit **separate test build**, never the default production output:

```powershell
dotnet publish samples/Spar.NativeSample -c Release -p:SparNativeProbe=true -o artifacts/native/probe-publish --disable-build-servers
# Copy those fixture files separately, then use Test-NativeHost.ps1 and Test-Bridge.ps1.
```

`Test-Bridge.ps1` now also runs a packaged-asset fixture through the same public bundle API and real WebView. It exercises local CSS/JS, denied missing/query/foreign/metadata resources, typed calls, stale fingerprints, denied handlers, document retirement, navigation rejection, and close with unfinished work. The required **Windows managed probe** job checks both the production application and the separate AOT fixture. CI does not claim desktop rendering or manual UI interaction.

## Remaining work

Explicit dependency-driven CoreCLR compatibility and strict failure checks are documented in the [compatibility guide](17-dependency-compatibility.md), #11 (A15/A16). External packages and the fresh consumer walkthrough remain #12 (A18/A01). A machine without the .NET SDK/runtime was unavailable; developer-machine execution does not certify clean-machine installation. Supported-machine coverage, installers, signing, and updates remain outside this slice.
