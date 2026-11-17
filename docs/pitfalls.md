# Known pitfalls

Surprising behaviours found while building the M0 spikes. Each one was observed while running something, except the entries under a "not rechecked" sub-heading at the end of a section: those come from earlier Spar versions and other frameworks. The evidence, with numbers, is in `docs/decisions.md` and in each spike's `RESULTS.md` (`spikes/<name>/`). Check this list before designing or debugging the area it covers, and add to it when you find a new one. Entries are filed by area; where one fits two areas, the other section points to it.

## Build, MSBuild and packages

- **NuGet's restore ignores `UndefineProperties` on a `ProjectReference`:** a global property the reference removes still reaches the referenced project's restore. When that property moves the project's `obj/` (the NativeAOT lane's `objot`), restore writes `project.assets.json` where the build never looks, and a clean publish fails with NETSDK1004. It passed locally for days, because an ordinary build had already restored `obj/`; CI's first lane that started clean found it. Give such a reference the property instead of removing it. (M1a CI)
- **NativeAOT on Linux cannot link with a NuGet package folder whose path has a space:** `clang` got `/tmp/spar`, `package` and `tests/nuget-packages/…/libSystem.Native.a` as separate arguments for every runtime library. The app's own folder, with a space and non-ASCII letters, linked fine. Windows links either. Keep `NUGET_PACKAGES` free of spaces wherever a Linux NativeAOT publish runs. (M1a Linux)
- **A file-local type's name holds a hash of its source file's path** (the regex generator's `<RegexGenerator_g>F298E…__Utilities`), so it changes with the checkout and between builds with and without `ContinuousIntegrationBuild`, which maps paths to `/_/`. A list of type names must not keep the hash. (M1a Linux)
- **MSBuild writes item paths with `/` on Linux,** whatever the project file says: a message that quotes an item written `..AppApp.csproj` prints `../App/App.csproj` there. (M1a Linux)
- **xUnit does not run a class's tests in the order they are written:** the build tests' publish ran before the "first" build, installed the frontend and wrote the client, so the first build skipped them and its check failed. A test that needs a fresh tree awaits one shared first build, whichever test starts. (M1a build tests)
- **`dotnet build-server shutdown` stops every MSBuild and compiler server on the machine,** not only the ones a test started. Isolated runs use their own `MSBUILDNODEHANDSHAKESALT` and `SharedCompilationId` and let their nodes end when idle. (M1a build tests)
- **An app's variants share `obj/spar/client`,** which the frontend's `package.json` names, and build at the same time: two client steps rewriting it raced once (`SafeFileHandle.CreateFile` in `ClientPackage.Sync`). It stays hidden while the files are unchanged, because unchanged files are not written. Writes to a folder variants share go under a lease. (M1a bridge limits)
- **Several targets hooked `BeforeTargets` on one target run in the order they are declared in the file, and `AfterTargets` does not change that:** Spar's embedding target, declared before the packing target, ran first, so every build embedded the previous build's pack and a clean build would have embedded none. It went unnoticed because each test build had a pack left from an earlier one. Hook one entry target and order the steps with `DependsOnTargets`; check a clean build. (M1a packing)
- **A resource embedded by a target that runs just before `CoreCompile` arrives after MSBuild settled resources** in `PrepareResources`, so it can miss the compile's resource list. Add generated `EmbeddedResource` items before `PrepareResources`. Inferred from the targets' order; the failing case was not built. (M1a packing)
- **A warm MSBuild node keeps a task's old parameter list,** even when the task runs in its own task host (`TaskHostFactory`): after a parameter was added, a build on a reused node failed with MSB4064 ("The 'SnapshotMode' parameter is not supported") while one on a fresh node passed. Only framework development changes a task under a running node; build with `-nr:false` and `DOTNET_CLI_USE_MSBUILD_SERVER=false` after changing a task's parameters. (M1a client step)
- **A check inside an incremental target misses a stale committed file:** the contract snapshot, verified inside the client step, passed a CI-mode build after being edited, because the step was up to date. Compare committed files in a target that always runs, against an expected copy under `obj/`. (M1a client step)
- **A project reference to a task project leaks into the app** unless both sides stop it: the task project's own `PackageReference` (Roslyn) and `ProjectReference` (the generator) need `PrivateAssets="all"`, or the app restores them and gets their assemblies, and the generator as an ordinary reference. `ReferenceOutputAssembly="false"` on the app's side alone does not stop transitive flow. Checked: with both, the app's `project.assets.json` names neither. (M1a client step)
- **The task authoring analyzer follows calls transitively:** MSBuildTask0005 flagged every `File` and `Directory` call in helper classes that took a `string` path, and MSBuildTask0003 a `File.Exists(Path.Combine(...))` in the task. Carry `AbsolutePath` to each call and combine with `new AbsolutePath(relative, basePath)`. (M1a client step)
- **A step that reads the project's C# before the compile must run after `GenerateGlobalUsings`:** without the implicit usings, `Task<T>` in a contract does not bind and the contract silently loses its commands. Inferred from the `csp-navigation` extractor's finding; the client step depends on that target, and the failing case was not built. (M1a client step)
- **A global property flows down every project reference that does not remove it,** transitive ones included: the app built as `SparVariant=Test` passed the variant to its plain library, and the library passed it to `Spar.csproj`, so both were built twice, with and without it, into the same `obj/` (CS2012 on `Spar.dll`, in the repository and in a clean copy). Only projects that keep a folder per value may receive such a property; `Spar.Sdk` removes the variant and the test lane from every other reference (`UndefineProperties`), and the build tests count each project's `CoreCompile` instances. (M1a library contract at run time)
- **A self-contained build and a framework-dependent one must not share an output folder:** the NativeAOT test lane built `WireShapes.Tests` self-contained into the same `bin/Production/Release/` as its CoreCLR Release build, and the `hostfxr.dll` it left there made the CoreCLR build's launcher look for .NET in that folder ("You must install or update .NET to run this application", .NET location: the test's own folder). Nothing failed until a Release `dotnet test` ran after a lane publish. The lane now builds into `bin/aot/` as well as `obj/aot/`. (M1a budgets)
- **Under central package management, every `PackageReference` an SDK adds with a version must be `IsImplicitlyDefined`:** Spar.Sdk marked its own packages so, but not `xunit.v3`, whose version it also sets, and a consumer with `ManagePackageVersionsCentrally` failed restore with NU1008 ("cannot define a value for Version: xunit.v3"). A consumer without central package management never shows it. (M1a central package management)
- **Roslyn spells a type in the global namespace `global::Order`, with no dot:** taking the text after the last dot as its name kept the `global::`, which would have generated invalid TypeScript. A keyword used as a name comes with `@` (`global::App.@string`): drop it before checking the name. (M1a client step)

- **Warm MSBuild nodes lock a task DLL** built in the same source checkout (MSB3027 on the next build). Run Spar's own tasks through `TaskHostFactory` in the repository. Package consumers don't need it: a package folder never changes. (`d1-pipeline`, `package-consumer`)
- **`Microsoft.Extensions.DependencyInjection.Abstractions` is part of the `net11.0` shared framework.** Restore prunes the package, and a direct `PackageReference` fails under the repository's settings with NU1510 ("will not be pruned"). Reference nothing; the types are already there. (`app-model-check`)
- **Never reuse a package version.** Deleting a cached `Spar.Sdk` version while the MSBuild server was warm stopped partway on a locked DLL and left the package half-deleted. The warm server still reported the next `dotnet build` as succeeded; only a fresh evaluation failed, with MSB4019. (`package-consumer`)
- **A write-if-changed output cannot be a target's `Outputs`.** The file can stay older than its inputs, so the target runs on every build. Use a separate stamp file. (`react`)
- **MSBuild misses a file restored with an older timestamp.** It skipped both the client step and CoreCompile, so the build went stale without a message. A normal editor save is detected. Scripts and agents that copy files with their original timestamps hit this. (`d1-pipeline`)
- **Exclude every `bin/` and `obj/` from the default globs,** not only the current variant's. Otherwise one variant compiles another's generated `AssemblyInfo` (CS0579). Also exclude `frontend/node_modules`, `dist` and the frontend's own `obj/` and `bin/`, from the globs, from the frontend target's input glob and from `dotnet watch`. (`d3-variants`, `react`, `esproj`)
- **`-p:PublishAot=true` on the command line fails with NETSDK1207,** because the global property flows to the netstandard2.0 generator project. The `MSBuild` task forwards global properties too: use `RemoveProperties`, and pass `Configuration` and `_IsPublishing` explicitly. (`react`, `d3-variants`)
- **MSBuild splits `-p:` values on `;`.** `-p:SpikeParts=A;B` was rejected as an invalid switch. Escape the semicolon as `%3B`. (`native-services`)
- **Concurrent restores of one variant race** unless they share a deterministic `MSBuildRestoreSessionId`. (`d3-variants`)
- **Two processes building the same variant clash** (6 of 10 runs in-proc, with the MSBuild server off). A `FileShare.None` lock file in the variant's `obj` fixes it. (`d3-variants`)
- **The MSBuild server hides concurrency.** On SDK 11.0.100, `dotnet build` and `dotnet publish` use the MSBuild server (`MSBuild.exe /nodemode:8`) by default; `dotnet msbuild` does not. A second `dotnet build` cannot use the busy server, so it starts cold and finishes after the first: no real overlap. Concurrency tests need `DOTNET_CLI_USE_MSBUILD_SERVER=false`. (`d3-variants`, `package-consumer`)
- **Inline `RoslynCodeTaskFactory` tasks failed with MSB0001** under `-mt` after a `-m` build in the same server ("created a task but returned null/empty assembly path"). They passed in a fresh server. Spar's tasks are compiled, so this affects only inline tasks in spikes and tests. (`d3-variants`)
- **`NUGET_PACKAGES` does not isolate NuGet's HTTP cache.** Isolation tests must also set `NUGET_HTTP_CACHE_PATH`, or they write to the user's `%LOCALAPPDATA%\NuGet\v3-cache`. (`package-consumer`)
- **NuGet audit fails builds** (warnings are errors) on vulnerable transitive packages.
  - Microsoft.Data.SqlClient 7.1.0 pulls System.Security.Cryptography.Pkcs 9.0.18, which is flagged; 10.0.12 is flagged too. Use SqlClient 7.1.2 or later.
  - Aspire 13.1.2 is flagged (GHSA-f7vh-h5g3-7jrh, plus MessagePack). Use 13.6.5 or later.

  (`react`, `aspire`)
- **`TreatWarningsAsErrors` does not fail SDK or MSBuild warnings.** NETSDK1229 (an MSBuild `Warning` from `Microsoft.NET.Windows.targets`) passed every build under the repository's settings. `MSBuildTreatWarningsAsErrors` or `-warnaserror` is the switch for those. A probe warning (MSB9999) then failed a build under `tests/`, and the root `Directory.Build.props` now sets it outside `spikes/` (decision "MSBuild and SDK warnings fail the build"). (`cswinrt`)
- **The SDK default `TrimmerSingleWarn=true` collapses a package's trim and AOT warnings to one per assembly.** SqlClient 7.1.2 then showed 4 (IL2104 ×3, IL3053) instead of 49. Suppressing those 4 would hide whole assemblies. Set `TrimmerSingleWarn=false` to see each warning. (`sqlclient-aot`)
- **With `IsAotCompatible=true`, the app's own IL2026 fails the C# compile** under warnings-as-errors, so ILC never runs and its diagnostics stay unseen. To see the ILC result, compile with warnings allowed and set `IlcTreatWarningsAsErrors=true` (52 errors for the SQL tool). (`sqlclient-linux`)
- **AOT-unsafe code passes a normal build** unless the AOT and trim analyzers are on. TUnit's `IsEquivalentTo` without a comparer (`[RequiresUnreferencedCode]`) failed only at AOT publish (IL2026); `-p:EnableAotAnalyzer=true -p:EnableTrimAnalyzer=true` surfaced it in a normal build. (`test-frameworks`)
- **On a `netstandard2.0` project, `EnableAotAnalyzer` or `IsAotCompatible` gives NETSDK1210,** which fails the build under `MSBuildTreatWarningsAsErrors`. Set the analyzers in `Directory.Build.targets`, under a target-framework condition (`net8.0` or later): there the `netstandard2.0` project built with 0 warnings, and a `net11.0` probe with AOT-unsafe code failed on IL2026 and IL3000, as intended. (guidance review probe)
- **A `netstandard2.0` project defaults to C# 7.3,** so file-scoped namespaces and later syntax fail with CS8370. The generator sets `LangVersion` explicitly. (guidance review probe)
- **The Aspire AppHost SDK references `Aspire.Hosting.AppHost` implicitly.** Listing it in `Directory.Packages.props` fails with NU1009. Without the Aspire CLI the build warns ASPIRE010. (`aspire`)
- **Generated sources must use `\n` line endings.** Mixing in `AppendLine` makes Visual Studio show "Inconsistent Line Endings". (`d1-pipeline`)
- **Visual Studio shows generator diagnostics in the Error List, but draws no squiggle.** Report located diagnostics from a `DiagnosticAnalyzer`. (`d1-pipeline`)
- **An analyzer that reports a type's problem from each contract's symbol action reports it once per contract** that uses the type, and from another document than the one it is in. Spar's analyzer reads every contract once per compilation and lets each type's own symbol action report the problems inside it. (M1a diagnostics)
- **Roslyn 5.9's closed-type API is `[Experimental]`** (RSEXPERIMENTAL006). The suppression is agreed and scoped to `src/Spar.Generator/**.cs`. Roslyn 5.9 hosts accept `closed` only at `LanguageVersion.Preview` (CS8652); the SDK 11.0.100 compiler doesn't need it. (`contract-syntax`)
- **The client step and the generator parse with the Roslyn they reference.** The spike's client step used Roslyn 5.9 at `LanguageVersion.Preview`. A contract written in syntax that this Roslyn does not know is skipped without a diagnostic. Parse errors in other files are ignored, because only contract types are read. The generator has the same limit inside an older IDE. Raise the referenced Roslyn with the supported C# version. (`d1-pipeline`, reasoned from the design, not provoked)
- **A C# extractor that binds a project's code needs its implicit global usings,** or binding fails. (`csp-navigation`)
- **Roslyn: a constructor's operation blocks are its body and its `: this(…)`/`: base(…)` initializer, separately;** their parent is the `IConstructorBodyOperation`. An analyzer that looks for that among the blocks finds nothing and reports every constructor-assigned field. (`spar050`)
- **CS0649 covers only private and internal fields** (static ones too), never public fields or auto-properties. CA2019, CA1825, CA1855 and CA2263 are on by default in SDK 11. (`spar050`)
- **C# does not chain two user-defined conversions.** Returning a union case where a `Result<T, TUnion>` is expected fails with CS0029; a `closed record` case converts directly. (contract syntax discussion)
- **C# implicit conversion operators do not convert from an interface-typed expression:** `Result<IReadOnlyList<PlanStep>, E> r = steps;` is CS0029 in both the class and the struct form. C# 15 union conversions do. (`result-api`)
- **A C# 15 union's own `Value` property must be `object`** (CS9386). Put the union members on a nested member-provider interface to keep a typed `Value`. (`result-api`)
- **C# 15 rejects `await` inside a type marked `unsafe`** (CS8652). Mark only the members that use pointers. (`d8-automation`, `linux-desktop`)
- **A `ReadOnlySpan<string>` collection expression used as a `foreach` source cannot cross an `await`** (CS4007). Use a static array. (`d8-automation`)
- **`Interlocked.Exchange<T>` on a struct compiles** (its `T` is unconstrained since .NET 9) but throws `NotSupportedException` at run time. (`spar050`)
- **`ArrayPool<T>.Rent` can return the previous renter's values,** not only defaults. For a `Result`, a stale success leaks between callers. Clear on return or don't pool results. (`spar050`)
- **`MaxBy`/`MinBy` are annotated `T?` but throw on an empty sequence** for value types. (`spar050`)
- **With `PublishAot=true`, `RuntimeFeature.IsDynamicCodeSupported` is false even under the JIT.** To detect a real AOT process, use `IsDynamicCodeCompiled`. The property also turns off reflection-based JSON under the JIT, so a library that needs it (Playwright) fails in any project that sets it. (`d8-automation`, `serializer-bench`)
- **Generic strings in binaries give false positives in boundary scans:** `LoopbackHost` is a `System.Uri` flag name. Scan for markers unique to Spar. (`react`)
- **A Linux NativeAOT publish writes a `.dbg` symbol file beside the executable:** 10.6 MB for the desktop spike, 24.4 MB beside the 17.4 MB SQL tool. It is not needed to run. (`linux-desktop`, `sqlclient-linux`)
- **To see where NativeAOT image size goes,** publish with `-p:IlcGenerateMapFile=true` and read the ILC map. (`cswinrt`, `result-api`)
- **Measured AOT size costs:**
  - the zip reader: +493 KB;
  - a Brotli decoder: +173 KB;
  - Microsoft DI: +0.3–0.9 MB;
  - the OpenTelemetry layer: +4.3 MB on an app that already uses HTTPS (logs alone +1.3 MB);
  - CsWinRT 3.0: +10–23 MB.

  (`react`, `cswinrt`)

- **The MSBuild task analyzer** (`Microsoft.Build.TaskAuthoring.Analyzer` 18.13.0):
  - **Its Info rules, including MSBuildTask0001** (`Console`, `Environment.Exit`, `SetCurrentDirectory`), never appear in command-line build output; only SARIF shows them.
  - **The option is `msbuild_task_analyzer.run_mt_analyzers_on_all_tasks`.** An unknown key such as `msbuild_task_analyzer.scope` is ignored silently.
  - **Its code fixes are not shipped** (dotnet/msbuild PR #15402). Applied by hand, some 0002 fixes don't compile (CS0200, CS1501), and the 0003 fix wraps only the first `File.Copy` argument.
  - **It does not see** static mutable state, `AppContext` switches, culture setters or assembly-load hooks.
  - `MSBuildMultiThreadableTaskAnalyzedAttribute` is missing from Framework 18.12.2. A local `internal` copy in the `Microsoft.Build.Framework` namespace works with the analyzer.

  (`msbuild-task-analyzer`)

- **Under `-mt`, a task without `[MSBuildMultiThreadableTask]` runs in a TaskHost** (`/nodemode:2`, one per thread node), even when it implements `IMultiThreadableTask`. (`msbuild-task-analyzer`)
- **`-mt` strict mode moves the working directory to a temp sentinel.** Relative paths that work under `-m` miss under `-mt`. A working-directory change is error MSB4286, and a write to the sentinel is warning MSB4287. `TreatWarningsAsErrors` doesn't cover MSB warnings, so pass `-warnAsError` where `MSBuildTreatWarningsAsErrors` is off (in `spikes/`). (`msbuild-task-analyzer`)
- **About 29 % of builds that launched new MSBuild processes with `-nodeReuse:false` stalled about 5 s** at node start, cause unknown. Report timing ranges, not single runs. (`msbuild-task-analyzer`)
- **`$(Configuration)` is still empty when a `Directory.Build.props` is read,** unless it was passed on the command line. A variant mapping or a folder name built from it there silently uses the empty string (`obj/test/` without the configuration). Read it where it is known, or with an explicit fallback. (`entry-accessor`)
- **In `Directory.Build.props` the project's own `TargetFramework` is not set yet** (only the root's `net11.0`). Conditions on it belong in a `.targets` file: turning on the AOT analyzer from props broke a netstandard2.0 generator with NETSDK1210. (`entry-accessor`)
- **A netstandard2.0 generator cannot use `record`** without an `IsExternalInit` stand-in (CS0518), and a generator with diagnostics needs `AnalyzerReleases.Shipped.md` and `.Unshipped.md` as additional files, or RS2008 fails the build under warnings as errors. (`entry-accessor`)
- **`PublishAot` set after Microsoft.NET.Sdk's targets are imported is silently ignored:** `dotnet publish` produced a 73 KB framework-dependent apphost and no NativeAOT image, with no warning. A wrapping SDK sets it in its targets file before that import (or in props). (M1a first slice)
- **Moving `BaseIntermediateOutputPath` makes the default globs pick up the old `obj/`:** the NativeAOT test lane restored into `obj/aot/`, and the normal lane's generated files under `obj/Development/` compiled a second time (CS0579, CS0101, CS0111). Exclude every `obj/**` and `bin/**` in `DefaultItemExcludes`, not just the current lane's. (M1a first slice)
- **An item `Update` outside a target cannot test custom metadata in its `Condition`** (MSB4191 for `'%(ProjectReference.SparFramework)'`). Order the items instead: add the items the update must not touch after it. (M1a first slice)
- **`Utf8JsonReader.TryGetDateTimeOffset` accepts a time without an offset** and reads it in the local time zone, so the same JSON means different instants on different machines. The wire rule requires an offset; check for `Z` or `±hh:mm` before parsing. (M1a, `tests/WireShapes.Tests`)
- **NativeAOT keeps test-only code that sits behind an ordinary run-time flag.** A fake registered under `if (testHost)` was still in the Production executable; only code behind a feature switch (`[FeatureSwitchDefinition]` plus a `RuntimeHostConfigurationOption` with `Trim="true"`) was removed. Search the published binary for type names (UTF-8) and string literals (UTF-16) to prove it. (`test-host`)
- **Under Microsoft.Testing.Platform, `dotnet test` passes MSBuild switches it does not own on to the test application:** `dotnet test --project X -nr:false` built, then every test application failed with "Unknown option '--nr'" (exit code 5, zero tests). Set `MSBUILDDISABLENODEREUSE=1` in the environment instead, or build first and run `dotnet test --no-build`. (M1a, `tests/Spar.PackageTests`)
- **An item transform cannot be concatenated with text inside another task parameter** (MSB4012, writing `SparXunitVersion=@(PackageVersion->…)` into `WriteLinesToFile`'s `Lines`). Put the transform in a property first, then use the property. (M1a, `Spar.Sdk` packing)

## dotnet watch and the development loop

- **`dotnet watch` (SDK 11.0.100) does not mark its builds.** There is no property and no variable, and the builds run in reused MSBuild nodes. `CustomCollectWatchItems` runs in dotnet-watch's own process, after each build. The app process does get `DOTNET_WATCH=1`. (`react`)
- **`dotnet watch -p` means `--project`.** Pass properties with `--property:`. (`react`)
- **Stopping `dotnet watch` through a shell wrapper** (`shell: true` plus `taskkill /T`) left `dotnet watch`, `dotnet run` and the app running. Start the real process directly, and stop its tree. (`react`)
- **Vite exits when its standard input ends** (unless `CI=true`). A Vite started through `cmd start /b` inherits its launcher's input, so it lives as long as the `dotnet watch` session, or the pipe. (`react`)

## npm, Vite and TypeScript

See also "Bridge and loopback" for the bridge's message, id and connection limits, "Page security (CSP, headers, permissions)" for the CSP traps of a Vite build (inlined assets, SRI, nonces), and "Tooling and environment" for Playwright.

- **Vue's reactivity wraps class instances in Proxies, and `#private` fields throw through them** (`TypeError: Cannot read private member`). This happens with `ref`, `reactive`, Pinia stores and templates. A `Symbol.toStringTag` getter stops Vue proxying the object; `shallowRef`, `markRaw` and `toRaw` work in app code. A WeakMap keyed by the instance fails the same way. (`frontend-frameworks`)
- **React's `use()` writes `status`, `value` and `_debugInfo` onto the thenable,** so a frozen thenable throws. (`frontend-frameworks`)
- **`no-floating-promises` ignores a floating thenable** unless `checkThenables: true`. A `Promise` subclass is flagged by default. (`frontend-frameworks`)
- **TanStack Query retries a rejected `queryFn` 3 times by default,** which re-runs the backend command. Also, its `queryFn` type `T | Promise<T>` types a returned thenable as the data itself. (`frontend-frameworks`)
- **RxJS `from(asyncIterable)` does not abort on unsubscribe;** the producer runs until its next item. (`frontend-frameworks`)
- **React StrictMode's first, aborted effect ends a subscription with an in-band `cancelled` failure,** which shows briefly unless the handler ignores its own abort. (`frontend-frameworks`)
- **Angular under a strict CSP** needs all of the following, or the page breaks:
  - AOT, since JIT is blocked as `eval` and the app does not start;
  - no `<base href>`, which violates `base-uri 'none'`;
  - no component `styles`, which are injected as `<style>`;
  - `inlineCritical: false`, because the inlined script that switches the stylesheet from `media="print"` is blocked, and the global CSS then never applies.

  Angular 22 also makes `OnPush` the default, which can look like a stale view. (`frontend-frameworks`)
- **Vue's full build compiles templates at run time** and is blocked as `eval`; use the runtime-only build. (`frontend-frameworks`)

- **Tailwind v4 puts plugin styles, such as the typography plugin's, in its utilities layer,** so a rule in the components layer cannot override them, whatever its specificity. Put such overrides outside any layer. (M1a Notebook page)
- **`Intl` formatters without a locale follow the machine, not the page:** an English page showed "1 secondo fa" on an Italian Windows. Pass `document.documentElement.lang`. (M1a Notebook page)
- **Playwright's `PressSequentially` on a textarea types at the start of its text,** where focusing puts the caret. Press `Control+End` first to type where a person would. (M1a Notebook page)
- **Two variants of one app build at once in a solution build and share one `node_modules`:** `npm ci` in both would break it. Install under a lease the variants share, and skip when the other build already installed. (M1a frontend build)
- **npm deletes what it does not know in `node_modules`.** A private junction to the generated client was pruned by `npm i`, deleting the files through it, and `npm ci` wipes everything. Link the client as a declared `file:` dependency outside `node_modules`. (`react`)
- **The generated client path must not depend on the configuration.** With an `obj/Debug/…` path, a clean clone running only `dotnet publish -c Release` fails (TS2307). (`react`)
- **Vite pre-bundles and caches `node_modules`,** so regenerated code there would miss hot updates. `file:` packages outside `node_modules` are treated as source. (`react`)
- **Rolldown could not resolve a bare package import through a junction** (`@spar/sdk` in the spike's layout; the client is now one package, `@spar/app`, with subpaths such as `@spar/app/sdk`). The spike's generated packages import each other relatively. (`react`)
- **`skipLibCheck` skips only `.d.ts` files.** A `.ts` dependency is checked under the app's own flags: an unused local in generated `.ts` fails the user's build. A broken `.d.ts` is silent under `skipLibCheck: true`. (`ts-compiled-contracts`)
- **A `.ts` file beside a `.d.ts` of the same name wins module resolution.** A `core.ts` next to `core.d.ts` would be picked instead of the declaration. Ship the SDK's sources in a folder of their own, never beside a `.d.ts` of the same name (the spike used `sdk/src/`). (`ts-compiled-contracts`)
- **VS Code uses its bundled TypeScript 6.0.3, not 7** (unless the Native Preview extension is installed). TypeScript 7's npm package has no `tsserver.js`, so `typescript.tsdk` cannot point at it. TypeScript 7.0.2's language server offers no quick fixes and no object-literal method snippets for these cases, and its completion details carry no doc comments (`completionItem/resolve` returns `detail` only). (`ts-results`)
- **Rename and some quick fixes edit generated files,** because the package's real path is outside `node_modules`. With a declaration map to C#, TypeScript 7 renames the C# identifier only, not its C# references. Rename in C#, then regenerate. `"preserveSymlinks": true` is no fix: rename was then refused for some symbols and allowed for others. (`ts-results`, `ts-compiled-contracts`)
- **A generic `match` infers its return type from `success` only,** unless each handler group has its own type parameter. Typos give misleading "not assignable to 'never'" errors unless `NoInfer` guards the error keys. Variants that failed: one type parameter per error slot (TS2322 with mixed return types); the exactness check without `NoInfer` (handlers lost contextual typing, TS7006); error handlers not intersected with the generated `ErrorHandlers<E>` (no hover docs, and rename found 3 of 10 locations). (`ts-results`)
- **TypeScript 6.0.3's "Add missing properties" writes stubs that return `unknown`** (`function(error: …): unknown { throw new Error("Function not implemented."); }`), which widens the `match` result to `unknown` until each stub is edited. (`ts-results`)
- **Bundlers keep every method of a class,** and every property of an exported object literal: all six commands of an imported contract stayed in the bundle. Only free functions and unused modules are tree-shaken. (`ts-results`, `ts-compiled-contracts`)
- **Vite's watcher coalesces quick successive writes.** A fix written milliseconds after a broken file was never applied; a 1 s pause between the writes fixed it. Matters for HMR test harnesses. (`csp-navigation`)
- **Behind a proxy, Vite's default HMR client connects to `wss://<page origin>`.** Point `server.hmr` at the dev server. (`desktop-origin`)
- **`ignore-scripts`, in `.npmrc` or passed to `npm run`, also skips the app's own `pre`/`post` script hooks.** Pass `--ignore-scripts` on the `npm ci` command line only. (`npm-ignore-scripts`)

- **Async iteration in the generated client:**
  - **Async generators layered over another async iterable cost about +100 ns and +320 B per item** on WebView2. Generate hand-written iterators.
  - **Wrap stream items when the iterator hands them out,** not when a batch is decoded. Wrapping at decode makes a stalled consumer hold about 40 % more memory per buffered row.

  (`stream-result-cost`)

### npm and Vite: from earlier versions, not rechecked

The entries below come from earlier Spar versions. They have not been rechecked on the current tools.

- **`npm ci` audits by default, with a 5-minute network timeout;** an earlier version's CI job hung for 30 minutes. Use `--no-audit` and check vulnerabilities as a separate step. (earlier version)
- **An inherited stdin stalled `npm install`.** Give child processes a closed, redirected stdin. (earlier version)
- **Rolldown's optional native packages need optional dependencies installed** (`--include=optional` when they are omitted). (earlier version)
- **Logs or profiles inside a folder that Vite or `dotnet watch` watches make the watcher react to its own output;** Vite once watched a locked browser cookie database. (earlier version)

## Windows host (WebView2)

See also "Bridge and loopback" for WebView2 web messages, the loopback transport and the heartbeat, and "Page security (CSP, headers, permissions)" for WebSockets and CSP, local network access, cancelled navigations, the clipboard and the camera on WebView2.

- **`WebResourceRequested` cannot stream.** `IStream::Read` runs on the UI thread (8,664 of 8,664 calls), the whole response is read before the page sees it, `E_PENDING` ends it, and there is no back-pressure. WebView2 also calls `Seek(0, STREAM_SEEK_SET)`, and never `Stat`. (`react`)
- **Custom responses are never decoded:** with `Content-Encoding: br` or `gzip`, the page receives the compressed bytes. (`react`)
- **Custom responses are always copied on Windows.** `SHCreateMemStream` copies its input, and with `CreateWebResourceResponse` it cost about 2.5 ms for 2 requests, even with nothing to decode. WebKitGTK's `g_bytes_new_static` serves the image in place. (`asset-packing`)
- **Under NativeAOT, `GetManifestResourceStream` returns an `UnmanagedMemoryStream` over the image,** on both OSes. Stored entries need no managed copy, but `SHCreateMemStream` still copies them on Windows. (`asset-packing`)
- **A hidden or minimized window breaks automation:** trusted input hangs and timers are throttled. `--disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows` restore the timers in a hidden window, but trusted input still never completes and `requestAnimationFrame` never fires. Minimized, a click took about 940 ms and screenshots never completed. Use a shown window placed offscreen. (`d8-automation`)
- **The first trusted input after navigation can stall for 5 s.** Wait two animation frames first. (`d8-automation`)
- **Rare 5 s stalls, unexplained:** once in about 400 runs between `NavigationStarting` and the HTML request, and once in 5 background Brotli decodes (5,012 ms against 66–78 ms). (`theme-startup`, `asset-packing`)
- **The browser process can take about 6.5 s to exit** after close, with any binding: 12 of 12 AOT runs in `cswinrt` (6.44–6.53 s, both bindings), 23 of 104 runs in `asset-packing`, 2 of 14 in `react`; the other runs took 235–284 ms.
  - **Cause, confirmed:** SmartScreen. A close within about 0.5 s of a navigation finds its reputation request (`nav-edge.smartscreen.microsoft.com`) still in its TLS connect, and the browser waits for it (6.40–6.49 s, 30 of 30). Closing 0.75 s or more after the navigation took 120–167 ms.
  - **Fixes:** `IsReputationCheckingRequired = false` (52–67 ms), `--disable-features=msSmartScreenProtection`, or `MAP * ~NOTFOUND`. `MAP app.localhost ~NOTFOUND`, `--no-proxy-server` and `--disable-background-networking` do not help.
  - **Exiting without waiting** lost no data in 330 runs, even with the host killed.
  - Wait for the exit, not for the window, only before reusing or deleting a profile.
  - **Earlier versions saw the same** (`docs/research-review.md`): a WebView closed shortly after it became ready took about 6.4 s to exit, and about 210 ms when closed later. ETW showed SmartScreen's shutdown work, about 101 sleeps of about 60 ms each, and `IsReputationCheckingRequired=false` removed it in 18 of 18 runs. Desktop tests that close right after `ui-ready` pay this every time.

  (`react`, `cswinrt`, `asset-packing`, `webview-profiles`)
- **`BrowserProcessExited` fires about 160 ms after the real kernel exit, and never after a failed controller creation.** A host that waits for it exits after about 326 ms instead of 20 ms. (`webview-profiles`)
- **`ICoreWebView2Profile8.Delete` poisons the profile name for good:** creating it again fails with `0x8007012F` (`ERROR_DELETE_PENDING`) at every later start (8 of 8), because `Local State` keeps it in `profile_basenames_deleted`. Use `ClearBrowsingDataAll` instead. (`webview-profiles`)
- **`ClearBrowsingData(ALL_SITE)` keeps the `History` file,** which can hold data from page URLs. Add `BROWSING_HISTORY`, or use `ClearBrowsingDataAll`. (`webview-profiles`)
- **A second app instance with different environment options** (additional browser arguments included) creates its environment, then fails at controller creation with `0x8007139F` after about 37 ms: environments sharing one profile folder must use identical options. If the first instance is exclusive, it fails with `0x8000FFFF`. Map both to a clear "another instance with different settings is running" error. (`webview-profiles`; earlier versions hit the same rule)
- **InPrivate's `ProfilePath` reports the parent profile's folder,** but InPrivate writes no site data. Its data is shared by the InPrivate windows of that profile and dropped when the last of them closes. (`webview-profiles`)
- **`WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` is appended even in Production,** so remote debugging can be enabled from outside. `DevToolsActivePort` goes stale, and a fixed port fails silently. (`d8-automation`)
- **`AreDevToolsEnabled=false` does not gate in-process CDP.** Every automation call worked the same with it off. Only a compile-time or trim-time exclusion keeps automation out of a build. (`d8-automation`)
- **A page `fetch` abort is invisible to the host.** Cancellation needs an explicit message. (`react`)
- **Hand-written COM:**
  - a missing vtable slot shifts every later method and crashes. A base interface must declare every slot: `IFileDialog` without its last slot (`SetFilter`, slot 26) put every `IFileOpenDialog` method one slot off (segfault under AOT, `0xC0000005` under the JIT). The rule also covers inherited interfaces: an earlier version chose `ICoreWebView2Environment5` without declaring slots 2–4, which shifted the vtable and crashed;
  - an interface whose methods are never reachable fails its cast only under AOT, at run time, with no warning (`IFileSaveDialog`). Ask for the interface whose methods you call.

  Test slot order. (`native-services`)
- **The static WebView2 loader exists only under NativeAOT.** Link it with `<DirectPInvoke Include="WebView2Loader" />` and a `NativeLibrary` item for `WebView2LoaderStatic.lib`, from `Microsoft.Web.WebView2` referenced with `ExcludeAssets="all" GeneratePathProperty="true"`. The link needs no extra libraries, and the publish folder then has no `WebView2Loader.dll`. CoreCLR publications must ship `WebView2Loader.dll`. (`react`, `d8-automation`, `bridge-encoding`)
- **`CREDENTIALW` is 80 bytes on x64,** with `FILETIME` declared as `long` at offset 24. Assert the size at run time. (`native-services`)
- **A `.local` app origin goes to name resolution.** After the machine's network changed (hotspot, VPN), `https://app.spar.local` added about 2 s to every page load; `app.localhost` loaded in 54 ms and `app.spar.invalid` in 89 ms. Use a name that never resolves. (`asset-packing`)
- **A window class with no brush is shown white by DWM until something paints it.** That, not the WebView, is the startup flash. (`theme-startup`)
- **In dark mode WebView2 paints Chromium's `#121212` before any document has painted,** for about 900 ms, whatever opaque `DefaultBackgroundColor` is set. Use a transparent default background over a painted window. (`theme-startup`)
- **A WebView2 controller under a hidden parent window does not render,** so the page never reaches `ui-ready`, not even after `ShowWindow`. Toggling the controller's visibility (`put_IsVisible(false)`, then `true`) after the show makes it render (the app was visible 20–40 ms after the show), but a start hidden until `ui-ready` still always waits for its timeout. DWM cloaking works, but a slow start then shows nothing for over a second. Spar's answer is to show the window at once in the theme colour, never hidden and never cloaked (decision "Theme and splash choices"). (`theme-startup`)
- **Chromium holds the first frame of a page with nothing contentful** until contentful paint or about 500 ms: `first-paint` came 576–604 ms after navigation, against `domInteractive` at about 40 ms. Static text in the HTML brought the first frame to 172 ms. (`theme-startup`)
- **`PrintWindow` waits for the window's thread:** joining the capture thread from the window thread deadlocked. A print that races the first paint returns an empty surface. Windows.Graphics.Capture rejects a hidden window, but a cloaked one works. Earlier versions also got black `PrintWindow` screenshots of hidden windows, and the old frame in a screenshot taken right after a resize (not rechecked). (`theme-startup`)
- **An unanswered request to the app origin leaves the machine's loopback:** each opened 2 TLS connections to `[::1]:443`. Answer every request, with a 404 for unknown paths. A `sendBeacon` to an unanswered path still reports `queued=true`, so the page never learns. (`desktop-origin`)
- **Every navigation preconnects to `[::1]:443`** even when the host answers every request. Only `--host-resolver-rules="MAP app.localhost ~NOTFOUND"` stopped it, and its value must be quoted inside the argument: unquoted, Chromium kept only the first word. (`desktop-origin`)
- **The runtime makes requests the app never asked for.** The net logs showed SmartScreen (`nav-edge.smartscreen.microsoft.com/.../navigate/3`) on navigation to the app origin, `data-edge` and `telem-edge` SmartScreen hosts, `config.edge.skype.com/config/v1/Edge/…`, and 5 `wpad` lookups per run. Whether `IsReputationCheckingRequired=false` stops the SmartScreen calls is untested. (`desktop-origin`)
- **The legacy `AddWebResourceRequestedFilter` misses shared and service workers:** service-worker registration fails, and shared-worker fetches leak. Use the source-kinds filter (`ICoreWebView2_22`). (`desktop-origin`)
- **Shell file dialogs:**
  - `ClearClientData` returns `E_FAIL` when nothing is stored for the client GUID, so declare it `[PreserveSig]`;
  - `Close(E_ABORT)` from `OnFolderChange` makes `Show` return `0x80004004` without ever showing the window;
  - `FOS_FILEMUSTEXIST` shows a native error box for a missing name;
  - Save's default overwrite confirmation is a task dialog with Yes and No only, and it ignores `IDCANCEL`. Answering `OnOverwrite` with `FDEOR_ACCEPT` suppresses it;
  - an MTA caller works, but COM runs the dialog on its own host STA, on another thread.

  (`native-services`)
- **Automating native dialogs in tests:**
  - commands posted before the dialog is visible are ignored: the first `OnFolderChange` arrives before the show, so wait for `IsWindowVisible`;
  - a dialog moved offscreen repositions itself onto the desktop at (0,0) when shown. Make it transparent (`WS_EX_LAYERED`, alpha 0) instead;
  - message boxes and task dialogs reset `WS_EX_LAYERED` while initialising, and appeared centred on screen until a thread-local `WH_CBT` hook re-hid them on `HCBT_CREATEWND` and `HCBT_ACTIVATE`;
  - the WinRT `FileOpenPicker` is brokered: its dialog belongs to `PickerHost.exe`, so a watcher of your own process's windows hangs. Find it by owner HWND.

  (`native-services`, `cswinrt`)
- **WinRT toasts need an app identity.** In an unpackaged app, `CreateToastNotifier()` throws `0x80070490`, also when given an AUMID that is not registered, and `Package.Current` throws `0x80073D54`. A registered AUMID (shortcut or registry) was not tried. (`cswinrt`)
- **Every notification-area icon leaves a permanent per-user entry,** listed in the taskbar settings, even after its executable is deleted. Spar v5's tests left one for each test executable, and the user removed them from the registry by hand.
  - **Where Windows keeps them (Windows 11):** `HKCU\Control Panel\NotifyIconSettings\<number>`. Each key holds the values `ExecutablePath`, `UID` or `IconGuid`, `InitialTooltip`, `IsPromoted`, `IconSnapshot` and `Publisher`. On 2026-11-14 this machine had 40 entries and none from Spar.
  - **How an icon is identified:** by the executable path plus the `uID`, or by `guidItem` when `NIF_GUID` is set. A GUID is tied to the executable's path ([`NOTIFYICONDATA`](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-notifyicondataw)). So every new test path, such as a temporary folder or a per-run publish folder, creates a new entry.
  - **Rules for Spar's tests and spikes:**
    - Prefer Windows Sandbox or a fake shell for anything that calls `Shell_NotifyIcon`.
    - If a real icon must appear on the developer's machine, give it one fixed executable path under `artifacts/`, so that it reuses one entry.
    - Clean up only the keys whose `ExecutablePath` lies under this repository's `artifacts/` or `spikes/`. Never delete other keys.
    - Whether Explorer rewrites a deleted key from memory, and whether a cleanup needs Explorer to restart, is unverified; restarting Explorer needs the user's consent.
- **Custom title bar:**
  - **The first click on a page-drawn caption button of an inactive window is lost** if the host clears the press on `WM_NCMOUSELEAVE`: activation sends it between press and release. (`windows-sandbox-checks`)
  - **UIA: `ProviderOptions_RefuseNonClientSupport` on a top-level provider removes the phantom native title bar;** `IAccPropServices` annotation reports success and changes nothing, and answering `WM_GETTITLEBARINFOEX` only moves the phantom buttons. (`windows-sandbox-checks`)
  - **WebView2's windowed drag regions are child windows that rewrite hit codes.** About 14 transparent windows, one per drag rectangle, forward `WM_NCHITTEST` to the parent but return `HTCAPTION` for everything except resize codes. Caption-button codes need a host-owned window above the WebView.
  - **`HTTRANSPARENT` from a child window passes only to windows of the same thread,** so it never reaches past the WebView's window, which belongs to another process.
  - **A `WS_EX_NOREDIRECTIONBITMAP` child above the windowed WebView hides it;** `WS_EX_TRANSPARENT` does not, and still receives hit tests.
  - **`DwmDefWindowProc` stops reporting the caption buttons when the window is maximized** with a custom `WM_NCCALCSIZE`. Clicks on the visible restore button do nothing, with either controller.
  - **GDI paints alpha 0 in an extended frame:** a dark brush showed DWM's `#4a4a4a` until the page painted.
  - **Visual hosting shows no system menu on right-click** in a drag region, neither with `SendMouseInput` forwarding nor with `DefWindowProc` (no `WM_CONTEXTMENU`). The host must show it.
  - **`GetNonClientRegionAtPoint` reports only caption or client:** no CSS value marks the minimize, maximize or close regions.

  (`windows-titlebar`)
- **The managed UI Automation client (`System.Windows.Automation`) is misleading:** it showed no title bar even for a standard window, and none of the page under visual hosting. Use the native UI Automation client for accessibility checks. (`windows-titlebar`)
- **Windows Sandbox:**
  - The guest's DPI follows the client window's monitor: DisplayConfig is denied (100 % only), `SPI_SETLOGICALDPIOVERRIDE` fails, and `LogPixels` with `IgnoreClientDesktopScaleFactor` is ignored. After `wsb connect` the client window restores and activates itself on the host. (`windows-sandbox-checks`)
  - `wsb exec -r ExistingLogin` fails with 0x80070520 until a session exists. A `wsb connect`, minimized at once, creates one, and the guest keeps rendering and accepting `SendInput` while minimized.
  - The guest's WebView2 registration (157.0.4341.42) pointed at files that were not there (157.0.4341.46), so environment creation failed with 0x80070002 until the runtime folder was passed explicitly. An earlier version saw the same kind of mismatch (156 registered, 157 on disk).
  - Files added to a mapped folder appear in the guest after a few seconds, and a file replaced in place is reported as "not a valid application". Stage new file names and poll for them.
  - `Start-Process -WindowStyle Hidden` makes the app's first `ShowWindow` hide the window.
  - `wsb exec` with `powershell -File` cannot pass an array parameter.
  - `SendInput` arrow keys need `KEYEVENTF_EXTENDEDKEY` for the `Win+arrow` shortcuts.
  - **The guest grants the foreground to every process:** a relaunch took the foreground in 15 of 15 cases, controls without `AllowSetForegroundWindow` and without a click included, with the default `ForegroundLockTimeout`. On the desktop the same control failed 3 of 3. Check focus rules on a real desktop. (`windows-single-instance`)
  - `wsb exec -r System` runs a command as SYSTEM in the guest: a second account for cross-user checks, without creating a user. `wsb connect` did not return while the session stayed open. (`windows-single-instance`)
- **Blocking the page's file pickers** (`window-tests`):
  - **`Page.setInterceptFileChooserDialog` does nothing without `Page.enable` first:** it returns `{}` and the dialog opens.
  - **It covers one WebView, and not a cross-site iframe,** which needs `Target.setAutoAttach` (flatten) plus the same calls in each child session.
  - **`Permissions-Policy` and CSP `sandbox` do not block the File System Access pickers;** only `--disable-blink-features=FileSystemAccessLocal` removes them.
- **A named profile's first controller in a fresh user-data folder took 5.2 s** in 5 of 13 runs; InPrivate on the default profile never stalled. (`window-tests`)
- **`EBWebView\lockfile` shows whether a browser still uses a user-data folder:** held while it lives, gone when it exits (also after a kill), never left behind; deleting the folder while it is held fails with `0x80070020`. (`window-tests`)
- **Windows Sandbox for test runs** (`sandbox-runner`):
  - **only one sandbox runs per user session:** a second `wsb start` fails with `0x800401F6` (`CO_E_APPSINGLEUSE`);
  - **WebView2 needs the user session:** as SYSTEM, `CreateCoreWebView2Controller` failed with `0x80080005`;
  - **xUnit writes TRX and XML only at the end,** so a run killed at a deadline returns no report;
  - **client windows stayed open after `wsb stop`** in 6 of 11 connected sessions; close them with `WM_CLOSE`;
  - **in a generated `cmd` script,** `echo 1> file` writes "ECHO is off.", and parentheses around an echo break on `(x86)`: put the redirection first.
- **Single instance and named pipes** (`windows-single-instance`):
  - **`PipeOptions.CurrentUserOnly` follows the token's owner, not its user.** For an elevated administrator the owner is `BUILTIN\Administrators`, so the server's pipe was `O:BA D:(A;;0x1f019f;;;BA)`, open to every administrator: a SYSTEM process connected. The client's check compares owners too, so a SYSTEM squatter whose pipe Administrators owned received an elevated client's arguments. Build the DACL from the token's user SID, and check the user of the server process (`GetNamedPipeServerProcessId`, then its token), not the pipe's owner.
  - **`DisconnectNamedPipe` discards data the client has not read.** Wait for the client to close its end before disconnecting, or the acknowledgement can be lost.
  - **`NamedPipeClientStream.Connect(timeout)` retries a pipe that does not exist until the timeout,** so a missing pipe and a busy one both end as `TimeoutException`.
  - **A pipe name from the data folder's path misses a junction:** the second process found the lock held and no pipe. Name it from the lock file's identity (volume serial number and file index), which an open with no access rights can read while another process holds the file with `FileShare.None`. `Path.GetFullPath` does expand 8.3 short names.
  - **`AllowSetForegroundWindow`'s return value says nothing:** it returned true in background processes that could not give the foreground away. Only the primary's `SetForegroundWindow` result shows whether focus moved.
- **A raw `CreateFileW` past `MAX_PATH` fails with error 3** (path not found) without the `\\?\` prefix; .NET's own file APIs add it, P/Invoke does not. (`windows-single-instance`, a 384-character data folder)

  (`windows-titlebar`)

- **Timing and memory inside the page:**
  - `performance.now()` resolves 100 µs in WebView2 and Chromium unless the page is cross-origin isolated (COOP + COEP), and then 5 µs.
  - `performance.memory` returns a new snapshot on every read; a cached object reads the same numbers forever.

  (`call-promise-cost`)

- **Detecting a missing or broken WebView2 Runtime:**
  - **Environment creation doesn't start the browser.** It succeeded with the browser deleted or replaced. Only controller creation fails (`0x80070002`, `0x800700AA`), and `BrowserProcessExited` never fires.
  - **The loader follows `ClientState\…\EBWebView`.** A stale path reads as "not found" while the registry version looks right, so a registry check would say "installed". This is the cause behind the Sandbox registration mismatch.
  - **A runtime folder missing `EmbeddedBrowserWebView.dll`** is reported as not found, not as broken.
  - **Without `[STAThread]`, environment creation fails with `0x80010106`,** which looks like a runtime problem.

  (`startup-prerequisites`)

- **Native dialogs from a NativeAOT exe:**
  - **`TaskDialogIndirect` needs a Common Controls 6 manifest.** Without one it throws `EntryPointNotFoundException`, and the exe still starts.
  - **`MessageBoxW` is only system-DPI-aware** in a per-monitor-aware process. Neither dialog follows dark mode.
  - **The task dialog never opens links.** The host calls `ShellExecuteW`. Returning `S_FALSE` from a button click keeps the dialog open.
  - **An unregistered event-log source** writes, but viewers prefix "description cannot be found", and `Get-WinEvent` can't find it.
  - **A GUI-subsystem exe gets standard handles only when they are inherited.** It must attach to the parent console to write there. `%ERRORLEVEL%` on a single `cmd` line is expanded before the app runs.

  (`startup-prerequisites`)

- **Reading WebView2 process memory:**
  - **`GetProcessInfos` fails off the STA** with `0x802A000C` (`UI_E_WRONG_THREAD`).
  - **It omits the crashpad handler,** and counts per user-data folder, not per app: a second instance on the same folder counts both renderers.
  - **`Process.PrivateMemorySize64` costs 3.2 ms on Windows** for one process. `PROCESS_MEMORY_COUNTERS_EX2` with a kept handle costs 135 ns, and its `PrivateWorkingSetSize` is what Task Manager shows.
  - **`GC.GetGCMemoryInfo` reports 0 before the first GC.**
  - **NTFS directory-entry sizes lag for files open for writing.** They were 1.0 MB short 13 s after the write.
  - **`EnumerationOptions` skips Hidden and System files by default.** A size walk must set `AttributesToSkip = 0`.
  - **`navigator.storage.estimate()` covers only IndexedDB and Cache Storage on WebView2** (12.6 of 26 MB), and is missing on WebKitGTK.

  (`resource-usage`)

- **A hung page:**
  - **Reload, Stop + Reload, same-site navigation and `edge://kill` do not recover a hung WebView2 renderer.** Terminate its process (the id comes from `GetProcessInfos`; there is no API), then reload once.
  - **WebView2 raises `ProcessFailed(RenderProcessUnresponsive)` only about 16 s after input reaches a hung page,** a dialog's focus change included. It never does for an idle hang, and 30 s after a reload into one. An earlier version saw the same: the event fires only when input goes unacknowledged, and an idle hang produced nothing in 60 s.
  - **A suspended WebView answers no web messages,** and on resume they all arrive at once. Never ping a suspended or hidden WebView.
  - **Messages posted to a hung page queue in its renderer,** about 1.6 kB each for 1 KiB. Keep one outstanding.
  - **An endless microtask chain makes the renderer exit with out-of-memory** after about 20 s, rather than hang.

  (`page-heartbeat`)
- **An app built as `OutputType` `Exe` is a console program on Windows:** it opens a console window, `Process.MainWindowHandle` can return that console instead of the app's window, and `CloseMainWindow` then ends the app with `0xC000013A` (1 of 3 black-box runs). Desktop apps are `WinExe`. (`test-host`)

### Windows host: from earlier versions and other frameworks, not rechecked

The entries below come from earlier Spar versions and other frameworks (`docs/research-review.md`). They have not been rechecked on the current Runtime.

- **A confirmed `WM_ENDSESSION` was ignored** by a window that accepted `WM_QUERYENDSESSION`, so nothing was cleaned up at sign-out or shutdown.
- **Focus:** without forwarding `WM_SETFOCUS` to `MoveFocus(Programmatic)`, Tab never reaches the page. Playwright's CDP focus emulation hides `visibilityState` bugs.
- **WebView2 counts any navigation within about 5 s after `ExecuteScript` as user-initiated.**
- **A file sitting where the profile folder should be** caused a startup delay of about 65 s.
- **A long data path** (the root lengthened from 114 to 169 characters) broke PDF staging and `localStorage` persistence.
- **`Process.CloseMainWindow()` does not find hidden or offscreen windows.** Post `WM_CLOSE` to the recorded window handle.
- **Returning directly from `WM_NCCREATE` broke the window title.**
- **The CI runner ignored the WebView2 runtime-folder environment variable;** only the API parameter worked. In an elevated process (the Windows Sandbox runner), WebView2 ignores environment overrides altogether.
- **A visible-window teardown leaked handles in WebView2's network service** that hidden-window tests never showed. The mitigation: hide the controller before `Close`, navigate to a blank page, and post the final close outside the callback.
- **Unpackaged Windows apps get balloon notifications only;** in development, notifications show PowerShell's name and icon.

## Linux host (WebKitGTK)

See also "Bridge and loopback" for the heartbeat, and "Page security (CSP, headers, permissions)" for `nosniff`, `Permissions-Policy`, the clipboard setting, injected scripts under the `eval` ban, and WebAssembly and CSP on WebKitGTK.

- **A second instance on the same WebKitGTK data folders starts without any error and corrupts data:** after both exited, `localStorage` was empty, and both instances' writes were lost (2 of 2 on each API). Enforce single instance before the network session exists. (`webview-profiles`)
- **On a custom scheme (`spar://`), WebKitGTK stores no cookies, and Cache Storage rejects the requests.** Service workers don't work on custom schemes either (from other frameworks, not rechecked). (`webview-profiles`)
- **WebKit's website-data type constants differ between the APIs:** `ALL` is `0xFFF` on 6.0 and `0x3FFF` on 4.1. (`webview-profiles`)
- **Destroying a WebKitGTK window runs `pagehide`, `visibilitychange` and `unload`, but not `beforeunload`.** (`webview-profiles`)
- **GTK and its dialogs write to the real user config.** A GTK3 file chooser run without private folders created `~/.config/dconf/user` and `~/.config/gtk-3.0`. Printing added `~/.local/share/recently-used.xbel`, and restore added `~/.nuget/NuGet/NuGet.Config`. In tests and spikes, point `HOME`, `XDG_CONFIG_HOME`, `XDG_DATA_HOME`, `XDG_CACHE_HOME` and `XDG_STATE_HOME` at a private folder before starting anything. (`linux-integration`)
- **WebKitGTK ignores `app-region` / `-webkit-app-region`:** `CSS.supports` is false and the computed value is empty. A page-drawn title bar needs the host to start the move. Start it after a small threshold (4 px), because starting on press lost the double-click on GTK4. (`linux-integration`)
- **GTK4: web processes outlive the window after a window-manager move or resize,** for more than 10 s in 5 of 5 runs, until the next click in the page. GTK3 is not affected. **Cause:** the window manager grabs the pointer, so the release never arrives. Two WebKit gestures on the view keep an open press, a GTK4 gesture holds a strong reference on its widget while pressed, and destroying a GTK4 window only detaches the view. **Fix:** call `gtk_event_controller_reset` on every controller of the view right after `gdk_toplevel_begin_move`/`begin_resize`; the processes then exit in 24–26 ms, 5 of 5. Disabling the view before destroying the window also works. `webkit_web_view_terminate_web_process` leaves the network process alive. After any window-manager drag the page also sees a mousedown with no mouseup, on both APIs. (`linux-integration`, `linux-memory`)
- **Memory on a software GPU (llvmpipe):** WebKit's accelerated path costs about 90 MB per web process, and GTK4's default GL renderer about 75 MB in the app process. `GSK_RENDERER=cairo` removes the second. libLLVM and Mesa stay partly resident even with everything off, because WebKit sets up an EGL display anyway, as does GTK4 on X11. (`linux-memory`)
- **Memory readings are distorted by other WebKit apps in the same distribution:** shared libraries split their PSS, and two runs read up to 66 MB low. Record foreign processes at every snapshot and discard affected runs. (`linux-memory`)
- **Electron in WSL needs NSS and NSPR,** which a minimal Ubuntu lacks. `apt-get download` plus extraction works without root. (`linux-memory`)
- **WSLg Wayland, GTK4:**
  - A window started maximized is killed by the compositor (Wayland error 71, 2 of 2). Maximize after the window first maps.
  - A hidden window keeps showing its last frame. Unrealize it after hiding; it then comes back without its shadow.

  Probably compositor behaviour; not checked on a real desktop. (`linux-integration`)
- **Window position:** Wayland gives none. On X11, GTK3's position API drifts by the frame size at each restart, and GTK4 needs Xlib's `XMoveWindow`. (`linux-integration`)
- **Tray hosts call the grouped dbusmenu methods** (`EventGroup`, `AboutToShowGroup`; xfce4-panel). An item that implements only `Event` and `AboutToShow` gets no menu clicks. (`linux-integration`)
- **GApplication uniqueness waits forever when the first instance hangs,** and a launch during the first instance's exit is lost. A lock file plus a Unix socket fails clearly after its timeout. (`linux-integration`)
- **WSLg cannot test window moves with synthetic input:** nothing moved, not even weston's own title bar. Use a real X11 window manager (openbox in a container). (`linux-integration`)
- **No origin information:** `fetch` requests to the custom scheme carry no `Origin`, and script messages don't say which frame sent them, so an iframe's message can't be told apart from the window's (iframes posting to the handler were not tried in the spike). (`linux-desktop`)
- **Custom-scheme responses stream, but with no back-pressure:** a paused reader grew the web process by 826 MB. (`linux-desktop`)
- **The GTK4 web process is always sandboxed:** it needs a non-root user and user namespaces (in containers, `seccomp=unconfined`). As root under Docker's defaults, `bwrap` cannot create its namespace and the app aborts with SIGTRAP (exit 133). (`linux-desktop`)
- **`WEBKIT_INSPECTOR_SERVER` opens a local TCP port** even in a production-style run, so clear it. `WEBKIT_INSPECTOR_HTTP_SERVER` hung the app. (`linux-desktop`)
- **Missing WebKit pieces fail late and unclearly,** so check them at startup and report clearly:
  - a missing WebKit library throws an unhandled `DllNotFoundException` on the UI thread after the window exists (exit 134, 253–813 ms). This was seen on the 4.1 build (`libwebkit2gtk-4.1.so.0`); on 6.0, `libwebkitgtk-6.0.so.4` was hidden only with the startup check in place. The loader probes the app folder before the system;
  - a missing helper folder (`WebKitNetworkProcess`, `WebKitWebProcess`) gives a GLib error and SIGTRAP (exit 133) after 1.9–3.5 s;
  - a missing `bwrap` (GTK4) gives "Permission denied" and SIGTRAP after 3.0–4.3 s.

  (`linux-desktop`, `linux-wsl-desktop`)
- **The web processes stay alive** unless the GLib main loop keeps running until they exit. (`linux-desktop`)
- **GStreamer, Mesa, dconf and the GTK4 sandbox write to `$HOME/.cache`,** whatever data directory WebKit is given. A crashed 6.0 run leaves `$HOME/.cache/.flatpak/webkit-<pid>-*` behind. Tests should point `HOME` or `XDG_CACHE_HOME` at a disposable folder. (`linux-desktop`)
- **There is no in-process trusted input** (no CDP). WebKitWebDriver types and clicks only on GTK3. XTEST works under X11, including XWayland, on GTK3 and GTK4. Native Wayland has none: WSLg's weston offers no virtual-keyboard, virtual-pointer or EI protocol. (`linux-desktop`, `linux-wsl-desktop`)
- **Show the window only after the web view is attached and loading.** Shown first, GTK3 on X11 without the sandbox stalled about 0.5 s: WebKit's draw handler waits for a first frame the web process cannot produce yet. This is the 686–711 ms (against 201–244 ms sandboxed) start seen under Xvfb. The page also lacks focus, and the first Tab is lost. (`linux-wsl-desktop`, `linux-desktop`)
- **On a software GPU (llvmpipe), WebKit's accelerated path costs about 300 ms on the cold first frame,** and GTK4's GL renderer adds more (`GSK_RENDERER=cairo` removes it). `WEBKIT_DISABLE_DMABUF_RENDERER=1` turns acceleration off entirely in 2.52. (`linux-wsl-desktop`)
- **WSLg without `render` group access uses llvmpipe.** Forcing `GALLIUM_DRIVER=d3d12` crashed GTK3 at exit (segfault, exit 139, in 4 of 4 runs). On GTK4 the sandbox blocks `/dev/dxg`: the web process could not create its EGL display and crashed, the window stayed blank, and `web-process-terminated` fired after 8.8 s. (`linux-wsl-desktop`)
- **The Settings portal can be absent:** under WSLg, `xdg-desktop-portal` runs without a Settings interface, so `ReadOne` fails with `UnknownMethod`. The cause: `XDG_CURRENT_DESKTOP` is empty, `gtk.portal` declares `UseIn=gnome`, and the fallback does not cover Settings; it needs a desktop name and a `portals.conf` (`[preferred] default=gtk`). With no session bus, `g_bus_get_sync` fails ("Failed to execute child process dbus-launch"); with a bus but no portal, `ServiceUnknown` comes after 1.9 ms. GTK settings also differ by backend: Wayland reads GSettings, X11 uses defaults. (`linux-wsl-desktop`, `theme-startup`)
- **Scaling and decorations:** `GDK_SCALE` applies only to X11, and `GDK_DPI_SCALE` becomes a page zoom on 4.1 (ignored on 6.0). On Wayland under WSLg, GTK's title bar has only a close button, and GTK3's client-side decorations add a 26 px shadow margin (a 1012×809 window for 960×720 content). (`linux-wsl-desktop`)
- **On X11 the window is black until GTK's first paint,** and GTK4's surface `mapped` notification is unusable as a map signal: it came at 405–1112 ms, after `ui-ready`, while the window was viewable at 81–101 ms. (`linux-wsl-desktop`)
- **Bind `dlerror` before the first `dlopen`,** or the first failure's reason comes back empty. (`linux-wsl-desktop`)
- **WebKit's release log goes to journald,** and only with `WEBKIT_DEBUG` set. (`linux-wsl-desktop`)
- **The WSL distribution stops between `wsl` calls,** so the first window of a new call takes about 3.5 s longer. (`linux-wsl-desktop`)
- **A transparent WebView background is black on GTK3/X11 without a compositor.** Use an opaque background colour on Linux. (`theme-startup`)
- **Ubuntu's Yaru theme keeps the GTK3 header bar light** despite the dark preference; GTK4's follows it. (`theme-startup`)
- **Under Xvfb, the DMA-BUF renderer's output never reaches the window;** disable it for captures. Run Xvfb as the app's user, or Mesa cannot attach shared memory and GTK4 shows nothing. (`theme-startup`)
- **A redirect answered by a scheme handler is not followed:** the 302 body becomes the page. (`csp-navigation`)
- **A reload under `Cache-Control: no-store` skipped a modulepreloaded chunk:** `webkit_web_view_reload` re-requested `index.html` and the entry chunk, but not the preloaded one (1 run, cause not investigated). WebView2 re-requested everything. (`linux-desktop`)

- **Measuring in the page on WebKitGTK:**
  - `performance.now()` resolves only 1 ms, so page-side medians of short calls read 0 or 1 ms. Time from the host, or use means;
  - there is no `performance.memory`, so per-call memory needs process RSS;
  - `navigator.hardwareConcurrency` is capped at 8.

  (`call-promise-cost`, `linux-desktop`)

- **Start-up messages on Linux:**
  - **GTK 4 dialogs take 420–820 ms on a software GPU,** and 91–212 ms with `GSK_RENDERER=cairo`.
  - **Without `g_set_prgname`, windows have no class or app id.** The window manager showed "Unnamed Window".
  - **A denied web-process sandbox also happens as a normal user under Docker's default profile.** It ends in exit 133 after 4–7 s with no message, unless a trial `bwrap` run catches it first.
  - **zenity needs GTK 4** on Ubuntu 24.04, so it is missing exactly when GTK 4 is.
  - **D-Bus can start an installed but stopped notification daemon** when there is no display. It crashes and costs up to 730 ms, so check the display first.
  - **Kubuntu 24.04 lacks WebKitGTK 6.0 and `xdg-dbus-proxy`.**

  (`startup-prerequisites`)

- **A `WebKitWebView` that is never presented is a hidden page.** After the first few timers, they are throttled to about one per second, and `MessageChannel` hops take about 85 µs (3.3 µs on WebView2). Headless benchmarks must avoid timers or present the view. (`stream-result-cost`)

- **On Linux, `Environment.SetEnvironmentVariable` does not reach native `getenv` or child processes.** Use libc `setenv`, before other threads start. (`webkit-wasm-off`, JIT run)

- **Reading WebKitGTK process memory:**
  - **`Process.PrivateMemorySize64` reads a ~70 GB virtual size on Linux.** Use `statm` resident − shared (about 1 µs, within 0.4 MB of `Private_Dirty`). `smaps_rollup` costs about 70 µs per process and 0.4 ms for the .NET app process.
  - **WebKit has no API for its process ids.** Walk `/proc/PID/task/*/children`.
  - **In 2.52, `webkit_website_data_manager_fetch` reports sizes for four data types:** disk cache, localStorage (in UTF-16 bytes), IndexedDB and the DOM cache.

  (`resource-usage`)

- **A hung WebKitGTK page:**
  - **`is-web-process-responsive` stays true for an idle hang.** It turns false only 3 s after a reload or `load_uri`.
  - **`webkit_web_view_reload` and `load_uri` do not recover it.**
  - **A hung web process outlives the app by about 10 s** at exit, then logs "WebProcess didn't exit as expected". Terminate it before quitting.
  - **Hidden pages run timers every 2 s,** but message handlers still answer in about 1 ms.

  (`page-heartbeat`)

### Linux host: from other frameworks, not rechecked

These entries come from other frameworks (`docs/research-review.md`) and have not been rechecked here:

- **NVIDIA with DMABUF gives blank or white windows, flicker, or crashes on resize** (Wayland Error 71). Try the fixes in this order: `__NV_DISABLE_EXPLICIT_SYNC=1`, then `WEBKIT_DISABLE_DMABUF_RENDERER=1` (slower), then `WEBKIT_DISABLE_COMPOSITING_MODE=1` (last resort). Production must not strip these variables.
- **Without an explicit data manager or program name,** WebKitGTK falls back to a shared `webkitgtk` data folder.
- **The tray needs a StatusNotifierWatcher,** which GNOME lacks, and it gets no click events (menu only). Global shortcuts work in practice only on X11.

## Bridge and loopback

- **`ValidateOnBuild` checks only registered services:** an interface that generated code resolves, and that nothing registers, passes it, and fails only when it is resolved. An interface contract with no implementation started and failed every call. Check such services against the collection yourself before building the provider. (M1a interface contracts)

- **An empty XML documentation element carries its meaning in an attribute:** `<paramref name="x"/>`, `<typeparamref>` and `<see href="…"/>` have no text, so code that takes each element's text leaves a hole in the sentence. (M1a interface contracts)

- **`Task.Run` flows `Activity.Current`:** a command started from another call's completion (the next one in the queue) or from the host's receiving thread would have that activity as its span's parent. Clear `Activity.Current` before starting a command's span. (M1a instrumentation)

- **Kestrel refuses a request body over 30,000,000 bytes (413) by default,** and the page's loopback transport sends whatever it has queued in one request: a burst of large calls would end the document's channel instead of failing one call. The page caps a request at 1 MiB of text, and the loopback host sets its body limit from the bridge's message limit. (M1a bridge limits)

Entries tied to one host's API stay in that host's section. See also, in "Windows host (WebView2)": "`WebResourceRequested` cannot stream", "A page `fetch` abort is invisible to the host", "An unanswered request to the app origin leaves the machine's loopback" and "A hung page". In "Linux host (WebKitGTK)": "No origin information", "Custom-scheme responses stream, but with no back-pressure" and "A hung WebKitGTK page". Wire values are in "Wire format and serialization".

- **WebView2 web messages:**
  - a string containing NUL is cut off at the NUL, in both directions;
  - a page string with a lone surrogate cannot be read with `TryGetWebMessageAsString` (`E_INVALIDARG`);
  - a host string with a lone surrogate arrives with U+FFFD;
  - `PostWebMessageAsJson` halved stream throughput, cost about 50% more host CPU, and threw on a `\ud800` escape that `JSON.parse` accepts.

  (`bridge-encoding`)
- **JSON numbers as ids:** the page parsed a 10,000-digit id as Infinity and 2^53+1 as 2^53. Check ids with `Number.isSafeInteger`. Duplicate keys resolve to the last one, so `{"type":"cancel","id":7,"id":8}` cancels call 8. (`bridge-encoding`)
- **`encodeURIComponent` throws `URIError` on a lone surrogate,** and the .NET reader throws on a lone-surrogate escape inside a string you then read as text. Trace fields need a tolerant read. (`bridge-encoding`)
- **A browser allows 6 HTTP/1.1 connections per host** (loopback cannot use HTTP/2). Six open per-call streams blocked a unary call for more than 3 s, and even the cancels. Use one channel per document. (`react`)
- **Kestrel (loopback): cancelling a blocked response flush aborts the connection,** and the cancel confirmation is lost. Transport flushes must not take the command's token. (`react`)
- **Separate `fetch` requests reach the host in any order,** over up to six connections. With the id rule (ids increasing within a document), calls sent in parallel were dropped and never answered: 200 calls at once timed out. Send page messages one request at a time and batch what queues up. (M1a loopback)
- **`MemoryExtensions.EnumerateLines` splits on more than LF:** CR, NEL, form feed, U+2028 and U+2029 too, and a message's JSON may contain a raw U+2028 (`JSON.stringify` leaves it unescaped). Split loopback batches and channel lines on LF only. (M1a loopback, from the API's documentation)
- **Disposing an `HttpClient` response with an unread, endless body does not close the connection at once:** the handler drains it for up to 2 s, hoping to reuse the connection, so the server saw the channel close late. A test that stands in for a page going away cancels a pending read instead, which aborts the connection. (M1a loopback)
- **Playwright's `EvaluateAsync` has no deadline:** a page promise that never settles (a call the host dropped) hangs the test run. Give every evaluation its own deadline. (M1a loopback)
- **A loopback listener in a test also receives connections from other apps on the same origin** (other WebView2 apps' preconnects). Attribute connections by process. (`desktop-origin`)
- **A heartbeat armed at `NavigationCompleted` misses hangs during load,** and one armed on a page without the injected script reports a healthy page after 20 s. Arm at document creation and only with the script. (`page-heartbeat`)

## Page security (CSP, headers, permissions)

- **Chromium warns about a feature it lacks in `Permissions-Policy`:** Linux's headless Chromium has no Web Bluetooth and logs "Error with Permissions-Policy header: Unrecognized feature: 'bluetooth'." on every page Spar serves. Spar keeps `bluetooth=()`; browser tests that require a clean console accept exactly this message. (M1a Linux)
Entries tied to one host's API stay in that host's section. See also, in "npm, Vite and TypeScript": "Angular under a strict CSP" and "Vue's full build compiles templates at run time". In "Windows host (WebView2)": `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS`, `AreDevToolsEnabled`, "The runtime makes requests the app never asked for" and the workers that the legacy request filter misses. In "Linux host (WebKitGTK)": "No origin information", `WEBKIT_INSPECTOR_SERVER`, "The GTK4 web process is always sandboxed" and "A redirect answered by a scheme handler is not followed".

- **`style-src` does not govern stylesheets built through the CSSOM:** constructed stylesheets (`replaceSync`, `insertRule`) and `style.setProperty` apply under `style-src 'self'` on all three engines, while `<style>` elements and `style` attributes are blocked. The policy stops styles brought in by markup, not by script. (`csp-style-injection`)
- **A nonce in a `nonce` attribute is hidden from `getAttribute`** on all three engines once the policy arrives as a header; read it as `element.nonce`. (`csp-style-injection`)
- **Radix's modal Dialog and AlertDialog inject a `<style>` element** to lock page scrolling, which the default CSP blocks (`style-src-elem`, `blocked-uri: inline`); a modal dropdown menu does the same. Use the browser's modal `<dialog>` (`showModal()`) and non-modal menus. (M1a Notebook page)
- **Vite's default `build.assetsInlineLimit` turns small assets into `data:` URLs,** which a strict CSP blocks. Set it to 0. (`csp-navigation`)
- **SRI fails open when the import map is blocked.** An inline import map is an inline script: without its hash in `script-src` it is blocked, and a tampered lazy chunk then loaded. SRI failures are not CSP violations either, so CSP listeners and reports miss them; only the console and an `error` listener see them. (`csp-navigation`)
- **Vite's ETag is computed before a nonce is swapped in,** so a cached dev page kept an old nonce and its styles were blocked. Send the dev HTML `no-store`, without an ETag. (`csp-navigation`)
- **A CSP listener in a module misses violations raised while the HTML is parsed.** Listen in the host-injected script, or use `report-uri`; `report-to` delivered only on WebKitGTK. (`csp-navigation`)
- **Injected user scripts are subject to the page's `eval` ban** on WebKitGTK; WebView2 exempts its injected scripts. (`csp-navigation`)
- **On WebView2, WebSockets are never intercepted, and CSP `'self'` in `connect-src` allows `wss://` to the same host.** `http://` and `ws://` to `*.localhost` are not mixed content, so they leave in plaintext. Name the origin with its scheme. (`desktop-origin`)
- **On WebView2, local network access checks do nothing for host-served pages** (address space `unknown`): loopback fetches succeed with no prompt. (`desktop-origin`)
- **On WebView2, cancelling a navigation does not stop its request.** Every cancelled GET navigation still reached the external server until the host registered `AddWebResourceRequestedFilter("*", DOCUMENT)` and answered foreign documents itself. Earlier versions saw the same: the request is sent before the host cancels the navigation, which uses up one-time links, and they answered it with an empty response. (`csp-navigation`)
- **On WebView2, `clipboard.readText()` fails with "Document is not focused" in an offscreen window,** before any permission event. `getScreenDetails()` needs a user gesture; `Notification.requestPermission()` does not. (`desktop-origin`)
- **On WebView2, the camera does not raise `PermissionRequested`.** A `getUserMedia` call without `Permissions-Policy: camera=()` hung, and a prompt reached the user's desktop, most likely the Windows privacy prompt for unpackaged apps. Deny powerful features with the header, and never call the camera or microphone in an unattended test on a machine with a person. (`csp-navigation`)
  - In Windows Sandbox with fake devices, `PermissionRequested` does fire per kind (camera 2, microphone 1). So the earlier hang was probably the real device's path, which is still untested.
  - `SavesInProfile` defaults to true, so a remembered allow later starts the camera with no host event.
  - `--use-fake-ui-for-media-stream` bypasses the handler.

  (`windows-sandbox-checks`)
- **WebKit's `javascript-can-access-clipboard` bypasses the clipboard permission check entirely.** Keep it off; pasting outside text then raises a permission request, which the strict default denies. (`linux-integration`)
- **On WebKitGTK, `nosniff` silently blocks the app's own CSS, classic scripts and workers** unless `Content-Type` is also in the header set passed to `webkit_uri_scheme_response_set_http_headers`. Setting the content type alone is not enough. (`csp-navigation`)
- **WebKitGTK ignores `Permissions-Policy`, CORP and COEP isolation;** the `permission-request` handler is the only gate. WebKitGTK 2.52 checks the CSP's `'wasm-unsafe-eval'` only when a module is instantiated, so `compile` succeeds where Chromium refuses it; probe instantiation (`webkit-wasm-off`, which corrected the earlier "WebAssembly runs anyway" reading). (`csp-navigation`)

- **WebAssembly and CSP on WebKitGTK:**
  - **A worker script served without a CSP header escapes the page's policy.** It ran `eval` and every WebAssembly path under a page with a strict CSP. Send the CSP on every response.
  - **Blocked WebAssembly raises no `securitypolicyviolation` event** and sends no report.
  - **The JavaScriptCore option is `useWasm`** (2.48 and later; `useWebAssembly` up to 2.44). A wrong `JSC_*` name is only logged as "invalid option".
  - **`JSC_*` variables also affect the UI process.** An incoherent set (`JSC_useWasmIPInt=false` with `JSC_useBBQJIT=false`) aborted the app with exit 134, and `JSC_useJIT=false` from a user's shell made JS about 35 times slower.
  - **Setting the web extensions directory inside `initialize-web-process-extensions` is too late under bubblewrap.** The extension is silently never loaded.

  (`webkit-wasm-off`)

### Page security: from other frameworks, not rechecked

This entry comes from other frameworks (`docs/research-review.md`) and has not been rechecked here:

- **Once a CSP directive contains a hash or nonce, browsers ignore its `'unsafe-inline'`.** A Spar-added hash would silently break an app's CSS-in-JS.

## SQL Server client (Microsoft.Data.SqlClient)

- **SqlClient is not NativeAOT-clean:** 27–53 unique ILC diagnostics in every version and app tried. 6.1.8 to 7.1.2 gave 43–49 (43 for an app that only opens a connection), 8.0.0-preview1 gave 27, and the SQL tool gave 53 (the earlier 49 counted two IL2067 lines as one, `GetSchema` adds an IL2072, and 2 lines are the app's own). It works when the app accepts the warnings. (`sqlclient-aot`, `sqlclient-tool`)
- **`InvariantGlobalization` must be `false`.** Otherwise `SqlConnection.Open` throws `NotSupportedException`, even under the JIT. `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` in the environment has the same effect, under AOT and the JIT. (`sqlclient-aot`, `sqlclient-linux`)
- **Native SNI is a sidecar DLL.** An AOT exe copied without `Microsoft.Data.SqlClient.SNI.dll` fails at the first `Open` with `TypeInitializationException` → `DllNotFoundException`. The SNI package has no static `.lib`; the single-file option is managed SNI. (`sqlclient-aot`)
- **A bulk copy without a transaction keeps every row already sent when cancelled,** including part of the batch in flight. `BatchSize = 0` is not one atomic batch either: a cancel still kept about 310,000 rows, and took 0.8–1.0 s against 34–51 ms with 50,000-row batches. Use `UseInternalTransaction` (whole batches only) or an external transaction, and bounded batches when a cancel must be quick. (`sqlclient-tool`)
- **An external transaction holds its locks until `Rollback()`:** 9,880 locks at `BatchSize` 50,000 (1,713 at 0), with the session `sleeping` and one open transaction. (`sqlclient-tool`)
- **Cancelling a bulk copy does not stop its source query.** Call `SqlCommand.Cancel()` on the source. (`sqlclient-tool`)
- **Cancellation surfaces as different exceptions:** a cancelled command is `SqlException` Number 0 (Class 11), not `OperationCanceledException`; a cancelled bulk copy and `ReadAsync(token)` throw `TaskCanceledException`; `SqlRowsCopiedEventArgs.Abort` gives `OperationAbortedException`; a token cancelled before the call gives `TaskCanceledException`. Check your own token. (`sqlclient-tool`, `sqlclient-aot`, `sqlclient-linux`)
- **`ReadAsync(token)` cancels only on the client.** Disposing a reader early reads the rest of the result (about 1 s for 999k rows); call `SqlCommand.Cancel()` first. (`sqlclient-tool`)
- **Async reads with the default `CommandBehavior` are 1.5–1.7× slower** and allocate about twice as much as `SequentialAccess` or sync reads. `EnableStreaming`, `GetStream` and `GetTextReader` allocated 6–17 GB per million rows; use them only for large values. (`sqlclient-tool`)
- **Explicit `VarChar` parameters are encoded with the database's code page.** A copy tool should send `nvarchar`. (`sqlclient-linux`)
- **Managed SNI** (the single-file exe on Windows, always on Linux) has no shared memory. On Windows it was 18–24% slower than native SNI, and local integrated authentication then needs `np:`. On Linux it read as fast as native SNI on Windows in the same session. (`sqlclient-tool`, `sqlclient-linux`)
- **TLS errors and defaults mislead:**
  - the default is `Encrypt=Mandatory`, so a server with an untrusted certificate fails; managed SNI reports that as "TCP Provider, error: 35";
  - `HostNameInCertificate` is not checked when `TrustServerCertificate=true`;
  - `Encrypt=Optional` encrypts only the login;
  - the deprecated System.Data.SqlClient 4.9.1 connects unencrypted by default.

  (`sqlclient-aot`, `sqlclient-linux`)
- **A refused TCP connection took about 4.5 s of a 5 s `ConnectTimeout`** to fail on Linux, not milliseconds. A monitor that probes many instances needs short connect timeouts. Not compared on Windows. (`sqlclient-linux`)
- **On Linux:**
  - TCP only: remote `np:` throws `PlatformNotSupportedException` at once, and `.`, `(local)` and `localhost` mean TCP to the local machine;
  - integrated authentication is Kerberos only (no NTLM). Without `libgssapi_krb5.so.2` it fails with a type-initializer `SqlException`, not an abort;
  - missing ICU or `libssl` aborts the process (exit 134) instead of throwing;
  - bulk copy used about 2.3× the CPU of Windows, apparently spin-waiting (`DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0` brought it down; not profiled).

  (`sqlclient-linux`)
- **Development containers:** `Encrypt=Strict` fails against the `mssql/server` image, so use `Encrypt=true;TrustServerCertificate=true` locally. (`sqlclient-aot`, `sqlclient-linux`)
- **`sys.dm_os_performance_counters` returns about 2,000 rows** (3.4 MB allocated per poll). Filter the counters you need. (`sqlclient-tool`)
- **System-named constraints differ between databases** (`PK__OnlyLeft__3214EC06…`). A schema comparer must key primary keys by table, not by name. (`sqlclient-tool`)
- **Connecting to LocalDB's automatic `MSSQLLocalDB` instance creates it** when it does not exist. On this machine `SqlLocalDB.exe` is installed without that instance, so tests must not connect to it. (`sqlclient-tool`)

## Wire format and serialization

See also "Build, MSBuild and packages" for the C# traps found while building the `Result` type and its analyzer (conversion operators, `Interlocked`, `ArrayPool`, Roslyn operation blocks), and "Bridge and loopback" for the bridge's message and id limits.

- **.NET's parsers are looser than a wire spelling:** `decimal.TryParse` rounds past 28 or 29 significant digits instead of failing and accepts `+1` and `01`; `Utf8JsonReader.TryGetSingle` returns infinity for `1e39`; `TryGetBytesFromBase64` skips whitespace; `TimeSpan.TryParseExact(…, "c")` accepts `1:2:3`. Require the text the value writes back, or check the raw bytes, before trusting a parse. Negative zero `decimal` writes as `0`. (M1a wire kinds)
- **An analyzer that reports a descriptor missing from `SupportedDiagnostics` throws** (AD0001) instead of reporting it, and the build shows only AD0001. (M1a wire kinds)
- **One generated async method that awaits every command** keeps an awaiter for every result type in its state machine, which doubled async allocations to about 600 B per call. Emit one small async method per command. (`result-api`)
- **Tier-0 JIT code boxes a value-type `T` for `is not null`,** which breaks allocation assertions in Debug tests. Compute such checks once, in the constructor. (`result-api`)
- The first generated serializer (fixed in the fuzzing spike's copy):
  - accepted trailing content after the object;
  - threw `InvalidOperationException` or `FormatException` instead of `JsonException`;
  - wrote depths up to 1000, while readers stop at 64;
  - accepted number literals that overflow to infinity.

  (`serializer-fuzz`)
- **JavaScript numbers lose 64-bit integers and decimals.** `JSON.parse` loses precision on `long` values above 2^53: only 603 of 100,000 random 64-bit ids survived it; it also changed 5 of 7 `decimal` samples (`1.50` became `1.5`). The integers are decided: `long` and `ulong` travel as canonical decimal strings (`bigint` in TypeScript; decision "Wire format choices"). (`wire-details`, `serializer-fuzz`)
- **The first contract fingerprint hashed only type names,** so adding a DTO field did not change it. (`react`)
- **`JSON.stringify` turns NaN and ±Infinity into `null`** without an error; in a nullable field that silently changes data. (`wire-details`)
- **`JSON.stringify` throws on a `bigint`** (`TypeError: Do not know how to serialize a BigInt`), so only a generated encoder can send one. In TypeScript, `id + 1` is TS2365 and a `(a, b) => a.id - b.id` comparator is TS2345. (`wire-details`)
- **`JSON.stringify` sends lone surrogates as `"\ud800"`, which every .NET reader rejects;** .NET writers already replace them with U+FFFD, and every writer in the fuzzing spike did so silently. This is decided: lone surrogates become U+FFFD in both directions (decision "Wire format choices"). SqlClient also replaces them in `nvarchar`, in both directions. (`wire-details`, `serializer-fuzz`)
- **Copying a parsed object can change its prototype.** `JSON.parse('{"__proto__":{"x":1}}')` creates an own `__proto__` key, and `Object.assign({}, it)` then sets the copy's prototype. Build objects field by field. (`wire-details`)
- **`new Date()` on a date without an offset uses the page's time zone:** `"2026-11-14"` is 13 November in Los Angeles. SqlClient returns `datetime2`, even `SYSUTCDATETIME()`, as `Kind=Unspecified`. (`wire-details`)
- **SQL `decimal(38)` overflows `System.Decimal`:** `GetDecimal` and `GetValue` throw; only `GetSqlDecimal` reads it. (`wire-details`)
- **Lenient System.Text.Json readers:** `TryGetSingle` returns `Infinity` for `3.5e38`; `TryGetBytesFromBase64` accepts embedded whitespace; enum reading is case-insensitive and accepts numbers. (`wire-details`)
- **The .NET integer parsers are not canonical.** `Utf8Parser.TryParse` and `long.TryParse` accept `+1`, `007` and `-0`, and `Utf8Parser` stops early on `1 ` and `1e3` (it reports partial consumption). Check `-?(0|[1-9][0-9]*)`, at most 19 digits, then require full consumption. (`wire-details`)
- **System.Text.Json throws more than `JsonException`:**
  - `Utf8JsonReader.GetString()` throws `InvalidOperationException` on invalid UTF-8 and on unpaired `\u` surrogates, and so does `TryGetDateTimeOffset`, which throws rather than returning false. Dispatch counts anything but a `JsonException` as the command's own failure, so a request with a lone surrogate failed as `failed` / `unknown` until `SparJson.ReadString` handled it (M1a console harness);
  - `JsonEncodedText.Encode` throws `ArgumentException` on a lone surrogate;
  - `WriteNumberValue` throws `ArgumentException` on NaN and ±Infinity;
  - a writer at `MaxDepth` throws `InvalidOperationException`, even with `SkipValidation = true`.

  A pooled writer is reusable after any of these: `BytesCommitted` stays 0, and `Reset(buffer)` plus `ResetWrittenCount()` give a clean writer. (`wire-details`, `serializer-fuzz`)
- **Skipped values are not validated.** Every reader tested, System.Text.Json included, accepts invalid UTF-8 and lone-surrogate escapes inside an unknown property's value, because skipping does not decode strings. Every reader rejects a leading BOM. (`serializer-fuzz`)
- **An unbuffered writer that fails mid-response has already sent invalid JSON** (278,560 bytes in the test). Buffer unary responses and each stream item. (`wire-details`)
- **`foreach` over an `IReadOnlyList<T>` boxes its enumerator:** 64 B per `Note`, 70.8 KB at 2,000 records. Generated code uses indexed loops. (`serializer-bench`)
- **The compiler does not narrow an indexer:** `list[i] is null ? … : list[i].Value` gives CS8629 for `long?[]` and CS8604 for `IReadOnlyList<Wide?>`. Assign each element to a local first. (`serializer-fuzz`)
- **NativeAOT words some exception messages differently from CoreCLR.** Tests that compare messages must normalise them. (`serializer-fuzz`)

## Observability

- **Tag values taken from the page multiply metric series:** a refused call names whatever contract and command the page wrote, and each distinct value is a new series in every exporter. Spar tags only names the app's catalog has, and `unknown` otherwise. (M1a instrumentation)

- **Page spans are lost when a desktop window closes:** none delivered in 2 of 2 runs with the default 5 s batch delay. The host must ask the page to flush and wait (bounded, 1 s); the flush took 9–22 ms. A headless `page.close` on loopback also delivered nothing; navigating away flushed. (`browser-telemetry`)
- **A collector that is down stalls the page and the relay.** The JS SDK's `forceFlush` rejects, and each relay attempt waits about 2 s for a refused connection. Make the flush never reject, and have the relay answer 503 at once for a while after a failure. (`browser-telemetry`)
- **A hard-coded service name splits one app in two under Aspire.** Read `OTEL_SERVICE_NAME` (and name the page `<name>-page`). (`browser-telemetry`)
- **React produces 2–3 nested `click` spans per click,** and click spans need telemetry loaded before React mounts. (`browser-telemetry`)
- **The Aspire dashboard writes data-protection keys to `%LOCALAPPDATA%\ASP.NET\DataProtection-Keys`,** even with `LOCALAPPDATA` redirected. (`browser-telemetry`)
- **`@opentelemetry/sdk-trace-web` is deprecated in OpenTelemetry JS 3.x,** and its last release doesn't work with the current exporters. Use `@opentelemetry/sdk-trace`. (`browser-telemetry`)

## Tooling and environment

- **`Task.WhenAny` can return the task that completed second** when both come from `TaskCompletionSource`s made with `RunContinuationsAsynchronously`: it returns the one whose continuation runs first. The test host's start reported "Program.cs returned 70 without calling Run" for an app that did call `Run` and failed to build (1 of 60 NativeAOT runs, 4 of 60 under load), because `Run` completes its start signal and then returns 70. Check the other task's `IsCompleted` before deciding. (`SparTestApp.StartAsync`; found by `sandbox-runner`)

- **WSL empties `/tmp` when the distribution restarts,** and it stops between `wsl.exe` calls: a clone made there in one call was gone in the next. Keep runs in `/var/tmp`. Check that a script's `cd` succeeded: one that failed ran `npm ci` in the current Windows folder and replaced its `node_modules` with Linux binaries. (M1a Linux)
- **`XDG_RUNTIME_DIR` on a Windows drive under WSL fails .NET:** the folder cannot be 0700 there, and the CLI's first run stopped with "does not have the expected owner or permissions for system scope". Keep a Linux run's homes on the distribution's own file system. (M1a Linux)
- **`await using` lets a disposal's exception replace the one already in flight:** a test whose body failed and whose app then failed to stop showed only the stop failure. Spar's test host keeps the stop failure for a hook that raises it after the test. (M1a fixture rules)
- **xUnit's in-process runner (the NativeAOT lane) exits 0 when no test ran,** where `dotnet test` exits 8. The CI step checks for "Total: 0". (M1a fixture rules)
- **xUnit v3 4.x makes `CollectionBehavior.DisableTestParallelization` an error** (CS0619, obsolete). Turn parallel runs off with `[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]`. (M1a console harness)
- **xUnit's two flavours are different assemblies** (`xunit.v3.core.dll`, `xunit.v3.core.aot.dll`) with different extension signatures: a library compiled against one cannot run against the other. Code that extends xUnit is compiled into the test project, one file per flavour. (M1a fixture rules)

- **BenchmarkDotNet needs project names that are unique across the repository:** `Bench.csproj` in two spikes clashed. An analyzer whose descriptor uses a literal ID also needs `AnalyzerReleases.Shipped.md`/`Unshipped.md` (RS2008). (`result-api`)
- **Aspire names endpoints `localhost`,** which Node resolves to `::1`. A backend that listens on `127.0.0.1` only, and checks its origin, then hangs every proxied call. (`aspire`)
- **Aspire's SQL Server health probes log "pre-login handshake" errors** while the container starts. That is noise, not a failure. (`aspire`)
- **WebView2 and process tools on this machine:** never kill processes or containers that the current session did not start. The permission classifier blocks it. (session)
- **NuGet's HTTP cache expires every entry after 30 minutes, package files included:** a test that restores into a fresh package folder each run downloads everything again after a 30-minute gap (about 370 MB and four times the run time for the package tests). Keep the isolated package folder between runs, and give the packages under test a version no run has used. (M1a package tests)
- **Isolating builds from other sessions:** a private `MSBUILDNODEHANDSHAKESALT` and a private `SharedCompilationId` keep a build off other sessions' MSBuild server, nodes and compiler server. Give each step that must start cold its own salt: with one salt for packing and the consumer, the consumer's "cold" build reused the warm server. A Windows job object does not contain the processes, because the host's job allows silent breakaway; stop the process tree you started (by parent id and start time). (`d3-variants`, `package-consumer`)
- **File-based apps (`#:`):**
  - inside the repository, central package management rejects `#:package` (NU1008). Set `#:property ManagePackageVersionsCentrally=false`, or run the app from outside;
  - they default to `PublishAot=true`, which turns off reflection JSON even under `dotnet run`; Playwright needed `#:property PublishAot=false`;
  - an `MSBuildWorkspace` probe needs `Microsoft.Build.Locator` and `DisableMSBuildAssemblyCopyCheck=true` (MSBL001).

  (`d1-pipeline`, `package-consumer`, `contract-syntax`, `d8-automation`)
- **Test runner output:**
  - `dotnet test` (MTP) leaves an empty `TestResults/` in the working directory, and TUnit writes HTML and JSON reports there on every run. Pass `--results-directory`;
  - xUnit v3 prints `Console.WriteLine` raw, with no test attribution, unless the assembly has `[assembly: CaptureConsole]`;
  - MTP hides a passing test's output unless `--output Detailed`;
  - an `xunit.v3` exe started directly uses xUnit's console runner, not MTP, and rejects MTP options such as `--list-tests`.

  (`d3-variants`, `test-frameworks`)
- **The first `dotnet test` after a build was about 1–2.4 s slower** for all three frameworks, with or without browsers. Unexplained; possibly first loads of freshly written binaries. Discard the first run when timing. (`test-frameworks`)
- **A NativeAOT test exe (`xunit.v3.aot`) prints no stack frames on failure.** It also needs the `XUNIT_AOT` define and allows no generic test methods and no `params`. (`test-frameworks`)
- **TypeScript's language-service API does not follow declaration maps:** `getDefinitionAtPosition` returned the `.d.ts` position; `tsserver` maps it to the source. Test go-to-definition through `tsserver`'s protocol. (M1a client step)
- **Playwright's `WaitForFunctionAsync` with an async predicate passes immediately.** Poll instead. (`react`)
- **Playwright for .NET cannot run inside a NativeAOT process** (reflection JSON), so browser journeys stay on CoreCLR test projects. (`d8-automation`, `test-frameworks`)
- **BenchmarkDotNet switches the Windows power plan by default,** a system setting. Always use `DontEnforcePowerPlan()`. BenchmarkDotNet 0.15.8 on .NET 11 also needs `.NET 10.0` job labels with `net11.0` toolchains and `IlcInstructionSet=native`. Its NativeAOT jobs build with `IlcOptimizationPreference=Speed`, warnings not as errors and no `Directory.Build.props`, so they are not shipping builds: with default ILC settings one serializer gap grew from 1–2% to 7–13%. (`serializer-bench`)
- **IDE behaviour seen in the checks:**
  - Visual Studio regenerated source-generator output only after Save, "Rerun generator" or a build, never after an edit made on disk from outside;
  - when a contract became invalid, VS turned the open generated tab red ("has stopped generating this file");
  - VS's Solution Explorer ignored clicks for about 3 minutes after the solution opened;
  - VS Code opens an untrusted folder in Restricted Mode, and the project does not load until the folder is trusted;
  - one `devenv.com /Build` run sat idle with no output, probably on a dialog.

  (`d1-pipeline`)
- **IDE checks of analyzer squiggles:**
  - ReSharper's code analysis, when on, stops Visual Studio drawing squiggles for every analyzer the project references (Spar's and the SDK's CA rules alike); they still reach the Error List as IntelliSense entries. Compiler errors and the built-in IDE analyzers are drawn. Check squiggles with ReSharper's "Enable code analysis" off, and say so;
  - C# Dev Kit refuses a project without an SDK in `<Project Sdk="…">` ("unsupported format"), such as a project that imports an SDK's props and targets itself; its files then open as loose files, with no analyzer. This is why the repository's projects name `Spar.Sdk` through the `0.0.0-source` package (`eng/source-sdk/`) instead of importing `src/Spar.Sdk/Sdk/`;
  - opening a folder in VS Code through its 8.3 short path (`C:\Users\USERNA~1\…`, what `%TEMP%` often holds) made the project load twice once added to the workspace, and the open file stayed a loose file. Open folders by their long path;
  - the language server's own `project/open` loads a project Dev Kit refuses, so a headless check can pass where the window does not;
  - Visual Studio picks up a file changed on disk when its window gets focus, not before.

  (M1a editor diagnostics)
- **Driving TypeScript 7's language server over several projects in one Node process hit the client's 5 s shutdown timeout,** and the run was lost. One process per project worked. (`ts-compiled-contracts`)
- **`performance.memory` stays 0 in `chromium-headless-shell`.** Read heap figures through CDP. (`react`)
- **Linux NativeAOT needs `clang` and `zlib1g-dev`.** The Playwright .NET image lacks them ("Platform linker ('clang' or 'gcc') not found"); installing them took 93 s. (`linux-loopback`)
- **`KillOnParentExit` works on Linux** (the app exited 2 ms after its parent's SIGKILL). In a container whose PID 1 is `sleep infinity`, the exited app stays a zombie, and `kill -0` reports it alive. Check `/proc/<pid>/stat`, or run the container with `--init`. (`linux-loopback`)
- **`pgrep -f` also matches the shell running the probe.** Count processes with `pgrep -x` on the process name. (`linux-desktop`)
- **A `node` process started in the background from Git Bash can survive `kill`.** Stop it by its Windows PID and wait until its port is free. (`desktop-origin`)
- **Git Bash rewrites POSIX-looking paths:** `docker exec … /opt/...`, `devenv /Build`, and `/mnt` paths passed to `wsl.exe`. Set `MSYS_NO_PATHCONV=1`. (`react`, IDE checks, `startup-prerequisites`)
- **Windows environment variables do not reach `wsl … -- bash`.** Pass values as arguments, or list them in `WSLENV`. (`call-promise-cost`)
- **Test-harness surprises on Linux:**
  - dunst popups don't appear in the window manager's client list;
  - `swaymsg` needs `SWAYSOCK`;
  - `wsl.exe -- bash -c` expands variables in the outer shell.

  (`startup-prerequisites`)
- **Benchmark clocks:**
  - On Windows, Node's `setTimeout(1)` fires only about every 15 ms.
  - A clock-resolution probe that loops until it sees a sub-microsecond step never ends on a 1 ms clock (WebKitGTK). Bound it by iterations.

  (`stream-result-cost`)
- **`dotnet run` of a file-based app in a container can hang.** Use `docker run --init` and `UseSharedCompilation=false`. (`webkit-wasm-off`)
- **A window hosted inside the test process takes the whole test run down when the engine aborts.** With `bwrap` refused (root in Docker), WebKitGTK ended the xUnit process with a GLib `ERROR` and a core dump: no result for any test, finished or not. Check the engine's prerequisites before the first window test. (`test-host`)
- **On a Windows bind mount, files a root container created cannot be overwritten by a later uid 1000 container** (`tee: Permission denied`); new files in the same folder can be written. Delete the root-owned files from Windows, or keep the container on one user. (`test-host`)
- **`U+2028`/`U+2029` written through agent tool input can land in source files as raw characters,** which broke a C# build (CS1010). Write them as escapes and check the bytes. (`bridge-encoding`)

- **Hosted GitHub Actions runners (Windows):**
  - Playwright's `install --with-deps` installs the Media Foundation Windows feature on Windows: 3 min 16 s of a 3 min 23 s step, for a browser download of 6 s. The headless shell does not need it; pass `--with-deps` on Linux only;
  - the .NET CLI's MSBuild server did not start in time on a fresh runner, and the CLI built in-process after waiting ("MSBuild server unavailable … Falling back to an in-process build"). A CI job gains nothing from the server: `DOTNET_CLI_USE_MSBUILD_SERVER=false`;
  - the NuGet and npm caches and `TEMP` live on C:, which writes more slowly than D:, where the checkout and `RUNNER_TEMP` are. Point `NUGET_PACKAGES`, `NUGET_HTTP_CACHE_PATH`, `npm_config_cache` and `TEMP`/`TMP` under `RUNNER_TEMP`;
  - a job with no `timeout-minutes` runs up to six hours when a test hangs.

  (M1a CI)

### Tooling: from earlier versions, not rechecked

These entries come from earlier Spar versions (`docs/research-review.md`) and have not been rechecked on .NET 11:

- **Antivirus:** Defender trust checks made up 99% of loader waits (process to first call went from 1.28 s to 13.7 s), because randomized test-fixture paths defeated its caching. A Release-only `PathMap` fixed it.
- **Hot Reload accepted a DTO change that a fresh compile rejected;** setting `Watch=false` on the items was not enough. Contract edits must force a restart.
- **A drive-letter case mismatch (`e:\` in the PDB, `E:\` in the project system) broke Hot Reload** in VS Code; `PathMap` fixed it. C# Dev Kit's Hot Reload settings are machine-scoped, and mixed path casing gave ENC1008.
- **Passing `-m:1` through to Microsoft.Testing.Platform ran zero tests** and exited with code 5.
- **JSONL written by a child process inherited the console code page** and corrupted non-ASCII text (`caffè`).
- **Running `dotnet msbuild` as a child process was 61% slower** than calling the MSBuild APIs in-process.
- **Windows Sandbox:**
  - give the guest an ID before starting it; only one Sandbox runs per user session;
  - `wsb exec` relays no stdout, so read the exit code from its JSON result;
  - CDP inside the guest failed because the remote-debugging flags were dropped; use UI Automation;
  - the guest's colour mode cannot be set from `.wsb`.
