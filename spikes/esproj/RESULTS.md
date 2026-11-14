# `.esproj` spike: results

Recorded 2026-11-13 on the development machine: Windows 11, SDK 11.0.100, Node 24.21.0, npm 11.19.0, Visual Studio Community 2026 18.12.0 (MSBuild 18.12.2), `Microsoft.VisualStudio.JavaScript.Sdk` **1.0.7204101**. Other agents were building at the same time, so absolute timings are noisy; compare the modes measured side by side.

**Question.** Would the `.esproj` project type help Spar as the home of the app's single-page app: build integration, npm restore, incremental builds, the IDE, and how the C# app consumes the output?

## What was built

| Part | Role |
|---|---|
| `Esproj.Frontend/Esproj.Frontend.esproj` | Copy of the React frontend from `../react` (sources, `package.json`, lockfile, `tsconfig`, `vite.config.ts`). The `@spar/*` packages are `file:` dependencies on `../Esproj.App/obj/spar/client/packages/@spar/*`. Vite writes to `obj/dist/` (`BuildOutputFolder`, `BuildCommand`). `SpikeEsprojIncremental` (default `true`) turns off the SDK's build step and runs the same command from a target with Inputs/Outputs. |
| `Esproj.App` | Minimal NativeAOT C# app (notes contract from `../react`). It calls the generated dispatch and lists the embedded `Spar.Frontend` zip. |
| `Esproj.App/Esproj.targets` | Consumes the `.esproj`. `SpikeFrontendMode`: **`Drive`** (default): after `SparGenerateClient`, the `MSBuild` task builds the `.esproj` and packs the folder it returns. **`Reference`**: a `ProjectReference` (`ReferenceOutputAssembly=false`), then `GetTargetPath`. **`Cycle`**: `Reference` plus a `ProjectReference` from the `.esproj` back to the app for `SparGenerateClient`. Debug (`SparDevServerBuild=true`) embeds a placeholder and only runs the SDK's `RunNpmInstall`. |
| `Esproj.App/React.targets` | Copy of `../react/React.App/React.targets` as the **baseline** (`SpikeFrontendMode=Targets`): the same app, the same frontend folder, no `.esproj` involved. |
| `pipeline/` | Copy of the `../d1-pipeline` generator, runtime, tasks, tool and `Spike.targets` (taken 2026-11-13 17:18). One change: the frontend input glob also excludes the frontend's `obj/` and `bin/`. |
| `Esproj.slnx` | Pipeline projects, the app and the `.esproj`. |
| `measure-build.sh <mode> [args]` | Which steps run for which edit, timed. Builds Debug with `SparDevServerBuild=false`, like `../react/measure-build.sh`. |

## The SDK

- **Version.** Latest on nuget.org: 1.0.7204101, published 2026-10-28, listed. The local cache held only 1.0.4338480.
- **Download.** One package, 1.0.7204101: a 180,174-byte `.nupkg`, about 1.1 MB unpacked (mostly localized XAML property pages). It came through NuGet's SDK resolver, from `Sdk="Microsoft.VisualStudio.JavaScript.Sdk/1.0.7204101"`. Nothing else was downloaded: npm used its cache.
- **Contents.** `Sdk.props` and `Sdk.targets` (about 370 lines), `Sdk.WebAssets.targets`, and two task builds (`net472` for `MSBuild.exe`, `net6.0` for `dotnet`). The tasks are `GeneratePackageJsonProps` and `RunNpmAuditJson`. The only target difference from 1.0.4338480 is that publishing now prefers `PublishCommand`.
- **It is Microsoft.Common.targets with npm hooks:**
  - `TargetFramework` is hard-coded to `net6.0`.
  - The SDK's own `CoreCompile` runs `npm install` (when needed), then `BuildCommand`.
  - It has no Inputs or Outputs.
  - `OutputPath` and `TargetPath` are `BuildOutputFolder`.

| Property | Default | What it does (from `Sdk.targets`, observed) |
|---|---|---|
| `ShouldRunNpmInstall` | `true` | `RunNpmInstall` runs **`npm install`** (not `npm ci`). Its input is `package.json` and its output is `package-lock.json`. Afterwards it **touches `package-lock.json`**. If `node_modules` is missing, it first **touches `package.json`**. |
| `ShouldRunNpmAudit` | `true` | After each install, `RunNpmAuditJson` runs `npm audit --json`, a registry request. `npm install` audits as well, and the command has no `--no-audit`. |
| `ShouldRunBuildScript` | `true` | Runs `BuildCommand` in `CoreCompile`, **on every build**. |
| `BuildCommand` | `npm run build` (or `compile`) | |
| `BuildOutputFolder` | (none) | Becomes `OutputPath` and `TargetPath`. A `ProjectReference` or `GetTargetPath` returns this folder. |
| `StartupCommand` | `npm run start`, `serve` or `dev` | `dotnet run` on the `.esproj` runs `cmd /C <StartupCommand>`. |
| `CleanCommand`, `TestCommand`, `PublishCommand` | from `package.json` scripts | `Clean` first depends on `RunNpmInstall`. |

The official reference, [MSBuild properties for JSPS](https://learn.microsoft.com/visualstudio/javascript/javascript-project-system-msbuild-reference), says `ShouldRunNpmInstall` runs npm install "on Build and Restore". From the CLI, **`dotnet restore` did not install anything**: `node_modules` stayed absent. Restore only regenerates `obj/<Configuration>/package.g.props`.

## Results (observed)

### CLI support

- `dotnet build`, `dotnet publish`, `dotnet run` and `dotnet watch` all run on SDK 11.0.100, with no Visual Studio.
- `MSBuild.exe` from VS 18.12 also builds the solution. It uses the SDK's `net472` tasks: a clean Release build took 12.6 s, the bundle was embedded and the app ran.
- With the repository's `TreatWarningsAsErrors`, there were no warnings anywhere.

### What the stock `.esproj` does in a build

First Release build (`Drive` mode), from a binlog:
1. `RunNpmInstall`: `npm install`, 26 packages, 2 s, including npm's own audit.
2. `RunNpmAuditJson`: a second audit.
3. `GeneratePackageJsonProps`.
4. `CoreCompile`: `npm run build -- --outDir obj/dist --emptyOutDir`, which is `tsc --noEmit` then `vite build`.

Output: 223,561 bytes of JavaScript plus `index.html`, embedded and listed by the app.

**The stock build step is not incremental.** Every build re-runs `tsc` and `vite build`, which take about 1.8 s by themselves (`npm run build` 1.72–1.90 s, `tsc` alone 0.78–0.81 s). The rewritten `dist` timestamps then re-run the app's pack step on every build:

| Edit (`measure-build.sh Drive -p:SpikeEsprojIncremental=false`) | Run 1 | Run 2 | Client | npm | Vite | Pack | C# compile |
|---|---:|---:|---|---|---|---|---|
| No-op | 6.3 / 5.6 s | 6.9 / 5.4 s | no | no | **yes** | **yes** | no |
| C# method body | 4.6 s | 6.7 s | yes | no | **yes** | yes | yes |
| Frontend component | 3.3 s | 11.2 s | no | no | yes | yes | yes |
| New contract | 3.8 s | 4.9 s | yes | no | yes | yes | yes |
| No-op after the edits | 3.5 s | 3.5 s | no | no | **yes** | **yes** | no |

**It can be made incremental inside the `.esproj`.**
- Set `ShouldRunBuildScript=false` and add one target. It runs `$(BuildCommand)` with Inputs (`src/**`, `index.html`, `package.json`, the lockfile, `tsconfig.json`, `vite.config.ts`, `node_modules/@spar/**`) and Outputs (`obj/dist/index.html`, touched).
- MSBuild's glob follows npm's `file:` links into the generated packages.
- The `.esproj` then behaves like `React.targets`:

| Edit (Debug, bundle built) | Baseline `React.targets` (3 runs) | `.esproj`, Drive, incremental (3 runs) | Steps, both modes |
|---|---:|---:|---|
| No-op | 0.79–1.85 s | 0.74–1.56 s | none |
| C# method body | 1.8–2.2 s | 2.0–3.5 s | client, pack, csc (no Vite) |
| Frontend component | 2.1–2.7 s | 2.1–4.6 s | Vite, pack, csc (no client) |
| New contract | 3.1–3.2 s | 2.7–4.1 s | client, Vite, pack, csc |

**No-op cost, interleaved** (6 alternating builds each, warm): baseline 685–1,583 ms (median about 800), `.esproj` Drive 794–1,663 ms (median about 860). Evaluating and calling the `.esproj` costs about **50–100 ms per build**. `../react/RESULTS.md` recorded 1.1 / 2.7 / 3.2 / 4.1 s for the same four rows. Those were taken at another time, under different load.

### Ordering with the generated client (`ProjectReference` does not work)

The frontend needs the client that the app generates. The app needs the bundle that the frontend produces. MSBuild can't express that with project references:

| Mode | Result |
|---|---|
| **Reference**, working tree | Builds, but **one build late**: after a contract edit the client is regenerated **after** Vite ran, so the bundle is stale (`vite=no`). The *next* no-op build then runs Vite and recompiles. |
| **Reference**, clean clone (`-c Release`) | `npm install` succeeds and leaves dangling links. Then `tsc` fails with **TS2307** `Cannot find module '@spar/notes'` (and `@spar/sdk`). It fails again on every retry, because the app never reaches `SparGenerateClient`. |
| **Cycle** (the `.esproj` references the app for `SparGenerateClient`) | **MSB4006**: circular dependency involving `_GenerateRestoreProjectPathWalk`, already at restore. |
| **Drive** (the app calls `MSBuild` on the `.esproj` after `SparGenerateClient`) | Correct in every case measured. A clean-clone `dotnet build Esproj.slnx -c Release` took 4.5 s (npm install included); a clean Debug build took 5.0 s. |

**So the only working shape is the app driving the `.esproj`:** the `.esproj` is not a dependency in MSBuild's graph, it is a sub-build invoked at the right moment. That is what `React.targets` does with `Exec`, one level removed.

### Solutions (`.slnx`)

**The `.esproj` is not built by a solution build by default.**
- `dotnet build Esproj.slnx` and `MSBuild.exe Esproj.slnx` both log: `The project "Esproj.Frontend" is not selected for building in solution configuration "Release|Any CPU"` (`BuildProjectInSolution="False"`).
- The same happens with an explicit `Type="{54a90642-…}"`.
- It also happens with a `.sln` written by `dotnet sln add`: that writes only `ActiveCfg`, never `Build.0`, for the `.esproj`.
- For Drive mode this is what we want: a solution build does not build the frontend twice.

**With `<Build Project="true" />` on the `.esproj` entry, the frontend was built twice in one build.**
- Once by the solution, with `Platform=AnyCPU`.
- Once by the app's `MSBuild` call, with different global properties, so it was a second configuration.
- Each ran `vite build`.
- On a clean clone, the solution's copy would run before the client exists, the same failure as `Reference`.

So with this design, the solution must not build the `.esproj` itself.

### Debug, `dotnet watch`, `dotnet run`

- **Debug skips the bundle.** In Drive mode the app embeds the 76-byte placeholder and calls only the `.esproj`'s `RunNpmInstall`, so the dev server has `node_modules`. A no-op Debug build of the solution took 605–631 ms.
- **Not carried over:** `React.targets` also type-checks in Debug (`SparDevTypeCheck`).
- **In Reference mode a Debug build runs Vite anyway,** because a `ProjectReference` builds the `.esproj` regardless of `SparDevServerBuild`.
- **`dotnet watch` (Drive)** loaded 5 projects (no `.esproj`). The first build and run took 4.2 s. A C# edit was rebuilt and rerun in 3.2 s. A frontend edit triggered nothing, which is right: Vite owns the frontend.
- **`dotnet watch` (Reference)** loaded 6 projects, including the `.esproj`. A frontend edit again triggered nothing.
- **`dotnet run --project Esproj.Frontend.esproj`** ran `cmd /C npm run dev`. Vite was ready in 1.9 s, and `GET /` returned 200.
  - The process tree was `dotnet → cmd → node`.
  - `taskkill /T` on the `dotnet` pid left no `node` process behind.
  - It is a Vite launcher, not a backend-aware dev session. Spar's app already starts or reuses Vite itself (`../react`).

### Publish

- **`dotnet publish Esproj.App -c Release`**: the NativeAOT exe is 2,125,312 bytes, with **no warnings** (strict AOT, warnings as errors). It runs and lists `assets/index-CC7XkJt5.js` (223,561 bytes) and `index.html`.
- **`dotnet publish Esproj.slnx -c Release`**: the same, plus publish folders for the pipeline projects.
- **Publishing the `.esproj` itself** gives `Skipping unpublishable project` (`PublishableProject=false`).
- **The SDK's own publish integration is ASP.NET Core static web assets** (`GetCurrentProjectPublishStaticWebAssetItems`, `wwwroot`). It does not apply to an embedded zip in a NativeAOT desktop app.

### Restore, lockfile, clean

**A lockfile-only change does not reinstall.**
- After `touch package-lock.json` (as after a `git pull` that only bumps a transitive version), the `.esproj` skipped `npm install`: its input is `package.json`, and it counts the lockfile as its output.
- The baseline ran `npm ci`: its input is the lockfile and its output is `node_modules/.package-lock.json`.
- A clone or checkout that writes both files with near-equal timestamps can also skip the install. The SDK's own comment acknowledges this, and its fix is to touch `package.json`.

**`npm install`, not `npm ci`.**
- With a lockfile out of sync, it rewrites `package-lock.json` during a build.
- With it in sync, the content was unchanged here (same MD5 before and after a clean install), but the SDK **touches `package.json` and `package-lock.json`** (timestamps changed), so authored files are modified by a build.
- That touch also made the baseline's `npm ci` run once after the `.esproj` had installed.

**Clean.** Neither `dotnet clean Esproj.slnx` (Drive) nor the app's clean in Reference mode removed `obj/dist`. Clean runs `RunNpmInstall` first and then `CleanCommand`, if one exists.

## Side by side

| Aspect | `React.targets` (current) | `.esproj` |
|---|---|---|
| Where the frontend lives | `frontend/` inside the app project | Its own project, a sibling folder |
| Build from `dotnet build` and `MSBuild.exe` | yes | yes, but **only when the app drives it**. The solution doesn't build it by default, and a `ProjectReference` builds it before the client exists. |
| Generated `@spar` packages before the frontend build | Inherent (one project, target order) | Only through the app's `MSBuild` call after `SparGenerateClient` |
| npm restore | `npm ci --ignore-scripts --no-audit --no-fund` when the lockfile is newer than `node_modules/.package-lock.json` | `npm install` when `package.json` is newer than the lockfile. Misses lockfile-only changes, touches both files, audits twice. Not configurable beyond on/off. |
| Incremental frontend build | Yes (Inputs/Outputs) | **No** by default (Vite on every build, +3–6 s). Yes after replacing the build step, at about the same cost as the baseline. |
| No-op build | ~0.8 s | ~0.86 s incremental; 3.5–6.9 s stock |
| Debug skips the bundle | Yes, with a dev type-check | Yes in Drive mode (no type-check yet) |
| Output location | `obj/<cfg>/spar/frontend-dist` in the app | `BuildOutputFolder` (the `.esproj`'s `obj/dist`), returned as `TargetPath` |
| Clean | App clean (not checked here) | Bundle not removed |
| NativeAOT publish | Clean | Clean; the `.esproj` adds nothing to publish |
| `dotnet watch` | Frontend excluded by `Watch Remove` | Not in the graph (Drive) |
| Consumer model (one app project) | One project | Two projects and a convention between them (paths both ways) |
| IDE | The frontend is a folder of the C# project | A real project node in VS with npm UI, `StartupCommand`, debugging profiles. **Not verified here.** |
| Dependency | none | One MSBuild SDK package (180 KB), pinned per project |

## Recommendation

**Don't adopt `.esproj` for build integration. At most, offer it later as an optional IDE wrapper, once the interactive checks below are done.**

- **Everything Spar's pipeline needs had to be rebuilt or worked around.** Ordering forced `MSBuild` calls instead of references. Incrementality needed a replacement for the build step. The `npm ci` semantics are lost unless `ShouldRunNpmInstall=false` and our own target is added back. The bundle location had to be set, and clean doesn't remove it.
- **What is left from the SDK is property pages, `dotnet run` → `npm run dev`, and the VS project node.** None of these is needed for the build.
- **It splits the app into two projects with paths in both directions.** That conflicts with "one user app project" and adds a second graph node the solution must *not* build.
- **The baseline already measures the same or faster** (about 50–100 ms less per no-op build), works in a clean clone, and runs the type-check in Debug.
- **The IDE case is the only open benefit.** If interactive checks show that VS gives a materially better frontend experience with the `.esproj` (npm UI, JS debugging, task runner), Spar could generate an `.esproj` as a thin wrapper that the app drives (Drive shape, `ShouldRunNpmInstall=false`, `ShouldRunBuildScript=false`, no build of its own). It would stay out of the build logic, with the frontend still built by Spar's targets.

## Observed, skipped, not tested

**Observed (this spike, by running):**
- Package lookup and the single download.
- CLI and `MSBuild.exe` builds in Debug and Release, from a clean state and incremental.
- Step-by-step incrementality for the stock, tuned and baseline variants, with interleaved no-op timings.
- The Reference late-build and clean-clone failures, and the Cycle MSB4006 error.
- The solution not building the `.esproj` by default, and the double build when forced.
- `dotnet restore` not installing anything.
- The lockfile-only change being missed, and both npm files being touched.
- Clean leaving the bundle.
- `dotnet watch` in Drive and Reference modes.
- `dotnet run` on the `.esproj` starting Vite, and the process tree ending.
- A strict NativeAOT publish with no warnings, and the embedded bundle.

**Skipped:**
- A cold npm cache: every install used the warm cache.
- `PublishCommand`, `TestCommand` and `CleanCommand` scripts: none was defined.
- The dev-session bridge in `vite.config.ts`: its paths were adjusted, but no backend session was run.
- The debug-build type-check.
- Linux and macOS (`ShellEnvironment=sh`).

**Not tested: needs an interactive check in the IDE.**
1. **Visual Studio 2026 opening `Esproj.slnx`.** Does the `.esproj` load as a JavaScript project, and does VS itself run `npm install` on load? (The CLI restore doesn't.)
2. **VS Build Solution.** Does VS, unlike the CLI, mark the `.esproj` for build (its templates write `Build.0`)? If it does, the frontend is built twice, and in a clean clone it fails before the client exists. Check the build output for two Vite runs.
3. **VS build when saving the `.slnx`.** Does VS rewrite the `.esproj` entry, for example adding `<Build>` or `Type`?
4. **F5 with multiple startup projects** (app and `.esproj`). Does VS start Vite through `StartupCommand` and attach the browser or JS debugger, and does it conflict with the app starting or reusing Vite itself?
5. **The VS fast up-to-date check** for the app when only frontend files change. The `.esproj` isn't a reference in Drive mode, so VS may skip the app build entirely and leave a stale bundle.
6. **VS npm UI and IntelliSense** for `@spar/*` through the `file:` links into `Esproj.App/obj`.
7. **VS Code with C# Dev Kit loading `Esproj.slnx`.** Does the `.esproj` show as an unsupported or unloaded project, and does it produce errors or design-time builds that run npm? Design-time behaviour of the `.esproj` was not exercised.
