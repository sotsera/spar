# Production SPA delivery

This integration guide exercises the explicit acceptance application under `fixtures/`. For ordinary application startup and publication, start with the [minimal application guide](23-minimal-application.md). Historical captures retain their original paths.

This guide owns asset publication, the shipped payload and production boundaries. Normal Release publishes the React application with generated bindings and embedded local assets. The commands below use the instrumented fixture; ordinary app commands are in the [minimal application guide](23-minimal-application.md#start-edit-and-publish). Historical [execution evidence](evidence/production-delivery-2026-10-19/README.md) is source-bound.

## Build and run

Build prerequisites remain the pinned .NET SDK 10.0.402, Node 24.21.0/npm 11.19.0, PowerShell 7.4 or later, and the Visual Studio C++/Windows SDK NativeAOT tools. The installed application requires Windows x64, WebView2 Evergreen Runtime, and a writable browser profile. Node, npm, the .NET SDK, and a frontend server are build/development prerequisites, not application processes or payload dependencies.

```powershell
dotnet publish fixtures/Spar.NativeSample -c Release -o artifacts/production/publish --disable-build-servers
pwsh -NoProfile -File eng/Test-ProductionBoundary.ps1
pwsh -NoProfile -File eng/Test-Production.ps1 -VerifyRender
```

The normal Release build resolves the contract project first, regenerates C#/JavaScript/TypeScript,
prepares the locked frontend dependency graph, type-checks/builds Vite output, and embeds its archive.
The dependency helper runs `npm ci --ignore-scripts --include=optional` initially and after a changed
manifest, lockfile, effective npm configuration, Node/npm identity, helper payload or installed file.
It hashes installed file contents before reuse; missing or modified files require a clean install.
Its intermediate receipt contains hashes, never raw npm configuration or credentials. Failed installs
cannot leave a successful receipt. `dotnet clean` removes the receipt, not `node_modules`.

It rebuilds the frontend deliberately rather than trusting a previous `dist` directory. Dependency
reuse does not skip frontend compilation, contract matching, notices or archive generation. To require
a clean install explicitly, pass `-p:SparForceFrontendRestore=true`. Linked/workspace installations
inside `node_modules` retain clean installs on every build; a junction/symlink replacing the entire
`node_modules` directory is rejected before an installer can replace it. Build/publish failures remain
failures; no compatibility fallback is introduced. Use a normal publish with its build enabled.

Copy the non-PDB files from the publish directory to a directory outside the checkout and run `Spar.NativeSample.exe` there. The payload contains:

- `Spar.NativeSample.exe`, including the application, generated bridge, and production assets.
- `WebView2Loader.dll`, `WebView2-LICENSE.txt`, and `WebView2-NOTICE.txt`.
- `frontend-LICENSES.txt`, containing the bundled React, React DOM, scheduler, and emitted Vite helper notices. Identical text is embedded in the asset archive.
- `spar-publish.json`, identifying selected mode, configuration, RID, source revision and hashes, dirty-tree status, tool versions, archive hash, and payload hashes. Actual execution receipts independently report runtime mode.

Symbols are optional. The browser engine is installed separately; this is neither a single-process application nor an installer. The default persistent profile is `%LOCALAPPDATA%/Sotsera/Spar.NativeSample`. An optional first argument selects a different profile directory; profiles are leased exclusively and preserved on exit. `SPAR_WEBVIEW2_RUNTIME_FOLDER` selects an explicit runtime folder, primarily for a fixed-runtime deployment or the missing-runtime fixture. A redistributed fixed runtime has not been qualified. Missing WebView2 produces an error naming the Evergreen Runtime and native HRESULT.

## Public runtime and authority

`SparAssetBundle.Read(stream)` loads a caller-owned seekable ZIP. Pass the result through `SparWindowOptions.Assets`, with a bridge using the matching generated contract. Do not also set `Html` or `LoopbackContent`. The bundle contains `index.html`, `spar-contract.txt` holding the exact generated fingerprint, and canonical relative asset paths. The fingerprint file is consumed as metadata and never served. A mismatch fails before native creation; the generated client also verifies the backend fingerprint during its real handshake.

The runtime owns a bounded immutable copy: at most 256 files, 16 MiB per decompressed file, and 64 MiB total archive/decompressed content. It rejects duplicate names, empty/dot path segments, encoded/non-ASCII names, backslashes, query/fragment characters, and unsupported extensions. Supported MIME types cover HTML, JS, CSS, JSON, text, SVG, PNG, JPEG, WebP, ICO, WOFF, and WOFF2. No path is mapped to the filesystem. This initial contract deliberately excludes arbitrary asset providers and route fallback.

Each native document still receives a fresh `https://<random>.spar.invalid/index.html` address. WebView2 resource interception serves exact GET resources from that document's bundle, with MIME types, `nosniff`, and no caching. Unknown/query-alias/foreign resources are denied. A restrictive response CSP permits local scripts/styles/images/fonts and denies network connections, frames, objects, forms, and base-URL overrides. Resources never require DNS, a TLS connection, or a local HTTP server. Existing native top-level navigation, popup, permission, document retirement, and reply-delivery rules remain in force.

The build helper is framework-neutral: the sample configures its frontend directory and build command through its ordinary project/targets. No Vite, Node, compiler, or ZIP-build tool is a production runtime dependency. The same targets are distributed in the private build package for [external consumers](18-package-consumer.md).

The frontend build must emit `dist/frontend-LICENSES.txt`; the shared targets fail if it is absent and copy it to build/publish outputs. Other frontend toolchains supply their own appropriate notices at that path. The React sample and fresh package consumer enable [Vite's bundled dependency license output](https://vite.dev/config/build-options.html#build-license), then run `write-notices.mjs` to retain the installed Vite core license for its emitted modulepreload helper. This step reads locked installed packages and adds no dependency. Vite itself remains build tooling. Dependency changes require reviewing the resulting inventory, including any emitted virtual helpers, assets, and additional notice obligations.

After building the frontend, `node eng/Test-FrontendNotices.mjs` checks all four package versions and complete license texts against their installed licenses, plus repeated generation. The production boundary check compares the embedded notice with the sidecar by SHA-256; copied AOT and CoreCLR runs require the notices in the receipt-hashed payload. This covers the current sample's distributed frontend code, not arbitrary consumer dependencies or a complete public-release review.

The [distribution-notice evidence](evidence/distribution-notices-2026-10-20/README.md) records the new sample and external-consumer executions separately from the original production baseline.

## Production boundary and test fixtures

The production entry point accepts no development or probe switches. It grants only greeting, counter, and delay permissions. The sample contract still contains test operations for its development/probe fixture, but production does not grant them. The production frontend omits HMR event listeners, diagnostic/report effects, and probe actions. Release host compilation excludes inspection APIs, callbacks, and scripts; production browser developer tools/context menus are disabled.

`Test-ProductionBoundary.ps1` inspects the actual Release PE metadata and embedded ZIP: no development/compiler assembly references, no probe document/resources, and no development frontend markers. `Test-Production.ps1` verifies copied payload hashes/mode, actual typed calls, rejected switches, missing-runtime diagnostics, retained-process cleanup, released profile leases, and separate host/browser memory samples. Three runs distinguish a fresh profile from reused profiles; these are small baseline observations, not performance promises. `-VerifyRender` additionally captures pixels, which require visual review. CLI completion alone does not prove that React has rendered a reply.

The old native/bridge probes are now an explicit **separate test build**, never the default production output:

```powershell
dotnet publish fixtures/Spar.NativeSample -c Release -p:SparNativeProbe=true -o artifacts/native/probe-publish --disable-build-servers
# Copy those fixture files separately, then use Test-NativeHost.ps1 and Test-Bridge.ps1.
```

`Test-Bridge.ps1` now also runs a packaged-asset fixture through the same public bundle API and real WebView. It exercises local CSS/JS, denied missing/query/foreign/metadata resources, typed calls, stale fingerprints, denied handlers, document retirement, navigation rejection, and close with unfinished work. The required **Windows managed probe** job checks both the production application and the separate AOT fixture. CI does not claim desktop rendering or manual UI interaction.

## Startup delays and antivirus attribution

`Test-Production.ps1` retains `run-N-startup.json` on success and failure. It records the retained
process identity, sanitized first/last application event, startup phase, bounded one-second CPU and
working-set observations, failed wait and observed teardown. Pending messages every ten seconds do
not extend the existing 30-second deadline or retry the application. No events means no application
event was observed; it does not establish that `Main` was never entered. Rendering is recorded as
captured only after capture succeeds. Root exit and retained browser exits are separate observations;
`not-retained` is not evidence that no descendants survived.

These vendor-independent observations leave antivirus attribution `unattributed` and evidence
`not-collected`. Low CPU, an installed antivirus, or a security DLL in the process does not establish
the cause. Windows Security Center's [product API](https://learn.microsoft.com/en-us/windows/win32/api/iwscapi/nn-iwscapi-iwscproduct)
reports protection identity/state, not which file operation is blocking.

| Evidence | What it can establish |
| --- | --- |
| Process samples and application events | Where observable progress stopped; not the cause of a wait |
| CoreCLR EventPipe loader events | Assembly-loading intervals; not why the loader waited. NativeAOT needs native tracing. |
| Defender performance recording | Matching file/process scan intervals that can be correlated with loader waits |
| Other vendor performance providers or kernel/filter tracing | A blocking driver or scan when the provider exposes it; otherwise attribution remains unknown |

Prepare the publication before recording. Retain file hashes, process creation identity, exact copy
paths, recording coverage and lost-event counts. Compare overlapping intervals; nested or asynchronous
scans must not be summed as startup delay. For Defender, run this in a separate elevated PowerShell
from the repository root, then launch the prepared production driver from a normal terminal after
the recorder acknowledges that recording has started:

```powershell
$recordingDirectory = Join-Path (Get-Location) 'artifacts/antivirus'
New-Item -ItemType Directory -Path $recordingDirectory -Force | Out-Null
$recording = Join-Path $recordingDirectory ('startup-' + [guid]::NewGuid().ToString('N') + '.etl')
New-MpPerformanceRecording -RecordTo $recording -Seconds 45
Get-MpPerformanceReport -Path $recording -TopScans 1000 -Raw
```

See the [Defender performance reference](https://learn.microsoft.com/en-us/defender-endpoint/performance-analyzer-reference).
Require the saved recording and actual recorder completion; do not stop another recording session.
Raw traces contain machine activity and should remain private. Other antivirus products need their
supported performance provider or a bounded [WPR](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/windows-performance-recorder)
file-I/O/wait recording. [Minifilter diagnostics](https://learn.microsoft.com/en-us/windows-hardware/test/assessments/minifilter-diagnostics)
can distinguish individual blocking filter drivers, including antivirus, backup and encryption;
availability and attribution vary by product. Spar does not automatically elevate, change protection
settings or exclusions, or change a test outcome because antivirus involvement is suspected.

The [startup investigation](evidence/startup-diagnostics-2026-10-23/README.md) records the measured
Defender correlation, fixture identity experiment and their limits.

## Remaining work

Use [dependency compatibility](17-dependency-compatibility.md) for attributable CoreCLR selection and [packages](18-package-consumer.md) for a fresh external consumer. Current qualification limits and release work are maintained in [status and deferred work](README.md#status-and-deferred-work).
