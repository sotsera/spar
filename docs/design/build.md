# Build and packaging

Part of the Spar design. [`docs/plan.md`](../plan.md) has the goal, the settled decisions, the open questions and the milestones, and lists every design file. Section numbers are global: "section 1" anywhere in the plan, the design files or `docs/decisions.md` means the heading of that number, wherever it lives.

## 1. Build and packaging

- **Packages:**
  - `Spar.Sdk`, one MSBuild project SDK for app and test projects: `<Project Sdk="Spar.Sdk">`, version pinned in `global.json` (`msbuild-sdks`). It ships the generator, the analyzer, the task assembly and the compiled TypeScript SDK, each once.
  - Runtime packages: `Spar` (core), `Spar.Windows`, `Spar.Linux`.
  - **Choosing platforms (decided, user, 2026-11-14):**
    - An app sets `<SparPlatforms>windows;linux</SparPlatforms>`, or one of the two; both is the default.
    - `Spar.Sdk` adds the host packages at its own version, so apps never reference `Spar.Windows` or `Spar.Linux` themselves, and `dotnet new spar-app --platforms …` writes the property.
    - Each platform maps to its runtime identifiers (`win-x64`/`win-arm64`, `linux-x64`/`linux-arm64`), and NativeAOT removes the other host's code from each executable.
    - Running on a platform the app does not target stops with a clear error that names the property.
    - NativeAOT does not cross-compile between operating systems, so each executable is published on its own OS or in a container of it.
  - **Runtime choices (built in M1d step 7; decision "Runtime choices (implementation record; choices for review; 2026-11-23)"):** NativeAOT, `InvariantGlobalization=true` and no accepted warnings stay Spar's defaults; an app's own project may choose CoreCLR (framework-dependent, or a self-contained single file), accept named AOT warnings that stay warnings (`WarningsNotAsErrors`), or culture data, as `docs/guide/publishing-choices.md` shows with blocks the package tests build. Spar's machinery works in each shape: the WebView2 loader stays beside a single file unless the app sets `IncludeNativeLibrariesForSelfExtract`, and a black-box test project copies the app's whole publication.
  - Testing packages per test level.
  - `Spar.Templates`, the `dotnet new spar-app` template, packed with the others at the same version (M1d step 6a): the new app's `global.json` names that version for `Spar.Sdk`, filled in when the package is packed.
  - The `spar` .NET tool, versioned with the SDK: the package `Spar.Tool` (command `spar`), framework-dependent, packed with the others at the same version and named by a new app's local tool manifest (M1d step 6b).
- **Package rules:**
  - Never reuse a version.
  - The runtime package is referenced implicitly at the SDK's version.
  - The SDK version is an input of the client step.
  - NuGet's `analyzers/` folder is ignored for MSBuild SDK packages, so the generator is added as an `Analyzer` item from `Sdk.targets`.
- **Pipeline:**
  1. `ResolveReferences`.
  2. Contract inventory, written only when it changed.
  3. Client step: the generated modules under `obj/spar/client` (`.js`, `.d.ts`, `.d.ts.map` to C#) and the contract snapshot.
  4. Frontend: `npm ci --ignore-scripts` when the lockfile changed (section 3c), in every variant, because the Debug type-check and the dev server need `node_modules` too; then the type-check in every variant (Debug included, see below); in Release and test variants also `vite build` into `obj/`, then packing.
  5. One `CoreCompile`, where the generator and analyzer run.

  Steps 2 to 4 are built (M1a; decisions "Client step: the generated TypeScript modules", "Notebook page and the frontend build" and "Frontend packing built"); the install runs under a lease the app's variants share, and the steps run before `PrepareResources`, so the pack is an embedded resource in time.

  Design-time builds run none of steps 2–4.
  - **The client refreshes on build** in the IDE (Visual Studio and VS Code build on F5), and **on save while the development loop runs:** under `dotnet watch` or `spar dev` a saved contract change restarts the app, which regenerates the client (5.4–6.1 s measured with Vite reused), so frontend editors see it without a manual build. There is no IDE-specific refresh path (decision "M1a names").
  - **Debug builds on Windows map the drive letter to upper case** (`e:\=E:\` in `PathMap`; decision "The drive-letter PathMap in Debug builds"), because VS Code builds through a lower-case drive and its Hot Reload then finds no source in the PDB. Builds with deterministic source paths keep their own map, and `SparMapDriveLetter=false` turns it off.
  - **Debug type-checks too (decided, user, 2026-11-14),** with the opt-out `SparTypeCheck=false` (name confirmed, decision "M1a names"). It runs only when the generated client or the TypeScript sources changed since the last check, and reports errors at file and line, so a contract rename breaks `dotnet build` and an agent sees it. The app's `tsconfig` decides how strict it is; one with references (Vite's scaffold) is checked with `tsc -b --noEmit`, any other with `tsc --noEmit -p .` (decision "The M1d check follow-ups"). The measured baseline (`docs/evidence/react`, target `SparDevTypeCheck`) cost about 1 s of a contract restart and gated the build (TS6133 failed it).
- **Frontend packing:** one `Spar.Frontend` resource holding an index plus per-file Brotli (quality 11, fitted window). Built in M1a (decision "Frontend packing built"), with the asset checks (SPAR110) and the third-party notices as `Spar.ThirdPartyNotices`.
  - Files that Brotli shrinks by less than 5% are stored as is.
  - The host decodes on a background thread at start into a cache, and serves uncompressed entries straight from the image.
  - Small bundles may pack uncompressed, with a feature switch that trims the decoder. Built in M1a (decision "Frontend packing: stored below the decoder's cost"): `SparFrontendCompression` is `auto` (the default: Brotli when it saves at least 192 KiB over page and notices together), `brotli` or `none`, and any other value is SPAR112. A stored pack sets `Spar.Frontend.Brotli=false` with `Trim="true"`; the decision is kept in `obj/…/spar-pack.mode`, so a build that skips packing still applies it. The notices resource starts with one byte for its encoding (0 stored, 1 Brotli).
  - No zip.
  - Measured:
    - the decoder adds about 168 KB on both OSes, statically linked;
    - a 2.8 MB bundle decodes in about 5 ms and makes the exe 44% smaller than raw;
    - neither WebView decodes `Content-Encoding` itself.
  - Deflate was measured only inside the zip container: its payloads were about 15% larger than Brotli's (React 71,240 against 61,176 B; the 2.8 MB bundle 1,006,299 against 875,155 B). Per-file Deflate inside the same index, measured later on the real bundle, gave a larger executable (payload 6–15% larger, decoder 192 KB against 168 KB) and faster decoding (decision "Frontend packing: the Deflate control keeps Brotli").
- **Assets are served with `Cache-Control: no-store`.** Debug builds embed no bundle (see section 4).
- **Determinism:** write-if-changed outputs, with stamp files as MSBuild `Outputs`. Exclude every `bin/`, `obj/`, `node_modules` and `dist` from the default globs.
- **Multithreaded MSBuild:** every task is safe for `-mt`, and a build-test lane runs `-mt`.
- **A package-only consumer test from M1's first day:** an isolated feed, package folder and HTTP cache (`NUGET_HTTP_CACHE_PATH`), outside any repository folder, covering update and downgrade while warm.
