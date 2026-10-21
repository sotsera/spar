# Typed, document-bound bridge

This integration guide exercises the explicit acceptance application under `fixtures/`. For ordinary application startup and publication, start with the [minimal application guide](23-minimal-application.md). Historical captures retain their original paths.

Current publishing behavior: the inline probe below is a separate `-p:SparNativeProbe=true` Release test build. Default Release publishes the React SPA. The driver also exercises a packaged-resource bridge fixture; see [production delivery](16-production-delivery.md).

This guide owns the contract surface, native authority and cancellation/retirement behavior. [Development sessions](11-development-session.md) use the generated bindings with loopback authority; [editor](13-vscode-development.md) and [package](18-package-consumer.md) guides cover their integration.

## Consumer contract

The C# contract is the source of truth. Put one `[SparContract]` public interface and its public sealed positional record DTOs in a file-scoped namespace. Export each method explicitly:

```csharp
[SparContract]
public interface ISampleBackend
{
    [SparOperation("greet", "greeting.read")]
    Task<GreetingReply> GreetAsync(GreetingRequest request, CancellationToken cancellationToken);
}

public sealed record GreetingRequest(string Name);
public sealed record GreetingReply(string Greeting);
```

Implement the ordinary interface. The build tool produces `SampleBackendBridge`, a direct switch dispatcher, source-generated JSON metadata, `client.js`, and matching `client.d.ts` declarations. The consumer writes no dispatcher or serialization glue. Install no Roslyn/compiler dependency in the application runtime.

The repository sample's `Spar.NativeSample.Contracts` project owns the interface/DTO file, sets `SparContractFile`, imports `build/Spar.Contracts.targets`, and references the generator as a build-only project (`ReferenceOutputAssembly="false"`, `Private="false"`, `SkipGetTargetFrameworkProperties="true"`; remove inherited RID/AOT/self-contained and `TargetFramework`/`TargetFrameworks` properties). `Private="false"` prevents copying the compiler tool's apphost/configuration into the published app. The target runs before resource assignment and compilation; generated files live under `obj/<configuration>/<framework>/<rid>/spar/`. Unchanged output is not rewritten. The application references this contract/dispatcher assembly and reads its embedded client. This separation also keeps wire metadata outside the [implementation patch workspace](12-backend-updates.md). An ordinary build establishes generated sources for subsequent design-time compilation. First-open/editor regeneration and packaging this target as a consumer build asset remain follow-ups.

The tool uses the public C# syntax parser shipped with the pinned SDK. It validates a deliberately small syntax surface; it is not a semantic C# export discovery engine. The ordinary compiler still checks the complete application. The sample embeds the generated module and its own inline application module into its local HTML; a later SPA integration will import the same framework-neutral module.

```csharp
Bridge = new SparBridge(new SampleBackendBridge(backend), ["greeting.read"])
```

```typescript
import { backend } from './generated/client.js';
const reply = await backend.greet({ name: 'Spar' }, { signal, timeoutMs: 5000 });
```

The host grants permissions explicitly and copies the grant set. Exporting a method does not grant its permission. Application methods run on worker threads, including their synchronous prefix, with up to eight admitted calls per bridge. Application state must support that concurrency; use the window's public `Post` API for native UI work.

## Supported surface and limits

| Area | Initial limit |
| --- | --- |
| Contract | One public interface, 1–32 methods, no inheritance, overloads, generics, default implementations, or conditional declarations |
| Method | `[SparOperation("wire-name", "permission")] Task<Reply> MethodAsync(Request, CancellationToken)` |
| DTO | 1–32 public sealed positional records, 1–16 fields each, no custom members, defaults, or serialization attributes |
| Field | `string`, `string?`, `int`, `bool`; no collections, nesting, arbitrary objects, or converters |
| String | 4096 UTF-16 code units per field, request and response |
| Names | Simple identifiers; unique generated method names; operation/permission strings 1–64 ASCII letters, digits, dots, or hyphens |
| Wire | Version 1, 64 KiB UTF-8 input/output, JSON depth 16, at most 32 unique members per object |
| Ownership | Eight unfinished calls across documents sharing a bridge; timeout 1–30000 ms (default 30000) |
| Replay | Positive increasing 32-bit request IDs per client/document; duplicate/lower IDs rejected |
| Local document | 1 MiB UTF-8 HTML, all resources inline in this slice |

Fields use camelCase JSON names. Missing required fields, nonnullable nulls, unmapped fields, duplicate JSON properties, and incompatible scalar values are rejected before the application method. Response validation failures and handler exceptions produce sanitized error codes without renderer-visible exception details. Output beyond the wire limit produces `output_too_large`. Limits bound the bridge transport; application handler allocations and side effects remain application-owned.

`CallOptions` and `BridgeIdentity` are reserved TypeScript types; `__proto__` is not a supported client method. The generator rejects these collisions. Unsupported exports produce a source path/line `SPAR001` diagnostic. The SHA-256 fingerprint covers protocol version, namespace/interface, operations, permissions, method/type names, and DTO fields; formatting/comments do not change it. Contract changes require matching generated outputs and a later coordinated restart, not mutation replay.

## Native authority and delivery

The host serves the in-memory HTML at a fresh `https://<random>.spar.invalid/index.html` URI for every document. `WebResourceRequested` supplies a native memory stream for that exact GET and rejects other resource requests. This is no HTTP server and requires no DNS/certificate connection. The fresh origin avoids reusing an old document's persisted origin state. The sample CSP permits only inline script/style; this is an initial trusted-local-document policy, not the final production asset system.

The host authorizes only its own next top-level navigation to that exact URI. It captures the native navigation ID, opens bridge authority at matching non-error `ContentLoading`, and ignores unrelated completion IDs. Every top-level navigation attempt retires the previous bridge. Other navigation, frames, popups, browser permissions, host objects, and default script dialogs are blocked. A rejected navigation does not restore the old document's bridge; the sample probe explicitly requests a new trusted document afterward.

Admission reads `ICoreWebView2WebMessageReceivedEventArgs.Source` from the native event. Renderer JSON never supplies source authority. Handshake binds a random client nonce to a native document token and the contract fingerprint. Calls need all three. Before posting any reply, native code rechecks the live document object, navigation ID, activity, and actual WebView source. The generated client also checks its nonce/document/fingerprint before consuming the queued message. This guards delivery races after a native post; a posted message is not proof that a renderer consumed it.

Development URL admission is deliberately absent. M1.4 must introduce a session-owned origin policy; this implementation does not grant arbitrary localhost origins access.

## Cancellation, retirement, and close

A cancellation or deadline can reject the frontend promise while the application handler continues. The bridge retains its slot and returned ownership task until the handler actually completes **and** asynchronous cancellation callbacks return. `CancelAsync` keeps those callbacks off the native STA. A throwing callback does not abandon the remaining cleanup. No operation is retried automatically.

`ReloadDocument()` retires admission on the UI thread and waits for all admitted work before starting the replacement document. Close likewise retains the window, COM objects, profile lease, and message pump until actual completion. An indefinitely uncooperative handler therefore prevents graceful reload/close; this slice has no forced-termination coordinator and does not pretend a timeout completed the work. Tasks started independently by application callbacks remain application-owned.

Diagnostics contain stage, document token, request ID, operation, and code. The core observer cannot change execution ownership by throwing. Native callback errors retain the existing fail-after-cleanup behavior. Arguments, return payloads, and handler exception details are not diagnostics.

## Verification

```powershell
dotnet build Spar.slnx --disable-build-servers
dotnet test --solution Spar.slnx --no-build
npm ci --ignore-scripts --prefix eng/bridge-client-tests
npm test --prefix eng/bridge-client-tests

pwsh -NoProfile -File eng/Test-Bridge.ps1 `
  -Executable fixtures/Spar.NativeSample/bin/Debug/net10.0-windows/win-x64/Spar.NativeSample.exe `
  -EvidenceDirectory artifacts/bridge/coreclr -VerifyRender

dotnet publish fixtures/Spar.NativeSample -c Release -p:SparNativeProbe=true -o artifacts/bridge/publish --disable-build-servers
# Copy the non-PDB files outside the checkout, then point the same driver at that executable:
pwsh -NoProfile -File eng/Test-Bridge.ps1 `
  -Executable C:/path/to/copied/Spar.NativeSample.exe -ExpectedMode NativeAOT `
  -EvidenceDirectory artifacts/bridge/aot -VerifyRender
```

The native probe executes the generated client inside WebView2. It checks greeting/counter/delay results, denied handler entries, cancellation, an ignoring handler after timeout, stale contract/document and invalid DTO/ID/input rejection, actual work draining before reload, distinct replacement documents, stale reply suppression, rejected navigation, and close while work remains. Its report operation is sample-owned and granted only for explicit probe runs; the runtime has no inspection/test endpoint. Positive renderer acknowledgment follows the final reply. `-VerifyRender` separately requires actual success-color pixels and a visual review of the retained screenshot. CI omits that switch and does not claim desktop rendering.

Deterministic xUnit tests cover pre-handler rejection, repeated-ID mutation protection, all eight slots remaining occupied after timeout, retirement, a blocked cancellation callback after handler completion, bounded replies, exception sanitization, and generator fingerprint/diagnostic behavior. TypeScript 7.0.2 is an explicit, locked, Apache-2.0 development dependency: positive calls and `@ts-expect-error` negative cases compile against the actual generated declarations. The build/generator and TypeScript compiler stay outside the published payload.

The existing **Windows managed probe** required CI job now includes these checks plus the original watcher/native lifecycle checks and copied strict-AOT execution. Interactive screenshots, native debugger integration, full production assets, no-SDK machine qualification, and external NuGet consumption remain separate evidence requirements. No integrated A01–A18 scenario is marked complete by this slice.

## Recorded local evidence

Historical execution, 2026-10-18. These results describe the source and tooling in the linked record.

CoreCLR and copied NativeAOT passed both bridge scenarios with actual success pixels and no forced cleanup on Windows build 26220.9739 / WebView2 155.0.4283.62. The original seven native lifecycle scenarios were also rechecked. See the [retained receipt](evidence/typed-bridge-2026-10-18/receipt.json), generated examples, and CoreCLR/AOT captures in that directory. The copied AOT payload has the native executable, loader, and two notices; it contains no contract tool, Roslyn, managed app DLL, or runtime configuration. These are developer-machine observations, not clean-machine certification or performance benchmarks.

During implementation a first publish failed the SDK's executable-reference compatibility validation, and an early successful publish copied the generator apphost/config files. The final build-only reference fixes both. An intermediate-output property evaluated too early also wrote generated files beside source; moving its evaluation into the target fixed output placement and duplicate compilation. Failed attempts remain under ignored artifacts and do not count as final passes.

The [first upstream run](https://github.com/sotsera/spar/actions/runs/39159941118) passed build, all managed tests, TypeScript, and the bridge's renderer assertions, then failed the driver close request: `Process.CloseMainWindow()` did not discover the hidden CI window. The driver now posts close to the application's recorded native HWND, using the same helper as the existing lifecycle checks. That failure required forced cleanup and does not establish graceful exit; subsequent CI must pass actual exit and lease checks.

## Primary mechanisms and provenance

- [Native web-message source](https://learn.microsoft.com/en-us/microsoft-edge/webview2/reference/win32/icorewebview2webmessagereceivedeventargs?view=webview2-1.0.4258.31), [navigation identities](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/navigation-events), and [WebView2 security guidance](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security).
- [.NET cancellation callbacks](https://learn.microsoft.com/en-us/dotnet/api/system.threading.cancellationtokensource.cancelasync?view=net-10.0) and [native copied memory stream](https://learn.microsoft.com/en-us/windows/win32/api/shlwapi/nf-shlwapi-shcreatememstream).
- [TypeScript package metadata](https://registry.npmjs.org/typescript/7.0.2) records the tested compiler version and Apache-2.0 license.

`Interop/WebViewDocumentInterfaces.cs` and the extended prefixes in `Interop/WebViewInterfaces.cs` project ABI declarations from the same pinned SDK header/revision/hash recorded in the [native host provenance](09-native-host.md#webview2-source-and-license-provenance). Changes are C# source-generated prefix projections, explicit pointers/HRESULTs, renamed methods, and managed callback forwarding. Existing Microsoft copyright, BSD-3-Clause license, and notices apply. No Tauri, NeoAstra, Reactor, CodeAlta, or previous-Spar implementation source was imported.
