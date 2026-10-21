# Dependency-driven CoreCLR delivery

This integration guide exercises the explicit acceptance application under `fixtures/`. For ordinary application startup and publication, start with the [minimal application guide](23-minimal-application.md). Historical captures retain their original paths.

Issue [#11](https://github.com/sotsera/spar/issues/11), M1.7 / A15–A16. NativeAOT is the default production mode. An application that adds a dependency requiring runtime code generation can explicitly select self-contained, untrimmed CoreCLR. Development still uses CoreCLR independently of production selection. There is no automatic fallback, dependency classifier, or cached incompatibility decision.

## Persist the decision

In the application project, before its `Spar.Delivery.targets` import:

```xml
<PropertyGroup>
  <SparDeliveryMode>CoreCLR</SparDeliveryMode>
  <SparCompatibilityDependency>My.Dependency 1.2.3</SparCompatibilityDependency>
  <SparCompatibilityReason>IL3050: its proxy factory emits DynamicMethod IL at runtime.</SparCompatibilityReason>
</PropertyGroup>

<Import Project="../../build/Spar.Delivery.targets" />
```

This is a recorded application decision based on the dependency's actual API/diagnostic. The build requires attribution; it does not independently certify the explanation. The import currently lives in this repository; packaging it for external consumers belongs to #12. Keep the selection before this import, rather than in a late-loaded publish profile. Ordinary Release publishing then uses the selection:

```powershell
dotnet publish fixtures/Spar.NativeSample -c Release -o artifacts/compatibility/publish --disable-build-servers
```

`SparDeliveryMode` accepts only `NativeAOT` and `CoreCLR`. Omitting it selects NativeAOT. CoreCLR requires both attribution fields, self-contained publication, and no trimming. A conflicting direct `PublishAot` override fails with SPAR1004. Syntax, restore, compiler, linker and missing-tool failures remain failures in the selected mode.

To reevaluate a persisted compatibility choice in CI:

```powershell
dotnet publish fixtures/Spar.NativeSample -c Release -p:SparStrictAot=true -o artifacts/compatibility/strict --disable-build-servers
```

This selects NativeAOT even when the project requests CoreCLR. It does not suppress diagnostics. When removing the incompatible dependency and call, remove the compatibility properties too; normal publishing immediately returns to NativeAOT. Strict checks can also run before removing the recorded selection.

## Diagnostics and receipts

The sample enables the SDK AOT and trimming analyzers during development. Calls to a dependency annotated with `RequiresDynamicCode` surface IL3050 with the call-site location and affected API. In development and explicit CoreCLR delivery, IL3050/IL2026 remain visible warnings; NativeAOT builds treat them as errors. Spar framework libraries retain their own AOT checks and warning-as-error policy in both modes. This does not detect every unsupported dynamic pattern, and a successful CoreCLR build/run is not AOT compatibility evidence.

The SDK's [NativeAOT analyzer guidance](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) and [IL3050 diagnostic](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/warnings/il3050) describe the supported mechanism. The test dependency's source is authored in this repository; no upstream source or additional NuGet runtime dependency was imported.

Successful SPA publication writes `spar-publish.json` schema 2, including requested and effective modes, strict override, self-contained packaging, dependency attribution, source/tooling/contract identity, and payload hashes. Runtime events independently report actual execution mode. Attribution is written as data files before receipt generation, not interpolated into shell commands. Use a fresh output directory for each publish attempt: a failed build does not make files from an older successful publication current. Receipts establish completed publication, not application execution or supported-machine qualification.

The CoreCLR payload includes the app assemblies, .NET runtime, WebView2 loader/notices, and embedded SPA. Copy the complete directory, excluding optional PDBs. It needs neither an installed .NET runtime nor Node by construction, but execution on a machine without .NET has not been qualified. The same Windows/WebView2/profile prerequisites and production authority/control exclusions from the [production guide](16-production-delivery.md) apply. It is larger than the NativeAOT payload; no installer or single-file compatibility delivery is introduced.

## Executed acceptance driver

```powershell
dotnet build Spar.slnx --disable-build-servers
dotnet test --solution Spar.slnx --no-build
pwsh -NoProfile -File eng/Test-Compatibility.ps1 -VerifyRender
```

The driver copies source into a unique fixture with separate outputs and no junctions. It adds `eng/compatibility-probe/Spar.DynamicDependency.csproj` to the ordinary native sample, persists the compatibility choice, and calls the dependency from the generated typed greeting path. That dependency emits a `DynamicMethod`, creates its delegate, executes it, and returns **Hello from generated code, Spar**. A backend event records the returned value, the ordinary bridge completes, and the production SPA renders it. `-VerifyRender` captures pixels for separate visual review.

The driver checks the development warning, strict dependency rejection, copied self-contained CoreCLR execution, production exclusion, syntax failure, configuration conflicts, removal followed by copied AOT execution, and strict override receipts. It also injects a real AOT regression into an isolated framework source copy and fails actual compiler-tool invocation and native linking. Every negative case requires a nonzero exit, its expected diagnostic, and no success receipt in a fresh output directory. The framework regression must fail even when the application selects CoreCLR.

Evidence includes exact commands, source hashes, generated fixture sources, logs, receipt assertions, native application events, and retained process/profile cleanup. The required **Windows managed probe** runs this suite without claiming interactive rendering. See [local evidence](evidence/dependency-compatibility-2026-10-19/README.md) for observed outcomes and first failures. External packages and fresh-machine setup remain #12; milestone completion is not inferred from this compatibility slice.
