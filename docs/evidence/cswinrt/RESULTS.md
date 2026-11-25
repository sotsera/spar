# CsWinRT 3.0 preview spike — results

> A spike. Its code was deleted with `spikes/` at the start of M2; it is in git history at [`spikes/cswinrt/`](https://github.com/sotsera/spar/tree/24f5db0942833047c81fa692908410596f3b8960/spikes/cswinrt) (commit `24f5db0`), and this record and its `results/` moved here. Paths under `spikes/` below name that history.

**Question.** Would CsWinRT 3.0 (`3.0.0-preview.261104.1`) make Spar's life easier? In particular: (1) WebView2 through a
WinRT projection instead of the hand-written `[GeneratedComInterface]` vtables, and (2) Windows SDK APIs (file picker,
PasswordVault, toasts) from an unpackaged NativeAOT desktop exe.

**Answer, short.**

- **It works, with no AOT or trim warnings.** A NativeAOT exe that uses the WebView2 projection (environment,
  controller on an HWND, `WebResourceRequested` with a stream response, `WebMessageReceived`,
  `CallDevToolsProtocolMethodAsync`) behaves like the hand-written COM host and starts just as fast. FileOpenPicker
  and PasswordVault both work unpackaged.
- **The cost is size.** The WebView2 exe is **25.3 MB plus an 0.86 MB sidecar DLL, against 2.5 MB**. One
  PasswordVault call adds 11.6 MB to an exe. Publishing takes 75 s instead of 5 s.
- **Recommendation: don't adopt now.** Keep hand-written COM for WebView2. Re-evaluate the Windows SDK projection when
  3.0 is stable and its size improves. For a single Windows API such as the picker, a few hand-written COM or CsWin32
  declarations are far cheaper.

Environment: Windows 11 Pro 10.0.26320, SDK 11.0.100, WebView2 Runtime 157, Microsoft.Web.WebView2 1.0.4314.35, CsWin32
0.3.369, win-x64, offscreen windows (shown, not activated, at −32000,−32000). Every number below was observed on
2026-11-13 in this folder.

## What CsWinRT 3.0 changes (release notes and docs)

Sources:

- Release notes: [3.0.0-preview.261104.1](https://github.com/microsoft/CsWinRT/releases/tag/3.0.0-preview.261104.1)
  and the first preview, [3.0.0-preview.260319.2](https://github.com/microsoft/CsWinRT/releases/tag/3.0.0-preview.260319.2),
  which lists the breaking changes.
- Docs on the `staging/3.0` branch:
  [usage](https://github.com/microsoft/CsWinRT/blob/staging/3.0/docs/usage.md),
  [AOT and trimming](https://github.com/microsoft/CsWinRT/blob/staging/3.0/docs/aot-trimming.md),
  [3.0 spec](https://github.com/microsoft/CsWinRT/blob/staging/3.0/docs/cswinrt3.0-spec.md),
  [authoring](https://github.com/microsoft/CsWinRT/blob/staging/3.0/docs/authoring.md).

| Area | 3.0 |
|---|---|
| Positioning | A rewrite, described as "designed with AOT and trimming in mind from the start". It is a breaking release, not binary compatible with 2.x. 2.x remains supported during migration. |
| AOT/trim model | Marshalling no longer comes from a source generator in every project. After compilation, the app build runs **`cswinrtinteropgen`** over the whole app. It emits **`WinRT.Interop.dll`**: deduplicated marshalling stubs, CCW vtables and COM interface entries. Lookups use the .NET 10 **TypeMap** feature. ILC preinitializes vtables as read-only data. Optional behaviours (IDynamicInterfaceCastable, XAML type marshalling, the default `ICustomPropertyProvider`, type metadata) are trimmable MSBuild feature switches. User types no longer need `partial`. |
| Projections | A projection is a **reference assembly** (`CsWinRTGenerateReferenceProjection`). The app build regenerates the implementation into **`WinRT.Projection.dll`** (one per app). The Windows SDK projection's implementation ships prebuilt in the CsWinRT package as `WinRT.Sdk.Projection.dll`. "Embedded mode" is removed. `WinRT.Runtime` gets a separate reference assembly. |
| TFM | The revision selects the version: `net10.0-windows10.0.26100.0` is CsWinRT 2.x, and **`…26100.1` is CsWinRT 3.0**. The SDK defines `CSWINRT3_0` and multi-targeting is supported. Minimum .NET 10; recommended SDK 10.0.400+. |
| Packages | Both packages are required: `Microsoft.Windows.CsWinRT` 3.0.0-preview.261104.1 and `Microsoft.Windows.SDK.NET.Ref` `10.0.<build>.88-preview`, set through `WindowsSdkPackageVersion`. |
| Breaking changes | `As<I>()`, `FromAbi` and `FromManaged` are removed; use `WindowsRuntimeActivationFactory` and `WindowsRuntimeMarshal.ConvertToManaged/ConvertToUnmanaged`. `IWinRTObject` and `IEquatable` support are removed. `[In] T[]` parameters become `ReadOnlySpan<T>`, and fill-array parameters become `Span<T>`. `Point`, `Rect` and `Size` use `float` fields. Struct members project as fields. `TypedEventHandler<S,A>` becomes `System.EventHandler<S,A>`. .NET Standard 2.0 is dropped. |
| New in this preview | Component authoring (`CsWinRTComponent`), with WinMD generation after the build. `[Deprecated]` and `[Experimental]` are honoured. Interop generation is incremental and parallel. `WindowsRuntimeMarshal.GetIid<T>()`. Fixes for event-cache leaks and async-operation lifetime. |
| Known limits | The notes say performance and binary-size work is ongoing, and that trimmed JIT publishing may fail. No known-issues list for this preview. |

Two things the docs do not mention, which this spike found (see below):

- There is **no `InitializeWithWindow`/`WindowNative` helper** in 3.0.
- **.NET SDK 11.0.100 already knows the `.1` TFMs**, but maps them to the non-preview `10.0.26100.87` package, which has
  no CsWinRT 3.0 profile.

## What was built

| Path | Role |
|---|---|
| `Directory.Build.props` / `Directory.Packages.props` | Import the repository files first. They set `SpikeWindowsTfm=net11.0-windows10.0.26100.1` and `WindowsSdkPackageVersion=10.0.26100.88-preview`, plus `PublishAot`, `IsAotCompatible`, `TrimmerSingleWarn=false` and `InvariantGlobalization`. |
| `WebView2.Projection/` | A 19-line library: `CsWinRTGenerateReferenceProjection=true`, `CsWinRTIncludes=Microsoft.Web.WebView2.Core`, and `CsWinRTInputs` pointing at the package's `lib\Microsoft.Web.WebView2.Core.winmd`. |
| `WebView2.App/` | One host with two bindings, chosen with `-p:SpikeWebView2=WinRT\|Com`. The shared part (`Program.cs`) has the STA window, message loop, `UiContext`, page and measurements. `WinRTBackend.cs` uses the projection. `ComBackend.cs` plus `Interop/` is the hand-written COM copied from `spikes/react` (Com builds as plain `net11.0-windows` with the static loader). Each run serves a page through `WebResourceRequested`. The page measures 50 fetch round trips (1 KB POST, answered through a deferral) and 50 postMessage round trips. The host then runs 30 CDP `Runtime.evaluate` calls, closes, and waits for the browser process to exit. |
| `WinApis.App/` | Windows SDK probes, chosen with `-p:SpikeApi=Baseline\|Vault\|Picker\|Toast\|All` (`-p:SpikeNoWinRT=true` gives a build with no CsWinRT). `Picker.cs` opens a FileOpenPicker owned by an offscreen window. A watcher closes the dialog 1.5 s after it appears, or cancels the operation after 5 s. |
| `publish-webview2.sh`, `run-webview2.sh`, `publish-winapis.sh` | Reproduce the publications and runs. |

## WebView2: hand-written COM vs CsWinRT 3.0 projection

NativeAOT, Release, win-x64. For startup, runs 2–6 of 6 per binding, warm profile under
`artifacts/webview2-profiles/cswinrt-<binding>`.

| | Hand-written COM (`[GeneratedComInterface]`) | CsWinRT 3.0 projection |
|---|---|---|
| Interop code written by hand | **378 lines** (`Interop/WebView2.cs` 280, `Handlers.cs` 98), with placeholder vtable slots | **0 lines**: a 19-line projection project plus 6 lines in the app's csproj |
| Binding-specific host code | 173 lines | 138 lines |
| AOT/trim warnings (warnings as errors, no suppressions) | 0 | **0 ILC/IL warnings.** One MSBuild warning (below). |
| Published files | `WebView2.App.exe` **2,540,032 B** (loader linked statically) | `WebView2.App.exe` **25,316,864 B** + `Microsoft.Web.WebView2.Core.dll` **863,624 B** (a required sidecar) |
| … with CsWinRT feature switches off¹ | — | exe 22,390,784 B (still works) |
| `dotnet publish` (AOT) | 5.1 s | **74.9 s** |
| Debug build: clean / no-op / after editing one file | 0.8 / 0.4 / 0.7 s | **11.9 / 2.5 / 3.2 s** |
| Process start → `Main` | 26–45 ms | 26–28 ms (35 ms in run 1) |
| Environment created (from host start) | 11–20 ms | 12–14 ms |
| NavigationCompleted (from host start) | 251–361 ms (median 262) | 247–329 ms (median 271) |
| Page: fetch round trip, 1 KB via deferral (median of 50; `performance.now` has 0.1 ms resolution) | 0.8–1.1 ms | 0.9–1.3 ms |
| Page: postMessage round trip (median of 50) | 0.1 ms | 0.0–0.1 ms |
| CDP `Runtime.evaluate` (median of 30) | 0.11–0.16 ms | 0.11–0.14 ms |
| Close → browser process exited | 6.44–6.49 s | 6.48–6.53 s |

¹ `CsWinRTEnableIDynamicInterfaceCastableSupport`, `CsWinRTEnableXamlTypeMarshalling`,
`CsWinRTEnableDefaultCustomPropertyProviderSupport` and `CsWinRTEnableMarshalingTypeMetadataSupport` set to `false`,
and `CsWinRTMarshallingMode=strict`. The self-test still passed.

**Runtime behaviour is the same:** startup, bridge cost and CDP cost are equal within noise. The close-to-exit time
was about 6.45 s in every run of **both** bindings. That comes from the host's shutdown shape, not from the binding. It
matches the slow case in the desktop-host decision; it was not investigated here.

**Ergonomics.** The projection is clearly nicer to write against:

- **Events:** `+=`/`-=` with `EventHandler<CoreWebView2, TArgs>` instead of handler classes and registration tokens.
- **Async:** `await` on `IAsyncOperation<T>`, which resumes on the STA `SynchronizationContext`.
- **Properties:** `args.Request.Uri` and similar, instead of `GetX()`/`PutX()`.
- **Streams:** `new MemoryStream(bytes).AsRandomAccessStream()` and `IRandomAccessStream.AsStreamForRead()`, instead
  of `SHCreateMemStream` and a hand-read `ISequentialStream`.
- **Coverage:** the entire API surface, including newer interfaces, with no vtable placeholders to maintain.

What it does not remove: the STA ownership, the message pump, explicit unsubscription, `Close()`, and waiting for
browser exit. Optional interfaces are flattened into the classes. A member the installed Runtime lacks would fail at
call time (not tested), so feature detection is still needed.

**How the projection has to be built.**

- **Generating it inside the app project fails.** With `CsWinRTIncludes` in the exe, the generated sources do not
  compile: they reference implementation types missing from the `WinRT.Runtime` reference assembly. For example:
  `Generated Files\CsWinRT\Microsoft.Web.WebView2.Core.cs(10002,6): error CS0246: The type or namespace name 'WindowsRuntimeType' could not be found`
  and `… 'WindowsRuntimeObjectReference' could not be found`.
- **A separate reference-projection library works**, which is the documented 3.0 shape. The app build then produces
  `WinRT.Projection.dll` and `WinRT.Interop.dll`, and ILC compiles them into the exe.
- **The WebView2 package's own projection can't be used.** `lib_manual/net8.0-windows10.0.17763.0/Microsoft.Web.WebView2.Core.Projection.dll`
  is a CsWinRT 2.x build, which is not binary compatible. Its MSBuild logic would otherwise pick the classic .NET
  wrapper, so the spike references the package with `ExcludeAssets="all"`.
- **The sidecar DLL is required.** The runtime classes are implemented by
  `runtimes/win-x64/native_uap/Microsoft.Web.WebView2.Core.dll`, which is activated without a manifest from the app
  folder. Per `dumpbin`, it embeds its own loader and imports only system DLLs and the UCRT. There is no static-link
  option, so the single-file exe from decision D8 is lost.

## Windows SDK APIs (unpackaged NativeAOT exe)

| API | Works unpackaged? | Package identity needed? | AOT exe size (`-p:SpikeApi=…`) | Result |
|---|---|---|---|---|
| None: CsWinRT referenced but no WinRT call (`Baseline`) | — | — | 999,936 B | Referencing CsWinRT costs nothing until a WinRT type is used. |
| None: no CsWinRT, picker probe compiled without WinRT (`NoWinRT`) | — | — | 1,203,712 B | |
| `Windows.Security.Credentials.PasswordVault` (`Vault`) | **yes** | no | **12,653,568 B** (10,493,952 B with switches off¹) | Add, Retrieve, RetrievePassword round-trip and Remove all work. `Add` took 64–145 ms (AOT). Entries for `SparSpike.CsWinRT` were 0 before and 0 after each run (checked with `FindAllByResource`, which throws `0x80070490` when empty). A separate PowerShell `RetrieveAll` check also found no `SparSpike*` entries. |
| `Windows.Storage.Pickers.FileOpenPicker` + `IInitializeWithWindow` (`Picker`) | **yes** | no | **18,409,984 B** | `PickSingleFileAsync` returns in 1–3 ms with status `Started`. The dialog is **brokered**: its `#32770` window, owned by our HWND, belongs to **`PickerHost.exe`**, not to our process. It appeared 534–647 ms after the call and was closed with `WM_CLOSE` about 1.5 s later. The operation then completed with `file=null`. Each run had the dialog on screen for about 1.5 s; none was left behind. |
| Toasts: `ToastNotificationManager` (`Toast`) | **no** (not trivially) | **yes, or a registered AUMID** | 12,656,128 B | `CreateToastNotifier()` throws `COMException 0x80070490`. `CreateToastNotifier("SparSpike.CsWinRT")` with an unregistered AUMID also throws `0x80070490`. Making it work would need an AUMID registration (shortcut or registry), which this spike does not create. No toast was shown. |
| `Windows.ApplicationModel.Package.Current` | — | — | (in `Toast`) | Throws `InvalidOperationException 0x80073D54` (no package identity), as expected. |
| All of the above (`All`) | | | 18,434,560 B | |

**IInitializeWithWindow:**

- CsWinRT 3.0 has no helper for it: `WinRT.Interop.InitializeWithWindow` is gone, and no `WindowNative` type exists.
- The spike takes the interface from CsWin32 (`IInitializeWithWindow` in `NativeMethods.txt`, `allowMarshaling:
  false`) and the pointer from `WindowsRuntimeMarshal.ConvertToUnmanaged(picker, in IID)`, released with
  `WindowsRuntimeMarshal.Free`. That is about 12 lines, with no warnings.

**Where the size goes.** The `Vault` exe has an ILC map (`-p:IlcGenerateMapFile=true`). Its 11.4 MB of nodes break
down as:

- 3.04 MB of embedded metadata;
- 2.44 MB of `WinRT.Interop` code, of which **2.36 MB is about 36,000 generic collection marshallers**
  (`ABI.System.Collections.*`) for Windows SDK instantiations (`Windows.ApplicationModel`, `Devices`, `Media`, …);
- 1.66 MB of WinRT vtables;
- about 1 MB of BCL code.

The generated `WinRT.Interop.dll` is 29.6 MB of IL even for this app. The size is therefore mostly fixed cost from the
whole SDK projection, not from the API used, and the feature switches recover only about 2 MB. The release notes say
size work is still ongoing.

## Requirements and costs

- **TFM:** `net11.0-windows10.0.26100.1`, which works with .NET 11 and SDK 11.0.100. The SDK has the `.1` mapping
  built in (`Microsoft.NET.Windows.targets`: `_TargetPlatformVersionUsesCsWinRT3`, `CSWINRT3_0`, and the
  `Microsoft.Windows.SDK.NET.Ref.CsWinRT3.Windows` profile).
- **`WindowsSdkPackageVersion=10.0.26100.88-preview` is mandatory.** The SDK maps `…26100.1` to `10.0.26100.87`,
  whose package has only `lib/cswinrt2`. Without the pin, `Windows.*` types are missing, for example
  `error CS0234: The type or namespace name 'Security' does not exist in the namespace 'Windows'`.
- **`Microsoft.Windows.CsWinRT` is mandatory even for SDK-only use.** The preview SDK projection package's
  `cswinrt3` profile contains only `Microsoft.Windows.SDK.NET.dll` and `Microsoft.Windows.UI.Xaml.dll`. Without
  CsWinRT the build fails with
  `error MSB6011: Invalid parameters passed to the Microsoft.NET.Build.Tasks.RunCsWinRTGenerator task` /
  `Tools directory 'tools' is invalid or does not exist`.
- **The warning every build produces** (`Microsoft.NET.Windows.targets(199,5)`), verbatim:
  `warning NETSDK1229: Targeting a Windows SDK version with '1' as the revision number will reference CsWinRT 3.0, which is currently in preview. The current project is targeting the Windows SDK version ''. If this is not intended, change the revision number to '0' to use CsWinRT 2.x instead.`
  The `''` is an SDK formatting bug.
  - It is **not** turned into an error by the repository's `TreatWarningsAsErrors`, which affects only compiler
    warnings.
  - `-warnaserror` or `MSBuildTreatWarningsAsErrors` would fail the build, and only a suppression would get past it.
  - It is the only warning: there were zero CS, IL or ILC warnings in every build and publication here.
- **Downloads** (sizes checked first; none over 150 MB):

  | Package | Download | Extracted | Restore time |
  |---|---:|---:|---:|
  | `Microsoft.Windows.SDK.NET.Ref` 10.0.26100.88-preview | 21,107,445 B | 126 MB | 17.2 s |
  | `Microsoft.Windows.CsWinRT` 3.0.0-preview.261104.1 | 141,448,343 B | **512 MB** (tools for x64 and arm64, prebuilt SDK projections for six SDK versions) | 1 min 28 s |

  Nothing else was downloaded: WebView2, CsWin32, ILCompiler and the runtime packs were already in the cache.
- **Build time:** see the table above. A clean build takes about 12 s instead of under 1 s. A no-op build takes 2.5 s
  instead of 0.4 s, so CsWinRT work runs even when nothing has changed. An AOT publish takes 75 s instead of 5 s, of
  which the interop generator and an ILC run over ~60 MB of generated IL take most.
- **Stability risk:**
  - It is a preview: the version comes from a `staging/3.0` branch, the SDK warns about it, and two previews have
    shipped seven months apart.
  - It breaks API and binary compatibility with 2.x.
  - Adopting it would put preview package pins in every Spar app: `WindowsSdkPackageVersion` and a CsWinRT
    `PackageReference`. Spar apps would also carry a 141 MB / 512 MB package and the sidecar DLL.
  - The docs still show the previous preview's versions and a sample that "hasn't been updated yet".

## Recommendation

1. **WebView2: don't adopt.** Keep the hand-written `[GeneratedComInterface]` binding (plan §3, decision D8).
   - The projection removes about 380 lines of vtable declarations, and its API is much nicer.
   - The cost: the exe grows 10× (2.5 → 26 MB, or 23 MB with every switch off), the 23 KB static loader becomes a
     0.86 MB sidecar DLL, AOT publish is 15× slower, and every build carries preview pins and the NETSDK1229 warning.
   - None of startup, bridge latency or CDP latency improves.
   - Revisit only if a stable 3.x brings an app that uses only WebView2 down to a few MB.
2. **Windows SDK APIs: not now, and for one or two APIs probably never.**
   - The first WinRT call costs **10–17 MB**, almost all of it fixed SDK-wide marshalling.
   - For the SQL Server tool's needs, cheaper paths exist:
     - **File dialogs:** `IFileOpenDialog` through CsWin32. That is the same dialog without the broker, and needs no
       `IInitializeWithWindow`.
     - **Credentials:** the Win32 Credential Manager (`CredWrite`/`CredRead`/`CredDelete`) through CsWin32.
   - Neither alternative was measured here.
   - Toasts need identity or an AUMID registration whatever the binding.
3. **Re-evaluate when CsWinRT 3.0 is stable.** Re-run `publish-winapis.sh` and `publish-webview2.sh`. The deciding
   number is the AOT size of the `Vault` and `WinRT` variants.

## Failures and their causes

1. **A projection generated inside the exe project doesn't compile** (CS0246 on `WindowsRuntimeType` and
   `WindowsRuntimeObjectReference`). Fixed by putting the reference projection in its own library, the documented 3.0
   shape.
2. **The first picker runs hung until the harness timeout** (exit 124; the process was killed, and no dialog was left
   behind; this was checked). The watcher looked only at our own process's windows, but the dialog lives in
   `PickerHost.exe`. It now finds the dialog by owner HWND and cancels the operation after 5 s.
3. Building without `WindowsSdkPackageVersion` fails with CS0234, and building without the CsWinRT package fails with
   MSB6011. Both are explained under requirements.
4. Small compile issues, all mine:
   - `GetAwaiter` on `IAsyncOperation` needs `using Windows.Foundation;`.
   - `GetDeferral()` returns `Windows.Foundation.Deferral`, not a WebView2 type.
   - `Assembly.Location` gives IL3000 under AOT analysis, as expected.

## Observed / skipped / not tested

**Observed** (run here, on this machine):

- **Builds and publications:** every build and AOT publication listed, with zero CS, IL or ILC warnings and the one
  NETSDK1229.
- **WebView2:** 6 AOT runs per binding, all exit 0, plus one JIT run of the WinRT binding and one AOT run with the
  switches off.
- **Windows APIs:** JIT and AOT runs of the vault, picker, toast and identity probes, and the vault cleanup check.
- **Measurements:** build and publish times, and the ILC map breakdown for `Vault`.

**Skipped:**

- Showing a toast: it needs an AUMID registration, which would leave state behind.
- Publish timing for the WinApis variants: the timing helper failed on the first pass, and later runs would have been
  incremental.
- The CsWinRT 2.x path (`…26100.0`) for comparison.

**Not tested:**

- **WebView2 projection details:** behaviour with an older WebView2 Runtime (members missing at call time), the
  `ProcessFailed` and `BrowserProcessExited` events, and CDP event receivers.
- **Process and packaging variants:** trimmed CoreCLR (JIT) publishing, which the release notes say may fail, arm64
  and x86, and a packaged (MSIX) app.
- **Investigations not done:** why the browser takes about 6.45 s to exit in both bindings, the Win32
  `IFileOpenDialog` and `CredWrite` alternatives and their sizes, and the IDE (IntelliSense) behaviour of the
  projection library.

## Reproduce

```bash
./publish-webview2.sh && ./run-webview2.sh 6
./publish-winapis.sh
WinApis.App/bin/publish/All/WinApis.App.exe          # opens and auto-closes one picker dialog
```

Size probes: add the feature-switch properties from footnote ¹, or `-p:IlcGenerateMapFile=true`. `bin/`, `obj/` and
`artifacts/webview2-profiles/cswinrt-*` are disposable.
