# Decisions

This is an append-only log. Each entry states the decision, the evidence behind it, and what would make us revisit it. Plan sections D1–D8 refer to [the plan](plan.md).

## 2026-11-12: Baseline toolchain

- **Decision.** Use .NET SDK 11.0.100 (pinned with `latestPatch` roll-forward), Microsoft.Testing.Platform, central package management, and warnings as errors in every project.
- **Generators** reference `Microsoft.CodeAnalysis.CSharp` 5.9.0, while the SDK compiler is 5.12. A generator must load in the IDE's compiler, which can lag behind the SDK's.
- **Revisit** when the oldest supported Visual Studio or C# Dev Kit ships a newer Roslyn.

## 2026-11-12, D1: Compilation pipeline. Candidate A, provisionally adopted

- **Decision.** An incremental source generator owns all C#: dispatch, catalog, manifest, and serializers written directly against `Utf8JsonReader`/`Utf8JsonWriter`. A pre-compile tool shares the same contract-analysis code and writes only the TypeScript client and manifest. The frontend zip is embedded, then a single `CoreCompile` runs. Contract analysis reads an MSBuild-written inventory (sources, references, defines, nullable) instead of capturing the full `Csc` command line.
- **Evidence** (`spikes/d1-pipeline/RESULTS.md`):
  - NativeAOT exe of 2,243,072 bytes, versus 3,131,904 with System.Text.Json metadata: 28% smaller.
  - A no-op build skips everything (0.6–1.2 s).
  - Contract add, rename and remove regenerate the client correctly and delete stale modules.
  - Unsupported types fail the normal compile with a generator diagnostic at file:line.
  - A design-time compile needs no tool run and no prior build.
- **Open.**
  - Confirm diagnostics and generated-code navigation interactively in VS Code and Visual Studio.
  - The tool adds 0.5–1.0 s to every C# edit; reduce it in M1 (narrow inputs, early exit, or a resident tool).
  - Candidate B was not built, since A met the criteria.
- **Revisit** if IDE checks fail, or if serializer behaviour diverges from System.Text.Json in ways consumers need (number handling, escaping, enums).

## 2026-11-12, D1 follow-up: Spar build steps run as MSBuild tasks, not `Exec dotnet`

- **Decision.**
  - **Task assembly.** The client and pack steps (and later the inventory and the build lease) live in a compiled `net11.0` task assembly. Every task is marked `[MSBuildMultiThreadableTask]`, implements `IMultiThreadableTask`, resolves paths only through `TaskEnvironment`, and keeps no unsynchronized static state.
  - **Reference cache.** The contract-analysis task keeps a process-wide cache of metadata references, keyed by path, timestamp and size.
  - **Hosting.** Declare the tasks with `Runtime="NET"`. That loads them in-process under `dotnet build` (warm MSBuild node, or all projects in one process with `-mt`). Under Visual Studio's .NET Framework `MSBuild.exe`, it uses the pooled .NET task host.
  - **Source checkout.** When the task assembly is built in the same graph (Spar's own source checkout), use `TaskFactory="TaskHostFactory"` instead, to avoid the locked-DLL failure.
- **Evidence** (`spikes/d1-pipeline/RESULTS.md`, follow-up section):
  - The client step drops from about 500 ms (`Exec`) to 7–29 ms whenever the host is reused. Save-and-build drops from 1.5–1.9 s to 0.75–1.0 s with `-mt`.
  - Under VS `MSBuild.exe` 18.12.2 the pooled task host kept one process and a warm cache across builds.
  - The tasks run under `-mt` with no warnings.
  - `TaskHostFactory` gives no speed benefit.
  - A warm node locks the task DLL (`MSB3027`) when the task project is rebuilt in the same graph.
- **Open.**
  - Not checked: IDE design-time builds, and how long an idle pooled task host lives.
  - `-mt` is experimental, so Spar must not require it. A build-test lane should still run `-mt` to keep the tasks safe for it.
- **Revisit** if task-host lifetime turns out too short to keep the cache useful, or if `-mt` becomes the default and changes registered-object semantics we depend on.

## 2026-11-12, D1 follow-up: IDE design-time behaviour (headless evidence)

- **Finding.** Through Roslyn `MSBuildWorkspace` (the design-time path both IDEs use), the contract generator:
  - loads and produces all generated C# with no prior build;
  - regenerates the binding after an unsaved edit;
  - reports `SPIKE003` at the source location for an unsupported type.

  The client tool never runs at design time. Visual Studio 18 and the VS Code C# extension both ship Roslyn 5.12, above the generator's Roslyn 5.9 reference.
- **Consequences for M1.**
  - An invalid command must not drop its whole contract's binding; skip only that command.
  - The TypeScript client refreshes only on build, so frontend editing between C# edits sees a stale client. Decide in M1 whether that's acceptable or the editor view needs an on-save refresh.
- **Not verified:** behaviour in the real IDE windows (squiggles, navigation, Error List, refresh latency). Computer use was unavailable. Repeat these checks interactively. Evidence: `spikes/d1-pipeline/RESULTS.md`, "IDE behaviour".
- **Added the same day: Visual Studio's build engine (`devenv.com /Build`, run from PowerShell).**
  - The `Runtime="NET"` tasks ran in one pooled .NET task host that survived between separate `devenv` runs. The client step took 799 ms, then 30 ms and 20 ms with all 177 references reused.
  - Rebuilding the task project while that host had the DLL loaded failed with MSB3026/MSB3027 ("4 succeeded, 1 failed"). This confirms the `TaskHostFactory` rule for Spar's source checkout.

## 2026-11-13, D1 follow-up: interactive IDE checks

Observed by computer use in Visual Studio 18 and VS Code 1.146 (C# extension 2.160.4, C# Dev Kit 3.40.215). Evidence: `spikes/d1-pipeline/RESULTS.md`, "Interactive IDE checks", with screenshots in `artifacts/d1/`.

- **Confirmed in both IDEs:**
  - On a clean open with no build, generated `SparCatalog` resolves with 0 errors and no Spar build step runs.
  - Go to definition opens the generated file. VS marks it read-only with a "Rerun generator" banner.
  - IDE builds succeed and reuse the warm reference cache: 445 ms then 14 ms in VS, 447 ms then 53 ms in VS Code.
- **Generated-file listing:** VS lists the generated files under Dependencies › Analyzers. VS Code (Dev Kit "C# Project Details") doesn't; there they are reachable only by navigation.
- **External edits:** neither IDE regenerates when a file changes on disk from outside, for example from git, a script or an agent. Generated code and generator diagnostics stay stale until a Save in the IDE, VS's "Rerun generator", or a build. VS Code moved a stale diagnostic onto unrelated code until the next save.
- **Generator diagnostic display:** after Save, VS showed `SPIKE003` in the Error List within 2–8 s but drew **no squiggle**. VS Code showed both squiggle and Problems entry within 5 s.
- **Mixed line endings:** generated files mix line endings (`\n` header plus `AppendLine`'s `\r\n`), and VS raises an "Inconsistent Line Endings" dialog when they're opened.
- **M1 requirements from this:**
  1. Emit generated sources with a single `\n` newline style, never `AppendLine` or `Environment.NewLine`.
  2. Add a contract `DiagnosticAnalyzer` that shares the contract-analysis code with the generator. It reports SPAR diagnostics on every edit, in both IDEs, whatever the generator's execution mode, so VS shows a squiggle too.
  3. An invalid command skips only itself, not its whole contract (from the earlier entry).
  4. Document that agents and scripts editing files outside an IDE need a build, or the IDE's save or rerun, before generated code and the TypeScript client are current. The CLI build is the source of truth.
- **Not verified:** live refresh while typing in the real IDE windows (covered only by the `MSBuildWorkspace` probe), whether VS's Automatic generator mode shows the squiggle, and how long a pooled task host lives when idle.

## 2026-11-13, D8: Devtools automation transport, in-process CDP

- **Decision.** UI automation for devtools and desktop tests uses `ICoreWebView2::CallDevToolsProtocolMethod` from inside the app. There is never a remote debugging port. Agents reach it only through Spar's own endpoint: `127.0.0.1:0`, a per-launch bearer token, a Host-header check, Origin rejection, and an atomically written lockfile.
  - Click and fill default to trusted CDP `Input.*`, with `Runtime.evaluate` as an explicit `via: script` fallback.
  - Devtools code is removed from Production by a trimmed feature switch (`[FeatureSwitchDefinition]` + `RuntimeHostConfigurationOption Trim="true"`). This proved as complete as `#if`: 0 bytes difference.
  - The WebView2 loader is linked statically: about 23 KB, no sidecar DLL.
- **Evidence** (`spikes/d8-automation/RESULTS.md`), from an AOT exe on WebView2 Runtime 157:
  - Medians: accessibility tree 0.5 ms, trusted fill 1.4 ms, trusted click 10 ms, waitFor about 7 ms over the page's own delay, screenshot 48 ms.
  - The endpoint adds under 1 ms per call.
  - Zero ILC or trimming warnings.
  - All devtools in a build cost +1.18 MB; CDP alone costs +0.55 MB.
- **Consequences.**
  - **Window mode:** headless desktop runs (desktop tests, `spar snap`) must use a **shown, non-activated window placed offscreen**. With `SW_HIDE` or a hidden controller, trusted input hangs, `requestAnimationFrame` never fires and timers throttle to about 1 s. This replaces the plan's "hidden window".
  - **Readiness:** UI tools wait for a double `requestAnimationFrame` after navigation before the first input. Without it, one run in five stalled about 5 s.
  - **Playwright** cannot run inside a NativeAOT process (reflection JSON); it stays in JIT test projects.
  - **Production hardening (new open question):** `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` can open remote debugging in a Production build from outside the app. Decide in M1 whether the host clears or overrides it.
  - **C# 15:** `await` inside an `unsafe` type fails (CS8652). Mark only pointer-using members `unsafe`.
- **Rejected:** a remote debugging port. The port file goes stale after exit, one port is shared across apps on the same profile, a fixed port fails silently on a second app, and the port is unauthenticated.

## 2026-11-13, Wire serialization: generated code, confirmed

- **Decision (user).** The wire protocol uses the generator's own `Utf8JsonReader`/`Utf8JsonWriter` code, not System.Text.Json metadata. This settles the open item from the D1 entry and overrides the earlier v7 attempt's "source-generated System.Text.Json metadata".
- **Why.** Both options are NativeAOT-safe; only reflection-based System.Text.Json is not. The choice rests on D1's evidence:
  - a 28% smaller executable;
  - a pure source generator, so IDEs need no build first;
  - exact wire rules (nullable means optional, required properties are checked).
- **Cost accepted.** Spar owns JSON edge cases: escaping, number handling, future types. These need dedicated generator tests in M1.
- **Scope.** The loopback host's own small error bodies may keep using a System.Text.Json context; they are development-only.

## D3: Variant builds without nested processes

Pending.

## D6: Development loop on .NET 11

Pending.

## D8: Devtools automation transport

Pending: spike in `spikes/d8-automation/`.

## 2026-11-13, Client, caching, paths and launch profiles (user decisions, tested in `spikes/react/`)

- **TypeScript-only generated client.** Each `@spar/*` package is one `index.ts` with `package.json` exporting it. The app's own `tsc --noEmit` and bundler compile it. Plain JavaScript output is dropped: few plain-JS consumers are expected. Strict type-check, bundle, tests (3/3) and the development session all passed.
- **The generated client is not embedded.** Only the bundled `dist` output goes into the `Spar.Frontend` resource. Earlier spikes embedded the raw client files; the framework must not.
- **No caching of served assets.** Every asset is served with `Cache-Control: no-store`. A reload served 0 requests from the browser cache (CDP `Network.requestServedFromCache`).
- **No absolute paths in tests and automation.** The test project stores the app path relative to its output folder. The development session file resolves against the project directory. The `node_modules/@spar` junction carries a marker so a moved clone repairs it.
- **Launch profiles.** `dotnet run -lp Desktop` and `dotnet run -lp Loopback` choose the host directly. Which profile is the default stays open.

## 2026-11-13, In-process test factory (evidence only, adoption open)

Spike: `spikes/react/React.Tests/InProcessApp.cs`. The app's real entry point runs in the test process. An `AsyncLocal` hook, compiled only into the Debug loopback build, applies service overrides before the container is built and hands back the address, token and `IServiceProvider`.

- **Start time:** about 210 ms to launch the exe; in-process 124–134 ms for the first start in a test process, then **20–27 ms**.
- **A whole UI test** took 1.23–1.37 s in-process against 1.40–1.45 s launching the exe. The ~1 s browser launch dominates.
- **Works:** a fixed `TimeProvider` reached the rendered UI. Backend state could be read directly. Two concurrent apps in one process stayed isolated.
- **Limits:**
  - The app's console output mixed into the test output. Console, environment, current directory and statics are process-wide.
  - It runs the JIT Debug assembly, so AOT, trimming and process lifetime need the exe.
  - A desktop (WebView2) variant would need an STA thread and a message loop in the test process; not tested.
- **Leaning:** keep launching the exe as the default for journeys. Offer the factory for service fakes and backend inspection, with app logging routed to a per-app sink. Decide in the plan revision.

## 2026-11-13, Wire serialization: generated code, chosen by speed then size

Benchmark: `spikes/serializer-bench/` (BenchmarkDotNet 0.15.8, CoreCLR and NativeAOT jobs, plus a Stopwatch harness built with the repository's settings). The rule (user) was: pick the faster option, then the smaller. **A** is the generator's `Utf8JsonReader`/`Utf8JsonWriter` code; **B** is a System.Text.Json source-generated context. This confirms the previous entry with numbers.

- **Deserialize:** A takes 0.57–0.62× B's time on CoreCLR and 0.47–0.53× on NativeAOT, and allocates 26–48% less (816 B against 1,568 B for the small request).
- **Serialize, as the D1 spike shipped it:** A was 1.17–1.62× *slower*. `SparJson.Write` created a new buffer and writer on every call, allocating 3–13× more, with Gen2 collections at 2,000 records.
  - With both serializers on one reused writer: 1.01–1.04× (1.07–1.13 with shipping AOT settings), with identical allocations.
  - With a tuned emitter (names pre-encoded once, combined name-and-value writes, indexed loops): **0.83–0.90×** B, allocating only the output array. Output is byte-identical.
- **Size (minimal NativeAOT apps):** none 929,792 bytes; A 1,257,472; B 2,377,728. Serialization adds 327,680 bytes with A and 1,447,936 with B, so A's executable is 47% smaller.
- **Correctness:** A and B write byte-identical JSON for all three workloads. They differ on edge cases: B requires nullable properties to be present; B accepts PascalCase names, numbers as strings and `null` items in a string list, which A rejects.
- **M1 requirements for A:**
  - a reused or pooled writer;
  - the tuned emitter shape;
  - reject trailing content after the object;
  - throw `JsonException`, not `InvalidOperationException` or `FormatException`, for malformed input.
- **Not measured:** wide, deep, number-heavy or escape-heavy DTOs, enums, dictionaries, concurrency and per-thread pooling, cold start, and the cost of exceptions.

## 2026-11-13, Desktop host with the React app (evidence, choices open)

Spike: `spikes/react/React.Desktop`, results in `spikes/react/RESULTS.md`. A NativeAOT Win32 + WebView2 host on its own STA thread. `WebResourceRequested` serves `https://app.spar.local/`: the embedded bundle, plus the bridge at the loopback URL shape `/_spar/call/<contract>/<command>`, checked by `Origin` instead of a token.

- **Works unchanged:** the same React bundle and generated SDK ran in the desktop and loopback hosts. The SDK now has one `fetch` transport and sends a token only when it has one.
- **Startup (AOT, warm profile):** the UI is ready 339–396 ms after host start, plus 45–54 ms from process start. On a fresh profile, navigation took 1.58 s once.
- **Test level:** a journey through in-process CDP (insert text, click, wait for the list item) took 9–19 ms.
- **No caching:** with `no-store`, every reload re-requested the bundle; a `max-age` control was served from cache. After an app restart nothing was reused even with `max-age`.
- **Compression:** WebView2 does **not** decode `Content-Encoding: br` or `gzip` on `WebResourceRequested` responses, so the host must decompress.
  - The zip reader costs 493,056 bytes.
  - A Brotli decoder costs 173,056 bytes.
  - The choice of frontend packing format is open.
- **Bridge transport:** `postMessage` beats `fetch` interception.
  - Small calls: 0.1–0.2 ms against 0.8–1.0 ms (median).
  - 64 KB: 1.2 ms against 3.0 ms.
  - 1 MB: 15.8 ms against 21.5 ms.

  The choice is open: one URL-shaped transport for both hosts, or the faster native channel.
- **Sizes:**
  - The production desktop exe is 3,676,160 bytes.
  - The host costs 1,639,424 bytes without DI, of which 493,056 is the zip reader.
  - Microsoft DI costs 300,032 bytes next to the host and 875,008 without it. This weighs on the plan's "MS DI scopes per command"; open.
- **Shutdown:** browser exit usually takes 236–284 ms after close, but 2 of 14 runs took about 6.46 s. Tools must wait for browser exit before reusing or deleting a profile.
- **Boundary scan:** generic names give false positives (`LoopbackHost` is a `System.Uri` flag). Production-boundary tests need Spar-unique markers.

## 2026-11-13, Development loop: app-owned dev server (evidence)

Spike: `spikes/react` (`DevServer.cs`, `vite.config.ts`, `React.targets`, `dev-loop-owned.mjs`).

- **One command:** `dotnet watch -lp Loopback --property:SparDevServerBuild=true`.
  - The backend starts Vite in 0.52 s, or reuses it after a restart (same pid, 67–81 ms).
  - The Vite proxy adds the backend token and accepts only its own origin, so the page URL carries no token.
- **Measured:**
  - Fast Refresh: 81–83 ms, with page state kept.
  - Hot Reload of a method body: 3.7–3.9 s.
  - Contract restart: 5.4 s with the development build, 6.4 s with the full build.
  - The already open page kept saving (HTTP 200) after the restart, with no reload.
- **`dotnet watch` (SDK 11.0.100) does not mark its builds:** no property or environment variable is visible to MSBuild. The development build must be selected explicitly. This supports D2's explicit `SparVariant` and a `spar dev` command or template that supplies it.
- **Process lifetime:**
  - Vite ended 1.4 s after `dotnet watch` was stopped; the idle-exit watchdog (60 s) is the fallback.
  - Stopping `dotnet watch` through a shell wrapper (`shell: true` plus `taskkill /T`) left `dotnet watch`, `dotnet run` and the app running. Agent tooling must start and own the real process.

## 2026-11-13, Package-only consumer (evidence)

Spike: `spikes/package-consumer/` (background agent; results in its `RESULTS.md`).

- **It works from packages alone.** An app created by `dotnet new spar-app` in a temporary folder with no repository above it:
  - restores, builds and runs;
  - reports contract errors at file and line;
  - builds at design time;
  - publishes NativeAOT with no warnings.
- **Update while warm:** moving to 0.1.1 with the MSBuild server warm loaded the new task and generator side by side, with no lock errors. `TaskHostFactory` is **not** needed for package consumers; it stays for Spar's own source checkout only.
- **M1 requirements:**
  - one `tools/` folder, with the generator added as an `Analyzer` item from `Sdk.targets`: NuGet's `analyzers/` folder is ignored for MSBuild SDK packages;
  - the runtime package referenced implicitly at the SDK's version;
  - the SDK version as an input of the client step;
  - **never reuse a version number**: deleting a cached version while the server was warm broke the next evaluation;
  - a package-consumer test from day one.
- **Numbers:**
  - `Spike.Sdk` package: 3.7 MB, 99% of it Roslyn.
  - Cold restore: 55–62 s, almost all of it .NET runtime and ILCompiler packs; 36 s with `DisableTransitiveFrameworkReferenceDownloads=true`.
  - No-op build: 0.5–0.9 s.
  - AOT publish: 10.4–10.9 s.
- **Side effect:** isolating `NUGET_PACKAGES` does not isolate NuGet's HTTP cache. The agent's manual restores wrote to the user's `%LOCALAPPDATA%\NuGet\v3-cache`; isolation tests must also set `NUGET_HTTP_CACHE_PATH`.

## 2026-11-13, Test framework: plain xunit.v3 confirmed (evidence)

Spike: `spikes/test-frameworks/` (background agent). The same 207-test suite, including 2 Playwright journeys, was written for `xunit.v3` 4.0.3, TUnit 1.84.0 and `xunit.v3.aot` 4.0.3.

- **Spar-sized suites:** all three run within about 5% (warm `dotnet test` 3.97–4.05 s); the browser journeys dominate.
- **2,205 tests on JIT:**
  - Runs: xUnit 1.3 s against TUnit 3.2 s.
  - Builds: 1.2 s against 2.5 s.
- **NativeAOT:**
  - Plain `xunit.v3` cannot publish AOT (97 errors). `xunit.v3.aot` publishes with no source changes (16.2 MB exe), and so does TUnit (21.6 MB plus 11.1 MB of coverage files).
  - Playwright fails in an AOT test exe under either framework, so browser journeys stay on CoreCLR.
- **Defaults:**
  - xUnit runs a class serially and writes no report files.
  - TUnit runs everything in parallel, so app- and browser-launching tests need `[NotInParallel]`. It also writes HTML and JSON reports into `TestResults/` of the working directory on every run, against the outputs rule.
- **Choice:** plain `xunit.v3` (the user's choice) is confirmed. `xunit.v3.aot` is the path for a future AOT test project without browser journeys. Note: `xunit.v3` 4.0.3 only wraps `xunit.v3.mtp-v2`, so the root `Directory.Packages.props` entry for `xunit.v3.mtp-v2` is redundant.
- **Caveat:** `AppPathMetadata.targets` stores the app path relative to the build output, which breaks in a publish folder at another depth.

## 2026-11-13, Serializer differential fuzzing (evidence, requirements for M1)

Spike: `spikes/serializer-fuzz/` (background agent). The generated serializers were fuzzed against System.Text.Json and a strict reference: 1,320,000 distinct cases over 3 seeds, on JIT and NativeAOT.

- **No crashes or hangs.** Every valid document was read back exactly by every reader.
- **Generator requirements (each fixed and verified in the spike's copy):**
  - **R0:** emitted code compiles for every shape. The tuned writer failed on nullable list elements (CS8629, CS8604).
  - **R1:** reject trailing content after the top-level value (13,074 cases).
  - **R2:** every malformed-input failure is a `JsonException` (31,418 cases threw other types). Emit typed read helpers with `TryGet*`, wrap `GetString`, and include the byte offset.
  - **R3:** use one depth limit, 64, for reading and writing. The writer allowed depth 1000, producing output no reader accepts (751 cases).
  - **R4:** reject number literals that overflow to infinity (427 cases), stricter than System.Text.Json.
- **Open contract decisions:**
  - `long` above 2^53 loses precision in JavaScript `JSON.parse` (62,722 of 111,983 documents): restrict the range, send as a string, or use `bigint`.
  - Lone surrogates: keep the silent U+FFFD replacement, or throw.
  - Keep rejecting top-level `null` and `null` list items.
  - Map writer exceptions to one serialization error.
  - Duplicate properties: last wins, or reject.

## 2026-11-13, Generated client packages, clean clones and the contract snapshot (evidence)

Spike: `spikes/react` (`React.targets`, `package.json`) and `spikes/d1-pipeline` (`Spike.targets`, `ClientBuilder.cs`).

- **Junction rejected.** npm treats a private `node_modules/@spar` junction as extraneous. `npm i` deleted the generated files **through** it, and `npm ci` removes it too. The next build then failed with TS2307, because the client step's stamp was still up to date.
- **Adopted for the spike: `file:` dependencies.**
  - `package.json` lists `@spar/sdk` and `@spar/<contract>` as `file:../obj/spar/client/packages/@spar/<name>`, and npm records them as links.
  - The generated files survived `npm i` (2.4 s) and `npm ci` (3.6 s).
  - Adding a contract package means adding a dependency line; an alternative to evaluate in M1 is one aggregate package with subpath exports.
- **The generated client path must not depend on the configuration.** With a Debug path, a clean clone running only `dotnet publish -c Release` failed with TS2307. With `obj/spar/client/` it succeeded in 18.1 s (warm caches), and the exe was identical in size (3,676,160 bytes).
- **npm:** the build keeps `npm ci`, run only when the lockfile changed. With the lockfile in sync, `npm i` (2.3–2.9 s) did not modify it.
- **Contract snapshot** (`<Project>.contracts.snapshot`, committed next to the project, LF via `.gitattributes`):
  - The build copies the generated snapshot there when it differs (`Update`, the local default), or fails with `SPAR100` (`Verify`, the default for `ContinuousIntegrationBuild`).
  - A DTO field addition showed up as a one-line diff, and reverting restored the file byte for byte.
  - The snapshot is the one build-written file in an authored folder, an exception to the outputs rule that **needs the user's decision**.
- **Defect for M1:** the contract fingerprint hashes only the manifest's type names. Adding a DTO field left it unchanged, so an old page would not get `409`. The fingerprint must cover full wire shapes.

## 2026-11-13, dotnet watch has no usable build hook (evidence)

A probe ran `dotnet watch -lp Loopback` through a contract-changing restart (SDK 11.0.100).
- **No marker property:** the builds dotnet watch starts see no `DotNetWatchBuild`, `DOTNET_WATCH` or `_DotNetWatch*` property.
- **The builds run in reused MSBuild worker nodes** (`MSBuild.exe /nodemode:1 /nodeReuse:true`), not in a child of dotnet watch, so process-based detection is impossible.
- **The only hook is `$(CustomCollectWatchItems)`.** dotnet watch's file-list target calls it, but in dotnet watch's own in-process evaluation (`DesignTimeBuild=true`), and **after** each build: 1.2 s and 0.4 s after it in the two runs. It cannot influence the build it follows, and a marker file written there would go stale when the session ends.
- **Consequence:** the development variant must be chosen explicitly. Options are `--property:` on the `dotnet watch` command line (verified), an environment variable in the shell (MSBuild passes the caller's environment to its nodes; not tested here), or a Spar command (`spar dev`) that runs dotnet watch with the property and owns its processes.

## 2026-11-13, Debug builds served by the dev server, desktop included (evidence; correction)

Spike: `spikes/react` (`React.App.csproj`, `Program.cs`, `DevServer.cs`, `React.Desktop/DesktopHost.cs`, `dev-loop-desktop.mjs`).

- **Debug embeds no production bundle** (`SparDevServerBuild` defaults to true in Debug). At runtime the app starts or reuses Vite.
  - The desktop host navigates WebView2 to the Vite URL and filters `WebResourceRequested` on `<vite origin>/_spar/*`, so the bridge stays in-process.
  - Release and the test build (`AdditionalProperties="SparDevServerBuild=false"`) embed the bundle; the tests passed (6/6).
- **Measured:**
  - Desktop Debug UI ready in 516–661 ms; journey 16–21 ms.
  - Under `dotnet watch -lp Desktop`: Fast Refresh **inside WebView2** in 39 ms; a contract edit to a relaunched app ready in 6.1 s, with Vite reused (67 ms).
- **No build-time detection of `dotnet watch` is needed any more.** The open question moves to whether Debug may depend on Node at runtime.
- **Requires per-variant outputs.** The test and Debug builds share `obj/` and `bin/`; each switch recompiled the app (3.1–3.3 s), and `bin/Debug` holds whichever variant was built last.
- **Correction to the "App-owned dev server" entry:** Vite did not stop with the session because of a job object.
  - Vite closes when its standard input ends (`process.stdin.on("end")`, unless `CI=true`), and it inherits the launcher's input through `cmd start /b`.
  - Under `dotnet watch` it lives for the watch session; under a pipe it ends with the app; in a terminal, the 60 s watchdog is the fallback.

## 2026-11-13, D3: variant builds run in-proc (evidence, recommendation)

Spike: `spikes/d3-variants/` (background agent; results in its `RESULTS.md`). A test project builds the app's DesktopTest variant either through the in-proc `MSBuild` task (a separate `Restore` call, then `Build` or `Publish`) or through `Exec dotnet`.

- **Works in-proc on SDK 11.0.100,** including NativeAOT publish. Nothing forced a nested process.
  - NETSDK1004 and NETSDK1047 (missing restore, or a missing RID) are fixed by the in-proc `Restore` call.
  - NETSDK1185 never appeared.
- **Faster than `Exec`.** Medians of 5 with a warm server, time added to the test build:

  | Operation | In-proc | `Exec` |
  |---|---:|---:|
  | Build, cold | +0.24 s | +2.21 s |
  | Build, warm | +0.10 s | +1.68 s |
  | AOT publish, cold | +5.60 s | +7.81 s |
  | AOT publish, warm | +0.56 s | +2.30 s |

  `Exec` costs a fixed 1.6–2.2 s per call: a new process, plus C++ toolchain discovery.
- **Requirements:**
  - per-variant `obj/<variant>/` and `bin/<variant>/`, set before the SDK props;
  - `DefaultItemExcludes` covering every `bin/**` and `obj/**`. Without it the Production build compiled the other variant's generated AssemblyInfo (CS0579);
  - `RemoveProperties` plus explicit `Configuration` and `_IsPublishing`, because the `MSBuild` task forwards the caller's global properties;
  - a deterministic `MSBuildRestoreSessionId`, so concurrent restores of the same variant are shared, not raced;
  - a design-time guard.
- **Concurrency:**
  - Inside one solution build, in-proc calls are deduplicated (0 failures in 20); `Exec` failed 9 of 10.
  - Two separate processes building the same variant failed 6 of 10 in-proc and 9 of 10 with `Exec`.
  - A `FileShare.None` lock file in the variant's `obj`, held for the build through `RegisterTaskObject`, fixed every run (20 of 20). It belongs in Spar's compiled task assembly: the plan's "lease".
- **Recommendation:** in-proc `MSBuild` for restore, variant build and variant AOT publish; no `Exec`. This also resolves the collision seen in `spikes/react`, where the Debug and test builds shared one `obj/` and `bin/`.
- **Not tested:**
  - Visual Studio's MSBuild and real IDE design-time builds;
  - apps with project references or packages that ship build props;
  - `-graph`;
  - the lock as a compiled task;
  - how "publishing a non-Production variant is an error" (D2) applies to test variants.
- **Side effect:** a win-x86 probe downloaded win-x86 runtime and ILCompiler packages into the user's global NuGet folder.

## 2026-11-13, Microsoft.Data.SqlClient is not NativeAOT-clean (evidence)

Spike: `spikes/sqlclient-aot/` (background agent; results in its `RESULTS.md`).

- **Strict publish fails** for every version tried:
  - 7.1.2, the latest stable: 49 ILC diagnostics.
  - 7.0.4: 49. 6.1.8: 43.
  - 8.0.0-preview1: 27, still failing.
  - A minimal app (open plus `SELECT 1`) still gets 43.
- **Causes:** `DiagnosticSource.Write<T>`, reflection-based System.Text.Json, providers and retry logic loaded by name or from app.config, SQL CLR user-defined types, `BuildSchemaTable`, and System.Configuration. The package has no trim annotations, so only ILC reports these.
- **With the warnings allowed, it works in AOT.** Every ADO.NET check passed against SQL Server 2022: types, NULLs, transactions, async cancellation, timeouts and `SqlException` numbers, with output identical to JIT.
- **Hard requirement:** `InvariantGlobalization=false`; with it on, `Open` throws `NotSupportedException`, even under JIT.
- **Cost:**
  - The exe grows from 1.69 MB to 16.41 MB, plus a 0.56 MB native SNI DLL.
  - With SqlClient's managed networking it is a single 16.23 MB exe.
  - The first query comes 92 ms after process start, against 312 ms under JIT.
- **Consequence:** Spar must not depend on SqlClient. An app that wants SQL Server under AOT needs an agreed suppression and a test of the paths it uses, or JIT (CoreCLR) as a publishing decision, which AGENTS.md allows. System.Data.SqlClient is deprecated and fails earlier.

## 2026-11-13, Optional OpenTelemetry layer (evidence; leaning)

Spike: `spikes/react` (`SparDiagnostics.cs`, `React.Telemetry`). The user asked for OpenTelemetry as an optional integration layer, never a requirement.

- **Always on:**
  - One BCL `Activity` and one histogram sample per bridge command: +105 KB in the AOT exe.
  - No listener means no work.
- **Optional layer:**
  - The OpenTelemetry SDK plus the OTLP exporter, for traces, metrics and logs.
  - Active only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.
  - AOT-clean, with no warnings; the export was verified from the AOT exe against a local OTLP/HTTP receiver.
- **Size:**
  - +6.5 MB for an app without HTTPS.
  - **+4.3 MB for an app that already uses `HttpClient` over TLS** (which costs 2.2 MB by itself). Of that: traces 2.5 MB, metrics +0.5 MB, logs +1.3 MB (through `Microsoft.Extensions.Logging`).
- **Runtime:** no measurable change in call latency (0.8–0.9 ms) or startup. About +18 MB working set when exporting.
- **Leaning:**
  - Keep the BCL instrumentation in the core.
  - Ship the exporter as a separate opt-in package.
  - Never configure an endpoint by default: desktop apps run on users' machines.
  - A Spar-owned minimal OTLP exporter is an option to evaluate if 2.5–4.3 MB is too much; untested.

## 2026-11-13, Aspire as an optional integration-test and development layer (evidence)

Spike: `spikes/aspire/` (Aspire 13.6.5, the latest).

- **Works end to end.** The AppHost runs:
  - a SQL Server container (the local `2022-latest` image, never pulled);
  - the Spar React app in loopback mode, with port, token and connection string from Aspire;
  - Vite through `AddViteApp`.

  A `DistributedApplicationTestingBuilder` test saved a note through the real UI with Playwright and read the row back from SQL Server. It passed in 3 of 3 runs: 25–31 s inside the test (about 20 s is SQL Server starting), 42–48 s for the whole `dotnet test`.
- **Spar changes it needed:**
  - Loopback port, token and dev-server URL assignable from the environment.
  - A store chosen from `ConnectionStrings__notes`.
  - **The host-name trap:** Aspire names endpoints `localhost`, which Node resolves to `::1`, while the loopback host listens on 127.0.0.1 and checks its origin. The loopback host must listen on both addresses or advertise its real one.
- **Audit:** Aspire 13.1.2 (cached) is flagged by NuGet audit (high, GHSA-f7vh-h5g3-7jrh). The user chose 13.6.5 (an ~87 MB download), which is clean. SqlClient 7.1.2 is clean, and 7.1.0 is not, through Pkcs 9.0.18.
- **Leaning:** an optional layer for integration tests with real resources (a fifth test level) and for the development loop. Spar never depends on it at runtime.
- **Not tested:**
  - the dashboard and `aspire agent` with a live app;
  - the desktop host or the AOT exe as resources;
  - persistent containers;
  - CI.

## 2026-11-13, App authors choose the runtime and their own AOT warning policy (user decision)

- **Decision (user).** NativeAOT stays the default for Spar apps, but the app author can opt out per app, for example when a library is not AOT-clean (SqlClient) or a setting is needed:
  1. **Publish on CoreCLR** (`PublishAot=false`). Spar must work and be tested in that mode too.
  2. **Keep NativeAOT and accept specific trim/AOT warnings** in their own app: an explicit, per-app choice, so the app's build is not blocked. The author then owns testing the affected code paths.
- **Separate from the runtime choice:** `InvariantGlobalization` is an independent app setting.
  - Spar's default is `true`.
  - An app may set `false` for SqlClient or culture-aware backend code: +225 KB, no extra files on Windows, no measurable startup change.
- **Scope.** The repository rule in AGENTS.md is unchanged for Spar itself: Spar's own code stays warning-free and fails on AOT and trim warnings. These options apply only to consumer apps, as publishing decisions.
- **Implications for M1:**
  - The package-consumer tests need a CoreCLR app and an AOT app that accepts warnings.
  - The desktop host must ship `WebView2Loader.dll` when not AOT, because the static loader link exists only under NativeAOT. Today `Desktop.targets` copies the DLL for `dotnet run` but not for publish.
  - Spar's own warnings must stay distinguishable from the app's.

## 2026-11-13, CoreCLR as the app's escape hatch (evidence)

Spike: `spikes/react` with `-p:SparSql=true` (SqlClient 7.1.2, `InvariantGlobalization=false`), offscreen self-test against a local SQL Server 2022, 3 runs per variant. All 12 runs saved their note.

| Variant | Size | Process start → host | UI ready | First journey with SQL | Working set |
|---|---:|---:|---:|---:|---:|
| NativeAOT, warnings accepted | 18.6 MB (exe + SNI DLL) | 36–64 ms | 261–370 ms | 25–52 ms | 37 MB |
| CoreCLR, framework-dependent | 7.7 MB in 41 files (needs .NET 11 installed) | 72–77 ms | 282–318 ms | 133–143 ms | 67 MB |
| CoreCLR, self-contained single file | 86.9 MB | 70–74 ms | 280–304 ms | 136–139 ms | 69 MB |
| … + ReadyToRun | 106.3 MB | 65–70 ms | 298–357 ms | 57–68 ms | 70 MB |

- **Every variant works,** including the desktop host on CoreCLR. `Desktop.targets` now links the WebView2 loader statically only under NativeAOT and otherwise publishes `WebView2Loader.dll`; in a single-file bundle it still loads.
- **The JIT shows in first-use backend work and memory, not in UI readiness.** WebView2 dominates that.
- **For an app that needs SqlClient**, AOT with warnings accepted is still the smallest and fastest, provided the author tests the paths used. CoreCLR framework-dependent is smallest on disk but needs the runtime installed. Self-contained CoreCLR costs 87–106 MB.
- **Gotcha:** `-p:PublishAot=true` on the command line fails with NETSDK1207, because the global property flows to the netstandard2.0 generator. Spar's variant builds must not pass it globally; this fits D3's `RemoveProperties` rule.

## 2026-11-13, .esproj for the frontend: not for build integration (evidence)

Spike: `spikes/esproj/` (background agent). Tested `Microsoft.VisualStudio.JavaScript.Sdk` 1.0.7204101 (a 180 KB download) against a copy of the `React.targets` approach, both on the same frontend.

- **Works from the command line** on SDK 11.0.100 (`dotnet build`, `publish`, `run`, `watch`) and from Visual Studio's MSBuild.exe, with no warnings.
- **Every Spar build need had to be worked around:**
  - **Not incremental:** the stock project re-runs `tsc` and `vite build` on every build (no-op 3.5–6.9 s). It becomes incremental only with `ShouldRunBuildScript=false` plus a custom target with inputs and outputs (no-op about 860 ms, against about 800 ms for `React.targets`).
  - **Build order breaks:** a plain `ProjectReference` from the app builds the frontend before the client is generated. After a contract change the bundle is one build behind, and a clean clone fails with TS2307. The reverse reference is circular (MSB4006). The only working shape is the app invoking the `.esproj` through the `MSBuild` task, after client generation.
  - **Solution builds skip it** by default (`dotnet build` and MSBuild.exe, for `.slnx` and `.sln`); forcing it runs Vite twice.
  - **npm handling:** it runs `npm install` rather than `npm ci`, does not reinstall when only the lockfile changes, touches `package.json` and the lockfile, and audits twice. `dotnet restore` installs nothing, and clean leaves the bundle behind.
- **Recommendation:** keep Spar's own targets. `.esproj` could only become an optional wrapper for the Visual Studio experience, and only if interactive IDE checks show a real benefit.
- **Interactive checks still needed:**
  - Visual Studio: loading the project, running npm when the solution opens, whether VS marks it for building, whether it rewrites the `.slnx`, F5 with two startup projects, fast-up-to-date skipping, IntelliSense for `@spar/*`.
  - VS Code with C# Dev Kit.

## 2026-11-13, SqlClient features for the SQL Server tool under NativeAOT (evidence)

Spike: `spikes/sqlclient-tool/` (background agent). SqlClient 7.1.2, with NativeAOT warnings accepted (the user-approved app-level choice) and compared with a real CoreCLR build, against two local SQL Server 2022 containers and 1M realistic rows (195.5 MB).

- **Everything the tool needs works under AOT,** with the same results as CoreCLR: bulk copy, cancellation, large reads, schema comparison through `sys.*`, DMV polling, and Windows integrated authentication.
  - Integrated authentication was checked read-only against the local MSSQLSERVER instance: NTLM over shared memory and named pipes. TCP is disabled there, so Kerberos was not testable.
  - `GetSchemaTable`, `GetColumnSchema`, `GetSchema` and `DataTable.Load` also worked for these column types.
- **Build:**
  - ILC reports 53 warning lines: 51 from SqlClient (one more on the `GetSchema` path) and 2 for the harness's own `DataTable.Load`.
  - The AOT exe is 17.1 MB plus the 0.56 MB SNI DLL.
- **Numbers (AOT against CoreCLR):**
  - Bulk copy of 1M rows: 14.1 s against 11.7 s median. AOT used half the CPU and memory (43 MB against 75 MB peak).
  - Reading 1M rows with `SequentialAccess`: 1.30 s against 1.42 s.
  - Schema comparison of 5,730 objects: about 100 ms.
  - DMV poll: about 9.5 ms, allocating 3.4 MB because of 1,986 performance-counter rows, so the tool should filter the counters it reads.
- **SqlClient behaviours the tool must design for, under any runtime:**
  - **A bulk copy without a transaction keeps every row already sent when cancelled,** including part of the batch in flight. Only `UseInternalTransaction` (complete batches only) or an external transaction with rollback gives a defined outcome. This matches Spar's rules: cancellation does not prove work stopped cleanly, and never replay.
  - **Cancelling a bulk copy does not stop its source query:** the tool must call `SqlCommand.Cancel()` on the source.
  - **Command cancellation surfaces as `SqlException` Number 0,** not `OperationCanceledException`. It takes 1–15 ms, and the server request is gone in 2–18 ms.
  - **`ReadAsync(token)` cancels only on the client.** Disposing a reader reads the rest of the result (about 1 s for 999k rows) unless `SqlCommand.Cancel()` comes first (19 ms).
  - **Managed SNI** (single-file exe) is 18–24% slower and loses shared memory: integrated authentication on a local instance then needs `np:`.
- **Not exercised:**
  - the paths behind the remaining warnings: CLR UDTs, app.config providers and retry logic, DiagnosticListener, vector and json types, Always Encrypted;
  - Kerberos, MARS, a real network.

## 2026-11-13, CsWinRT 3.0 preview: not now (evidence)

Spike: `spikes/cswinrt/` (background agent), CsWinRT `3.0.0-preview.261104.1` with SDK.NET.Ref `10.0.26100.88-preview` on SDK 11.0.100. The user asked whether it makes Spar easier.

- **It works in an unpackaged NativeAOT Win32 app, with zero CS, IL or ILC warnings.** The one warning is NETSDK1229 (preview) on every build.
- **WebView2 through a projection.**
  - The WebView2 package's own projection is CsWinRT 2.x, so the spike generated a 3.0 projection from `Microsoft.Web.WebView2.Core.winmd` in a separate library.
  - Behaviour matched the hand-written COM: `WebResourceRequested` with a deferral and a stream, `WebMessageReceived`, CDP, and navigation in 262 ms against 271 ms. The API is much nicer (events, `await`, properties).
  - **The cost is the exe size:**

    | | Hand-written COM | CsWinRT 3.0 |
    |---|---:|---:|
    | Exe | 2.54 MB, single file | 25.3 MB + a 0.86 MB DLL beside it (22.4 MB with every feature switch off) |
    | AOT publish | 5.1 s | 74.9 s |
    | Debug clean / no-op build | 0.8 / 0.4 s | 11.9 / 2.5 s |

  - The cost is mostly fixed: metadata 3.0 MB, collection marshallers for the whole Windows SDK 2.36 MB, vtables 1.66 MB.
- **Windows APIs without package identity:**
  - `PasswordVault` works (12.65 MB exe).
  - `FileOpenPicker` works through `IInitializeWithWindow` (18.4 MB exe). The dialog runs in `PickerHost.exe`, out of process.
  - Toasts need package identity or a registered app ID.
- **Requirements:**
  - a `.1` TFM revision;
  - a mandatory preview SDK.NET.Ref pin (CS0234 without it);
  - the 141 MB CsWinRT package, even for SDK-only use (MSB6011 without it);
  - breaking changes from 2.x, and no `InitializeWithWindow` or `WindowNative` helpers.
- **Decision (recommended):** keep the hand-written `[GeneratedComInterface]` WebView2 binding, and use CsWin32 or COM declarations for individual Windows APIs. Re-evaluate when CsWinRT 3.0 is stable and its size work has landed.
- **Also observed:** browser exit took about 6.45 s after close in every run of both bindings, so the slow shutdown is not caused by the binding.

## 2026-11-13, Native file dialogs, file grants and credential storage (evidence)

Spike: `spikes/native-services/` (background agent). NativeAOT, warnings as errors, no suppressions. 13 AOT publishes with zero warnings; 40 of 40 checks passed.

- **File dialogs:**
  - **Bindings that work:** `IFileOpenDialog`/`IFileSaveDialog` work hand-declared with `[GeneratedComInterface]`, as CsWin32 structs (`allowMarshaling:false`; manual reference counting, no easy event sink), and through CsWin32's COM source generators (`CsWin32RunAsBuildTask` with `useComSourceGenerators`, which needs `allowMarshaling:true`).
  - **Behaviour:** dialogs belong on the window's STA thread. Cancel returns `0x800704C7`. `IFileDialog::Close(hr)` aborts a dialog, for example when the window closes.
  - **Timings:** opening to the window ready takes 89–216 ms.
  - **Automation:** fully automatable in-process (event-driven script plus watchdog, with a hook keeping dialogs transparent). No window was left on screen in the final runs.
- **Hazards of hand-written COM:**
  - **A missing vtable slot** shifted later methods and crashed. Spar needs a slot-order test.
  - **An AOT-only runtime cast failure** when an interface is declared but none of its methods is reachable: it works under JIT and gives no warning.
- **File grants (M3 design check):**
  - The page receives only `{handle, name}`; the handle is `fg_` plus 128 random bits, mapped to path, rights and expiry.
  - Contract: `pickOpen`, `pickSave`, `readText`, `writeText`, `release`. Cancel returns `null`. Lookups take under 1 µs.
  - Grants are in memory only, so after a restart the page picks again, and recent files reopen through a prefilled dialog.
  - Not built: file-identity pinning, and preserving permissions on an atomic write.
- **Credentials:**
  - **Credential Manager:** generic type, session or machine persistence. The blob limit is 2,560 bytes (2,561 fails with Win32 error 1783). Writes take 4–11 ms at machine persistence, reads about 0.1 ms.
  - **DPAPI:** direct imports and `ProtectedData` interoperate, at about 0.1–0.27 ms. The description is stored in clear text.
  - **Isolation:** neither store isolates apps run by the same user.
  - **Recommendation for the SQL tool:** Credential Manager for passwords (target `<AppId>/<connection-id>`, login in `UserName`, only the password in the blob), and DPAPI for larger blobs. Any page-facing secrets API should be write-only (`set`, `has`, `delete`).
- **Sizes:**
  - The first `[GeneratedComInterface]` use costs about 907 KB, which the WebView2 host already pays.
  - Dialogs add 138–197 KB.
  - Credential Manager plus DPAPI add about 73 KB.

  This compares with 10–23 MB for the CsWinRT 3.0 equivalents.
- **Open choice:** accept CsWin32's COM generators (`allowMarshaling:true`, possibly in a separate interop assembly), or keep hand-declared interfaces plus a slot-order test.
- **Cleanup verified:** no `SparSpike*` credentials, temp folders or processes remain.

## 2026-11-13, Bridge streaming, cancellation and push (evidence; transport leaning)

Spike: `spikes/react`, `spikes/loopback`, `spikes/d1-pipeline` (background agent); results in `spikes/react/RESULTS.md` under Streaming, Cancellation and Push. Re-checked afterwards: React tests 18 passed, 6 explicit benchmark tests skipped.

- **Built through the generator:**
  - Commands returning `IAsyncEnumerable<T>` become TypeScript `AsyncIterable<T>`.
  - `[SparSubscription(capacity)]` subscriptions become `AsyncIterable<SubscriptionEvent<T>>`.
  - One text message protocol, with call ids, credits and exactly one terminal message per call.
  - The SDK is now a real `.ts` file embedded in the tool.
- **Fixed in passing:** generated dispatch reported a handler's own `JsonException` or `InvalidOperationException` as "not executed", even after side effects. Request parse errors and unknown commands now use their own exception types.
- **WebView2 cannot stream a `WebResourceRequested` response.** `IStream::Read` runs on the UI thread, the whole response is read before the page sees it, `E_PENDING` ends it, and there is no back-pressure (backend +114–177 MB for 100k rows). Streams, subscriptions and cancellation on the desktop host therefore need `postMessage`.
- **100k rows, batches of 100–1000:**
  - Desktop channel: first row 0.5–2.8 ms, about 160 ms in total, about 620k rows/s.
  - Loopback channel: 578–850k rows/s.
  - Loopback per-call fetch: 306–345k rows/s.

  No long tasks. Batches of 1 are 20–130× slower, so batches should be sized by bytes or time. Holding 100k rows cost about 38.6 MB of page heap.
- **Cancellation.**
  - The SDK sends a cancel message and waits for the backend:
    - `CancelledError` only when the backend confirms the command ended on its token (for streams, after the iterator is disposed);
    - the real result if the command completed anyway;
    - `NotExecutedError` if it never started;
    - otherwise `OutcomeUnknownError`: `unconfirmed` after a 5 s timeout, or `lost` when the connection drops.
  - Confirmation in 0.2–1.1 ms. Work that ignored its token was always reported as unknown and really was still running.
  - Nothing is ever retried.
- **Push.**
  - A bounded drop-oldest buffer that never blocks the producer. The page sees `gap` events with the dropped count, and delivered plus dropped equalled produced.
  - 20,000 events/s were delivered completely: p99 0.84 ms on desktop, 1.0 ms on loopback, page main thread 2.8–5.3% busy.
  - After a loopback backend restart the subscription resumed in about 390 ms with a `reset` event.
- **Loopback lesson:** per-call fetch streams exhaust the browser's 6 HTTP/1.1 connections per host. With six subscriptions open, a unary call blocked for more than 3 s and the cancels could not even be sent. One streamed channel per document fixes it (5.7 ms), and it also makes overflow visible instead of hiding it in OS buffers.
- **Transport leaning:** one message protocol carried by each host's cheapest channel.
  - Desktop: `postMessage` for everything. Unary takes 0.2 ms median against 1.3 ms over fetch interception.
  - Loopback: `fetch` POST for unary calls (0.7 ms), plus one HTTP channel per document for streams, subscriptions and cancellation.

  This supersedes the "one fetch transport for both hosts" spike shape. Final decision at the plan revision.
- **Open for the user:** `keepSubscribed`, an explicit per-subscription opt-in that re-subscribes after a lost connection and yields `reset`. Is it acceptable under "never replay a command" for read-only subscriptions?
- **Production boundary:** the strict AOT publish has no warnings: 6,233,600 bytes (+232,448). The bench page is not in the production bundle, but the spike's `lab` contract is, as a spike shortcut like `echo`.

## 2026-11-13, Subscription resumption: chosen in three levels (user decision)

- **Decision (user).** Automatic re-subscription after a lost connection is allowed, but chosen explicitly, with a sensible default:
  1. **The contract author** sets `[SparSubscription(Resume = SubscriptionResume.Automatic | Manual)]`. The default is `Automatic`, because Spar subscriptions are observation and must not have side effects. Anything that does (draining a queue, creating server-side sessions) is marked `Manual`.
  2. **The page** may override per call, only toward caution: `{ resume: "manual" }`. For `Manual` contracts the generator types the option as `resume?: "manual"`.
  3. **The app** may lower the default for all subscriptions through SDK configuration, never raise it above a contract's value.
- **Behaviour:**
  - Backoff re-subscription when the bridge is back, with `reconnecting`, `reset` and `resumed` events.
  - Giving up after a limit with `OutcomeUnknownError` (`lost`).
  - One-shot streams are never resumed, and unary commands are never retried. This keeps "never replay a command": nothing runs again unless its author declared it safe.
- **Type-checking verified** with the project's TypeScript 7.0.2 (`tsc`, strict) on a stand-in for the generated signatures:
  - `{ resume: "manul" }` fails with TS2820, which suggests `"manual"`.
  - `{ resume: "automatic" }` on a `Manual` subscription fails with TS2322.

  The values are string-literal union types, so editors offer them as completions.
- **Recorded in `docs/plan.md`, M3.**

## 2026-11-13, Contract syntax (user decisions, with evidence)

Discussed after the spikes; prototypes in the session scratchpad were checked with SDK 11.0.100 (C# 15) and TypeScript 7.0.2.

- **Contract forms:** an attributed class (`[SparContract] sealed class`, one instance per call from the call's DI scope) **or** an attributed interface whose implementation is registered in DI. No Minimal-API style, for now at least.
- **Command shape (as in v6):** zero or one request parameter, then `CancellationToken`. The request may be a DTO record or a scalar (`GetAsync(string id)` becomes `get("n42")`). The return is `Task<T>`, `Task<Result<T, TError>>` or `IAsyncEnumerable<…>`. No objects synthesized from several parameters.
- **Names:**
  - Contracts are derived from the type name (`CopyJobsService` becomes `copyJobs`), with `[SparContract("…")]` as an override.
  - Commands are derived from the method name without `Async`, in camelCase, with `[SparCommand("…")]` as an override.
  - **A diagnostic is required when two contracts resolve to the same name,** including contracts from referenced assemblies, so the user must add an explicit name to at least one of them.
- **TypeScript scoping:** one package per contract, so same-named types in different contracts never clash. A C# type shared by contracts is emitted once and re-exported. Two different types with the same name inside one contract get a diagnostic asking for `[SparName("…")]`. M1 needs a clash test matrix that includes contracts, DTOs and errors defined in referenced assemblies.
- **Expected errors: `closed record` hierarchies** with nested sealed cases (C# 15, no preview needed), for example:

  ```csharp
  public closed record PlanError
  {
      public sealed record ConnectionNotFound(string ConnectionId) : PlanError;
      public sealed record TableMissing(string Connection, string Table) : PlanError;
  }
  ```

  - **Compared with `union`:** both give exhaustive `switch` checking; a missing case is CS8509, an error for us. The plain `abstract record` (v6) does not, and needs `_ =>`.
  - **Why `closed record` won:** `return new PlanError.ConnectionNotFound(…)` converts directly into `Result<T, PlanError>`. With a union this fails with CS0029, because C# does not chain two user-defined conversions, so it needs a cast. A union is also a struct.
  - **Cost:** a case cannot be shared between two error types; it is declared in each.
- **Results are opaque (user choice).** The value can only be read through an exhaustive `match` (success, every declared error, optional `failure`) or `unwrap` (opt-in exceptions).
  - Infrastructure failures (`CallError` with `execution: notStarted | completed | unknown`, which replaces the spike's `NotExecutedError`, `OutcomeUnknownError` and `CancelledError`) go to the call's `failure` handler, or else to an app-wide interceptor (`configureSpar({ onFailure })`).
  - The interceptor may observe and notify, never retry or turn a failure into a success.
  - Verified with `tsc` on a prototype of a generic `match`: mixed handler return types are inferred, and it rejects a missing error case, a misspelled case, a nonexistent payload field, a forgotten `errors` object and reading the value without checking.
  - **Not yet verified:** editor completions (IntelliSense), and the opaque variant itself. A spike follows.

## 2026-11-13, Opaque results in TypeScript: the `Call` form and editor support (evidence; leaning)

Spike: `spikes/ts-results/` (background agent). Re-checked afterwards with `tsc` 7.0.2: the documented usage compiles, and the mistakes file is rejected (missing case TS2741, misspelled case TS2561, wrong payload field TS2551, missing `errors` TS2345).

- **Forms compared:**
  - (A) a `match` method on the awaited result;
  - (B) a free `match(result, …)` over an opaque branded type;
  - (C) generated commands return `Call<T, E>`, a thenable with `match` and `unwrap`, so the whole call reads as one expression: `await copyJobs.plan(r).match({ … })`. Awaited results and stream or subscription items have the same `match` and `unwrap`.
- **Leaning: (C).**
  - Typing `.` offers `match` and `unwrap`, and it is opaque at run time too (`#private` fields).
  - Costs: about 170 B gzip more than (B), and about 20 ns more per call (negligible against 0.2 ms bridge calls).
  - Calling `.match` without `await` is not a compile error.
  - `Call` is a thenable, not a real `Promise`.
- **Handler typing that satisfies every requirement:**
  - `success` and `failure` each have their own type parameter, and `undefined` joins the result only when `failure` is omitted.
  - `errors` is one inferred object whose handler return types are unioned.
  - Extra keys are rejected through `NoInfer`.
  - Anchoring on the generated `ErrorHandlers<E>` gives go-to-definition, doc comments and rename.
  - A command without declared errors has no `errors` property: `errors: {}` is TS2353.
- **Generated errors should be an interface map** (case name to payload, with the C# XML docs as JSDoc), from which the discriminated union is derived. The plain union loses go-to-definition, doc comments, rename and the "add missing properties" fix.
- **Editors (driven headlessly):**
  - **Both language servers** (TypeScript 7.0.2 `tsc --lsp`, and VS Code 1.146's bundled TypeScript 6.0.3) complete exactly the remaining case names, show the payload on hover, and go to the generated declaration.
  - **Only 6.0.3** shows doc comments and offers "Add missing properties", which fills only the missing cases. When `errors` is missing entirely, that fix writes `errors: undefined`.
  - **VS Code does not use TypeScript 7 by default** (only with the Native Preview extension), so the editor shows 6.0.3 behaviour while the build uses 7.0.2.
  - **Rename and some quick fixes edit generated files.** Error cases must be renamed in C# and regenerated; consider marking the generated packages read-only.
- **Open design points:**
  - whether C# streams may declare errors;
  - an `events` handler slot for subscriptions instead of a `success` switch;
  - checking the `Call` form with `no-floating-promises`, React 19 `use()` and TanStack Query.
- **Needs a person in VS Code:** how the popups, lightbulb and rename preview look; the Native Preview extension; the effect of read-only generated packages.

## 2026-11-13, Contract syntax in the generator (evidence)

Spike: `spikes/contract-syntax/` (background agent). Re-checked afterwards: generator tests 62 of 62 passed.

- **Works end to end:**
  - class and interface contracts (dispatch produces byte-identical results);
  - zero or one request (a record or a scalar) and an optional `CancellationToken`;
  - `Task<T>`, `Task<Result<T, TError>>` and `IAsyncEnumerable<T>`;
  - derived names with overrides;
  - `closed record` errors on the wire as `{"ok":false,"error":{"code":"connectionNotFound",…}}`;
  - one package per contract, with types used by several contracts emitted once under `@spar/shared/`;
  - contracts, DTOs and errors from a referenced class library;
  - strict NativeAOT publish with 0 warnings (2,578,944 bytes);
  - generated TypeScript clean under strict `tsc`.
- **Diagnostics:**
  - 23 errors, SPAR001–SPAR040, each placed on the declaration to change, including the duplicate contract-name diagnostic.
  - A shared `DiagnosticAnalyzer` reports located diagnostics, and the generator reports only location-less ones (inside referenced assemblies), so nothing is reported twice.
  - Design-time probe: diagnostics update after an unsaved edit in about 92 ms.
- **Detecting `closed`:**
  - Roslyn 5.9 has `ITypeSymbol.IsClosed` and `GetClosedDerivedTypeInfo`, working for source and metadata, but they are `[Experimental("RSEXPERIMENTAL006")]`, so using them needs a suppression (**needs the user's agreement**).
  - Without it, the generator reads the `closed` modifier from syntax, and `IsClosedTypeAttribute` from metadata, which `GetAttributes()` hides.
  - Roslyn 5.9 hosts accept `closed` only at `LanguageVersion.Preview` (CS8652); SDK 11.0.100 (Roslyn 5.12) needs no preview. So the minimum supported IDE is the one with C# 15 support.
- **Open syntax points:**
  - whether `[SparCommand]` is required, or all public methods are commands (the spike made all public methods commands);
  - an optional `CancellationToken` (implemented);
  - plain `Task` becoming `Promise<void>` (recommended; currently SPAR010).
- **Other recommendations:**
  - a generated startup check for missing DI registrations of interface contracts;
  - an analyzer mode for contract libraries, whose own errors have no location in the app;
  - treating derived names as advisory, since the `I` prefix rule turns `IOStatsService` into `oStats`;
  - caching the derived-type map and metadata scans per compilation.
- **Not tested:**
  - interactive IDE squiggles;
  - large solutions;
  - subscriptions and the bridge protocol in this copy;
  - managed-client error deserialization.

## 2026-11-13, Contract syntax follow-ups (user decisions and recommendations)

- **Class contracts (user decision):** only methods marked `[SparCommand]` are commands, so public helpers stay private to C#.
- **Interface contracts (recommended, the user leans this way; to confirm):**
  - Every method declared on a `[SparContract]` interface is a command. `[SparCommand]` is optional there and used only for overrides such as the wire name.
  - When a class implements a contract interface, only the interface's members are commands. Its other public methods are not, and `[SparCommand]` on them is a diagnostic.
  - A type that is a `[SparContract]` class and also implements a `[SparContract]` interface is a diagnostic: choose one.
  - Properties, events, static members and default interface implementations on a contract interface are diagnostics.
- **`CancellationToken` stays optional (user decision).** The generated dispatch always creates a token and passes it when the method declares one.
  - A source generator cannot add a parameter to the user's method. A method without the parameter cannot observe cancellation, so a cancel of that command ends as `OutcomeUnknown` (unconfirmed), as already defined.
  - An ambient accessor (for example `SparCall.Current.CancellationToken`) is an option for code that wants the token without the parameter.
  - On the TypeScript side `signal` is always available.
- **Plain `Task` is allowed (recommended).** It becomes `Call<void, E>`: success arrives as `{"ok":true}`, and `match({ success: () => … })` works. There is no real downside; a write that returns nothing just gives the page nothing to reconcile with after an unknown outcome, so returning a version or id is good practice, not a rule. `ValueTask` and `ValueTask<T>` should be accepted too.
- **Analyzer override agreed:** `RSEXPERIMENTAL006 = none`, scoped to `src/Spar.Generator/**.cs`, recorded in `.editorconfig`.
  - It lets the generator use Roslyn's experimental closed-type API (`ITypeSymbol.IsClosed`, `GetClosedDerivedTypeInfo`), which works for source and metadata.
  - Verified with Microsoft.CodeAnalysis.CSharp 5.9.0: without the override the build fails with RSEXPERIMENTAL006; with it, `IsClosed` returns `true` for a `closed record`.

## 2026-11-13, Derived contract names: the rule (recommendation, checked)

The spike's rule turned the class `IOStatsService` into `oStats`. It stripped an `I` from a class, and its camelCase did not handle acronyms. The user keeps derived names but rejects that result.

- **Proposed rule:**
  1. Classes keep their name.
  2. Interfaces drop exactly one leading `I`, the .NET interface prefix (CA1715 convention).
  3. Both drop a trailing `Service`.
  4. Then acronym-aware camelCase, the same algorithm as `JsonNamingPolicy.CamelCase`.

  Commands drop `Async` and use the same camelCase.
- **Checked with SDK 11.0.100:**

  | Type | Derived name |
  |---|---|
  | `NotesService` | `notes` |
  | `IOStatsService` | `ioStats` |
  | `SQLServerMonitorService` | `sqlServerMonitor` |
  | `HTMLExportService` | `htmlExport` |
  | `CopyJobs` | `copyJobs` |
  | `Service` | `service` |
  | interface `INotes` | `notes` |
  | interface `IIOStats` | `ioStats` |
  | interface `ISQLServerMonitor` | `sqlServerMonitor` |
  | interface `IOrders` | `orders` |
  | interface `ICopyJobsService` | `copyJobs` |

- **Where names stay visible:**
  - The generated TypeScript package name and the contract snapshot show every derived name.
  - `[SparContract("…")]` and `[SparCommand("…")]` override them.
  - The duplicate-name diagnostic catches collisions.

## 2026-11-13, Contract rules approved (user decision)

The user approved two rules that were recorded above as recommendations.
- **Interface contracts:**
  - Every method on a `[SparContract]` interface is a command, and `[SparCommand]` is optional there (for overrides).
  - A class implementing a contract interface exposes only the interface's members.
  - Mixing the class and interface forms is a diagnostic, and so are non-method members on a contract interface.
- **Derived names:**
  - Classes keep their name, interfaces drop one leading `I`, and both drop a trailing `Service`.
  - Then acronym-aware camelCase, as in `JsonNamingPolicy.CamelCase`.
  - Commands drop `Async`.
  - Overrides are `[SparContract("…")]` and `[SparCommand("…")]`.

## 2026-11-13, Cancellation token delivery (user decision)

- **When the per-call token fires:**
  - the page aborts the call (`AbortSignal`, sent as a cancel message);
  - the page that started it goes away (reload, navigation, window closed);
  - the app shuts down;
  - later, an optional timeout.

  The page sees "cancelled" only if the command observes the token and ends with `OperationCanceledException`; otherwise the outcome is unknown.
- **Delivery:**
  - **A. An optional `CancellationToken` parameter** on the command: the documented default.
  - **B. A scoped `ISparCall` per-call context:** token, call id, contract and command names.
    - The generated dispatch fills it in before resolving the contract.
    - It is injectable into the contract (one instance per call) and into any scoped service of that call.
    - A singleton depending on it fails at startup, because the container uses `ValidateScopes = true`.
    - The call id also serves log and OpenTelemetry correlation.
- **Rejected for now: C, an ambient `SparCall.Current` through `AsyncLocal`.** It flows into fire-and-forget work, where it can carry a finished call's token. The dependency is invisible, and it is harder to fake in tests. B covers the same needs.
- **A suggestion-level diagnostic** (it does not block the build) flags streams, subscriptions and other long-running commands that receive the token in neither way, because cancelling them can only end as "outcome unknown".

## 2026-11-13, User project layout (user decisions; two points open)

- **Frontend location:** a `frontend/` folder inside the app project folder by default. A csproj property (`SparFrontendPath`, as in the spikes) moves it. The build uses the property to find the frontend.
- **Spar.Sdk:** apps declare `<Project Sdk="Spar.Sdk">`. It is an MSBuild project SDK, prototyped and validated in `spikes/package-consumer/`. It wraps Microsoft.NET.Sdk and brings the generator, tasks, targets, the runtime package and the frontend integration. **Its version is pinned in `global.json` (`msbuild-sdks`)**, one place for the app and its tests.
- **Vite integration:** a Spar Vite plugin (bridge proxy, token, origin check, dev-server state), shipped inside the NuGet SDK with the generated client, so nothing is published to npm and versions always match.
- **Templates:** React + TypeScript + Vite first; other frameworks as later templates.
- **Documentation:** a from-scratch story. Scaffold `frontend/` with a regular React or Vue scaffolder, then add the Spar dependency and plugin. The build should diagnose exactly what is missing.
- **Open:**
  - generating the client into `node_modules` (the user's question) against one fixed `file:` dependency with subpath exports per contract;
  - one test project against a fast suite plus a separately run end-to-end project. This touches the AGENTS.md consumer model, which names one app project and one test project.

## 2026-11-13, Linux desktop host with WebKitGTK: feasible (evidence)

Spike: `spikes/linux-desktop/` (background agent), run headlessly in Docker (Ubuntu 24.04, Xvfb, software rendering). The user is considering Linux as a second desktop OS.

- **It works on both WebKitGTK APIs,** 4.1 (GTK3) and 6.0 (GTK4). NativeAOT publishes with 0 warnings: a 6.9 MB exe that links only glibc, with GTK and WebKit loaded at first use.
  - The host uses `[LibraryImport]` and `[UnmanagedCallersOnly]`.
  - A GLib main loop runs on a dedicated thread, with a `SynchronizationContext` posting through `g_idle_add_full`.
  - A `spar://app/` scheme serves assets and `fetch`.
  - The bridge is a script message handler up, and batched `evaluate_javascript` down, carrying the existing protocol.
  - The React spike's built bundle and generated SDK ran unchanged, through a 12-line shim.
- **Numbers (Xvfb, software rendering):**
  - **UI ready:** GTK4 271–292 ms; GTK3 with the sandbox 201–244 ms. GTK3 without the sandbox took 686–711 ms (unexplained; check on a real desktop).
  - **Bridge:** small unary calls 0.20–0.29 ms on either transport. 100k-row streams in 71–151 ms. Cancellation confirmed in 0–4 ms. Push at 20,000 events/s with nothing lost.
  - **Memory (PSS):** 329–336 MB in total for GTK3, 413–426 MB for GTK4.
  - **Shutdown:** the WebKit processes are gone in 20–51 ms, provided the main loop keeps running until they exit.
- **Differences from WebView2:**
  - **Streaming:** custom-scheme responses stream, but with no back-pressure (a paused reader grew the web process by 826 MB), so streams stay on the credit channel.
  - **No origin information:** neither `fetch` to the scheme nor script messages carry it, so the origin check must be redesigned (navigation lock, top-frame-only shim, isolated world; foreign iframes untested).
  - **No in-process trusted input:** WebKitWebDriver (a TCP port; GTK3 only) or XTEST (X11 only); otherwise untrusted JavaScript input. D8's in-process CDP has no Linux counterpart.
  - **The GTK4 sandbox is always on:** it needs a non-root user, and user namespaces in containers.
  - **Hardening:** `WEBKIT_INSPECTOR_SERVER` opens a TCP port, so Production must clear it.
  - **Missing WebKit** surfaces as `DllNotFoundException`: check at startup with a clear error.
  - **GStreamer, Mesa and dconf** write to `$HOME/.cache`.
- **Consequences for the host abstraction:**
  - each host provides the SDK's channel object;
  - the transport is chosen per host from measurements;
  - streams use credits on every desktop host;
  - origin checks and automation are per-host features;
  - one WebKitGTK API to choose.
- **Not tested:** a visible desktop (WSLg), Wayland, GPU, HiDPI, IME, foreign-origin iframes, ARM64, other distributions.
- **Leftovers kept on the machine:** image `spar-linux-desktop:webkit2.52` (1.24 GB unique) and volume `spar-linux-desktop-nuget` (467 MB). About 187 MB of apt packages were downloaded.

## 2026-11-13, Client wiring, test projects, and the spar tool commands (user decisions)

- **Client wiring:**
  - One fixed `file:` dependency in `frontend/package.json`, `"@spar/app": "file:../obj/spar/client"`, with one subpath export per contract plus `sdk` and `vite`.
  - It is not generated into `node_modules`: npm prunes or wipes undeclared packages there (the spike lost the generated client exactly this way), Vite pre-bundles and caches `node_modules`, so regenerated code would miss hot updates, and the lockfile would not know the package.
  - A linked `file:` package outside `node_modules` survived `npm i` and `npm ci` and hot-updated an open page.
- **Two test projects:**
  - **`MyApp.Tests`:** fast, no browser, WebView2 or containers; run by every `dotnet test`.
  - **`MyApp.EndToEndTests`:** Playwright on loopback, the desktop exe, Aspire; run explicitly and in a separate CI stage.
  - The AGENTS.md consumer model now names one app project, one fast test project and one optional end-to-end project.
- **`spar doctor` and `spar init frontend`:** added to the plan (section 6, and M1 item 7 for the basics). The project checks are shared with the build's diagnostics; `--json` is for agents.
- **The `spar snap` description** in the plan is expanded: build or reuse the development build, run offscreen, wait for UI ready, replay an optional input script, then write a PNG, the accessibility tree and the call log, and exit.

## 2026-11-13, TypeScript SDK distribution (user decision)

- **The SDK (`src/Spar.Client`) ships compiled:** ES modules (`.js`), `.d.ts`, `.d.ts.map`, source maps and the original `.ts` sources.
  - Go-to-definition in the editor opens the TypeScript source, through the declaration maps.
  - The app's own `tsconfig` and TypeScript version never compile Spar's code. `skipLibCheck` covers `.d.ts` only, so with `.ts` sources the strictest app flags (`exactOptionalPropertyTypes`, `noUncheckedIndexedAccess`, `erasableSyntaxOnly`, …) would apply to the SDK.
  - Tools that do not transpile TypeScript dependencies can consume it.
  - The browser part (DOM `lib`) and the Vite plugin (Node types) are compiled with separate configurations.
- **Generated contract modules stay TypeScript.** They are small, in one environment, regenerated each build and kept clean under the strictest flags.
- **A CI matrix** compiles a sample app and the generated modules with permissive-to-strictest `tsconfig` settings and with TypeScript 6 and 7.
- **Tree shaking** is done by the app's bundler, not by `tsc`, and works on compiled ES modules as well as on TypeScript. What matters is ES module output, `"sideEffects": false`, no top-level side effects, and a modular API. Measured in `spikes/ts-results`: a free function that is never imported (`unwrap` in form B) was dropped, while methods on a class (`match`/`unwrap` on `Call`, form C) are kept together.

## 2026-11-13, The SQL Server tool's data layer on Linux (evidence)

Spike: `spikes/sqlclient-linux/` (background agent). The same SqlClient 7.1.2 workload as `spikes/sqlclient-tool`, run in a Linux container under NativeAOT (warnings accepted) and CoreCLR, against SQL Server 2022 containers.

- **Verdict: it works, with platform caveats.** Checksums, the 28 schema differences, committed-row counts and exception types match Windows, and wall-clock speed is within run-to-run noise:
  - bulk copy of 1M rows: AOT 10.7–10.8 s, against 11.1 s for the Windows AOT exe in the same session;
  - 1M-row read: about 1 s on both OSes;
  - schema comparison: 97–99 ms;
  - DMV poll: 5–6 ms.
- **AOT build:**
  - the same 53 warning lines as Windows;
  - a single 17.4 MB exe linking only libc, libm and ld-linux. ICU, OpenSSL 3 and (for Kerberos) `libgssapi_krb5` load at run time;
  - managed SNI only, with no sidecar;
  - glibc 2.34 or later: built on Ubuntu 24.04, it ran on 22.04.
- **Linux differences the tool must handle:**
  - **TCP only:** named pipes are not supported, and there is no shared memory.
  - **Integrated authentication is Kerberos only** (no NTLM fallback). Without a realm the error is "Cannot generate SSPI context".
  - **Missing ICU or `libssl` aborts the process** (exit 134) instead of throwing, so `spar doctor` and a startup check should verify both.
  - **Bulk copy uses about 2.3× the process CPU.** It drops to Windows levels with `DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0`; spin-waiting is inferred, not profiled.
  - **Explicit `VarChar` parameters use the database code page,** so the copy tool should send `nvarchar`.
- **Credentials on Linux:** `libsecret` is callable from NativeAOT. It needs a D-Bus session and a Secret Service (gnome-keyring or KWallet), which a desktop session normally provides.
- **Leftovers:** none. About 45 MB was added to the `spar-linux-desktop-nuget` volume, and no containers or networks remain.

## 2026-11-13, Generated contract modules ship as .js + .d.ts with a map to C# (decision; the user delegated it)

Spike: `spikes/ts-compiled-contracts/` (background agent). Three variants of the linked `@spar/app` package were compared: `.ts`; `.js` + `.d.ts`; and `.js` + `.d.ts` + a `.d.ts.map` pointing into the C# contract file. Both language servers (TypeScript 7.0.2, VS Code's 6.0.3) were driven headlessly.

- **No user-visible editor difference** between `.ts` and `.js` + `.d.ts`: the same completions, hovers with docs, diagnostics and quick fixes. Through the package, no quick fix edits a generated file.
- **The declaration map to C# works in both servers.** Go-to-definition opens the `.cs` file at the exact identifier (error case, DTO property, command, contract). TS 7 also maps references and rename locations through it: a rename from TypeScript then edits the C# identifier only, not its C# references. So renames must be done in C# with the IDE's refactoring, then regenerated (to document).
- **Rename is never refused,** because the package's real path is outside `node_modules`. `preserveSymlinks` is not a fix.
- **tsc matrix (48 cells):** variant × TS 6/7 × permissive/strictest × `skipLibCheck` × bundler/nodenext. The app compiles clean everywhere, and the deliberate mistakes give identical errors.
- **Fault injection:**
  - A generated `.ts` is checked under the app's flags: an unused local fails the user's build.
  - A broken `.d.ts` is silent under `skipLibCheck: true`.
  - Nothing checks `.js` against `.d.ts`.
- **Vite bundles are byte-identical** across variants. An unused contract is tree-shaken through both the subpath and a barrel.
- **Decision:** the generator emits `.js` + `.d.ts` + `.d.ts.map` (to the C# declarations, from Roslyn locations), with two required tests:
  1. the generated `.d.ts` compiles with `skipLibCheck: false` and the strictest flags under TS 6 and 7;
  2. the `.js` matches the `.d.ts` and the manifest.

  If the C# map proves harmful (for example renames under TS 7), it can be dropped without other changes.

## 2026-11-13, Linux from the start (user decision)

- **Decision (user):** Spar supports Windows (WebView2) and Linux (WebKitGTK) desktop hosts from the start, not as a later milestone. This follows from the Linux host spike (feasible on both WebKitGTK APIs) and from the SQL Server tool's data layer working on Linux with the same results as Windows.
- **Plan changes (`docs/plan.md`):**
  - Design section 3 is now "Desktop hosts". The core is host-neutral, and each host supplies the SDK channel, the transport, origin checks, automation and native services (`Spar.Windows`, `Spar.Linux`).
  - The desktop test level runs on both hosts; Linux runs headlessly under Xvfb in a container in CI.
  - M0 close-out gains a Linux real-desktop spike in the user's WSL with WSLg, which also chooses the WebKitGTK API.
  - M1 builds both hosts, and runs the Notes sample and the desktop tests on both.
- **Pitfalls:** everything surprising found in the spikes is collected in `docs/pitfalls.md`, at the user's request. It includes sending `nvarchar` rather than `varchar` in copies.

## 2026-11-13, Generated JavaScript is validated against tsc in Spar's own tests (user decision)

- **The generator writes the contract modules' `.js` + `.d.ts` (+ `.d.ts.map` to C#) directly,** from one in-memory model per contract and a single emitter for both files.
  - It does not generate `.ts` and run `tsc` in the user's build. That would add about 1 s to every contract-changing build, measured with the TypeScript 7 native `tsc`.
  - It would also bring back a dependency on the app's TypeScript version, or else require shipping a per-OS compiler binary in the NuGet package.
  - It would complicate the declaration map to C#.
- **Validation in Spar's test suite, not in the user's build (user emphasis): a differential test.**
  - From the same model the generator also emits the equivalent `.ts`.
  - `tsc --declaration` compiles it, with TypeScript 6 and 7.
  - The resulting declarations and JavaScript are compared with the directly generated `.d.ts` and `.js`. Any divergence fails the test.
  - It adds to the already required tests (the `.d.ts` compiles with `skipLibCheck: false` and the strictest flags; the `.js` matches the `.d.ts` and the manifest).
- **Plan:** observability is now design section 6b of `docs/plan.md`: the optional layer, client telemetry forwarded through the backend to OTLP, and `traceparent` over the bridge.

## 2026-11-13, Frontend packing: per-file Brotli (decision; the user leaned to Brotli) and the `.local` origin finding

Spike: `spikes/asset-packing/` (background agent). Four formats were tested on Windows and Linux NativeAOT, through both hosts.

- **Decision:** one `Spar.Frontend` resource, holding an index plus per-file Brotli (quality 11, fitted window).
  - Files saving less than 5% are stored as is.
  - The host decodes on a background thread at start into a cache, and serves uncompressed entries straight from the image.
  - Small bundles may pack uncompressed, with a feature switch (`Spar.Frontend.Brotli=false`) that trims the decoder (tested on Windows).
  - Zip and separate resources are dropped.
- **Numbers:**
  - The decoder adds about 168.5 KB on both OSes. It is linked statically on both; the Linux exe still needs only libc, libm and ld-linux.
  - Compression: React 227 KB to 61 KB (zip 71 KB); a 2.8 MB bundle to 875 KB (zip 1,006 KB). The exe was 44% smaller than raw for the large bundle, and the same size for React.
  - Decoding takes about 2 ms per MB of JS.
  - Page load showed no format difference.
  - The raw and Brotli packs were byte-identical across OSes; the zip was not.
- **Neither WebView decodes `Content-Encoding`** (`br`, `gzip`) on host-answered responses: confirmed on WebKitGTK 2.52.6 too.
- **Finding:** after the machine's network changed (hotspot, VPN), the origin `https://app.spar.local` added about 2.0 s to every page load. `.local` goes to system name resolution; `Resolve-DnsName app.spar.local` took 1 s. `https://app.localhost` loaded in 54 ms and `https://app.spar.invalid` in 89 ms. The desktop origin must be a name that never resolves; the choice is open (added to `docs/pitfalls.md` and the plan).

## 2026-11-13, Plan synchronized with the decisions (second pass)

`docs/plan.md` was rewritten to match this log. Its previous text is in git history and in a scratch backup.
- **Stale statements corrected:** "Windows first", the serializer marked "awaiting confirmation", the client linked into `node_modules`, `dotnet run` with packaged assets.
- **Decisions added:** the contract syntax, the TypeScript SDK and generated client, the bridge protocol, both hosts and native services, the dev-server loop, the runtime choices, testing projects and levels, tooling, observability, Brotli packing, and the SQL Server tool as the M3 consumer.
- **The open-questions table** now lists every pending question with its state.

## 2026-11-13, Browser telemetry through the host: confirmed (spike result)

Spike: `spikes/browser-telemetry/` (background agent; `RESULTS.md`, screenshots in `artifacts/browser-telemetry/`). It covered Windows desktop (AOT), loopback, and Linux WebKitGTK (AOT, Docker + Xvfb). The traces were also checked in the Aspire 13.6.5 dashboard, standalone with API-key auth and from an AppHost.

- **Shape that worked:**
  - The page makes a boot check, `POST /_spar/otlp/config`, and loads the OpenTelemetry chunk lazily only when the host says telemetry is on. It exports OTLP protobuf to the relative `/_spar/otlp/v1/traces` and `/logs`.
  - Each host serves `/_spar/otlp/*` behind the same origin or token checks as bridge calls, and answers 404 without a relay or an endpoint.
  - The backend layer forwards the bytes unchanged to `OTEL_EXPORTER_OTLP_ENDPOINT`, adds `OTEL_EXPORTER_OTLP_HEADERS`, and speaks http/protobuf or gRPC (wrapping the same protobuf).
  - Every call carries `traceparent`/`tracestate`, as headers on fetch and as a field in the call messages. `SparDiagnostics` parents the backend span on the page span.
- **Numbers:**
  - Every page call span had a backend child: 5,601 of 5,601 on Windows, 2,811 of 2,811 on loopback, 5,601 of 5,601 on Linux.
  - Page boot code +819 B gzip. The lazy chunk is 27.0 KB gzip (23.7 KB Brotli); traces alone 20.6 KB.
  - Executable with the layer: Windows 6.25 → 10.63 MB, Linux 7.02 → 10.60 MB.
  - Tracing adds about 25–45 µs per call (Windows postMessage 0.092 → 0.117 ms).
  - The config check costs 2–6 ms before mount. Windows UI-ready was 292–306 ms with telemetry against 239–263 ms without; about 30 ms of that is unexplained.
  - Zero AOT warnings in every publish.
  - Disabled means nothing is sent: no layer means no OpenTelemetry code in the bundle; layer without endpoint means a 404 and 0 receiver requests.
- **Requirements found:**
  - Desktop hosts ask the page to flush before closing and wait at most 1 s; without it, 0 page spans arrived in 2 of 2 runs.
  - The page flush never rejects. The relay answers 503 at once for 5 s after a connection failure. Retries stay with the page's bounded exporter; the backend never replays.
  - The service name comes from `OTEL_SERVICE_NAME`, and the page reports as `<name>-page`.
- **Split for Spar:**
  - The core SDK carries the tracing hook, trace context on every call, the flush-before-close hook and host headers, with no OpenTelemetry dependency.
  - Core runtime and hosts pass trace context, keep the BCL `SparDiagnostics`, route `/_spar/otlp/*` (404 without a relay), and do the bounded flush.
  - The optional .NET layer (`Spar.OpenTelemetry`) holds the SDK, the exporter and the relay. The optional page module (`@spar/app/telemetry`) is compiled in only when the app references the layer.
- **Open choices for the user:**
  - the config check before mount, or a flag the host puts in the HTML;
  - whether to keep click spans (they need telemetry before React mounts, and nest 2–3 deep);
  - whether `zone.js` (12.9 KB gzip) is worth it for click spans that survive an `await`.
- **Not tested:**
  - trace context on streams and subscriptions;
  - the Linux close button;
  - GTK4 and the sandboxed WebKitGTK;
  - Aspire with TLS;
  - relay rate limits beyond the 4 MB body cap;
  - a foreign frame posting to the relay on Linux;
  - non-default sampling.

## 2026-11-13, Browser telemetry: how the page starts it (user decision)

The user accepted the proposal that followed the browser-telemetry spike.

- **No user-interaction (click) spans by default.**
  - Every Spar call already has a page span with its backend child. Click spans would force telemetry to load before React mounts, and React nests 2–3 `click` spans per click.
  - The SDK offers an explicit span to group the calls of one interaction (for example `traced("save note", async () => …)`).
  - Click instrumentation stays an app opt-in.
- **Nothing waits before mount.**
  - Desktop hosts (WebView2, WebKitGTK) put the telemetry settings in the script they already inject at document creation, for example `window.__spar.telemetry = { serviceName, delayMs, logs }`, or leave it out. The boot code reads it synchronously, and `import()`s the chunk only when it is present.
  - Loopback has no injected script, so the page keeps `POST /_spar/otlp/config`. It runs in parallel with the mount, not before it. Writing the flag into the HTML (by the backend in Release, by the Vite plugin through `transformIndexHtml` in Debug) was rejected as an extra path to maintain for a 2–6 ms request off the critical path.
  - An app that does not reference the layer has no telemetry code and makes no request (as in the spike).
- **No `zone.js`.**
  - It costs 12.9 KB gzip plus a context manager, and it patches globals such as `Promise`.
  - It only matters with click spans.
  - It cannot intercept native `async`/`await` without downlevelling them. This is the documented limit, not verified in the spike.
  - TC39 `AsyncContext` is the better future route if it ships.
- **Accepted cost:** document-load spans start slightly later; the browser keeps the navigation timings, so no data is lost.
- **To verify in M2:** that host-injected scripts run before the page's own scripts on both WebViews, including when the page comes from Vite.

## 2026-11-13, Page security: strict CSP declared in C#, and a navigation lock on every host (proposal, being verified)

The user asked for the strictest CSP an app can work with, so that anything extra (a font CDN, for example) is added consciously, and applied the same way by the WebView and by Vite. They want it declared in C# where the Spar application is configured, generated with the contracts, and readable by the Vite plugin. They approved the plan entry and a spike.

- **Proposal** (plan section 3b):
  - The base policy allows only `'self'`, plus `base-uri`, `form-action`, `frame-ancestors` and `object-src` set to `'none'`.
  - The declaration is `spar.ContentSecurity(csp => …)` inside `SparApplication.Create`. Constant values and a direct call are required; otherwise the analyzer reports an error.
  - The generator puts the policy in the catalog; the client step writes `obj/spar/client/csp.json` for the Vite plugin.
  - The HTML response carries it as a header, from the host in Release and from Vite in Debug. Debug adds only the HMR WebSocket and a nonce.
  - Violations are reported to the backend as diagnostics.
- **Navigation lock:** while answering, a gap was found. The plan required a navigation lock only on Linux, because WebKitGTK messages carry no origin. Windows had origin checks on the bridge but no rule for navigations or new windows.
  - **Proposal:** every host cancels navigations outside the app origin and blocks new windows. External links open in the system browser only through an explicit SDK call.
- **Why a C# declaration rather than an MSBuild item or a file:** it is where the app is defined and most visible. The cost is a static-analysis rule (constants only, a direct call). A CSP change reaches Vite after the next build, as contract changes do.
- **Spike `spikes/csp-navigation` checks:**
  - CSP headers on `WebResourceRequested` and `spar://` responses;
  - host-injected scripts under a strict policy, and whether they run before page scripts (also the open check from the telemetry decision);
  - Vite Fast Refresh with a nonce;
  - the C# extraction and its diagnostics;
  - violation reporting;
  - the navigation lock and external links on both hosts.
- **Extended the same day at the user's request:** the other security headers and Subresource Integrity. The proposal is in plan section 3b:
  - `nosniff` with exact content types;
  - `Referrer-Policy: no-referrer`;
  - COOP and CORP `same-origin`;
  - a deny-by-default `Permissions-Policy`, matched by the hosts' permission events, with opt-ins declared in C#;
  - COEP only as an opt-in;
  - no HSTS or `X-XSS-Protection`;
  - `Host` checks on loopback and the dev server;
  - Trusted Types as a candidate;
  - SRI from the Vite plugin. For self-served files SRI catches faults more than attacks; for external resources it is a real defence.

  The spike checks engine support for each.

## 2026-11-13, Security defaults and overrides (user decision)

The user said the initial security constraints are not fixed. What matters is that apps are secure and follow best practice, and that an author who really wants to can go past a default. The existing constraints were reviewed (AGENTS.md, plan, this log, skills), and the user accepted the result, now in plan section 3c.

- **Three levels:**
  1. **Always on**, for controls against other sites and processes: the bridge origin and document checks, the loopback token, `Host` and `Origin` checks, the devtools token and lockfile, and no remote debugging port.
  2. **Strict defaults with an explicit C# override** in the `Create` configuration, logged at startup and listed by `spar doctor`.
  3. **Page-only limits** (file grants, write-only secrets), which an author bypasses with their own command.
- **Kept as they were:**
  - the production boundary test;
  - no-store;
  - file grants and write-only secrets;
  - no automatic replay;
  - the AOT rules for Spar's own code;
  - top-frame-only injection on Linux as a default.
- **Reworded:**
  - **The profile rule:** `artifacts/webview2-profiles/` and "never `%LOCALAPPDATA%`" apply to this repository only. Shipped apps default to `%LOCALAPPDATA%\<Company>\<App>\WebView2` on Windows and to the XDG folders on Linux, because WebView2's default next to the executable fails under `Program Files`. Updated in `AGENTS.md` and `spar-webview2-hosting`.
  - **Non-Production publish:** it stays an error, with `SparAllowNonProductionPublish` turning it into a warning. Updated in `spar-msbuild`.
- **Restored:** `npm ci --ignore-scripts`, from the first draft, with an opt-out. Not yet verified with Vite 8's native binaries.
- **Gaps closed:**
  - Production browser settings: devtools, accelerator keys, password saving and autofill (relevant to the SQL tool), downloads, swipe navigation and host objects off; the WebKitGTK equivalents.
  - Ignored browser environment overrides, now including `WEBVIEW2_BROWSER_EXECUTABLE_FOLDER` and `WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS`. This settles the M0 hardening question.
  - The per-user profile location.
  - A user-only devtools lockfile on Linux (`XDG_RUNTIME_DIR` or `0600`).
  - Loopback off in Production unless opted in, with a safe token hand-off.
- **Later:** signing in M4; telemetry privacy defaults and log redaction.
- **Verification:** M1 host work checks every default and its opt-in on the published executable. The running `spikes/csp-navigation` does not cover the browser settings.
- **Same day, permissions (user question, proposal in plan 3b):** permissions have two layers.
  - The author declares the allowed features in C#, which sets both `Permissions-Policy` and the host gate; everything else is denied without a prompt.
  - Per declared feature, a request is granted, prompted by the WebView, or decided by a C# handler.
  - The app can reset remembered answers, so end users can revoke the webcam from the app.
  - OS privacy settings still apply.
  - The grant path is checked in M3.
- **Same day:** the user agreed that the browser settings are verified in M1. They questioned whether loopback has any production use; the answer is pending.
- **Same day, loopback in Production (user decision): removed.**
  - The candidate uses all had better answers:
    - a missing WebView runtime: detect it at startup, or install it with the package;
    - a backend in WSL or a container: a developer scenario;
    - the user's own browser: the WebView already supports screen readers and zoom;
    - remote use from another machine: a different product, needing TLS and authentication.
  - The loopback host is compiled only into development and test variants, and the production boundary test covers it. `AGENTS.md` updated.
  - Revisit only for a real consumer.

## 2026-11-14, Light and dark theme without flicker (proposal)

The user asked that light and dark mode be handled without flicker. An earlier Spar version let the app give a light and a dark background colour, which the window used while the app started. That is research input, not checked here. The proposal is in plan section 3d.

- **Colours declared in C#** (constants, read by the build like the CSP).
- **The host paints before the page exists:**
  - the window background and title bar;
  - the WebView default background (WebView2 `DefaultBackgroundColor`; WebKitGTK `webkit_web_view_set_background_color`);
  - `prefers-color-scheme`.
- **The build adds `color-scheme` and a matching style to `index.html`,** with the style's hash added to the CSP.
- **The theme follows the system,** or the app's own choice saved by the host and applied before the page loads at the next start.
- **To measure:** showing the window at once against showing it at `ui-ready`, with first-frame captures in both themes on both hosts.
- **Interaction with plan 3c:** the host may set `WEBVIEW2_DEFAULT_BACKGROUND_COLOR` itself. That does not conflict with ignoring external `WEBVIEW2_*` overrides.
- **Same day, splash screen (user decision, defaults to measure):**
  - An optional splash lives as static HTML in `index.html`, not as native code and not as a SPA component.
  - It becomes visible only after a delay, so a fast start shows no splash; that avoids a second kind of flicker, as the user asked. Once shown, it stays for a minimum time before fading.
  - The SDK removes it at `ui-ready`, or at `spar.ready()` in manual mode.
  - Plan section 3d. Both the theme handling and the splash are being measured by `spikes/theme-startup`.

## 2026-11-14, Desktop origin: `https://app.localhost` (user decision)

- **Decision:** the Windows host serves the page from `https://app.localhost`, the same for every app. Linux keeps `spar://app/`, because WebKitGTK cannot intercept `https`. The origin never changes between versions without a migration, because it keys `localStorage`, IndexedDB, cookies and remembered permissions.
- **Why:**
  - `.localhost` is reserved (RFC 6761) and needs no name resolution: 54 ms per load, against about 2 s for `app.spar.local` after a network change.
  - It is a secure context.
  - The browser profile is already per app (plan 3c), so one shared name mixes no data.
- **Rejected:**
  - `app.spar.invalid`: fails safe, but measured 89 ms (one sample), and may be treated as a public origin by Chromium's local network rules.
  - `.example` and `.test`: some resolvers answer for them.
- **Mitigation:** a request that escaped the host would reach `127.0.0.1:443`. So the host answers every request to its origin, with a 404 for unknown paths.
- **Being checked** by `spikes/desktop-origin`:
  - requests that the host's interception cannot see (WebSocket, service workers, beacons) and whether they leak to loopback;
  - stored data across restarts and an app update;
  - local network access prompts;
  - load times of the candidate names, repeated.

## 2026-11-14, Default `dotnet run` profile: Desktop (user decision)

- **`Desktop` comes first** in the template's `launchSettings.json`, so plain `dotnet run` opens the real window, with the page served by Vite in Debug. `Loopback` stays as the second profile, for browser devtools with extensions, Playwright, Aspire, and machines without a display.
- **Why:** the default shows the app as users will see it (window, theme, splash, permissions, native services). Loopback is not shipped in Production. In Debug, the WebView's own devtools are available (the plan 3c ban applies to Production only).
- **No silent fallback:** without a display the app stops with an error that suggests `-lp Loopback`, and `spar doctor` reports the same.

## 2026-11-14, Node at run time in Debug (user decision)

- **Accepted.** Debug serves the page from the app-owned Vite server, so it needs Node and `node_modules`.
  - A development machine has Node anyway, for the Release build.
  - The fast test project needs no Node.
  - The loop stays fast: Fast Refresh in 39 ms, and no bundle build per C# change.
- **Missing Node** stops the app with a clear error naming both fixes. `spar doctor` checks the version.
- **Opt-out:** `-p:SparDevServer=false` embeds the bundle in Debug through Release's code path, for checking startup, theme, CSP and headers as shipped, and for machines whose policies block Node processes.
- **Follow-up being checked** (`spikes/desktop-origin`): Debug keeping the `https://app.localhost` origin by having the host fetch pages from Vite, with HMR's WebSocket going to Vite directly.

## 2026-11-14, Review of earlier versions and other frameworks (research input)

At the user's request, read-only reviews covered:
- the earlier Spar repositories (`spar`, `spar_old`, `spar-experiments`, `old/spar_spikes`, `spar.v2` to `spar.v6` including the application-factory experiment, `old/spar.v7`);
- the Tauri v2 documentation.

The synthesis is `docs/research-review.md`, now a design record exempt from the user-facing documentation rule (`AGENTS.md` updated). Nothing there is decided yet; it is listed as one open question in the plan, to be moved into the plan item by item.

**Applied now:**
- `docs/pitfalls.md` gained the cheap pitfalls, marked as not rechecked on the current Runtime. Among them:
  - the 6.4 s exit of a young WebView, traced to SmartScreen;
  - idle hangs raising no event;
  - `WM_ENDSESSION`;
  - the navigation pre-request;
  - identical options for a shared profile;
  - the Linux GPU variables;
  - antivirus delays;
  - Hot Reload patching DTOs;
  - the Sandbox lessons.
- **Plan 3c:** the browser environment filter is a block-list of the named variables only, so the Linux GPU workarounds keep working in Production.

**Main themes for discussion:**
- bridge limits and admission (every implemented earlier version had them; v7 has none);
- two-stage close;
- a startup-failure dialog with a runtime-version check;
- single instance;
- app data folders separated by channel;
- the SQL tool's desktop-feature list;
- generated C# test clients and the test-SDK item;
- devtools and MCP rules;
- measurement method.

## 2026-11-14, Page security verified (spike result)

Spike: `spikes/csp-navigation/` (background agent; `RESULTS.md`, 138 logged runs in `artifacts/csp-navigation/`).
- **Engines:** WebView2 157.0.4341.46, Chromium 156 (Playwright, loopback), WebKitGTK 2.52.6 (4.1 and 6.0, Docker + Xvfb).
- **The plan 3b proposal holds,** with the changes below; plan 3b is updated.

**Verified:**
- **CSP:**
  - The base CSP works unchanged with the React production bundle on all four engines (0 violations), given `assetsInlineLimit = 0`.
  - It is honoured as a header everywhere, with the same results as `<meta>`.
  - Every blocked probe stayed blocked: inline script and handlers, `eval`, `javascript:`, inline styles, `data:` images, external img/font/fetch/script, `<base>`, `<object>`, frames, form posts and `blob:` workers.
- **Injected scripts** run before page scripts on both hosts, also with pages from Vite.
- **Navigation lock:** it held on both hosts.
  - WebView2 leaked every cancelled GET navigation to the network until the host added `AddWebResourceRequestedFilter("*", DOCUMENT)`.
  - WebKitGTK does not follow redirects from a scheme handler.
  - `ShellExecuteEx` opened an external URL in 374 ms (once). The Linux call failed in Docker ("Operation not supported").
- **Headers:**
  - `nosniff` holds everywhere, but the Linux host must repeat `Content-Type` in its header set.
  - `Permissions-Policy` and COEP isolation hold on WebView2 and Chromium, not on WebKitGTK.
  - CORP, COOP and `frame-ancestors` showed effects only on loopback.
  - No `Referer` was sent on the desktop.
  - `isSecureContext` is true on `spar://` without registration.
- **Vite dev with a nonce:**
  - Fast Refresh in 37–47 ms, CSS in 12–15 ms, the overlay in 26–31 ms (Chromium), 0 violations.
  - No HMR `connect-src` entry is needed.
  - The dev HTML must be `no-store` without an ETag (a stale cached nonce blocked Vite's styles).
- **Reporting:**
  - A host-injected listener and `report-uri` caught every violation.
  - An SDK module listener missed those raised while parsing the HTML.
  - `report-to` delivered only on WebKitGTK.
- **Trusted Types:** supported by all four engines; React and (in Chromium) the Vite dev client work.
- **SRI:**
  - Enforced everywhere, including import-map integrity for lazy chunks on all engines.
  - The import map's hash must go into `script-src`.
  - 73–79 ms per build.
- **C# extraction prototype:**
  - Generator, analyzer and extractor agree.
  - Five misuse cases give the same located errors.
  - 109–574 ms without building.

**Incident: a camera prompt reached the user's desktop.**
- A Windows run without `Permissions-Policy` called `getUserMedia`.
- WebView2 raised no `PermissionRequested` for the camera, so the host's deny handler never ran. The call hung, and a prompt (most likely the Windows privacy prompt for unpackaged apps) appeared.
- With `camera=()` the call failed in about 250 ms with no event.
- **Consequences:**
  - The `Permissions-Policy` deny list is required on Windows.
  - Camera and microphone handling needs a follow-up check that cannot reach a person.
  - The spike's probe now runs only in the Linux container.

**Pending user decisions:**
- Trusted Types default;
- same-origin iframes;
- WebAssembly as an opt-in, given that WebKitGTK does not enforce the block;
- the reporting channel;
- SRI default;
- camera and microphone on WebView2.

## 2026-11-14, Wire details and `npm ci --ignore-scripts` (spike results)

Spikes: `spikes/wire-details/` and `spikes/npm-ignore-scripts/` (one background agent; nothing downloaded).

**Wire details: a proposal in plan section 2, eight choices pending.**
- **`long` and `ulong`:**
  - always a canonical decimal string, `bigint` in TypeScript;
  - as numbers, only 603 of 100,000 random 64-bit ids survived `JSON.parse`;
  - a `JSON.parse` reviver using source text cost 132–158 ms per 100k rows, against 14–16 ms;
  - real SQL values cross 2^53: `cpu_ticks` after about 35 days of uptime, `query_hash` as `bigint`, identities seeded low.
- **`decimal`:**
  - a canonical string;
  - `JSON.parse` changed 5 of 7 samples (money max, `1.50`);
  - SQL `decimal(38)` overflows `System.Decimal`, and only `GetSqlDecimal` reads it.
- **Dates:**
  - ISO strings;
  - `new Date()` on offset-less values uses the page's time zone (`"2026-11-14"` is 13 November in Los Angeles);
  - SqlClient returns `datetime2` and `SYSUTCDATETIME()` as `Kind=Unspecified`.
- **NaN and ±Infinity:** a writer failure. `JSON.stringify` silently turns them into `null`, so the TypeScript encoder checks `Number.isFinite`.
- **Lone surrogates:**
  - replaced with U+FFFD both ways;
  - .NET writers already do this, and .NET readers throw on the `"\ud800"` that `JSON.stringify` sends, which fails the whole request today;
  - SqlClient also replaces them in `nvarchar`;
  - a lossless prototype worked but is not recommended.
- **Null and void:** void has no `value`; `T?` writes `null`.
- **Duplicate properties:** rejected, with name and byte offset, at no measurable cost.
- **Writer failures:**
  - buffered responses, with a failure carrying `execution: "completed"`;
  - streams report `stopped` and `itemsSent`;
  - an unbuffered writer had already sent 278,560 bytes of invalid JSON when it failed.

**Pending choices:**
- `long` as `bigint` or `string`;
- lone surrogates;
- enum names, and `[Flags]`;
- `byte[]` as `Uint8Array` or a string;
- the `TimeSpan` format;
- `DateTime` strictness;
- SQL `decimal(38)`;
- the stream failure wording, and whether loopback uses HTTP 500.

**`--ignore-scripts`: verified as the default.**
- The installed tree is file-for-file identical to a normal `npm ci`. Rolldown, Lightning CSS and TypeScript 7 native packages work, and so do the build and the dev server.
- Only `fsevents` (macOS only) and `lightningcss` (`prepare`, never run for registry packages) declare scripts.
- The flag goes on the command line, not in `.npmrc`. `--no-audit` was added from the research review.

## 2026-11-14, Desktop origin `app.localhost` verified (spike result)

Spike: `spikes/desktop-origin/` (background agent; WebView2 Runtime 157.0.4341.46; NativeAOT host, 0 warnings). The decision stands, and plan sections 3, 3b and 4 are updated.

- **Requests the host does not answer:**
  - An unanswered request opened 2 TLS connections to `[::1]:443` (never to `127.0.0.1`); a host 404 never leaked.
  - `wss://app.localhost` is never intercepted.
  - `ws://` and `http://` to `*.localhost` are not mixed content, so they leave in plaintext to port 80.
  - The legacy filter misses shared and service workers; `AddWebResourceRequestedFilterWithRequestSourceKinds(ALL)` sees them.
  - Every navigation preconnects to `[::1]:443` even when every request is answered.
- **Fixes:**
  - the source-kinds filter;
  - `connect-src https://app.localhost` instead of `'self'`, because `'self'` allows `wss://` to the same host;
  - as defence in depth, `--host-resolver-rules="MAP app.localhost ~NOTFOUND"`. It gave 0 loopback connections with no load cost. It must be quoted inside the argument, and was tested only through the environment variable.
- **Stored data survived** a restart and a new exe version on the same profile: `localStorage`, IndexedDB, a `__Host-` cookie and a remembered permission. `app2.localhost` saw none of it.
- **Secure context:** `isSecureContext`, `crypto.subtle`, OPFS, Web Locks and service workers work. `storage.persist()` raises permission kind 13.
- **Local network access:** no prompt and no effect for host-served pages, on either name (address space `unknown`). **Correction to the origin decision:** the risk named there for `.invalid` (being treated as a public origin) did not show. The choice stays `app.localhost`: same speed, and the guards above are needed either way.
- **Load times** (median, `Navigate()` to `ui-ready`):
  - `app.localhost` 68.5 ms, `app.spar.invalid` 67.3 ms, `app.example` 68.8 ms;
  - `app.spar.local` 190 ms, without the earlier 2 s penalty (the network was unchanged).
- **Debug through an in-process proxy keeps `https://app.localhost`:**
  - Fast Refresh, CSS updates and the overlay all worked, at the same latencies.
  - A full load costs about 35 ms more.
  - Vite's HMR client must be pointed at `ws://127.0.0.1:<port>`.

  Proposed as the Debug default.
- **Interference:** the spike's listener on ports 443/80 accepted preconnect sockets from the theme spike's browsers during about 30 short windows, so those timings may differ slightly.
- **Not tested:**
  - a real LAN server;
  - Runtimes without `ICoreWebView2_22`;
  - the resolver rule through the options object;
  - `IsReputationCheckingRequired`;
  - a focused-window clipboard;
  - Vite's nonce through the proxy;
  - Linux.

## 2026-11-14, Theme without flicker and the splash (spike result)

Spike: `spikes/theme-startup/` (background agent; captures in `artifacts/theme-startup/`).
- **Hosts:** WebView2 157 on Windows; WebKitGTK 2.52 GTK3 and GTK4 under Docker + Xvfb.
- **Capture:** frames every ~16.7 ms with `PrintWindow`, cross-checked against Windows.Graphics.Capture (10 configurations × 3 runs agreed), and every ~2.2 ms from the Xvfb root.
- **Build:** NativeAOT, 0 warnings.
- **Plan 3d is updated.**

**Baseline, dark mode:**
- **Windows:** the window was white for 333–366 ms (no class brush), and the title bar was light.
- **Linux:** the GTK window colour for about 60 ms, then the WebView's white for 14–25 ms, then a light page.

**Minimal fix, 0 wrong frames in both themes on all three hosts:**
- **Windows:**
  - the class brush, plus a `WM_ERASEBKGND` handler painting the current colour;
  - `DWMWA_USE_IMMERSIVE_DARK_MODE` set before showing;
  - `PreferredColorScheme`;
  - a **transparent** WebView default background at creation. In dark mode, before any document paints, WebView2 shows Chromium's `#121212` for about 900 ms whatever opaque colour is set.
- **Linux:** a GTK CSS provider, GTK's dark preference (which drives `prefers-color-scheme` on both GTK versions, live), and an **opaque** WebView background. Transparent is black on GTK3/X11 without a compositor.
- **Supersedes the theme proposal:** `WEBVIEW2_DEFAULT_BACKGROUND_COLOR` is not needed, and it would override the creation options. The page meta and style have no measurable effect once the host layers exist; they stay for the splash and form controls. Their CSP hash works on both engines.

**Showing the window:**
- Show at once: visible in 1–5 ms (Windows), 35–60 ms (GTK3), 120–190 ms (GTK4).
- A hidden window never renders, so `ui-ready` never arrives.
- DWM cloaking works, but a slow start then shows nothing for 1.5 s.

**Theme sources:**
- The app's saved choice, applied before load, gave 0 wrong frames.
- Live switches react in 6–21 ms, with at most one mixed frame (3 of 12).
- The Linux portal was absent in the container; the GTK settings fallback was used.

**Splash:**
- The fading-in transparent design was flawed: it showed app → splash → app. The adopted design is an opaque cover in the theme colour from the first frame, with only the mark fading in.
- A fast app showed 0 splash frames; slow apps showed it exactly once in 109 runs, for at least about 470 ms.
- The delay runs from the first frame: WebView2 holds a non-contentful first frame for about 550 ms, so the effective threshold there is about 0.9–1.2 s.
- 1500 ms of synchronous work showed no splash on WebView2 (no frames render).
- A minimum-visible guard needed an animation-frame request at bundle start.
- Proposed defaults: 350 ms delay, 400 ms minimum, 120/150 ms fades.

**Pending user decisions:**
- show at once or cloaked;
- a transparent WebView2 background, or `#121212` as the dark colour;
- the delay counted from the first frame, or contentful HTML;
- the blocked-rendering guard;
- title bar timing on live changes;
- a dark GTK theme name for themes like Yaru.

**Not tested:** see plan 3d.

## 2026-11-14, Linux on a real desktop (WSLg) (spike result)

Spike: `spikes/linux-wsl-desktop/` (background agent; the user's WSL Ubuntu 24.04 with WSLg).
- **Software:** WebKitGTK 2.52.6, GTK 3.24.41 and 4.14.5, Mesa 25.2.8.
- **Build:** NativeAOT, 0 warnings (GTK3 2,756,528 bytes, GTK4 2,752,272 bytes), restored offline from the Windows packages folder.
- **No GPU:** the user is not in the `render` group, so everything ran on llvmpipe. Plan section 3 and the open questions are updated.

**Results:**
- **Windows and backends:**
  - Both APIs map, render and theme correctly on Wayland and X11.
  - On Wayland, GTK's own title bar has only a close button; on X11, weston draws a full frame.
  - Warm starts, view loaded before showing: GTK3 167–185 ms, GTK4 214–261 ms. Cold, GTK4 took 794–1059 ms, because of its GL renderer on llvmpipe; with `GSK_RENDERER=cairo` it took 102–137 ms.
- **The slow GTK3 start explained:** only on X11, without the sandbox, with the window shown before loading (674 ms warm). WebKit's draw handler waits about 494 ms for a first frame that the web process cannot produce until the UI thread sets up its network connection. Loading before showing fixes it.
- **The sandbox works in WSL** for both APIs, costing 5–22 ms.
- **GPU:**
  - The accelerated path cost about 300 ms on the cold first frame on llvmpipe.
  - `WEBKIT_DISABLE_DMABUF_RENDERER=1` turns acceleration off entirely in 2.52.
  - Forcing `GALLIUM_DRIVER=d3d12` crashed GTK3 at exit and blanked GTK4 (its web process aborted).
- **Smoothness and resize:** 61–62 fps with no gaps; maximize and restore were clean.
- **HiDPI:** the monitors were at 100%. `GDK_SCALE` applies only on X11; `GDK_DPI_SCALE` becomes a page zoom on 4.1 and is ignored on 6.0.
- **Focus:** correct only when the view exists before the window is shown, or with an explicit grab.
- **Input:** XTEST under XWayland gives trusted input on both APIs; Wayland has nothing. WebKitWebDriver is packaged for 4.1 only. IME is untested (no framework installed).
- **Theme:** the portal under WSLg has no Settings interface, so the GTK fallback is required. A private Settings portal answered in about 1 ms. Theme layers gave 0 wrong frames in 40 runs.
- **Startup check:** a prototype exits with code 3 within 7–102 ms, naming the missing library or helper. Before it, the app died with `DllNotFoundException` or a GLib abort after 2–4 s.

**API recommendation: WebKitGTK 6.0 (GTK4).**
- **For it:** the maintained direction, the sandbox always on, XTEST working, and the cost mostly the renderer on a software GPU.
- **For 4.1:** 47–76 ms faster warm, about 80 MB less memory, a WebDriver package.

**Pending user decisions:**
- the API;
- acceleration on software GPUs;
- the Wayland title-bar layout;
- XTEST with forced X11 in tests, against WebDriver;
- the GTK fallback only, or documenting `portals.conf`.

**Not tested:**
- a real GPU and NVIDIA;
- real HiDPI;
- IME;
- screen readers;
- GNOME, KDE, wlroots, libadwaita;
- WebKitWebDriver;
- the splash on a real desktop;
- ARM64.

## 2026-11-14, Spike review: unrecorded results and corrections (evidence)

Three read-only reviews of the spikes listed results that no design record held, and contradictions between the records and the spikes. Each item below was checked against the cited spike's `RESULTS.md` or code before it was written. Nothing was re-run. This entry decides nothing; the choices it raises are in the plan's open-questions table or the section's pending list.

- **Added to `docs/plan.md`:**
  - **Bridge protocol (2c):** the spikes' encoding, marked as evidence for M1 and not a decided wire format. Seven host-to-page kinds (`o r i e c n u`, plus `z` for the flush before close) and five page-to-host kinds (`h q s k x`, plus `z`). Credits: a window of 8 batches by default, refilled in halves. The trace field (`-`, or `traceparent` with an optional `;tracestate`; over 128 or 8 KB dropped; 55 bytes). The loopback endpoints and their status codes (200, 400, 401, 403, 404, 409, 499, 500). Cancellation details: a cancel that overtakes its call is remembered for 30 s (implemented, never provoked); a foreign `OperationCanceledException` is `failed`; `break` waits for the confirmation; a pull stream stops at its next `yield` (16–17 ms); un-awaited background work is invisible; a navigation starts a new document epoch.
  - **Contracts (2):** the `Program.cs` rule as a proposal (one `Create`, one `Run`), the accepted scalar request types, explicit-name rules, reserved contract names including the `vite` and `telemetry` subpaths, payload-less error cases, the spike's C# `Result` API and the `default(Result)` problem, the diagnostic catalogue (SPAR001–005, 010–016, 020–023, 030–035, 040) with a pointer to `spikes/contract-syntax/RESULTS.md`, the canonical `long` check (`+1`, `007`, `-0`, `1 `, `1e3` accepted by the BCL parsers), and building client objects field by field (`__proto__` through `Object.assign`).
  - **Hosts (3):** the extra WebView2 interface slots M1 needs (`ICoreWebView2_22` slot 123; the theme's raw-vtable slots 26/27, 105, 8/9, 20/21 and 10), the GTK3/4.1 against GTK4/6.0 API table, the runtime package names (`libwebkit2gtk-4.1-0`, `libwebkitgtk-6.0-4`, about 34 MB download and 128 MB on disk each), the 2.40 minimum for the APIs used, and the distribution list (jammy-updates 2.50.4).
  - **Security (3b, 3c):** `AreDevToolsEnabled=false` does not stop in-process CDP; the loopback bridge spike has no `Host` check (two other spikes answer 421 and 403); how a loopback page gets its token (URL fragment, `sessionStorage`, `replaceState`) is undecided for a page served without Vite.
  - **Development loop (4):** the Debug proxy's requirements, and dropping `If-None-Match` and `ETag` for HTML, which the spike proxy forwards.
  - **M1 runtime choices:** the SqlClient single-file recipe (managed networking switch plus the SNI package excluded), the `DllNotFoundException` without the sidecar, and single-warn mode hiding whole assemblies (4 warnings instead of one per member).
  - **M3:** `BatchSize = 0` is not atomic (about 310,000 rows kept on cancel) and cancels in 0.8–1.0 s against 34–51 ms with 50,000-row batches.
  - **M0 close-out:** the evidence location is pending the user's decision (`docs/evidence/` does not exist; `/spikes/` is git-ignored), the raw evidence inside `obj/` and `artifacts/` folders, and the dependencies between spikes that constrain the deletion order.
- **Corrected in place:**
  - `docs/plan.md`: `InvariantGlobalization=false` costs 260,096 B on the measured baseline (1,689,088 against 1,428,992 B), not the unsourced "+225 KB" (also in the 2026-11-13 entry "App authors choose the runtime", which stays as written); invalid UTF-8 is a `JsonException` only in values that are read; the CSP "verified under this policy" wording (the bundle ran `connect-src 'self'` on `app.spar.invalid`); the cancellation wording in plan 2c and M3 now uses `CallError`; Linux maximize was clean of white and black frames but one capture showed the bounds before the content; the startup aborts took 1.9–4.3 s, not 2–4 s; Deflate was measured only inside the zip (about 15% larger payloads); xUnit leaves an empty `TestResults/`; warnings as errors does not cover `MSB*` and `NETSDK*` warnings (NETSDK1229 passed every `spikes/cswinrt` build).
  - `docs/research-review.md`: the background-colour environment variable is no longer a fallback; the Deflate control's status; the empty-response rule against plan 3b's 403 and the unexercised handler.
  - `docs/app-model.md`: "plan D7" did not exist; the `Program.cs` rule now cites plan section 2 and is marked Leaning. Stream failure delivery, the `Result` API, payload-less cases and the `kind` values are listed as open. No code block changed (`node tools/extract-blocks.mjs --check` in `spikes/app-model-check`: 8 blocks, 0 differ).
- **Put up for the user:** the bridge encoding (letter-prefixed lines or JSON envelopes), stream failure delivery, the `CallError.kind` values, the C# `Result` API and `default`, validating strings in skipped unknown properties, Debug type-checking, `MSBuildTreatWarningsAsErrors`, the loopback `Host` status code and token delivery, and how spike evidence is kept before `spikes/` is deleted.

## 2026-11-14, Linux desktop integrations on both WebKitGTK APIs (evidence)

Spike: `spikes/linux-integration/` (background agent), in WSLg (Wayland and X11), with a real xfce4-panel and openbox in a container built from the existing `spar-linux-desktop` image (54.7 MB downloaded inside it). NativeAOT publish with 0 warnings: GTK3 3,578,272 bytes, GTK4 3,582,464 bytes.

- **Everything tested works on both APIs:**
  - **Title bar drawn by the page:** the host starts the move. On openbox, 6 of 6 drags moved the window exactly, and the edges resized it. Double-click maximize took 2–16 ms. Outside maximize reached the page in 7–14 ms.
  - **Tray, on a real panel:** StatusNotifierItem plus dbusmenu on GDBus. It registers 0.4–0.8 ms after a watcher appears and again after a panel restart.
  - **Close to tray:** restore to the first frame in 18–25 ms, with the page's state kept.
  - **Placement:** size and maximized state on every backend, position on X11 only.
  - **Other services:** file pickers, clipboard, print to PDF (82 ms), single instance.
- **Recommendations:**
  - start the move after a 4 px threshold;
  - single instance through a lock file and a Unix socket (handoff 4–10 ms, a clear failure after 2 s), not GApplication;
  - keep `javascript-can-access-clipboard` off.
- **GTK4-only problems:**
  - web processes outlive the window after a window-manager move or resize (5 of 5, more than 10 s), to fix in M1;
  - on WSLg Wayland, a hidden window shows its last frame, and a window started maximized is killed. Both have verified workarounds.
- **Effect on the API choice:** none. The 6.0 recommendation stands; the choice is still the user's.
- **Not tested:** Wayland input, file drop, GNOME and KDE tray hosts, notifications, the window menu (no window manager tested offered one).
- **Rule broken during the spike:** GTK, printing and restore wrote to the real WSL home (`~/.config`, `~/.local`, `~/.nuget`). Each path's birth time falls within the spike's run. Cleanup waits for the user. Recorded as a pitfall.

## 2026-11-14, Windows title bar drawn by the page (evidence; proposal)

Spike: `spikes/windows-titlebar/` (background agent). Real input ran in Windows Sandbox. On the host desktop the spike sent messages only to its own off-screen windows. WebView2 Runtime 157, SDK 1.0.4314.35. NativeAOT with 0 warnings.

- **Proposal:**
  - the windowed controller;
  - a custom frame that keeps `WS_OVERLAPPEDWINDOW` (`WM_NCCALCSIZE` restores the top edge, with a maximized inset);
  - `app-region: drag / no-drag` through `IsNonClientRegionSupportEnabled`;
  - caption buttons drawn by the page, made real through a host hit-test cache and one host-owned transparent child window above the WebView.
- **Numbers:**
  - every real-input check passed, including the Snap Layouts flyout on the page's maximize button;
  - hit test p50 0.1–0.5 µs, p99 under 4 µs, unaffected by a busy page;
  - +4,096 bytes of executable, no start-up difference (median 389–393 ms);
  - 0 wrong-colour frames in both themes.
- **Rejected for the default:**
  - Windows' own buttons: they fail when maximized (`DwmDefWindowProc`), cost about 17 µs per hit test, and show `#4a4a4a` for about 350 ms in a dark start;
  - visual hosting: it passed too, but it brings input forwarding, DirectComposition and a host-drawn system menu, with drag and drop, touch, pen and IME untested. It stays a fallback.
- **Still open:**
  - screen readers see a native title bar with empty rectangles;
  - other DPI scales;
  - Windows 10;
  - the drop shadow (not visible in Sandbox).
- **For the user:** whether the page-drawn bar becomes the default, and whether to try the `WM_GETTITLEBARINFOEX` workaround for native buttons.

## 2026-11-14, Spike evidence is versioned (user decision)

- **Decision (user):** `spikes/` is no longer ignored by git.
  - `bin/`, `obj/`, `node_modules/` and the root `artifacts/` stay ignored.
  - The spike sources and results come to 12.3 MB (1,676 files), with no binaries or WebView2 profiles.
- **Raw evidence was copied out of ignored folders** into each spike's `results/`, so that a clean cannot lose it: `d3-variants/obj` → `results/from-obj` (825 KB), `ts-results/obj/editor`, `ts-compiled-contracts/obj/editor` and its `.md` files, `npm-ignore-scripts/*/obj/spar/client`, and `d8-automation/artifacts/results` → `results/`. The originals stay in place so the spikes still run.
- **Deleting a spike:** move its `RESULTS.md` and `results/` to `docs/evidence/<name>/` and update the links first (AGENTS.md, Layout). The dependency order between spikes is in plan M0 item 8.
- **History (proposal, the user's call):** the user expects to squash the M0 commits into one. Before squashing, keep the full history in an archive tag (for example `archive/m0-spikes`) or a `git bundle` outside the repository. The main branch then carries only `docs/evidence/`, and the spike code stays recoverable.

## 2026-11-14, The C# `Result` API (evidence; proposal)

Spike: `spikes/result-api/` (background agent). SDK 11.0.100, NativeAOT with 0 warnings and no suppressions. Four forms were compared: class, struct, union struct and union class. One writer source produced byte-identical JSON for all four.

- **Proposal:**
  - **Shape:** a C# 15 custom union (`[Union]`) with typed `Value` and `Error` (they throw on the other state), `IsSuccess`/`IsError`, `TryGetValue`/`TryGetError`, and `Result<TError>` with `Result.Success` for commands without a value.
  - **Not included:** `Match` or factories.
  - **Reading:** a `switch` over the result is exhaustive (CS8509).
  - **Conversions:** union conversions also work for interface-typed values, where implicit operators fail with CS0029.
- **`default`:** a distinct invalid state on which every member throws. Caught at compile time:
  - the struct form needs analyzer SPAR050 (about 40 lines, a new rule), which caught 4 of 6 probe cases;
  - the class form gets 5 of 6 from nullable analysis;
  - neither catches array elements.
- **Numbers (20 commands):**

  | Form | Executable | Synchronous `ValueTask` error |
  |---|---|---|
  | Plain baseline | 1,517,568 B | — |
  | Union struct | +102,400 B | 24 B per call |
  | Union class | +83,456 B | 64 B per call |

  Synchronous dispatch is 120–180 ns for every form.
- **For the user:** union struct (no allocation, needs SPAR050) or union class (simpler safety, one allocation per result).
- **`docs/app-model.md`:** its code blocks compile unchanged. Only its text changes, once the form is chosen.

## 2026-11-14, The client in React, Vue and Angular (evidence; proposal)

Spike: `spikes/frontend-frameworks/` (background agent). React 19.3.1, Vue 3.5.45, Angular 22.2.6 (full CLI), production builds in headless Chromium under the plan 3b CSP, with a scripted fake transport. New downloads came to 35, 20 and 83 MB.

- **React and Angular work** with the `Call`/`Result` prototype. Vue breaks by default: Proxies and `#private` fields throw `TypeError`.
- **Proposed SDK changes** (user decision):
  - `Call` becomes a `Promise` subclass. This fixes TanStack's types, and `no-floating-promises` then catches a forgotten call by default.
  - A `Symbol.toStringTag` getter on `Result`/`Call` fixes Vue.
  - An optional Vue type augmentation.

  `#private` stays.
- **CSP:** React and Vue's runtime-only build give 0 violations. Vue's full build and Angular JIT are blocked as `eval`. Angular AOT needs no `<base>`, no component styles and `inlineCritical: false`.
- **Guidance found:**
  - TanStack retries ran 4 backend calls for one error, so `retry: false` with `unwrap`;
  - a call created during render started 30 times in 600 ms;
  - RxJS `from()` does not abort;
  - StrictMode's aborted effect shows a `cancelled` failure. The `docs/app-model.md` 6.2 example now ignores its own abort; the check passes again on tsc 7 and 6.
- **Not tested:** the real hosts and bridge, dev servers and HMR under the CSP, the per-call cost of a `Promise`-subclass `Call`.

## 2026-11-14, Bridge message encoding (evidence; recommendation)

Spike: `spikes/bridge-encoding/` (background agent). NativeAOT hosts with 0 warnings: WebView2 desktop, loopback with headless Chromium, and WebKitGTK codec checks in WSL. Medians of 3 runs.

- **Encodings compared:**
  - (A) the spikes' letter-prefixed lines;
  - (B) JSON envelopes;
  - (C) a JSON header object, a TAB, then the payload's own JSON;
  - (Bj) B over `PostWebMessageAsJson`.
- **End to end, A, B and C are equal within noise:**

  | Measure | Result |
  |---|---|
  | WebView2 unary | 59 / 79 / 61 µs |
  | WebView2 100k rows | 163–164 ms |
  | Loopback 100k rows | 59–69 ms |
  | Push p99 | 1.05–1.37 ms |
  | Cancel confirmation p99 | 0.2–1.3 ms |

  B and C add 20–90 bytes per message (+0.02% on batched streams). Bj halved stream speed (328 ms) and cost +50% host CPU.
- **Robustness decides:**
  - A cannot add header fields and misparses silently.
  - B loses the call id on a damaged frame and re-reads large arguments.
  - C has neither problem.
  - B and C need a safe-integer id check, duplicate-key rejection and tolerant trace reads.
- **Recommendation: C**, with version negotiation in `hello`/`open` and the rule that an unknown message type for a pending call fails that call. **User decision.**
- **Not tested:** WebKitGTK end to end, unary over `fetch` on loopback, several streams on one channel, very large unary results. Four WSL runs were killed by `SIGTERM` while another spike ran in WSL and were retried; treat the WebKitGTK codec numbers as indicative.

## 2026-11-14, Windows Sandbox checks: title bar accessibility, native buttons, camera and microphone (evidence)

Spike: `spikes/windows-sandbox-checks/` (background agent). Windows Sandbox had `VideoInput` and `AudioInput` disabled. On the host there was no input injection and no setting change. NativeAOT with 0 warnings.

- **DPI:** only 100 % could be tested; the guest's scale follows the client window's 96 DPI monitor. 125–200 % and `WM_DPICHANGED` need a host monitor at that scale (user's setting) or a VM.
- **Accessibility, solved:** a top-level UIA provider with `ProviderOptions_RefuseNonClientSupport`, plus subclassed drag-region windows, removes the phantom title bar and the unnamed panes. The control view went from 45 to 24 elements, with the page fully exposed. Narrator not run (audio).
- **Bug fixed:** the first click on an inactive window's page buttons was lost (`WM_NCMOUSELEAVE`).
- **Native buttons:** viable when the host handles them only while maximized, using `WM_GETTITLEBARINFOEX` rectangles. Page-drawn buttons stay the proposed default.
- **Camera and microphone:**
  - `PermissionRequested` fires for fake devices, per kind.
  - `SavesInProfile` defaults to true, so a remembered allow bypasses the host later.
  - `--use-fake-ui-for-media-stream` bypasses the handler; only `Permissions-Policy` held everywhere.
  - `ICoreWebView2Profile4` lists and resets remembered answers, and a deny stops a running track.
  - Host rules recorded in plan 3b. A real device and the Windows privacy prompt stay untested.

## 2026-11-14, The C# `Result` container is a union struct (user decision)

- **Decision (user):** `Result<T, TError>` and `Result<TError>` are `readonly struct` C# 15 unions, as in `spikes/result-api/Forms/UnionStruct`. The user prefers allocating as little as possible, unless the class form brings another advantage.
- **The class form's advantages, weighed:**
  - nullable analysis catches 5 of 6 unassigned cases, against 4 of 6 with SPAR050;
  - about 19 KB smaller over 20 commands;
  - no analyzer to maintain.

  None outweighed the allocation per result: Spar already ships analyzers, and SPAR050 is about 40 lines.
- **Consequences:**
  - analyzer SPAR050 (`default`/`new()` of a `Result`) becomes part of M1;
  - the generator emits one async method per command;
  - `docs/app-model.md` section 3.1 describes reading and `default`. Its check (`spikes/app-model-check/Spar.Stubs/Result.cs`) now uses the union struct, with a union switch and a `Result<TError>` command: 8 blocks unchanged, 0 warnings, and CS8509 still fails a missing case.
- **Errors themselves are unchanged:** `closed record` hierarchies, as decided on 2026-11-13.

## 2026-11-14, WebKitGTK memory and the GTK4 lifetime fix (evidence)

Spike: `spikes/linux-memory/` (background agent). WSLg with Mesa llvmpipe, 3–4 clean rounds per configuration; Electron 45.2.1 as a reference. The GTK4 lifetime part ran under openbox in a container. NativeAOT with 0 warnings.

- **Memory does not separate 4.1 and 6.0.** 6.0's default costs 59–62 MB more than 4.1's, all of it GTK4's GL renderer on llvmpipe. With `GSK_RENDERER=cairo`, 6.0 is equal or lower: 310 against 320 MB on Wayland, 301 against 297 on X11.
- **Where it goes:**
  - the software GL path (WebKit acceleration about 90 MB, GTK4 GL about 75 MB) accounts for about 230 of 379 MB in 6.0's default;
  - WebKitGTK and GTK themselves take 140–155 MB;
  - .NET takes about 2 MB.
- **A heavy page** adds about 245 MB on both APIs; Electron's renderer grew 417 MB. Electron's empty page totals 292 / 281 MB.
- **GTK4 web processes outliving the window: fixed.**
  - Cause: the window-manager pointer grab leaves WebKit's gestures pressed. A pressed GTK4 gesture holds a reference on the view, and a GTK4 window's destruction only detaches it.
  - Fix: `gtk_event_controller_reset` on the view's controllers after `begin_move`/`begin_resize`. Exit then took 24–26 ms, 5 of 5 moves and 5 of 5 resizes.
- **The 6.0 recommendation stands.** Candidate default on software GPUs, already an open user decision: WebKit acceleration off plus cairo, giving 201–213 MB and the fastest start-up.
- **Not tested:** a real GPU, the fix on Wayland and on GNOME/KDE window managers, longer idle, memory pressure.
- **Measurement note:** other spikes ran WebKit in the same WSL distribution, so 35 runs were taken again with a foreign-process check, and 5 snapshots were excluded.

## 2026-11-14, Profiles, shutdown and data clearing on both hosts (evidence; recommendations)

Spike: `spikes/webview-profiles/` (background agent). WebView2 157.0.4341.46 (330 shutdown runs) and WebKitGTK 2.52.6 on 6.0 and 4.1 (320 runs, GDK Broadway, nothing on the user's desktop). NativeAOT with 0 warnings. The user's reading was confirmed: `Close` needs no wait, and waiting matters only before deleting or reusing a profile.

- **Shutdown:**
  - Exiting without waiting lost no `localStorage`, IndexedDB, cookie or Cache Storage data, even with the host killed. Every browser process exited.
  - The 6.4 s Windows exits are SmartScreen (a close within about 0.5 s of a navigation). They drop to 52–67 ms with `IsReputationCheckingRequired = false`.
  - Waiting for `BrowserProcessExited` delays the host to about 326 ms.
  - On Linux every process exits in 3–24 ms.
  - `Close` runs the unload events on WebView2.
- **Instances:**
  - An immediate relaunch works: environment creation took 6–11 ms, and the controller waits for the old browser.
  - A second Windows instance with the same options shares the browser; with different options it fails with `0x8007139F`.
  - A second Linux instance on the same folders silently loses `localStorage`.
- **Windows and profiles:**

  | Extra window | Private memory |
  |---|---|
  | Same profile | +26 MB |
  | Another profile | +30 MB |
  | InPrivate | +27 MB |
  | Another folder | +149 MB |

  On Linux an extra window costs +23–24 MB, and another data set +24 MB (6.0) or +32 MB (4.1).
- **Clearing data:**
  - `ClearBrowsingDataAll` takes 69–74 ms; `webkit_website_data_manager_clear` takes 30–38 ms.
  - `Profile8.Delete` poisons the name permanently.
- **Test profiles:** InPrivate costs 76–79 ms per test; Linux ephemeral data sets 60–69 ms; a fresh folder per launched process p50 701 ms.
- **Recommendations (user decisions pending):**
  - exit without waiting;
  - single instance before the environment;
  - one folder per app, with windows on the default profile;
  - clear through `ClearBrowsingDataAll`;
  - InPrivate or ephemeral data for in-process tests;
  - `IsReputationCheckingRequired = false` by default with a C# opt-in. This is a level-2 security default (plan 3c).
- **Correction:** the plan said `Close` skips `beforeunload`; on Runtime 157 it does not.
- **Not tested:** sign-out (`WM_ENDSESSION`), crashes, a pending Runtime update, the HTTP cache of a real origin, Windows 10, Linux on a real desktop.

## 2026-11-14, Analyzer rules for default `Result` values (evidence; proposal)

Spike: `spikes/spar050/` (background agent), asked by the user to cover all 6 probe cases and look for cases nobody had considered. SDK 11.0.100, Roslyn 5.9.

- **Corpus:** 199 cases (131 creating or observing a default `Result`, 57 clean, the rest compiler errors or by design).
- **Coverage:** the prototype caught 33 of 131 (25 %, including the compiler's), with 3 false positives. The new rules SPAR050–SPAR054 catch 117 of 131 (89 %), including all 6 original cases, with 0 false positives on the clean cases and on the app-model check.
- **Cost:**
  - 31–63 ms per build on 200 files (median 36 ms);
  - about 2,100 lines of analyzer and a separate code-fix assembly, with 10 fixes verified.
- **Left to the run-time guard (14 cases):** library or generated code built without the analyzer, early virtual calls from constructors, and APIs where .NET throws first.
- **Relies on:** CS0649, CA2019, CS0165/CS0177, CS0208, CS0452 and CS0019.
- **Effect on the struct decision:** the struct form now catches 6 of 6 (the class form 5 of 6). The analyzer cost moves from about 40 lines to about 2,100, which the decision did not count.
- **For the user:**
  - adopting the rule set;
  - raising CS0649 and CA2019 to errors in `Spar.Sdk` (a severity override);
  - a local `#pragma` for SPAR050 in Spar's own guard tests (a suppression).

## 2026-11-14, Linux host uses WebKitGTK 6.0 (user decision)

- **Decision:** the Linux host targets `webkitgtk-6.0` (GTK4, libsoup 3) only. No GTK3 build, no compile-time switch.
- **Evidence:**
  - `spikes/linux-wsl-desktop`: 6.0 is the maintained API, and its web-process sandbox is always on. 4.1 was 47–76 ms faster warm.
  - `spikes/linux-integration`: the desktop integrations work on both; two GTK4-only problems have verified workarounds.
  - `spikes/linux-memory`: with `GSK_RENDERER=cairo`, 6.0 uses as much memory as 4.1 or less (310 against 320 MB). The GTK4 lifetime problem is fixed.
- **Given up:** the only WebDriver package (Ubuntu 24.04 ships it for 4.1 only), and distributions without `webkitgtk-6.0`.
- **What other frameworks do** (read 2026-11-14, from their release notes and docs):
  - Wails v3.0.0 defaults to GTK4 with WebKitGTK 6.0, keeps GTK3 behind a build tag until v3.1, and documents Ubuntu 24.04 or Debian 13 as the minimum. Its release notes list a Linux advisory, only partly fixed, in which a same-origin child frame reaches the native message handler (see the pitfall on iframe messages).
  - Tauri 2 (wry) uses webkit2gtk-4.1 on GTK3; a move to GTK4 is an open, help-wanted Tauri issue.
  - The webview/webview C library lets the build choose 6.0, 4.1 or 4.0.
  - The saucer C++ library uses GTK4 and WebKitGTK 6.0.
  - Electron bundles Chromium and does not use WebKitGTK.
- **Still open:** the oldest supported distribution (M4); XTEST or WebDriver for desktop tests; acceleration on software GPUs.

## 2026-11-14, Software GPU default on Linux (user decision)

- **Decision:** on a software GPU the Linux host turns WebKit acceleration off and uses GTK4's cairo renderer. An app can turn acceleration back on from C#. A user's own `GSK_RENDERER` is respected.
- **Numbers** (`spikes/linux-memory`, WSLg with llvmpipe, at ready on a small page, Wayland / X11):
  - 6.0 by default: 379 / 359 MB;
  - acceleration off plus cairo: 213 / 201 MB.
  - The accelerated path also cost about 300 ms on the cold first frame.
- **Not measured:** a real GPU, where the defaults stay unchanged.
- **To define in M1:** how the host detects a software GPU, and the option's name.

## 2026-11-14, Bridge encoding C (user decision)

- **Decision:** bridge messages are a JSON header, a TAB, then the payload's own JSON (form C of `spikes/bridge-encoding`). The user left the choice to the recommendation.
- **Numbers:** A, B and C performed the same within noise end to end:
  - unary call on WebView2: 59–79 µs;
  - 100k rows: 163–164 ms;
  - push p99: 1.2–1.4 ms.
  - B and C add 20–90 bytes per message.
- **Why C:**
  - A could not grow: an older decoder rejected a new header field, and a stray space shifted fields silently.
  - B lost the call id on a damaged frame, so the call hung until its timeout, and it re-read large arguments (533 µs against 220 µs for 223 KB).
  - C keeps the id, passes the body through unchanged, and its keys match the loopback headers.
- **Rules that come with it:**
  - version negotiation at `hello`/`open`;
  - an unknown type for a pending call fails that call;
  - safe-integer ids;
  - duplicate header keys rejected;
  - tolerant trace values;
  - string messages only, never `PostWebMessageAsJson`.

## 2026-11-14, SmartScreen default in WebView2 (user decision)

- **Decision:** `IsReputationCheckingRequired = false` while the navigation lock keeps the page on the app origin. It turns on automatically when the app allows navigation to other origins from C#. An explicit C# override exists both ways. This is a level-2 security default (plan 3c).
- **Why:**
  - On the app origin SmartScreen checks only the app's own embedded content, and external links open in the system browser, which runs its own check.
  - With it on, a close within about 0.5 s of a navigation waited 6.40–6.49 s (30 of 30). With it off, the exit took 52–67 ms (`spikes/webview-profiles`).
  - It also sends navigation requests to Microsoft that the app never asked for.
- **Not verified:** that the flag stops every SmartScreen request seen in the net logs (`spikes/desktop-origin`). M1 checks it with a net log.

## 2026-11-14, Profiles and shutdown (user decision)

The user confirmed all the recommendations from `spikes/webview-profiles`:

- **Shutdown:** exit without waiting for the browser. Wait for `BrowserProcessExited`, with a timeout, only before deleting or reusing the user data folder. 330 Windows and 320 Linux runs lost no data, even with the host killed.
- **Single instance:** enforced before the WebView environment is created, because on Linux a second instance on the same folders started silently and lost `localStorage`. A relaunch forwards its arguments to the running instance. An app that wants several instances says so in C#, and how their data folders work is settled when one needs it.
- **Data folders:** one user data folder per app. Windows use the default profile; named profiles only for separate data.
  - Cost of an extra window: +26 MB on the same profile, +30 MB on another profile, +149 MB with another folder.
- **Clearing data:** through `ClearBrowsingDataAll` / `webkit_website_data_manager_clear` (about 70 ms), never `Profile8.Delete`, which poisons the profile name.
- **Tests:** InPrivate on Windows (76–79 ms per test) and ephemeral data sets on Linux (60–69 ms).
  - The user data folder that tests still need stays under `artifacts/webview2-profiles/` (AGENTS.md rule), never versioned, so cleanup knows where to look.
- **Open, for the test-framework topic:** how parallel tests and several test processes create environments and profiles without clashing. Options: one folder per process, or a shared folder with identical options, plus a lock and cleanup of folders left by crashed runs.

## 2026-11-14, Caption buttons drawn by the page (user decision)

- **Decision:** with a custom title bar, the page draws minimize, maximize and close on Windows, as it does on Linux. The host hit-tests them from the page's cached layout, through a `WS_EX_TRANSPARENT` child window above the WebView.
- **Evidence** (`spikes/windows-titlebar`, `spikes/windows-sandbox-checks`):
  - Snap Layouts appear on the page's maximize button.
  - 0 wrong-colour frames, at start and across 8 theme switches.
  - Real input in Windows Sandbox passed: drag, double-click, system menu, edges, Snap, keyboard shortcuts.
  - Accessibility solved with a UIA provider (`ProviderOptions_RefuseNonClientSupport`).
  - The first-click bug is fixed.
  - Cost: +4 KB of executable, about 170 lines of host code, no start-up difference.
- **Rejected:** Windows' own buttons. They work only while restored (or maximized through `WM_GETTITLEBARINFOEX`, where they are invisible to UIA), and in dark mode they show `#4a4a4a` for about 350 ms at every start.
- **Still to check with the user at the PC:** DPI other than 100 %, and Narrator (plan table).

## 2026-11-14, TypeScript SDK: `Call` as a `Promise` subclass, `toStringTag`, Vue augmentation (user decision)

- **Decision:** the user accepted the three changes from `spikes/frontend-frameworks`, provided the subclass's per-call cost was not high. `spikes/call-promise-cost` measured it as negligible:
  1. `Call<T, E>` extends `Promise<Result<T, E>>` and adds `match` and `unwrap`. The client constructs it directly, it keeps `Symbol.species` = `Promise`, and the client exports `Call` as a type only.
  2. `Result` and `Call` get a `Symbol.toStringTag` getter, so Vue never proxies them.
  3. An optional `@vue/reactivity` `RefUnwrapBailTypes` augmentation keeps `ref(result).value` typed.
- **Numbers** (ns per call to create, settle and consume; WebView2 157 / Node 24 (V8 13.6) / WebKitGTK 2.52.6 on 6.0; 3 runs each):
  - Native promise: 44–65 / 53–68 / 65–113.
  - Today's thenable: 93–116 / 99–126 / 198–257.
  - The subclass: 85–135 / 96–149 / 246–326.
  - At most +91 ns over native on WebView2, which is 0.15 % of the 59 µs unary round trip measured by `spikes/bridge-encoding`. On JSC +261 ns.
  - 10,000 outstanding calls settled in 1.2–1.45 ms, against 1.8–2.0 ms for the thenable.
  - 75 bytes kept per pending call, against 163.
- **Microtasks:**
  - `await call` takes 1 tick after a pending call, where a native promise takes 0 and the thenable 2.
  - The order is exactly the thenable's (12 of 50 mixed combinations differ from native, the same 12).
  - Nothing changes across separate bridge messages.
- **Rejected:**
  - The wrapping `of` form, which costs about twice as much.
  - Dropping the `species` override, which made `then` return a misleading `Call` and cost +127 to +145 ns.
- **Measured, not chosen:** setting `Call.prototype.constructor = Promise` gives native ticks and order for +17 to +25 ns on `await`, but `call.constructor` then reports `Promise`.
- **Not measured:**
  - a bridge round trip with the subclass;
  - a WebKitGTK round trip;
  - allocation on JSC (RSS only);
  - first-call cost and GC pauses;
  - streams;
  - bundle size.

## 2026-11-14, Analyzer rules SPAR050–SPAR054 adopted (user decision)

- **Decision:** adopt SPAR050–SPAR054 as proposed by `spikes/spar050` (117 of 131 cases, 0 false positives, 31–63 ms per build).
- **No severity override for CS0649 or CA2019.** SPAR053 also reports a never-assigned field that holds a `Result`, next to the compiler's warning. Fields of other types are unaffected. This covers the 5 cases that only those two diagnostics caught, which stayed warnings in apps without `TreatWarningsAsErrors`.
- **No `#pragma` in source.** Spar's own tests of the run-time guard create `default(Result)` on purpose. They turn SPAR050 off through an `.editorconfig` section scoped to those test files, with its reason, following the existing `[src/Spar.Generator/**.cs]` pattern. The user proposed using `.editorconfig` for test-only analyzer changes. The rest of the test code keeps the analyzer.
- **Related, for M1:** `Microsoft.Build.TaskAuthoring.Analyzer` (NuGet 18.13.0) checks MSBuild task code for process-global APIs. Spar's build steps are already meant to be thread-safe tasks, so the task project will reference it. The user raised it.

## 2026-11-14, Linux single instance and where test-only analyzer overrides live (user decisions)

- **Single instance on Linux:** a lock file plus a Unix socket (`spikes/linux-integration`).
  - The handoff of the arguments to the first instance takes 4–10 ms.
  - A hung first instance gives a clear failure after 2 s.
  - Rejected: GApplication's D-Bus uniqueness, which waited forever on a hung first instance and lost a launch made while the first instance was exiting.
- **Analyzer overrides for tests:**
  - They are path-scoped sections in the root `.editorconfig`, never nested `.editorconfig` files. One file shows every exception at review. `AGENTS.md` now says so.
  - The run-time guard tests that create `default(Result)` on purpose live in their own folder, and SPAR050 is off for that folder only.
  - Tests of the analyzers themselves pass source text to Roslyn and need no change.

## 2026-11-14, The UI thread is never blocked; startup prerequisites get a visible error (user requirements)

- **UI thread:** the user asked to reinforce that the window's UI thread (the WebView2 STA, the GLib main loop) is never blocked and does as little as possible.
  - The plan (section 3), `AGENTS.md` and the WebView2 hosting skill now list:
    - what may run there;
    - what never does: backend commands, I/O, synchronous waits, long CPU work, large payload parsing;
    - that host callbacks on that thread answer from data at hand or take a deferral.
  - Proposed for M1: a stall probe in Development and DesktopTest builds, desktop tests that fail on stalls over a budget, and no probe in Production.
- **Missing prerequisites:** the user asked how each OS shows an error when the app starts without what it needs. This was "Adopt (M1)" in `docs/research-review.md`. `spikes/startup-prerequisites` was started to find out:
  - Windows: a missing, too old or broken WebView2 Runtime; dialogs only inside Windows Sandbox.
  - Linux: missing GTK4, WebKitGTK 6.0, helpers or display, in Docker with a real X server.

## 2026-11-14, Resource-usage API (user request)

- The user asked for an API that reports:
  - how much disk the WebView profile takes;
  - how much memory the backend and the WebView use.
  The app can then show these in its frontend.
- Recorded as a native service in plan section 3 ("Native services") and in the open-questions table. The app exposes it through its own contract, so the page sees nothing by default.
- Candidate counters to verify:
  - Windows: the WebView2 process list, per-process private memory, the backend's working set and GC counters, and folder size.
  - Linux: the WebKit processes' PSS, `webkit_website_data_manager_fetch` sizes, and folder sizes.
- A spike is to follow, likely for M3.

## 2026-11-14, Native feature APIs must be easy and pleasant (user requirement)

- The user asked that the APIs for the tray icon, the custom title bar and the other native features be as easy and pleasant as possible.
- Recorded in plan section 3 ("Native services") and the open-questions table:
  - the common case is one short call, and platform differences don't reach app code;
  - each feature is designed with the user, one at a time, with a compiled example in `docs/app-model.md` before implementation.
- First ideas, not decided: a declarative tray in `SparApplication.Create`, a one-option custom title bar, and caption buttons and the drag region found from page markup by the SDK.

## 2026-11-14, Native feature list and live resource usage (user requests)

- **Native features to design:** the user added drag and drop, print to PDF, the file open and save dialogs and a folder picker to the list.
  - Already adopted for M3 (`docs/research-review.md` section 6): file access through grants, the clipboard, drag and drop.
  - Measured but without a designed API: the tray, the title bar, placement, print to PDF, single instance.
  - New: the folder picker, whose grant scope needs designing.
  - Plan section 3 ("Native services") now lists each feature with its state.
- **Live resource usage:**
  - An app may show memory and disk use continuously, for example in a status bar. So the resource-usage service also offers a subscription that samples on a timer and keeps only the newest value.
  - Proposed: sample only while subscribed, separate intervals for memory and disk, push only on a change beyond a threshold, and a C# interval option with a floor.
  - The spike runs after the three spikes already in progress.

## 2026-11-14, Resource-usage API is selective; menus and notifications to discuss (user)

- **Selective resource usage:** an app chooses what it samples. Each measure can be subscribed to or read once on its own: backend memory, web-engine memory, each data folder. For example, an app may sample memory for a status bar and read folder sizes only when its settings page opens.
- **To discuss:** the user wants a design discussion of the features first marked "later" or "discuss": menus and accelerators, notifications, deep links and file associations.

## 2026-11-14, Trusted Types on by default (user decision)

- **Decision:** the base policy includes `require-trusted-types-for 'script'`, with an explicit C# opt-out. This is a level-2 security default (plan 3c).
- **Evidence:** all four engines support it. React and Vue production bundles gave 0 violations under it, and in Chromium the Vite dev client, Fast Refresh and the error overlay also work (`spikes/csp-navigation`, `spikes/frontend-frameworks`).
- **Tested later, when needed:** Angular, the dev loop on WebView2 and WebKitGTK, and other edge cases.

## 2026-11-14, MSBuild task analyzer and thread-safe tasks (evidence; proposal)

Spike: `spikes/msbuild-task-analyzer/` (SDK 11.0.100, MSBuild 18.12.2). The user raised the analyzer.

- **Packages:**
  - `Microsoft.Build.TaskAuthoring.Analyzer` 18.13.0, the only version published. It loads with this SDK's compiler.
  - `Microsoft.Build.Framework` and `Microsoft.Build.Utilities.Core` 18.12.2, already in `Directory.Packages.props`, as compile-only references.
  - No NuGet warnings.
- **Rules:**
  - MSBuildTask0001–0014, plus a suppressor for CS8618 on `[Required]` properties.
  - Warnings: 0002–0005, 0012 and 0014. Info: 0001 and 0006–0011. 0013 is off.
  - Info rules never show in a command-line build, so 0001 (`Console`, `Environment.Exit`, `SetCurrentDirectory`) is invisible there.
  - The real option is `msbuild_task_analyzer.run_mt_analyzers_on_all_tasks`.
- **Code fixes:** not shipped in 18.13.0 (dotnet/msbuild PR #15402). Applied by hand, 2 of 9 fixes for 0002 did not compile.
- **Not detected:** static mutable state, `AppContext` switches, culture setters, assembly-load hooks.
- **Multithreaded MSBuild (`-mt`, experimental):**
  - Only tasks with `[MSBuildMultiThreadableTask]` stay in the entry process. The interface alone is not enough: such a task goes to TaskHost processes.
  - 32 projects with thread-safe tasks: 255–262 ms with `-m:8 -mt`, against 474–514 ms with `-m:8`.
  - Strict mode is on under `-mt`: the working directory becomes a temp sentinel. Changing it is error MSB4286; a write through a relative path is warning MSB4287.
- **Cost:** about 35 ms of analyzer time, and no measurable difference in rebuild time (1,386 against 1,385 ms median).
- **Proposed:**
  - Reference the analyzer and use the full task shape now: the attribute, `IMultiThreadableTask`, a `TaskEnvironment` constructor, and a parameterless constructor using `TaskEnvironment.Fallback`.
  - Keep "no unsynchronized static state" as a review and test rule.
  - Add a `-mt` test lane with `-warnAsError:MSB4286;MSB4287;MSB4288`.
- **Needs the user's agreement** (`.editorconfig`, scoped to the task sources):
  - `msbuild_task_analyzer.run_mt_analyzers_on_all_tasks = true`;
  - MSBuildTask0001, 0011 and 0013 raised to warning, so they show and fail the build under `TreatWarningsAsErrors`;
  - optionally 0009 and 0010.
- **Not tested:** Visual Studio, design-time builds, the MSBuild server, Linux, a packaged `Spar.Sdk` consumer.
- **Seen in passing:** 13 of the 45 runs that launched new MSBuild processes stalled about 5 s at node start. The cause is unknown.

## 2026-11-14, Same-origin iframes blocked; MSBuild task analyzer settings (user decisions)

- **Same-origin iframes:** blocked by the base CSP (`default-src 'none'` covers `frame-src`). An app opts in from C# with `FrameSrc("'self'")`.
  - The opt-in's documentation warns that the framed content gets the app's access to the bridge. On Linux the host cannot tell an iframe's message from the window's.
  - For untrusted content, use `sandbox` iframes without `allow-same-origin`.
- **MSBuild task analyzer:** agreed as proposed.
  - The task project references `Microsoft.Build.TaskAuthoring.Analyzer` 18.13.0, and its tasks take the full thread-safe shape.
  - The root `.editorconfig` has a section for the task sources: `run_mt_analyzers_on_all_tasks = true`, and MSBuildTask0001, 0011 and 0013 at warning.
  - Its path (`src/Spar.Sdk.Tasks/`) is a placeholder until M1 creates the project.
  - A probe confirmed that an `.editorconfig` section carries both the option and the severities.
  - MSBuildTask0009 and 0010 (typed task parameters) stay at their defaults. They are reconsidered if Spar's tasks use typed parameters.

## 2026-11-14, Security risks are blocked by default; WebAssembly; MSBuildTask0009; docs site and skills plugin (user)

- **Rule:** anything that is a security risk is blocked by default. An app unblocks it only through an explicit option, and that option's documentation explains:
  - the risk;
  - when unblocking is reasonable;
  - the safer alternative, if there is one.

  The rule is in plan 3c and `AGENTS.md`.
- **WebAssembly:** blocked by the base CSP, with a C# opt-in that adds `'wasm-unsafe-eval'`.
  - WebKitGTK does not enforce the block, and no run-time WebKitGTK setting for it was found.
  - A probe of `JSC_useWebAssembly=false` in the web process follows, so the Linux host can match.
- **MSBuildTask0009 raised to warning** for the task sources. The user left the two optional rules to the recommendation.
  - 0009 catches a typed task parameter whose type MSBuild cannot parse, which otherwise fails only when the task is bound.
  - 0010 stays Info: it only notes culture-invariant parsing, which is what build tasks want.
- **Documentation website:** a good-looking, searchable site for app authors, built from the repository's user-facing docs, with the API reference and compiled examples. The generator and hosting are to choose.
- **Skills plugin:** the app-author skills are published as an agent plugin that users install, from a marketplace in this repository or in a separate one. The choice is pending. Its version follows the SDK.

## 2026-11-14, Performance regression tests (user request, for the test discussion)

- The user wants benchmarks in the test suite that keep Spar's builds fast and show when a new feature makes things slower.
- Proposed for the test-framework discussion:
  - BenchmarkDotNet suites for run-time hot paths: dispatch, serializers, the bridge codec;
  - a build-timing harness for clean, warm no-op and incremental builds, since BenchmarkDotNet suits in-process code better than whole `dotnet build` runs;
  - both compared with stored baselines.
- Still open: which run in CI, and how noise is handled. The spikes saw multi-second process-start stalls and wide ranges under concurrent load.

## 2026-11-14, Subresource Integrity on by default (user decision)

- **Decision:** the Spar Vite plugin adds sha384 `integrity` to the entry script, CSS, `modulepreload` links and lazy chunks (through an import map with `integrity`) by default. A C# option turns it off.
- **External resources:** every external script or stylesheet an app allows must come with its hash. Fonts cannot carry SRI.
- **Evidence** (`spikes/csp-navigation`): 73–79 ms per build. A tampered entry stopped the page and tampered CSS was not applied on every engine. Import-map `integrity` worked on WebView2, Chromium and WebKitGTK 2.52.

## 2026-11-14, CSP reporting: both channels, with flood limits (user decision)

- **Decision:** keep the injected `securitypolicyviolation` listener as the main channel and `report-uri /_spar/csp-report` as the backstop. The host removes duplicates. Both received every violation on every engine (`spikes/csp-navigation`); `report-to` delivered only on WebKitGTK.
- **Requirement from the user:** a badly made page can raise very many violations, which must not become a problem.
  - Reports are grouped by directive, blocked URI and source location. The first report of a group is logged in full, then only a periodic count.
  - The page-side listener batches, posting at most one message per interval.
  - The number of distinct groups per document is bounded.
  - The report endpoint caps the body size and the request rate.
  - All parsing and logging happen off the UI thread.
- **Still to do:** M1 sets the numbers with a flood test (for example 100,000 violations), and checks whether worker violations reach the listener.

## 2026-11-14, Camera, microphone and other permissions (user decision)

- **Decision:** the two-layer design from plan 3b.
  - **Default deny:** Spar always sends `Permissions-Policy`. Undeclared features are denied with no prompt on both hosts.
  - **Declaring grants:** declaring a feature (`spar.Permissions(p => p.Allow(Permission.Camera))`) grants it when the page asks. The OS privacy settings still apply.
  - **Asking first:** an app that wants to ask adds a C# handler. On Windows it may explicitly choose WebView2's own prompt.
  - **Remembering:** the browser never remembers answers (`SavesInProfile = false`). The app can list and reset stored answers, and resetting stops a running camera.
  - **Production:** `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` stays ignored.
- **Evidence:**
  - `spikes/csp-navigation` and `spikes/windows-sandbox-checks`.
  - Only `Permissions-Policy` held under every condition, and `--use-fake-ui-for-media-stream` bypassed the handler.
  - A real camera on both hosts is still to check (M3, with the user at the PC).
- **CSP reporting can be turned off:** the user asked for a C# option that turns each reporting channel off or limits it to Development builds. Whether Production reports by default is a separate question.

## 2026-11-14, Missing prerequisites: native messages and exit codes (evidence; proposal)

Spike: `spikes/startup-prerequisites/`. The user asked for it; the research review had marked it "Adopt (M1)". Windows dialogs ran only inside Windows Sandbox. Linux ran in Docker (Xvfb with openbox, headless sway) and in WSL with the real journal.

- **Exit codes, same on both OSes:**
  - 0 OK, 2 bad command line;
  - 3 missing, 4 too old, 5 present but broken;
  - 6 no graphical session, 7 OS too old.

  This replaces the earlier prototype's single 3.
- **Windows:**
  - A missing runtime showed its dialog in 46–128 ms, a too-old one in 49–104 ms, a broken one in 71–285 ms.
  - Environment creation succeeds with the browser deleted. Only controller creation fails, with `0x80070002` or `0x800700AA`.
  - The loader's check beats the registry.
  - `[STAThread]` is required, or `0x80010106` blames the runtime.
  - Task dialog with a Common Controls 6 manifest, with a message-box fallback. UIA exposes all its text.
- **Linux:**
  - Every case ended in a readable message, with no crash.
  - The GTK dialog appears when GTK 4 initialises, in 91–212 ms with cairo against 420–820 ms with the GL renderer on a software GPU. Otherwise the order is kdialog, zenity, yad, a D-Bus notification, notify-send, then xmessage.
  - With no display: exit 6 in 6–23 ms.
  - A trial `bwrap` run catches a denied sandbox in 1.5 ms, where WebKit otherwise aborts after 4.4–6.7 s with exit 133.
  - `g_set_prgname` is required.
  - Ubuntu 24.04 and Fedora 43 Workstation have everything; Kubuntu 24.04 lacks WebKitGTK 6.0 and `xdg-dbus-proxy`.
- **Both:**
  - stderr and the system log are always written. On Windows that is a per-user log file, not the event log.
  - A C# hook can replace or suppress the dialog, but not change the exit code.
- **Not tested:** DPI above 100 %, screen readers, the download page opening, older real Windows and Runtimes, real GNOME and KDE.
  - Four checks were added to the plan's table of checks that need the user.
- **For the user:** the exit codes, and the hook's power: can it suppress the dialog entirely?

## 2026-11-14, CSP reporting on in Development and Production (user decision)

- **Decision:** both reporting channels, the injected listener and `report-uri`, are on by default in every build. A C# option turns each one off, or limits reporting to Development.
- **Why:** in a shipped app a violation is a bug or an injection attempt, which is worth logging.
  - The flood limits keep the cost negligible.
  - Reports stay in the local log unless the app turns on OpenTelemetry export.

## 2026-11-14, Cost of one `Result` per stream item (evidence)

Spike: `spikes/stream-result-cost/`, asked by the user before choosing how a stream reports failure. WebView2 157, Node 24, WebKitGTK 2.52 on 6.0; 3 runs per engine.

- **Big loop**, 1M rows in batches of 1,000, against plain items with a hand-written iterator:
  - one `Result` per item adds +3 to +10 ns per row on WebView2, against a 170 ns baseline;
  - +3 to +14 on Node, against 305;
  - +8 to +20 on WebKitGTK, against 320.
  - Against the bridge's ~1.63 µs per row, that is 0.4–0.6 %.
  - Longest task 1.07 ms on WebView2, none over 50 ms.
- **Allocation per row on WebView2:**
  - baseline 243 B;
  - `match` with an inline handler +97 B;
  - shared handler +27 B;
  - `unwrap` +37 B.
- **Slow consumer, window full:** the memory held per buffered row was the same for plain items and for a `Result` made at hand-out (89 B on WebView2). Wrapping at decode adds +36 B.
- **Push at 20,000 events/s:** +0.1 to +0.3 ms of page time per second over the 7.3 ms baseline.
- **The real cost:** async-generator layering, as in the prototype `stream()`, costs +103 ns and +321 B per row on WebView2. The generated client must use a hand-written iterator and wrap items at hand-out.
- **Hybrids:** one `Result` per batch was fastest (−18 ns and −59 B per row) but puts batches into app code. Plain items with a separate `stream.result` lets a loop miss the failure. Neither is clearly better.
- **Skipped:** timer-driven scenarios on WebKitGTK, whose hidden headless view throttles timers.
- **Not tested:** a real bridge, cancellation with `break`, React-like consumers, bundle size.
- **Effect on the decision:** cost does not decide between in-band `Result` items and a throwing iterator.

## 2026-11-14, Stream failures in-band; start-up exit codes; the start-up hook (user decisions)

- **Stream failures arrive in-band.** Every item of a stream or subscription is a `Result`, and a terminal failure is one more item, as in `docs/app-model.md` 6.2.
  - It matches unary calls, and a `for await` loop cannot skip it.
  - `spikes/stream-result-cost` measured the cost at 0.4–0.6 % of the bridge's per-row time.
  - The generated client uses a hand-written iterator and wraps each item when it hands it out.
- **Start-up exit codes, fixed for every app, the same on both OSes:**
  - 0 OK, 2 bad command line;
  - 3 missing, 4 too old, 5 present but broken;
  - 6 no graphical session, 7 OS too old.
- **The start-up hook:** a C# hook runs before Spar's dialog. It may replace the dialog with the app's own message, and suppress Spar's dialog only when it shows its own. stderr and the system log are written regardless, and the exit code cannot change.

## 2026-11-14, `CallError` kinds (user decision)

Every infrastructure failure is a `CallError` with `kind`, `code` and `execution`. Declared errors are never a `CallError`.

| `kind` | When | `execution` | Example codes |
|---|---|---|---|
| `cancelled` | The backend confirmed the cancellation | `notStarted` or `stopped` | `cancelled`, `cancelled-before-start` |
| `unavailable` | The call could not be sent (backend unreachable, host closing) | `notStarted` | `unavailable`, `stopping` |
| `rejected` | The host refused the request before running it | `notStarted` | `invalid-request`, `unknown-command`, `contract-mismatch` |
| `failed` | The command threw, or stopped during shutdown | `unknown` | `failed`, `failed-while-stopping` |
| `transport` | The connection dropped mid-call, or a cancellation was never confirmed | `unknown` | `lost`, `unconfirmed` |
| `response` | The command finished but its result could not be delivered | `completed` | `serialization` (was `response-serialization`) |
| `protocol` | A malformed message, or an error case the SDK does not know | `unknown` | `malformed`, `unknown-error-case` |

- `execution` is `notStarted` only when the host is certain the command did not run, since Spar never retries on its own.
- Still to discuss with the user: the syntax for handling these errors, what the app-wide interceptor does, and how a call opts out of it to handle its own errors.

## 2026-11-14, WebAssembly off on Linux; CSP on every response (evidence)

Spike: `spikes/webkit-wasm-off/`. WebKitGTK 2.52.6 in WSL and 2.54.2 on Fedora 43, WebKit source at `webkitgtk-2.52.6`.

- **Correction:** WebKitGTK enforces `'wasm-unsafe-eval'`. On 2.52 it does so only at instantiation: `compile`, `Module` and streaming compile succeed, and every instantiate path fails. 2.54 also blocks compiling. `spikes/csp-navigation` had tested compile only.
- **The Linux host disables WebAssembly** with libc `setenv("JSC_useWasm", "false", 1)` before GTK and WebKit start.
  - The variable reaches the web and network processes; bubblewrap keeps the environment.
  - `WebAssembly` is then undefined in the page, its workers and frames, even with `'wasm-unsafe-eval'`.
  - It had no measurable cost: 146.0 against 149.7 ms median from host start to first page script, and the same JS time.
  - The C# opt-in adds `'wasm-unsafe-eval'` and sets `JSC_useWasm=true` explicitly.
  - `useWasm` needs WebKitGTK 2.48 or later.
  - A real-host test asserts `typeof WebAssembly` both ways, in the page and in a worker.
- **The CSP goes on every host-served response, worker scripts included.** A worker served without the header escaped the page's policy (eval and WebAssembly ran). WebView2 and the Vite plugin are to check in M1.
- **Other routes:**
  - No `WebKitSettings` switch exists.
  - A user script deleting `WebAssembly` missed workers and some frames.
  - A web-process extension works, but means shipping a native `.so`. It stays the fallback.
- **Remaining difference:** on WebView2 the global exists and instantiation throws; on Linux the global is absent. `--js-flags=--noexpose_wasm` on WebView2 might match; untested.
- **Not tested:**
  - the Spar NativeAOT host;
  - shared and service workers;
  - web-process restarts;
  - Windows.

## 2026-11-14, Failure handling and the interceptor (user decision)

- **`failure` in `match`:**
  - a function handles every failure;
  - an object keyed by `CallError` kind handles only the kinds it lists. A misspelled kind fails `tsc` (TS2322).
- **The interceptor `onFailure`** receives the failures no call handled, and `match` returns `undefined` for them. The return type says so: a `failure` object or a missing `failure` adds `undefined`, a function does not.
- **`unwrap`** throws `SparError` and never reaches the interceptor.
- **`observe`** is a separate hook that sees every failure, handled or not, for logs and telemetry.
- **Limits:** neither hook can retry, change a result or turn a failure into a success. Stream items follow the same rules.
- **Evidence:** `spikes/app-model-check` rerun with the new SDK prototype.
  - TypeScript 7.0.2 and 6.0.3: 0 errors on the document's examples.
  - 9 of 9 deliberate mistakes rejected.
  - 11 of 11 run-time routing checks passed.
  - `docs/app-model.md` 6.1 shows the new forms.

## 2026-11-14, Resource usage: counters and sampling (evidence; proposal)

Spike: `spikes/resource-usage/`, NativeAOT hosts with 0 warnings (Windows 2.9 MB, Linux 2.7 MB). WebView2 157 and WebKitGTK 2.52 on 6.0.

- **Proposed measure, "private memory":** `PrivateWorkingSetSize` on Windows, which matched Task Manager in 25 of 25 processes, and `statm` resident − shared on Linux, which is about `Private_Dirty`.
  - Small page: 88.5 MB on Windows, 70.7 MB on Linux, reported as an app total with engine and app-process parts.
- **Cost:**
  - Windows: listing processes takes 29 µs and works on the STA only. Memory with kept handles takes 2.5 µs on any thread.
  - Linux: finding the tree takes 107 µs, a `statm` sample 4.6 µs.
  - 0 bytes allocated per tick on both.
  - `Process.PrivateMemorySize64` is slow on Windows (3.2 ms) and wrong on Linux (a ~70 GB virtual size).
- **Disk:**
  - Folder walks took 0.2–1.2 ms for realistic and large profiles, and 3.4–19 ms for 20,000 files.
  - WebKitGTK's data-manager `fetch` gives per-type sizes in 1.7 ms, 81–84 % of the walk.
- **Sampling, proposed:**
  - memory 1 s by default with a 250 ms floor;
  - disk 30 s with a 5 s floor, or on request;
  - push only on a change over 1 % or 1 MiB.
- **For the user:** a "coalesced" subscription option (latest value wins, no `gap` events).
- **User checks added:** Task Manager's total, and GNOME and KDE System Monitor.

## 2026-11-14, Coalesced subscriptions (user decision)

- **Decision:** a subscription can be declared coalesced, for example `[SparSubscription(Coalesce = true)]`; the exact name is settled in M3.
  - It keeps only the newest value and replaces older ones silently, with no `gap` events.
  - The page always ends on the newest value, and a resumed subscription starts with the current one.
  - `Capacity` is not allowed with it; a contract diagnostic reports the combination.
- **Why:** live resource-usage samples (`spikes/resource-usage`), progress and status only matter as their latest value. With the normal drop-oldest buffer, each dropped sample would raise a `gap` event, which is noise here.
- **App model:** `docs/app-model.md` gets an example when the resource-usage contract is designed.

## 2026-11-14, MSBuild and SDK warnings fail the build (user decision)

- **Repository:** the root `Directory.Build.props` sets `MSBuildTreatWarningsAsErrors` for every project outside `spikes/`, so `MSB*`, `NETSDK*` and `NU*` warnings fail like compiler warnings.
  - Verified: a `Warning` task with code MSB9999 failed a build under `tests/` (exit 1, "error MSB9999") and stayed a warning under `spikes/` (exit 0).
  - The probe projects were deleted.
  - Exceptions go in `MSBuildWarningsNotAsErrors`, agreed with the user ID by ID, with their reason.
- **App template:** `dotnet new spar-app` sets it too, so new apps start strict and can turn it off in their project file.
- **Not in `Spar.Sdk`:** forcing it on apps would let a new NuGet advisory warning break existing builds.
- **Why:** `TreatWarningsAsErrors` covers compiler warnings only. NETSDK1229 passed every build in `spikes/cswinrt`, and MSB4287 (relative-path writes under `-mt`) would also pass.

## 2026-11-14, App-specific settings stay in the app (user decision)

- **Decision:** what SqlClient needs is the SQL Server tool's choice, not a Spar default or assumption:
  - `InvariantGlobalization=false`;
  - the single-file networking switch and the SNI package exclusion;
  - the AOT warnings it accepts.
- **Spar's defaults:** `InvariantGlobalization=true`, the NativeAOT default, and no accepted warnings.
- **For apps:** an app that needs a different setting puts it in its own project file. Spar documents the SqlClient settings as a recipe and tests them in M1 ("Runtime choices"), but never sets them.
- The same holds for every other dependency of a single app. `AGENTS.md` (consumer model) now says so.

## 2026-11-14, CS8509 stays the app's setting; a Spar rule for `Result` switches (user decision)

- **Decision:** `Spar.Sdk` does not raise CS8509 in apps.
  - The template's `TreatWarningsAsErrors` already makes it an error.
  - An app that turns that off has chosen to, which matches the rule that Spar imposes nothing beyond its defaults.
- **Added: SPAR055 (error, category `Spar.Results`).** It reports a `switch` expression over a `Result`, or over a Spar contract's `closed` error hierarchy, that misses a case.
  - It appears next to CS8509, so it stays an error in apps without `TreatWarningsAsErrors`. That means two diagnostics at one place, the trade-off already accepted for SPAR053.
  - Not yet prototyped.
  - Whether it also covers `switch` statements without `default`, which silently skip a missing case, is decided when it is built.
- **Why:** a missing case otherwise surfaces only at run time, as `SwitchExpressionException`, when a library adds an error case.

## 2026-11-14, Wrong `Host` on loopback: 403 (user decision)

- **Decision:** the loopback host and the devtools endpoint answer a request whose `Host` is not the loopback address or `localhost` with the right port with **403** and `forbidden-host` in the body. This is the DNS-rebinding check.
- **Why:**
  - Vite's dev server answers 403 for a host it does not allow, so Debug and the Spar host match.
  - 421 Misdirected Request can make a browser retry on a new connection, where a plain refusal is wanted.
  - `spikes/csp-navigation` had used 421 and `spikes/d8-automation` 403.

## 2026-11-14, Loopback token in the URL fragment (user decision)

- **Decision:** a loopback page served without Vite gets its per-run token in the URL fragment (`#token=…`).
  - The host publishes the URL on its ready line and in the devtools lockfile, and writes the token nowhere else.
  - The SDK moves it to `sessionStorage` and removes it with `history.replaceState`.
  - Through Vite, the dev server's proxy still adds it.
- **Why:** a fragment never reaches the server, a `Referer` or a log. The token's job is to keep other sites and local processes out. Page script can already call the bridge, so hiding the token from it gains nothing.
- **Rejected:**
  - An `HttpOnly` cookie: cookies are not separated by port, so parallel hosts would overwrite each other and other local servers would receive it.
  - The token in the served HTML: anyone reaching the port would get it.

## 2026-11-14, Debug builds type-check the frontend (user decision)

- **Decision:** the TypeScript type-check runs in Debug too, with an opt-out property (`SparTypeCheck=false`; the name is confirmed in M1).
  - It runs only when the generated client or the TypeScript sources changed since the last check, and reports errors at file and line.
  - The app's `tsconfig` decides how strict it is.
- **Why:**
  - A contract rename then breaks `dotnet build` at the TypeScript call sites, even in files nobody opened.
  - An agent gets that feedback from a plain build.
  - The measured cost was about 1 s of a contract restart (`spikes/react`).

## 2026-11-14, Trusted input in Linux desktop tests: XTEST (user decision)

- **Decision:** Linux desktop tests force `GDK_BACKEND=x11` and send trusted input through XTEST. In CI they run on Xvfb in a container.
- **Why:**
  - XTEST under XWayland delivered trusted Tab and clicks on GTK3 and GTK4 (`spikes/linux-wsl-desktop`), and Wayland offers no equivalent.
  - WebKitWebDriver is packaged only for 4.1, which Spar no longer uses, and opens a TCP port.
- **Design rule:** the test framework sends input through one interface, so a Wayland route can be added later. Wayland input stays in the table of checks that need the user.

## 2026-11-14, The contract snapshot is committed (user decision)

- **Decision:** the build writes `<Project>.contracts.snapshot` next to the project. It is committed, with LF endings through `.gitattributes`.
  - Local builds update it when the contracts change (`Update`).
  - CI fails with SPAR100 when it is stale (`Verify`, the default under `ContinuousIntegrationBuild`).
- **Why:**
  - Contract changes show up as readable diffs in review, including indirect ones.
  - CI catches a forgotten update.
  - Agents can read the whole contract surface in one file.
  - `spikes/d1-pipeline`: a DTO field addition was a one-line diff, and reverting restored the file byte for byte.
- **The only exception:** it is the one stated exception to "generated files live under `obj/`". `AGENTS.md` now names it.
- **M1 defect:** the contract fingerprint covers only type names. It must cover full wire shapes, so an old page gets the 409 mismatch after a field change.

## 2026-11-14, Microsoft DI (user decision)

- **Decision:** Spar uses `Microsoft.Extensions.DependencyInjection` for the app's services and the per-command scope.
- **Why:**
  - `AddLogging`, `AddOptions`, `AddHttpClient`, OpenTelemetry and most .NET libraries register through `IServiceCollection`.
  - App code stays standard (`spar.Services.AddScoped<…>()`).
  - It is AOT-compatible.
  - Its cost is small next to the web engine: +300,032 bytes beside the Spar host and +875,008 without it (`spikes/d1-pipeline`), and about 30 µs more for the first resolve than a custom container (from earlier research).
- **Rejected for now:** a generated container. It would reimplement scopes, disposal and lifetimes and still need an `IServiceCollection` adapter. It can be revisited behind the same `spar.Services` API.

## 2026-11-14, Wire format choices (user decision)

The nine open choices from `spikes/wire-details`. Each was re-checked so that it holds on grounds beyond SQL Server, which stays evidence of real data but not a reason for a Spar rule.

1. **`long`/`ulong`:** a canonical decimal string on the wire, `bigint` in TypeScript. As numbers, 603 of 100,000 random 64-bit ids survived `JSON.parse`. `Int128`, `UInt128` and `BigInteger` join this family when an app needs them.
2. **Lone surrogates:** replaced by U+FFFD in both directions, never an error.
   - .NET JSON writers replace them, WebView2 turns a host one into U+FFFD and rejects a page one (`E_INVALIDARG`), and `fetch` text encoding replaces them.
   - Only replacement behaves the same on every transport.
3. **Enums:** the member name, exact match. `[Flags]` gets a contract diagnostic for now.
4. **`byte[]`:** padded base64, `Uint8Array` in TypeScript.
5. **`TimeSpan`:** the constant format `[-][d.]hh:mm:ss[.fffffff]`.
6. **`DateTime`:** written as System.Text.Json writes it (by `Kind`). A contract warning suggests `DateTimeOffset`.
7. **SQL `decimal(38)`:** no SQL-specific wire type. Apps that need such values send `string`.
8. **A stream's writer failure:** `"execution":"stopped","itemsSent":N`.
9. **Skipped unknown properties:** their strings are not validated. They never reach the app, and System.Text.Json does not validate them either.

## 2026-11-14, Streams may declare errors (user decision)

- **Decision:** a stream may return `IAsyncEnumerable<Result<T, TError>>`.
  - It yields values. Yielding a declared error ends it, and Spar then disposes the iterator.
  - A contract diagnostic flags code that yields after an error.
- **In TypeScript:** each item is already a `Result`, so the loop's `match` gains an `errors` object with one handler per declared case, and a missing case fails `tsc` as for unary calls.
- **On the wire:** nothing new; the last item carries the error as an ordinary error outcome.
- **Why:** an expected stop, such as a source table dropped mid-copy, gets a typed case the compiler checks, instead of a generic `failed`.
- **Still open:** an `events` slot for subscriptions.

## 2026-11-14, The `events` slot for subscriptions (user decision)

- **Decision:** a subscription item's `match` takes `success` for the app's values and an `events` object for status events, besides `errors` and `failure`.
  - `reset` is required; `gap`, `reconnecting` and `resumed` are optional.
  - Each subscription offers only the events it can emit: a coalesced one has no `gap`.
  - The generated type is `Subscription<T>`.
- **Replaces:** the union inside `success` that app code had to `switch` on, which never forced anyone to handle `reset`.
- **Evidence** (`spikes/app-model-check`, `docs/app-model.md` 6.2 rewritten):
  - TypeScript 7.0.2 and 6.0.3: 0 errors on the examples.
  - 12 of 12 deliberate mistakes rejected, three of them new: a missing `reset` (TS2322, whose elaboration names `reset`), a misspelled event, a missing `events` object.
  - 14 of 14 run-time routing checks pass: events reach their handler, an unhandled optional event gives `undefined`, and events never reach the failure hooks.

## 2026-11-14, Theme and splash choices (user decision)

The six open choices of plan 3d (`spikes/theme-startup`, 0 wrong frames on all three hosts with the host layers):

1. **Showing:** the window shows at once in the theme colour, never cloaked. It is visible in 1–5 ms on Windows and 120–190 ms on GTK4, while a cloaked slow start showed nothing for over a second.
2. **WebView2 background:** transparent, the only setting with 0 wrong frames in every case. `#121212` is not imposed on apps. M1 measures the cost.
3. **Splash delay:** it runs from the page's first frame, through a CSS animation delay, with no JS.
4. **Blocked rendering:** the SDK requests an animation frame at bundle start. That fixed the minimum-visible count in 12 of 12 runs, and M1 confirms the mechanism.
5. **Live changes:** at most one mixed frame is accepted (the title bar was one frame ahead in 3 of 12 switches).
6. **GTK theme name:** none is set. GTK4 follows the dark preference, and setting a name would override the user's theme.

## 2026-11-14, Debug serves Vite through the app's own origin (user decision)

- **Decision:** by default, Debug serves the page as `https://app.localhost` on Windows and `spar://app` on Linux. The host fetches each request from the Vite dev server in-process. Serving straight from Vite stays an option.
- **Why:** Debug then shares Release's origin, storage, CSP and secure-context behaviour, which matters with Trusted Types, SRI and the strict CSP on by default.
- **Cost** (`spikes/desktop-origin`):
  - Fast Refresh 23 against 25 ms, with state kept 5 of 5 times;
  - about 35 ms more per full page load (153 against 117 ms).
- **What proxies:** the host's own request interception (`WebResourceRequested` with a deferral on Windows, the scheme handler on Linux), with `HttpClient` to Vite's loopback URL. No proxy server or extra package. The HMR WebSocket goes to Vite directly. Development-variant code only.
- **Rules already recorded:**
  - forward `Accept` and `Sec-Fetch-*`;
  - pass `Content-Type`;
  - never cache;
  - drop `ETag` and `If-None-Match` for HTML;
  - `HttpClient` with `UseProxy=false` and no automatic decompression.
- **M1 checks:** Vite's nonce through the proxy, and the Linux path.

## 2026-11-14, No default deadlines (user decision)

- **Decision:** calls and streams have no default deadline and no default idle timeout.
  - The page sets one with `AbortSignal.timeout(ms)` as the call's `signal`; no new API is needed.
  - C# may set an optional backend limit, for example `[SparCommand(Timeout = …)]` (name settled in M1), folded into the command's token.
  - Both end like any cancellation: `cancelled` only when the backend confirms, otherwise outcome unknown, never retried.
- **Why:** an earlier attempt's 15 s default forced `timeoutMs: 0` on calls that wait on a person (pickers). Long copies would hit it too, and an idle timeout would cut quiet but healthy subscriptions.

## 2026-11-14, Heartbeat for hung pages (user decision, pending a spike)

- **Decision:** the host pings the page over the bridge every 5 s while a document is loaded and the window is visible. Three missed answers mark it unresponsive: a log entry, a C# hook, and by default a native "Wait / Reload page" dialog.
  - No automatic kill.
  - At most one automatic reload, only after a document had loaded.
  - Backend commands are never replayed.
- **Why:** an idle hung page raises no event on WebView2 (earlier research: nothing in 60 s).
- **Pending:** `spikes/page-heartbeat` checks detection, false alarms when minimised, covered or busy, WebKitGTK's `is-web-process-responsive`, and the intervals.

## 2026-11-14, Per-window command allow-lists are optional (user decision)

- **Decision:** every window may call every command by default. A window can declare an allow-list of contracts or commands in C#. A call outside it is refused before running, with `kind: "rejected"`, `code: "forbidden-command"`, `execution: "notStarted"`.
- **Why:** with one origin and the navigation lock, every window runs the app's own code. A required list would add configuration without removing a risk. The documentation recommends a list for windows that show content the app does not fully control.

## 2026-11-14, Hot Reload signal to the page, with a toast (user decision)

- **Decision:** in Debug, after each applied C# Hot Reload edit, the host's `MetadataUpdateHandler` sends `backend-updated` (revision, time, updated type names) to open pages.
  - The SDK exposes `onBackendUpdate(handler)`. In Release it is a no-op that the bundler removes.
  - The user added a toast: Spar's development overlay shows a small, dismissable toast for each update by default. An app can turn it off or show its own.
- **Limits:**
  - The event can follow the first run of the patched code.
  - Rejected edits stay in `dotnet watch`'s terminal, since it has no hook into the app process.
  - Contract and DTO edits still restart.

## 2026-11-14, Checks on the generated client (user decision)

- **Contract fingerprint** in the generated client: kept, as planned. It covers full wire shapes from M1.
- **Bundle budget:** optional and off by default (`SparBundleBudget`, name confirmed in M1). The build warns or fails when the compressed frontend exceeds it.
- **No licence check built in.** Licence policy belongs to each app or company. The docs point to existing tools.

## 2026-11-14, Test lessons from the spikes (evidence)

At the user's request, three read-only reviews went through 47 spikes for their testing lessons; `page-heartbeat` was still running. The summary is in `docs/evidence/test-framework-review/README.md`, and the resulting proposal in plan section 5, "Test framework proposal".

- **Most common lessons:**
  - timing noise and stalls;
  - explicit readiness and process lifecycle;
  - offscreen shown windows;
  - profiles and process ownership;
  - isolation from the user's machine;
  - checks that printed instead of asserting;
  - exact expected-diagnostic sets;
  - differential oracles;
  - JIT/AOT parity;
  - fakes and seams;
  - racy waits;
  - production-boundary checks done only by string scans.
- **Open, for the user one at a time:**
  - the managed level's in-process factory;
  - the package split (`Spar.Testing`, `@spar/app/testing`);
  - frontend tests in Node or in real engines;
  - selectors and scenes;
  - sharing a browser across loopback tests;
  - Linux desktop tests on Windows machines (Docker or WSLg);
  - publishing the Linux test image;
  - hidden-page throttling;
  - data fixtures;
  - shipping the Sandbox runner;
  - performance baselines in CI;
  - the fuzz budget;
  - the analyzer marker syntax;
  - headless IDE probes.

## 2026-11-14, The in-process factory is the default managed test level (user decision)

- **Decision:** managed tests run the app's real `Main` in the test process through the in-process factory: service overrides, real serialization and dispatch, no window. Its hooks exist only in the test variant.
- **Why:** 20–27 ms per test warm (`spikes/react`), against about 600 ms for launching the exe (`spikes/loopback`).
- **Known limit:** tests share the process's console, environment, current directory and statics. How the framework keeps app code off those globals is discussed next. Tests that need their own process use the launched-app fixture.

## 2026-11-14, In-process factory approval withdrawn (user)

- The user withdrew the approval recorded above ("The in-process factory is the default managed test level") the same day. They want to see the full list of test types, for app authors and for Spar itself, before deciding.
- The question is open again in the plan.

## 2026-11-14, Test types listed with their open issues (user request)

- Plan section 5 now lists every test type, for app authors and for Spar, with what it checks, what runs it, its speed and what Spar provides.
- Eleven open issues (A–K) are flagged, among them:
  - the managed level's form;
  - where page tests run;
  - replacing services in end-to-end tests without touching real data;
  - desktop specifics;
  - performance baselines.
- The user wants these investigated further, with more spikes where needed, before the test framework is decided. No test-framework point is decided yet; the in-process factory approval stays withdrawn.

## 2026-11-14, Documentation site and skills plugin (user decision)

- **Site:** Astro Starlight, published to GitHub Pages from CI on each release.
  - The pages are plain Markdown in the repository.
  - The TypeScript API reference comes from TypeDoc. The C# reference comes from the XML doc comments, through DocFX's Markdown output (to verify) or a small Spar generator.
  - Examples are compiled, like `docs/app-model.md`.
  - Rejected: DocFX as the site (best C# reference, plainer look) and VitePress (similar, Vue-based).
- **Skills plugin:** a marketplace in this repository, with the plugin in `plugins/spar/` and the marketplace file at the root.
  - It is versioned and released with the SDK.
  - It holds the app-author skills. `.claude/skills/` stays for work on Spar itself.

## 2026-11-14, Custom title bar API (user decision)

- **C#:** `spar.Window(w => w.TitleBar(TitleBar.Custom))`. `TitleBar.System` is the default.
- **Page:** markup only, the same on both OSes and in every framework.
  - `data-spar-titlebar` marks the drag area. Its interactive children stay clickable, and `data-spar-no-drag` covers any other element.
  - `data-spar-caption`: empty for the SDK's standard buttons (accessible names, translatable labels, CSS variables), or `minimize`, `maximize` or `close` on the app's own buttons.
  - The SDK posts the layout to the host, and sets `data-spar-window` to `maximized` or `normal` on `<html>`.
  - The host handles double-click and the system menu.
- **Example and check:** `docs/app-model.md` 6.4 and the `Program.cs` block, checked by `spikes/app-model-check`:
  - C# builds with 0 warnings;
  - all 9 blocks verbatim;
  - TypeScript 7 and 6 with 0 errors;
  - 12 of 12 mistakes rejected.

## 2026-11-14, Tray icon API (user decision)

- **Declared in `SparApplication.Create`:** `spar.Tray(t => …)` with icon, tooltip, `OnActivate` (default `TrayAction.ShowWindow`) and a menu.
  - Menu items run a built-in action (`ShowWindow`, `Quit` through the normal shutdown) or a handler that gets a service from DI.
  - Handler items carry an id, so they can be changed later. The id was added while writing the checked example.
- **Changes at run time:** app services use the injectable `ITray` (tooltip, icon, checked and enabled items, `IsAvailable`). The page reaches the tray only through the app's own contracts.
- **Close to tray:** `w.CloseToTray()` hides the window on close.
- **Linux safety:**
  - A diagnostic warns when the `OnActivate` action is not also in the menu, since some hosts send no clicks.
  - Where no tray exists, `CloseToTray` minimises so the window stays reachable.
- **Check:** `docs/app-model.md` 3.5 and the `Program.cs` block, checked by `spikes/app-model-check`:
  - C# builds with 0 warnings, and the run prints the configured tray;
  - 10 of 10 blocks verbatim;
  - TypeScript 7 and 6 clean.
  - The check also caught a name clash (`Monitor` with `System.Threading.Monitor`), fixed by naming the class `InstanceMonitor`.

## 2026-11-14, Window placement API (user decision)

- **Declared:** `w.Size(w, h)` sets the first start, centred on the primary screen, with `w.MinSize(w, h)`.
- **Remembered:** `w.RememberPlacement()` is the default for the main window. It saves size, position, maximized state and screen in the app's data folder and restores them on a visible screen.
  - Wayland restores size and maximized state only, because it gives apps no position (`spikes/linux-integration`).
  - `RememberPlacement(false)` turns it off.
- **Run time:** the injectable `IWindows` (`Maximize`, `Restore`, `SetSize`, `Focus`). The page reaches it only through the app's own contracts.
- **Tests** start from the declared size, because each run has a fresh data root.
- **Check:** `docs/app-model.md` (`Program.cs` and 3.5), checked by `spikes/app-model-check`: C# 0 warnings, 10 blocks verbatim, and the run prints the configured placement.

## 2026-11-14, Choosing target platforms: `SparPlatforms` (user decision)

- **Decision:** an app picks its hosts with one property, `<SparPlatforms>windows;linux</SparPlatforms>` (or one of the two). Both is the default, and the template's `--platforms` option writes it.
- **What the SDK does:**
  - It adds `Spar.Windows` and `Spar.Linux` at its own version; apps never reference host packages directly.
  - It maps each platform to its runtime identifiers.
  - NativeAOT trims the other host from each executable.
- **Errors:** running on a platform the app does not target stops with a clear error naming the property.
- **Why a property and not package references:** it is the same idea the user suggested, but without versions to keep in step.
- **Limit:** NativeAOT publishes each executable on its own OS (or in a container of it).

## 2026-11-14, File dialog API on `Result` (user decision)

- **Decision:** opening a file returns `Result<FileGrant, FileDialogError>`, from C# (`IFileDialogs.OpenAsync`) and from the page (`files.open()`, a normal `Call`). The user asked for a declared error instead of `null`.
  - The dialog's cases: `Dismissed` (named so it is not confused with the `cancelled` `CallError` of an aborted call), `AccessDenied`, `Unavailable` (no portal or display) and `Busy` (a dialog already open for the window).
  - Reads and writes have their own cases, `FileAccessError`: `NotFound`, `AccessDenied`, `InUse`, `TooLarge`, `GrantExpired`.
  - Saves follow the same pattern, through a temporary file and a replace.
  - A command can take a `FileGrant` parameter. The page sees only the file name.
- **Check:** `docs/app-model.md` 3.6, run by `spikes/app-model-check`:
  - C# 0 warnings; 12 of 12 blocks verbatim; TypeScript 7 and 6 clean.
  - A `switch` missing `Dismissed` fails with CS8509 naming `Spar.FileDialogError.Dismissed`.
  - A page `match` without `dismissed` fails with TS2741 (mistake 13 of 13).

## 2026-11-14, References and provenance review before the first commit (user request)

- Before the first commit, the user wants:
  - every reference to other frameworks and earlier Spar versions reviewed, to decide which to keep;
  - a check that no code was copied from earlier Spar versions or other frameworks.
- Added as M0 close-out step 9, with a method: a list of the mentions, then a similarity scan across the trees with a manual look at the hits. The results go into this log before the squash and first push.

## 2026-11-14, References and provenance review before the first commit (user request)

- Before the first commit, the user wants:
  - every reference to other frameworks and earlier Spar versions reviewed, to decide which to keep;
  - a check that no code was copied from earlier Spar versions or other frameworks.
- Added as M0 close-out step 9, with a method: a list of the mentions, then a similarity scan across the trees with a manual look at the hits. The results go into this log before the squash and first push.

## 2026-11-14, Scope of the references and provenance review (user clarification)

The user clarified the review in M0 close-out step 9:
- **May stay:**
  - references to official documentation;
  - references to earlier Spar versions, unless what they describe came from another framework.
- **Other frameworks** (Tauri and the others):
  - no code copied from them;
  - every idea taken from them is reframed as a Spar requirement in Spar's own words, so it is not identifiable as coming from elsewhere;
  - names and attributions are dropped.
- **Still to confirm:** whether this also covers the design records (`docs/plan.md`, `docs/decisions.md`, `docs/research-review.md`, `docs/app-model.md`), which `AGENTS.md` exempts today.

## 2026-11-14, Other frameworks in the repository (user decision)

- **Licences:** Tauri is MIT and Apache-2.0, Wails MIT (from registry pages, to verify against the official repositories), Electron MIT.
- **Mentions found** (a search excluding `node_modules`, `obj`, `bin` and `artifacts`):
  - Electron, only as a measured reference in `spikes/linux-memory`, with its numbers carried into the plan, this log and the pitfalls;
  - webview/webview and saucer, once each in the WebKitGTK entry.
  - VS Code's `ELECTRON_RUN_AS_NODE` in two TypeScript spikes is tool usage, not a framework reference.
- **Decision:** these mentions stay in the design records as evidence. They never appear in user-facing documents.

## 2026-11-14, Folder picker API (user decision)

- **Decision:** `IFileDialogs.OpenFolderAsync` (C#) and `files.openFolder()` (page) return `Result<FolderGrant, FileDialogError>`, with the same dialog cases as files.
- **The grant:** it declares `Read` (default) or `ReadWrite`, and offers:
  - list;
  - read;
  - a safe write (temporary file, then replace);
  - create folder;
  - delete, with write access only.
- **Path rules:** every path is relative to the folder. Absolute paths, `..` and links leaving it give `FileAccessError.OutsideFolder`, checked on the final resolved path. A write through a read grant gives `ReadOnlyGrant`. The page sees only names.
- **Left out of the first version:** remembering a folder across restarts. It would be an explicit C# opt-in, re-checked on use.
- **Check:** `docs/app-model.md` 3.6 (`ExportService.cs`, `listFolder` in `notes.ts`), run by `spikes/app-model-check`:
  - C# builds with 0 warnings;
  - 13 of 13 blocks verbatim;
  - TypeScript 7 and 6 clean.
- **Not measured:** the Linux portal's folder path (M3).

## 2026-11-14, Drag and drop API (user decision)

- **Off by default.** Dropping files outside a declared area does nothing, and the navigation lock stops the browser from opening a dropped file.
- **Drop areas:** the page declares them with `files.dropTarget(element, { accept, folders, limit }, onDrop)`, which returns a function that removes the area.
  - `onDrop` gets `Result<DroppedItems, DropError>`: file grants identical to the dialog's, and read-only folder grants with `folders: true`.
  - A drop grants nothing on the parent folder. The error cases are `notAccepted` and `tooMany`.
  - While a valid drag hovers, the SDK sets `data-spar-drop="over"`.
  - Drag and drop inside the page keeps working.
- **Not verified:** how each host receives the files (WebView2's `postMessageWithAdditionalObjects` giving real paths to the host, a GTK `text/uri-list` drop target on WebKitGTK). M3 checks it. Real drags need the user at the PC.
- **Check:** `docs/app-model.md` 3.6 (`drop.ts`): 14 of 14 blocks verbatim, TypeScript 7 and 6 clean.

## 2026-11-14, Real input checks can be automated (user decision)

- **Decision:** checks that need real input (mouse, keyboard, drag and drop, focus) no longer require the user at the PC.
  - They may be simulated, or driven in Windows Sandbox, a Linux container or VM, or WSL.
  - On the user's own desktop, through computer use or injected input, the agent first asks whether the user is using the monitor, and goes ahead only after they say no.
- **Unchanged:** sound, camera and microphone, changes to the user's settings, and anything needing the user's judgement still wait for the user.
- **Updated:** `AGENTS.md` (Evidence) and the plan's table introduction.

## 2026-11-14, Clipboard API (user decision)

- **Page:** the web's own `navigator.clipboard`, with no Spar API.
  - Writing is allowed.
  - Reading needs `Permission.ClipboardRead` declared in C#, which sets both the header and the host's permission gate. Undeclared reads are refused on both hosts.
- **Backend:** an injectable `IClipboard`: `SetTextAsync` returns `Result<ClipboardError>`, `GetTextAsync` returns `Result<string, ClipboardError>`. The cases are `Busy` (after a short retry), `TooLarge` and `Unavailable`.
- **Text only in the first version.** Images and other formats come later, if needed.
- **Check:** `docs/app-model.md` 3.7 (`ScriptService.cs`): C# builds with 0 warnings, 15 of 15 blocks verbatim.
- **Not tested:** real copy and paste, which needs a focused window. It runs in Windows Sandbox or a Linux VM.

## 2026-11-14, Keyboard shortcuts added to the native features (user request)

- The user asked to remember keyboard shortcuts for Spar apps. They are now in the plan's native-feature list, to design together with the menus:
  - shortcuts inside the app's windows, declared once and shown in menus;
  - how they coexist with the page's key handling and the browser accelerators that are off by default;
  - platform conventions;
  - whether system-wide shortcuts are ever offered. They were marked "ignore", and work only on X11 on Linux.

## 2026-11-14, More native features on the list (user decision)

The user asked which desktop features were still missing and added both groups to the plan's native-feature list (section 3, "Native services"). Each is designed with the user like the others.
- **Soon:**
  - context menus;
  - JavaScript dialogs;
  - opening files and folders outside the app;
  - taskbar progress and badges;
  - power and session events;
  - secondary and modal windows;
  - zoom and find in page;
  - screen-capture protection;
  - network status;
  - logs and crash reports for users.
- **Later:**
  - automatic updates and relaunch;
  - installers;
  - localisation;
  - starting at login;
  - recent documents and jump lists;
  - always on top, fullscreen and kiosk;
  - system-wide shortcuts;
  - spellcheck;
  - sounds.

## 2026-11-14, Printing and print-to-PDF API (user decision)

- **Page:**
  - `print.show()` opens the system print dialog (WebView2's own, GTK's on Linux).
  - `print.toPdf(options)` asks where to save through the save dialog and writes safely (temporary file, then replace).
  - Both are `Call`s with declared errors, and `window.print()` does the same as `show()`.
- **C#:** an injectable `IPrinting.ToPdfAsync(options, folderGrant, fileName, ct)` writes the page as a PDF into a grant without a dialog, returning `Result<FileGrant, PrintError>`.
- **The cases:** `Dismissed`, `NoPrinter`, `Unavailable`, `Failed`, plus the file cases when writing.
- **Scope of the first version:** the current page, styled with `@media print`. Printing another document through a hidden window comes later, if needed.
- **Check:** `docs/app-model.md` 3.8 (`printing.ts`, C# stubs):
  - C# builds with 0 warnings;
  - 16 of 16 blocks verbatim;
  - TypeScript 7 and 6 clean;
  - 13 of 13 mistakes rejected.

## 2026-11-14, Resource usage API (user decision)

- **Decision:** an injectable `IResourceUsage`:
  - `GetMemoryAsync` reads once.
  - `WatchMemory(interval)` samples off the UI thread while enumerated. It pushes only changes over 1 % or 1 MiB, with a 250 ms minimum interval.
  - `GetDiskAsync` walks the data folders on request and returns `Result<DiskUsage, FileAccessError>`.
- **The data:** `MemoryUsage` is private memory, the same meaning on both OSes (`spikes/resource-usage`). It holds the app total, the engine with its GPU process, the app process, and each engine process by kind. `DiskUsage` holds the profile, app data and cache sizes.
- **Exposure:** the page sees it only through a contract the app writes. The example uses a coalesced subscription for the status bar and a command for the settings page. Spar ships no ready-made contract, so what the page can see stays explicit.
- **Check:** `docs/app-model.md` 3.9 (`StatusService.cs`): C# builds with 0 warnings, 17 of 17 blocks verbatim.

## 2026-11-14, Actions, context menus and shortcuts (user decision)

- **Actions** are declared once, in one object: `actions.define({ copyRow: { label, shortcut, run }, … })`. Menus reference the returned action objects, not strings.
  - The user asked what happens with duplicates, which led to this typed form:
    - a duplicate id in one `define` is TS1117;
    - a misspelled action is TS2551;
    - a second `define` of an existing id throws at once, naming both;
    - the same shortcut on two app-wide actions throws at the second definition;
    - the same label twice is allowed, with a Development warning when one menu shows it twice.
  - Scoped actions (`actions.scoped`, disposed with their view) may shadow app-wide shortcuts while active.
- **Context menus:** native, from `menus.show(event, items)` with actions, separators and inline items. It is a `Call` whose only case is `dismissed`. WebView's default menu is off, and text fields keep a minimal native Cut/Copy/Paste/Select all.
- **Shortcuts:**
  - They work while the window has focus, on both hosts.
  - Plain editing keys go to inputs while the user types.
  - Browser accelerators stay off.
  - C# actions share the same ids with the tray and page menus. A duplicate id fails at startup, and an analyzer reports constant ids.
- **Not in the first version:** a native menu bar, and system-wide shortcuts.
- **Check:** `docs/app-model.md` 6.4 (`actions.ts`); the title bar moved to 6.5.
  - 18 of 18 blocks verbatim; C# builds with 0 warnings.
  - TypeScript 7 and 6 clean, and 15 of 15 mistakes rejected, two of them new: TS1117 and TS2551.

## 2026-11-14, JavaScript and native dialogs (user decision)

- **Browser dialogs:** `alert`, `confirm` and `prompt` are taken over by the host (`ScriptDialogOpening` on WebView2, `script-dialog` on WebKitGTK). They are shown as the OS's message box, owned by the window, with the app's name as title, and answered through a deferral so the window's thread never blocks.
- **New code:** `dialogs.message({ title, text, buttons, default })`, a `Call` typed by the button names, whose only error case is `dismissed`. It is a task dialog on Windows and `GtkAlertDialog` on Linux. C# gets the same through an injectable `IDialogs`.
- **`beforeunload`** never shows a browser prompt; closing goes through the two-stage close.
- **`prompt()`** becomes a small native input box; the documentation recommends a page form.
- **Check:** `docs/app-model.md` 6.5 (`confirmDelete.ts`); the title bar moved to 6.6.
  - 19 of 19 blocks verbatim; C# builds with 0 warnings.
  - TypeScript 7 and 6 clean, and 16 of 16 mistakes rejected. The new one: a default that is not one of the buttons, TS2322.

## 2026-11-14, Opening links and files outside the app (user decision)

- **Links:**
  - An ordinary `target="_blank"` link to an allowed scheme opens in the system browser by itself, since the click is the user action.
  - `shell.openExternal(url)` serves links built in code.
  - Allowed schemes stay `http(s)`, `mailto` and `tel` (plan 3b), only after a user action, never with credentials in the URL. Every other new-window attempt stays blocked.
- **Files and folders:** granted files get `openWithDefaultApp()`, granted folders `reveal()`. C# gets an injectable `IShell` (`OpenExternalAsync`, `OpenAsync(grant)`, `RevealAsync(grant)`).
- **The cases:** `notAllowed`, `noHandler`, `failed`.
- **Security default:** files that can run code (`.exe`, `.bat`, `.cmd`, `.ps1`, `.lnk`, `.msi`, scripts, `.desktop`, executables on Linux) are `notAllowed` unless the app opts in from C#, with the risk documented.
- **Check:** `docs/app-model.md` 6.6 (`outside.ts`); the title bar moved to 6.7.
  - 20 of 20 blocks verbatim; C# builds with 0 warnings.
  - TypeScript 7 and 6 clean; 16 of 16 mistakes rejected.

## 2026-11-14, Opening files is allow-listed (user decision)

- **Change:** the earlier entry ("Opening links and files outside the app") refused a fixed list of code-running types. The user pointed out that `.reg` and `.crt` were missing and that many types carry risk, so it is replaced by an allow-list:
  - **Nothing opens by default.** The app declares the types it opens with `spar.Shell(s => s.AllowOpening(".csv", ".pdf"))`.
  - **Dangerous types stay refused even when declared:**
    - on Windows, those `AssocIsDangerous` reports: Windows' own list of dangerous extensions plus types registered with `FTA_AlwaysUnsafe`;
    - on Linux, executables, `.desktop` files, scripts and executable MIME types.
  - **The exception:** `AllowDangerous(".reg")`, with the risk documented.
  - **Not restricted:** `reveal()`, which only shows a folder.
- **Still to check (M3):** whether `AssocIsDangerous` flags `.crt`, `.cer`, `.iso` and `.vhdx`. Any type it misses is added to Spar's own refused list.
- **Check:** `Program.cs` now declares the allow-list. C# builds with 0 warnings and the blocks are verbatim.
- **Sources:** [AssocIsDangerous](https://learn.microsoft.com/windows/win32/api/shlwapi/nf-shlwapi-associsdangerous), [FILETYPEATTRIBUTEFLAGS](https://msdn.microsoft.com/library/bb762506).

## 2026-11-14, Taskbar progress and badges (user decision)

- **Decision:** an injectable `ITaskbar` per window, and the same calls as `taskbar` in the SDK:
  - `SetProgress(0–1)`;
  - `SetProgress(Indeterminate | Paused | Error)`;
  - `ClearProgress()`;
  - `SetBadge(count | null)`.
- **Badges:** on Windows a count is a small overlay that Spar draws in the theme colours, or the app's own icon. On Linux the launcher shows it (the Unity LauncherEntry signal that worked in `spikes/linux-integration`).
- **Unsupported desktops:** the calls do nothing and `IsAvailable` is false. It is decoration, not a result, so it raises no error.
- **Check:** `docs/app-model.md` 3.10 (`CopyProgressReporter.cs`); C# builds with 0 warnings and the blocks are verbatim.

## 2026-11-14, Power and session events (user decision)

- **Decision:** an injectable `ISystemEvents.Watch()` yields `Suspending`, `Resumed`, `Locked`, `Unlocked` and `SessionEnding(Deadline)`. The page gets the same events through `system.on(event, handler)`, which only notifies; saving on session end happens in C#.
- **Session end** cannot be refused. The app has a short fixed deadline, then the normal shutdown runs.
- **Keeping awake:** `IPower.KeepAwake(reason)` returns a disposable. It uses `SetThreadExecutionState` on Windows and a logind inhibitor showing the reason on Linux, so a long copy does not stop when the PC sleeps.
- **How the host listens:**
  - Windows: `WM_POWERBROADCAST`, session-change notifications, `WM_QUERYENDSESSION`/`WM_ENDSESSION`;
  - Linux: logind `PrepareForSleep`, `PrepareForShutdown`, lock and unlock, with short delay inhibitors.
- **Testing:** real sleep and wake run in Windows Sandbox or a Linux VM.
- **Check:** `docs/app-model.md` 3.11 (`PowerAwareMonitor.cs`). C# builds with 0 warnings; the blocks are verbatim and TypeScript is clean.

## 2026-11-14, Secondary and modal windows (user decision)

- **C#:** window kinds are declared with `spar.Window(name, w => w.Route(…).Size(…).Modal().SingleInstance().Commands(c => c.Allow<…>()))`. A duplicate kind fails at startup.
- **Page:**
  - The generator emits the declared names as a TypeScript union (`@spar/app/windows`), so a misspelled kind fails `tsc` (TS2345).
  - `windows.open(kind, params)` opens a window. `windows.showModal(kind, params)` is a `Call` that waits until the modal closes; its close button gives `dismissed`.
  - Inside a modal, `windows.closeWith(value)` hands the result back.
- **Rules:**
  - Modals disable their owner and stay above it.
  - Single-instance kinds focus the open window.
  - Windows share data through the backend's services and subscriptions.
  - The main window closes its secondary ones first, through the two-stage close.
- **Check:** `docs/app-model.md` 6.7 (`openWindows.ts`) and `Program.cs`; the title bar moved to 6.8.
  - 23 of 23 blocks verbatim; C# builds with 0 warnings, and the run prints both kinds.
  - TypeScript 7 and 6 clean, and 17 of 17 mistakes rejected. The new one is a misspelled kind, TS2345.

## 2026-11-14, Zoom and find in page (user decision)

- **Zoom** is on by default: Ctrl+wheel and Ctrl+plus/minus/0, handled by the host since browser accelerators stay off.
  - It is saved per window with its placement.
  - C# limits it or turns it off per window (`w.Zoom(min, max)`, `w.Zoom(false)`).
  - The page has `zoom.get()`, `zoom.set(factor)` and `zoom.onChange(handler)`.
- **Find in page** is off by default, so an app's own Ctrl+F can be a scoped action.
  - `w.FindInPage()` turns on Ctrl+F with an SDK-drawn find bar, the same on both hosts.
  - `find.start(text, { matchCase })` returns the match count, with `next`, `previous` and `stop`. The engine searches: WebView2's find API, `WebKitFindController`.
- **M1 checks:** the WebView2 find API's minimum Runtime, and suppressing its default dialog.
- **Check:** `docs/app-model.md` 6.8 (`search.ts`, `Program.cs`); the title bar moved to 6.9. C# builds with 0 warnings; the blocks are verbatim and TypeScript is clean.

## 2026-11-14, Per-OS shortcuts (user decision)

- **Decision:** a shortcut is either one string, the same on every OS, or one per OS: `{ windows, linux, default }`, with `default` for an OS not listed. The platforms differ; for example Redo is Ctrl+Y on Windows and Ctrl+Shift+Z on Linux.
- **What follows from it:**
  - Menus and tooltips show the shortcut for the current OS.
  - Duplicate checks are made per OS.
  - C# actions take the same form.
- **Check:** the `docs/app-model.md` 6.4 example adds a Redo action with per-OS shortcuts. The blocks are verbatim and TypeScript 7 and 6 are clean.

## 2026-11-14, Screen-capture protection (user decision)

- **Decision:** `w.ProtectFromCapture()` per window kind hides the window from screenshots, recordings and screen sharing. At run time it is `IWindows.SetCaptureProtection(bool)` in C# and `capture.protect(bool)` on the page, both reporting whether it took effect.
- **Platforms:**
  - Windows uses `SetWindowDisplayAffinity` with `WDA_EXCLUDEFROMCAPTURE`, from Windows 10 2004.
  - Linux (X11 and Wayland) lets no app block capture, so there `isAvailable` is false and the call has no effect.
- **Documentation:** it reduces casual leaks, such as during a screen share. It is not a security boundary.
- **Check:** the `Program.cs` settings window uses it (`docs/app-model.md` 6.7). C# builds with 0 warnings; the blocks are verbatim and TypeScript is clean.

## 2026-11-14, Network status (user decision)

- **Page:** the web's own `navigator.onLine` with the `online` and `offline` events. Both web views take them from the OS, so there is no SDK API.
- **Backend:** an injectable `INetworkStatus` on .NET's `NetworkChange`: `Current`, and `Watch()`, which yields real changes debounced by about a second. It reports metered connections on Windows.
- **Documentation:** "available" is not "reachable"; apps check their own servers.
- **M1 check:** the page events in Windows Sandbox or a Linux VM, by dropping the virtual network.
- **Check:** `docs/app-model.md` 3.12 (`NetworkAwareMonitor.cs`). C# builds with 0 warnings and the blocks are verbatim.

## 2026-11-14, Logs, crash reports and configurable folders (user decision)

- **Logs:** Spar logs through `ILogger`. In Production it writes rotating files (by default 5 × 10 MB) at a level that configuration can raise without a new build.
  - Page console errors, uncaught exceptions and unhandled rejections reach the same log, grouped with flood limits.
- **Crashes:** an unhandled .NET exception writes a crash record (exception, versions, last log lines) before the process ends.
  - The next start sees it through `IAppHealth.LastCrash`.
  - A crashed web-engine process is logged and recovered as planned.
- **Sending:** Spar never uploads anything by itself. `diagnostics.collect()` and `IAppHealth.CollectAsync` pack logs and crash records into one zip, saved through a grant.
- **Secrets:** Spar's own entries never contain secrets, and the documentation reminds apps of the same.
- **Folders (user, 2026-11-14):** every folder is configurable, as usual, with `spar.Folders(f => f.Data(…).Logs(…).Crashes(…))`.
  - Each has a per-user default.
  - `Data` is the root the others default under: profile, settings, logs, crashes.
  - Test runs move them all under their own data root.
- **Check:** `docs/app-model.md` 3.13 (`CrashReport.cs`) and `Program.cs`, whose example puts logs next to the executable as in a portable install.
  - C# builds with 0 warnings; 26 of 26 blocks verbatim.
  - TypeScript 7 and 6 clean.

## 2026-11-14, Remaining decisions wait for their milestones (user decision)

- **Postponed:** none of the open items blocks M1, so each is decided when its milestone reaches it:
  - the test framework (issues A–K), with spikes at implementation time;
  - bridge transport, Hot Reload and Linux CI (M1);
  - devtools scope (M2);
  - serial groups, a binary wire type and settings files (M3);
  - the "later" native features (M3–M4).
- **Next:** the M0 close-out work:
  1. review all guidance against the plan and this log;
  2. run the copy-paste scan against other frameworks;
  3. make the first commit.

## 2026-11-14, Hung-page heartbeat confirmed (evidence)

Spike: `spikes/page-heartbeat/`, on WebView2 157 (host and Windows Sandbox) and WebKitGTK 2.52 (6.0). The first agent was stopped before writing up; a second wrote `RESULTS.md` from the raw logs without re-running.
- **Detection:**
  - 163 of 163 armed endless hangs were caught, with 0 false alarms in 123 runs.
  - It takes 15–20 s at 5 s × 3, because the first missed ping can leave up to 5 s after the hang starts.
  - Finite tasks of 3 s and 10 s and worker-only hangs do not alarm.
- **Engines:**
  - WebView2 never raised `ProcessFailed` for an idle hang in 72 s. It raises `RenderProcessUnresponsive` about 16 s after input, a dialog's focus change included.
  - WebKitGTK's `is-web-process-responsive` stayed true for idle hangs.
- **Cost:** 7–11 µs per ping, and the UI-thread pump p99 stayed at 0.21–0.37 ms. Messages queue in a hung renderer at about 1.6 kB each.
- **Recovery:**
  - Only terminating the renderer and reloading once works: 50 ms on Windows, 100 ms on Linux. Reload, Stop + Reload and same-site navigation recovered 0 of 40.
  - WebView2 has no terminate API, so the host uses the process id from `GetProcessInfos`.
  - Backend state survived, and no command was replayed.
- **Design changes adopted from the spike** (plan section 3):
  - arm at document creation;
  - disarm without the injected script;
  - gate on minimised, hidden and controller-hidden windows;
  - one state and one dialog with `ProcessFailed`;
  - "Reload page" terminates, then reloads once;
  - at most one outstanding ping;
  - on Linux, terminate a hung web process before quitting.
- **Open, for the user:** what "Wait" does next.
- **Not tested:** CPU stress, long hidden periods, several WebViews per browser, a Linux dialog, iframe hangs, sleep and lock.

## 2026-11-15, Guidance review applied (M0 close-out step 2; user approval)

- **Applied:**
  - **Rewritten skills:** `spar-contract-generation` (slimmed), `spar-typescript-conventions`, `spar-webview2-hosting`, `spar-native-interop` (now covering Linux), `spar-csharp-conventions`, `spar-msbuild`.
  - **New skill:** `spar-webkitgtk-hosting`.
  - **`README.md`:** Linux, `@spar/app`, NativeAOT as a default.
  - **`AGENTS.md`:** the new skill row, `plugins/spar/`, no framework names in skills, comments or file names, MSBuild warning exceptions, the docs site.
  - **New `CLAUDE.md`** (`@AGENTS.md`).
  - **`.editorconfig`:** `end_of_line = lf`, with CRLF for `.cmd` and `.bat`.
  - **`Directory.Packages.props`:** the task analyzer package added, the unused `xunit.v3.mtp-v2` removed.
  - **New `Directory.Build.targets`:** turns the trim, AOT and single-file analyzers on in every project outside `spikes/`, for target frameworks `net8.0` and later. A `netstandard2.0` project would otherwise hit NETSDK1210.
- **The five points, as recommended:**
  1. `docs/pitfalls.md` and the spike evidence (`spikes/`, `docs/evidence/`) join the exempt design records.
  2. **CA2016 at error, agreed:** the diagnostic CA2016, severity error, scope all projects, reason "callers must forward cancellation or opt out explicitly". It was already in `.editorconfig`; the agreement is now recorded.
  3. `GSK_RENDERER=cairo` is set for the startup error dialog always, and for the app only on a software GPU.
  4. Linux test runs keep their private `HOME`/`XDG_*` folders under `artifacts/linux-homes/<run>/` (`AGENTS.md`).
  5. `ManagedTest` and `Editor` stay in the MSBuild skill with a note. Whether they remain is settled with the test framework.
- **Reviewers' notes:** `CHANGES.md` per group, kept in the session scratchpad.

## 2026-11-15, Provenance scan before the first commit (evidence; M0 close-out step 9)

- **Question:** whether any code in the repository was copied from another framework.
- **Scope:**
  - Spar: 1,732 code files, 71 Markdown files with 74 code blocks, and a 1,879-file text corpus, without `node_modules`, `obj`, `bin`, `artifacts`, bundles or generated files.
  - Compared against: Tauri, wry, Wails, Electron (`lib/` and `typings/`), webview/webview and saucer, as shallow clones of their current heads, and every other framework source available locally.
- **Method:**
  - jscpd 4.3.0 on same-language pairs at 50 tokens / 8 lines, plus a lower 35/5 run where the languages match Spar's;
  - a normalized-token search for renamed clones;
  - 8-word text runs and shared identifiers;
  - targeted searches for bridge and injected-global names, licence headers and framework names;
  - a manual review of every hit.

  Positive controls caught a planted 120-line copy and a renamed 141-line copy.
- **Result: no copied or closely adapted code.**
  - The largest match is the tray's D-Bus introspection XML. It is fixed by the StatusNotifierItem and dbusmenu specifications, and it differs in layout, argument names and members.
  - Two generic phrases matched; they were reworded anyway.
  - Everything else was standard material: Microsoft Learn URLs, the documented WebView2 registry GUID, HTML boilerplate, include lists, PowerShell and GLib idioms.
  - The small Electron app in `spikes/linux-memory` uses Electron's public API for the memory comparison and copies nothing.
  - No foreign licence or copyright header appears in Spar. No skill, `AGENTS.md`, README or code file outside the design records names a framework.
- **Follow-ups applied:**
  - a comment citing the specifications above the tray XML (`spikes/linux-integration/Integ.Linux/Tray.cs`);
  - the drag-and-drop line in `docs/app-model.md` 3.6 reworded;
  - the `EvaluateAsync` summary in `spikes/browser-telemetry/linux-desktop/Linux.Desktop/SelfTest.cs` reworded.
- **Limits:**
  - Translation between languages (Rust, Go or C++ into C#) is checked only through names, comments and messages.
  - Reordered code and clones below the thresholds are missed.
  - Untagged Markdown code blocks were left out of clone detection.
- **Leftovers:** about 520 MB under `artifacts/provenance-scan/`, all disposable.

## 2026-11-15, Spike leftovers cleaned up (user approval)

- **Removed:**
  - **Docker:** the spikes' images (`spar-linux-desktop`, `spar-linux-loopback`, `spar-startup-prereq:*`, `spar-webkit-wasm-off`) and `fedora:43`, about 21 GB; the volumes `spar-linux-desktop-nuget` and `spar-linux-nuget`.
  - **`artifacts/`:** the folders that were disposable leftovers or WebView2 profiles: `provenance-scan`, `webview2-profiles`, `clean-clone`, `page-heartbeat`, `startup-prerequisites`, `resource-usage`, `webkit-wasm-off`. That was about 1.9 GB.
  - **WSL:** `~/spar-spikes/` (2.1 GB). `~/.dotnet` was kept.
- **Kept:**
  - The other `artifacts/` folders (583 MB), because several spikes' RESULTS tables cite raw data there. They move with the evidence when the spikes are deleted (M0 close-out step 8).
  - Docker images that predate the spikes or may be the user's own: the SQL Server, Playwright, .NET SDK and Postgres images.
  - The three WSL journal entries tagged `spar-sample`, which the journal cannot remove one by one.

## 2026-11-15, `Program.cs` shape confirmed (user decision)

- **Decision:** `Program.cs` holds exactly one `SparApplication.Create` and one `Run`, directly in the top-level statements. Anything else is an analyzer error at its location. This confirms the proposal that `docs/app-model.md` section 2 and plan section 2 carried as "Leaning".
- **Read by the build, so constants only:** the CSP additions and other header opt-ins, the permissions, the window kinds (their names become the `@spar/app/windows` union) and the window colours.
- **Run at start-up, so ordinary values are fine:** the rest of the configuration, such as services, folders, the tray and its menu handlers. The example already does this with `Path.Combine(AppContext.BaseDirectory, "logs")`.
- **Why now:** the CSP, the permissions, the window kinds and the theme colours were all decided on top of it, and M1a builds the analysis that reads them.

## 2026-11-15, Plan and guidance review; M1 split (user decisions; review evidence)

A second review after the first commit read `docs/plan.md`, this log, `docs/app-model.md`, `docs/pitfalls.md`, `AGENTS.md` and the seven skills against each other. Two reviewers read the files completely and cited both sides of each finding.

- **User decisions:**
  - **M1 is split into four slices,** each with its own exit: M1a build machinery, contracts and the core, with no window; M1b the Windows host; M1c the Linux host; M1d the development loop, loopback, the template and the tools. The review assembled each slice's contents from the old M1 list, the "in M1" notes spread through the plan and the research review. Moving an item between slices needs no decision; dropping one does.
  - **The research review's "Adopt (M1)" items are adopted,** together with the two-stage close and the threat-model note. They are in the slices, marked "(RR)": bridge limits and admission, increasing call ids, control-message capacity, parsing off the UI thread, exception redaction in Release, the isolated script world on Linux, the app identity and channel-separated folders, history navigation off, the host details of its section 4, typed C# test clients, the test-SDK shape, the generality fixtures, the fixture rules, the UI Automation smoke test, the restart check for contract edits, F5 debugging, asset checks while packing, third-party notices and the Deflate control. Numbers are still set by measurement.
  - **The WebView2 runbook is not added to the repository.** The plan is the record of what was taken from it.
  - **Raw spike data under the root `artifacts/` stays unversioned and disposable** (22 folders, 583 MB: about 77 MB of logs and JSON, about 400 MB of screenshots). Each `RESULTS.md` is the record. New spikes still keep raw evidence in their own `results/`.
- **Plan corrections, where the plan disagreed with a later decision or with itself:**
  - the base CSP block now carries `require-trusted-types-for 'script'` (decision "Trusted Types on by default");
  - the navigation lock no longer says that `target="_blank"` is only blocked: a link to an allowed scheme opens in the system browser (decision "Opening links and files outside the app");
  - the minimum WebKitGTK for the startup check is 2.48, provisionally: the APIs need 2.40 and the `JSC_useWasm` option name 2.48;
  - the task analyzer has four severity raises, not three (MSBuildTask0009 was added by "Same-origin iframes blocked; MSBuild task analyzer settings");
  - resource usage: Spar ships no ready-made contract, and the first version has one memory watch and one disk read, not a watch per measure;
  - the `SparVariant` values are listed (`Production`, `Development`, `DesktopTest`, with `ManagedTest` and `Editor` pending). Until now only the MSBuild skill listed them;
  - `npm ci --ignore-scripts` runs in every variant when the lockfile changed, because the Debug type-check and the dev server need `node_modules`. The plan had it only in Release and test variants. This is a consequence of "Debug type-checks too", not a new choice; it is checked when M1a builds the step;
  - the decided "Profiles and shutdown" block moved from the open-questions table to section 3, and the labels "pending", "not decided" and "proposed" were removed from decided content.
- **Recorded here because only the plan and the app model said it:** plain `Task` and the `ValueTask` forms are accepted command returns. This log had them only as a recommendation (entry "Contract syntax", before "Contract rules approved"); the plan, the app model and its check have used them as accepted since 2026-11-14.
- **M0 close-out:** steps 1 to 3, 5 to 7 and 9 are done. Moving the evidence (step 4) and deleting the spikes (step 8) remain and do not block M1a.
  - `eng/check-size.cs` was written new and run: 0 errors on the repository, and in a scratch repository it reported an oversized file, a folder of 41 files, a ceiling to lower, a stale entry and a malformed line, and passed once the exceptions matched. `eng/size-exceptions.txt` is empty.
  - `agent-setup.md` is not added: `.claude/settings.json` and `AGENTS.md` cover it.
  - `spikes/app-model-check` and `spikes/frontend-frameworks` hold checks that the guidance requires. They stay until M1a rebuilds them under `tests/`.
- **`docs/app-model.md`:** three examples broke rules the document states, and compiling against stand-ins could not see it.
  - `ExportAsync` took a list as its request; it now takes a record. `LastCrash` was synchronous; it is now `LastCrashAsync` returning `Task`. The failure hooks read `e.call` and `e.message`, which `CallError` does not define.
  - A contract named `export` (a reserved word) became `csvExport`, and an error case named `Cancelled` became `Dismissed`.
  - The check gained `tools/check-contracts.mjs`, a text check of request shapes, return forms and reserved names, as a stopgap until the analyzer exists.
  - Run on 2026-11-15 (SDK 11.0.100, Node 24.21.0, TypeScript 7.0.2 and 6.0.3): 26 of 26 blocks verbatim; `tsc` clean on both versions; all 20 deliberate mistakes rejected on both; routing 17 of 17; the C# build with 0 warnings; the missing-case build fails with CS8509 as required. Not run: a NativeAOT publish.
- **`docs/pitfalls.md`:** the entry that recommended cloaking the window now gives the decided answer (show at once, never cloaked). Ten duplicate entries were merged, unverified entries moved under "not rechecked" sub-headings at the end of their section, and two sections were added: "Bridge and loopback" and "Page security". 280 top-level entries became 270, all by merging.
- **Skills and `AGENTS.md`:** `GSK_RENDERER=cairo` has its condition again; the variant list, "plan D3" and the multi-package wording are corrected; `AGENTS.md` gains the size-limit rule, says that "framework" means desktop and application frameworks, and states the `artifacts/` exception above.
- **Entries in this log that later entries replace,** listed here because the log is append-only:
  - the stubs "D3", "D6" and "D8" marked "Pending" near the top: D3 and D8 have dated entries ("D3: variant builds run in-proc", "D8: Devtools automation transport, in-process CDP"); D6 is the development loop, decided across "Node at run time in Debug", "Debug serves Vite through the app's own origin" and "Hot Reload signal";
  - "TypeScript-only generated client" and "Generated contract modules stay TypeScript": replaced by `.js` + `.d.ts` + `.d.ts.map`;
  - "one package per contract" and `@spar/shared`: replaced by the single `@spar/app` package with subpaths;
  - `JSC_useWebAssembly`: the option is `JSC_useWasm`;
  - `execution: notStarted | completed | unknown`: `stopped` was added by "`CallError` kinds";
  - the 4-of-6 coverage and "about 40 lines" that justified the union struct: SPAR050–SPAR054 now cover 6 of 6 in about 2,100 lines;
  - the task-analyzer note that other severities "stay at their defaults": MSBuildTask0009 was raised afterwards.
- **Open, added to the plan's table or to a slice's "decide first" list:** the CI provider; arm64; the two-stage close API; the Windows single-instance mechanism; what "Wait" does after the hung-page dialog; whether `CallError` names its call; the TypeScript entry for a payload-less error case.
- **Not checked by this review:** every spike number against this log, beyond a sample; the service-worker claim for `spar://`, which no spike tested and M1c now owns.

## 2026-11-15, Test strategy: the app runs inside the test process (user decision; `spikes/test-host`)

The user was not convinced by tests that launch the app's executable, nor by changing dependencies only for tests, and asked for a better design. After the spike the user accepted the proposed direction ("let's go with it"). It replaces the managed, loopback and desktop levels of plan section 5 as they stood; Spar's own test types (generator, wire, build, package, host lifecycle, Sandbox, performance, documentation) are unchanged.

- **Decided:**
  - **A test runs the app's own `Program.cs` inside the test process.** Spar's test library calls the app's entry point (the spike used an unsafe accessor to the compiler's `<Main>$`, which also works under NativeAOT). `SparApplication.Create` sees the test's session, and `Run` applies the test's service overrides after the app's registrations, builds the app and hands it to the test instead of opening its window. The app contains no test code, there is no `TestConfiguration.cs`, and overrides are ordinary DI code whose fakes the test can inspect.
  - **One test-app API, three surfaces** in front of the same in-process backend: none (command tests through real serialization and dispatch, with typed C# test clients generated per contract), a browser (the page over in-process loopback, one Chromium per test run, a context per test), and the app's real window (WebView2 or WebKitGTK on the test process's UI thread).
  - **Safe by default:** one data root per app instance, from which every Spar folder derives, including folders the app set itself; Spar's outward services (clipboard, credentials, shell, dialogs, tray, notifications, power) are fakes in a test host; an app marks its own real resources as external (`AddExternal<TService, TImpl>()` in the spike), and resolving one that the test did not replace or allow fails with its name.
  - **A separately launched executable is used only for** the exact Production binary as a black box (nothing replaced) and for behaviour across processes (single-instance handoff, exit codes, the crash record at the next start). It gets no replacements, by design: only a disposable data root and real disposable resources through configuration.
  - **The NativeAOT lane is the same test sources** built against `xunit.v3.aot` and published with NativeAOT, without the browser tests.
  - **The test host is trimmed out of Production:** it sits behind a feature switch, and so do Spar's test fakes. The production boundary test proves it on the published binary.
  - **Variants:** the instrumented desktop executable is gone, so `DesktopTest` is replaced by one test variant, the one a test project builds the app in (test-host hook, page automation, the UI-thread stall probe). Its name and how `Configuration` selects it are fixed in M1a; `Editor` is settled there too.
- **Evidence** (`spikes/test-host/RESULTS.md`, Windows 11 with WebView2 157 and Linux in a container with WebKitGTK 2.52.6 under Xvfb, CoreCLR and NativeAOT, two runs each, all passing): command-level starts 0.2–0.6 ms median and 8 parallel apps isolated; browser tests 97–144 ms each on a shared Chromium; real-window tests 466–472 ms (Windows) and 297–301 ms (Linux) for open, journey and close, 50 in sequence and 4 at once with 0 engine processes left; the NativeAOT test executables hosted the real windows on both OSes with 0 warnings; the Production binary contains neither the test session nor the fakes; a black-box run of it passed 5 of 5.
- **Requirements the spike found, now in the plan:** Spar's test fakes behind the feature switch (behind an ordinary flag they stayed in the binary); `IsReputationCheckingRequired = false` in test windows (otherwise each early close costs 6.4 s); one UI thread per test process on each OS; the engine prerequisite check before the first window test, because a native engine abort ends the whole test process; Spar keeps no process-wide mutable state and never uses `Console`, environment variables or the working directory for per-app values; apps are `WinExe` on Windows.
- **Issues answered:** plan section 5 issue A (the managed level's form) and most of issue C (replacing services in end-to-end tests).
- **Still open, each decided with the user when its slice reaches it, with a spike where needed:** trusted input in the in-process window (CDP `Input.*`, XTEST); one shared WebView2 environment with an InPrivate controller per test (76–79 ms per test in `spikes/webview-profiles`) against a fresh folder per test (about 470 ms here); how the generator emits the entry-point accessor; streams and cancellation through the typed test client; whether the hook stays an `AsyncLocal` or becomes compile-time interception; a data-root switch for black-box runs of the Production binary, which needs a security review; issues B and D to K of section 5.

## 2026-11-15, Plan split, additions and the M1a scope (user decisions)

- **The plan is split** (user: "Split now"): `docs/plan.md` keeps the goal, the inputs, the settled decisions, the open questions, the milestones, the risks, the definition of done and verification; the design sections move to one file each under `docs/design/`. Section numbers stay global: "section 3c" or "plan section 3c" anywhere, including earlier entries of this log, means heading 3c in the file the plan's design index lists.
- **M1a takes the test host** (user: "Move to M1a"): the in-process test host with the no-page and browser surfaces, and the loopback bridge the browser surface needs, move from M1d into M1a, so the first slice ends with a real page calling a real backend in a test. M1b and M1c add the window surface on their host. M1d keeps the development loop, the development uses of loopback (launch profile, dev session), the template and the tools.
- **Added to the plan** (user selected all four): provisional budgets checked at each M1 exit, taken from the spike numbers, instead of first in M4; a definition of done for every feature; a risk list, each risk with the slice or spike that retires it; and an open question on upgrades and data migration.
- **Earlier review calls confirmed** (user kept all four): `npm ci --ignore-scripts` in every variant; plain `Task` and `ValueTask` as accepted command returns; the app-model contract `csvExport`; no `agent-setup.md`.

## 2026-11-15, Polished sample apps: a Markdown notebook; the test variant (user decisions)

- **Sample apps are real, polished applications** (user: "for our examples projects, let's try to do very nice applications"). The bare-bones Notes sample is replaced. A sample has a considered design in both themes, keyboard and screen-reader accessibility, real data handling and real error states, so it shows what an app built with Spar looks like, not the minimum that works.
- **The sample that grows through M1 to M3 is a Markdown notebook** (user chose it from four: a Markdown notebook, a system monitor, a budget tracker, a photo library). A local knowledge base: notebooks and pages stored as Markdown files under the app's data folder, live preview, tags, full-text search, backlinks, images by drag and drop, pages in their own windows, and export to PDF.
  - M1 needs only commands, typed errors and files, so the sample is useful from M1a; streams (search results, file watching), dialogs, secondary windows, drag and drop and printing fill it out in M3.
  - Its Markdown preview needs an HTML sink, so it exercises the Trusted Types policy an app declares in C# under the default CSP (plan section 3b), with sanitised HTML.
  - The working name is "Notebook" (`samples/Notebook/`); the final name is chosen when the sample is created.
- **UI stack for the samples:** React, TypeScript and Vite, with Tailwind CSS compiled to a static stylesheet and Radix headless primitives for dialogs, menus and popovers (user chose it over a design system of our own, and over deciding later). Reason: a polished, accessible result under Spar's strict CSP, which blocks styles injected at run time; libraries that inject `<style>` elements are not used. Tailwind and Radix are the samples' choice, never a Spar dependency or default.
- **The test variant is `Test`** (user chose it over `TestHost`): `SparVariant=Test`, the variant a test project builds the app in, with the test-host hook, page automation and the UI-thread stall probe.
- **`Configuration` maps to the variant, and `Editor` is dropped** (user chose it over keeping `Editor` and over a variant independent of `Configuration`): Debug builds `Development`, Release builds `Production`, and a test project builds its referenced app as `Test` whatever its configuration. `SparVariant` can still be set explicitly. No spike's build files use `Editor` (only `Production`, `Development` and `DesktopTest` appear in them); IDE design-time builds are handled by the tasks' design-time guard. The variants are therefore `Production`, `Development` and `Test`.

## 2026-11-15, Test host mechanics: the generated entry point (user decisions; `spikes/entry-accessor`)

Plan section 5, issue L, decided with the user after the spike:
- **The test host reaches the app through `SparTestApp.StartAsync<Program>()`** (user chose it over a generated delegate that keeps `Program` internal and needs `InternalsVisibleTo`). In the `Test` variant only, Spar's generator makes `Program` public and implements `Spar.ISparEntryPoint` (`static abstract int Run(string[] args)`) on it, calling the compiler's top-level method through an `[UnsafeAccessor]` whose name comes from `Compilation.GetEntryPoint`.
  - Evidence: 3 of 3 tests passed under CoreCLR and under NativeAOT (`xunit.v3.aot`, 0 warnings, 19 s publish, 0.5 ms per start). In the Production publish `Program` stays internal with no interface or accessor, and the executable contains none of `ISparEntryPoint`, `SparEntry` or `SparTestSession`; `Development` is unchanged too.
  - A `Program.cs` in another shape fails the `Test` build with a located error: no `return` (void), async top-level statements (`Task<int>`), or a classic `Main`. The same files build normally in `Development`.
- **The session hook stays an `AsyncLocal`** (user chose it over compile-time interception). Interception of the `Create` call would still need the `AsyncLocal` to find the right session when several apps start at once, and the only thing it would remove, the `IsSupported` branch in `Create`, is already trimmed from Production behind the feature switch.
- **The public names are the spike's** (user chose them over shorter `TestApp`/`TestSurface`, which could collide with an app's own helpers): `SparTestApp.StartAsync<Program>(options => …)`, `SparTestSurface.None | Browser | Window`, `options.Services(…)`, `options.AllowExternal<T>()`, and `services.AddExternal<TService, TImplementation>()` in `Program.cs`.
- **Found on the way** (in `docs/pitfalls.md`): `$(Configuration)` is empty when `Directory.Build.props` is read; conditions on the project's own `TargetFramework` belong in a `.targets` file; a netstandard2.0 generator needs an `IsExternalInit` stand-in for records and analyzer release tracking for its diagnostic IDs.
- **Still open for M1a:** streams and cancellation through the typed test client; a Spar diagnostic for an app that declares `internal partial class Program` (expected CS0262 with the generated public part, not tested); IDE behaviour of the new diagnostics and of `Program` as an `ISparEntryPoint` before a build; the same on Linux and through packages.

## 2026-11-15, Bridge messages: encoding C header fields (user decisions)

The M1a "decide first" points on the bridge (plan section 2c). The full message table and rules are in section 2c, "The messages"; this entry records the choices and why.
- **The outcome lives in the header** (user chose it over keeping section 2's envelope in the payload): `{"type":"result","id":7,"ok":true}` TAB the value's JSON (empty when there is none), or `"ok":false` TAB the declared error `{"code":…}`. The payload is exactly the value, so the generated serializer's buffer goes out unchanged, and the page branches before parsing a large value. This replaces the `{"ok":…,"value"|"error"|"failure":…}` envelope of decision "Contract syntax in the generator" and section 2; streams end with `end`, in the same way.
- **Infrastructure failures are one `failure` message** (user chose it over the spike's `cancelled`, `not-executed` and `outcome-unknown`): its header carries `kind`, `code` and `execution` exactly as decided in "`CallError` kinds", so the page builds the `CallError` from the header alone and there is one mapping, not two. A confirmed cancel is `kind: "cancelled"`. A detail travels only in Development, as the payload, in line with the exception redaction of M1a.
- **The contract fingerprint is checked once per document** (user chose it over a fingerprint on every call): the page sends its own in `hello`, the host answers with its own in `open`, and on a mismatch every call of that document fails with `rejected` / `contract-mismatch`. A page's bundle cannot change within a document; this saves about 30 bytes per call and lets the page warn before the first call.
- **`CallError` names its call** (user chose it over keeping only `kind`, `code` and `execution`): the SDK adds `contract` and `command` from its own record of the call id, so `onFailure` and `observe` can say which call failed with nothing more on the wire. `docs/app-model.md` section 7 no longer lists this as open; the app-model check's SDK stand-in has the two fields, and its routing test checks that both hooks see them (run 2026-11-15: 26 of 26 blocks verbatim, 20 of 20 mistakes rejected on TypeScript 7.0.2 and 6.0.3, routing clean).
- **Proposed with them and part of the same table, not asked separately:** the host assigns document ids in `open` (the page never names a document, so it cannot claim another one); `ready` and `closing-done` as page-to-host messages and `event` and `closing` as host-to-page ones; `stream`, `credits`, `items` and `end` get their header fields and vectors in M1a and their implementation in M3; the rules of `spikes/bridge-encoding` (safe-integer ids, duplicate keys malformed, unknown fields ignored, an unknown type failing only a pending call, version negotiation, tolerant trace values, string messages only).
- **Not done yet:** the conformance vector file itself, written in M1a with the first codec.

## 2026-11-15, M1a names: timeout, build properties, payload-less cases, test results, client refresh (user decisions)

The small batch of M1a "decide first" points:
- **A command's backend time limit is `[SparCommand(TimeoutSeconds = 30)]`** (user chose it over a `Timeout = "00:00:30"` string and `TimeoutMilliseconds`): attribute arguments must be constants, and a positive integer needs no parsing and no culture. Sub-second limits are not offered.
- **The build properties keep their names:** `SparTypeCheck` (`false` turns off the Debug type-check) and `SparBundleBudget` (user chose them over `SparFrontendTypeCheck` and `SparFrontendBudget`).
- **A declared error case without a payload has the interface-map entry `undefined`** (user chose it over `{}`): its handler takes no parameter, and in the error union it is `{ code: "<case>" }`. Checked in `spikes/app-model-check` (2026-11-15: 26 of 26 blocks verbatim; `tsc` 7.0.2 and 6.0.3 clean; 21 of 21 mistakes rejected on both, the new one, a handler that reads a payload from such a case, with TS2322 and TS7006; routing 18 of 18). The stand-in's `ErrorHandlers` and `ApplicationError` special-case `undefined`.
- **Test results go to `artifacts/test-results/<project>/`** at the repository root (user chose it over each project's `obj/`). `AGENTS.md` now lists test results among the root `artifacts/` contents. How the default is set (an MTP results-directory setting from Spar.Sdk or the template) is checked in M1a.
- **The TypeScript client refreshes on build, and on save while the development loop runs** (user chose it over a separate on-save path and over build only): in the IDE a build regenerates it; under `dotnet watch` or `spar dev` a saved contract change restarts the app, which regenerates it. There is no IDE-specific refresh path that could disagree with the build. This answers the question left by "D1 follow-up: IDE design-time behaviour".

## 2026-11-15, CI on GitHub Actions; the first slice (user decision; implementation record)

- **CI is GitHub Actions** (user: "For ci we'll go with GitHub actions. Prepare it for now and we'll push when we'll have a repository"). `.github/workflows/ci.yml` runs on `windows-latest` and `ubuntu-latest`: the size check, the solution build, `dotnet test`, the NativeAOT test lane of the Notebook, a `-mt` build, and an upload of `artifacts/test-results/`. Not run yet (no repository). Before the first push: pin every action to a full commit SHA, and check whether the hosted Windows runner can run the window tests of M1b.
- **The first slice of M1a is built** (evidence `docs/evidence/m1a-first-slice.md`): `src/Spar` (results, attributes, the app builder, dispatch, the test session), `src/Spar.Generator` (dispatch, serializers, catalog, the `Create` interceptor, the Test-variant entry point and typed test clients, and an analyzer), `src/Spar.Testing` (`SparTestApp` with the `None` surface), `src/Spar.Sdk/Sdk/` (props and targets), and `samples/Notebook` with its pages contract and ten command tests, passing under CoreCLR and in the NativeAOT lane.
- **Implementation choices recorded for review** (not user decisions; each can be changed):
  - **The generator hands the catalog to `SparApplication.Create` through a C# interceptor** of the call in `Program.cs` (`InterceptsLocation`, namespace `Spar.Generated`, enabled by `InterceptorsNamespaces` in Spar.Sdk). This satisfies "class contracts are registered by the generated code" without a process-wide registry or reflection. A `Create` call that the generator did not intercept throws with a message naming the cause.
  - **Source mode in this repository:** samples and test apps import `src/Spar.Sdk/Sdk/Sdk.props` and `Sdk.targets` directly, and reference Spar's projects (with `UndefineProperties` so an app's variant or lane never reaches them, and each Spar project builds once). The package-only consumer test covers `<Project Sdk="Spar.Sdk">` from packages.
  - **Program.cs diagnostics get new IDs:** SPAR060 (not top-level statements) and SPAR061 (not returning `int` from `return app.Run();`), category `Spar.Program`, from the entry-accessor spike's SPIKE001 and SPIKE002.
  - **Generated C# is written under `obj/`** (`EmitCompilerGeneratedFiles` on by default) so people and agents can read what dispatch does.
  - **The default globs exclude every `obj/`, `bin/`, `node_modules` and `dist`:** without it, the NativeAOT lane (restoring into `obj/aot/`) compiled the other lane's generated files a second time (CS0579, CS0101).
  - **`PublishAot` is set before Microsoft.NET.Sdk's targets are imported:** set after them, `dotnet publish` silently produced a 73 KB framework-dependent apphost.

## 2026-11-15, Bridge in C#: codec, session and tests (implementation record; one rule corrected for review)

Evidence: `docs/evidence/m1a-bridge-and-tests.md`.
- **Built:** the encoding C codec (`src/Spar/Bridge/BridgeCodec.cs`), the per-document `BridgeSession` over dispatch, the shared vectors `tests/conformance/bridge-messages.json` (24 canonical messages, 10 other valid forms, 16 malformed ones), and three test projects: `tests/Spar.Tests` (93 tests), `tests/Spar.Generator.Tests` (25) and `tests/WireShapes.Tests` (20). With the Notebook's 10, `dotnet test` runs 148, all passing; the three run-time projects also pass in the NativeAOT lane.
- **Corrected rule, for review:** section 2c said a missing, unsafe or repeated id is answered with `rejected` / `invalid-request`. Building it showed that a reply to an untrusted id is addressed to an id the page may be using for another call: a repeated id would fail the earlier call, and `"id":7,"id":8` would answer 8 (the bridge-encoding spike saw such a header cancel call 8). The rule is now: such a message gets no reply and is logged; a malformed message with a new, valid id is `rejected` / `invalid-request`; one for a pending call fails that call with `protocol` / `malformed`. Section 2c is updated.
- **Found by the new tests and fixed:** arrays and `IReadOnlyList<T>` of the same element shared one generated method name (CS1503), and an array property could not take the list the reader returned (CS0266); `DateTimeOffset` values without an offset were accepted and read in the local time zone (now refused, as the wire rule requires).
- **Choices made while building, for review:** a `stream` message is answered `rejected` / `invalid-request` until M3; a call before `hello` is `rejected` / `invalid-request`; the session negotiates `protocol` as the lower of the page's and its own.

## 2026-11-15, Bridge id rule accepted (user decision)

The user accepted the corrected id rule of "Bridge in C#: codec, session and tests" ("Accept it"): a message whose id cannot be trusted (missing, not a safe integer from 1, given twice, or not higher than every earlier id of the document) gets no reply and is logged; a malformed message with a new, valid id is answered `rejected` / `invalid-request`; a malformed message for a pending call fails that call with `protocol` / `malformed` / `unknown`. Plan section 2c states it.

## 2026-11-15, TypeScript SDK core (implementation record; two choices for review)

Evidence: `docs/evidence/m1a-typescript-sdk.md`.
- **Built:** `src/Spar.Client` with the encoding C codec, `Call`, `Result`, `CallError`, `configureSpar`, `SparError` and a per-document `Connection` over a host channel. 103 Vitest tests pass, including the 76 shared vector checks (the C# side passes the same file, 95 tests). The sources and tests are clean on TypeScript 6.0.3 and 7.0.2.
- **Choices made while building, for review:**
  - **An aborted call waits 5 s for the backend's confirmation**, then ends as `transport` / `unconfirmed` / `unknown`. The plan names "the confirmation timeout" without a number.
  - **A call made before `open` waits for it,** instead of failing at once; if the channel closes first, it fails `unavailable` / `notStarted`.
- **Two vectors added on both sides:** an id written `7.0` and credits written `4e0` are malformed, because the C# reader takes only integers written as integers and the TypeScript decoder now checks the raw text the same way.

## 2026-11-15, SDK abort confirmation and calls before open accepted (user decision)

The user kept both choices of "TypeScript SDK core" ("Keep both"): an aborted call waits 5 s for the backend's confirmation and then ends as `transport` / `unconfirmed` / `unknown`; a call made before `open` waits for it, and fails `unavailable` / `notStarted` if the channel closes first.

## 2026-11-15, Loopback transport: one ordered send path (implementation record; choices for review)

Evidence: `docs/evidence/m1a-loopback-browser.md`.
- **Built:** `src/Spar.Loopback` (Kestrel on 127.0.0.1, the page's files, the bridge endpoints), the SDK's `loopbackChannel` and `takeToken`, the test host's `Browser` surface (`o.Surface = SparTestSurface.Browser`, `o.Page(folder)`, `app.PageUrl()`, `app.Problems`), and `tests/WireShapes.EndToEndTests`: the WireShapes page in Chromium calling the backend in the test process. `dotnet test` runs 186 tests, all passing; the NativeAOT lane passes 153.
- **Choice for review: every page message goes through one ordered send path.** Section 2c said "`fetch` for unary calls, and one streamed channel per document", from `spikes/loopback`. Building it against the accepted id rule showed two problems with a `fetch` per call:
  - **Order:** separate requests travel over up to six connections and can arrive out of order, and a call whose id is not higher than every earlier one is dropped without a reply. With the order broken on purpose (sends in parallel), the 200-call test timed out: calls were dropped and never answered.
  - **Connections:** a long call holds its `fetch` connection until it ends, so five long calls plus the channel use up the browser's six connections and block even cancels, the pitfall that moved streams to the channel.

  So the page posts its messages to `POST /_spar/send` one request at a time, and whatever queues up while one is in flight travels in the next request, one message per line. Every host message, call replies included, comes on the document's channel (`POST /_spar/channel`, opened by the `hello`, first line `open`). This is the same shape as the desktop hosts: one ordered path each way. The cost is that a message can wait for one loopback round trip behind the request in flight.
- **Other choices made while building, for review:**
  - **Endpoints:** `/_spar/channel` (the body is the `hello`; 400 `invalid-request` otherwise), `/_spar/send` (the document in `X-Spar-Doc`, 404 `unknown-document` for a document the host does not have, 204 otherwise) and `/_spar/csp-report` (no token: the browser sends reports itself; only logged). Refusals: 403 `forbidden-host`, 403 `forbidden-origin`, 401 `unauthorized`, 405 `method-not-allowed`, 404 `not-found`, as plain-text codes. The spike's per-command paths and the status-code table of section 2c no longer apply: outcomes travel as bridge messages.
  - **A refused or failed send closes the page's channel,** so the connection fails the calls it had sent as `transport` / `lost` / `unknown`: the host may have run them.
  - **The host checks `Host` on every request, assets included** (127.0.0.1 or `localhost` with the port), and the `Origin` only on bridge requests, when one is sent.
  - **Files are served only with a listed content type** (`SparContentTypes`), so `.map` files and unknown types are 404, and only from plain path segments (no dot segments or dotfiles, no backslashes or escaped characters, no links).
  - **The page headers of section 3b are one runtime type,** `SparPageHeaders`, for every host: the base CSP (with `connect-src` naming the served origin), `nosniff`, `no-referrer`, the cross-origin policies, `Permissions-Policy` and `no-store`.
  - **`Spar.Testing` references `Spar.Loopback`,** so every test executable carries Kestrel: the NativeAOT test executables grew from 16.4 MB to 20.2–20.3 MB, and each lane publish from 10–14 s to 21–24 s.
  - **The Browser surface serves page folders the test names** until the packing step embeds the built page; the browser fixture lives in the test project, because Playwright cannot be part of `Spar.Testing` (it is not trim-safe, and `Spar.Testing` is in the NativeAOT lane).

## 2026-11-15, Loopback transport accepted; Browser surface in its own package (user decisions)

- **Loopback transport accepted** ("Accept it"): the rule of "Loopback transport: one ordered send path" is decided. Every page message goes through one request at a time to `/_spar/send`, batched, and every host message, call replies included, comes on the document's channel. The user's view on its cost: the ordering costs something only on loopback, which is for development and tests ("if it's just for loopback i'm good").
- **Browser surface in its own package** ("Separate package", chosen over keeping one package and over a feature switch that trims it in the NativeAOT lane): `src/Spar.Testing.Browser` holds the surface and is the only package that references `Spar.Loopback`. The test syntax stays: `o.Surface = SparTestSurface.Browser`, and `o.Page(folder)`, `app.PageUrl()` and `app.Origin` are extension members from the new package. Asking for the Browser surface without the package fails at start with a message naming it (a test in `tests/WireShapes.Tests`). In this repository the end-to-end project references it directly; how an app's end-to-end project gets it from `Spar.Sdk` is settled with packaging.
  - **Effect:** the NativeAOT test executables of `WireShapes.Tests` and `Notebook.Tests` are back to 16,441,856 B and 16,480,768 B (20.2 MB with Kestrel). `Spar.Tests` keeps Kestrel (20,328,448 B) because it holds the loopback host's own tests. Lane publishes took 18–25 s in this run, under uncontrolled conditions, so no saving in time is claimed.
  - **Tests:** 187 pass on CoreCLR, 154 in the NativeAOT lane.

## 2026-11-15, Client step: the generated TypeScript modules (implementation record; choices for review)

Evidence: `docs/evidence/m1a-client-step.md`.
- **Built:** `src/Spar.Sdk.Tasks` with the `SparClient` task (multithreadable, paths through `TaskEnvironment`, a process-wide reference cache), wired in `Sdk.targets` for apps: an inventory written only when it changes, the step before `CoreCompile`, skipped at design time and when no input changed, and in this repository a task host of its own. It runs the generator's `ContractAnalysis` and the catalog's fingerprint function on a compilation of the project, and writes the `@spar/app` package under `obj/spar/client`: a module per contract, `shared` for types several contracts use, `spar` with the fingerprint, and the compiled SDK under `sdk/` with its sources under `sdk/src/`. Files are written only when their bytes differ, and stale ones are deleted. The WireShapes page now uses its generated modules, and the Notebook gets its `pages` module.
- **Tests:** `tests/Spar.Sdk.Tasks.Tests` (24): emitter output, shared types, names, and the package compiled by TypeScript 6.0.3 and 7.0.2 at the strictest settings with `skipLibCheck` off, under bundler and Node resolution, with eight deliberate mistakes each failing with its exact code on its line. `dotnet test` runs 213, all passing; the NativeAOT lane 154.
- **Choices made while building, for review:**
  - **The page's fingerprint comes from `@spar/app/spar`** (`export const fingerprint`), computed by the same function as the C# catalog; `spar` is one of the reserved subpaths. A browser test checks that the two are equal.
  - **A request the generated encoder refuses never leaves the page:** NaN or an infinity for a `double`, a fraction or an out-of-range value for an `int`, a `long` out of range. The call ends `rejected` / `invalid-request` / `notStarted`, and the SDK logs why on the console. `invoke` now takes the encoder, so this goes through `match` like every other outcome instead of throwing.
  - **Generated values are read and written through a new SDK module, `sdk/wire.js`:** field readers that check each JSON type (a mismatch is `protocol` / `malformed`), writers that produce JSON text (`long` as a string, lone surrogates as U+FFFD), and `field()`, which reads only own properties. Error codes are read with a `switch`, never by looking a code up in an object.
  - **The declarations import the SDK by package name (`@spar/app/sdk`), the modules by relative path (`./sdk/sdk.js`):** the `.d.ts` reads as `docs/app-model.md` shows it, and the `.js` loads in a browser with no bundler, as the WireShapes page does.
  - **A record request is named `request`, a scalar one keeps its C# parameter name** (`cancel: (orderId: string, …)`), as in the app model; a parameter name that is a JavaScript reserved word gets `Value` appended.
  - **SPAR003 (reserved contract names) and SPAR040 (two types with one TypeScript name, or a type named like an SDK type) are reported by the client step** as build errors at the contract's file, until the analyzer reports them as you type.
  - **In this repository the step needs the SDK built** (`npm run build` in `src/Spar.Client`), and fails with that instruction otherwise; CI builds it first. The SDK's compiled maps use `sourceRoot` so they point at `sdk/src/` inside the package.
- **Found while building:** a type in the global namespace kept `global::` in its TypeScript name, which would have generated invalid TypeScript (fixed, with a test).
- **Not built yet:** the `.d.ts.map` to the C# declarations, the manifest, the contract snapshot (SPAR100), the differential test against `tsc --declaration`, the package-mode paths (the task and the SDK from the `Spar.Sdk` package), and the SDK version as the package version.

## 2026-11-15, Client step: reserved type names (implementation record, at the user's request)

The user asked for the gap found in review to be closed ("Yes, fix it"). Evidence: `docs/evidence/m1a-client-step.md`, "Follow-up: reserved names".
- **A type named with a reserved word is SPAR003,** reported at the type's own file and line: JavaScript reserved words and TypeScript's type keywords (`any`, `string`, `undefined`, …). C# allows them as names (`record any`, `record @string`), and the declarations would not have compiled. Roslyn writes such a name with `@`, which the TypeScript name drops before the check.
- **A type named like an SDK import is SPAR040,** at its declaration. The import line of every declaration file is written from the same list, `TypeScriptNames.SdkImports`, so a new import (streams will add `Result` and `Subscription`) is forbidden as a type name without a second change.
- **A contract named like a name its own module declares is SPAR003:** the module's imports (`invoke`, `wire`, `unknownErrorCase`), its readers and writers (`readItem`), its decoders (`pingDecoder`) and `noValue`/`noErrors`.
- **Corrected:** the clash check compared each contract's types with every shared type, including ones that contract does not import, so it could report a clash that does not exist. It now checks each module's own names, and the shared module on its own.
- **Not handled:** an explicit `[SparName]` on a type does not rename it in TypeScript yet, so the fix for these problems is renaming the C# type.
- **Tests:** `tests/Spar.Sdk.Tasks.Tests` 37 (13 new); `dotnet test` 226, all passing.

## 2026-11-15, Declaration maps and the contract snapshot (implementation record; choices for review)

Evidence: `docs/evidence/m1a-client-step.md`, "Follow-up: declaration maps and the snapshot".
- **Declaration maps built:** every generated `.d.ts` ends with `//# sourceMappingURL=` and has a `.d.ts.map` (source map version 3) whose sources are the C# files, reached from the package folder (`../../../Program.cs`). Each declared name maps to its C# declaration: a record or error type to its name, a property to the record's parameter, an error case to its nested record, the contract object to the contract type, a command to its method. Observed through `tsserver`, the process editors talk to: go-to-definition on seven names in a page landed on the exact C# line each time. The plain language-service API returns the `.d.ts` position; the mapping to the source is applied by `tsserver`, so the test drives `tsserver`.
- **The contract snapshot built:** `<Project>.contracts.snapshot` next to each app, committed (`tests/apps/WireShapes/`, `samples/Notebook/`): the fingerprint, then each contract's commands with their request and result, each record's values and each error's cases, one per line, sorted, with C# names (BCL namespaces dropped).
- **Choices made while building, for review:**
  - **The property is `SparContractSnapshot`,** `Update` by default and `Verify` when `ContinuousIntegrationBuild` is `true`; CI now sets `ContinuousIntegrationBuild`.
  - **The committed copy is compared on every build, not only when the client step runs.** The step writes the expected text to `obj/spar/contracts.snapshot`; a small target in `Sdk.targets` compares the committed file with it (CRLF read as LF), copies it under `Update` and fails with SPAR100 under `Verify`. The first version did both inside the incremental step, and a stale snapshot passed a CI-mode build because the step was up to date.
- **Not built:** the manifest, the differential test against `tsc --declaration`, and maps for the `.js` (only declarations are mapped; go-to-definition uses declarations).
- **Tests:** `tests/Spar.Sdk.Tasks.Tests` 40 (snapshot text, a one-line diff for a new field, the go-to-definition probe, the map files in the package); `dotnet test` 229, the NativeAOT lane 154.

## 2026-11-15, Contract snapshot choices accepted (user decision)

The user accepted both choices of "Declaration maps and the contract snapshot" ("Accept them"): the property is `SparContractSnapshot`, `Update` by default and `Verify` when `ContinuousIntegrationBuild` is `true`; and the committed snapshot is compared on every build against the expected copy under `obj/spar/`, not only when the client step runs.

## 2026-11-15, Notebook page and the frontend build (implementation record; choices for review)

Evidence: `docs/evidence/m1a-notebook-page.md`.
- **Built:** the Notebook's page in `samples/Notebook/frontend/`, the frontend steps of the build (install, type-check, Vite build), a first `@spar/app/vite`, and `samples/Notebook.EndToEndTests` with seven journeys in Chromium against the real backend. `dotnet test` runs 236, all passing; the NativeAOT lane 154.
- **Choices made while building, for review:**
  - **The dialogs use the browser's modal `<dialog>`, not Radix.** Radix's modal Dialog and AlertDialog inject a `<style>` element to lock scrolling; the default CSP blocks it (`style-src-elem`), and the sample decision already excludes libraries that inject styles. `showModal()` gives focus containment, Escape and an inert page with nothing injected. Radix stays for the dropdown menu, non-modal (`modal={false}`), which injects nothing. This narrows the sample decision's "Radix for dialogs, menus and popovers".
  - **The Markdown preview builds React elements (`react-markdown`), with no HTML sink,** so it needs no Trusted Types policy. The sample decision expected an HTML sink to exercise the policy an app declares in C#; that declaration arrives with page security in M1b, and the preview can take the sink then.
  - **The frontend steps:** `npm ci --ignore-scripts --no-audit --no-fund` when the lockfile is newer than npm's record, under a lease in `obj/spar/` that the app's variants share; `tsc --noEmit` in every variant (`SparTypeCheck`); `vite build` into the variant's `obj/…/frontend/` in Production and Test, and in Development only with `SparDevServer=false`. The frontend folder is `SparFrontendPath` (`frontend/`). They run after the client step, because `package.json` links the generated package, and before `CoreCompile`.
  - **`@spar/app/vite` today sets only what a production build needs under the default CSP:** `assetsInlineLimit: 0`, no source maps, base `/`, target ES2022. It is compiled separately for Node and copied into the package with the SDK.
  - **The sample pins `postcss-selector-parser` to 6.1.6 with npm `overrides`** (a moderate advisory in the version the typography plugin pins), and the SDK moves to Vitest 4.1.12 (two moderate advisories).
  - **Until packing exists, the browser tests serve the Test variant's Vite output** from the app's `obj/` folder.
- **Found while building:** the client step re-ran on every build, because two of its Outputs were files written only when their content changed; the expected snapshot is now the variant's own file, written on every run, and `package.json` left the Outputs (a deleted package still reruns the step).

## 2026-11-15, Styles added at run time: a nonce and constructed stylesheets both work (evidence for M1b)

Spike `spikes/csp-style-injection` (user request, after the Notebook's dialogs moved off Radix). Nothing is decided here; the options go to the M1b page-security decisions.
- **Measured on Chromium 156, WebView2 157 and WebKitGTK 6.0 2.52.6, identical on all three:**
  - under Spar's base policy, a `<style>` element is blocked (`style-src-elem`) and a `style` attribute set from script is blocked (`style-src-attr`); constructed stylesheets (`replaceSync`, `replace`, `insertRule`, in the document or a shadow root), `insertRule` into the page's own stylesheet and `style.setProperty` apply with no violation;
  - with `'nonce-…'` added to `style-src`, a `<style>` with the right nonce applies and one with a wrong nonce or none stays blocked; the page reads the nonce from `<meta property="csp-nonce">` as `meta.nonce`, while `getAttribute("nonce")` is empty;
  - Radix's modal Dialog under the base policy opens and keeps focus but does not lock scrolling, with one violation per opening; with the nonce passed to `get-nonce`, or with `react-style-singleton` replaced by a module that adopts a constructed sheet, it locks and releases scrolling with no violation.
- **Options for M1b:** document constructed stylesheets as the way to add CSS from script under the default policy (no policy change, no host support); and consider an explicit C# opt-in for a per-document style nonce, which needs each host to mint the nonce and give it to the page (the meta-tag convention or the injected start-up script). `'unsafe-inline'` is not needed by either.
- **Change to an earlier spike:** `spikes/csp-navigation`'s shared host code supports `{NONCE}` in a profile's policy; its WebView2 host was republished.

## 2026-11-15, Frontend packing: the Deflate control keeps Brotli (evidence)

The research review asked for a per-file Deflate control on the real bundle before M1 packing (`docs/research-review.md`, "Asset codec control"). Measured in `spikes/asset-packing` ("Follow-up: per-file Deflate inside the same index"), Windows, NativeAOT, on the Notebook's bundle (772,422 B) and the spike's 2.8 MB bundle: Brotli gives the smaller executable by 46 KB and 155 KB (payload 6.2% and 15.1% smaller, decoder 168 KB against 192 KB), while Deflate decodes faster (0.9 against 1.5 ms, 3.3 against 5.0 ms, off the UI thread) with the same start time within noise. Packing stays per-file Brotli inside Spar's own index, as plan section 1 says; nothing else changes.

## 2026-11-15, Frontend packing built (implementation record; choices for review)

Evidence: `docs/evidence/m1a-frontend-packing.md`.
- **Built:** the `SparPack` task and its format, the asset checks, the third-party notices, the runtime reader, the embedding, and the `Browser` surface on the app's own packed page. `dotnet test` runs 250, all passing; the NativeAOT lane 156; the Notebook's journeys now run on the page its executable carries.
- **Choices made while building, for review:**
  - **The format is Spar's own:** `SPARFE`, a version byte, an entry count, then per entry the encoding (stored or Brotli), path, content type, offset, stored and original lengths, then the data, entries sorted by ordinal path. Unlike the spike's index it carries no per-file SHA-256, which nothing read.
  - **The asset checks fail the build with SPAR110** (category `Spar.Frontend`), one error per file: source maps; any hidden file or folder (version control, `.env`, `.npmrc`); key and secret files by extension or name (`.pem`, `.key`, `.p12`, `.pfx`, `.jks`, `.keystore`, `.kdbx`, `.env`, `id_rsa` and its kin); links; names that differ only by case; and file types the hosts do not serve. SPAR110 is a new ID after the snapshot's SPAR100 and the CSP analyzer's SPAR101–104.
  - **The third-party notices come from Vite's `build.license`,** which `@spar/app/vite` now turns on: `.vite/license.md`, Brotli-compressed, as the resource `Spar.ThirdPartyNotices`. Nothing under `.vite/` is packed or served. WebView2's and the static loader's notices join them with the Windows host.
  - **The runtime finds the pack through `SparCatalog.AppAssembly`,** set by the generated catalog, because in a test the entry assembly is the test's.
  - **`o.Page()` serves the app's packed page;** `o.Page(folder)` stays for pages that are not the app's. The test host's core cannot reach the `Browser` package unless the test calls into it, so the surface still needs this one call.
  - **Spar's build steps now run before `PrepareResources`** instead of `CoreCompile`, so the pack is an embedded resource before MSBuild settles resources.

## 2026-11-15, Source maps stay out of the executable; third-party notices are reported and copyleft is flagged (user decisions)

- **Source maps:** asked whether an app that turns on devtools in Production should ship them, the user concluded they are needed only in development ("they may be needed only on loopback"). So the rule stands: the packing step refuses `.map` files (SPAR110) in every packed variant, and Debug gets its maps from the Vite dev server (section 4). The generated `.d.ts.map` files, which point into the C# contracts, are editor-only: they live in `obj/spar/client`, Vite does not bundle declarations, and no executable needs them.
- **Third-party notices** (user chose "Also flag copyleft" over an informational report alone and over saying nothing):
  - The build prints one informational line, never a warning (warnings fail an app built from the template): how many packages the notices cover, where they came from, and their size.
  - `SparThirdPartyNotices` replaces the collected notices with the team's own file (a path), or turns them off (`false`); the default (`true`) takes Vite's licence file.
  - `spar doctor` lists the embedded notices, and flags packages with no licence and packages under a copyleft licence (GPL, AGPL, LGPL). This revises the 2026-11-14 note, under "Bundle budget", that Spar has no built-in licence check; the docs still point to dedicated tools for a full licence policy.
- **Built now:** the build line and `SparThirdPartyNotices`. `spar doctor` does not exist yet; its check is in its design (section 8).

- **Built with it (2026-11-15):** the informational build line (for the Notebook: "the third-party notices of 109 packages from Vite's licence file (3,057 B)") and `SparThirdPartyNotices` (`true`, `false`, or a file; a missing file fails with SPAR111). Checked in real builds: all three settings, a switch back to the default, and an unchanged build.
- **Found on the way and fixed:** the embedding target ran before packing. Spar's steps were each hooked `BeforeTargets="PrepareResources"`, and MSBuild ran them in the order they are declared in `Sdk.targets`, whatever `AfterTargets` said; every build had embedded the pack left by the build before, and a clean clone's first build would have had no page. One entry target, `SparBuildSteps`, now lists the steps in order through `DependsOnTargets`; a clean Test build embeds its pack on the first pass.

## 2026-11-15, Build tests: a clean copy, the step matrix from binary logs, the production boundary (implementation record; choices for review)

Evidence: `docs/evidence/m1a-build-tests.md`.
- **Built:** `tests/Spar.BuildTests`, three tests on one copy of the working tree: a clean first build of the Notebook and its tests (every incremental Spar step runs, the page, notices and client are there, no authored file changes), an unchanged build that runs none of them, and the Production NativeAOT publish searched for the test host, the loopback host and the test libraries (none found; a control finds them where they belong). 3 of 3 in about 69 s.
- **Numbers:** unchanged Debug build of the Notebook 0.57-0.67 s with node reuse and the MSBuild server, 1.47-1.52 s fully isolated, 1.63-1.64 s isolated with a binary log; the 1.5 s budget holds in the developer's case.
- **Choices made while building, for review:**
  - **A copy, not `git clone`:** the files `git ls-files --cached --others --exclude-standard` lists, so uncommitted work is tested too and nothing needs a commit first. Under `artifacts/build-tests/<run>/`, kept after the run for inspection.
  - **The step matrix reads a diagnostic replay of each binary log** for `Building target` / `Skipping target` lines of Spar's targets, rather than a binary-log reader package: no new dependency. It sees only targets with inputs and outputs, which are the ones that can be skipped; the test also requires each expected step to appear, so a renamed target fails it.
  - **The no-op time is recorded in `logs/timings.txt`, not asserted:** the isolated build the tests need pays for starting MSBuild and sits at the budget, and machine speed varies. A budget check under a warm server belongs with the performance lane (M4).
  - **Not in `Spar.slnx`;** CI runs them as their own step after the `-mt` build.
  - **"Authored" is every file outside `bin/`, `obj/` and `node_modules/`;** a build that writes or adds anything else fails the test, the contract snapshot included.

- **Revised the same day (user, 2026-11-15):** asked how many copies the build tests leave, what happens as the tree grows, and what happens when `node_modules` is not ignored, the user agreed to three changes. (1) At most one run on disk: a run deletes the earlier runs under `artifacts/build-tests/` when it starts, and deletes its own copy and publications when every test passed; a failed or interrupted run keeps them. The diagnostic replays (20-40 MB per build) are deleted once read; the binary logs stay. Before: four runs, 2.4 GB; after a passing run: 4.3 MB of logs. (2) The copy holds only the root files and `src/`, `samples/`, `tests/` and `eng/`: `spikes/` was 4,077 of the 4,272 files copied. (3) A run fails before copying when git lists any file inside a `bin/`, `obj/` or `node_modules/` folder, naming the count and five examples; without the `node_modules/` line in `.gitignore` that was 50,694 files. All three paths were checked by running them (a deliberate failure kept a 423 MB copy; the guard; a pass).

## 2026-11-15, Contract diagnostics located in the analyzer (implementation record; choices for review)

Evidence: `docs/evidence/m1a-analyzer-diagnostics.md`.
- **Built:** the analyzer reports every diagnostic of the starting map that one compilation can raise, at its declaration, as you type: SPAR003, SPAR004, SPAR005, SPAR015, SPAR016, SPAR021–SPAR023, SPAR031, SPAR032, SPAR034 and SPAR040 join the ones already there. `[SparName]` on a record or error type now names it in TypeScript (the gap noted in "Client step: reserved type names"). 269 tests pass; the NativeAOT lane passes.
- **Choices made while building, for review:**
  - **SPAR003 is a name that breaks the rule, SPAR004 a reserved one,** as in the spike's map. The client step had used SPAR003 for reserved names and SPAR040 for a type named like an SDK import; both are now SPAR004, and SPAR040 is only two types with one name. Contract and command names and error codes must be camelCase ASCII (a lower-case letter, then letters and digits), derived ones too, so `Copy_JobsService` is SPAR003 at the class; type names must be ASCII letters and digits.
  - **The name lists live in the generator (`ClientNames`), and the emitters write with them,** so a name the generated modules start using is checked without a second change.
  - **Decoders are named `decodeGet`** instead of `getDecoder`: a command `readItem` and a type `ItemDecoder` would both have made `readItemDecoder`. With the prefixes `read`, `write` and `decode`, only the contract's own object can clash with a module name, and SPAR004 checks that.
  - **One analysis per compilation:** the analyzer reads every contract when the first type is analyzed, and each problem is reported by the type that contains it. A record two contracts use is reported once, in its own file.
  - **A contract in a cross-contract problem is left out** of the generated C# and the client; within a contract, an invalid command is still the only thing left out.
  - **The client step writes nothing while the contracts have a problem,** and says so in one line: writing a package without the invalid parts would let the frontend's type-check fail first, on names the developer did not touch, and would rewrite the contract snapshot. This replaces "the client step skips invalid contracts silently". A clean clone whose contracts have a problem has no client until they are fixed.
- **Not built:** contracts from referenced assemblies, with their half of the clash matrix (next).
- **Revised the same day (user, 2026-11-15):** asked whether keeping the previous client is right, the user agreed that it did not do what it was for. The frontend steps run before the C# compile, so when the frontend already uses a name the contract change adds, the type-check against the previous client fails first, on a name written correctly, and the SPAR error never shows. The client step now reports the contract problems itself, with the analyzer's own diagnostics (`ContractProblems`, shared by both: same ID, message and place), and fails the build. The build stops before the frontend steps and the C# compile, so each problem is reported once; nothing is written, so the previous client and the snapshot stay on disk, unused. Other C# errors show once the contract problems are fixed. Checked in a real build: a command renamed in C# and in the Notebook's page, plus a `string[]` request elsewhere, gave only `PagesService.cs(42,35,42,39): error SPAR015`, with the client and the snapshot unchanged.
- **Accepted (user, 2026-11-15):** "Yep, go on" to the five remaining choices of this entry: SPAR003 for a name that breaks the rule and SPAR004 for a reserved one, with ASCII names; the name lists in the generator; `decodeX` decoders; one analysis per compilation, reported by the containing type; a contract in a cross-contract problem left out.

## 2026-11-15, Contracts from referenced libraries: the app opts in (user decision; built)

The spike exported every public contract of every referenced assembly that references Spar. Asked how an app should expose a library's contracts, the user chose "Opt in per library" over the spike's automatic export and over automatic export for project references only: an automatic export lets a dependency, even a transitive package, add commands the page can call without the app saying so (`AGENTS.md`: a security risk is blocked by default).
- **The rule:** `<SparContracts Include="Lib" />` in the app's project, by assembly name (case ignored). Only the listed libraries' public contracts are served; their dispatch, serializers, TypeScript modules and DI registration are generated in the app, as for its own. A library runs no generator.
- **SPAR006** (new): a listed name the project does not reference, a listed library that declares no contract, and a non-public contract in a listed library. Problems inside a listed library are the usual diagnostics, with no location, their messages starting "In referenced assembly 'X': …". In the build, the client step reports them against the app's project file; in the editor, the analyzer reports them when the compilation ends.
- **Clashes** follow the spike's location rules: a clash between the app and a library is reported at the app's declaration only; between two libraries, once, with no location. A local type named like a library type in one contract is SPAR040 at the local type, fixed with `[SparName]`.
- **How it is passed:** `Spar.Sdk` turns the items into the property `SparContractAssemblies` in a target before `GenerateMSBuildEditorConfigFileCore` (items do not exist when properties are evaluated), so the generator and the analyzer read it from the compiler's config, design-time builds included; the client step gets it as a parameter, and its inventory includes it, so a changed list reruns the step.
- **Found while building:** a library referenced as a project has source locations of its own, in its compilation; "in source" had to become "in this compilation" (`Compilation.ContainsSyntaxTree`), or its problems were filed under types the project never reports. And the client step crashed logging a problem with no location (`GetLineSpan().Path` is null for `Location.None`), found only by a real build.
- **Evidence:** `docs/evidence/m1a-referenced-contracts.md`.

## 2026-11-15, Bridge limits, admission and exception redaction: the behaviour (user decisions)

Asked before building the M1a items "bridge limits and admission" and "exception redaction in Release" (research review, section 2), the user chose the recommended option each time:
- **Over the in-flight limit, queue then refuse** (over refusing at once): a call past the calls in flight of its document waits in a small bounded queue for that document and starts when a slot frees; it is refused only when the queue is full, so a page that fires a burst of calls at load still works.
- **A call these limits refuse is `rejected` / `busy` or `rejected` / `too-large`, `notStarted`** (over `unavailable`): the same kind as the other refusals before a command runs.
- **The correlation id is the call's W3C trace id** (over a random id per failure): the one in its `traceparent`, or a new one when the page sent none, so the same id finds the log entry and, with telemetry on, the trace.
- **The details go to `ILogger` from the app's services** (over the call's `Activity` only), with an exception event on the call's `Activity` as well; nothing is written when the app adds no logger.
- **Still to set by measurement in M1a:** the maximum message size, the calls in flight and the queue per document.

## 2026-11-15, Bridge limits built: the numbers, admission and redaction (implementation record; choices for review)

Evidence: `docs/evidence/m1a-bridge-limits.md`; measurements in `spikes/bridge-limits`.
- **Built:** `BridgeOptions` (`builder.Bridge(o => …)`, each value at least 1), the size check before decoding, slots and a FIFO queue per document, the result-size check, the correlation id in failure payloads, logging through the app's `ILogger`, `CallError.correlation` and `CallError.detail` in the SDK, and 1 MiB batches on the page's loopback transport. `dotnet test` 309 of 309; the NativeAOT lane passes.
- **The numbers, from the measurements** (a call waiting or running holds about 620 bytes besides its message; taking a message in allocates about 3× its size at 0.5 ms per MiB; the page parses a result at 0.35 ms per MiB; Kestrel refuses bodies over 28.6 MiB by default):
  - **`MaxMessageBytes` 4 MiB:** a large Markdown page or a recipe fits with room; taking one in costs about 2 ms and 12 MiB.
  - **`MaxResultBytes` 16 MiB:** the page parses it in about 6 ms; larger transfers belong in streams (M3).
  - **`CallsInFlight` 16:** not a memory bound (a call is cheap) but a bound on how much of the app's work one page can start at once.
  - **`QueuedCalls` 1,024 and `QueuedBytes` 16 MiB:** the count was 64 at first, and the existing browser test that sends 200 calls at once failed with `busy`; since the bytes are bounded separately, the count only bounds bookkeeping, about 0.6 MB at 1,024.
  - What one document can make the app hold is then about 16 × 12 MiB running plus 2 × 16 MiB waiting (as .NET strings), about 230 MiB.
- **Choices made while building, for review:**
  - **An oversized message is answered by the id in its header** when the TAB is within its first 16 K characters and the header decodes alone; otherwise it is dropped, like any message whose id cannot be trusted.
  - **Refusals carry no correlation id and log no error;** `too-large` logs a warning, `busy` a debug entry (a page under load would flood a warning).
  - **The failure payload is the place for the correlation id,** not a header field: the header stays the decided `kind`, `code`, `execution`.
  - **On loopback the page caps a request at 1 MiB of text** and the server's body limit follows `MaxMessageBytes` (twice it, at least Kestrel's 28.6 MiB), so an oversized call fails alone rather than ending the channel.
- **Found while building:** the client step's writes raced between an app's variants, which share `obj/spar/client`; it showed once, when an SDK rebuild made both variants rewrite every file (a `SafeFileHandle.CreateFile` failure in `ClientPackage.Sync`). The client step now writes under the same kind of lease as the npm install (`FileLease`). Not seen to wait in four later builds, so the overlap itself is unverified. Also open: two variants with different `DefineConstants` could write different clients into that one folder.
- **Not run:** WebView2's and WebKitGTK's own message size limits (M1b, M1c); the production boundary with the bridge in it (the Production build has no host yet, so the session is trimmed away).
- **Accepted (user, 2026-11-15):** "Accept them": the numbers (4 MiB messages, 16 MiB results, 16 in flight, a queue of 1,024 calls and 16 MiB) and the four choices of this entry (an oversized message answered by the id in its first 16 K characters, else dropped; no correlation on refusals, a warning for too-large and a debug entry for busy; the correlation in the failure's payload; 1 MiB loopback requests).

## 2026-11-15, Bridge instrumentation built (implementation record; choices for review)

The M1a item "the BCL instrumentation (section 6b)": one server `Activity` and one `spar.call.duration` sample per bridge command, BCL only. Evidence: `docs/evidence/m1a-bridge-instrumentation.md`.
- **Built:** `src/Spar/Bridge/BridgeTelemetry.cs`, started and ended by `BridgeSession` around each command; `tests/Spar.Tests/BridgeTelemetryTests.cs` (5, in a collection that runs alone, since listeners are process-wide). `dotnet test` 314 of 314; the NativeAOT lane passes.
- **Choices made while building, for review:**
  - **The duration is in seconds** (the spike recorded milliseconds), as OpenTelemetry's conventions ask of durations.
  - **The metric carries `spar.outcome`** (`success`, `error` for a declared error, or the failure's kind), a handful of values, so a dashboard can split failures out; the code goes on the span as `error.type`, not on the metric.
  - **Queue time is not in the duration:** it starts when the command gets its slot. The span starts there too.
  - **With an activity, the correlation id is its trace id,** so the page, the log entry and the trace always name one id; without one, the `traceparent`'s trace id or a new one, as decided.
  - **The span never inherits an ambient activity:** `Activity.Current` is cleared before it starts, because the host's receiving thread (a Kestrel request on loopback) and a call that freed a slot (whose task starts the next queued call) would otherwise become its parent.
  - **An oversized result is recorded as the failure the page gets** (`spar.outcome=response`), not as the success the command returned.
  - **The instrumentation lives in the bridge,** so a command called through the test host's command surface has no span; it is the bridge's view of a call.
- **Measured** (20,000 echo calls through one session, Debug, three rounds each): 2.7–3.7 µs and 3,935–3,949 B per call with no listener, 2.3–2.9 µs and 4,915–4,919 B with both listeners; time within noise, about 970 B more per call when listened to. A build without the instrumentation was not measured, so "no work without a listener" rests on the code (one `HasListeners` and one `Enabled` check), not on a comparison.
- **Not measured:** the size it adds to a Production executable, which has no bridge yet (the spike measured +105 KB).

## 2026-11-15, Bridge instrumentation: every call, from receipt, with a flood budget (user decisions; built)

Reviewing "Bridge instrumentation built", the user asked why queue time was not counted, then what happens to calls that are not admitted, and proposed recording every call and holding back only when refusals flood. Agreed ("yes, build it this way"); this replaces that entry's choices on queue time and on the scope of the instrumentation.
- **Queue time counts.** A call's span and its `spar.call.duration` sample start when its message arrives, as OpenTelemetry's RPC server duration does; a `started` event and the `spar.queue.seconds` tag show when it got its slot, so one trace shows both the wait and the run. No second metric for the queue.
- **Every call that names a command records one duration sample,** refused ones included, tagged `spar.outcome` (`success`, `error`, or the failure's kind) and, for failures, `error.type` (the code: `busy`, `too-large`, …). A histogram sample is aggregated in memory, so a flood adds no export.
- **Names the catalog does not know are `unknown`,** in span names and tags: the page chooses them, and every distinct tag value is a new metric series.
- **Spans and logs of refusals at the door have a per-document budget:** a burst of 20, then 5 per second (a token bucket; numbers by judgment, for review). Past it, refusals are counted but not traced or logged; the first one let through afterwards, or the document's close, logs how many were left out. Admitted calls, those cancelled or failed while waiting included, always get their span.
- **Untouched:** messages whose id cannot be trusted name no call, and stay out of the metrics (the host's dropped-message log only).
- **Built and checked:** `BridgeTelemetry` is now one object per document; 9 telemetry tests (queue time in the span and the duration, a busy refusal counted and traced, unknown names, the flood budget on a manual clock, and the earlier five). `dotnet test` 318 of 318 twice; the NativeAOT lane passes.
- **Accepted (user, 2026-11-15):** "Accept them": the refusal budget of a burst of 20, then 5 per second, per document.

## 2026-11-15, Static-state test built (implementation record; choices for review)

The M1a item "the static-state test of Spar's assemblies" (plan section 5, "Rules for Spar's own code": "Spar keeps no process-wide mutable state; a test lists every static field of Spar's assemblies against a reviewed list").
- **Built:** `tests/static-state/StaticStateTests.cs` and `tests/static-state/reviewed.txt` (64 fields, each with why it is safe). Two tests: every static field is in the list with the right `readonly` or `writable`, and every entry has a reason. A field added, removed or made writable fails the first with the line to add or remove. Checked by adding a writable `static int` to `SparJson` for one run: it failed with "not reviewed: Spar | Spar.Runtime.SparJson.s_calls | writable". `dotnet test` 324 of 324.
- **What the list holds today:** one writable field, `TestDataRoots.s_next`, changed only through `Interlocked`; the rest are readonly: log message delegates, the "Spar" activity source, meter and histogram, constants and options, collections filled once, the test-host switch and its `AsyncLocal`, two thread-safe caches keyed so any project may share an entry (`Descriptors.Referenced`, `ContractSource.ReferenceCache`), and the regex generator's data.
- **Choices made while building, for review:**
  - **Six assemblies,** not only the four in an app's process: `Spar`, `Spar.Loopback`, `Spar.Testing`, `Spar.Testing.Browser`, and also `Spar.Generator` (it runs in the compiler for every project at once) and `Spar.Sdk.Tasks` (the decision on the task assembly asks for no unsynchronized static state, and nothing checked it).
  - **The fields are read from the assemblies' metadata** (`System.Reflection.Metadata`), not by reflection: `Assembly.GetTypes` is unsafe for trimming, which fails the build, and metadata shows every field, even ones the trimmer would remove. So the test needs the assembly files and runs in the CoreCLR lane only; the NativeAOT lane leaves the file out by an MSBuild condition, not `#if`.
  - **One shared file, linked into three test projects** (Spar.Tests, Spar.Sdk.Tasks.Tests, WireShapes.EndToEndTests), each checking the Spar assemblies in its own output, so every one of the six is checked by some project without Spar.Tests referencing the Browser package.
  - **Compiler data is left out:** `<Module>`, `<PrivateImplementationDetails>`, and the lambda and method-group delegate caches (`<>c`, `<>O`), which hold delegates created once. Types a source generator writes stay in the list, so a `file`-local class Spar adds is never hidden.
  - **A readonly field holding a collection is listed as readonly** with "filled once" as its reason: the metadata shows the field, not whether something writes into the collection. That stays a review point; making those collections frozen would let the test prove it.
- **Accepted (user, 2026-11-15):** "Accept them": the five choices of this entry (six assemblies, metadata reading in the CoreCLR lane, one shared file linked into three test projects, compiler data left out, readonly collections listed as "filled once").

## 2026-11-15, Test host: the per-app log and the clock (implementation record; choices for review)

Two of the M1a test-host items (plan section 5: "each app instance has its own log sink"; "an injectable `TimeProvider`").
- **Built:** `SparTestApp.Log` and `LogDropped`, `SparTestOptions.Time(TimeProvider)` and `SparTestApp.Time`; Spar registers `TimeProvider.System` in every app's services (`TryAdd`), the dispatcher exposes the app's clock, and the bridge session uses it. Tests: `tests/WireShapes.Tests/HostTests.cs` (3) and a browser test in which a command throws: the page gets `failed` with a 32-hex correlation, and `app.Log` has one Error entry naming `echo.ratio` and that correlation, with the `FormatException`. `dotnet test` 328 of 328; the NativeAOT lane passes.
- **Choices made while building, for review:**
  - **The per-app log holds what Spar logs for that instance,** written by the bridge to both the app's own `ILogger` (when it has one) and the test's log. The app's own logging is not redirected: adding a logger provider or factory to the app's services in tests only would let an app pass its tests with services it lacks in production.
  - **The command surface logs nothing:** it calls dispatch directly and the test reads the outcome; the log is for what the bridge does (Browser tests, and the window tests of M1b).
  - **Every level is kept, the newest 10,000 entries,** with a count of the dropped ones; timestamps come from the app's clock.
  - **`TimeProvider.System` is registered by default in every app,** not only in tests, so app code can inject a `TimeProvider` the same way in both; `o.Time(…)` replaces it, and Spar's bridge uses the same clock.
- **Not built, and needing a decision:** the fixture rules (assertion, capture and teardown failures reported together; unresolved cleanup is not a pass; an empty test selection fails; skips are explicit). Today a test whose body fails and whose app then fails to stop loses the assertion: `await using` lets the disposal's exception replace it. Reporting both needs a hook in the test framework, which `Spar.Testing` does not reference.
- **Accepted (user, 2026-11-15):** "Accept them": the four choices of this entry (the per-app log holds what Spar logs; the command surface logs nothing; every level, the newest 10,000, timestamped by the app's clock; `TimeProvider.System` registered in every app). The fixture rules stay open.

## 2026-11-15, Fixture rules: the teardown hook (user decision; built)

The fixture rules of the research review (plan section 5): assertion, capture and teardown failures reported together; unresolved cleanup is not a pass; an empty test selection fails; skips are explicit. The problem behind the first two: `await using var app = …` disposes in a `finally`, so when the test body fails and the app then fails to stop, the stop failure replaces the assertion that explains it.
- **The user chose an xUnit hook** ("xUnit hook in Spar.Testing", the recommended option) over a framework-free `RunAsync` helper, which changes how tests are written and leaves `await using` able to hide a failure, and over documenting a fixture field only.
- **Found before building:** the two lanes use different xUnit assemblies (`xunit.v3.core.dll`, and `xunit.v3.core.aot.dll` in the NativeAOT lane), and their `BeforeAfterTestAttribute` differ (`Before(MethodInfo, IXunitTest)` against `Before(ICodeGenTest)`, both returning `void`). A `Spar.Testing` compiled against one could not run against the other. So:
  - **`Spar.Testing` stays framework-free:** `SparTestCleanup` keeps a failure to stop for the current test when a hook watches it; `SparTestApp.DisposeAsync` runs every step (surface, app, data root) even after one fails, and keeps the failures there, or throws them when nothing watches, so a failure to stop is never a pass.
  - **The hook is a source file `Spar.Sdk` compiles into each test project,** one per lane (`Sdk/Testing/SparTeardownHook.Xunit.cs`, `…XunitAot.cs`), chosen by the same condition that chooses the xUnit package; `SparTeardownHook=false` leaves it out. Before each test it registers the test; after it, it throws what was kept.
- **Observed, in both lanes,** with a probe test written for the run and deleted: a failing body with an app that failed to stop reported both, the assertion and "The app did not stop cleanly after the test." with the dispose failure inside; a passing body with an app that failed to stop failed. Kept as tests: with the hook, the failure is kept and not thrown, and the data root is still deleted; unwatched, it is thrown.
- **An empty selection:** `dotnet test` already fails it (exit code 8, "No tests ran"); xUnit's in-process runner, which the NativeAOT lane runs, exits 0 with "Total: 0". The CI step now fails such a run.
- **Skips:** xUnit requires a reason for a skip and reports skipped tests separately; nothing was added. **Capture failures:** there is no capture to report yet (window screenshots arrive in M1b).
- **Static state:** two fields, reviewed: `SparTestCleanup.Watched` (keyed by each test's id) and `s_currentTest` (set once by the hook). `dotnet test` 330 of 330; the NativeAOT lane passes.

## 2026-11-15, Frontend packing: stored below the decoder's cost (implementation record; choices for review)

The M1a item "the decoder trim feature switch" (`spikes/asset-packing`: the Brotli decoder costs a NativeAOT executable about 168.5 KB, and the switch `Spar.Frontend.Brotli=false` removed it, measured on Windows). Evidence: `docs/evidence/m1a-frontend-packing.md`, "Follow-up: stored below the decoder's cost".
- **Built:** the packing step encodes each file once, adds up what Brotli saves on the page and on the notices, and packs everything stored when that is under the threshold; the reader's decoder sits behind `[FeatureSwitchDefinition("Spar.Frontend.Brotli")]`, which `Sdk.targets` turns off, with `Trim="true"`, for a stored pack. One line says which and why ("the page is packed with Brotli (Brotli saves 529,922 B, more than its decoder costs)").
- **Choices made while building, for review:**
  - **The threshold is 192 KiB of savings,** the decoder's 168.5 KB plus a margin of about 28 KB, counted over page and notices together, since either one keeps the decoder in the executable.
  - **`SparFrontendCompression` = `auto` | `brotli` | `none`,** `auto` by default; any other value fails the build with SPAR112. It is part of the packing step's settings, so changing it repacks.
  - **The decision is written to `obj/…/spar-pack.mode`** and read on every build, so a build that skips packing as up to date still sets the switch.
  - **The notices resource now starts with an encoding byte** (0 stored, 1 Brotli), so it reads correctly whatever the switch, including in a test executable, which does not take the app's switches.
- **Checked:** the Notebook stays on Brotli (529,922 B saved); with `SparFrontendCompression=none` it packs 11 files stored (773,763 B), the notices stored (123,418 B), its runtime config holds `"Spar.Frontend.Brotli": false`, and its unit tests (12) and browser journeys (7) pass on the stored page; back on `auto`, the switch is gone again. `dotnet test` 336 of 336; the NativeAOT lane passes.
- **Not verified here:** that the trimmer removes the decoder from a Spar app: the Production executable reads no page until a desktop host exists (M1b), and a test executable does not take the app's switches. The spike verified the same switch and condition.

## 2026-11-15, The documentation check under tests/ (user decision; first step built)

The M1a item "the documentation check of `docs/app-model.md`, moved from `spikes/app-model-check` into `tests/` and run by `dotnet test`". Asked what the examples should compile against, the user chose real Spar, with stand-ins only for what is not built yet ("Real Spar, stand-ins for the rest"), over moving the stand-in check as it is.
- **What real Spar cannot take yet:** the document's `copyJobs` and `status` contracts return streams and subscriptions, and its recipe and export contracts use `FileGrant`; Spar's analyzer rejects both (SPAR010, SPAR020) until M3, and an analyzer error cannot be stood in for. So the blocks that use them, and the page code built on the `copyJobs` module, stay with the stand-ins until then.
- **First step, built:** the check moved as it is to `tests/AppModel` (its record to `docs/evidence/app-model-check/`), and `tests/AppModel.Tests` (in `Spar.slnx`) runs it: each npm script (verbatim copies, contract rules, TypeScript 7 and 6, the 21 deliberate mistakes on both, the run-time routing) is one test, plus the C# build with `Program.cs` run, and the deliberate missing-case build failing with exactly CS8509. 9 tests, about 9 s; CI installs the check's packages. Changing one copied file made the verbatim test fail with "26 blocks, 1 differ". Under `tests/` the repository's stricter build rules now apply to it, and it built with no warning; the size check covers it (178 files).
- **Next step:** the document's blocks that real Spar can build, as a real Spar app built with the SDK and the generator, its page type-checked against the real generated client, with stand-ins only for the builder calls and page APIs not built yet.
- **Second step, built (2026-11-15):** `tests/AppModel/Real` builds the document's app with real Spar: its SDK in source mode, the C# blocks as the check copies them, through the real analyzer, generator and client step. Three contracts are real today (`connections`, `crashReport`, `script`), with the snapshot committed; `Program.cs` runs through the real `SparApplication.Create` and its interceptor; the check's own exhaustive switches run over the real `Result` union. Left out: `copyJobs` and `status` (streams, subscriptions), `recipes` and `csvExport` (file grants), and the two deliberately failing files. `tests/AppModel/Planned` (`Spar.Planned`) stands in for the rest: the builder calls (`Window`, `Tray`, `Shell`, `ContentSecurity`, `Permissions`, as extension members on the real `SparBuilder`) and the tray, clipboard, taskbar, system-event, network, health and file types, by linking the stand-in check's files, two of which were split for it (`Window.cs` out of `SparApplication.cs`, `Health.cs` out of `Diagnostics.cs`). One check file changed: a `Result` switch takes the plan command as a function instead of `CopyJobsService`. A test builds it and requires exactly those three contracts; with a copied contract renamed `shared`, the real build failed with SPAR004 while the stand-in build passed. `dotnet test` 346 of 346; `-mt` and the size check (181 files) clean.
- **Choice for review: the TypeScript blocks stay with the stand-ins for now.** None of the page blocks real Spar could serve imports a generated module (the ones they use are `copyJobs`, which streams, and `windows`, not generated yet), and the page APIs they call (dialogs, drop, print, `ready()`) are not built, so a real-Spar TypeScript build today would be almost all stand-ins. They move as those modules and page APIs land (M1b, M3).

## 2026-11-15, The frontend-framework checks in the SDK's tests (user decision; built)

The M1a item "the React, Angular and Vue checks that now live in `spikes/frontend-frameworks`", which ran against a prototype SDK. Asked how to rebuild them against the real `@spar/app`, the user chose the SDK's own Vitest suite ("In the SDK's Vitest suite", the recommended option) over a separate browser project with real framework builds (the Angular toolchain alone is about 83 MB, and each run builds three apps).
- **Built:** `src/Spar.Client/test/frameworks/`, against the real SDK through a contract module in the generator's shape and the scripted `FakeHost`:
  - **React 19.3** (`happy-dom`): `use(call)` suspends and shows the value through `match` with one backend call; a declared error renders through `match` with no error boundary; `use(call.unwrap())` hands the boundary a `SparError`; a `Call` is a `Promise` and not frozen.
  - **Vue 3.5 and Pinia 4.1:** `ref`, `reactive`, a reactive array and a Pinia store keep a `Result` raw, so `match` reads its private state; `ref(call)` stays awaitable; `shallowRef` keeps the type with no cast.
  - **Angular 22.2** (`createApplication`, zoneless, `happy-dom`): `resource()` loads through a `Call` and holds the `Result`; a change of params aborts the previous call, which the page cancels with the host.
  - **Types** (TypeScript 6 and 7, `npm run check`): TanStack Query infers `data` as the `Result`; Angular's resource loader takes a `Call`; a `Call` is a `Promise`, so `no-floating-promises` flags a forgotten call by default.
- **Checked that they bite:** asserting `Plan` as TanStack's `data` failed with TS2344; removing `Result`'s `Symbol.toStringTag` getter failed the three Vue tests that hold a `Result` in `ref`, `reactive`, an array or Pinia. SDK: 141 Vitest tests (13 new), `npm run check` and `npm run build` clean.
- **Dev dependencies added to the SDK** (not shipped): react, react-dom and their types, @tanstack/react-query, vue, pinia, @angular/core, @angular/common, @angular/compiler, @angular/platform-browser, rxjs and happy-dom, at the spike's versions (happy-dom 21.0.4); `node_modules` grew from 98 MB to 183 MB. Angular needs its JIT compiler outside the CLI (its packages ship partially compiled), and `resource()` needs an application's injector, not a bare one.
- **Not rebuilt here:** the frameworks under the strict CSP (they concern the frameworks, not the SDK: page security, M1b), the ESLint runs (the `Promise` type test stands for them), RxJS and React StrictMode guidance (documentation for app authors, plan 2b), and the Vue type augmentation, which does not exist yet. `spikes/frontend-frameworks` stays as the record: deleting spikes is still the M0 step left to the user.

## 2026-11-15, Packages for the package-only consumer (user decisions)

Asked before building Spar's packages and the M1a package-only consumer test (plan section 1, "Packages"; decision "Package-only consumer"), the user chose the recommended option each time:
- **An end-to-end test project asks for the Browser surface with a property,** `<SparBrowserTests>true</SparBrowserTests>`, and `Spar.Sdk` adds `Spar.Testing.Browser` at its own version, so the versions never drift (over an explicit package reference the project keeps in step itself).
- **Packages built from this repository are `0.1.0-dev.N`, unique per pack,** so no version is ever reused (over a fixed `0.1.0`, which the decisions forbid); the release scheme stays open for M3.
- **No `dotnet new` template in M1a:** the consumer test writes its app, test and end-to-end projects itself, with the generality fixtures; the template comes when its shape is settled.
- **The M1a packages:** `Spar.Sdk` (the MSBuild project SDK: props and targets, the task assembly and the generator in `tools/`, the compiled TypeScript SDK), `Spar`, `Spar.Loopback`, `Spar.Testing` and `Spar.Testing.Browser`. `Spar.Windows` and `Spar.Linux` arrive with their hosts.

## 2026-11-15, Packages and the package-only consumer test built

Built as decided in "Packages for the package-only consumer"; the record is `docs/evidence/m1a-package-consumer.md`.
- **Five packages** at `0.1.0-dev` (a pack passes its own suffix): `Spar.Sdk` (3.8 MB: props and targets, `tools/` with the task assembly and the generator, the compiled TypeScript SDK), `Spar`, `Spar.Loopback`, `Spar.Testing`, `Spar.Testing.Browser`. Every other project is not packable.
- **Package mode in the same props and targets as source mode:** Spar's runtime and test packages at the SDK's own version, written into the package at pack time with the xUnit version Spar was built with; the generator from `tools/` as an `Analyzer` item; the tasks from `tools/` with `Runtime="NET"` and no task host, since a package's task assembly never changes under a running build. The generated `@spar/app` package carries the SDK's version, and the client step's inventory lists it, so an update or a downgrade reruns the step.
- **`tests/Spar.PackageTests`,** outside `Spar.slnx` and run in CI after the build tests: two packs at fresh versions into a run's own feed, a consumer in a temporary folder with a space and non-ASCII letters, an assembly name different from its project, `<clear />` and source mapping, its own package folder and MSBuild salt. Five tests: build and test, the analyzer's located SPAR001, a quiet design-time build, a NativeAOT publish that runs, and an update and a downgrade under a warm MSBuild server. 5 of 5, 35–45 s with the HTTP cache warm (2 min 34 s for the first run, which filled it with 151 MB). The solution's 346 tests, the `-mt` build, the build tests and the size check pass.
- **Not covered yet:** a frontend and the Browser surface in the consumer, a custom app id, Linux, CI's own run, and a consumer with central package management.

## 2026-11-16, The generated package keeps version 0.0.0; the consumer gets a page (user decision)

- **`@spar/app`'s `package.json` keeps `"version": "0.0.0"`, and the SDK's version goes in a `sparSdk` field** (user, over keeping the SDK's version as `version`; this replaces that part of "Packages and the package-only consumer test built"). npm writes a linked package's name and version into the app's committed `package-lock.json`. Observed with npm: `npm ci` accepted a lockfile whose recorded version (`0.0.0`) no longer matched the linked package (`0.1.0-dev.7`) and left it unchanged, but the next `npm install` rewrote the line, so a real version would change the author's lockfile after every Spar update. The version of a private, linked package is used by nothing else. The package is never published, since it holds one app's contracts, and nothing of Spar goes to npm (decision "User project layout"). The SDK's version stays an input of the client step, so an update or a downgrade still regenerates the package.
- **The package-only consumer has a page and an end-to-end project:** a vanilla TypeScript page with Vite 8.3.8 and TypeScript 7.0.2 in `ui/` (`SparFrontendPath=ui`, not the default folder), with a committed lockfile kept in `tests/Spar.PackageTests/consumer-ui/`, and `Notes.App.EndToEndTests` with `SparBrowserTests=true` and Playwright at the repository's version. Package tests, 6 of 6: the frontend install, type-check, Vite build and packing run from the SDK's `tools/`; the build leaves the lockfile byte for byte as committed; the NativeAOT executable carries the packed page (`SPARFE`); the page counts words in Chromium through the backend. The run's npm cache is shared across runs like the NuGet HTTP cache, apart from the user's. 48 s with the caches warm; 2 min 41 s for the first run with an empty npm cache.
- **Left:** the custom app id waits for the app identity (plan M1b, section 3c); Linux, CI's own run and a central-package-management consumer remain.

## 2026-11-16, SparTestedApplication: the test project names its app (user decisions)

The research review's test-SDK shape (section 8, adopted for M1), tried on `Spar.Sdk` as the plan asks. The user chose the recommended option each time:
- **A test project names the app it tests with `<SparTestedApplication Include="..\App\App.csproj" />`,** over adding the item while still forcing every `ProjectReference` into the Test variant. `Spar.Sdk` turns the item into a `ProjectReference` in a static item group, so restore sees it; the app builds with `SparVariant=Test` (its libraries inherit it) and never gets the test lane. Other project references, such as a helper library, now build as they would anywhere; until now every reference of a test project was built as Test.
- **Three build errors:** SPAR120, the item in a project that is not a test project (it would be ignored); SPAR121, a Spar app reached through a plain `ProjectReference`, which would be tested in its Development build without the test host or its typed test clients (observed: the Notebook's tests then failed with CS0246 on `PagesClient`); SPAR122, an item that names something other than a Spar app. The message of SPAR121 gives the item to write.
- **No extra evaluation:** whether a reference is a Spar app comes back with the answer MSBuild already asks each reference for, `GetTargetFrameworks`, through `AdditionalTargetFrameworkInfoProperty` `SparIsApp` (returned as `<SparIsApp>true</SparIsApp>` inside `AdditionalPropertiesFromProject`). The check runs after `_GetProjectReferenceTargetFrameworkProperties`, before any reference builds, as the .NET SDK's own `ValidateExecutableReferences` does. A first version that called each reference for a target of its own was replaced before it ran, since that call can evaluate the app a second time.
- **The generated `Projects.X` marker waits** for the black-box fixture (level 7, plan M1b), its first user, so its members come from a real use. "The artifacts to build inferred from the testing packages" has nothing to infer yet: the Test variant packs the page the Browser surface serves.
- **Applied:** `Notebook.Tests`, `Notebook.EndToEndTests`, `WireShapes.Tests`, `WireShapes.EndToEndTests` and the package consumer's two test projects; `docs/app-model.md` section 1 shows the test project. The package tests gained one that checks the three errors against the packed SDK.

## 2026-11-16, Console harness built; its budget raised to 3.5 MB; lone surrogates on read (user decision on the budget; implementation record)

The M1a items "the production boundary test of section 5 on a published console harness" and the exit "a console harness around the core publishes with NativeAOT with no warnings, and gives the same results as on CoreCLR". Evidence: `docs/evidence/m1a-console-harness.md`.
- **Built:** `tests/apps/Spar.Harness`, a Spar app whose console host builds the app as a desktop host will and runs the conformance vectors through the codec and a fixed script through a bridge session. `Spar.BuildTests` builds it in Release and publishes it with NativeAOT in the clean copy: the two outputs are byte for byte identical (89 lines), and the Production executable has none of the boundary markers while its control finds `BridgeSession`, `SparDispatcher` and `BridgeCodec`, so the bridge is in it. The Notebook's executable could not show that: it has no host yet, so its bridge is trimmed away (decision "Bridge limits built", "Not run").
- **The budget (user, 2026-11-16):** the NativeAOT executable is 3,104,768 bytes, 3,043,840 without its own vector reading, against the provisional 3 MB (in decimal MB, as the spike's 2,243,072-byte "2.24 MB"). The cause: the spike's app had dispatch and serializers only, and the core now also has the bridge session with its limits and queue, DI with scope validation, logging and the `Activity` and meter instrumentation. Asked, the user chose "Raise to 3.5 MB" (the recommended option) over finding the bytes first and over leaving the size unasserted until M4. The build test asserts 3,500,000 bytes.
- **Found and fixed:** a lone-surrogate escape in a string the app reads failed the call as a handler failure, `failed` / `unknown`, against the wire rule that it becomes U+FFFD and is never an error. `SparJson.ReadString` now handles it, and invalid UTF-8 in a read string is `rejected` / `invalid-request` rather than a handler failure; a date holding a lone surrogate is malformed.
- **Choices made while building, for review:**
  - **The harness reaches Spar's internals,** through `InternalsVisibleTo("Spar.Harness")`, as the desktop hosts will, rather than through a public host API before M1b designs one. `SparRunningApp` moved to `src/Spar/Hosting/` for the same reason.
  - **The comparison is the output itself, not a committed transcript:** a golden file would repeat what `Spar.Tests` and `WireShapes.Tests` already assert. The build test checks that the output holds each kind of outcome, so a run that does nothing cannot pass.
  - **The build tests no longer run in parallel** (`[assembly: Parallelization(Mode = ParallelMode.None)]`): both classes build in the one copy.
- **Also checked:** a `SparTestedApplication` item's own metadata reaches the compiler's reference (`Aliases="notes"` on the item is on the app's `ReferencePath`), in the package tests.
- **Not run:** Linux, and a breakdown of the core's size.

## 2026-11-16, The package tests share one package folder across runs (user decision)

A package-test run took 2 min 33 s where the one before took 49.5 s. The cause, reproduced: NuGet's HTTP cache treats every entry as stale after 30 minutes, package files included, and each run had an empty package folder of its own, so a run more than 30 minutes after the last downloaded about 370 MB again (Playwright alone 214 MB); the first build and the browser test took about 55 s each instead of 10 s and 8 s. Asked, the user chose "Yes, share it" over keeping a folder per run: every run now uses one package folder under the tests' base folder, apart from the user's. Spar's packages cannot go stale there, because each run packs new versions, and each run deletes the earlier runs' Spar packages first. With the HTTP cache aged by two hours, a run took 46.8 s and downloaded nothing. This replaces "its own package folder" per run in "Packages and the package-only consumer test built". The tests also record each tool run's time in `artifacts/test-results/Spar.PackageTests/timings.txt`. Evidence: `docs/evidence/m1a-package-consumer.md`, follow-up of 2026-11-16.

## 2026-11-16, No contract manifest for now; the differential tsc test built (user decision; implementation record)

- **No separate manifest (user, 2026-11-16):** asked what to do with the planned contract manifest, which no longer had a reader, the user chose "Drop it for now" (the recommended option) over writing a JSON manifest and embedding it before any tool reads it. Its old roles are covered: the fingerprint hashes the full wire shapes (decision "Serializer differential fuzzing", "Defect for M1"), the committed contract snapshot describes the contracts, and the required check "the `.js` matches the `.d.ts` and the manifest" is now the differential test below. The devtools `contracts` tool (M1d) reads the generated catalog, which gains what it needs then. `docs/design/contracts.md`, `docs/design/build.md`, the plan and the contract-generation skill no longer list a manifest.
- **Built:** the differential test of decision "Generated JavaScript is validated against tsc in Spar's own tests". A typed twin of every module, written from the same model by a test-only emitter, compiles with `tsc --declaration` under TypeScript 6 and 7, and what `tsc` writes is compared with the client step's `.js` and `.d.ts`. Two fixtures, four cases, 4 of 4; temporary faults in either emitter fail all four. Evidence: `docs/evidence/m1a-differential-tsc.md`.
- **Choices made while building, for review:**
  - **The twin lives in the test project, not in the task assembly:** it never ships, and it is written apart from the emitters (sharing only the naming rules), so a shared mistake in an emitter's helper is not repeated in it.
  - **Tokens are compared, not text:** TypeScript's printer keeps some layout from the source, so both outputs are printed without comments and compared token by token, ignoring parentheses around a lone arrow parameter and trailing commas.
  - **Two differences are expected and filtered:** `tsc` would also declare the readers and writers `shared.js` exports to the contract modules, and import the SDK modules only they need; Spar's declarations leave them out on purpose.

## 2026-11-16, A library's contract at run time; Spar's properties stay off plain references (implementation record; one correction)

The open item of decision "Contracts from referenced libraries: the app opts in": a library's command called at run time. Evidence: `docs/evidence/m1a-referenced-contracts.md`, "Follow-up 2026-11-16".
- **Built:** `tests/apps/WireShapes.Library`, a plain class library with a contract, served by the WireShapes app through `SparContracts`. Its command runs through the generated dispatch from the C# test client (three tests, also in the NativeAOT lane) and from the page in Chromium over loopback (one test): values and a declared error carrying `long` values beyond 2^53, the app's singleton and the call's `ISparCall` injected into the library's contract.
- **Correction, found by the first build:** decision "SparTestedApplication: the test project names its app" said the tested app's libraries inherit `SparVariant=Test`. They did, and passed it on: the library and `Spar.csproj` were each built twice, with and without the variant, into one `obj/`, and the build failed with CS2012. A plain library keeps no folder per variant, and the variant means nothing to it. `Spar.Sdk` now removes `SparVariant` and `SparTestLane` from every project reference other than the tested app, transitive references included (target `SparPlainReferences`, after `_SplitProjectReferencesByFileExistence`, so no reference is even evaluated with them). Spar's own projects already removed both.
- **Guarded by a build test:** `ReferenceBuildTests` builds `WireShapes.Tests` in the clean copy and requires each project's `CoreCompile` to be entered by one instance only. With the target disabled it fails, and the clean build hits CS2012 again.
- **Found, not fixed:** a library's documentation comments do not reach its TypeScript module, because the client step loads referenced assemblies without their XML documentation files.

## 2026-11-16, A listed library's comments reach its JSDoc (implementation record; at the user's request)

The gap recorded in the previous entry, fixed at the user's request. Evidence: `docs/evidence/m1a-referenced-contracts.md`, "Follow-up 2026-11-16: a library's comments in its module".
- **The rule:** a listed library's documentation comments come from the XML documentation file beside its assembly, as the compiler finds them; a library that writes none (`GenerateDocumentationFile`) gets modules without JSDoc. `docs/app-model.md` section 3.4 says so.
- **Choices made while building, for review:**
  - **Spar reads the file itself** (`XmlDocumentationFile`, a Roslyn `DocumentationProvider`), instead of referencing Roslyn's Workspaces assembly for its provider: the task would carry a large assembly for one small reader. It refuses DTDs, and an unreadable file documents nothing.
  - **No new inputs for the client step:** turning a library's documentation file on or off rebuilds its DLL, which is already an input; observed: the step reran each time and was skipped on an unchanged build. The reference cache's key includes the documentation file, for a long-lived node.
  - **Only the listed libraries' types are indexed for comments,** after the project's own, which keep a name both declare.

## 2026-11-16, M1a budgets measured; the NativeAOT lane builds into bin/aot (implementation record; choices for review)

The three M1a budgets still open in the plan's "Provisional budgets" table, measured on Windows in Release, two runs each. Evidence: `docs/evidence/m1a-budgets.md`.
- **All met:**
  - command test, app start: median 0.20–0.24 ms on CoreCLR and 0.14–0.20 ms on NativeAOT (budget 2 ms);
  - browser test, app + context + journey: median 122.5–136.5 ms (budget 250 ms);
  - small unary round trip over loopback, timed by the page: median 0.70 ms (budget 2 ms).
- **Choices made while building, for review:**
  - **The measurements are tests on the WireShapes app** (`BudgetTests` in `tests/WireShapes.Tests` and `tests/WireShapes.EndToEndTests`), the same small workload as the spike that set the budgets, so each slice exit reruns them unchanged. The samples carry no measurement code.
  - **They record and do not assert:** median, range and count go to `artifacts/test-results/<project>/budgets.txt` with the configuration and runtime, like the no-op build time in the build tests. A slow machine never fails the suite; a missed budget is found when the slice exit reads the file.
  - **The Notebook's own journeys are recorded for reference, not compared:** they do much more than the budget's journey (dialogs, autosave, a second window, screenshots) and took 191 ms to 1.74 s in Release. Whether the browser budget should also be set on a Notebook journey is left to the M1b budget review.
- **Found and fixed:** the NativeAOT test lane built self-contained into the same `bin/Production/Release/` as the CoreCLR Release build, and its `hostfxr.dll` stopped that build from starting. The lane now builds into `bin/aot/`, beside its `obj/aot/` (`Sdk.props`).

## 2026-11-16, Spar.Sdk's own package references are implicit, the test framework included (implementation record; one fix)

The open item from the package layout (`spar-msbuild` skill: "untested with a central-package-management consumer"). Evidence: `docs/evidence/m1a-package-consumer.md`, "Follow-up, 2026-11-16: a consumer with central package management".
- **Built:** a second package-only consumer whose solution manages package versions centrally, with transitive pinning and a pin of a package Spar uses. Its tests build and pass, the pin holds, and the NativeAOT lane restores.
- **Fixed:** Spar.Sdk set the version of `xunit.v3` and `xunit.v3.aot` in package mode without marking the references implicit, so such a consumer failed restore with NU1008. They are now `IsImplicitlyDefined="true"`, like `Spar`, `Spar.Testing` and `Spar.Testing.Browser`.
- **What this keeps:** the SDK owns the test framework's version, as before; the app's `Directory.Packages.props` needs no entry for it and cannot move it. The teardown hook is compiled against that version, so letting an app choose it would need a compatibility range of its own; not proposed.

## 2026-11-16, Test projects stay on xUnit; no opt-out for now (user decision)

Asked what an app author does without xUnit: Spar.Sdk adds `xunit.v3` (or `xunit.v3.aot` in the NativeAOT lane), a global `using Xunit` and the teardown hook to every test project, with no switch to leave out the framework; `Spar.Testing` itself references no test framework. Offered a property that would leave the xUnit parts out, so a test project could bring MSTest, NUnit or TUnit and keep the rest of what Spar.Sdk does, the user chose to keep xUnit only for now, as decided on 2026-11-13 ("Test framework: plain xunit.v3 confirmed"), and may revisit it later. Using another framework was not tried.

## 2026-11-16, Linux runs in WSL for M1a; what Linux found (user decisions; implementation record)

The user chose to run M1a's Linux checks in WSL. Everything M1a builds and tests passed on Linux after four fixes. Evidence: `docs/evidence/m1a-linux.md`.
- **How:** `eng/wsl/setup.sh` and `eng/wsl/step.sh` run CI's Linux steps in the WSL distribution, on a fresh clone of HEAD in `/var/tmp/spar-linux/` with its own SDK and caches, HOME and every XDG folder under the clone's `artifacts/linux-homes/<run>/`, Chromium's missing NSS and NSPR unpacked without root, and `ContinuousIntegrationBuild=true`. Nothing goes to the user's WSL home. Choices for review: the clone and the homes live in the distribution, not on the Windows drive, because WSL's file system there cannot give `XDG_RUNTIME_DIR` 0700 and is slow; `/var/tmp`, because WSL empties `/tmp` on restart.
- **Results:** the solution 363 of 363 with the browser tests in Chromium, the NativeAOT lane, the `-mt` build, the build tests (the console harness's NativeAOT output equal to its CoreCLR output), the package tests 9 of 9, and the M1a budgets: command start 0.28–0.29 ms CoreCLR and 0.11–0.12 ms NativeAOT, browser journey 60–63 ms, loopback round trip 0.70–0.90 ms.
- **Bluetooth in the Permissions-Policy header (user, 2026-11-16):** Linux's headless Chromium has no Web Bluetooth and warns about `bluetooth=()` in Spar's deny list, which failed every browser test's clean-console check. Asked, the user chose "Keep it; tests ignore it" over dropping the feature from the list (a weaker default where the feature exists) and over a header that depends on the platform. The two browser-test helpers accept exactly that message.
- **Fixed:**
  - the static-state test writes the path hash in file-local type names as `{file}`, since it differs between builds with and without `ContinuousIntegrationBuild` (it would have failed in CI on Windows too);
  - the package tests' base folder has no space, `spar-package-tests`: the NativeAOT link step on Linux splits paths into the NuGet package folder at spaces; the consumer's own folder keeps its space and non-ASCII letters, and links;
  - the SPAR121 check builds the expected item path with the platform's separator.
- **Still open for the M1a exit:** CI's own run (nothing is pushed) and the editor squiggles (a check that needs the user).

## 2026-11-16, Interface contracts run; a missing implementation stops the start; a contract library from a package (implementation record; two fixes)

The open items "a library from a package" and "a library with an interface contract implemented in the app" (`docs/evidence/m1a-referenced-contracts.md`). No interface contract had run in a real app before: the generator's tests covered them, and `tests/AppModel` only compiled one. Evidence: `docs/evidence/m1a-referenced-contracts.md`, "Follow-up 2026-11-16: an interface contract, and a library from a package".
- **Built:**
  - `WireShapes.Library` declares `IRatesService`, an interface contract with a declared error; the WireShapes app implements it (`FixedRates`) and registers it. `tests/WireShapes.Tests/RatesContractTests.cs` calls it through the generated test client, replaces the implementation from a test, and checks that an app without one does not start.
  - A third package-only consumer: a class library `Notes.Tags`, in a folder of its own, references the `Spar` package, declares the interface contract `ITagsService`, writes its documentation file, and is packed into a feed of its own; the consumer's app references the package, lists it in `SparContracts`, implements the interface and tests it (`tests/Spar.PackageTests/LibraryPackageTests.cs`). Nothing in Spar had to change for a package: listing is by assembly name, and the documentation file sits beside the DLL in the package as it does in a build.
- **Fixed: a missing implementation now stops the start.** An app that registered no implementation of an interface contract started, and every call to the contract then failed as `failed` / `unknown`. The provider's `ValidateOnBuild` checks only what is registered, so it could not see this. The generated catalog now lists the interface contracts (`SparCatalog.Interfaces`: wire name, C# name, `typeof`), and building the app checks each has a registration before the provider is built, failing with "The contract 'rates' is the interface WireShapes.Library.IRatesService, and the app registers no implementation of it. Register one in SparApplication.Create, for example spar.Services.AddScoped<IRatesService, MyImplementation>()." This is the start-up check recommended on 2026-11-13 ("Contract syntax in the generator") and marked Leaning in `docs/app-model.md`; it is now listed there as built. Choice for review: the check looks for any registration of the interface, whatever its lifetime, and runs after the test's service changes, so a test can replace or remove the implementation.
- **Fixed: empty documentation elements left holes in the JSDoc.** `<paramref name="currency"/>` came out as nothing ("How many cents one unit of is worth"), for the app's own contracts as well as a library's; `<typeparamref>` and an empty `<see href>` were dropped the same way. They are now the name in backticks and the address.

## 2026-11-16, The rest of the wire-rules table (implementation record; choices for review)

The generator supported ten wire kinds; the wire-rules table decided on 2026-11-14 (plan section 2) has more, and nothing recorded them as deferred. Built now, except enums: `byte`, `sbyte`, `short`, `ushort`, `uint`, `ulong`, `float`, `decimal`, `char`, `DateTime`, `DateOnly`, `TimeOnly`, `TimeSpan` and `byte[]`, in the generated C# serializers (`SparJson`), the declarations and modules of the client step, and the SDK's `wire` functions. Evidence: `docs/evidence/m1a-wire-kinds.md`.
- **SPAR024, the `DateTime` warning** (decided as a warning without an ID): reported once at each property, parameter or command that carries a `DateTime`, in a list or nullable too, and the command stays valid. The client step now stops only on errors; the compiler's analyzer reports the warning. Restated diagnostics about a referenced assembly keep their descriptor's severity (they were always errors). An app that builds with warnings as errors, as Spar's own tests and the app template do, must use `DateTimeOffset` or lower SPAR024 in its `.editorconfig`.
- **Each value has one spelling, and the reader requires it,** because .NET's parsers are looser than the table:
  - `decimal`: the text must be what the value writes back. `decimal.TryParse` rounds past 28 or 29 significant digits instead of failing, and accepts `+1` and `01`; negative zero writes as `0`, so `"-0"` is refused;
  - `ulong`: a canonical decimal string, like `long`, up to 20 digits;
  - `float`: a number beyond its range parses as infinity, so it is refused as not finite; NaN or an infinity in a result is a writer failure, as for `double`;
  - `TimeSpan`: the constant format, written back equal (`1:2:3` parses, and is refused);
  - `byte[]`: padded base64, with any byte outside the alphabet refused before decoding (the reader's decoder skips whitespace, and an escape never belongs in base64);
  - `char`: a string of one UTF-16 code unit; a lone surrogate is U+FFFD, as in any string.
- **Choices made while building, for review:**
  - **`TimeOnly`** reads `HH:mm:ss` with up to seven fraction digits and writes the fraction only as long as needed (`08:30:00.25`, `08:30:00`). `DateOnly` is exactly `yyyy-MM-dd`.
  - **`byte[]` is a scalar request** (one base64 string), so SPAR015 no longer refuses it; `IReadOnlyList<byte>` stays a list of numbers.
  - **The page checks numbers, `ulong`, `decimal`, `char` and base64,** each against the same rules, and refuses a bad value before sending (`rejected` / `invalid-request`). Dates, times and GUIDs stay plain strings on the page, as `Guid` and `DateTimeOffset` already were; the backend checks them. A `float` from the page must still be finite once rounded to single precision.
  - **Base64 on the page** uses `atob` and `btoa` with a pattern check, in 32 KiB slices, so it needs no newer API.
- **Not built:** enums and the `[Flags]` diagnostic (next), `Int128`, `UInt128` and `BigInteger` (the table adds them when an app needs them).

## 2026-11-16, Enums on the wire (implementation record; choices for review)

The last kind of the wire-rules table. An enum travels as its member name, exactly, and is a union of string literals in TypeScript (decided 2026-11-14); `[Flags]` is a contract diagnostic, now SPAR025. Evidence: `docs/evidence/m1a-wire-kinds.md`, "Follow-up: enums".
- **Built:** the generator reads each enum into its member names in declaration order; the C# serializer reads with a `switch` over the names and writes with one over the values; the client step declares `export type Priority = "Low" | "Normal" | "High";`, with a reader and a writer per enum placed like a record's (in the contract's module, or once in `shared` when two contracts use it); the snapshot lists each enum's members; the fingerprint includes them. The SDK gains `unknownMember` and `refusedMember`, which the generated functions call for anything that is not a member.
- **SPAR025** reports an enum that cannot travel as one member name, at the enum, saying why: `[Flags]`, two members with one value, or no members. The commands that use it are left out, as for any unsupported type.
- **Choices made while building, for review:**
  - **Two members with one value are an error,** not "the first name wins": a value written back would change its name, so a round trip would not be stable.
  - **An enum with no members is an error;** its TypeScript type would be `never`, and no call could carry it.
  - **A value that is no member** (a cast integer) is a writer failure, `response` / `serialization`, like a NaN `double`. On read, a name that is not a member, a member in another letter case, or a number is `rejected` / `invalid-request`.
  - **Member names are exactly as C# spells them** (`High`, not `high`), and `[SparName]` does not apply to members. Renaming members on the wire would need a decision of its own.
  - **No per-member JSDoc:** a union of string literals has no place for it; the enum's own summary becomes the type's JSDoc.
  - Enum names follow the type-name rules of records and errors (SPAR003, SPAR004, SPAR040), and an enum's reader and writer names are checked against the contract's name like a record's.
