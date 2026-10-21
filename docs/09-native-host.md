# Minimal Windows host and NativeAOT proof

This integration guide exercises the explicit acceptance application under `fixtures/`. For ordinary application startup and publication, start with the [minimal application guide](23-minimal-application.md). Historical captures retain their original paths.

Current publishing behavior: normal Release now packages the React SPA. The precursor lifecycle driver below uses an explicit `-p:SparNativeProbe=true` Release test build. See [production delivery](16-production-delivery.md) for the shipped application and separate test commands.

This guide owns the native host API, lifetime rules and host-only fixture checks. [Typed contracts](10-typed-bridge.md), [development sessions](11-development-session.md) and [package consumers](18-package-consumer.md) build on this host. See [current status](README.md#status-and-deferred-work) for acceptance scope.

The embedded-document fixture exercises one Win32/WebView2 window. It deliberately differs from the ordinary SPA application; its standalone lifecycle receipts are test instrumentation.

## Public entry point

Call `SparApplication.Run(new SparWindowOptions { Title = ..., Html = ..., UserDataFolder = ... })` from an `[STAThread]` entry point. `Run` owns the calling thread's native message pump and returns after the controller, subscriptions, COM wrappers, and window are released. Startup or callback failures are rethrown after cleanup. The first implementation supports Windows/x64 and one active window per calling thread.

`Created` and `DocumentLoaded` run on the UI thread. `Created` precedes browser initialization; `DocumentLoaded` reports successful navigation. Neither establishes visible rendering or a typed bridge handshake. `SparWindow.Post` queues work from another thread (maximum 256 queued actions); callback failures end the session. `Close` requests shutdown from any thread. A close request does not prove completion; wait for `Run` to return or observe the process exit.

The HTML is an application-owned document. The sample supplies a restrictive CSP and no network assets. M1.3 now adds the [typed bridge](10-typed-bridge.md), constrained native navigation, unique local document addresses, explicit permissions, and contract matching. Native host objects remain disabled; this host is not a general untrusted-content browser.

## Run and publish

Prerequisites: Windows x64, SDK `10.0.402`, an installed [WebView2 Evergreen Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/), and the Visual Studio C++/Windows SDK tools needed for NativeAOT publishing. The developer-machine proof uses Windows build 26220.9739 and WebView2 155.0.4283.62. This is not a tested Windows minimum-version matrix.

```powershell
dotnet build Spar.slnx --disable-build-servers
dotnet run --project fixtures/Spar.NativeSample --no-build
dotnet publish fixtures/Spar.NativeSample -c Release -p:SparNativeProbe=true -o artifacts/native/publish --disable-build-servers
```

Debug uses CoreCLR; this sample defaults Release publishing to NativeAOT. AOT compilation warnings are errors. There is no automatic CoreCLR fallback. The published application needs its `WebView2Loader.dll` and the WebView2 license/notice files alongside the executable. PDBs are optional symbols. The browser engine remains a separately installed runtime with writable profile data; NativeAOT does not bundle it or make the app a single OS process.

`BrowserRuntimeFolder` optionally forwards an explicit fixed-runtime directory to the native loader. The negative fixture uses a nonexistent directory so the native API must report the missing runtime. Normal startup uses the installed Evergreen Runtime. A valid redistributed fixed-runtime payload has not been qualified in this slice.

To verify the payload outside the checkout, copy the published files (PDBs optional) to another directory and run the copied executable there. The sample accepts an optional first argument for the browser profile directory and prints source-generated JSON lifecycle receipts. Its default profile is `%LOCALAPPDATA%/Sotsera/Spar.NativeSample`.

## Ownership and failure handling

- Native calls and WebView callbacks stay on the owning STA. P/Invoke and COM stubs are generated at build time. The sample disables built-in COM support under CoreCLR as well as AOT.
- Every COM wrapper is a unique instance and is explicitly released on the STA. Callback parameters are borrowed; getter results transfer a reference. Temporary callback interface pointers are released after registration; native COM retains the callback while it needs it.
- Closing during asynchronous environment/controller creation keeps the window and message pump alive until the pending creation callback returns. Final cleanup is posted outside the WebView callback stack. There is no timeout that pretends native creation finished.
- Native event subscriptions are removed, the controller is closed, and COM references are released before destroying the window and uninitializing COM. Cleanup attempts continue when an individual cleanup operation reports failure.
- An exclusive `.spar-profile.lock` file prevents concurrent Spar hosts from using the same profile. It is a host lease, not a claim that every browser helper exited when `Run` returns. Persistent browser data is preserved. Browser processes may finish later; the test driver observes them separately and never kills unrelated processes by name.
- Before creating the controller, Spar reads the actual native profile directory through `ICoreWebView2Environment7`. A mismatch with the leased directory fails startup. Environment/policy redirection must not silently move the browser outside that lease. Runtimes lacking this interface receive an actionable update diagnostic.
- The missing-runtime test passes an explicit nonexistent runtime directory to WebView2. It verifies an actual native startup failure and an actionable install/runtime diagnostic without uninstalling the developer's runtime.

Admitted bridge operations and cancellation callbacks now remain owned through actual completion before close/reload. Arbitrary tasks started independently by application callbacks remain application-owned. Application-generation/restart coordination is owned by the [development layer](12-backend-updates.md). Keyboard/DPI polish, process-failure recovery, runtime installation, multi-window behavior, and clean-machine qualification remain follow-ups.

## Repeatable native checks

Use PowerShell 7.4 or later for the test driver. The sample reports the explicit runtime directory used by the missing-runtime fixture:

```powershell
pwsh -NoProfile -File eng/Test-NativeHost.ps1 `
  -Executable fixtures/Spar.NativeSample/bin/Debug/net10.0-windows/win-x64/Spar.NativeSample.exe `
  -ExpectedMode CoreCLR -VerifyRender

# Point this at the copied published executable, outside the checkout.
pwsh -NoProfile -File eng/Test-NativeHost.ps1 `
  -Executable C:/path/to/copied/Spar.NativeSample.exe -ExpectedMode NativeAOT -VerifyRender
```

The driver checks normal close, concurrent-profile rejection, close during initialization, worker-to-UI dispatch, a throwing document callback, missing-runtime startup, profile redirection, invalid profile input, profile preservation, execution mode, and actual host exit. It retains process handles and creation identities for attributed browser observations. Forced cleanup is reported as failure. A post-exit browser snapshot is not a full history of every short-lived subprocess.

The profile-redirection probe distinguishes a rejected override from a platform that ignores the override and keeps the correct leased profile. The latter is recorded as `profileOverrideRejected: not-exercised-platform-ignored-override`; it does not establish override rejection. Both local desktop modes actually exercised rejection. Ordinary successful startup always confirms the actual profile before admitting the document.

`-VerifyRender` shows the native window, captures actual pixels through Win32 `PrintWindow`, checks the sample's accent pixels, resizes, and captures again. The captures require separate visual inspection as evidence of the complete layout. Without this switch, receipts explicitly say rendering was not exercised. A document-load event alone never passes the rendering check.

The existing required **Windows managed probe** CI job is extended to run the CoreCLR lifecycle checks, strict AOT publishing, and the copied AOT lifecycle checks. It does not request desktop rendering. CI evidence and developer desktop evidence answer different questions; neither substitutes for later debugger, SPA HMR, typed-call, external-package, or clean-machine acceptance.

## WebView2 source and license provenance

The direct SDK dependency is [Microsoft.Web.WebView2 1.0.4258.31](https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4258.31), stable, pinned centrally. Only the x64 native loader and license/notice files are copied. Managed WPF/WinForms wrappers and package build targets are excluded from the runtime project.

`src/Sotsera.Spar.Windows/Interop/WebViewInterfaces.cs` projects ABI definitions from that package's `build/native/include/WebView2.h`, also represented in `WebView2.idl`. Changes: C# source-generated prefix interfaces, renamed methods, explicit pointer parameters, HRESULT preservation, and managed callback implementations. Every slot before the last projected method is retained in SDK order. This is a new host implementation, not adapted Tauri/NeoAstra/old-Spar host code.

The SDK's `LICENSE.txt` is BSD-3-Clause with Microsoft's copyright; it and the package's `NOTICE.txt` are retained under [third-party/webview2](../third-party/webview2/) and copied into build/publish output. The installed browser runtime has its own distribution terms; the SDK license is not a license to redistribute the browser engine.

Immutable package/source identifiers:

- NuGet package SHA-512 (base64): `HlGVwvyP/IWiXAU8K57Vmg59khY3DWMOxMnH4AHusFRy0/vDAjKItXUIVNXiXXaq4CLfSZyNUic1wOIlnPLsPQ==`.
- `WebView2.h` SHA-256: `DFF1E3181EC7EC203A34EF6EFA966590E0EF0BA1A5C3FE3B69DA6508C2F8A02E`.
- `WebView2.idl` SHA-256: `57B35D4B63C25F656D04559C5692E1D9A622D77AD25FB556406C348A496D0E24`.

The implementation uses the public [.NET COM source generator](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/comwrappers-source-generation), unique-instance [COM release](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.marshalling.comobject.finalrelease?view=net-10.0), and [WebView2 STA threading model](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/threading-model).

## Recorded execution

Historical execution, 2026-10-18. These results describe the source and tooling in the linked record.

CoreCLR and copied NativeAOT desktop executions passed all seven driver scenarios, including a concurrent-profile subcheck in normal startup. The [desktop receipt](evidence/native-host-2026-10-18/receipt.json) records payload/source hashes and process identities; [CoreCLR](evidence/native-host-2026-10-18/coreclr-resized.png) and [AOT](evidence/native-host-2026-10-18/aot-resized.png) captures show the resized page. All four retained captures were visually inspected.

The final no-symbol AOT payload contains the native executable, loader, and two notice files; no managed application DLL or runtime configuration is needed. Browser runtime/profile storage is additional. This is a single execution baseline, not a performance benchmark or clean-machine/no-.NET certification. This host-only run did not establish integrated milestone completion; see the later [acceptance matrix](19-milestone-1-acceptance.md).

Earlier test-driver iterations exposed a PowerShell 5 process-API mismatch and a nonexistent `[nint]` PowerShell alias; those failed attempts were retained under ignored `artifacts/native/` and did not count as passes. Visual inspection then caught a stale browser frame immediately after resizing; the final check waits for the accent to move into the smaller layout before accepting the image. These were harness fixes, not substituted rendering evidence.

The first two upstream runs ([39153079064](https://github.com/sotsera/spar/actions/runs/39153079064), [39153448434](https://github.com/sotsera/spar/actions/runs/39153448434)) failed because the runner's native loader ignored the missing-runtime environment override, including when the child reported that variable as set. The runner loaded WebView2 154.0.4258.62; the local runtime was 155.0.4283.62. This does not establish why the override was ignored. The final fixture uses the explicit API parameter instead, and its expected native failure remains required; neither a timeout nor a loaded document passes it.

[Upstream recovery run 39153778439](https://github.com/sotsera/spar/actions/runs/39153778439) passed solution build, three real watcher tests, CoreCLR native lifecycle, strict AOT publication, copied AOT lifecycle, and evidence upload on implementation revision `c120a53`. The final PR head must pass its own [required check](https://github.com/sotsera/spar/pull/16/checks). The runner did not exercise visible desktop rendering.
