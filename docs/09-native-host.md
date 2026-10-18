# Minimal Windows host and NativeAOT proof

Date: 2026-10-18. Scope: M1.2 / [upstream issue #3](https://github.com/sotsera/spar/issues/3).

This slice introduces `Sotsera.Spar.Windows` and the ordinary `Spar.NativeSample` consumer. It loads trusted local HTML in one Win32/WebView2 window. There is no application bridge, development coordinator, SPA server, general inspection API, or package/template delivery yet. The integrated A01–A18 scenarios remain open.

## Public entry point

Call `SparApplication.Run(new SparWindowOptions { Title = ..., Html = ..., UserDataFolder = ... })` from an `[STAThread]` entry point. `Run` owns the calling thread's native message pump and returns after the controller, subscriptions, COM wrappers, and window are released. Startup or callback failures are rethrown after cleanup. The first implementation supports Windows/x64 and one active window per calling thread.

`Created` and `DocumentLoaded` run on the UI thread. `Created` precedes browser initialization; `DocumentLoaded` reports successful navigation. Neither establishes visible rendering or a typed bridge handshake. `SparWindow.Post` queues work from another thread (maximum 256 queued actions); callback failures end the session. `Close` requests shutdown from any thread. A close request does not prove completion; wait for `Run` to return or observe the process exit.

The HTML is an application-owned document. The sample supplies a restrictive CSP and no network assets. There are no exported backend operations or native host objects. Navigation policy, native document authority, explicit permissions, and contract matching must be implemented together before adding the typed bridge in M1.3; this host is not a general untrusted-content browser.

## Run and publish

Prerequisites: Windows x64, SDK `10.0.402`, an installed [WebView2 Evergreen Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/), and the Visual Studio C++/Windows SDK tools needed for NativeAOT publishing. The developer-machine proof uses Windows build 26220.9739 and WebView2 155.0.4283.62. This is not a tested Windows minimum-version matrix.

```powershell
dotnet build Spar.slnx --disable-build-servers
dotnet run --project samples/Spar.NativeSample --no-build
dotnet publish samples/Spar.NativeSample -c Release -o artifacts/native/publish --disable-build-servers
```

Debug uses CoreCLR; this sample defaults Release publishing to NativeAOT. AOT compilation warnings are errors. There is no automatic CoreCLR fallback. The published application needs its `WebView2Loader.dll` and the WebView2 license/notice files alongside the executable. PDBs are optional symbols. The browser engine remains a separately installed runtime with writable profile data; NativeAOT does not bundle it or make the app a single OS process.

To verify the payload outside the checkout, copy the published files (PDBs optional) to another directory and run the copied executable there. The sample accepts an optional first argument for the browser profile directory and prints source-generated JSON lifecycle receipts. Its default profile is `%LOCALAPPDATA%/Sotsera/Spar.NativeSample`.

## Ownership and failure handling

- Native calls and WebView callbacks stay on the owning STA. P/Invoke and COM stubs are generated at build time. The sample disables built-in COM support under CoreCLR as well as AOT.
- Every COM wrapper is a unique instance and is explicitly released on the STA. Callback parameters are borrowed; getter results transfer a reference. Temporary callback interface pointers are released after registration; native COM retains the callback while it needs it.
- Closing during asynchronous environment/controller creation keeps the window and message pump alive until the pending creation callback returns. Final cleanup is posted outside the WebView callback stack. There is no timeout that pretends native creation finished.
- Native event subscriptions are removed, the controller is closed, and COM references are released before destroying the window and uninitializing COM. Cleanup attempts continue when an individual cleanup operation reports failure.
- An exclusive `.spar-profile.lock` file prevents concurrent Spar hosts from using the same profile. It is a host lease, not a claim that every browser helper exited when `Run` returns. Persistent browser data is preserved. Browser processes may finish later; the test driver observes them separately and never kills unrelated processes by name.
- The missing-runtime test uses WebView2's browser-directory override with a nonexistent directory. It verifies an actual native startup failure and an actionable install/runtime diagnostic without uninstalling the developer's runtime.

Arbitrary tasks started by application callbacks are application-owned; this initial API does not track backend operations. The generation/admission/retirement model comes with the bridge and development coordinator. Keyboard/DPI polish, process-failure recovery, runtime installation, multi-window behavior, and clean-machine qualification remain follow-ups.

## Repeatable native checks

Use PowerShell 7.4 or later for the test driver:

```powershell
pwsh -NoProfile -File eng/Test-NativeHost.ps1 `
  -Executable samples/Spar.NativeSample/bin/Debug/net10.0-windows/win-x64/Spar.NativeSample.exe `
  -ExpectedMode CoreCLR -VerifyRender

# Point this at the copied published executable, outside the checkout.
pwsh -NoProfile -File eng/Test-NativeHost.ps1 `
  -Executable C:/path/to/copied/Spar.NativeSample.exe -ExpectedMode NativeAOT -VerifyRender
```

The driver checks normal close, concurrent-profile rejection, close during initialization, worker-to-UI dispatch, a throwing document callback, missing-runtime startup, invalid profile input, profile preservation, execution mode, and actual host exit. It retains process handles and creation identities for attributed browser observations. Forced cleanup is reported as failure. A post-exit browser snapshot is not a full history of every short-lived subprocess.

`-VerifyRender` shows the native window, captures actual pixels through Win32 `PrintWindow`, checks the sample's accent pixels, resizes, and captures again. The captures require separate visual inspection as evidence of the complete layout. Without this switch, receipts explicitly say rendering was not exercised. A document-load event alone never passes the rendering check.

The existing required **Windows managed probe** CI job is extended to run the CoreCLR lifecycle checks, strict AOT publishing, and the copied AOT lifecycle checks. It does not request desktop rendering. CI evidence and developer desktop evidence answer different questions; neither substitutes for later debugger, SPA HMR, typed-call, external-package, or clean-machine acceptance.

## WebView2 source and license provenance

The direct SDK dependency is [Microsoft.Web.WebView2 1.0.4258.31](https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4258.31), stable, pinned centrally. Only the x64 native loader and license/notice files are copied. Managed WPF/WinForms wrappers and package build targets are excluded from the runtime project.

`src/Sotsera.Spar.Windows/Interop/WebViewInterfaces.cs` projects ABI definitions from that package's `build/native/include/WebView2.h`, also represented in `WebView2.idl`. Changes: C# source-generated prefix interfaces, renamed methods, explicit pointer parameters, HRESULT preservation, and managed callback implementations. Every slot before the last projected method is retained in SDK order. This is a new host implementation, not adapted Tauri/NeoAstra/old-Spar host code.

The SDK's `LICENSE.txt` is BSD-3-Clause with Microsoft's copyright; it and the package's `NOTICE.txt` are retained under [third-party/webview2](../third-party/webview2/) and copied into build/publish output. The installed browser runtime has its own distribution terms; the SDK license is not a license to redistribute the browser engine.

The implementation uses the public [.NET COM source generator](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/comwrappers-source-generation), unique-instance [COM release](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.marshalling.comobject.finalrelease?view=net-10.0), and [WebView2 STA threading model](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/threading-model).

## Recorded execution

Initial CoreCLR and copied NativeAOT desktop executions passed. Final source/payload hashes, screenshots, lifecycle receipts, CI outcome, and remaining limits are recorded with the PR's evidence. A copied AOT run on a developer machine does not certify execution on a machine without .NET installed. The full milestone remains incomplete.
