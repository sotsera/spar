---
name: spar-msbuild
description: Author, format, or review Spar MSBuild project files, props/targets, build variants, output locations, and package build integration. Use with the dotnet-msbuild plugin skills for general incremental-build and binlog diagnosis.
---

# Work on Spar builds

The design is plan section 1 (`docs/design/build.md`). General MSBuild technique (target authoring, Inputs/Outputs, generated files, import hooks, binlogs) belongs to the `dotnet-msbuild` plugin skills. This skill holds the rules specific to Spar. Where they conflict, `docs/decisions.md` wins.

## XML layout

Apply these rules to authored `.csproj`, `.props` and `.targets` files.

- Group properties and items by purpose: compiler options, analysis settings, package metadata, and related dependencies each together. Put a blank line between logical groups, including inside one `PropertyGroup` or `ItemGroup`, and between major groups, imports and targets.
- Keep related entries next to each other, and keep any order that evaluation needs. Before moving anything, check conditions, property dependencies, item operations, import precedence and target hooks. Don't sort alphabetically.
- Indent with two spaces, use double quotes for attributes, and end the file with a newline. Keep simple elements on one line. Wrap long attribute lists when that reads better, and keep meaningful whitespace inside MSBuild values.
- Check grouping, spacing and order in review. A formatter can fix indentation, but recheck the build behaviour after any reordering.

## Spar build rules

- **One variant property.** Build variants are selected by a single `SparVariant` property (`Production`, `Development` and `Test`; plan, "Settled decisions"). Debug builds `Development`, Release builds `Production`, and a test project builds the app it names with `<SparTestedApplication Include="…" />` as `Test` whatever its configuration; an explicit `SparVariant` wins. It is set once in props. Don't add independent mode flags. Each variant gets its own `obj/<variant>/` and `bin/<variant>/`, using short hashed keys where paths get long. Publishing anything other than `Production` is a build error, unless `SparAllowNonProductionPublish=true` turns it into a prominent warning (plan section 3c). The NativeAOT test lane publishes the test project, not the app: the app enters it as a project reference built in the test variant. Every other project reference, the app's own libraries and transitive ones included, gets `SparVariant` and `SparTestLane` removed (`UndefineProperties`, target `SparPlainReferences`): a project without a folder per variant would be built once per variant into one `obj/` (CS2012). `Spar.BuildTests` checks that each project is compiled once.
- **The tested app is an item, not a plain reference** (decision "SparTestedApplication: the test project names its app"). `Spar.Sdk` turns `SparTestedApplication` into a `ProjectReference` statically, so restore sees it. Whether a reference is a Spar app comes back with MSBuild's own `GetTargetFrameworks` answer (`AdditionalTargetFrameworkInfoProperty` `SparIsApp`), so the checks SPAR120–SPAR122 cost no evaluation; they run after `_GetProjectReferenceTargetFrameworkProperties`, as the .NET SDK's executable-reference check does.
- **One platforms property.** An app picks its hosts with `<SparPlatforms>windows;linux</SparPlatforms>` or one of the two; both is the default (plan section 1).
  - `Spar.Sdk` adds `Spar.Windows` and `Spar.Linux` at its own version; apps never reference host packages.
  - Each platform maps to its runtime identifiers (`win-x64`/`win-arm64`, `linux-x64`/`linux-arm64`), and NativeAOT removes the other host from each executable.
  - NativeAOT does not cross-compile between operating systems: each executable is published on its own OS or in a container of it.
- **Keep user settings separate from machinery.** Settings users choose live in the app project. Reusable machinery lives in the SDK's props and targets.
- **Spar's defaults serve every app.** `InvariantGlobalization=true`, NativeAOT, and no accepted warnings. A setting that one app or one dependency needs, such as SqlClient's `InvariantGlobalization=false`, its single-file networking switch and SNI exclusion, or the AOT warnings it accepts, goes in that app's project. Spar documents such settings as recipes and never sets them. `Spar.Sdk` raises no compiler warning to an error in apps and does not set `TreatWarningsAsErrors` or `MSBuildTreatWarningsAsErrors` for them; the app template sets both, and the app may turn them off.
- **No C# in MSBuild strings.** Don't write generated C# from `WriteLinesToFile`. Generators produce C#, reading MSBuild values through `CompilerVisibleProperty` / `CompilerVisibleItemMetadata`.
- **Know each generator's files.** For each generator, know its inputs, the outputs it owns, and the first target that consumes them. Write outputs only when their content changes. Delete stale outputs only from the set the generator owns.
- **Design-time builds stay quiet.** They never launch the app, start dev servers, build or type-check the frontend, or run the client tool (pipeline steps 2–4 in plan section 1). Generated C# comes from the source generator, so design time needs none of those steps.
- **Frontend steps.**
  - `npm ci --ignore-scripts` runs in every variant when the lockfile changed: the Debug type-check and the dev server need `node_modules` too.
  - The TypeScript type-check runs in every variant, Debug included. It runs only when the generated client or the TypeScript sources changed since the last check, and reports errors at file and line. `SparTypeCheck=false` turns it off (the name is confirmed in M1a).
  - Release and test variants also run `vite build` into `obj/`, and the packing step. Spar's steps run before `PrepareResources`, because MSBuild settles embedded resources there and the pack is one. They hang off one entry target, `SparBuildSteps`, ordered by `DependsOnTargets`: targets hooked `BeforeTargets` on the same target run in declaration order, whatever `AfterTargets` says. Packing rejects source maps, version-control and secret files, links, and names that differ only by case (plan section 3c).
  - Debug embeds no bundle: the host serves the page from the app's Vite server through the app's own origin. `SparDevServer=false` embeds the bundle in Debug through Release's code path. As built (M1d step 1): `SparUsesDevServer` (Development, a frontend, `SparDevServer` not false) turns the feature switch `Spar.DevServer.IsSupported` on, off with `Trim="true"` everywhere else; the frontend folder, the `obj/` folder (`Spar.DevServer.Intermediate`) and `SparDevServerUrl` reach the run as runtime configuration values, in Development only. Never a C# constant or catalog entry for them: a Production build must carry none.
- **Steps are compiled tasks.** Spar build steps are compiled `net11.0` MSBuild tasks, not `Exec dotnet tool.dll`. Each one:
  - is marked `[MSBuildMultiThreadableTask]` and implements `IMultiThreadableTask`, with a constructor taking a `TaskEnvironment` and a parameterless one that uses `TaskEnvironment.Fallback`;
  - resolves every path and environment variable through `TaskEnvironment`;
  - keeps no unsynchronized static state. The analyzer below does not see static mutable state, `AppContext` switches, culture setters or assembly-load hooks, so review and tests check them.

  Declare them with `Runtime="NET"`. In Spar's own source checkout, where the task assembly is built in the same graph, use `TaskFactory="TaskHostFactory"` instead, because a warm MSBuild node locks the DLL (MSB3027). Expensive state that can be reused, such as metadata references, goes in a thread-safe, process-wide cache keyed by file identity.
- **Task analyzer.** The task project references `Microsoft.Build.TaskAuthoring.Analyzer` with `PrivateAssets="all"`, and `Microsoft.Build.Framework` and `Microsoft.Build.Utilities.Core` as compile-only references. Its option and severity raises are the `[src/Spar.Sdk.Tasks/**.cs]` section of the root `.editorconfig`; keep that path in step with the task project's folder. Its code fixes are not shipped, so apply fixes by hand and build them. A build-test lane runs `-mt`, where the strict-mode warnings MSB4286–MSB4288 fail the build.
- **Avoid nested builds.** Prefer the in-process `MSBuild` task with explicit global properties over `Exec dotnet ...` for variant builds (decision "D3: variant builds run in-proc"). Record every remaining `Exec dotnet` and the reason for it in `docs/decisions.md`.
  - Run `Restore` as its own call: a `Restore;Build` call reuses the evaluation made before restore wrote `nuget.g.props`/`.targets`, so packages' build assets would be missing from it (inferred; the failing case was not built).
  - The `MSBuild` task forwards global properties: strip them with `RemoveProperties` and pass `Configuration` and `_IsPublishing` explicitly.
  - Fail an unknown variant before restore (`BeforeTargets="Restore;_CheckForInvalidConfigurationAndPlatform"`).
- **Incremental inputs.**
  - Timestamps cannot show a removed file: list an inventory file, written only when the input list changes, in `Inputs`.
  - A write-if-changed output cannot be the target's `Outputs`; use a stamp file.
  - `npm ci --ignore-scripts` runs only when the lockfile is newer than npm's own `node_modules/.package-lock.json`.
  - A glob over `node_modules/@spar/**` follows npm's `file:` link into the generated `@spar/app` package.
- **Feature switches remove code.** `[FeatureSwitchDefinition("…")]` on the guarding property plus `<RuntimeHostConfigurationOption Include="…" Value="false" Trim="true" />` trims the guarded code as completely as `#if`.
- **Package layout (MSBuild SDK).** `PackageType=MSBuildSdk`, `IncludeBuildOutput=false`, `SuppressDependenciesWhenPacking=true`; tasks and everything they load go to `tools/` through `TargetsForTfmSpecificContentInPackage`. MSBuild resolves a task's dependencies from the task's folder, so the one generator DLL in `tools/` also serves as the `Analyzer` item. Mark every `PackageReference` the SDK adds with its own version `IsImplicitlyDefined="true"`, the test framework (`xunit.v3`, `xunit.v3.aot`) included: under central package management a version on any other `PackageReference` is NU1008. The package tests have a consumer with `Directory.Packages.props` and transitive pinning.
  - **Two modes, one set of targets.** `SparSourceMode=true` (this repository) uses project references and `TaskHostFactory`; package mode uses `PackageReference`s at `$(SparSdkVersion)` and `Runtime="NET"` tasks from `tools/`, whose version is the SDK's own. `Spar.Sdk.Version.props`, written by the pack into the package, carries `SparSdkVersion` and the xUnit version Spar was built with.
  - **Opt-in surfaces are properties,** not package references an app keeps in step: `SparBrowserTests=true` adds `Spar.Testing.Browser`; `SparBlackBoxTests=true` adds the generated `Projects` class (from `AdditionalFiles` item metadata) and publishes each tested app's Production NativeAOT executable in-process into the test project's `obj/…/spar-apps/`, with `SparBlackBoxBuild=true` giving that publish `obj/Production.BlackBox/` and `bin/Production.BlackBox/` of its own (a Release build of the app may run in the same build); `SparBlackBoxPublish=false` skips it. `RemoveProperties` on the `MSBuild` task also removes what `Properties` sets: list only the globals to drop.
  - **Check a package change with `tests/Spar.PackageTests`:** it packs at two fresh versions and builds a consumer outside the repository, including an update and a downgrade under a warm MSBuild server.
- **Warnings fail the build.** The root `Directory.Build.props` sets `TreatWarningsAsErrors`, and `MSBuildTreatWarningsAsErrors` for every project outside `spikes/`, because `TreatWarningsAsErrors` alone does not fail `MSB*` or `NETSDK*` warnings (NETSDK1229 passed every build in `spikes/cswinrt`). An exception is agreed with the user ID by ID and listed with its reason in `MSBuildWarningsNotAsErrors`.
- **Analyzers in every project.** The root `Directory.Build.targets` turns on the trim, AOT and single-file analyzers for every project outside `spikes/` whose target framework supports them, tests included. The condition needs the project's `TargetFramework`, so it lives in a targets file: on the `netstandard2.0` generator, `EnableAotAnalyzer` is NETSDK1210, which fails the build.

## Output locations

- Runnable output and ordinary publish output go in the project's own `bin/`.
- Generated files, caches and private publish staging go in the project's own `obj/`. Keep publish staging separate from the incremental cache so staging can be cleared before a publish.
- The one generated file outside `obj/` is the contract snapshot, `<Project>.contracts.snapshot`, next to the project. It is committed with LF endings and written only when it changes. Local builds update it; under `ContinuousIntegrationBuild` the build verifies it and fails with SPAR100 when it is stale (`SparContractSnapshot` = `Update` or `Verify`). The client step writes the expected text under `obj/spar/`, and a target that runs on every build compares the committed copy with it, so a stale copy is found even when the step is up to date.
- Test results go under `artifacts/test-results/<project>/` at the repository root, never into authored folders (the empty `TestResults/` folder `dotnet test` otherwise leaves in the working directory); the mechanism is checked in M1a.
- The root `artifacts/` is only for web-engine profiles and test data roots, and for outputs deliberately collected for inspection or transfer, such as Windows Sandbox guest payloads (`AGENTS.md`). Everything there is disposable.
- Keep outputs separate for every project and build dimension that can run concurrently. The NativeAOT test lane has its own `obj/aot/` and `bin/aot/`.

## Verify

- Check a first build and an unchanged repeat. Call a step incremental only after the repeat skips it.
- Check changed, added and removed inputs, and the relevant `dotnet run` or `dotnet publish` path.
- For packaging changes, inspect the package contents and build an isolated consumer that references the package, outside any repository folder, with its own NuGet cache.
  - Isolate `NUGET_PACKAGES`, `NUGET_HTTP_CACHE_PATH`, the template hive (`dotnet new install --debug:custom-hive <dir>`) and the MSBuild server (`MSBUILDNODEHANDSHAKESALT`, plus `SharedCompilationId`). Use separate salts for steps that must start cold.
  - Give the consumer a `nuget.config` with `<clear />` and package source mapping, and check that no ancestor folder has `.git`, `Directory.Build.*`, `Directory.Packages.props`, `global.json` or `nuget.config`.
  - Never reuse a package version; a warm server hides the broken result.
- A warm MSBuild server masks problems: concurrency tests need `DOTNET_CLI_USE_MSBUILD_SERVER=false`, and a broken package needs a fresh evaluation to show. Stop only the process tree you started.
- Design-time compile as a test: `dotnet msbuild -t:Compile -p:DesignTimeBuild=true -p:BuildingInsideVisualStudio=true -p:SkipCompilerExecution=true -p:ProvideCommandLineArgs=true`.
- Escape `;` in `-p:` values as `%3B`. The evidence for these rules is in `docs/pitfalls.md` ("Build, MSBuild and packages", "Tooling and environment").
