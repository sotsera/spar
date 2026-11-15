# M1a: Spar's packages and the package-only consumer test

Recorded 2026-11-15. What it shows: Spar packs into five packages, and an app with its test project, written outside any repository and using Spar from those packages alone, restores, builds, runs its tests, reports a contract problem from the packaged analyzer, keeps a design-time build quiet, publishes with NativeAOT, and builds with the right SDK after an update and a downgrade while MSBuild is warm. Decisions: "Package-only consumer", "Packages for the package-only consumer".

## What was added

| Path | Contents |
|---|---|
| `Directory.Build.props` | `VersionPrefix` 0.1.0 and `VersionSuffix` `dev` unless one is given; `IsPackable=false` by default, `Authors`. |
| `src/Spar`, `src/Spar.Loopback`, `src/Spar.Testing`, `src/Spar.Testing.Browser` | `IsPackable=true` and a description. Their dependencies on each other carry the same version; `Spar.Loopback` keeps its framework reference to `Microsoft.AspNetCore.App`. |
| `src/Spar.Sdk/Spar.Sdk.csproj` | The MSBuild SDK package: `Sdk/` (props, targets, teardown hooks, and `Spar.Sdk.Version.props` written at pack time with the SDK's version and the xUnit version Spar was built with), `tools/` (the task assembly, the generator, their dependencies), `client/dist` and `client/src` (the compiled TypeScript SDK). Packing fails when `src/Spar.Client` is not built. In `Spar.slnx`. |
| `src/Spar.Sdk/Sdk/Sdk.props`, `Sdk.targets` | Package mode beside source mode: `PackageReference`s to `Spar`, and in test projects `Spar.Testing` (and `Spar.Testing.Browser` with `SparBrowserTests=true`), all at `$(SparSdkVersion)` and implicit; the generator as an `Analyzer` item from `tools/`; the client, install and pack tasks from `tools/` with `Runtime="NET"` and no task host; the xUnit version from the SDK. The generated `@spar/app` package now carries the SDK's version (`0.0.0` from source), and the client step's inventory lists it, so a changed SDK reruns the step. |
| `samples/Notebook.EndToEndTests`, `tests/WireShapes.EndToEndTests` | `SparBrowserTests=true` in place of their explicit reference to `Spar.Testing.Browser`. |
| `tests/Spar.PackageTests/` | The test project below; not in `Spar.slnx`. |
| `.github/workflows/ci.yml` | A step after the build tests: `dotnet test --project tests/Spar.PackageTests`. |

The fixture packs the five projects twice, at `0.1.0-dev.<run>.1` and `.2`, into a feed of the run's own under `%TEMP%/spar package tests/<run>/`, and checks that the repository's `git status` reads the same before and after. The consumer lives in `<run>/Nötes Äpp/` (a space and non-ASCII letters), with no `.git`, `Directory.Build.*`, `Directory.Packages.props`, `global.json` or `nuget.config` in any folder above it. It has its own `global.json` (the repository's .NET SDK, `Spar.Sdk` in `msbuild-sdks`, the testing platform runner), a `nuget.config` with `<clear />` and package source mapping that sends `Spar` and `Spar.*` to the feed alone, its own `NUGET_PACKAGES`, an HTTP cache shared across runs but apart from the user's, and its own MSBuild handshake salt and compiler server id. The app project is `Notes.App.csproj` with the assembly name `NotesTool` and warnings as errors.

The tests:
1. **Build and test from the packages:** `dotnet test` of the consumer's test project restores, builds, and passes its one test, which calls the app's contract through the generated `NotesClient`. The generated `package.json` has the first version, the contract snapshot exists beside the project, and `spar/<version>` sits in the run's package folder.
2. **The packaged analyzer:** a `[SparContract]` class without commands fails the build with `Broken.cs(6,14…): error SPAR001`.
3. **Design time:** `dotnet msbuild -t:Compile -p:DesignTimeBuild=true …` passes and runs no client step.
4. **NativeAOT:** `dotnet publish -c Release` succeeds; the executable runs, says there is no desktop host and exits with 2 (M1a has none).
5. **Update and downgrade with MSBuild warm:** with node reuse and the MSBuild server on (under the run's salt), the consumer's `global.json` goes from the first version to the second and back, and each build passes with the generated `package.json` at that version. The run's server is then stopped with `build-server shutdown --msbuild` under the same salt, never every server on the machine.

## Results (observed, Windows 11)

| Check | Result |
|---|---|
| `dotnet test --project tests/Spar.PackageTests` | 5 of 5 in three consecutive passing runs, 35–45 s each with the HTTP cache warm |
| The first run (empty HTTP cache, 151 MB downloaded into it) | 2 min 34 s; its first test alone took 2 min 18 s |
| Per test, warm cache | build and test 6.6 s, NativeAOT publish 5.4 s, design-time build 0.55 s, analyzer 1.2 s, update and downgrade 5.7 s |
| `Spar.Sdk` package | 3,817,239 B: 3 files under `Sdk/`, 2 under `Sdk/Testing/`, 5 under `tools/`, 28 under `client/dist/`, 7 under `client/src/` |
| A passing run's folder | deleted; only the shared HTTP cache stays |
| The working-tree check, made to fail by writing the version file beside the project | all 5 tests fail with "Strings differ"; a failing run keeps its folder for the next run to delete |

Two problems the test found and that are fixed: `dotnet test -nr:false` under the testing platform passes the switch on to the test application ("Unknown option '--nr'", zero tests; now `MSBUILDDISABLENODEREUSE=1` in the consumer's environment), and the pack wrote `Spar.Sdk.Version.props` into `src/Spar.Sdk/`, because `IntermediateOutputPath` is still empty in a project's body (now set inside the target, and caught by the working-tree check). Both are in `docs/pitfalls.md`.

## Not done or not verified

- **The other generality fixtures:** a frontend (and so a non-default frontend folder), the Browser surface in an end-to-end project, and a custom app id. The consumer has no page yet; the frontend steps in package mode (`SparFrontendInstall`, `SparPack` from `tools/`) are declared but not run by this test.
- **Linux and CI:** not run on Linux; the CI step has not run (there is no push yet). Under CI's `ContinuousIntegrationBuild=true` the consumer's fresh app would verify a snapshot it does not have, so the fixture sets it to `false` for the consumer; that path is not exercised yet.
- **A central-package-management consumer:** the implicit `PackageReference`s are untested with `Directory.Packages.props`.
- **The `dotnet new` template:** not in M1a (decision "Packages for the package-only consumer").
- **Release versions and publishing** stay open for M3.
