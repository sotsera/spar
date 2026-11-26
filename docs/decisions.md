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

## 2026-11-16, Editor diagnostics through VS Code's language server; the windows still to check (evidence; one fix)

The M1a exit asks for contract diagnostics as you type, with squiggles, in Visual Studio and VS Code. The user allowed the PC to be used while away, but computer use's approval dialog needs someone present, and the request was denied. Checked instead, without the windows: VS Code's C# language server (extension 2.160.4) reports SPAR015 and SPAR024 0.21–0.23 s after an unsaved edit of the Notebook's contract, at the parameter and the method, and clears them 0.17–0.18 s after the edit is undone. Evidence: `docs/evidence/m1a-editor-diagnostics.md`; the check is `eng/editor-diagnostics.cs`.
- **Fixed:** SPAR024's message named the type with `global::`; it now uses the C# display name.
- **Still open for the M1a exit:** the squiggles drawn in the VS Code window and in Visual Studio, which needs the user at the PC.

## 2026-11-16, Squiggles in both windows; ReSharper's code analysis hides them in Visual Studio (evidence)

The rest of the M1a exit "contract diagnostics as you type, with squiggles in both". Checked with the user at the PC, on an app that uses Spar from packages (`<Project Sdk="Spar.Sdk">`, Spar packed from the working tree), with the two commands of the earlier check written to disk and then removed. Evidence: `docs/evidence/m1a-editor-diagnostics.md`, "Follow-up: the windows".
- **VS Code** (C# Dev Kit 3.40.215): a red squiggle under the parameter for SPAR015 and a green one under the method for SPAR024, in Problems within 5 s; gone within 4 s of the restore.
- **Visual Studio 2026** (18.12.12405.243): with ReSharper's code analysis on, as the user has it, SPAR015 and SPAR024 reach the Error List as IntelliSense entries within 6 s and no squiggle is drawn; the .NET SDK's own CA1822, raised to a warning, behaves the same, while compiler errors and the built-in IDE analyzers are drawn. With "Enable code analysis" unticked, both Spar squiggles are drawn, the hover shows the SPAR015 message, and both clear on the restore. The spike's "no squiggle in Visual Studio" is this ReSharper behaviour, not Spar's.
- **Kept as it is:** `ContractAnalyzer` keeps its compilation-level actions. Splitting them into a second analyzer did not change Visual Studio, and would run the contract analysis twice per build.
- **Open:**
  - **C# Dev Kit does not load the samples:** a project that imports Spar.Sdk's props and targets itself (source mode) is "unsupported format" to Dev Kit, so the Notebook opens as loose files, with no diagnostics. Apps from packages are not affected. How the samples name their SDK in this repository is a decision of its own.
  - Which ReSharper setting, if any, shows analyzer squiggles while its code analysis is on; go-to-definition in the windows; the Notebook in Visual Studio.

## 2026-11-16, The repository's projects name Spar.Sdk (implementation record; user's choice of approach)

C# Dev Kit loads only projects that name their SDK in `<Project Sdk="…">`: the samples and Spar's test projects, which imported `src/Spar.Sdk/Sdk/Sdk.props` and `Sdk.targets` themselves, were "unsupported format" to it, so in VS Code they opened as loose files with no Spar diagnostics (decision "Squiggles in both windows; ReSharper's code analysis hides them in Visual Studio"). The user chose, of three options, a source package over `Microsoft.NET.Sdk` with `Directory.Build` imports (which would split Spar.Sdk's props around `Microsoft.NET.Sdk` and make the samples unlike an app) and over changing the samples only.
- **Built:**
  - **`eng/source-sdk/`:** a `Spar.Sdk` package at version `0.0.0-source` (about 1 KB, committed as `feed/spar.sdk.0.0.0-source.nupkg`) whose `Sdk.props` finds `src/Spar.Sdk/Sdk/Sdk.props` by looking upward from the project (`GetDirectoryNameOfFileAbove`, forward slashes) and imports it, and whose `Sdk.targets` imports the matching targets. Spar.Sdk's own detection of source mode is unchanged: the imported file is still the one in `src/`. `pack.cs` rebuilds the package with fixed timestamps and LF endings, and writes it only when it changes.
  - **`global.json`** pins `"msbuild-sdks": { "Spar.Sdk": "0.0.0-source" }`; a new root **`nuget.config`** clears the sources, keeps nuget.org, adds `eng/source-sdk/feed`, and maps `Spar.Sdk` to it alone. This machine had nuget.org only.
  - **The nine projects** (`samples/Notebook`, `Notebook.Tests`, `Notebook.EndToEndTests`, `tests/Spar.Tests`, `WireShapes.Tests`, `WireShapes.EndToEndTests`, `tests/apps/WireShapes`, `tests/apps/Spar.Harness`, `tests/AppModel/Real`) write `<Project Sdk="Spar.Sdk">` and import nothing. None set anything before the import, so nothing moved after Spar's props.
  - **`SourceSdkTests`** (Spar.Sdk.Tasks.Tests): the committed package holds the current nuspec, props and targets, and the nuspec, the package's file name and `global.json` name one version. NuGet keeps one extracted copy per version, so a change to the package needs a new version; the test says so. Checked by changing the targets without repacking: it failed with that message.
- **Checked:** VS Code loads the Notebook ("1 project(s) are loaded, and 0 failed to load") and shows SPAR015 and SPAR024 on an edit of its contract, cleared on the restore (evidence: `docs/evidence/m1a-editor-diagnostics.md`, "Follow-up: the samples name Spar.Sdk"). On Windows: `dotnet test` 429 of 429; the NativeAOT lane 158, 79 and 12 with no warnings; `-mt` clean; build tests 6 of 6 (the clean copy takes the files git lists, untracked ones included); package tests 10 of 10 (their consumers have their own `nuget.config` and clear the sources).
- **Not yet run:** the Linux lane in WSL with this change.

## 2026-11-16, The source package on Linux (evidence)

Follow-up to "The repository's projects name Spar.Sdk": the CI lane in WSL (Ubuntu 24.04), on a fresh clone of 7b8068a, in CI's order: the TypeScript SDK steps, `dotnet build Spar.slnx` with no warnings, `dotnet test` 429 of 429, the NativeAOT lane 158, 79 and 12, and `-mt` clean. The package's upward search with forward slashes finds `src/Spar.Sdk/Sdk/` there too. Not run on Linux: the build tests and the package tests.

## 2026-11-16, Page tests: @spar/app/testing (user decisions; implementation record; choices for review)

The last open M1a exit item: the Notebook's contracts through the TypeScript SDK against a fake channel. It needed plan section 5's issue B, where page tests run, and the shape of `@spar/app/testing`.
- **Decided (user, 2026-11-16):**
  - **Where:** in Node with Vitest, with happy-dom for hooks and components. The real engine stays covered by the browser tests (level 4). Chosen over Vitest's browser mode and over both.
  - **The layer:** a fake backend on the wire, typed. It speaks the real bridge protocol to the page's own connection, and scripted values and the requests it reports cross the generated encoders and decoders. Chosen over a fake at the call level, which would have skipped serialization.
  - **The shape:** `fakeSpar()`, then `spar.on(<command>)` with `succeed`, `fail(case, payload)`, `failWith(CallError fields)`, `hang()` and `answer((request, reply) => …)`, and `spar.calls(<command>)` for the typed requests. Keyed by the generated command, so a wrong command, value or case fails `tsc`. `fakeSpar()` resets `configureSpar`'s hooks.
- **Built:**
  - **SDK:** `src/Spar.Client/src/testing.ts`, compiled into the package as `sdk/testing.js`. The fake host answers `hello` with `open` carrying the page's fingerprint, runs each command's script, records the requests, and delivers answers in a microtask. A cancel of a hanging call is confirmed `cancelled` / `stopped`. `results.ts` gains an internal `resetConfiguration`. Tests: `test/testing.test.ts`, 12 tests over a hand-written contract module, plus ten type errors that must not compile on TypeScript 6 and 7.
  - **Client step:** each contract module gains a writer per error type (the case's fields after `code`, as the host writes them) and `__sparTesting`, a table naming each command's contract, wire name, request reader and result writers. The package gains `testing.js` and `testing.d.ts` (export `./testing`), whose `fakeSpar` builds the map from each command function to its entry. The declarations do not mention the table. The typed twin covers the writers and the table, so `tsc` checks them against the declared types; the comparison drops the table from `tsc`'s declarations, as it drops the shared helpers.
  - **The Notebook:** `frontend/src/notebook.test.tsx`, 7 tests of `useNotebook` through a probe component, with React's `act` and no testing library. They cover the list order, a missing page, a save over the page's revision (a `bigint` round trip), a conflict and "keep mine" over the newer revision, a lost connection, typing during a save, and a taken title. `vitest` 4.1.12 and `happy-dom` 21.0.4, the SDK's versions, as dev dependencies; `npm test` runs them, as a CI step after the build and as the WSL step `pagetests`.
  - **`docs/app-model.md` section 6.10**, compiled by the documentation check against stand-ins of `@spar/app/testing` and `vitest`.
- **Production bundles carry none of it:** the Notebook's Vite build contains neither `fakeSpar`, `__sparTesting`, `createFakeSpar` nor its error writers. `Spar.BuildTests` now checks this on the Production page before packing, with the generated package as the control.
- **Choices made while building, for review:**
  - **A command no test scripted is refused** with `rejected` / `unknown-command` / `notStarted` and a console error naming it, rather than throwing into the page.
  - **A scripted value the encoder refuses** (NaN, for example) ends the call as a host's writer failure does, `response` / `serialization` / `completed`, with a console error. A script that throws ends its call `failed` / `unknown`.
  - **Each script replaces the last** for its command and holds for every later call; there is no queue of answers. `answer` covers per-call behaviour and delays.
  - **Not built:** stream and subscription scripts (with streams, M3), host events, a scripted closed channel, and the framework notes of plan section 5.
- **Checked on Windows:** the SDK 160 of 160 with its checks on TypeScript 6 and 7; `dotnet test` 430 of 430 (the client step 62, the documentation check with 27 blocks); the Notebook's page tests 7 of 7, about 25 ms of test time; the NativeAOT lane 158, 79 and 12 with no warnings; `-mt` clean; build tests 6 of 6, the Production page check included; package tests 10 of 10; the size check clean (226 files).

## 2026-11-16, Page tests on Linux (evidence)

Follow-up to "Page tests: @spar/app/testing": the CI lane in WSL (Ubuntu 24.04), on a fresh clone of 7eb0449, in CI's order: the size check, the TypeScript SDK's checks and tests, the build with no warnings, `dotnet test` 430 of 430, the new `pagetests` step (the Notebook's 7 page tests), the NativeAOT lane (Spar.Tests, WireShapes.Tests 79, Notebook.Tests 12) and `-mt`, every step exiting 0. Not run on Linux: the build tests and the package tests.

## 2026-11-16, CI's first runs on GitHub Actions (evidence; user decisions on the workflow)

The repository went to `sotsera/spar` (public; user, 2026-11-16) after a search of every commit for the one framework the repository must never name (none found), for secrets (only fakes and run-time passwords) and for files over 5 MB (none). All commits carry the user's GitHub noreply address. `m1a` is pull request sotsera/spar#1 into `main`.
- **First run (bc589b7):** both jobs passed: Windows 14 min 28 s, Linux 7 min 20 s. The test suite was not the difference (Test (CoreCLR) 55 s against 49 s). Windows spent 203 s installing Chromium against 16 s on Linux, and its NativeAOT lane (159 s against 105 s), build tests (147 s against 109 s) and package tests (139 s against 74 s) were slower.
- **Second run (82c68b9), both passed:** with `--with-deps` on Linux only (on Windows it installed the Media Foundation feature, 3 min 16 s) and the MSBuild server off, Windows took 9 min 44 s (Chromium 9 s) and Linux 6 min 52 s.
- **Decided (user, 2026-11-16), in 4abe871:**
  - **Windows caches and `TEMP` on D:** under `RUNNER_TEMP` (NuGet's packages and HTTP cache, npm's cache, `TEMP` and `TMP`), as two published measurements suggested; its effect is measured on the run of 4abe871.
  - **A job timeout of 45 minutes,** instead of GitHub's six hours.
  - **Concurrency per pull request,** grouped by workflow and pull request number; a newer push cancels only pull request runs, never a run on `main`.
- **Considered and left:** caches between runs (a cache can make a job slower overall; none without a measured run each way); path filters for documentation-only changes (a skipped workflow leaves a required check waiting; revisit when `main` gets required checks). The actions are pinned by commit SHA (checkout v7.0.1, setup-dotnet v6.0.0, setup-node v7.1.0, upload-artifact v7.0.2), each the newest release at least two weeks old.

## 2026-11-16, Windows caches on D: measured (evidence)

Follow-up to "CI's first runs on GitHub Actions": the run of 4abe871 against the run of 82c68b9, both passed, one run each. Windows went from 9 min 44 s to 7 min 43 s. The package tests gained most (148 s to 84 s, now beside Linux's 72 s), then the build (55 s to 38 s). The NativeAOT lane (114 s to 110 s) and the build tests (104 s both times) did not change, so their Windows time is not the cache location. Linux, which the change does not touch, went from 6 min 52 s to 7 min 24 s, which shows how much one run varies. The two jobs now take about the same time.

## 2026-11-16, CI in parallel lanes; the tested app keeps the NativeAOT lane (user decision; one fix)

- **Parallel lanes (user, 2026-11-16):** each OS runs four jobs at once, `tests` (size, the TypeScript SDK, the build, the CoreCLR and page tests, `-mt`), `aot`, `build-tests` and `package-tests`, each with the setup and build it needs. Run 43970040890: the lanes took 67–207 s, against about 7.5 minutes per OS in one job.
- **What the split found:** the `aot` lane failed on both OSes. Publishing `WireShapes.Tests` with `SparTestLane=Aot` from a clean checkout stopped with NETSDK1004: the tested app's assets were in `obj\aot\` and its build looked in `obj\`. The reference from a test project to its app removed the lane (`UndefineProperties="SparTestLane"`, decision "SparTestedApplication: the test project names its app"), and NuGet's restore ignores that metadata, so the app was restored with the lane and built without it. It passed locally and in the single CI job because an ordinary build had restored `obj\` first. Reproduced in a fresh clone.
- **The fix (corrects "never gets the test lane"):** the tested app keeps the lane. In the NativeAOT lane it is restored and built under `obj\aot\` and `bin\aot\`, apart from its CoreCLR build; the lane changes nothing else for an app, since only test projects switch xUnit packages on it. Spar's own projects and plain libraries still have it removed, which is harmless: their folders do not depend on it. A fresh clone then passed the whole lane, 158, 79 and 12 tests. CI's `aot` lane starts clean on every run, so it keeps checking this.

## 2026-11-16, Parallel lanes measured (evidence)

Follow-up to "CI in parallel lanes; the tested app keeps the NativeAOT lane": the run of 3806d7f passed all eight jobs in 3 min 52 s from start to end, against 7 min 48 s for the last run with one job per OS (4abe871). Windows: tests 228 s, build-tests 217 s, aot 213 s, package-tests 137 s. Linux: tests 127 s, aot 127 s, build-tests 116 s, package-tests 110 s. Windows' three longest lanes now set the time; one run each.

## 2026-11-16, Two-stage close (user decisions; compiled example; choices for review)

The first "decide first" item of M1b (research review: two-stage close and its automated mode, adopted 2026-11-15). Shown, compiled, in `docs/app-model.md` sections 2 and 6.11.
- **Decided (user, 2026-11-16):**
  - **Guards decide or ask.** The page's guard, then the backend's, each answer close, keep, or ask with a question. An ask shows one native confirmation owned by the window ("Close anyway" / "Keep open"), so the backend never needs UI of its own; a page may show its own dialog and answer close or keep. Chosen over guards that only answer yes or no.
  - **Session end runs the guards but never prompts:** reason `SessionEnd`, a short deadline of about 5 s, an ask counts as close, and Spar never blocks Windows from logging off. Chosen over blocking the session end with a reason Windows shows.
  - **The automated mode runs the guards without prompts:** `IsAutomated` / `automated` is true and an ask counts as close, so saving on close is tested. Chosen over skipping the guards.
  - **The automated mode outside tests only in Development and Test:** the test host and the devtools agent session turn it on, and an environment variable works only in the Development and Test variants; a Production build never honours it. Chosen over also offering a Production option.
- **The shape, for review:**
  - **C#:** `spar.OnCloseRequested(guard)` in `Program.cs`, with a guard of `(CloseRequest, IServiceProvider) => CloseDecision` or `(CloseRequest, IServiceProvider, CancellationToken) => ValueTask<CloseDecision>`. `CloseRequest(Window, Reason, IsAutomated)`, `CloseReason.User`, `App` or `SessionEnd`, and `CloseDecision.Close`, `Keep` or `Ask(question)`.
  - **Page:** `windows.onCloseRequested(guard)` in `@spar/app/windows`, returning a function that removes it. The request has `reason` (`"user"`, `"app"`, `"sessionEnd"`), `automated` and a `signal` aborted when the request ends without the guard; the answer is `"close"`, `"keep"` or `{ ask }`.
- **Choices made while writing it, for review:**
  - **What asks:** the close button, Alt+F4 and the taskbar are `User`; the app's own code and the tray's Quit are `App`. With close to tray, the close button hides the window and asks nothing; Quit asks.
  - **Secondary windows first:** closing the main window asks each open secondary window, then itself, and keeps them all open if any answers keep.
  - **A second click joins** the open request; an answer from a document that has since navigated is ignored.
  - **A page guard that does not answer** within about 5 s, with no dialog of its own open, is treated as a hung page (plan section 3, "Hung pages"; what "Wait" then does is the next M1b question).
  - **The environment variable** is `SPAR_AUTOMATED=1`.
- **The check:** `RunningCopies` and the page's `closing.ts` are new blocks; the stand-ins gain `Spar.Stubs/Closing.cs` (also linked into the real-Spar check's planned stand-ins), `OnCloseRequested` on both builders, and `onCloseRequested` in the stand-in `windows.d.ts`. The documentation check passes, 29 blocks, on TypeScript 6 and 7 and through real Spar.

## 2026-11-17, Windows single instance: a lock file plus a pipe (user decisions; spike)

The second "decide first" item of M1b. `spikes/windows-single-instance`: a NativeAOT probe (0 warnings) on the host and in Windows Sandbox, and the focus checks on the user's desktop with the user's go-ahead.
- **Decided (user, 2026-11-17):**
  - **The mechanism:** an exclusive lock file in the app's data folder (`FileShare.None`, released by the OS when the process ends) plus a named pipe for the relaunch's arguments. Chosen over a lock file plus an AF_UNIX socket, the Linux code (a 108-character path limit, and no peer pid without an extra ioctl), and over a named mutex plus a pipe (a mutex is scoped by its name, so the same user in two sessions gets two instances on one folder).
  - **Measured before deciding** (this entry).
  - **A relaunch shows and focuses the main window by default,** restoring it from the tray or from minimized, and hands its arguments to an optional C# handler, designed with a compiled example in `docs/app-model.md`. The page sees nothing unless the app forwards it.
- **What the spike found:**
  - **Handoff:** 20 of 20 delivered, 1.2 ms median (max 1.5) from the secondary's `Main` to the primary; the secondary exits 41 ms after `Process.Start` (median), mostly process start-up. Ten at once: 10 of 10 on one reused pipe instance.
  - **A hung primary** (suspended): the secondary gave up after a 2 s acknowledgement timeout (6 of 6) and started no second primary; the primary handled the queued launches when resumed. **A primary exiting** with the pipe closed and the lock held 3 s: the secondary waited and became the primary with its own arguments (3 of 3, about 3.0 s); held 7 s, it failed clearly at its 5 s lock wait (3 of 3). **A killed primary:** the next launch took the lock at once (10 of 10).
  - **.NET's `PipeOptions.CurrentUserOnly` is unsafe for an elevated user:** it follows the token's owner, which is Administrators, so the pipe was open to every administrator. A SYSTEM process connected, and a SYSTEM squatter whose pipe Administrators owned passed the elevated client's check and received the arguments.
  - **The explicit security works:** a DACL with only the token's user SID (`O:<user>D:P(A;;FA;;;<user>)`), `FILE_FLAG_FIRST_PIPE_INSTANCE` and `PIPE_REJECT_REMOTE_CLIENTS`, and a client that checks the user of the server process (`GetNamedPipeServerProcessId`, then its token). The SYSTEM probe got "Access denied", and the SYSTEM squatter was refused. While a primary runs, nobody can take the name (`ERROR_PIPE_BUSY`). It costs about 0.5 ms.
  - **The pipe name comes from the lock file's identity** (volume serial number and file index): naming it from the path failed through a junction. Short names worked either way.
  - **Focus needs `AllowSetForegroundWindow`:** on the desktop the primary came to the front in 9 of 9 (normal, minimized, hidden) with it, and in 0 of 3 without it. In Windows Sandbox every case came to the front, the controls included, so Sandbox focus results show nothing.
- **Recommended for the build (M1b start-up), from the spike:** pipe security as above, never `CurrentUserOnly`; one pipe instance reused with `DisconnectNamedPipe` after the client closes its end; a 2 s acknowledgement timeout and a lock wait of about 5 s; close the pipe before releasing the lock; a primary whose pipe name is taken keeps running and logs it. The secondary's failures (not responding, still closing, running for another user) need exit codes beside the fixed start-up codes; choosing them is part of the handler's design.
- **Not tested:** two sessions of the same user, elevated and non-elevated instances together, another real account, a launch from Explorer or a file association, network or redirected profile folders.

## 2026-11-17, The relaunch handler (user decisions; compiled example)

Follow-up to "Windows single instance: a lock file plus a pipe". Shown, compiled, in `docs/app-model.md` sections 2 and 3.14.
- **Decided (user, 2026-11-17):**
  - **`spar.OnRelaunch(handler)` in `Program.cs`,** with `(Relaunch, IServiceProvider)` and `(Relaunch, IServiceProvider, CancellationToken) => ValueTask` overloads, like the close guard. `Relaunch(Arguments, WorkingDirectory)` with `ResolvePath(path)` against the relaunch's working directory. Chosen over an injectable `IRelaunches` stream, which would have to buffer relaunches that arrive before anything watches and would drop them silently without a watcher.
  - **A relaunch that cannot reach the running instance shows a native message and exits with a fixed code,** through the start-up error path (stderr and the system log too): 8 running but not responding (no acknowledgement within 2 s, or the lock still held after about 5 s), 9 running for another user on the same data folder. Chosen over an exit code alone, since a launch from an icon has no terminal. In the automated mode the message goes only to stderr and the log.
- **Already decided and shown with it:** the main window is shown and focused before the handler runs; a relaunch is acknowledged when queued and never replayed.
- **Choices made while writing it, for review:**
  - The handler runs off the UI thread, one relaunch at a time in arrival order, with the app's root services, as the close guard does. The async overload's token is cancelled at shutdown.
  - The first launch's arguments stay in `Program.cs`'s `args`; only later launches reach the handler.
  - The page sees nothing unless the app passes it on.
  - The example is the SQL tool's `--recipe <file>`, checked by `RecipeQueue.TryAdd`, because a relaunch's arguments are untrusted input from any process of the same user.
- **Open:** a black-box test of a failed relaunch runs the Production executable, which ignores the automated mode, so its message would block the test. This is settled with the black-box data root (plan section 5, issue M). The option for several instances is still designed only when an app needs it.
- **The check:** `RecipeQueue` is a new block, and `Program.cs` gains the handler and its service. The stand-ins gain `Spar.Stubs/Relaunch.cs`, also linked into the real-Spar check's planned stand-ins, and `OnRelaunch` on both builders.

## 2026-11-17, What "Wait" does after the hung-page dialog (user decisions)

The third "decide first" item of M1b (plan section 3, "Hung pages"; `spikes/page-heartbeat`, which left it open).
- **Decided (user, 2026-11-17):**
  - **Wait asks again, with a doubling interval.** The heartbeat keeps pinging, with at most one ping outstanding. The dialog comes back after another three misses (about 20 s), then after 40 s, 80 s, and every 160 s after that. Chosen over asking every 20 s, the untested proposal, which nags a user who is waiting for long work, and over asking once per hang, which leaves only the close button for a page that never recovers.
  - **The page answering again closes the dialog:** Spar dismisses an open "Wait / Reload page" dialog itself, logs that the page recovered, and resets the count and the interval.
  - **Closing a window whose page does not answer shows "Wait / Close anyway".** This is the case where the page's close guard is silent for about 5 s, or the page is already hung. It is the same native dialog, with "Close anyway" in place of "Reload page". Close anyway skips only the page's guard: the backend's guard still runs, with its own question, then the window closes and its renderer is terminated. Wait keeps the close request open, with the same doubling interval. Chosen over the ordinary dialog, whose Reload would drop the close request.
- **Unchanged:** the library never kills anything on its own (a reload or a close anyway is the user's choice); reload terminates the renderer, then reloads once; one state and one dialog for the heartbeat alarm and `ProcessFailed(RenderProcessUnresponsive)`.
- **Not measured:** the interval sequence is a policy, checked when the host is built (M1b). The dialog on the real desktop is already a row of the plan's "Checks that need the user at the PC".

## 2026-11-17, Repository settings on GitHub (user decisions)

After `sotsera/spar#1` merged (a merge commit, `b37d728`), the user had the repository configured; all of these were applied through the GitHub API and read back.
- **A ruleset on `main` (the default branch):** changes only through a pull request, with no approval required, since the user maintains it alone; the eight CI jobs (`tests`, `aot`, `build-tests` and `package-tests`, on `windows-latest` and `ubuntu-latest`) must pass; signed commits required; no force pushes and no deletion. The user, as admin, can still edit the ruleset. An unsigned commit on a pull request branch now blocks its merge, so commits made while signing fails are re-signed before merging.
- **Merge commits only:** squash and rebase merging are off, so a milestone's commits keep their hashes and signatures on `main`. The "Update branch" button is on, and a merged pull request's branch is deleted automatically.
- **Actions:** the workflow token is read-only by default (`ci.yml` already asks for `contents: read` only), Actions can no longer create or approve pull requests, and every action must be pinned to a full commit SHA.
- **Security:** secret scanning with push protection, and private vulnerability reporting. Dependabot alerts were already on; Dependabot's security-update pull requests stay off.

## 2026-11-17, M1b's remaining questions without a spike (user decisions)

The user asked to settle all of M1b's "decide first" items. These nine needed no spike; the user chose the recommended option each time except the stall probe. The WebView2 versions come from the stable release notes (`MicrosoftDocs/edge-developer`, `microsoft-edge/webview2/release-notes/sdk/`, read 2026-11-17).
- **The UI-thread stall probe: in Development and Test, and an opt-in in Production** (chosen over Development and Test only). A timer posted through the pump or the GLib main loop logs each delay over 50 ms as a warning, with the Spar callback that was running. Window tests fail on a stall over 250 ms until M4 measures the budget. In Production the probe is absent unless a C# option keeps it, logging only, for diagnosing a user's machine; it is behind a feature switch, so a build without the option trims it away and the production boundary test still finds no probe.
- **Styles added at run time: documentation plus a nonce option.** The app-author docs recommend build-time CSS and constructed stylesheets. `spar.ContentSecurity(csp => csp.StyleNonce())` gives each document a fresh nonce, which the host writes into the served HTML as `<meta property="csp-nonce">` and into `style-src`, for CSS-in-JS libraries that need one (`spikes/csp-style-injection`).
- **The app id: reverse-DNS and required.** `<SparAppId>com.example.SqlTool</SparAppId>` in the app project. The build checks GTK's application-id rules: two or more dot-separated segments of ASCII letters, digits and `_`, no segment starting with a digit, at most 255 characters. A missing or invalid id is a build error; the template fills it in. It becomes the AppUserModelID, the Credential Manager prefix, the GTK application id and the desktop file name.
- **Folders come from the app id, per channel.** Production uses `%LOCALAPPDATA%\<app id>` on Windows, and `<app id>` under `$XDG_DATA_HOME`, `$XDG_CACHE_HOME` and `$XDG_STATE_HOME` on Linux. Development appends `.Development` to the id everywhere (folders, AppUserModelID, GTK id), so a Debug run is a separate app to the OS. The profile, logs and crash records are subfolders, and `spar.Folders(…)` still overrides each. Chosen over `%LOCALAPPDATA%\<Company>\<Product>` on Windows, a second identity to keep unique. This replaces the `<Company>\<App>` example in plan section 3c.
- **The black-box data root: the environment only** (plan section 5, issue M; the security review is below). A Production executable takes no data-root switch. Spar derives its per-user folders from the environment: `LOCALAPPDATA`, read from the variable on Windows rather than the known-folder API, and `XDG_*` on Linux. The black-box fixture launches the app with those variables pointing into its disposable root. Chosen over a `--spar-data-root` switch limited to local paths.
- **The `Projects.X` marker: a generated class per tested app.** For each `SparTestedApplication`, `Projects.SqlTool` with `ExecutablePath` (the app's Production NativeAOT executable, published incrementally into the test project's `obj/`, only in projects that use the black-box fixture), `AppId` and `ProjectDirectory`. The fixture takes it, as in `SparBlackBox.Launch(Projects.SqlTool, …)`. `SparBlackBoxPublish=false` turns the publish off. Chosen over a lookup by name.
- **A failed relaunch in a black-box test:** the fixture finds the native message by its process through UI Automation, reads it, presses OK, and the test checks the exit code (8 or 9). This replaces the open point of decision "The relaunch handler": the shipped executable gets no mode for it. Chosen over a variable that suppresses the message in every build.
- **The minimum WebView2 Runtime: 139, provisional, with no fallbacks.** Every API Spar uses is stable there. The highest floors are find in page (`ICoreWebView2_28`, SDK 1.0.3405.78, Runtime 139.0.3405.78), the background colour on the controller options (`ICoreWebView2ControllerOptions3`, Runtime 137.0.3296.44), the worker-aware request filter (`ICoreWebView2_22`, Runtime 122.0.2365.46) and `NavigationKind` (Runtime 115.0.1901.177). The current Runtime is 157.0.4341.42. Microsoft services only the latest, and Edge's assisted support covers the current release and the two before it. An older Runtime gets the start-up dialog and exit 4. Chosen over 122 with feature detection, which needs two code paths and risks a white flash below 137. M4 confirms it on a real Runtime 139.
- **`<input type="file">`: blocked by default, with a C# opt-in.** The engine's picker never opens; the docs point to `files.open()`. `AllowPagePicker()` brings it back, with the risk explained: the page gets the content of a file outside the grant model. WebKitGTK cancels it from `run-file-chooser` (`WebKitFileChooserRequest.cancel()`). WebView2 has no API for it (WebView2Feedback issue 1479, "tracked"); the route others report, `Page.setInterceptFileChooserDialog` through `CallDevToolsProtocolMethod`, is checked by an M1b spike first, and the decision comes back if it fails.
- **Also found:** WebView2 has no switch for back and forward navigation. The routes are `AreBrowserAcceleratorKeysEnabled = false` (Alt+Left and the browser keys; the mouse buttons are not documented) and cancelling `NavigationStarting` when `NavigationKind` is `BackOrForward`, which Microsoft's specification names for the mouse buttons.

### The black-box data root: security review

- **Asset:** the app's per-user folder: the browser profile (cookies, storage, remembered permissions), settings, saved connections and credential references, logs and crash records.
- **Who can pass input:**
  - **The command line** comes from a shortcut, the shell, a file association, or a URL handler that pastes untrusted text into it (deep links are planned for M3).
  - **The environment** can be set only by a process that starts the app, which already runs as the user.
- **What a data-root input would allow:**
  - **Pointing at a network share,** so profile cookies, settings and logs land where an attacker reads them.
  - **Pointing at a prepared folder,** whose profile or settings the attacker controls. For example, a cached page or a service worker the page then trusts.
  - **Breaking single instance** by giving the second launch another folder.
- **Conclusion:**
  - **A command-line switch** puts all three behind input that a URL handler can reach. Limiting it to local absolute paths still leaves prepared folders.
  - **The environment** adds nothing a same-user process could not already do: it could write into the real folder directly.
  - So Production reads no Spar switch and no Spar variable for its folders, only the platform's own per-user variables.
- **Residual:**
  - **Windows reads `LOCALAPPDATA` from the variable,** so a process that starts the app with another value moves its folders. That process is already the user.
  - **The variable can be missing or relative** (a service, a stripped environment). The app then falls back to the known folder.
  - **The folder actually in use is still checked** (plan section 3c). A `LOCALAPPDATA` that is a UNC path is refused, as it is for the profile.

## 2026-11-17, Window-test profiles, trusted input, the page's pickers and the Sandbox runner (user decisions; spikes)

The last four "decide first" items of M1b, each after its spike: `spikes/window-tests` (WebView2 157, one NativeAOT probe, windows shown offscreen and never activated, all input through the in-process DevTools Protocol) and `spikes/sandbox-runner` (Windows Sandbox on this host, a NativeAOT runner prototype). The user chose the recommended option each time.
- **Profiles for window tests (issue I): InPrivate on one shared environment per test process.**
  - **Cost per test,** setup plus teardown, 25 tests each (p50 / p95): InPrivate 81 / 147 ms; a new named profile, then `Profile8.Delete`, 220 / 360; the same with `ClearBrowsingDataAll` instead, 259 / 313; a new user-data folder 510 / 1,072.
  - **Isolation:** every mode isolated `localStorage`, IndexedDB, a cookie, a service worker, a remembered permission and the HTTP cache (309 of 310 tests; the exception was a race in the probe).
  - **Named profiles stalled:** their first controller in a fresh folder took 5.2 s in 5 of 13 runs; InPrivate never did.
  - **Two test processes at once,** each with its own root, did not interfere.
  - **Cleanup:** each test process uses its own root under `artifacts/webview2-profiles/`. A later run deletes a leftover root only when its `EBWebView\lockfile` is gone, and otherwise uses a new root. The lock file was held for as long as the browser lived, disappeared when it exited (also after a kill, 76–88 ms later), and never stayed behind. Checking it costs 0.09 ms.
- **Trusted input in the in-process window (issue D): CDP `Input.*`.**
  - **Trusted:** clicks, `insertText`, key events and Tab arrived with `isTrusted` true, gave user activation (`window.open` reached `NewWindowRequested` as user-initiated), and focused the page. The window never became the foreground window.
  - **Latency** (p50 / p95, 100 each): a click 14.8 / 15.2 ms, a key 0.8 / 1.1, `insertText` 0.3 / 0.5.
  - **No first-input stall:** the 5 s stall was not seen in 200 first clicks. Window tests keep a two-frame wait after a navigation (about 18 ms).
  - **What stays in Windows Sandbox:** real OS input, for focus between windows, the clipboard and drag and drop.
- **The page's pickers on WebView2: intercepted, and the File System Access APIs removed.** This confirms the block of decision "M1b's remaining questions without a spike".
  - **Interception:** right after each controller is created, the host calls `Page.enable`, then `Page.setInterceptFileChooserDialog {enabled: true}`, through `CallDevToolsProtocolMethod`, and listens to `Page.fileChooserOpened`. 46 of 46 file-input clicks then opened no dialog, across same-origin, reload and cross-origin navigations, for `showPicker()`, and with `AreDevToolsEnabled = false`. The page saw only its `click`: no `change`, no `cancel`, no files.
  - **`Page.enable` first:** without it the call returns `{}` and the dialog still opens.
  - **Per WebView:** each WebView needs its own calls, and a cross-site iframe needs `Target.setAutoAttach` (flatten), then the same calls in each child session. Spar's navigation lock keeps foreign frames out, so this becomes necessary only if an app allows them.
  - **The File System Access APIs:** `showOpenFilePicker`, `showSaveFilePicker` and `showDirectoryPicker` were intercepted too, rejecting with `AbortError`. `Permissions-Policy` and CSP `sandbox` did not block them. The host also passes `--disable-blink-features=FileSystemAccessLocal`, so the page sees no such API, as on WebKitGTK.
  - **The opt-in:** `AllowPagePicker()` turns both off.
  - **In Production:** these are in-process DevTools calls; they open no devtools endpoint, and the production boundary test keeps checking that none is present.
- **The Windows Sandbox runner (issue J): two lanes, one sandbox per run.** Built in M1b as an internal part of `Spar.Testing`, for Spar's own checks; the app-facing layer (the trait, the fixture, one-off tasks) still waits for M3.
  - **In the guest,** which has no .NET: `WireShapes.Tests` published as the `aot` lane does passed 79 of 79, and a payload with real WebView2 windows passed 15 of 15. TRX, xUnit XML, the console log and the exit code came back through the mapped folder.
  - **Cost per cold cycle,** without the tests (7 cycles): 6.2–9.0 s with a user session, about 2.6 s with SYSTEM only. `wsb start` took about 1 s, and `wsb connect` until a user-session command worked 2.5–4.5 s. Reusing one sandbox saved about 5.8 s per extra job. Issue J's "minutes per run" was wrong for this host.
  - **One sandbox at a time:** a second `wsb start` fails at once with `0x800401F6` (`CO_E_APPSINGLEUSE`). The runner checks `wsb list` first and fails with a clear message.
  - **Lanes:** tests without windows run as SYSTEM, with no client window on the host. Window and input tests need the user session (as SYSTEM, `CreateCoreWebView2Controller` failed with `0x80080005`), whose client window took the foreground 2–3 times per connect, so the "ask first" rule stays for that lane.
  - **One sandbox per test run,** jobs in sequence, each staged under a new folder. A fresh sandbox only for tests that need a clean machine.
  - **Results stream as they arrive,** so a run killed at its deadline keeps them: xUnit writes TRX and XML only at the end, and the spike's killed run returned only its exit code and a partial console log.
  - **CI:** from documentation, hosted `windows-latest` is Windows Server 2025, and Windows Sandbox is a client-edition feature, so it stays a local or self-hosted lane.
- **Also found:**
  - `WireShapes.Tests` looks for `Spar.slnx` to find its results folder, so one test fails outside the repository; tests should get that folder from the runner.
  - `RatesContractTests.Without_an_implementation_the_app_does_not_start` failed 4 of 60 times on the host and 1 of 13 in the guest, unrelated to the Sandbox.

## 2026-11-17, The Windows host, first slice (implementation record; choices for review)

Plan M1b, build step 1: the host with STA ownership, instrumentation, asset serving on `https://app.localhost`, the bridge with document epochs, teardown behind the close request, and failure recovery; the Notebook runs in a window. Evidence: `docs/evidence/m1b-host.md`.
- **Built:** `src/Spar.Windows` (WebView2 through hand-declared `[GeneratedComInterface]` interfaces, Win32 through CsWin32, the loader linked statically under NativeAOT and `WebView2Loader.dll` on CoreCLR); `spar.OnCloseRequested` with `CloseRequest`, `CloseReason` and `CloseDecision` as `docs/app-model.md` 6.11 shows them; `SparPlatforms` wired in `Spar.Sdk`; the WebView2 transport in `src/Spar.Client` (`webViewChannel`, `hostChannel`). The stand-in `Closing.cs` and the planned `OnCloseRequested` left `tests/AppModel/Planned`.
- **Numbers:** the Notebook as one NativeAOT executable of 4,512,768 B (2,918,400 B at M1a), 0 warnings; window shown 18.5 ms after the host starts, controller 178 ms, `ui-ready` 265 ms (medians of 7); a close request to exit in 25–30 ms with exit code 0, and no `msedgewebview2` process left.
- **Choices made while building, for review:**
  - **How `Run` reaches the host:** the generator intercepts `app.Run()`, as it already intercepts `Create`, and passes `new Spar.Windows.SparWindowsHost()` behind `OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)` when the compilation references `Spar.Windows`. NativeAOT folds the OS check per target, so a Linux executable will keep no Windows host. No module initializer and no registry of hosts: the host travels as an argument (`SparApp.Run(ISparHost?)`, hidden from IntelliSense), so Spar keeps no static state for it. An app that never calls `app.Run()`, such as the console harness, references no host and its image does not grow. The Test variant gets no interceptor: it runs through its test session.
  - **`ISparHost` is a public interface** in `Spar.Runtime`, hidden like `ISparInvoker`, rather than an abstract class with internal members, so the generator's tests can compile a stand-in host.
  - **A build without a host for the OS** prints that `SparPlatforms` lacks it and exits 3 ("missing"). The decision only says "a clear error naming the property".
  - **Interop declarations are committed source,** hand-declared with the help of a throwaway script over `WebView2.h`, not generated at build time: they are few, they change only when the host needs a new slot, and the slot-order test in `Spar.Tests` reads `Spar.Windows.dll`'s metadata and the header of the pinned `Microsoft.Web.WebView2` package and checks every declared method's slot, placeholders included (`SlotNNN_<header name>`). Interface-returning methods return raw pointers that `Com.Take` wraps as unique instances, released on the UI thread.
  - **Document epochs:** `NavigationStarting` closes the current document's session (its calls cancelled, late replies dropped); the new session opens at `ContentLoading`, and a message arriving between the two is dropped. Opening the new session at `NavigationStarting`, as plan 2c words it, would let a message still in flight from the old page take an id in the new session and silence the new page's first calls. `BridgeSession` now runs nothing for a call that arrives after `Close`.
  - **Parsing off the UI thread:** the UI thread only copies the string out of the event; one reader task decodes and admits messages in arrival order. Replies are posted to the UI thread, one window message per burst.
  - **The page's close work** (`closing`, `closing-done`, at most 1 s) runs after the request is approved and before calls stop; commands then get up to 3 s to settle before the controller closes.
  - **A failing backend guard** is logged and becomes an ask ("The app could not check whether it is safe to close. Close anyway?"), or a close at session end and in the automated mode. A guard that ignores its token loses its say at the session-end deadline.
  - **Recovery budget:** one automatic reload after a renderer exits, available again once a document reports `ui-ready`; one recreation of the environment and controller for the app's life. Anything further shows a message and exits 5.
  - **Folders:** the per-user root is `LOCALAPPDATA` (absolute and local, else the known folder) plus the app's assembly name, and the profile is its `WebView2` subfolder (`SparFolders.ForApp`, the one place step 5 changes for the app id and channels).
  - **Logging:** the host's phases go to the app's logging (category `Spar.Host`) and to standard error when the process has one, which is how the evidence was read. The per-user log file is step 7.
  - **The window's first colour** is the system's app theme (white or `#1f1f1f`) as the class brush; the other layers of plan 3d are step 2.
  - **Spar's application manifest** is added to Windows apps that have none: Common Controls 6 for the task dialogs, per-monitor DPI v2, Windows 10 and 11.
  - **`Spar.Development.IsSupported`,** a new feature switch: on in Development and Test, off and trimmed in Production. It gates failure details for the page and `SPAR_AUTOMATED`.
  - **No `System.Uri` in the host:** its flag names (`LoopbackHost`) made the Production boundary scan fail, so the origin check and path decoding are plain string work on WebView2's canonical URLs.
  - **The page side:** `hostChannel()` picks WebView2 when `window.chrome.webview` exists and loopback otherwise, so the Notebook's Production bundle still carries the loopback transport, as it did before.
- **Deviations from the design:** none in behaviour beyond the epoch's opening point above. `WM_ENDSESSION`, the page's guard, `CloseReason.App` and the shutdown hang prompt are not built; the seam for the page's guard is `WindowsApp.AskPageAsync`.
- **An incident while building, fixed:** the package test's consumer app, published with the new host, opened a real window from a test run and created its WebView2 profile in the developer's own `%LOCALAPPDATA%\NotesTool` (73 MB), against `AGENTS.md`. The folder was deleted with the user's approval. The test now publishes without the Windows host and also runs the executable with `LOCALAPPDATA` inside its own root, so a host can never reach the developer's folder from it. Any test that starts an app's executable sets `LOCALAPPDATA` (Windows) or `XDG_*` (Linux) the same way (pitfall "A test that starts an app's executable").

## 2026-11-17, The Windows host's first-slice choices accepted (user decision)

Asked about the choices listed in "The Windows host, first slice (implementation record; choices for review)", the user kept them all: exit 3 for a build with no host for its OS, the document's session opening at `ContentLoading`, the generated host hand-off to `app.Run()`, a throwing backend guard counting as Ask, one recovery of each kind, and folders by assembly name until the app id (step 5).

## 2026-11-17, Start-up: prerequisites, single instance and theme (implementation record; choices for review)

Plan M1b, build step 2. Evidence: `docs/evidence/m1b-startup.md`.
- **Decided (user, 2026-11-17), built:**
  - **The minimum Windows: Windows 10 22H2 (build 19045), provisional.** An older Windows gets the start-up message and exit 7; M4 confirms the floor on a real Windows 10 22H2. Features of Windows 11 are detected at run time.
  - **The start-up hook: `spar.OnStartupProblem(problem => …)` in `Program.cs`,** returning true when the app showed its own message, so Spar shows none. `StartupProblem` has `Kind` (`Missing`, `TooOld`, `Broken`, `OsTooOld`, `NotResponding`, `OtherUser`), `Component`, `Found` and `Required` (nullable), `Details` and `HelpUrl`. It runs before the app's services exist; standard error and the per-user log are written regardless, and the exit code never changes. It also covers a relaunch that cannot reach the running app (8, 9). Shown, compiled, in `docs/app-model.md` 3.15.
- **Built:** the prerequisite checks in plan 3's Windows order with the task dialog, the `MessageBoxW` fallback and `ShellExecuteW`; the per-user log file `host.log` (step 7's first part); single instance as `spikes/windows-single-instance` measured it, with `spar.OnRelaunch` real; the window in the theme's colour from its first frame, with every host layer of plan 3d; `WindowOptions` with `Background`, `Size` and `MinSize`.
- **Numbers:** each path forced on NativeAOT and CoreCLR, Production and Development, gave its message and code (3, 4, 5, 7) in 67–449 ms; a relaunch hands over in 0.5–0.6 ms and the second process exits 0 in 30–31 ms; a suspended first instance gives exit 8 at 2.0 s; 0 white or `#121212` frames in dark and 0 dark frames in light (5 runs each); the transparent background costs nothing measurable (`ui-ready` 290.7 against 294.5 ms, WebView2 memory 86.3 against 86.4 MB). The Notebook's executable is 4,772,864 B.
- **Choices made while building, for review:**
  - **Order:** Windows, then single instance, then the Runtime. Single instance comes before the Runtime check so a relaunch hands over at once and never reads the Runtime; plan 3 listed the Runtime checks first.
  - **Windows older than 1809,** where `Spar.Windows` cannot load (its assembly is marked for 10.0.17763, WebView2's floor), get exit 7 too: the generated `Run` hands them `Spar.Runtime.UnsupportedWindowsHost`, which writes stderr and the log line, runs the hook and shows a message box. Without it they got step 1's "no desktop host" (exit 3) on stderr only. .NET still runs on Windows Server 2016 (14393).
  - **`Found` and `Required` are strings,** as the platform reports them (a WebView2 version can carry a channel name), not `Version`.
  - **The hook is not called in the automated mode,** where no message is shown: nobody can answer it. It runs on a thread of its own (an STA), never on the window's UI thread, and a hook that throws counts as false.
  - **Development-only overrides** force the paths on a healthy machine: `SPAR_DEV_REQUIRE_WEBVIEW2` and `SPAR_DEV_REQUIRE_WINDOWS_BUILD` raise the minimums, and `SPAR_DEV_WEBVIEW_BACKGROUND=default` keeps WebView2's own background for the measurement. All three sit behind `Spar.Development.IsSupported`, so a Production build has none (observed: ignored, and their names are absent from the image). A real "too old" was also forced on Production with a patched copy of `EmbeddedBrowserWebView.dll`.
  - **The log file** is `host.log` in the app's logs folder (`spar.Folders(f => f.Logs(…))` moves it), written by its own task so the UI thread never waits for the disk, appended by every process of the app through an append-only handle (a `FileStream` in append mode lost half the lines of two writers; pitfall); above 5 MB a run moves it aside to `host.1.log` until M3's rotation. It holds the host's lines, not yet the app's own logging.
  - **Standard error without a handle:** a desktop executable started from a console, without redirection, attaches to that console for the problem's line (`AttachConsole(ATTACH_PARENT_PROCESS)`), as the spike's batch file saw it.
  - **Single instance details beyond the spike:** the lock file is `instance.lock` in the data root (beside the profile and the data); the frame carries the sender's `GetTickCount64`, and the primary drops a relaunch that waited longer than its sender's 2 s (a suspended primary that resumes would otherwise act on a launch already reported as failed); the pipe closes at the start of the teardown and the lock is released at process exit; a closing primary answers "refused", reported as exit 8 "closing". The client opens the pipe with `CreateFileW` and reads token user SIDs itself: `NamedPipeClientStream` and `WindowsIdentity` added 168 KB to the image.
  - **The relaunch, in order:** the pipe's task acknowledges once the relaunch is posted to the UI thread, which shows and restores the window, calls `SetForegroundWindow`, then queues the relaunch for the handler. Spar logs the number of arguments, never their values.
  - **The test host has no single instance:** the Test variant never reaches the desktop host (no `Run` interceptor), and each test app has its own data root anyway, so there is nothing to disable.
  - **The theme:** the system's (`AppsUseLightTheme`), or the app's saved choice read from `color-scheme` in the data root (`light`, `dark`, `system`), the file the SDK's `setColorScheme` will write. The profile's `PreferredColorScheme` is Auto when following the system, and the choice otherwise. A system change (`WM_SETTINGCHANGE`, `ImmersiveColorSet`) repaints the window and the title bar; it reads the registry value on the UI thread (one value of the user's hive).
  - **The colours are read at run time** from `spar.Window(…)`, which runs before `Run`: that is all the first frame needs. What 3d still needs from the build, the page's theme style and its CSP hash, waits for the step that reads the constants.
  - **Defaults:** without `Background`, white and `#1f1f1f`; without `Size`, a 1024 × 768 content area. `Size` and `MinSize` are the content area in device-independent pixels, scaled by the monitor's DPI and centred in the primary monitor's work area; the window follows `WM_DPICHANGED`.
  - **The Notebook** declares its page's own sheet colours (`#ffffff`, `#1d1f24`), 1100 × 720 and a 640 × 420 minimum.
  - **The documentation check:** the window options Spar lacks (`TitleBar`, `RememberPlacement`, `CloseToTray`, `Route`, `Modal`, `SingleInstance`, `ProtectFromCapture`, `Zoom`, `FindInPage`, `Commands`) are C# 14 extension members on the real `WindowOptions` in `tests/AppModel/Planned`; window kinds stay planned and configure the main window's options there, compile-only. The relaunch stand-in left `Planned`.
- **Found:** the loader reads the Runtime's version from `EmbeddedBrowserWebView.dll`'s version strings, not from its fixed file version; a folder holding only that DLL reads as a healthy 157 and fails at controller creation (`0x80070002`). A primary that `DisconnectNamedPipe` skipped after a broken pipe spun in a loop writing 40 MB of log in seconds (fixed before the evidence: it always disconnects and backs off).
- **Not verified:** focus under Windows' real foreground rules (the spike checked it), a live theme change (added to "Checks that need the user at the PC"), exit 9, Windows 10, an older real Runtime, the `MessageBoxW` fallback, the download page opening.

## 2026-11-17, Start-up's choices accepted (user decision)

Asked about the choices listed in "Start-up: prerequisites, single instance and theme (implementation record; choices for review)", the user kept them all: the order Windows, single instance, Runtime; the stand-in host that reports "OS too old" (exit 7) below Windows 10 1809; `Found` and `Required` as strings; no hook call in the automated mode; the Development-only variables that force each path; the pipe client through `CreateFileW` and the token's user SID (168 KB smaller than `NamedPipeClientStream` and `WindowsIdentity`); relaunches stamped with their send time and dropped when their sender already gave up; `instance.lock` and the saved theme choice in the data root; colours read at run time until the build reads them; no single-instance setup in the test host; the Notebook's colours and sizes; the WireShapes relaunch handler and opt-in hook; and the probe kept in `docs/evidence/m1b-startup/`. The review that followed changed one thing: `host.log` is opened append-only, since a `FileStream` in append mode lost half the lines of two writers (pitfall).

## 2026-11-17, Hangs: the page's hook, the Production stall probe and "Exit now" (user decisions)

Before M1b build step 3 (plan section 3, "Hung pages" and the UI-thread stall probe; the shutdown hang policy of `docs/research-review.md`), three public shapes were still open.
- **The app observes a hung page; it cannot replace Spar's dialog.** `spar.OnPageUnresponsive((hang, services) => …)` is called on each change of a window's page: unresponsive, recovered, reloaded, closed anyway, with the window and how long the hang lasted, for logs and telemetry. Spar's native dialog always shows, and the hook cannot kill or reload anything. Chosen over a hook that returns true to hide the dialog, like `OnStartupProblem`, since an app has no native UI of its own to show instead, and over no hook until M3.
- **A Production app keeps the stall probe with `spar.Diagnostics(d => d.KeepStallProbe())`.** It logs only. The call is what keeps the probe's code in a Production image; without it the trimmer removes the probe and the production boundary test finds none. `Diagnostics` is the group where M3's crash records and `diagnostics.collect()` options also go. Chosen over an MSBuild property, which would have changed the decision for a C# option, and over a method directly on the builder.
- **"Exit now" exits with a new fixed code, 10:** shutdown did not finish and the user ended it. It joins the fixed table that apps cannot change, so scripts and installers can tell a clean exit from one that skipped the app's cleanup. Chosen over 0, which hides the skipped cleanup, and over keeping the close's own code.

## 2026-11-17, Hangs: heartbeat, stall probe and shutdown prompt (implementation record; choices for review)

Plan M1b, build step 3: the hung-page heartbeat, the UI-thread stall probe and the shutdown hang policy, on the Windows host, with the three shapes decided in "Hangs: the page's hook, the Production stall probe and "Exit now"". Evidence: `docs/evidence/m1b-hangs.md`.
- **Built:** `ping` and `pong` in the bridge protocol (C# codec and session, the SDK's connection, the shared vectors); `Spar.Hosting.PageHeartbeat` (the rules, host-neutral and unit-tested) driven by `Spar.Windows.PageWatch`; the native "Wait / Reload page" and "Wait / Close anyway" dialogs (`PromptDialog`); "Reload page" ending the renderer, then reloading once; `spar.OnPageUnresponsive((hang, services) => …)` with `PageHang(Window, State, Duration)` and `PageHangState` (`Unresponsive`, `Recovered`, `Reloaded`, `ClosedAnyway`); the stall probe (`UiStallProbe`) with every Spar callback on the UI thread named, and `spar.Diagnostics(d => d.KeepStallProbe())`; `ShutdownWatch` with "Keep waiting / Exit now" and exit code 10; Development-only triggers to force each path; `docs/app-model.md` 2, 3.13, 3.15, 3.16 (new, with `HangLog.cs`) and 6.11, compiled against the stand-ins and through real Spar (32 blocks).
- **Numbers** (27 of 27 scenarios in the second full run; the first was set aside because the user maximized one of its windows and two probe steps were wrong):
  - the alarm 15.5–19.2 s after an endless loop began (7 runs) and 16.8–18.9 s after the renderer was suspended (6 runs, Production NativeAOT); no alarm on an idle page for 60 s, nor on work of 3 s or 10 s (6 runs); work of 25 s alarmed at 16.6 s, and the dialog closed by itself when the page answered; a resumed renderer's dialog went 20 ms after the resume;
  - "Reload page": the renderer ended 0.3–3.7 ms, and the new document's `ui-ready` came 85–140 ms (median 91), after the choice (8 runs);
  - the Wait schedule on a 500 ms heartbeat: back after 1.9, 3.9, 7.9, 15.9, 15.9 s (20, 40, 80, 160, 160 s at 5 s);
  - a close of a silent page: "Wait / Close anyway" 5,016 ms after the request; Close anyway to exit 0 in 42 ms, with the renderer ended;
  - the shutdown prompt at 10.0 s and again at 40.1 s; Exit now ended the process with code 10;
  - the heartbeat's longest tick on the UI thread 0.15–0.36 ms on NativeAOT; no stall over 50 ms in any run but the two forced ones;
  - the Production Notebook carries no probe and no trigger; keeping the probe costs 32,256 B (measured on the harness); the Notebook grew by 76,800 B to 4,849,664 B.
- **Deviations from the design:**
  - **Arming.** The design arms the heartbeat when the document is created, through the injected script. Spar's host injects no script: the page's SDK is that script, and its `hello`, the first thing the page's start-up code sends, arms the heartbeat when it lists `ping`. A document whose page never connects the SDK is never pinged, as the design wants for documents without the script. A page that hangs in its own code before it calls `connectSpar` is not seen by the heartbeat; WebView2's own report still covers it once input reaches it. `NavigationCompleted` is never used.
  - **The page's close guard is still not built** (`windows.onCloseRequested`, its messages and the generated `windows` module). In its place a close request sends a ping out of turn and gives the page one heartbeat interval (5 s) to answer; a page that answers agrees. When the guard is built, its answer replaces the ping, and the silent-guard path already leads to the same dialog.
  - **WebView2's `RenderProcessUnresponsive` never fired** in these runs, also with the dialog open 30 s over a suspended renderer: the windows were never activated, so the dialog took no focus and no input reached the page. The single state for both reports is unit-tested only.
- **Choices made while building, for review:**
  1. **The protocol:** `ping` (host to page) and `pong` (page to host), each with `seq`, a safe integer from 1 that is not a call id and follows no id rule; sent only to a page that listed `ping` in `hello`; malformed without a valid `seq`; loopback never pings.
  2. **The Wait schedule counts visible ticks** (4, 8, 16, then 32 every time), so each return comes up to one interval early: 15–20 s, 35–40 s, 75–80 s, 155–160 s.
  3. **A hidden or minimised window** counts no misses and forgets its outstanding ping, so a throttled page is never reported; a hang already seen stays, with its schedule paused.
  4. **"Reload page" ends every renderer process of the app's environment,** found with `GetProcessInfos` on the UI thread: one environment per app and single instance make them this window's (secondary windows in M3 will need the renderer of one WebView). It does not use up the automatic reload, since the user chose it; with no renderer found, the page is reloaded as it is.
  5. **A hung document replaced another way** (its renderer crashed and was reloaded automatically, or it navigated) is reported to the hook as `Reloaded`.
  6. **Close anyway:** the backend's guard still runs, and `ClosedAnyway` is reported only when the teardown starts; a hung page's close work is skipped and its renderers are ended before the controller closes. A backend that keeps the window open returns the page to the Wait schedule.
  7. **The dialogs run on an STA thread of their own** (`PromptDialog`), owned by the window, whose thread keeps pumping, and are taken down with `TDM_CLICK_BUTTON`. The first button (Wait, Keep waiting) is the default, and Esc or the close button choose it. The texts: "<app> is not responding" with "The page has not answered for N seconds. You can wait for it, or reload it: what the page has not saved is lost, and the app's other data is kept." (or "…close the window anyway: what the page has not saved is lost."); "<app> is taking long to close" with "It has been closing for N seconds. You can keep waiting, or exit now: exiting now skips the app's remaining cleanup, so what it has not saved yet may be lost."
  8. **A page that never answered pings but was reported unresponsive by the engine** keeps being asked about on the schedule after Wait, since its recovery cannot be seen.
  9. **The automated mode** shows no hung-page dialog and no shutdown prompt: the change is logged and the hook called; a close of a page that does not answer closes anyway, as an ask does; a long shutdown is only logged.
  10. **The hook** has one shape, `Action<PageHang, IServiceProvider>`, with no async overload; it runs on the thread pool, one change at a time, and the process waits up to 3 s for it at exit. `Duration` counts from the first ping the page missed (or the engine's report, or the close request), so it is up to 5 s shorter than the hang.
  11. **The stall probe** posts one probe at a time, 20 ms after the last one ran (plus the timer's resolution, about 16 ms), and reports the larger of the probe's delay and the slowest named Spar callback that ran meanwhile, naming that callback only when it took over 50 ms itself. A nested callback that took at least half of its caller's time keeps its name; a callback during which the probe ran (a modal dialog) is no stall; window messages are named only for those Spar handles. It logs through the host's log (stderr and `host.log`), from before the app's logging exists. Window tests read `Longest`, `Count`, `Recent()` (the last 64) and the `Stalled` event, against `WindowTestLimitMilliseconds` (250).
  12. **The control for the kept probe is the console harness,** which now calls `KeepStallProbe()` (+32 KB, within its 3.5 MB budget): `Spar.BuildTests` finds the probe's type and warning in it and neither of them in the Production Notebook, and the Development triggers' names joined the boundary markers. The probe's interface is `IUiThreadWatch`, because `IUiStallProbe` contained the marker (pitfall).
  13. **The shutdown watch** runs from the teardown's start until `Run` returns, after the services are disposed and the single-instance lock is released: the first prompt after 10 s, then 30 s after each "Keep waiting". The prompt has no owner and asks for the foreground. "Exit now" writes its line, flushes the log briefly and ends the process with `TerminateProcess` on itself, so nothing still stuck, process-exit handlers included, can hold it.
  14. **Development-only triggers,** each called only behind `SparDevelopment.IsSupported` at its call site, so the class is trimmed from Production: `SPAR_DEV_PAGE_HANG` (`<delay ms>` for an endless loop, `<delay ms>:<length ms>` for finite work, run through `ExecuteScript` after the first `ui-ready`), `SPAR_DEV_UI_STALL_MS`, `SPAR_DEV_SLOW_DISPOSE_MS` and `SPAR_DEV_HEARTBEAT_MS` (at least 100 ms; the Wait schedule and the close check's interval scale with it).
  15. **New interop:** `ICoreWebView2Environment8.GetProcessInfos` (slot 18), `ICoreWebView2ProcessInfoCollection`, `ICoreWebView2ProcessInfo`, and `ExecuteScript` with its completion handler, all checked by the slot-order test.
- **A bug found and fixed while verifying:** `Recovered` reported 0.0 s, because the heartbeat cleared the hang's start before the change was reported; `PageWatch` keeps it now.
- **Not verified:** real input on the dialogs and the prompt, the prompt's foreground on the real desktop, `RenderProcessUnresponsive` on a real run, a hang before the SDK connects, minimised and hidden windows on a real run, the message-box fallback, Windows 10, Linux.

## 2026-11-17, The page hang hook gets its async form; the heartbeat's start moves to step 4 (user decisions)

Asked about two of the choices in "Hangs: heartbeat, stall probe and shutdown prompt (implementation record; choices for review)".
- **`spar.OnPageUnresponsive` gets the async form, like the close guard and the relaunch handler.** `(hang, services, cancellationToken) => ValueTask` is the main form, and `(hang, services) => …` the shorter one for a hook that finishes at once. Each change is awaited before the next, still off the window's UI thread. At exit the changes already reported (a "closed anyway" comes just before it) still reach the hook within about 3 s; when that wait ends, the hook's token is cancelled and what is still queued is dropped. The synchronous form alone was safe for the window, which never waits for the hook, but it left a hook that writes a file or sends telemetry to block a pool thread or fire and forget, losing its errors and its order, and it could not be told to stop at exit. `docs/app-model.md` 3.16's `HangLog` now appends each change to a file through the async form; two tests cover the await order and the cancelled token at exit.
- **The heartbeat's start moves back to the document's creation in M1b step 4.** Step 3 armed it on the SDK's `hello` only because the host injects no script yet, so a page that hangs in its own start-up code, before it calls `connectSpar`, is never seen. Step 4 builds the host-injected script for the violation listener (plan section 3b); that script also answers the pings, and the heartbeat is armed when the document is created, as plan section 3 designed it. Until then, choice 1 of the step-3 record stands.

## 2026-11-17, Page security's public shape (user decisions, M1b step 4)

Asked at the start of M1b step 4, with options and a recommendation for each; the user took the four recommendations and added one requirement.
- **The CSP in C#: directive methods take sources only, and the risky keywords have named methods.** `spar.ContentSecurity(csp => …)` has one method per directive an app may widen (`FontSrc`, `ImgSrc`, `ConnectSrc`, `MediaSrc`, `ScriptSrc`, `StyleSrc`, `FrameSrc`), taking hosts and schemes; `FrameSrc` also takes `'self'` (decision "Same-origin iframes blocked"). `'unsafe-inline'`, `'unsafe-eval'`, `'wasm-unsafe-eval'` and the other keywords and nonces are a located analyzer error in those methods and come only through named methods whose documentation explains the risk, when it is reasonable and the safer alternative: `AllowInlineStyles()`, `AllowEval()`, `AllowWebAssembly()`, besides `StyleNonce()` (decided 2026-11-17), `WithoutTrustedTypes()` and `WithoutIntegrity()`. The alternative, any CSP source string with only a warning, left the risk inside a string.
- **Several values per directive, one valid policy (user requirement).** Each directive method takes several sources at once and may be called more than once; the policy Spar sends has each directive once, with every value for it grouped together (the app's, Spar's own and the build's hashes and nonces), duplicates removed, in a stable order. Browsers use only the first copy of a repeated directive, so a policy that repeated one would silently drop values.
- **An external script or stylesheet without `integrity` fails the build,** from the Vite plugin, in every variant, so it shows in Debug first. Fonts are exempt, since they cannot carry SRI. `WithoutIntegrity()` turns the check off with the rest of SRI. The alternative, a warning, let an unhashed CDN script ship unnoticed.
- **Violation reporting: one call, a setting per channel.** `csp.Reporting(listener: CspReports.Always, endpoint: CspReports.Always)`, each `Always` (the default), `DevelopmentOnly` or `Off`, as decision "CSP reporting on in Development and Production" asked. A single setting for both channels could not turn the backstop off alone.
- **Permissions: step 4 builds the first layer; the second comes with M3's camera work.** Step 4 builds `p.Allow(Permission.X)` (declaring grants), `Permissions-Policy` from the declarations, and the deny-by-default `PermissionRequested` gate with `SavesInProfile = false`, over an enum of the features both the header and WebView2 know. The WebView's own prompt per feature, the C# handler, and listing and resetting remembered answers come in M3, where a real camera checks them with the user at the PC (decision "Camera, microphone and other permissions"). Building them now could only be checked with Chromium's fake devices.
- **Unchanged and also in step 4:** the host-injected script answers the heartbeat's pings, so the heartbeat is armed at document creation (decision "The page hang hook gets its async form; the heartbeat's start moves to step 4").

## 2026-11-17, Page security (implementation record; choices for review)

Plan M1b, build step 4, with the public shape of decision "Page security's public shape". Evidence: `docs/evidence/m1b-page-security.md`.
- **Built:** `spar.ContentSecurity(csp => …)` (`CspBuilder`: `FontSrc`, `ImgSrc`, `ConnectSrc`, `MediaSrc`, `ScriptSrc`, `StyleSrc`, `FrameSrc`, `AllowInlineStyles`, `AllowEval`, `AllowWebAssembly`, `StyleNonce`, `WithoutTrustedTypes`, `WithoutIntegrity`, `Reporting(listener, endpoint)` with `CspReports`) and `spar.Permissions(p => p.Allow(Permission.X))`, each risky method documenting its risk, when it is reasonable and the safer alternative; `CspSourceRule`, one rule for the builder and the build; `PagePolicy`, one valid policy; `PageSecurityAnalysis` and `PageSecurityAnalyzer` (SPAR101–SPAR104, and SPAR105), the catalog's `PageSecurity`, `csp.json` from the client step, the Development comparison; on Windows `PageGuard` (navigation lock, new windows, permission gate), `ExternalLinks`, the injected `PageScript.js`, the headers, nonce and `report-uri` in `PageServer`, `CspViolationLog`; the `csp` bridge message; the Vite plugin's SRI and external-integrity check; the loopback host on the same policy; `docs/app-model.md` section 2 and its stand-ins (`tests/AppModel/Spar.Stubs/ContentSecurity.cs`; `ContentSecurity` and `Permissions` removed from `Planned`, so `Real` compiles them through real Spar); the `spar-page-security` skill; `tests/apps/SecurityProbe`.
- **Numbers** (WebView2 157, NativeAOT unless noted): 100,000 identical violations are one event and one report (the engine deduplicates them); 100,000 distinct kept the page busy 19.7–20.2 s with no listener and about 9.5 s more with one counting them all, so the listener now stops after 1,000 per document (20.07 s, pings answered throughout); 1,000 distinct gave 1 bridge message, 100 log lines and 1 summary; `report-uri` reached the host 89 times per document (20 read, 69 over the limit); the heartbeat's tick 0.07–0.29 ms; working set 24.2 MB idle, 29.9 MB after 100,000 distinct, the same with the listener off; a page hanging before it connects reported 19.6–19.7 s after its document opened (3 runs, Development); controller to first document 102–137 ms with the script awaited first (100–121 ms before); the Notebook 4,940,288 B (+90,624 B) with 0 violations, its 7 browser journeys passing under the same policy.
- **Choices made while building, for review:**
  1. **Permissions:** `Camera`, `Microphone`, `Geolocation`, `ClipboardRead`, `Sensors` (accelerometer, gyroscope, magnetometer; WebView2's "other sensors"), `LocalFonts`, `Midi` (the policy's `midi`; WebView2's system-exclusive kind), `WindowManagement`, `Autoplay`. USB, serial, HID, Bluetooth, payment and display capture stay `=()` with no option (WebView2 has no request for them); notifications, downloads, file access and persistent storage, which no header covers, are always denied. `autoplay=()` is new: undeclared, media with sound no longer plays without a gesture.
  2. **`'unsafe-inline'` with a nonce or hash:** with `AllowInlineStyles()`, Spar puts no nonce or hash in `style-src`, so the author's choice always holds (its own theme hash included, when it comes); `StyleNonce()` beside it is a new warning, **SPAR105**, on `StyleNonce`. `'unsafe-inline'` exists only for styles, so no other directive can meet the case.
  3. **`media-src 'self'` in the base policy**, a deviation from plan 3b's text: without it an app could not play its own media, no more a risk than its own images, and `MediaSrc` had no base to add to. `frame-src` is written only when `FrameSrc` is declared, with no implicit `'self'`.
  4. **Order and duplicates:** each directive once, in a fixed order; values: the base value, the opt-in keywords (`'unsafe-eval'`, `'wasm-unsafe-eval'`, `'unsafe-inline'`), the app's sources in the order first declared, then the build's hashes, then the document's nonce. Sources are normalized (scheme and host lower case) before duplicates are dropped. Permissions follow the enum's order, at build and run time.
  5. **The source rule** refuses, besides keywords, nonces and hashes: a bare `*`, `javascript:`, credentials, and `data:` or `blob:` in `ScriptSrc` (each runs script the page builds, as `eval` would). A directive method takes constants one by one: an array or a collection expression is SPAR102.
  6. **SPAR101 applies to apps only** (a compilation whose entry point is top-level statements): a library or test project may call the builder freely, and the Development comparison catches a declaration the build did not read. SPAR103 covers a delegate variable or method group, other statements in the lambda, and calls that are not the options' methods.
  7. **The build's import-map hash is read by the host** from the served HTML at start (only Spar's own `<script type="importmap">`, hashed as written), not passed from the build: every host, the loopback host included, gets it the same way, and whoever can change the executable could change both anyway (plan 3c, "what the controls defend"). A multi-page app gets every page's import map in one policy.
  8. **The listener's limits:** a batch at most every second, the first 100 ms after the first violation; 100 groups per document; it stops listening after 1,000 violations in a document and says so (`"stopped":true`), because the engine creates an event per violation only while a listener exists. Texts cut to 256 characters, samples to 40.
  9. **The host's limits:** 100 groups per document (one line each, then one "more than 100" line), a summary at most every 10 s naming the 10 largest groups, a group's count the larger of the two channels', `report-uri` bodies up to 16 KiB and 20 at once then 5 a second (a token bucket on the UI thread), a parse queue of 256 that drops and counts, one totals line at exit. Production logs each new group and the summaries; Development adds the C# that would allow it (`CspAdvice`, passed in behind the feature switch, absent from Production and among the boundary test's markers).
  10. **The injected script and the page:** it runs in the app origin's top document only, defines nothing on `window`, and binds `postMessage`, `addEventListener`, `removeEventListener` and the built-ins it calls before page code runs. Page code can already post any message, fake `csp` batches and `pong`s included, and the host bounds them and treats them as untrusted, so the script gives the page nothing new; a page that breaks the listener loses only its own reports, and `report-uri` remains. The listener sits on `window`, capturing and registered first, so no page listener can stop the event before it.
  11. **The heartbeat's arming:** a session's `HostAnswersPing` (set by the Windows bridge for every document but an error page) replaces `hello`'s `ping`; the SDK no longer lists or answers `ping`; the loopback host never sets it, so it never pings. `BridgeSession.Opened` lost its argument.
  12. **The `csp` bridge message:** page to host, a header with no fields, the batch as payload; in the shared vectors.
  13. **New windows:** `NewWindowRequested` is always handled. Only a user-initiated request to `http(s)` (with a host, no credentials, not the app's own origin), `mailto` or `tel`, at most 2,048 characters with no spaces, quotes or control characters, goes to `ShellExecuteW` on an STA thread of its own; everything else is logged and blocked. Frames may load `about:blank` and `about:srcdoc`, which keep the app's origin and policy.
  14. **Permission requests** are allowed only from the app's origin for a declared feature, with `SavesInProfile = false` and `Handled = true` on `ICoreWebView2PermissionRequestedEventArgs3`, feature-detected, and the base interface as fallback; each kind logged once per run.
  15. **`AddScriptToExecuteOnDocumentCreated` is awaited before the first `Navigate`,** so the first document has the script for certain; it overlapped the navigation's setup (controller to first document unchanged).
  16. **`DevelopmentOnly` reports in Development and Test builds;** the loopback host counts as Development.
  17. **The Vite plugin** works in `writeBundle` on the bundle's final contents (the HTML rewritten in place), checks external tags in `transformIndexHtml` on the development server and in every build, and treats a `<link rel="modulepreload">` to another host like a script. CSS that Vite loads lazily at run time gets no SRI (import maps cover modules only).
  18. **Image size:** the interfaces the host only calls are declared `Options = ComInterfaceOptions.ComObjectWrapper` (the existing ones too), log messages take strings only, and permissions are an array and a bitmask rather than enum collections; without these the Notebook was 5,146,624 B, over the 5 MB budget.
- **Deviations from the design:** `media-src 'self'` (choice 3); SPAR105 (choice 2); the listener stops at a bound (choice 8), beyond "a bounded number of groups per document".
- **Found:** Trusted Types make `new Worker(url)` a script sink, Vite's worker pattern included: an app passes the address through a policy (`docs/app-model.md` section 2, pitfalls).
- **Incident:** six Production flood runs showed the hung-page dialog on the desktop (correctly: the page was busy 20 s, and the window was offscreen); the host took each down when the page answered, and no input was sent. Later long runs used Development with the automated mode.
- **Not verified:** a user's click on an external link (`ShellExecuteW`), frames with `FrameSrc`, `LaunchingExternalUriScheme` (not handled), the opt-ins on a real page beyond the nonce, a Runtime without `PermissionRequestedEventArgs3`, the camera and microphone, the development server's header (M1d), `--js-flags=--noexpose_wasm`, Linux.
- **For the user's decision:** whether `autoplay` belongs among the features denied by default (choice 1); `media-src 'self'` in the base policy (choice 3); SPAR105 as a warning rather than an error (choice 2).

## 2026-11-17, Page security's choices accepted (user decisions)

Asked about the three choices "Page security (implementation record; choices for review)" left for the user; the user took each recommendation and accepted the rest of the record.
- **Autoplay stays denied by default** (choice 1): `autoplay=()` unless the app declares `Permission.Autoplay`, so media with sound starts only after a user's gesture. Sound that starts by itself is a surprise for the user, and the app that wants it says so in one line.
- **`media-src 'self'` stays in the base policy** (choice 3): an app's own audio and video are no more a risk than its own images, and without it an app could not play them at all; `MediaSrc` adds to it. Plan section 3b's base policy already includes it.
- **SPAR105 stays a warning** (choice 2): `StyleNonce()` beside `AllowInlineStyles()` does no harm, since Spar then adds no nonce and the author's inline styles apply, but the nonce is pointless; a warning says so without failing a build that works.

## 2026-11-17, Security defaults' public shape (user decisions, M1b step 5)

Asked at the start of M1b step 5, with options and a recommendation for each; the user took the three recommendations.
- **The browser opt-ins are one group, `spar.Browser(b => …)`:** `AllowDevTools()` (F12 and Inspect), `AllowAcceleratorKeys()`, `AllowPasswordSaving()` (with general autofill), `AllowDownloads()`, `AllowHistoryNavigation()` (mouse back and forward, Alt+Left, swipe), `AllowContextMenu()` (the full default menu; text fields keep Cut, Copy, Paste and Select all without it), `AllowPagePicker()` and `AllowBrowserEnvironmentOverrides()` (both named earlier, decision "M1b's remaining questions without a spike" and plan 3c). Every window shares one browser profile, so these are per app; one call shows every loosened default in one place, like `spar.ContentSecurity`. Each method's documentation explains the risk, when it is reasonable and the safer alternative (plan 3c). Chosen over top-level methods on the builder, which mix with the app's other configuration.
- **Development keeps every Production default except devtools:** a Debug build has F12, Inspect and the reload keys (F5, Ctrl+R) on, so the page can be debugged, and everything else as shipped, so a blocked download or picker shows while developing, not after shipping. Chosen over Debug identical to Production (an opt-in to remember to remove) and over everything open in Debug (a feature that works in Debug and breaks when shipped).
- **The profile folder is configurable with `spar.Folders(f => f.Profile(path))`,** like `Data` and `Logs`. The path must be absolute and local: a relative or network path stops the start with a clear message, since a profile on a share is slow and readable by others. The host still checks the folder actually in use (plan 3c). Chosen over leaving it fixed under the data root.

## 2026-11-17, Security defaults (implementation record; choices for review)

Plan M1b, build step 5 (plan section 3c), with the public shape of decision "Security defaults' public shape". Evidence: `docs/evidence/m1b-security-defaults.md`.
- **Built:** `spar.Browser(b => …)` (`BrowserOptions`, its eight methods each documenting the risk, when it is reasonable and the safer alternative), one start-up line naming the opt-ins in force; on Windows `BrowserDefaults` (settings, `DownloadStarting`, `ContextMenuRequested`, `AcceleratorKeyPressed`), history navigation cancelled in `PageGuard`, `PagePicker` (the two DevTools Protocol calls) and `BrowserEnvironment` (the variables removed, the environment options the host implements); `SparAppId` checked by the build (SPAR130) and carried by the generated catalog (`SparCatalog.AppId`); folders and the process's explicit AppUserModelID per channel; `spar.Folders(f => f.Profile(path))`; the folder in use checked and kept in `BrowserProfile`; `SparFolders.Profile`; the Development-only `SPAR_DEV_INPUT` trigger; `docs/app-model.md` 2, 3.13 and 3.17 (new, `SupportBuild.cs`, 33 blocks) with the stand-ins; `tests/apps/SecurityProbe` with `-p:SecurityProbeOptIns=true`; the production boundary test's DevTools Protocol scan.
- **Numbers** (WebView2 157, NativeAOT unless noted): settings set in 0.04–0.16 ms; the two picker calls 40.2–47.5 ms (median 45.1, nearly all `Page.enable`), overlapping the injected script's 45 ms, so host start to `ui-ready` was 325.0 ms median with them and 329.5 ms without (7 runs each); `--remote-debugging-port` in the variable opened no port without the opt-in and opened one with it, closed 1 s after exit; the profile and Runtime folder variables moved nothing without the opt-in and moved both with it; the Notebook 4,999,680 B (+59,392 B; 5,022,208 B before the size work of choice 15), 320 B under the 5,000,000 B budget; the probe 4,228,608 B.
- **The open question of plan 3c, answered: clearing a variable in-process is enough.** The loader reads the WebView2 variables in the app's process when the environment is created, and the browser inherits the process's block; removed with `SetEnvironmentVariableW` before the loader's first call (the Runtime check), none reached the browser on the published executable.
- **Choices made while building, for review:**
  1. **The opt-ins are logged in one warning line,** "Browser opt-ins in force: AllowDevTools() …", before anything else of the run; each ignored or allowed variable gets its own line, by name, never its value.
  2. **Keys where devtools are on:** `AreBrowserAcceleratorKeysEnabled` is one switch for every key, so a Development build (and a Production one with `AllowDevTools()`) turns it on and sets `IsBrowserAcceleratorKeyEnabled = false` in `AcceleratorKeyPressed` for every key but F12 and Ctrl+Shift+I, J, C, plus F5 and Ctrl+R (with Shift or Ctrl) in Development. So `AllowDevTools()` gives F12 as well as Inspect, as "F12 and Inspect" says, without the other browser keys. The page still receives every key.
  3. **The context menu without `AllowContextMenu()`:** editable fields keep WebView2's `cut`, `copy`, `paste`, `selectAll`; `inspectElement` stays wherever devtools are on; a menu left empty is not shown; in the automated mode no menu is shown at all, so an unattended run puts nothing on the desktop. A Development build logs what it kept for its first 20 menus.
  4. **History:** a `NavigationStarting` of kind back-or-forward is cancelled, which also cancels the page's own `history.back()` to another document; moves within one document (`pushState` entries) raise no navigation and stay, so a router's history keeps working, also on the mouse's buttons. The decision's "mouse back and forward" therefore holds between documents only; suppressing the buttons in the injected script was not built.
  5. **Downloads:** cancelled and handled; the first 20 of a run are logged; a Development build names the address.
  6. **SmartScreen stays off with no override:** no app can allow navigation to other origins yet (the navigation lock has no opt-in, and frames off the origin are cancelled), so the automatic switch and the opposite override of decision "SmartScreen default in WebView2" are not reachable and not built.
  7. **The picker calls** start as soon as the controller exists, chained in one completion handler, and are awaited with the injected script before the first `Navigate`; a failure fails the start as a broken Runtime (exit 5), never a page with the picker open. They are made again after the browser is recreated. **The production boundary test** now lists every DevTools Protocol method name in the image, over 25 domains, and requires exactly `Page.enable` and `Page.setInterceptFileChooserDialog` (matched as a prefix, since a native image stores literals back to back); its control is `Spar.Windows.dll`. The markers gained `DevelopmentInput`, `SPAR_DEV_INPUT` and `remote-debugging-port`; none collided.
  8. **No fallbacks:** every interface used is in Runtime 139 (the newest, `AcceleratorKeyPressedEventArgs2`, came with 116), so a missing one fails the start as too old (exit 4). `ICoreWebView2NavigationStartingEventArgs` is now declared once, flat at `_3`.
  9. **The variables removed are eight, four more than plan 3c names** (a deviation, for the user's review): Microsoft's documentation now names `WEBVIEW2_CHANNEL_SEARCH_KIND` and `WEBVIEW2_RELEASE_CHANNELS` for the channel, and `WEBVIEW2_PIPE_FOR_SCRIPT_DEBUGGER` and `WEBVIEW2_WAIT_FOR_SCRIPT_DEBUGGER` put the page under a script debugger; `WEBVIEW2_RELEASE_CHANNEL_PREFERENCE` stays for older loaders. `COREWEBVIEW2_MAX_INSTANCES` passes.
  10. **SPAR130** for a missing or an invalid `SparAppId`, two messages, from a target that runs first in `SparBuildSteps` (apps only, never in a design-time build), with GTK's rules as a regular expression and the 255-character limit. The 100s are Spar's build-time diagnostics (SPAR100 snapshot, 101–105 page security, 110–112 packing, 120–122 tested app); 130 opens the app-identity group. The generator reads `build_property.SparAppId` into `SparCatalog.AppId`; a catalog without one (no Spar build steps) falls back to the assembly's name.
  11. **The repository's app ids:** `com.example.Notebook` (sample), `com.example.SqlTool` (the documented app), `com.example.tests.SecurityProbe`, `com.example.tests.Harness` and `com.example.tests.WireShapes` (test apps), and `org.example.notes.Tool` for the package test's consumer, the plan's "custom app id", different from its project and assembly names and checked in its published image.
  12. **The AppUserModelID** is set through CsWin32 before the window exists. Windows takes at most 128 characters, fewer than the 255 the decision allows an id, so a longer channel id keeps Windows' default and logs a warning (for review: the build could cap ids at 116, leaving room for `.Development`).
  13. **A profile folder Spar refuses exits with 70,** the code an app already gets when its services cannot be built, rather than a new fixed code: the fixed table describes the machine, and this is the app's own configuration. Before anything else of the start, standard error and `host.log` get the line, then a native message unless automated; `spar.OnStartupProblem` is not called. Refused: a path that is not fully qualified, and a network path (UNC, `\\?\UNC\`, or a drive letter `GetDriveTypeW` reports as remote). A `LOCALAPPDATA` on a mapped network drive now falls back to the known folder as a UNC one did.
  14. **The folder in use** is read with `GetUserDataFolder` right after the environment exists and compared case-insensitively; a moved one is logged as a warning. Both folders are kept in `BrowserProfile`, an internal singleton in the app's services with `Contains(path)`, which M3's file grants are to call. `SparFolders` gained `Profile` (a positional record, so its constructor changed).
  15. **Image size:** the first build put the Notebook at 5,022,208 B. Each COM method the host calls costs about 300–420 B of stub plus 120 B of metadata, so the settings are read back into the log only in Development (a Production build logs the values it set), a download's address is read only in Development, `DriveInfo` and `Environment.SetEnvironmentVariable` became `GetDriveTypeW` and CsWin32's `SetEnvironmentVariable`, the picker's `async` method became one completion handler, and log lines were shortened. 320 B of budget remain.
  16. **`SPAR_DEV_INPUT`,** a Development-only trigger like those of step 3: `click`, `right`, `back`, `forward` at a CSS pixel and `key` steps through the `Input` domain, from the first `ui-ready`. It is how the picker, the context menu and the mouse's back button were checked without touching the desktop.
- **Deviations from the design:** the four extra variables (choice 9); history between documents only (choice 4); mapped network drives refused too (choice 13).
- **Found:** `Input.dispatchKeyEvent` never reaches the browser's accelerators, so no browser key can be checked through the DevTools Protocol; the back button skips documents the user never interacted with; `chrome.webview.hostObjects` exists with host objects disallowed; Git Bash's path rewriting makes the loader refuse a Runtime folder (pitfalls).
- **Incident:** one Production start-up dialog ("can't start", no owner) appeared on the desktop for about 40 ms when a Runtime folder was passed as `E:/…`; the run closed it with `WM_CLOSE` and no input was sent. Later overrides ran on a Development build with the automated mode first.
- **Not verified:** F12, Inspect, the browser keys and the cut-down context menu on a Production executable (real input; Windows Sandbox's user session, after asking); any browser key at all (the finding above); the dialogs of `AllowPagePicker()` and the full menu of `AllowContextMenu()` (they would reach the desktop); password saving in a real form (read back only); the AppUserModelID read back; the channel and script-debugger variables with the opt-in; a profile moved by a policy; Spar's own browser argument when the variable is allowed too; the interception after a renderer crash; the SmartScreen net log (step 9); Windows 10, an older Runtime, Linux.
- **For the user's decision:** the four extra variables (choice 9); exit 70 for a refused profile (choice 13); the 128-character AppUserModelID against the 255-character id (choice 12); the Notebook's budget, now 320 B from its limit; a Windows Sandbox run for the Production input checks.

## 2026-11-17, Security defaults' choices accepted; the Windows size budget raised to 6 MB (user decisions)

Asked about the choices "Security defaults (implementation record; choices for review)" left for the user; the user took each recommendation and accepted the rest of the record.
- **The Notebook's Windows budget is 6 MB,** raised from 5 MB. Step 5 left the Notebook at 4,999,680 B, 320 B under the old budget, after trimming it from 5,022,208 B. The cause: each WebView2 method the host calls costs about 300–420 B of stub plus 120 B of metadata, and the 5 MB came from `spikes/react` (3.68 MB), which had far less host code. Steps 6–8 and M3's native services add more calls, so 5 MB would have needed a trimming pass at every step. M4 replaces the budget with a measured one. Chosen over keeping 5 MB and slimming the image now.
- **An app id has at most 116 characters** (SPAR130), down from GTK's 255: with `.Development` it then fits the 128 characters Windows takes for an AppUserModelID, so every app gets one identity everywhere and none silently keeps Windows' default. The host's check of the length stays as a guard for a catalog built without Spar's build steps. Chosen over allowing 255 with a warning at start (choice 12).
- **The eight `WEBVIEW2_*` variables are ignored, four more than plan 3c named** (choice 9): the channel variables Microsoft documents now and the two that attach a script debugger to the page carry the same risk as the plan's four. `AllowBrowserEnvironmentOverrides()` lets all eight through.
- **A refused profile folder exits with 70** (choice 13), the code of an app whose own configuration cannot start it; the fixed codes 3–10 describe the machine and the session.
- **The Production input checks** (F12, Inspect, the browser keys and the cut-down context menu on a Production executable) wait for step 8's Windows Sandbox runner, which runs real input in the user session there; until then they stay in the record's "not verified".

## 2026-11-17, Host details (implementation record; choices for review)

Plan M1b, build step 6 (research review section 4): focus, the controller's visibility, the window's position, and the visible-window teardown leak rechecked. Evidence: `docs/evidence/m1b-host-details.md`, measured against a baseline published from the host before the step (d94c48a).
- **Built:** `ViewWindowState` (new, `src/Spar.Windows`), one per `BrowserView`, attached to each controller and detached on release; `WM_WINDOWPOSCHANGED` and `WM_SETFOCUS` in the window procedure, named for the stall probe; `MoveFocusRequested` subscribed; `ICoreWebView2Controller` slots 12, 13, 14 and 23 and `ICoreWebView2MoveFocusRequestedEventArgs` declared and required by the slot-order test; the `focus` and `visibility` probes in `tests/apps/SecurityProbe`; the leak probe and the Sandbox driver in the evidence.
- **Numbers** (Runtime 157, NativeAOT): with real input in Windows Sandbox, the baseline's page got no key after a title-bar activation, Tab, Alt+Tab back, a restore or the hung-page dialog, until a click into it; the current host's got every one, Tab past the last field came back to the first and Shift+Tab before the first went to the last, and focus came back after the hung-page dialog both when the host took it down and after Enter (Wait). A `<select>` opened from the keyboard after a 150, 100 px drag: the baseline's list at the old place (268,215), the current at 418,315. On the desktop: a hidden window's page went from `visible` with 60 frames and 20 timer callbacks a second to `hidden` with 0 and 1, 37–38 ms after `SW_HIDE`; minimized was already `hidden` (the browser's own, 32–47 ms); a hidden or minimized page answered a close request's ping within 1 ms. Leak: the network service moved −10 to +5 handles in 30 and 100 cycles, plain or mitigated, offscreen on the desktop and on the screen in the Sandbox; the GPU process grew about one handle per window (+102, +104 in 100). The Notebook 5,010,944 B (+11,264 B), within its 6 MB budget.
- **Choices made while building, for review:**
  1. **One class, `ViewWindowState`, owned by the view:** it holds the current controller, so a browser recreated after a crash gets the window's state again on attach; `WindowsApp` only forwards two messages. A file of its own keeps `BrowserView` (724 lines) and `WindowsApp` (671) under 800.
  2. **No `WM_ACTIVATE` handler.** Windows sends `WM_ACTIVATE` first, and `DefWindowProc` then gives the window focus unless it is minimized, which arrives as `WM_SETFOCUS`; forwarding that one message covers activation, Alt+Tab and a closed dialog. A window activated while minimized has no focus, so the restore (seen in `WM_WINDOWPOSCHANGED`) moves it; a controller created after the window got focus (at start-up) takes it on attach. Those two paths move focus only when the window is the active window, enabled, and its focus is on itself or nowhere, so a dialog that owns the focus (the hung-page dialog disables its owner, as the modal close-guard dialog does) is never robbed. `WM_SETFOCUS` itself is skipped while the window is disabled or minimized.
  3. **`MoveFocusRequested` is always handled:** `Handled = true`, then `MoveFocus` with the same reason posted to the UI thread (not called inside the event), so Tab cycles within the page. WebView2's default looks for the next tab-stop child window first; the host has none now, but the page-drawn title bar's overlay child will exist, and handling it keeps the cycle independent of that. The default was not measured.
  4. **`IsVisible` is "shown and not minimized",** set from `WM_WINDOWPOSCHANGED` only when it changes (a new controller is set only when it is false, its default being true). `WM_WINDOWPOSCHANGED` was chosen over `WM_SHOWWINDOW`, `WM_SIZE` and `WM_MOVE` because it comes for every show, hide, minimize, restore and move, also those made by `SetWindowPos`, which sends no `WM_SHOWWINDOW`; the handler returns null so `DefWindowProc` still sends `WM_SIZE` and `WM_MOVE`, after the controller is shown. **The tray's hook:** M3's close to tray hides the window, which this follows with no new code; a tray state that kept the window shown would add its condition to `WindowShown`. The heartbeat is unchanged: it already skipped hidden, minimized and controller-hidden windows, and now the page is `hidden` in all three; the stall probe sees two more named messages.
  5. **Every move is notified,** the minimize's and restore's included: 22 calls for one drag in the Sandbox, each a call into the browser process from the UI thread, not measured for cost.
  6. **The WebView keeps its last size while minimized** (`Resize` skips a minimized window), where it was given 0×0 before. A small addition to the plan's list; the page saw no `resize` either way.
  7. **The teardown mitigation is not adopted,** since the leak it was for did not reproduce (numbers above). The teardown keeps its order.
  8. **Logging:** one Production line at teardown, "Window: N focus moves, N Tab past the page, N visibility changes, N moves.", and a line only if `MoveFocus` fails; nothing per event. It was what showed the counts in the Sandbox's `host.log`.
  9. **The probes went into `SecurityProbe`,** whose project comment now names step 6, rather than a new test app (a contract, a snapshot and an npm project more). Its name now undersells it; a rename to a general Windows host probe is for the user.
  10. **The leak probe compiles Spar's own `Interop/` declarations,** copied in by its script from a copy outside the repository, so it calls WebView2 exactly as the host does; it is NativeAOT so the same file runs in the Sandbox guest, which has no .NET.
- **Deviation from the plan:** none in what was built; choice 6 adds one behaviour.
- **Found** (pitfalls): the keyboard never reaches the page without the forwarding; a hidden window's page renders at full rate; a hidden or minimized page still answers web messages; keyboard-opened popups stay at the old place without the notification; the teardown leak does not reproduce and the GPU process gains a handle per window; **closing the last controller ends the browser, and a controller created about a second later waited 5.7 s for a new one** (2 of 2, against 0.28 s), which step 8's window tests on one shared environment will meet unless another controller stays alive; an app started under `wsb exec` got the foreground in 1 of 7 starts, and an Alt+F4 that reached the guest's desktop opened its shut-down dialog.
- **Incidents:** none on the desktop (no dialog, no activation; every run in the automated mode). In the guest, one early driver sent Alt+F4 to the desktop (the dialog stayed open, unconfirmed, until the next run closed it) and an Edge window appeared, probably from keys typed while the desktop had the foreground (not established); the driver now activates the app by its title bar, sends Alt+F4 only to it, and clears both before a run.
- **Not verified:** IME candidate windows; focus with the page-drawn title bar's overlay; DPI other than 96; a real desktop's foreground rules (a row added to the plan's table of checks that need the user); the close guard's modal dialog and focus after it; a restore while the hung-page dialog is open; a restore from the taskbar button itself and a start minimized from a shortcut; throttling after more than 8 s hidden; Windows 10, an older Runtime, Linux.
- **For the user's decision:** renaming `SecurityProbe` (choice 9); whether step 8's shared test environment keeps a controller alive between tests, given the 5.7 s restart and the GPU process

## 2026-11-17, Logging's public shape (user decisions, M1b step 7)

Asked at the start of M1b step 7, with options and a recommendation for each; the user took the four recommendations.
- **One log file, on by default in every app.** Spar adds a file logger to the app's services, so the app's `ILogger` lines and Spar's own go to one file, `app.log` in the logs folder, which replaces `host.log`; the start-up lines written before the services exist go to it through the same writer. The app's own providers (console, OpenTelemetry) still work beside it. Support asks for one file. Chosen over two files (`host.log` for Spar and `app.log` for the app, to line up by time) and over an opt-in, which would leave a shipped app without one.
- **The level: a default in C#, overridden by a file in the app's per-user folder.** `spar.Logging(l => l.Level(LogLevel.Information).Level("SqlTool.Copy", LogLevel.Debug))` sets the default and per-category levels; a `logging.json` in the app's per-user folder overrides them at start, writable without administrator rights and kept across updates, read with Spar's own JSON reader (no configuration package, no reflection, no size). Chosen over `appsettings.json` beside the executable (needs administrator rights under Program Files, and the configuration packages add size) and over an environment variable (awkward for a user who starts the app from a shortcut).
- **Plain text lines:** one line per entry, UTC time, level, category and message, with an exception's stack indented below it, readable in any editor. Chosen over JSON lines, better for tools but harder to read.
- **Page errors come in step 7:** console errors, uncaught exceptions and unhandled rejections reach the same log through the host-injected script of step 4, grouped and batched with flood limits like the violation reports, with the page's source location. Rotation, crash records and `diagnostics.collect()` stay in M3 (plan M1b step 7).

## 2026-11-18, Logging (implementation record; choices for review)

Plan M1b, build step 7, with the public shape of decision "Logging's public shape". Evidence: `docs/evidence/m1b-logging.md`.
- **Built:** `app.log` in the logs folder for every app, replacing `host.log`: the writer (`Spar.Logging.LogFile`, moved from the Windows host to the runtime), Spar's file provider and its own small `ILoggerFactory` with `ILogger<T>`, `spar.Logging(l => l.Level(…).Level(category, …))` (`LoggingOptions`), `logging.json` in the data root, the plain lines, the page's errors from the host-injected script (the `errors` message, `PageErrorLog`), the test host's file, and `UnsupportedWindowsHost` through the same writer; `docs/app-model.md` 2 and 3.13 with stand-ins in `tests/AppModel/Spar.Stubs` (`spar.Logging` is real, so `Real` compiles it through Spar); the Notebook's store logs the files it skips and its page writes unhandled failures with `console.error`; SecurityProbe's page-error modes; the skills `spar-webview2-hosting`, `spar-page-security` and `spar-testing`.
- **Numbers:** the Notebook 5,103,616 B (+92,672 B from 5,010,944 B; 0 warnings); Microsoft.Extensions.Logging's `AddLogging()` alone measured +90,624 B, and with one filter rule +96,256 B; a line costs the caller 0.26 µs (CoreCLR Debug, 10,000 lines); 100,000 page errors of each kind (console, uncaught, rejection; one text or all different) kept the page busy 0.17–0.59 s in all, one bridge message per flood, Spar's reporting 3–18 µs per console or uncaught error while it counts (2–22 ms for a document's 1,000); no stall, and a heartbeat tick of at most 1.20 ms on the UI thread (Development); the Notebook's `app.log` from one Production run holds the host's, the app's, the bridge's and the page's lines.
- **Choices made while building, for review:**
  1. **Spar's own logger factory, not Microsoft.Extensions.Logging's:** that factory cost the Notebook 96,256 B with one rule; Spar's (`SparLoggerFactory`: a fan-out to every `ILoggerProvider` in the services, no scopes) about 7 KB. It is registered with `TryAdd` after the app's services, so an app that calls `AddLogging` (console, OpenTelemetry) keeps that factory, which takes Spar's provider from the services; `ClearProviders` in the app's call does not remove it, since Spar adds it later. No package was added.
  2. **The levels apply to the file only:** the provider filters itself, so the app's other providers keep their own filters. Consequence: under an app's own `AddLogging`, its factory's minimum (Information unless it sets one) applies before the file, so the file's Debug lines need `SetMinimumLevel` in the app; the provider's alias `SparFile` lets such an app write a provider rule. The alternative, Spar adding the rule itself, needs the package (96 KB).
  3. **Defaults:** Information in Production, Debug in Development and Test builds. `Level(level)` sets the default for every build.
  4. **`logging.json` in the data root** (`SparFolders.DataRoot`, `%LOCALAPPDATA%\<app id>`), not the logs folder: it is a setting beside the app's other per-user state (`instance.lock`, `color-scheme`), it stays put when the app moves its logs (to the executable's folder, a shared place), and M3's rotation and the support zip, which own the logs folder, never sweep it.
  5. **The file's shape and precedence:** `{"level":"Debug","categories":{"SqlTool.Copy":"Trace"}}`, both properties optional, level names as `LogLevel` names them, in any case. Its level replaces the default; its categories are added after the C# ones, so for the same category the file wins, and a longer category of either still beats a shorter one. Strict: an unknown property, a property twice, a category twice (in any case) or with two `*`, more than 100 categories, a comment, trailing text, a wrong type or a file over 64 KiB refuse the whole file, with one `Spar.Host` warning naming the file, the reason and the shape; a byte-order mark is skipped. Read once at start: a change needs a restart.
  6. **Category matching as Microsoft.Extensions.Logging's filters:** a case-insensitive prefix not bounded by a dot (`SqlTool.Copy` also covers `SqlTool.CopyJobs`), at most one `*`, the longest rule winning, the later of two equal ones.
  7. **The lines:** `2026-11-17T22:41:07.318Z INFO  Spar.Host  message`: milliseconds, the level in five characters and a space, the category, two spaces, the message. A message's further lines, and an exception's `ToString()` (type, message, stack, inner exceptions), follow **indented by four spaces**, chosen over escaping them: stacks and JSON stay readable, and only an entry's first line can start in the first column. CR, LF, CRLF, NEL, LS and PS all count as breaks; other control characters are written as `\u` escapes; a category stays on one line; a message over 16,384 characters is cut with a count. **Scopes and structured values are not written** (the factory passes no scopes; the values are in the message). CRLF on Windows, LF elsewhere. The host's lines keep their run id in brackets; other categories have none, since two processes share the file only around a relaunch.
  8. **The queue:** 10,000 entries; when it is full the newest are dropped and counted, and the next batch writes one `WARN  Spar.Log` line with the count. The caller formats the message and queues an entry (the time taken at the call); the file's task formats the lines and writes batches of up to 512 entries in one append. Closing waits 2 s at most: at process exit after the services, in "Exit now" before `TerminateProcess` (the same `HostLog.Close` call as step 3), and asynchronously when a test host's services are disposed.
  9. **One writer per process:** the Windows host reads the levels, opens the file and writes its first line before anything else, and hands the same `LogFile` to the services (not owned), so the host's early lines, start-up failures and the app's loggers share one queue and one handle. `HostLog.Logger` is one object for the run, so loggers taken before the services exist keep working: standard error at Information and above as before, then the file directly, then the app's factory once it exists, which writes the file once. Other categories never reach standard error unless the app adds a console provider. Old `host.log` files are left where they are.
  10. **Test runs:** the test host registers the same provider with a file the services own, `app.log` in the test's own logs folder, at the levels of `spar.Logging` and a `logging.json` in the test's data root, closed with the services and deleted with the data root unless `KeepData`; the in-memory per-app log (`app.Log`) is unchanged. The console harness gets the factory and no file.
  11. **`console.error` is wrapped; the two events are listened to.** No event reports a console call, and Production refuses the DevTools Protocol's console events (only the two picker calls are allowed). The wrapper is a method named `error` that calls the page's original with the same `this` and arguments and returns its result; it is put back at the document's bound unless the page wrapped it meanwhile (a later page wrapper is left in place and still reaches ours). `error` (only events whose target is the window: a capturing listener also gets resource failures) and `unhandledrejection` are listened to on the window, capturing, and never cancelled. Nothing is defined on `window`. Downside, for review: DevTools in Development probably links a console error to the wrapper's line (a row added to "Checks that need the user at the PC"); a Production build has no DevTools.
  12. **What a report holds:** kind, text, place and the first stack. Values are described without running page `toString` or `toJSON` (strings as they are, anything with a string `message` as its name and message, other objects by their tag); every read is guarded by `try` and a re-entrancy flag, so a getter that throws or logs reports nothing. The place is the event's file, line and column for an uncaught exception, otherwise the first `http(s)` frame of the stack; a console error's caller comes from `Error.captureStackTrace(holder, wrapper)` (V8), its stack from the first error among the arguments, else the caller's stack. Up to 10 arguments; texts cut to 512 (message), 256 (source) and 2,048 (stack) characters on both sides.
  13. **The limits** reuse the violation listener's machinery (one `reporter` in the script): a batch at most every second, the first after 100 ms, 100 groups per document, and a stop after 1,000 errors per document. The floods set them: Spar's cost is 3–18 µs per error while it reports, so the bound keeps it at about 2–22 ms per document, while a page raising 100,000 errors spends 0.17–0.59 s on them itself.
  14. **The host's side** (`PageErrorLog`, category `Spar.Page`): the first of a group at Error as `<kind> at <place>: <message>` with the stack on the following lines; later ones in an Information summary at most every 10 s naming the 10 largest; 100 groups per document with one "more than 100" line; one "stopped reporting" warning; totals at exit; a queue of 256 batches that drops and counts; batches parsed as untrusted (unknown kinds dropped, numbers clamped). It mirrors `CspViolationLog` rather than sharing a base class with it, which would save an estimated 10 KB of the 21 KB it costs.
  15. **The protocol:** `errors`, page to host, a header with no fields and the batch as payload, like `csp`: `BridgeMessageType.Errors`, `BridgeSession.PageErrors`, the SDK's type list and a shared vector. Hosts without the injected script never receive it.
  16. **Image size work:** records replaced by classes (their `Equals`, `GetHashCode` and `PrintMembers` cost 2–5 KB each), the fan-out logger without a `try`, a failure list or scope forwarding, and the host logger's formatting moved to a non-generic method, since each logger type's `Log<TState>` is compiled again for every message state (pitfall); the first version was 5,131,264 B.
  17. **The Notebook** logs a warning for a page file it skips, and its page also writes a failure no handler took with `console.error`, so a failure only the page saw reaches the log a user sends; its behaviour is otherwise unchanged, and its journeys still see a clean console.
- **Not verified:** Linux (the writer appends at the file's length there, which two processes can race: M1c); WebKitGTK's console and `captureStackTrace`; an app with Microsoft.Extensions.Logging's own factory and the `SparFile` alias; DevTools' display of a wrapped `console.error`; "Exit now" after this step (it needs a prompt on the desktop); a full disk or an unwritable logs folder; the dropped-entries line from a real app and a `logging.json` on a real run (both unit-tested); the Windows Sandbox focus runner's change to read `app.log`.
- **For the user's decision:** whether the file needs an opt-out (today `"level":"None"` silences it, but the file is still created); whether to wrap `console.error` in Development too, given DevTools' link (choice 11), or only in Production; whether `PageErrorLog` and `CspViolationLog` should share a base class (choice 14); whether Spar should take Microsoft.Extensions.Logging (96 KB) so its levels hold under an app's own factory (choice 2).

## 2026-11-18, Logging's follow-up choices (user decisions)

Asked about four points "Logging (implementation record; choices for review)" left for the user.
- **`spar.Logging(l => l.WithoutFile())`** removes Spar's file provider, for an app that sends its logs only to its own sink; support then has no local file. Chosen over no opt-out with level `None` leaving no file.
- **Debug builds read the page's console from the devtools events.** A Development build, which has devtools on, takes `console.error` from the in-process DevTools Protocol's console events, so the devtools source links point at the page's code; Production and Test keep the wrapper from the injected script. The code is Development-only and trimmed from Production. Chosen over the wrapper everywhere (links pointing at the wrapper in Debug) and over no console errors in a Debug log.
- **The logger factory is Microsoft.Extensions.Logging's.** The user judged that about 90 KB (under 2 % of the 5.1 MB Notebook, within the 6 MB budget) is not worth a custom factory: with Microsoft's, the `spar.Logging` and `logging.json` levels become its filter rules and hold whatever providers the app adds, an app's own `AddLogging` included, which Spar's factory could not guarantee; filtering behaves as .NET developers expect, and Spar keeps less code. Spar keeps its file provider, line format, writer and `logging.json` reader; there is no configuration binding, so nothing reflective. The start-up cost is measured before and after.
- **`PageErrorLog` and `CspViolationLog` stay separate** for now (the estimated 10 KB saving is not needed under the 6 MB budget).

## 2026-11-18, Logging's follow-up (implementation record; choices for review)

The four points of decision "Logging's follow-up choices (user decisions)", with the lead's change of plan for the factory: the user judged about 80 KB not worth a factory of Spar's own, so Spar now always uses Microsoft.Extensions.Logging's (variant b). Evidence: `docs/evidence/m1b-logging.md`, section "Follow-up".
- **Built:** the `Microsoft.Extensions.Logging` package (11.0.0, no Configuration package, no binding) in `Spar`; `SparLoggerFactory` and Spar's own rule matching removed, `LogLevels.Apply` writing the levels into `LoggerFilterOptions`; `spar.Logging(l => l.WithoutFile())`; `DevelopmentConsole` reading `Runtime.consoleAPICalled` in Development builds, with three new interface declarations (the event receiver, its handler and its arguments; slot 42 of `ICoreWebView2` declared); the injected script's `__SPAR_WRAP_CONSOLE__`; the production boundary's `DevelopmentConsole` marker and its control (`Runtime.consoleAPICalled`, `Runtime.enable` in the host's assembly); SecurityProbe's `SecurityProbeAddLogging` and `SecurityProbeWithoutFile` build properties and a Debug line; `docs/app-model.md` 3.13 and its stand-ins; the skills.
- **Numbers:** the Notebook 5,182,464 B (5,103,616 B with Spar's own factory, +78,848 B; 5,010,944 B before logging); SecurityProbe 4,409,856 B, 4,411,392 B with its own `AddLogging` (+1,536 B), 4,410,368 B without the file; start-up to `ui-ready` 388.5–418.8 ms before and 376.5–414.8 ms after (medians of 7, three and two rounds), the services built 0.6 ms later; a line costs the caller 0.27 µs through the factory (CoreCLR Debug). In Development, `Runtime.enable` made 99,000 console errors cost the page 2.6–2.8 s (0.44–0.47 s before) and exceptions 2.6–2.7 s (0.42–0.58 s); no stall.
- **Choices made while building, for review:**
  1. **The levels apply to every provider:** they are the app's levels, so the default is the factory's minimum and each category a rule without a provider. They are configured after the app's own `AddLogging`, so they replace its `SetMinimumLevel` and win over its equal rules; a rule the app adds for one provider (by type, or the file's alias `SparFile`) still applies to that provider, since a provider rule beats a general one. Chosen over the file only, which would leave the app's console or OpenTelemetry at another level than the file.
  2. **The host's lines before the services exist** go through a `LoggerFactory` of their own with the file's provider and the same rules, so Spar keeps no rule matching; it costs the start nothing measurable.
  3. **Matching is Microsoft.Extensions.Logging's,** one difference from step 7's own: a wildcard's prefix and suffix may overlap (`SqlTool.*.Jobs` matches `SqlTool.Jobs`; pitfall). Spar still refuses two `*` in `Program.cs` and `logging.json`, where Microsoft.Extensions.Logging would throw at the first logger.
  4. **`WithoutFile()`** adds no provider and opens no file at all: the host's early lines and start-up failures go to standard error only, the start-up message's details name no log file, `UnsupportedWindowsHost` writes no file, and a test run writes none. Chosen over keeping a file for start-up failures alone, since an app that opts out told its users there is no file. The risk is in the method's documentation and in `docs/app-model.md` 3.13. `Level(LogLevel.None)` remains the way to keep an empty file.
  5. **The Development console reads `Runtime.consoleAPICalled`,** not `Log.entryAdded`: the console's calls reach only the Runtime domain, and the event carries the call's own stack, whose first frame is the page's caller. It applies to Development builds only (`PageScript.ConsoleFromDevTools`: the Development switch on, the test host's off), so Test builds keep the wrapper like Production; the script's `error` and `unhandledrejection` listeners stay in every build, and an exception is never read from the protocol, so nothing is reported twice. `Runtime.enable` runs on each WebView before its first navigation, awaited with the picker calls.
  6. **On the UI thread** the console handler only reads the event's JSON, looks for `"type":"error"` in it (the keys come sorted, so the type is last) and queues a copy; a task of its own parses it, groups by text and place, applies the script's bounds (100 groups and 1,000 errors per document; past the bound the UI thread skips that document's events), and posts batches of the script's shape to `PageErrorLog` at most once a second. Its handler is its own COM class, so `WebView2EventHandler`, which Production carries, did not grow. Arguments are described as the console shows them (a string as it is, other values by their description, an error by its first line with its description as the stack).
  7. **The "stopped" line** now reads "Spar stopped reporting them", since the host stops in Development.
  8. **The plan's row** for checking DevTools' link of a wrapped `console.error` is removed: Development no longer wraps it.
- **Measured and not adopted:** (a), Spar's own factory, 5,103,616 B, with its levels for the file only and the app's factory filtering first. (c), the hybrid, was not measured, as the lead asked.
- **Not verified:** the devtools window's display; `Runtime.enable` across a navigation (one document per run); the alias rule on a NativeAOT image (unit-tested on CoreCLR); WebKitGTK; the console's memory in a long Development session (the app process's working set was 92 MB after 100,000 console calls, 50 MB with the wrapper; probably the events' JSON in the host, and the renderer may keep the calls' objects until the document goes).

## 2026-11-18, Configuration stays the app's choice (user decision)

Asked after the spike [`spikes/configuration-cost/`](evidence/configuration-cost/RESULTS.md), which measured what Microsoft.Extensions.Configuration costs a Production NativeAOT Spar app (SecurityProbe-shaped, .NET SDK 11.0.100, five variants, 96 timed runs):
- **Size:** a JSON file applied to logging (`AddJsonFile` and `AddConfiguration(config.GetSection("Logging"))`) +351,232 B; with environment variables, `IConfiguration` in DI and one options class bound by the binding generator +393,728 B; `reloadOnChange: true` +0 B over that; the file read by the app and handed over with `AddJsonStream` +82,432 B. Most of the standard route is the file machinery `AddJsonFile` always brings (file provider, watcher, globbing, their generic instantiations), not configuration itself. On the Notebook (5,182,464 B) the standard route would make about 5.53 MB, within the 6 MB budget.
- **System.Text.Json:** only `JsonDocument` over the `Utf8JsonReader` the image already has: +12,787 B in every variant, the floor of any JSON configuration source. No `JsonSerializer`, `JsonTypeInfo` or anything of `System.Text.Json.Serialization` in any image, so decision "Wire serialization: generated code, confirmed" is untouched.
- **Warnings and reflection:** none; every variant published with zero trim, AOT or ILC warnings, nothing suppressed. With the binding generator no reflection-based binding is in the image (Logging.Configuration carries its own generated binder, 1,363 B).
- **Start-up and memory:** host start to `ui-ready` unchanged (medians 344–356 ms, within the spread); building the configuration 0.6 ms before the host starts; working set +0.3 to +0.6 MB; 11 threads in every variant, the file watcher included, so `reloadOnChange` is free in size and threads (+88 KiB of working set).
- **Three problems found:** (1) the standard `"Default"` is a rule for every category, and Microsoft.Extensions.Logging uses a matching rule before the minimum level, so it beat Spar's levels, written as the minimum, and `logging.json`'s `"level"` did nothing for such an app; (2) the same `"Default"` silenced Spar's own `Spar.Host` lines in `app.log` from "services built" to "exit 0", undocumented; (3) an app building its configuration in `Program.cs` had to repeat Spar's folder rule (`%LOCALAPPDATA%\<app id>`, `.Development` in Development builds), since Spar gave the folders only as a service.

The user's decision:
- **Spar keeps its own `logging.json` reader and takes no configuration package.** An app that wants configuration adds the packages itself: they are NativeAOT-clean. Decision "Logging's public shape" stands; its size argument holds (351 KB for the standard route against about 4 KB for Spar's reader).
- **Fix 1: `logging.json`'s `"level"` always wins** over the app's own configuration, whatever the order of registration: the file's level becomes a rule for every category added after everything the app configured, plus the minimum; its categories stay rules added last. The C# default of `spar.Logging` stays as it is: the app's own configuration may override it, which is the app's choice and is documented.
- **Fix 2: document** in the user-facing logging documentation that an app's own logging configuration (a `"Default"` rule, `AddFilter`) overrides `spar.Logging`'s levels, Spar's own `Spar.*` lines in `app.log` included, and how to keep them (a `"Spar": "Information"` category).
- **Fix 3: give apps Spar's folders before their services are built,** so an app's own configuration does not repeat Spar's folder rule. Recommended shape, for the user's review once built: `SparFolders SparBuilder.ResolveFolders()`, exactly the record the services later get (the test host's root under the test host), after which `spar.Folders(…)` throws.

## 2026-11-18, Configuration stays the app's choice (implementation record; choices for review)

The three fixes of decision "Configuration stays the app's choice (user decision)". Evidence: `docs/evidence/m1b-logging.md`, section "Configuration"; the spike's [`RESULTS.md`](evidence/configuration-cost/RESULTS.md).
- **Built:** `LogLevels` keeps the file's level and categories apart from the app's: `ApplyApp` (the default as the minimum, `spar.Logging`'s categories as rules) is a `Configure<LoggerFilterOptions>` as before, and `ApplyFile` (the file's level as the minimum and as a rule for every provider and category, then its categories) a `PostConfigure<LoggerFilterOptions>`, registered only when the file sets something; the host's early logger applies both. `SparBuilder.ResolveFolders()`, with `SparApplication.Create` giving the builder the app id and, under the test host, the session's data root; `FolderOptions` frozen once resolved; the Windows host, `UnsupportedWindowsHost` and the test host take their folders from it, and the test host refuses folders that are not the test's root. The documentation: `docs/app-model.md` section 2 (`ResolveFolders` in the SQL tool's `Program.cs`, its `HangLog` given the logs folder) and 3.13 (the app's own rules override `spar.Logging`, Spar's lines included, and how to keep them; `logging.json` over the app's configuration; `ResolveFolders`; the app's own configuration), with the stand-ins (`SparFolders`, `ResolveFolders`); `LoggingOptions.Level` and `SparBuilder.Logging`; `docs/design/hosts.md`; plan M1b step 7; a pitfall; the skills `spar-testing` and `spar-webview2-hosting`. SecurityProbe's `-p:SecurityProbeAppConfiguration=true` (Microsoft.Extensions.Configuration.Json and Logging.Configuration in the probe only) and a `folders` page mode; `scenarios.sh configuration`. The root `Directory.Packages.props` lists Microsoft.Extensions.Configuration, .Configuration.Json and .Logging.Configuration 11.0.0 for the probe and Spar.Tests; Spar, the Notebook and the packages reference none.
- **Numbers:** the Notebook 5,184,512 B (+2,048 B from 5,182,464 B; 0 warnings); SecurityProbe 4,411,904 B (+2,048 B), 4,766,208 B with its own JSON configuration (+354,304 B; the spike measured +351,232 B). A real Production NativeAOT run of the configured probe with `{"Logging":{"LogLevel":{"Default":"Warning",…}}}`: without `logging.json`, `app.log` keeps 4 `Spar.Host` lines (the host's early ones); with `{"level":"Information"}`, 40, `ui-ready` and `exit 0` included. The `SparFolders` service equalled what `Program.cs` resolved, with the default folders and with `spar.Folders(f => f.Data(…))` before the call, whose folder the app's `appsettings.json` was read from.
- **Choices made while building, for review:**
  1. **The file's level does not beat the app's category rules.** It is a rule without a category, and Microsoft.Extensions.Logging prefers the longer category before the later rule, so an app's `"SqlTool.Copy": "Warning"` (in its configuration, an `AddFilter` or `spar.Logging`) keeps its level when the file says `"level": "Debug"`; the file's own `"categories"` are added last and win for the same category, so support raises one by naming it (`{"categories":{"SqlTool.Copy":"Debug"}}`). Chosen, as the lead recommended, because a category rule is the app's deliberate, more specific choice and the file can still override it by name. The alternative, the file's level beating every rule, would need Spar to remove or rewrite the app's category rules, or a provider-specific rule per provider, and would also silence a category the app deliberately raised. Rules for one provider (the app's `Logging:Console:LogLevel:Default`, or the file's alias `SparFile`) still beat the file's level for that provider, as they beat `spar.Logging`.
  2. **A post-configuration, not a later `Configure`:** `PostConfigure` runs after every `Configure` whatever the order of registration, and again when an app's configuration reloads (`AddConfiguration` with `reloadOnChange`), so the order holds without Spar registering last. An app's own `PostConfigure<LoggerFilterOptions>` runs before Spar's, since the app registers its services before Spar adds its own; only a test's service overrides come after Spar's. With a plain `Configure`, the cases where the app registers after Spar failed.
  3. **`ResolveFolders()` as recommended:** a method on `SparBuilder` returning the `SparFolders` record the services get (the same instance), cached, after which `spar.Folders(…)` throws `InvalidOperationException` naming the call, and so do the methods of a `FolderOptions` the app kept from an earlier `Folders` lambda. It needs no host: the builder gets the app id from the catalog and, under the test host, the session's data root when `Create` makes it, and the hosts take their folders from the builder, so the value is the services' by construction (a unit test compares them under the test host and in a desktop run's shape; the real run compares them on a Production executable). Alternative considered and not built: a `spar.Folders(f => …)` overload or an `OnFoldersResolved` callback handing the folders to the app's configuration, which avoids the freeze but splits `Program.cs` into a callback; and a static `SparFolders.ForApp(appId)` for apps, which cannot know the test host's root or `.Development`.
  4. **`SparApp.AppId` reads the builder's,** which `Create` sets from the catalog as before, so there is one source; a builder made in a unit test has `"Spar"` unless given one.
  5. **The console harness** (`tests/apps/Spar.Harness`) keeps its own folders under its executable (`SparFolders.Under`), as a test executable that writes nothing; its `Program.cs` does not call `ResolveFolders`, which there would give the per-user folders.
- **Not verified:** Linux; a Development build of the configured probe; `reloadOnChange` with the file's level on a real run (unit-tested with an in-memory configuration's reload); an app's own `PostConfigure`.
- **Tests:** Spar.Tests 477 (475 passed, 2 skipped as before; 11 new in `AppConfigurationTests`), its NativeAOT lane 473 (471 passed, 2 skipped; publish without warnings, the configuration packages included); Spar.Generator.Tests 94, AppModel.Tests 10, WireShapes.Tests 79, Notebook.Tests 12, Notebook.EndToEndTests 7, Spar.BuildTests 6, Spar.PackageTests 11, all passed; `eng/check-size.cs` 0 errors, 0 warnings. The TypeScript SDK did not change (vitest not run).

## 2026-11-18, Step 8's shape: window tests driven by Spar's own API (user decisions; spikes)

Asked at the start of M1b step 8, with options and a recommendation for each. Before answering, the user asked for three spikes: [`spikes/window-automation`](evidence/window-automation/RESULTS.md) (Microsoft's browser automation library attached to WebView2 through a remote debugging port, against Spar's own API over in-process DevTools calls, the same 28-step journey on a React + Radix page), [`spikes/window-automation-linux`](evidence/window-automation-linux/RESULTS.md) (the same question on WebKitGTK 6.0 in WSL), and [`spikes/webview-restart-wait`](evidence/webview-restart-wait/RESULTS.md) (the 5.7 s wait of pitfall "The last controller's close ends the browser"). The user took the recommendation each time.
- **1. Three slices.** 8a: the `Window` test surface on Windows with Spar's own API, and the required host tests of section 5 (with the OS session end and a long install path). 8b: the black-box level on the Production executable, with a UI Automation smoke test. 8c: Spar's Windows Sandbox runner, and the Production input checks that wait for it.
- **2. Window tests use Spar's own API, on both hosts; browser tests keep Microsoft's library.** The API takes the library's method names (`GetByRole`, `GetByLabel`, `GetByText`, `ClickAsync`, `FillAsync`, `Expect(…).ToBeVisibleAsync()`, …) so a journey reads alike on both levels; it stays a separate API.
  - **Shared core:** an accessible-name and actionability engine in page script, run in an isolated world (503 lines in the spike; 39 of 39 roles and names equal to Chromium's own tree). Trusted input through DevTools `Input.*` on Windows and XTEST on Linux (`GDK_BACKEND=x11`, Xvfb).
  - **Numbers, NativeAOT, p50:** Windows per test 1,564 ms with `Accessibility.queryAXTree`, 1,497 ms with the shared engine (role lookup 13.0 → 0.9 ms, an assertion that holds 17.8 → 1.2 ms); Linux per test 1,270 ms, role lookup 0.3–0.4 ms. The library attached to WebView2: 1,799 ms per test, 0.6–0.8 s and a 128–171 MB driver process per test process. 0 failures in every run (Windows 60 plus 30 in parallel per option; Linux 140 trusted journeys).
  - **Why not the library on WebView2:** no NativeAOT; an unauthenticated port any local process can attach to (shown: a separate process ran script in the page), with its port file left behind; every controller's page in one default context (the cookie list came back empty, traces mixed pages); no route interception or video; `page.CloseAsync` hung (once 193 s); a new page opened a real window on the desktop.
  - **Why not the library on Linux: not possible.** WebKitGTK's inspector servers speak WebKit's protocol, not CDP, and `ConnectOverCDPAsync` is Chromium-only; the library's own WebKit is a separate 293 MB patched build that cannot attach to an app's view (and needed root-installed packages); WebKitWebDriver is packaged for 4.1 only and, driving the 6.0 view, answered click, keys and actions with "unsupported operation", with no BiDi.
  - **Effort (spike estimates, developer-days):** a Windows 1.0 30–40; a Linux 1.0 12–20 on top of the shared engine, which needs 5–8 more, counted once. Hardest: waiting and actionability rules that stay reliable on component libraries, and failure messages that show what was found. The library's trace viewer, codegen and inspector are not matched.
- **3. No test ids for now.** Locators by role and accessible name, label and text only: test ids would put test-only markup in the Production page, and role-based queries also check the page's accessibility.
- **4. One default-profile anchor controller per test process; each test gets its own InPrivate controller.** This corrects decision "Window-test profiles, trusted input, the page's pickers and the Sandbox runner":
  - **An InPrivate anchor leaks state:** test *n* saw test *n−1*'s `localStorage` and cookie in 4 of 4 tests, in two runs. A default-profile anchor, or none, isolated (0 of 16).
  - **InPrivate controllers alive at the same time share storage** unless their `ProfileName`s differ, so tests running at once in one process get distinct names.
  - **The 5.7 s wait was SmartScreen,** not the browser restart itself: step 6's probe left `IsReputationCheckingRequired` on. With it on, a close within about 0.5 s of a navigation keeps the old browser alive 6.47–6.56 s (174 of 176), and the next controller completes about 150 ms after it exits (5,653 ms at a 1 s gap, 1,663 at 5 s). With it off, the next controller took 203–291 ms at every gap from 0.1 to 8 s. Waiting for `BrowserProcessExited` first gains nothing. Linux has no equivalent (290 restarts, 83–96 ms p50 per cell).
  - **So the anchor is for speed, not correctness:** a controller on a live browser takes 49–88 ms against 203–291 ms in a new one, about 170 ms per test, against the 700 ms window-test budget.
- **Recorded for later:**
  - **M1c:** the Linux host clears `WEBKIT_INSPECTOR_SERVER` and `WEBKIT_INSPECTOR_HTTP_SERVER` unless the app opts in, as the Windows host does for the `WEBVIEW2_*` variables: with them set, any local process can run script in the page. Test runs keep `XDG_RUNTIME_DIR` outside `/var/tmp`, where WebKitGTK's sandbox aborts.
  - **SmartScreen on (an app that allows other origins):** a relaunch right after an exit waits up to about 6.2 s (6.14–6.20 s measured). A host that must reopen at once can create the new controller before closing the old one.
- **Not verified:** Windows 10 and other Runtimes; Wayland, a window manager, a GPU, HiDPI, IME and non-US layouts on Linux; the engine against the accessible-name test suites; frames and drag and drop in Spar's API; two unexplained 5 s waits on Windows with SmartScreen off (a named profile's first controller in a fresh folder, 5 of 15; a new browser's first navigation, 4 of about 386).

## 2026-11-18, Window tests on Windows (implementation record; choices for review)

Plan M1b, build step 8a (decision "Step 8's shape: window tests driven by Spar's own API"): the `Window` surface on Windows with Spar's own page API, and the required host tests of section 5. Evidence: `docs/evidence/m1b-window-tests.md`.
- **Built:**
  - **The surface:** `SparTestSurface.Window`; `SparTestApp.StartAsync<Program>(o => o.Surface = SparTestSurface.Window)` runs the app's own `Program.cs` in the test process, and the test session asks the desktop host for the app's real window (`ISparTestWindowHost`, internal in `Spar`), which `SparWindowsHost` opens through `Spar.Windows/Testing/TestWindow`. The Test variant now hands `app.Run()` its host as the other variants do, so the session has it.
  - **The host's test-only part** (`src/Spar.Windows/Testing/`, reached only behind the test-host feature switch): `TestWindowProcess` (one UI thread per test process from `UiThread.StartShared`; the Windows and Runtime checks once, before the first window, as exceptions rather than dialogs; the WebView2 variables filtered as an app does; one environment per set of browser arguments, each with a default-profile anchor controller in an offscreen window; a root per test process under `artifacts/webview2-profiles/test-host/`, leftover roots deleted only when no `EBWebView\lockfile` is left in them, the process's own root deleted at exit once the browsers let go; InPrivate profile names `spartestN`, unique among the windows open at once); `TestWindow` (the app's own `AppWindow` on that thread, offscreen at (-32000, -32000) with `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` and `SW_SHOWNOACTIVATE`, InPrivate, automated, its host log to the app's log file only; the DevTools Protocol calls and events of its page for the driver; the leftover check); `DevToolsCall` and `DevToolsEvents`, its own COM handlers.
  - **The host:** `WindowsApp` split into the process (`WindowsApp`) and the main window (`AppWindow`, new), joined by `IAppWindowOwner`; `BrowserView` takes a `BrowserSource` (shared environment, InPrivate profile, the driver's hook before the first navigation, the phases) and, for a test window, never recreates a lost browser and ends only its own renderers; `ICoreWebView2Environment13.GetProcessExtendedInfos` and the frame interfaces (`WebView2.Processes.cs`), `ICoreWebView2_20.get_FrameId`, `ICoreWebView2ControllerOptions` `put_ProfileName` and `put_IsInPrivateModeEnabled` declared; `HostLog(standardError: false)`.
  - **The end of the OS session** (left by step 1): `WM_ENDSESSION` with `TRUE` runs a close request with `CloseReason.SessionEnd` without the page's guard, the backend's guard under its 5 s deadline, the teardown, and then the services' disposal, while the window procedure holds the message in a message loop of its own (`MessagePump`), 10 s at most. `WM_QUERYENDSESSION` stays `TRUE`.
  - **The page API** (`src/Spar.Testing/Window/`): `SparPage` (`app.Page`), `SparLocator`, `SparAssertions.Expect` with `SparLocatorAssertions` and `SparPageAssertions`, `SparKeyboard`, `SparMouse`, `SparRole` and the options; the driver `PageSession` over the window's in-process DevTools session; the engine `PageEngine.js` (roles and accessible names from WAI-ARIA 1.2, HTML-AAM and Accessible Name 1.2, the label, text, placeholder, alt-text and title queries, chains, filters, strict mode, element state, and actionability with a pointer watcher that stops a press that lands on another element), installed with `Page.addScriptToEvaluateOnNewDocument` in the isolated world `spar-automation` before the first navigation and called with `Runtime.callFunctionOn` in that world; trusted input through `Input.*` after two frames following a navigation; `app.Problems` fed by a logger provider with every warning and error Spar logs for the app.
  - **Tests:** `src/Spar.Client/test/pageEngine.test.ts` (22, happy-dom); `tests/Spar.WindowTests` on the new test app `tests/apps/WindowHost` (a plain-JavaScript page and a contract whose calls run until cancelled, ignore cancellation, or echo; 22 window tests (smoke, the page API on real layout, every required host test, the long install path, the budget) and 18 that need no window (key names, address globs, how locators read), which run with the fast tests); seven Notebook window journeys (`samples/Notebook.EndToEndTests/Window/`); the production boundary's new markers; `docs/app-model.md` 6.12 with its stand-in and real-Spar builds (`tests/AppModel/SqlTool.EndToEndTests`, `tests/AppModel/RealTests`).
  - **Build and CI:** `Spar.Sdk` links the WebView2 loader into Windows test projects (`_SparWebView2Loader`), so their NativeAOT lane runs windows; `Notebook.EndToEndTests` leaves its browser files and Playwright out of that lane; CI runs window tests (trait `Surface=Window`) as their own steps, on Windows, in both lanes (not run until the first push).
- **Numbers** (Windows 11, Runtime 157.0.4341.60, 20 runs each; budget recorder): a window test of open, an 8-step journey and close took 306 ms median (p95 343) on CoreCLR Debug and 298 ms (p95 320) on NativeAOT, against the 700 ms budget: open to the page's ready 127–130 ms, the journey 157–159 ms, the close 14.6–14.8 ms with the renderer check; a test's InPrivate controller 24 ms; the first window of a test process adds the checks, the environment and its anchor once, 228 ms on NativeAOT and 427 ms on CoreCLR. Per action (NativeAOT, 100 each, median): a role lookup 0.48 ms, an assertion that holds 0.59 ms, a click 33.3 ms (checks with a frame of stability, the trusted press, the landing check), a fill 0.62 ms, a key 0.44 ms, evaluate 0.13 ms, a viewport screenshot 57 ms. The Production Notebook is 5,200,896 B against 5,194,240 B before the step (+6,656 B: the session-end phase, the window's split and its async stop; no test type, name or string in it); its budget is 6 MB. **Flakiness:** the Notebook's seven window journeys 20 times on each lane (280 journeys) and the window suite 20 times on CoreCLR and 10 on NativeAOT (660 tests), 0 failures, with test classes running in parallel on the one UI thread.
- **Choices made while building, for review:**
  1. **Type names with Spar's prefix** (`SparPage`, `SparLocator`, `SparRole`, `SparAssertions`, `SparGetByRoleOptions`…) and the library's method names. Bare `Page` and `Locator` would clash with apps' own types (the Notebook's contract has a `Page`) and with the library's in a project that has both surfaces; `SparRole` members are spelled as the library's `AriaRole` (`Menuitem`, `Alertdialog`) so journeys read alike. `GetByRole(role, options)` only: a `GetByRole(role, name)` overload would make `GetByRole(role, new() { … })` ambiguous.
  2. **Where it lives:** the page API, its driver and the engine in `Spar.Testing`, which apps never reference, so no app image can carry them; the host gives the driver only a transport (`ISparTestWindow`: DevTools calls and events, renderer ids, the window's lifetime). The CDP calls are Windows-specific; the Linux host will need another transport behind the same API (the interface is internal, so it can change). The engine is plain JavaScript (1,254 lines, under none of the size rule's languages), tested in `src/Spar.Client`'s Vitest run rather than an npm project of its own.
  3. **The Test variant intercepts `Run`** (it did not): the session needs the host to open the window. So every test executable of a Windows app carries the Windows host, the fast lane's included. Its NativeAOT executables were 18.7 MB (WireShapes.Tests) and 19.4 MB (Notebook.Tests) after the step, against 16.9 and 17.7 MB for the last ones built on this machine (from older commits, so the difference is an upper bound).
  4. **Defaults:** actions wait 10 s and assertions 5 s (`SparPage.DefaultTimeout`, `DefaultExpectTimeout`; the library's are 30 s and 5 s); retries pause 0, 20, 50, 100, 100, 250, then 500 ms, and at once after "not stable"; script dialogs are dismissed unless `AcceptDialogs` (as the library does), `beforeunload` accepted; a strict-mode violation fails at once.
  5. **Input:** `FillAsync` selects the text and inserts the value as an input method does (`Input.insertText`: `beforeinput`/`input`, no key events), and sets date, time, colour and range inputs directly with `input` and `change`; `PressSequentiallyAsync` and `Keyboard.TypeAsync` send key events on the US layout and insert characters it has no key for; `SelectOptionAsync` sets the options and fires `input` and `change`, as the library does; `CheckAsync` clicks and then requires the state to have changed.
  6. **The leftover-process check is per window:** the renderer processes of its page (by its main frame, `GetProcessExtendedInfos`), noted when the page loads and when the teardown starts, must exit within 5 s of the window's destruction, or the test's cleanup fails (reported beside the test's own failure). The shared browser itself lives until the test process exits; the run's own check is that no browser of the repository is left (evidence).
  7. **`app.Problems` on the Window surface** is every warning and error Spar logs for the app (categories `Spar.*`), from a logger provider the surface adds to the app's services: the bridge's drops, CSP violations, the page's errors.
  8. **The test process filters the WebView2 variables** for itself, once, before its first window, as an app does at start; it changes the test process's environment block.
  9. **Session end:** the page's guard is skipped (nobody can answer it, and the page's guard is not built yet), the page's close work still runs in the teardown; the hold is 10 s at most, with the services disposed inside it. In the automated mode `Fail` now shows no error dialog either (it logs only), as start-up problems already did.
  10. **Long install path:** 255 characters for the executable (the test copies itself there and runs one window test); a path over 260 cannot be started at all with long paths off (pitfalls), so it is not a host test. A long data root was not tested.
  11. **Internals for Spar's host tests:** `InternalsVisibleTo` from `Spar` and `Spar.Testing` to `Spar.WindowTests`; `o.WindowPhase`, `o.WaitForPage`, `app.Window` are internal. There is no public "close the window as the user would" yet (an app's test of its close guard needs one): left for review.
  12. **No window tests for WireShapes:** its page is a loopback test page that its executable does not pack, its wire shapes do not depend on the host, and the window transport is covered by `WindowHost`'s calls.
  13. **The test window follows the system's theme** (dark on this machine); a colour-scheme option for tests is left.
- **Deviation from the plan:** the close during a stream is not tested (streams arrive in M3); everything else of step 8a is built.
- **Found** (pitfalls): a DevTools evaluation is not under the page's CSP; an executable over `MAX_PATH` cannot be started; a feature switch kept in an async method's local is not folded (the shared-environment path was in the Production image until each condition read the switch); a dotted namespace is no boundary marker in a NativeAOT image.
- **Not verified:** the hosted CI runner's session; Windows 10, other Runtimes, DPI other than 96; IME and non-US layouts; frames, drag and drop, network waits, file choosers in the page API; the engine against the WPT accessible-name suites; a native engine abort's effect on a run; real OS input (Windows Sandbox, step 8c).

## 2026-11-18, Window tests: closing, session-end budget, test theme and any-character keys (user decisions; implementation record)

Follow-up to "Window tests on Windows (implementation record; choices for review)", whose choices 5, 9, 11 and 13 the user answered on 2026-11-18. Evidence: `docs/evidence/m1b-window-close.md`.
- **Decided (user, 2026-11-18):**
  1. **Keys for any character.** Text is walked by Unicode scalar value, never by UTF-16 unit, so an emoji is never two U+FFFD. A character the US layout has no key for still sends `keydown` and `keyup` (`key` the character, `code` empty, the text), and `PressAsync` takes any one character. The US layout stays for the characters it has. Dead keys, other layouts and input-method composition are not built.
  2. **Choice 11: closing from a test, with the page's close guard built.** `windows.onCloseRequested` in the generated `@spar/app/windows`, the host asking the page first and then the backend, one native confirmation per ask, a second request joining the open one, a navigated document's answer ignored, and "Wait / Close anyway" for a page silent for about 5 s with no dialog of its own, or already hung, replacing the ping of step 3. The test API: `await app.Window.CloseAsync()` (automated by default, an ask counts as close) and `app.Window.CloseAsync(new() { Automated = false, Answer = CloseAnswer.KeepOpen })`, whose result carries `Outcome` (`CloseOutcome.Closed` or `Kept`) and `Question`; a window test never shows a dialog.
  3. **Choice 9: session end within 4 s in total.** The guards, page then backend, share at most 3 s; teardown and the services' disposal run until 4 s from the start of `WM_ENDSESSION`; then the window procedure returns whatever is left. Why: Microsoft's `WM_QUERYENDSESSION` page (after five seconds the system shows which applications prevent shutdown), the archived "Application Shutdown Changes in Windows Vista" (ms700677, Table 1: an app with a visible top-level window may take as long as it needs in `WM_ENDSESSION`, with new UI after 5 seconds), and the `WM_ENDSESSION` page (the session can end any time after every application has returned). **This supersedes** the "about 5 s" deadline of "Two-stage close" and choice 9 of the step-8a record (the page's guard skipped at session end, the backend's 5 s, a 10 s hold).
  4. **Choice 13: window tests are light by default.** `o.ColorScheme = SparColorScheme.Light` unless `Dark` or `System`, for the page's `prefers-color-scheme` and the window's own colours, so screenshots match on every machine. **This supersedes** choice 13 of the step-8a record (the system's theme).
- **Built:**
  - **Bridge** (plan section 2c, protocol 1 unchanged: new types a host sends only to a page that lists them): `close-request` (host to page, `seq`, payload `{"reason","automated"}`), `close-answer` (page to host, `seq`, payload `{"answer":"close"|"keep"|"failed"}` or `{"answer":"ask","question":…}`), `close-ended` (host to page, `seq`: the request ended without the page, its guards' `signal` is aborted), in both codecs and the shared vectors (3 canonical, 3 malformed); the injected script's `pong` carries `{"dialog":true}` while the page shows a modal `<dialog>` or a visible `aria-modal="true"` element.
  - **SDK:** `src/Spar.Client/src/windows.ts` (`windows.onCloseRequested`, `CloseRequest`, `CloseAnswer`, `CloseGuard`), run by the connection, which lists the two host types in `hello`; the client step generates `@spar/app/windows` re-exporting it (`windows.js`, `windows.d.ts`, the `./windows` export); `fakeSpar().requestClose({ reason, automated, signal })` in `@spar/app/testing` for page tests.
  - **Host:** `AppWindow.Closing.cs` (the two-stage close, `RequestCloseAsync` that a second request joins, the session end within 4 s), `PageWatch.Closing.cs` (the page's side: ping, `close-request`, the 5 s check, "Wait / Close anyway" by dialog, by a test's answer or by the automated mode), `Spar.Hosting.PageCloseAnswer` (the answer read as untrusted text), `ICloseQuestions` (the questions a test answers instead of the native dialogs), `CloseGuard` with the shared budget, the teardown cut to a deadline, `WindowTheme.Fixed`.
  - **Test API** (`Spar.Testing`): `SparWindow` (`app.Window`, public) with `CloseAsync(SparCloseOptions?)`, `SparCloseOptions` (`Automated`, `Answer`, `Reason`, `Timeout`), `SparCloseResult` (`Outcome`, `Question`, `PageDidNotAnswer`), `CloseOutcome`, `CloseAnswer`; `SparTestOptions.ColorScheme` with `SparColorScheme` (`Light`, `Dark`, `System`); `KeyboardLayout` and `SparKeyboard` by `Rune`, `KeyboardLayout.Unmapped`. The host tests' internals (`Opened`, `Closed`, posting `WM_CLOSE`, window messages, renderers, the native theme) stay internal on `SparWindow`.
  - **Tests:** `src/Spar.Client/test/closing.test.ts` (11) and a page-script test of the pong's flag; `tests/Spar.Tests/PageCloseTests.cs` (22 cases) and a `CloseGuardTests` case for the shared budget; window tests `CloseTests` (15), `KeyTests` (9), `ThemeTests` (5), two new `HostTests` (the session end's hold) and the session-end test now running the page's guard; `PageApiUnitTests` for any-character keys; the Notebook's page guard with four page tests and three window journeys (`Window/CloseJourneyTests.cs`); `docs/app-model.md` 6.11 and 6.12 with the compiled `ClosingWindowTests.cs` (stand-ins and real Spar; 35 blocks); new boundary markers.
- **Choices made while building, for review:**
  1. **The messages carry their data in the payload** (reason, mode, answer, question), with only `seq` in the header, so neither codec gained header fields; the host reads the answer with its own small reader and cuts a question at 500 characters (surrogate-safe). Recommendation: keep.
  2. **"A dialog of its own" is a modal `<dialog>` or a visible `aria-modal="true"` element,** reported by the injected script in each `pong`; the host pings before `close-request`, so the pong is read before a guard can block the page. A `confirm()` blocks the page and counts as no answer (pitfalls). Recommendation: keep; guards answer `{ ask }` or use the page's own modal.
  3. **A silent guard on a live page** (it answers pings, not the request) gets "Wait / Close anyway" after one interval but is **not reported** to `spar.OnPageUnresponsive`, and "Close anyway" is reported as `ClosedAnyway` only for a hung page. Wait asks again after 4, 8, 16, then every 32 intervals. Recommendation: keep; the hook is for hangs.
  4. **Page guards run one after another in the order added;** the first keep or ask ends the run; a guard that throws, or answers anything but `"close"`, `"keep"` or `{ ask }`, makes the answer `failed`, which the host asks about as for a failed backend guard ("The page could not check whether it is safe to close. Close anyway?"), and which counts as close at session end and in the automated mode. Recommendation: keep.
  5. **A document that goes away during the request** (navigation, a reload) counts as agreement and is not asked again; its late answer reaches no open request. Recommendation: keep while the page is one document; revisit with secondary windows.
  6. **At session end the page's guard gets at most 2 s,** so the backend's always has at least 1 of the shared 3 s; when the page's time runs out the host sends `close-ended`. Recommendation: keep.
  7. **`SparCloseResult.PageDidNotAnswer`** is added to the decided `Outcome` and `Question`: the page did not answer in time and "Wait / Close anyway" was answered. `Question` is the last question a guard asked, also one that counted as close in the automated mode. Recommendation: keep both.
  8. **`Answer` answers every question of that close,** a guard's ask and "Wait / Close anyway" alike; `KeepOpen` is "Wait" for a page that does not answer, which keeps the request open until the page answers. **`Automated = false` without `Answer` throws `ArgumentException`** before anything is sent (the recommendation of the request). Recommendation: keep.
  9. **`Reason` exists** (`User` by default, `App`, `SessionEnd`); `SessionEnd` runs the session end's rules and budgets without Windows' message hold. `Timeout` (30 s) bounds the request and the teardown. Recommendation: keep.
  10. **After `Closed`,** `CloseAsync` has waited for the window's teardown and its leftover-renderer check; `app.Page`'s actions then fail with "The app's window is closed (app.Window.CloseAsync or the test's end closed it)". A close while another is open joins it and gets its result. Recommendation: keep.
  11. **The theme of a test window:** `WindowTheme.Fixed` for the window and its title bar, and the page's scheme emulated on its DevTools session before the first navigation (`Emulation.setEmulatedMedia`), because the profile's `PreferredColorScheme` alone did not reach the page on a test's InPrivate profile (pitfalls); `System` emulates nothing. A DevTools screenshot holds only the page, so the window's own colours are checked from the window (its brush colour, `DWMWA_USE_IMMERSIVE_DARK_MODE`). Recommendation: keep; find why the profile setting does not apply (not blocking).
  12. **Keys by `Rune`, not by text element:** DevTools refuses key text of four UTF-16 units or more (measured: 2 and 3 accepted, 4 and 8 refused), so a grapheme such as a family emoji cannot be one key, while one scalar value always fits; a combining sequence arrives as one key per code point and the page ends with the same value. `PressAsync` takes exactly one scalar value. `FillAsync` already inserted the whole value at once; it gained tests. Recommendation: keep.
  13. **`fakeSpar().requestClose`** drives the page's guards in page tests (the natural place: the fake speaks the real protocol); aborting its `signal` sends `close-ended`. Recommendation: keep.
  14. **`@spar/app/windows` has no window-kind union yet:** window kinds are not built (M3); `tests/AppModel`'s stand-in keeps its union. Recommendation: add the union with secondary windows.
  15. **A test window disposes the app's services inside the session end's hold,** as `WindowsApp` does, so the hold measured in a test includes a slow disposal; the test host's later disposal finds them done. Recommendation: keep.
  16. **The Notebook's page guard** saves a draft that is not saved yet, waits for a save on its way, tries a failed save once more, never overwrites a conflict, and asks "<why> Close without your last changes?" when it cannot save; in the automated mode and at session end it closes. Recommendation: keep.
- **Numbers:** `WM_ENDSESSION` held 4,001–4,015 ms (21 runs, both lanes) with a page guard and a backend guard of 5 s each, a call in flight and a service whose disposal takes 5 s (the disposal still running at the return), and 15–95 ms with nothing slow. "Close anyway" for a silent guard closed the window 5,014–5,030 ms after the request, for a hung page 5,015–5,031 ms; "Wait" on a page hung 12 s ended with its guard's own answer after 11,842–11,861 ms (20 runs each). A window test of open, an 8-step journey and close: 305 ms median on CoreCLR, 299 ms on NativeAOT, against 700 ms (step 8a: 306 and 298). The Production Notebook: 5,234,688 B (+33,792 B over step 8a's 5,200,896 B; budget 6 MB), no test-only name in it. Flakiness: the window suite (79 tests) 10 times on each lane, 1,580 tests, and the Notebook's 10 window journeys 10 times on each lane, 200 journeys: 0 failures.
- **Left over:** `CloseReason.App` from app code (no app-side "close this window" API is decided); the window-kind union (M3); scenes and a fixed clock.
- **Not verified:** a real sign-out or shutdown on Windows 11 (with step 8c's Sandbox runner); the native confirmation and "Wait / Close anyway" with real input on the desktop; a guard that calls `confirm()`; the Wait schedule's later asks for a silent guard (40, 80, 160 s); a session end while a user's close dialog is open; why the profile's `PreferredColorScheme` does not reach an InPrivate page; dead keys, IME and other layouts; Linux.

## 2026-11-19, Window tests: two timing races found in review, fixed (implementation record)

Review of "Window tests: closing, session-end budget, test theme and any-character keys (user decisions; implementation record)" found its window suite failing 5 times in 9 back-to-back runs. Evidence: `docs/evidence/m1b-window-close.md` ("After review").
- **A test that read the page's console right after `CloseAsync` returned:** the SDK logs a failed guard before it answers, but the console message comes by the page's DevTools events and the answer by the bridge, with no order between them. The test now waits for it, bounded; the SDK's order was already right. No other test asserted an event delivered that way without waiting.
- **A click on a dialog's button that did nothing:** the button passed one frame of stability before its transition started, got the press, and moved before the release, so the click went to the dialog. Pre-existing in the engine, made likelier by the heavier parallel suite. Built: stability also waits while a running animation moves the element or an ancestor; a first click that misses an element still in the page is stopped and the action tried again (an element the page removed, or a label's later click on its control, is no miss); the watcher's listeners are registered once at the engine's install, before page scripts, so stopping an event keeps it from the page's own capturing listeners. A deterministic window test of a button that jumps on its first press.
- **Choice for review:** stopping a stray click and retrying is the engine's own rule beyond the browser automation library's; a page that acts on the press or the release and keeps the element in the page would see that press twice. Recommendation: keep; such a page should act on the click.
- **Numbers:** after the fixes, `dotnet test --project tests/Spar.WindowTests` 30 times on CoreCLR (2,400 tests), the NativeAOT executable 10 times (800), the Notebook's window journeys 10 times per lane (200): 0 failures.

## 2026-11-19, Step 8b's shape: the black-box fixture (user decisions)

Asked at the start of M1b step 8b (the black-box level on the Production executable, with a UI Automation smoke test), with options and a recommendation for each. The user took the recommendation each time. The data root (the environment only), the generated `Projects.X` class and the failed relaunch's message read through UI Automation were decided earlier (decision "M1b's remaining questions without a spike").
- **1. One entry point.** `await using var app = await SparBlackBox.LaunchAsync(Projects.Notebook, o => …)` waits until the app is ready; `o.WaitForReady = false` returns at once, for start-up failures and their exit codes. The handle carries the process id, the data root and the folders under it, the captured output, `CloseAsync` (the window's close button, as the user would), `WaitForExitAsync`, the exit code and `Kill`. `o.DataRoot = first.DataRoot` starts a second instance on the same data. The fixture also points `TEMP`, `TMP` and `APPDATA` into the root, and the app dies with the test process (a job object) and after a deadline. Chosen over `Start` plus explicit waits, and over a fixture class tied to one test framework.
- **2. Ready means the log line and the window.** The app's own log file in the disposable root has the host's `ui-ready` phase line (the page connected its bridge), and UI Automation finds the process's top-level window. The phase line becomes a format Spar keeps stable. Chosen over UI Automation alone (a loaded document does not prove the bridge connected) and the log alone (a window that never appeared would pass).
- **3. A hidden desktop, after a spike.** On this PC the fixture launches the app on a separate Windows desktop (`CreateDesktop`, `STARTUPINFO.lpDesktop`), so nothing appears on the monitor and focus is never taken. WebView2's rendering and UI Automation there are unproven, so a spike comes first; if it fails, the window is shown without activation (`SW_SHOWNOACTIVATE` through `STARTUPINFO`, which the host honours since it calls `SW_SHOWDEFAULT`). Chosen over both of those directly, and over running black-box tests only in Windows Sandbox (step 8c).
- **4. A small public UI Automation API.** `app.Window` with its title and a few queries named as in the page API (`GetByRole(SparRole, new() { Name = … })`, waits, `InvokeAsync` through UI Automation's Invoke pattern, which is no input), and `app.WaitForDialogAsync()` returning a native dialog's text and buttons with `PressAsync`. Written from scratch over UI Automation's COM interfaces with source-generated interop, so it runs under NativeAOT. Chosen over keeping UI Automation internal to the fixture, and over the whole page API re-implemented over UI Automation.
- **Taken without asking, as the conventional default:** a test project opts in with `<SparBlackBoxTests>true</SparBlackBoxTests>`, as `SparBrowserTests` does for the Browser surface; it gets the `Projects.X` classes and the incremental Production publish, which `SparBlackBoxPublish=false` turns off.

## 2026-11-19, The black-box fixture (implementation record; choices for review)

Plan M1b, build step 8b (decision "Step 8b's shape: the black-box fixture (user decisions)"): the black-box level on the app's real Production NativeAOT executable, with a UI Automation smoke test. Spike: [`spikes/blackbox-desktop`](evidence/blackbox-desktop/RESULTS.md). Evidence: `docs/evidence/m1b-black-box.md`.
- **The spike's verdict on the hidden desktop: it wins.** 50 of 50 Production Notebook launches on a desktop of their own (`CreateDesktop`, `STARTUPINFO.lpDesktop`) reached `ui-ready` (385 ms median, 377–541), were found by UI Automation (391 ms), showed the page's tree with ARIA roles (a heading by name at 497 ms), took an Invoke and closed with exit 0 (34 ms; every process gone 126 ms after the request); 0 samples (one every 10 ms) with an app process in the foreground and 0 visible windows of the app on the input desktop; the desktop and the 8 processes gone after close; the GPU process up and not restarting; the real `%LOCALAPPDATA%\com.example.Notebook` never created. A UI Automation thread placed there with `SetThreadDesktop` read the native task dialogs and pressed their buttons (a held lock: exit 8 after 5.1 s; a suspended first instance: exit 8 after 2.1 s). **The fallback** (`SW_SHOWNOACTIVATE` through `STARTUPINFO` on the user's desktop) **took the foreground in 2 of 2 launches** (about 0.6 s each), so it was not run further and is no fallback for a machine in use. **Invoke takes about 2 s on the hidden desktop** (2,040–2,082 ms, against 0.5–1.1 s on the input desktop): the page acts on the click only when the call returns; finding and reading are not slowed.
- **Built:**
  - **The fixture** (`src/Spar.Testing/BlackBox/`): `SparBlackBox.LaunchAsync(SparProject, Action<SparBlackBoxOptions>?, CancellationToken)`; `SparBlackBoxOptions` (`Arguments`, `Environment`, `DataRoot`, `WaitForReady`, `ReadyTimeout`, `ExitTimeout`, `KeepData`); the handle `SparBlackBoxApp` (`ProcessId`, `ProcessIds`, `DataRoot`, `LocalAppData`, `RoamingAppData`, `TempFolder`, `AppFolder`, `DataFolder`, `LogsFolder`, `LogFile`, `StandardOutput`, `StandardError`, `StartedAfter`, `Window`, `WaitForDialogAsync`, `CloseAsync`, `WaitForExitAsync`, `WaitForProcessTreeExitAsync`, `HasExited`, `ExitCode`, `Kill`, `DisposeAsync`); `SparBlackBoxWindow` and `SparBlackBoxLocator` over UI Automation; `SparNativeDialog`; `SparProject`. The process (`AppProcess`): `CreateProcess` with `PROC_THREAD_ATTRIBUTE_JOB_LIST` (a job object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, joined at creation) and `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` (only the two output pipes inherited), a Unicode environment block, and quoting that `CommandLineToArgvW` reads back. The desktop (`HiddenDesktop`, `spar-black-box-<pid>`, one per test process, named in the caller's window station) and its UI Automation thread (`AutomationHost`, `AutomationThread`: MTA, `SetThreadDesktop`, `CUIAutomation8`, every element released when its call returns, a call abandoned with its thread after 10 s). The app's windows by process id through `EnumWindows` (`TopWindows`), never a search from the root. Win32 through CsWin32 (now a private reference of `Spar.Testing`); UI Automation hand-declared (`BlackBox/Interop/UiAutomation.cs`: six interfaces, every slot up to the last one called, `VARIANT` by value). Platform code sits behind `IBlackBoxProcess` and `IBlackBoxAutomation`, so the public types carry no OS annotation; elsewhere than Windows they throw `PlatformNotSupportedException`.
  - **The ready signal:** `HostPhases.UiReady` (`src/Spar/Hosting/`), used by the host's bridge and by the fixture; `ReadyLine` matches `<time> INFO  Spar.Host  [<run id>] ui-ready at <ms> ms` in `app.log`, read as it grows (`LogWatch`), then the main window through UI Automation. Pinned by `tests/Spar.Tests/ReadyLineTests.cs` against the line `HostLog` writes; documented in `docs/app-model.md` 6.13.
  - **The build** (`Spar.Sdk`): `<SparBlackBoxTests>true</SparBlackBoxTests>`; the app answers `SparAppId`, `SparAppName` and `SparAppExecutableName` with its `GetTargetFrameworks` answer (`AdditionalTargetFrameworkInfoProperty`, no evaluation of its own); target `SparBlackBoxApps` turns each tested app into an `AdditionalFiles` item with that metadata (in design-time builds too), which the generator turns into `Projects` (`ProjectsEmitter`, `Spar.Projects.g.cs`); target `SparBlackBoxPublish` restores and publishes the app in-process (`Configuration=Release;SparVariant=Production;SparBlackBoxBuild=true;_IsPublishing=true;PublishDir=…`) into `obj/…/spar-apps/<app>/`, and a `None` item copies the executable beside the tests and into their publish. `SparBlackBoxBuild` gives that publish `obj/Production.BlackBox/` and `bin/Production.BlackBox/` (`Sdk.props`); Spar's own projects and the app's libraries drop `SparBlackBoxBuild`, `PublishDir` and `_IsPublishing`; the caller's solution configuration map is removed.
  - **Tests:** `samples/Notebook.EndToEndTests/BlackBox/BlackBoxTests.cs` (6: start, page and clean close with no process left; a page button invoked and its dialog seen, after the strict-mode refusal; every file under the root and the real per-user folder untouched; a second launch hands over and exits 0; a relaunch to a lock nobody serves shows its message, read and answered, exit 8; a start whose profile folder is a file shows its message, exit 5). `tests/Spar.WindowTests/BlackBoxFixtureTests.cs` on WindowHost's executable (11: the job's kill-on-close ends the app and the browser's processes; a hung app ended after its deadline and reported; a root that cannot be deleted reported; a launch not ready in time ended with its root deleted; a relaunch to a suspended first instance read and answered, exit 8; the window's queries with `Exact`, `NameRegex`, `Disabled`, strictness, a disabled element waited for, `NotSupportedException` for `Level`, an Invoke and its result, a text field's value; option refusals; the environment; argument quoting). `tests/Spar.Tests/UiAutomationSlotTests.cs` against the Windows SDK's `UIAutomationClient.h` (an explicit skip without an SDK), with the WebView2 slot test's checker moved to `ComSlots.cs`. The package consumer's black-box project (`Spar.PackageTests`: the app published by the packed SDK, launched, and an unchanged build publishes nothing). New production-boundary markers; the static-state list; `docs/app-model.md` 6.13 with its stand-ins and the real-Spar build (`tests/AppModel/RealTests`, `SparBlackBoxPublish=false`; the document's real app now has the assembly name `SqlTool`).
  - **CI:** the solution run leaves out `Surface=BlackBox`; a "Black-box tests (CoreCLR)" step on Windows; the NativeAOT window step runs them too (not run until the first push).
- **Numbers:** the Notebook's six black-box tests run in 15.1 s on NativeAOT (median of 20; 14.9–15.4) and 17 s with `dotnet test` on CoreCLR, about 2.5 s per test; the fixture's eleven in 18.6 s (NativeAOT). **Flakiness:** both suites 20 times on each lane after the last change, 680 tests, 0 failures; before that, 1 failure in 24 suite runs (the root's deletion, Found), fixed and the series started again. The Production Notebook is 5,234,688 B, the size before the step, with none of the new markers; the executable the build publishes beside the tests is the same size. The first publish adds a NativeAOT publish of the app to the test project's first build (about 10 s for the whole build once Spar is built, warm); an unchanged build skips it (the whole `Notebook.EndToEndTests` build 2.3 s; a changed app source 10.5 s). `Spar.BuildTests` 6 of 6 and `Spar.PackageTests` 12 of 12 (the new black-box case included) passed.
- **Choices made while building, for review:**
  1. **One hidden desktop per test process,** `spar-black-box-<pid>`, created at the first launch and freed by Windows when the test process exits (the spike made one per launch). Recommendation: keep; one desktop heap and one UI Automation thread serve every launch.
  2. **The placement is one internal switch** (asked for by the coordinator for 8c): `SparBlackBox.Placement` for the run and `SparBlackBoxOptions.Placement` per launch, with `HiddenDesktop` (the default), `ShownWithoutActivation` (measured to take the foreground) and `Activated` (for the Sandbox runner, since real input reaches only the input desktop). Recommendation: keep internal until the runner exists.
  3. **`Projects` is an internal static class in the global namespace with one `SparProject` property per tested app,** not a class per app: `LaunchAsync(Projects.Notebook)` needs a value. Recommendation: keep.
  4. **The property is named by the app's assembly name,** not its project file's (`Projects.NotesTool` for the package consumer's `Notes.App.csproj`), since that names the executable. Recommendation: keep.
  5. **`ExecutablePath` is `AppContext.BaseDirectory/spar-apps/<name>/<executable>`,** the executable copied there (its PDBs stay in `obj/`); `ProjectDirectory` is the building machine's absolute path. Recommendation: keep.
  6. **`SparBlackBoxPublish` defaults to true on Windows only** until the Linux fixture (M1c), so a Linux build of a black-box project needs no NativeAOT toolchain; the tests skip there. Recommendation: keep until M1c.
  7. **The publish runs on every build of the test project** and MSBuild's incremental targets skip it when nothing changed (no native code generated, the executable untouched; the whole unchanged build of `Notebook.EndToEndTests` took 2.3 s); a `Restore` call is made every time (a no-op when restored). Recommendation: keep.
  8. **Defaults:** `ReadyTimeout` 30 s; `ExitTimeout` 15 s; a locator's `DefaultTimeout` 10 s; one UI Automation call 10 s; the process tree 10 s after an exit; polls every 25 ms (ready) and 50 ms (locators, dialogs). Recommendation: keep.
  9. **Disposal:** an app the test never asked to exit is ended at once, which is no failure; one asked to exit gets `ExitTimeout`, then it is ended and that is a failure; browser processes still running 10 s after a clean exit are a failure (then ended); a root that cannot be deleted within 5 s of the last process ending is a failure. All go through `SparTestCleanup`, as `SparTestApp`'s do. Recommendation: keep.
  10. **The environment** is the test process's own without any `WEBVIEW2_*` or `SPAR_*` variable, with the four folders in the root; `o.Environment` may not name those (`ArgumentException`); `o.DataRoot` must be a data root of this test run. Recommendation: keep.
  11. **Data roots are `artifacts/test-host/<run>/black-box-NNN/`,** the app's WebView2 profile inside, following `TestDataRoots`, not under `artifacts/webview2-profiles/`: Spar does not set that profile, the app derives it from `LOCALAPPDATA`. Recommendation: keep.
  12. **`app.Window`'s API:** `TitleAsync()` (a read through UI Automation, so a method, like the page API's `TitleAsync`), `GetByRole` with `Name`, `NameRegex`, `Exact` and `Disabled` (the other options throw `NotSupportedException`), `GetByText`, `First`, `Last`, `Nth`, `CountAsync`, `IsVisibleAsync` (not off screen), `AllNamesAsync`, `NameAsync`, `InputValueAsync` (the Value pattern), `InvokeAsync` and `WaitForAsync`; strict like the page API. A role matches a page element by its ARIA role and a native one (no ARIA role) by the control type that stands for it. Recommendation: keep; add more when an app's test needs it.
  13. **`WaitForDialogAsync(TimeSpan? timeout)`** returns the process's first dialog (`#32770`: a task dialog or a message box) with `Title`, `Text` (its text elements on lines of their own), `Texts` and `Buttons` (the title bar's left out), and `PressAsync(name)` with the whole name; it fails at once when the app exits without one. Recommendation: keep.
  14. **`CloseAsync` uses UI Automation's Window pattern** (`Close`, the close request the window's close button sends) and returns the exit code, or throws `TimeoutException` after `ExitTimeout`; a guard's question is read and answered through `WaitForDialogAsync` meanwhile. Recommendation: keep.
  15. **Trait `Surface=BlackBox`** for black-box tests, their own CI invocation. Recommendation: keep.
  16. **The Notebook's start-up failure is a profile folder that is a file** (exit 5): a UNC `LOCALAPPDATA` falls back to the real per-user folder (Found) and the folder refusal (exit 70) needs a `Program.cs` the Notebook does not have. Recommendation: replace it with a cleaner start-up failure once the two Found items about folders are settled.
  17. **The failed relaunch in the Notebook's tests is a held lock** (the 5 s wait), with public API only; the not-answering case (2 s) is in Spar's own tests through the internal `SuspendProcess`. Recommendation: keep.
  18. **No black-box budget test:** the numbers come from the spike and the suites' durations. Recommendation: add one with M4's performance work.
- **Found:**
  - **A UI Automation search from the desktop's root stalls on a hung app** (pitfalls); the fixture reaches windows by handle.
  - **The app's process has more visible top-level windows** than its main one (the input indicator's); the fixture takes the first whose control type is Window.
  - **A solution build gave the black-box publish Debug builds of Spar:** the solution's configuration map (`CurrentSolutionConfigurationContents`, inherited from the test project) assigned the app's references the solution's configuration, and the Production executable came out 13,312 B smaller than its own publish. The publish now drops that property; the executable is then the size of `dotnet publish -c Release`.
  - **`RemoveProperties` on the `MSBuild` task also removes what `Properties` sets** (pitfalls).
  - **A profile folder the app cannot create is reported as a broken WebView2 Runtime** (exit 5, "Install it again from Microsoft"). Needs a decision: a configuration failure (exit 70) naming the folder would tell the truth.
  - **A UNC `LOCALAPPDATA` falls back to the real per-user folder** in `SparFolders.PerUserRoot`, where decision "M1b's remaining questions without a spike" says it is refused. Not run (it would write into the developer's profile). Needs a decision: refuse it at start, or keep the fallback and correct the decision's text in a new entry.
  - **A browser file stays open a moment after its job reports no process:** deleting the root right after a suspended app was ended failed once on `CrashpadMetrics-active.pma` ("being used by another process"), in the 7th of 12 runs of the fixture's suite, while the build tests ran beside it; the earlier delete gave up after 0.5 s. Disposal now retries for 5 s (`TestDataRoots.DeleteAsync`), and the flakiness runs were started again.
  - **A page that never calls the SDK's `ready()` never makes a launch ready:** the package consumer's page (written for the loopback host only) timed out after 30 s with no `ui-ready` line; its black-box test launches with `o.WaitForReady = false` and waits for its heading, as `docs/app-model.md` 6.13 now says.
  - **Invoke is slow on the hidden desktop** (2 s); not explained.
- **Not verified:** Linux (AT-SPI, M1c); Windows 10; the hosted CI runner (whether its session allows a desktop of its own; the step has not run); real input, focus and the first click (Windows Sandbox, 8c); a crash record at the next start (no crash records yet); exit 9 (another user); an app whose `Program.cs` moves its logs folder (no ready line: documented); a test process killed in the middle of a launch, beyond the spike's one forced kill.

## 2026-11-19, Two folder findings of step 8b fixed (user decision; implementation record)

The two items "Found" in "The black-box fixture (implementation record; choices for review)" that needed a decision. Asked, the user chose "Fix both". Evidence: `docs/evidence/m1b-black-box.md`, "After review".
- **A network `LOCALAPPDATA` is refused at start, as decision "M1b's remaining questions without a spike" says.** The code fell back to the known folder, which moved the app's data to the user's real per-user folder when the process that started it asked for another. Now the variable is kept when fully qualified (a missing or relative one still falls back), and the Windows host refuses a data root on a network drive before it reads `logging.json` or opens its log file: the configuration message (`spar.Folders` and `LOCALAPPDATA` named), exit 70, standard error only. The same check covers a root an app's `spar.Folders` choices put there. `spar.ResolveFolders()` still returns the network folders; an app that writes there from `Program.cs` before `Run` does so on its own. A black-box test runs it on WindowHost's Production executable.
- **A profile folder that cannot be created is exit 70, not 5.** The host creates the folder once it holds the single instance, before the WebView2 environment, and a failure is the configuration message naming the folder. A folder that exists but that the browser cannot write still shows as a failed controller (exit 5); not changed, since telling the two apart needs the browser's own error. The Notebook's start-up failure test now expects 70, which also settles choice 16 of the implementation record.

## 2026-11-19, Step 8c's shape: the Windows Sandbox runner (user decisions)

Asked at the start of M1b step 8c (Spar's Windows Sandbox runner, decided as internal to `Spar.Testing` in M1b by "Window-test profiles, trusted input, the page's pickers and the Sandbox runner"), with options and a recommendation for each. The user took the recommendation each time, and asked one question back.
- **1. Its own test project.** `tests/Spar.SandboxTests`: `dotnet test --project tests/Spar.SandboxTests` publishes `Spar.WindowTests` and `Notebook.EndToEndTests` with NativeAOT, runs their Sandbox-tagged tests in one sandbox, and reports each guest test as an ordinary test result on the host, with screenshots and UI Automation dumps under `artifacts/test-results/`. Chosen over a script in `eng/` (its results would not be test results) and over an MSBuild target (the public shape M3 designs).
- **2. In `Spar.slnx`, not built (the user's question).** Asked why the project would stay outside the solution, and whether it could be in it but not built, the user chose, after a check, to put the Sandbox tests, the build tests and the package tests in `Spar.slnx` with `<Build Project="false" />`. Checked on a throwaway solution with `Spar.BuildTests` marked that way: `dotnet build` on the solution built no DLL for it, `dotnet test --solution` ran only the other project's tests (96 of 96) with and without `--no-build`, and gave no error; `dotnet test --project` still runs it. No reason beyond keeping them out of the solution's test run had been recorded for leaving them out. Not checked: an IDE's "Run all" (it may build and run them) and the solution's restore (it may restore them).
- **3. The "ask first" rule: an explicit allow per run.** The SYSTEM lane (no windows, nothing on the host's desktop) always runs. The user-session lane, whose client window takes the foreground 2–3 times per connect, runs only when the run says so (for example `SPAR_SANDBOX_DESKTOP=allow`); without it those tests are explicit skips naming the variable. Agents ask the user before setting it. Chosen over documentation only and over no user-session lane until M4.
- **4. The checks of 8c, all four offered:** the Production input checks on the Notebook's Production executable (F12 and the devtools keys, the browser keys, Alt+Left and the mouse back and forward buttons doing nothing, a text field's context menu with only Cut, Copy, Paste and Select all); focus and dialogs (the first click on an inactive window, Tab into the page after activation, the native close confirmation and "Wait / Close anyway" with real keys and mouse); a real sign-out of the guest with the app open and a close guard waiting (the session end within 4 s), after a short spike, since signing out may end the sandbox; the Production executable's first run on a clean, offline guest, and the page's `online` and `offline` events (a step-9 check).
- **5. Real input: internal, on the black-box locators.** `ClickAsync`, `PressAsync` and `TypeAsync` on the black-box fixture's `app.Window` locators, through `SendInput` at the element's UI Automation bounds, internal to `Spar.Testing` for Spar's own Sandbox tests and refusing to run outside a Sandbox guest. It becomes public with M3's app-facing layer. Chosen over public now and over helpers in the test project.

## 2026-11-19, The Windows Sandbox runner (implementation record; choices for review)

Plan M1b, build step 8c (decision "Step 8c's shape: the Windows Sandbox runner (user decisions)"). Spike: [`spikes/sandbox-signout`](evidence/sandbox-signout/RESULTS.md). Evidence: `docs/evidence/m1b-sandbox.md`.
- **Built:**
  - **The runner** (`src/Spar.Testing/Sandbox/`, internal): `SandboxRunner` (availability through `wsb --version` on Windows 11; `wsb list` refusing with `SandboxBusyException` while any sandbox runs; the shared jobs in one sandbox, in order, then each `FreshSandbox` job in a sandbox of its own), `SandboxSession` (start with a new id and the configuration as a string; ready when `cmd /c exit 0` runs as SYSTEM; the runner's marker written as SYSTEM; the guest's WebView2 registration pointed at the Runtime on its disk (choice 17); `wsb connect` only before the first user-session job, its client windows minimized every 100 ms while shown, by `ShowWindowAsync`, until the stop; each job staged under `jNN-<random>` without PDBs, `ready.txt` last, probed as SYSTEM; the run as `wsb exec` in the job's lane with the job's deadline; at the deadline `taskkill /f /t /im <exe>` in the guest; `exitcode.txt` waited for; the job's `*.log` files copied from the guest's disk; `wsb stop`, this runner's `wsb connect` ended by its process object, the client windows that appeared after its connect closed with `WM_CLOSE`, `wsb list` again; a timeline per sandbox), `GuestScript` (the configuration: networking, clipboard, printers, camera and microphone `Disable` unless asked, input read-only at `C:\spar\in`, output writable at `C:\spar\out`; the job's `run.cmd`), `GuestTestResults` (xUnit's JSON reporter read per test), `Wsb`, `SandboxClientWindows`, `SandboxTimeline`, `SandboxJob` with `SandboxLane`, `SandboxFeatures` and `SandboxDesktopGate`, `SandboxGuest`, `SandboxEvidence`.
  - **The run's folders for the tests** (`src/Spar.Testing/TestArtifacts.cs`): `SPAR_TEST_ARTIFACTS` (data roots and profiles) and `SPAR_TEST_RESULTS` (results, `<folder>/<project>/`), set by the guest script, otherwise the repository's `artifacts/`; `TestDataRoots` and the tests' result helpers (`Spar.WindowTests` `Results`, the Notebook's screenshots, the shared `Budgets.cs`) use them, so no test looks for the repository's files to find its results.
  - **Real input** (`src/Spar.Testing/BlackBox/Input/`, `SparBlackBoxWindow.Input.cs`, `AutomationHost.Input.cs`, internal): `RealInput` (`SendInput`: the mouse at a screen point with any button, the extra buttons as back and forward; keys by the page API's names with modifiers, scan codes and the extended flag; text as Unicode input), on the black-box locators `ClickAsync(RealMouseButton)`, `PressAsync`, `TypeAsync`, `FocusAsync` and `BoundsAsync`, on the window `ActivateAsync` (a click on its title bar), `PressAsync`, `IsForegroundAsync`, `PartsAsync`, `TopLevelWindowsAsync` (every visible top-level window of the app's process tree with its menu items) and `DumpAsync`, `SparNativeDialog.ClickAsync(button)`, `OtherWindow` (a plain window to take the foreground), `ScreenCapture` (the screen as a PNG) and `SparBlackBoxOptions.OutlivesTest` (the app outside the kill-on-close job; choice 14). UI Automation gained `SetFocus` (slot 3) and `GetCurrentBoundingRectangle` (slot 43), checked by the slot-order test.
  - **`tests/Spar.SandboxTests`:** its build publishes `Spar.WindowTests` and `Notebook.EndToEndTests` with NativeAOT in-process into its `obj/sandbox-payloads/`; `SandboxSuite` runs the jobs once per test process; `GuestTests` reports each guest test and each job; `RunnerTests` checks the runner from the host.
  - **The checks:** `Spar.WindowTests/Sandbox/` (`SystemLaneTests`: the folders and the mark, real input refused as SYSTEM, no network, the guest's WebView2 registration and a Production app finding the Runtime as SYSTEM; `DeadlineProbeTests`; `FocusAndDialogTests`: the first click on an inactive window, Tab after a title-bar activation, the close confirmation with Enter and a click, "Wait / Close anyway" with clicks; `OfflineTests`: the page starts offline and still calls the backend; `SessionEndTests`: the sign-out) and `Notebook.EndToEndTests/Sandbox/` (`ProductionInputTests`: 25 browser and devtools keys and the two extra mouse buttons doing nothing, the text field's menu and no menu elsewhere; `CleanMachineTests`; `SystemLaneTests`).
  - **`Spar.slnx`** lists `Spar.BuildTests`, `Spar.PackageTests` and `Spar.SandboxTests` with `<Build Project="false" />`; `AGENTS.md`, the two projects' comments and CI's comments say so; CI has no Sandbox step, with a comment why.
  - Unit tests in `tests/Spar.Tests/SandboxRunnerTests.cs` (11); production-boundary markers; the static-state list; the `spar-testing` skill; pitfalls; `docs/design/testing.md` issue J; the plan.
- **Choices made while building, for review:**
  1. **The trait is `Sandbox`, with the lane as its value:** `System`, `UserSession`, `CleanMachine` (a fresh, offline sandbox of its own) and `SignOut` (a sandbox of its own that the test signs out); `Deadline` is the runner's own check. A Sandbox check is also `[Fact(Explicit = true)]` and skips unless `SandboxGuest` says it runs in a runner's guest, so ordinary runs leave it out on both lanes; `dotnet test` shows the explicit tests as skipped (10 in `Spar.WindowTests`, 4 in `Notebook.EndToEndTests`). The runner passes `-trait Sandbox=<lane> -explicit on`. Recommendation: keep.
  2. **One host result per guest test,** not one per job: a theory without discovery enumeration whose rows are `[<job>] <guest test>`, with the guest's outcome, message and stack trace, plus a row per job that fails when the job did not come back. Why: a failure is then one red test with its own message in any test UI and in the TRX, and a skip in the guest stays a skip; per job, one failure would hide the others' state. xUnit appends the row's number to each name. Recommendation: keep.
  3. **The allow variable is `SPAR_SANDBOX_DESKTOP=allow`,** the decision's example (the name says what it lets the run touch). Without it the user-session tests are listed from the payloads on the host (`-list tests/json`, which runs nothing) and reported as skips whose reason names the variable; the runner itself never reads it, the suite does. Recommendation: keep.
  4. **A runner's guest is known by three things:** the run's id in `SPAR_SANDBOX_RUN` (the guest script), the same id in `C:\spar\runner\run-id.txt` (written by the runner as SYSTEM once the sandbox is ready, on the guest's own disk), and, for the user-session lane, the user `WDAGUtilityAccount`. Real input, the screenshot and the second window refuse unless all three hold, before anything is sent; a unit test checks the refusal on this machine. Recommendation: keep.
  5. **`SparBlackBox.Placement` is `Activated` by default in a runner guest's user session,** the hidden desktop everywhere else, so a Sandbox check needs no setup. Recommendation: keep.
  6. **The run's folders reach the tests through two variables, read by an internal `TestArtifacts`;** a public results API waits for M3. The shared `Budgets.cs` (in `WireShapes.Tests`, which has no access to Spar.Testing's internals) reads `SPAR_TEST_RESULTS` itself, which CI already sets to the same folder. Recommendation: keep.
  7. **Results are xUnit's JSON reporter in `console.log`,** read per test (`test-starting` for the name, then its outcome by id); the TRX and xUnit XML are still written at the end, for tools. A test that started without an end is `Unfinished`. Recommendation: keep.
  8. **The payloads are published by `Spar.SandboxTests`' own build,** in-process (restore, then publish with `SparTestLane=Aot`), after its `Build`, on Windows only, into its `obj/sandbox-payloads/`, the folders named in assembly metadata. An unchanged build skips the native compile. Recommendation: keep.
  9. **In the guest each payload is copied to the guest's disk** (`C:\spar\work\<stage>\payload`, robocopy) and its data roots live there too; after each job the runner copies the job's `*.log` files back as SYSTEM, which is how the apps' logs (the sign-out's among them) reach the host. Recommendation: keep.
  10. **Deadlines:** 5 minutes for a SYSTEM job, 10 for a user-session job, 5 for the clean machine, 3 for the sign-out, 45 s for the deadline check; the kill is `taskkill /f /t /im <executable>` in the guest. Recommendation: keep; M4's budgets may tighten them.
  11. **Real input's shape:** a click at the middle of the element's UI Automation rectangle (physical pixels; the guest is at 96 DPI); `PressAsync` and `TypeAsync` on a locator first give the element the focus through UI Automation's `SetFocus` (no input), then send keys; the window's `PressAsync` sends to whatever has the focus; activation is a real click on the title bar. Text goes as Unicode input, so it does not depend on the guest's layout. Recommendation: keep.
  12. **`Notebook.EndToEndTests` sees Spar.Testing's internals** (`InternalsVisibleTo`), since its Sandbox checks use internal real input on its own Production executable. Recommendation: keep until real input is public in M3; the alternative was moving the Notebook's checks into `Spar.WindowTests`, which would build the Notebook as a second tested app there.
  13. **The Production input check presses each key with the "New page" dialog open and a title typed,** and counts as "nothing happened": the same top-level windows in the app's process tree (a devtools window or a print dialog adds one), the dialog's heading at the same place (zoom, reload or navigation move or remove it), the field's value unchanged, and none of "DevTools", "Print", "Find on page", "Save as", "Zoom", "Caret browsing" new in the UI Automation dump. 25 keys (devtools, print, find, source, save, open, caret browsing, reload, zoom, new window and tab, history) and the two extra mouse buttons; typing before and after shows the keyboard reaches the page. Recommendation: keep.
  14. **The sign-out is a job of its own,** per the spike: a fresh sandbox, the scene set and the data root noted before `shutdown /l`, 15 s of waiting, the logs collected, whether the sandbox and a user session are still there recorded, and the session end's 4 s checked on the host from the app's log. Its app is launched with the fixture's internal `OutlivesTest` (a job that tracks the app's processes without ending them when the test process ends), refused outside a runner's guest. Recommendation: keep; not yet verified (below).
  15. **A run lives under `artifacts/test-results/Spar.SandboxTests/<run>/`** (its staging folder deleted after the stop): the guest writes straight into the results, so nothing is copied. Recommendation: keep.
  16. **`wsb` is found on `PATH`** (the Windows Sandbox package's app execution alias); "available" is Windows 11 and `wsb --version` answering; anything else is one skip naming why. Recommendation: keep.
  17. **The runner repairs the guest's WebView2 registration** at each sandbox's start, as SYSTEM: `Clients\…\pv` and `ClientState\…\EBWebView` set to the Runtime folder on the guest's disk. Without it no app in the guest found a Runtime on 2026-11-19. It changes only the disposable guest, and a SYSTEM test checks it, a Production app finding the Runtime among it. Recommendation: keep while Windows Sandbox ships its base image's registration with the host's files.
  18. **The real input that is not on a locator,** the second window (`OtherWindow`) and the screenshot (`ScreenCapture`, Spar's own PNG writer over GDI) are internal to Spar.Testing too, behind the same guard: the guest has no Notepad for a second window, and a test must not capture a developer's screen. Recommendation: keep.
- **Numbers** (evidence `docs/evidence/m1b-sandbox.md`):
  - **SYSTEM lane:** every run passed. Five back to back took 53.4–54.3 s of `dotnet test` each, the sandbox alive 51.6–51.8 s, 45 s of it the deadline check. Per step: `wsb start` 0.99–1.12 s, ready as SYSTEM 0.33–0.36 s (first attempt every time), the marker 0.32–0.39 s, staging 7–13 ms, staged files seen at the first probe in 15 of 15 jobs (0.29–0.38 s), a SYSTEM job's run 0.74–0.98 s, log collection 0.29–0.38 s, stop 0.56–0.58 s. The deadline ended its job at 45.04–45.06 s, the guest kill took 0.34–0.42 s, and the finished tests came back from the stream with no TRX.
  - **User-session lane:** four runs, allowed by the user. Runs 1 and 2: no WebView2 Runtime in the guest (choice 17). Run 3: 25 of 32 passed. Run 4: 30 of 32 passed, in 146 s.
  - **Run 4's checks:** all 25 browser and devtools keys and both mouse history buttons "nothing happened" on the Production Notebook (28.3 s); the text field's menu exactly Cut, Copy, Paste, Select all, and no menu on a heading (3.6 s); the first click on an inactive window (2.2 s); Tab after a title-bar activation (1.1 s); the close confirmation with Enter and a click (2.3 s); the page offline with its calls working (7.7 s); the clean offline first run, ready in 3.5 s (4.0 s).
  - **The client window:** `wsb connect` until a user command runs took 2.5–3.5 s. The client took the host's foreground 3–7 times per run of three connects, each time minimized within 100 ms, and its 3–5 leftover windows per sandbox were closed in 63–70 ms. No sandbox was left after any run.
  - **The Production Notebook:** 5,235,712 B, built from runtime sources this step does not touch. The +1,024 B against step 8b's 5,234,688 B came from the folder fixes committed before it (inferred: the commit before them was not rebuilt). None of the new boundary markers is in it (`Spar.BuildTests` 6 of 6).
- **The sign-out spike's verdict** (`spikes/sandbox-signout`):
  - **Restart:** `shutdown /r /f` keeps the sandbox, which runs commands again about 18 s later.
  - **Shutdown:** `shutdown /s /f` ends it within 2 s; `shutdown /s` without `/f` fails with 1717 and does nothing.
  - **Sign-out:** a real `shutdown /l` keeps the sandbox, with mapped folders and SYSTEM commands still working, but no user session comes back on its own. It terminates the test process about 130 ms after it is asked (exit `0x40010004`), and with it the fixture's kill-on-close job ended the app before its session end: the app's log had no session-end line.
  - So the sign-out is a runner job of its own in a fresh sandbox, with the app outside the kill-on-close job (choice 14). The session end within 4 s on a real sign-out is still to be measured.
- **Found** (pitfalls):
  - the guest's stale WebView2 registration (choice 17);
  - the sign-out's order;
  - a page element below the fold cannot be clicked, only focused through UI Automation;
  - a window a background process shows can stay behind the foreground window;
  - the app's title-bar "System" menu item;
  - xUnit's JSON reporter streams per test;
  - explicit tests show as skipped in `dotnet test`;
  - a theory row's display name gets its argument appended;
  - `--` in an XML comment breaks a project or `Spar.slnx`;
  - `Environment.UserName` as SYSTEM in the guest is the machine account;
  - PowerShell output redirected under `wsb exec -r System` came back empty.
- **The `InstallPathTests` failure:** `The_window_works_from_an_install_folder_with_a_long_path` failed once in the first full CoreCLR window-suite run of this step, its message not captured. It then passed alone and in every later full run (see the final report's count). Not explained.
- **Not verified:**
  - the sign-out's session end with `OutlivesTest` and "Wait / Close anyway" with keys sent one at a time, both changed after run 4 and checked in run 5 (addendum below);
  - the page's `online` and `offline` events (the guest has no adapter to switch; a networked fresh sandbox with `netsh` in the guest was not tried);
  - the Development-only F5 and Ctrl+R (no Development executable in the payload);
  - a machine without Windows Sandbox (one skip, by reading);
  - hosted or self-hosted CI;
  - Windows 10, Linux, DPI other than 96.
- **Addendum: run 5 and two host fixes.** The coordinator ran the user-session lane once more with the user's allow (`docs/evidence/m1b-sandbox.md`, "Run 5"). 30 of 32 passed, "Wait / Close anyway" among them.
  - **The sign-out's session end took about 35 ms** by the app's log: "session ending at 4878.1 ms" to "services disposed at 4912.8 ms", both guards answering Close. The app now ran outside the kill-on-close job. The log showed two host defects, both now fixed:
    - **The session end's last lines were lost:** Windows ended the process before the queued writer reached the disk. `EndSession` now closes the log file synchronously before `WM_ENDSESSION` returns, bounded by what is left of the 4 s budget. This is new `IAppWindowOwner.SessionEndLogged(TimeSpan)`, `HostLog.Close(TimeSpan)` and `LogFile.Close(TimeSpan)`; a test window does nothing there. Other exits already close the file: `WindowsApp.Run`'s `finally`, and "Exit now".
    - **A browser lost during the session end was being recreated:** it died 17 ms in, and the host began a new environment. `BrowserView.EndRecovery()` now stops browser and renderer recovery when `EndSession` starts and when an approved close's teardown starts. Before, only the controller's close stopped it.
  - **Host tests:** `SessionEndHostTests.A_renderer_lost_during_the_session_end_is_not_reloaded` (Window surface) failed 3 of 3 without the fix. `SessionEndHostTests.The_session_ends_last_lines_are_in_the_log_when_Windows_ends_the_process_right_after` (black-box: the two messages sent to WindowHost's Production window, then the process ended) passes, but passed without the fix too: it guards the line, it does not reproduce the loss. `LogFileTests` gained the bounded close.
  - **The close confirmation's scene failed:** the guard's list still read "No guard" after its keys. The guard is now picked by walking the list's known order with real arrow keys: the window activated by its title bar first when it is not in the foreground, the list focused through UI Automation before every key, and each step read back. Choosing the option through UI Automation was tried on the host and does not work for Chromium's `<select>`: no options in the tree, and `SetValue` has no effect.
  - **Choices for review:**
    19. **The flush blocks the UI thread inside the session end's hold,** at most what is left of its 4 s. It is the window's last act before Windows may end the process. Recommendation: keep.
    20. **Recovery also stops at an approved close's teardown,** not only at session end, as the webview2-hosting skill's "no recovery during shutdown" says. Recommendation: keep.
  - **Found:** ending one test window's renderer (a single process, listed by its main frame) stopped another test window's page from answering: its session end held 3.0–3.1 s instead of about 0.1 s, in 5 of 6 full window-suite runs. `SessionEndHostTests` now runs in a collection with no other test at the same time, and the suite then passed 5 of 5. Why the pages are not separate on the test process's shared environment is not established.
  - **The Production Notebook** is 5,236,736 B after the two fixes (+1,024 B), within its 6 MB budget.
  - **Not verified:** the sign-out after the two fixes; the close confirmation after its scene's change. Run 5 was the last allowed user-session run.

## 2026-11-19, Invoke on the hidden desktop and the Notebook's size steps (investigation; implementation record)

Two findings left open by "The black-box fixture (implementation record; choices for review)" (Invoke takes about 2 s on the hidden desktop, not explained) and by "The Windows Sandbox runner (implementation record; choices for review)" (the Production Notebook's +1,024 B, inferred to come from the folder fixes). Spike: [`spikes/blackbox-invoke`](evidence/blackbox-invoke/RESULTS.md). Evidence: `docs/evidence/m1b-black-box.md`, "After 8c". Another agent ran window tests on the machine at the same time, so timings are compared only within one run.
- **Why Invoke took 2 s: UI Automation's `AutoSetFocus`.** A client object's `AutoSetFocus` is true by default, so `IUIAutomationInvokePattern::Invoke` first gives the element the focus, which brings its window to the foreground ([IUIAutomation2::AutoSetFocus](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomation2-get_autosetfocus)). A probe app whose page timestamps every event it sees, with the backend's clock beside it, showed: the page's window got the focus within about 20 ms and lost it again; nothing for 2 s; then the focus and, about 1 ms later, `pointerdown` … `click`; the call returned with the click, 2,019–2,068 ms after it started (8 launches, 80 actions, 20 of them `SetFocus` alone). `SetFocus` alone took the same 2.0 s; a connection timeout of 250 ms and a transaction timeout of 1 s (defaults 2 s and 20 s) and an STA thread that pumps messages did not change it. From a thread on that desktop `GetForegroundWindow` is null and `SetForegroundWindow` fails, so a wait for the foreground never ends early there (that UI Automation waits for exactly that is inferred). The page is not throttled there: 60 frames and 10 timer ticks of 100 ms per second, `visible`. With `AutoSetFocus` off, Invoke took 10–29 ms (median 15, 290 clicks) and the page got the same trusted events, `focusin` on the button first; `LegacyIAccessible.DoDefaultAction` took 12–35 ms whatever `AutoSetFocus` said.
- **Fixed in the fixture:** `AutomationThread` sets `put_AutoSetFocus(FALSE)` on each `CUIAutomation8` object it creates, the replacement after a stuck call included; `IUIAutomation2` is declared in `BlackBox/Interop/UiAutomation.cs` (slots 3–58 placeholders, slot 59 `PutAutoSetFocus`), checked by `UiAutomationSlotTests` (now 7 interfaces). No Production code changed.
- **Numbers:** an interleaved A/B on private copies of the same test outputs, differing only in `Spar.Testing.dll` (CoreCLR, 3 rounds): the Notebook's six black-box tests 15.8–16.6 s before, 9.4–14.6 s after; the fixture's thirteen (trait `Surface=BlackBox` in `Spar.WindowTests`) 21.2–26.4 s before, 13.4–13.6 s after. Per test (medians): the Notebook's Invoke and dialog 2,757 → 723 ms, its failed relaunch 7,757 → 5,803 ms, its profile-folder failure 2,708 → 699 ms; the fixture's window test 5,702 → 1,554 ms. A native dialog's `PressAsync` is an Invoke too, so every test that answers one lost about 2 s. Then both suites 10 more times: 190 tests, 0 failures (Notebook 9.4–11.5 s, fixture 12.6–13.5 s). NativeAOT: `Notebook.EndToEndTests` published with no warning, its black-box tests 4 of 4 runs, 8.9–9.6 s (15.1 s median recorded before). `Spar.BuildTests` 6 of 6; `eng/check-size.cs` 0 errors.
- **The Notebook's size steps: two commits, each its code rounded to 512 B blocks.** Built in a `git worktree` per commit (`dotnet publish samples/Notebook -c Release`): 6c6252e 5,234,688 B; e4d8885 5,235,712 B; febdd1c 5,236,736 B, the recorded sizes exactly. The image's file alignment is 512 B. e4d8885 (the folder fixes, its only runtime change) added 1,240 B of code and data: `.text` +416 B and `.data` +448 B each crossed a block boundary (+512 B each), `.rdata` +288 B, `.pdata` +60 B and `.reloc` +28 B fitted in their last blocks. febdd1c (the session end's log close and the recovery stop) added 836 B: `.text` +416 B and `.rdata` +176 B crossed a boundary. So the earlier inference holds, and both steps are real growth that the file shows in whole blocks. The PDB's absolute path is in the image (125 characters in the worktrees, 99 in this checkout) and did not change a size here; a longer checkout path could.
- **Choices made, for review:**
  1. **`AutoSetFocus` off on every client object, in every placement** (hidden desktop, shown without activation, activated in the Sandbox guest): Invoke is documented as no input, so it moves neither focus nor foreground. Alternative: off only on the hidden desktop. Recommendation: keep; no Sandbox check calls Invoke, and real input activates the window explicitly.
  2. **`IUIAutomation2` declared on its own with placeholders** rather than as a derived interface of `IUIAutomation`, which would need `IUIAutomation`'s slots 29–57 declared and the slot-order test taught inheritance. Recommendation: keep.
  3. **The object is reached by casting the `IUIAutomation` wrapper** (a `QueryInterface`), called once per object. Recommendation: keep.
  4. **No test pins Invoke's time.** The suites' durations show it. Recommendation: add a bound (for example Invoke under 500 ms on the hidden desktop) with M4's performance budgets.
- **Found** (pitfalls): `AutoSetFocus` belongs to one client object, the connection and transaction timeouts to the whole process; an object with `AutoSetFocus` off still set the focus and waited 2 s in 2 of 10 and 2 of 20 dialog openings while a second, default object of the process searched at the same time (0 of 20 with both off), which is why every object the fixture creates gets it; a .NET thread started as STA cannot move to another desktop (`ERROR_BUSY`), so the spike's STA thread starts MTA, uninitializes, moves, then initializes STA. **Spike only, not explained:** in 4 of 11 kept launches of the focus-setting variants whose dialog watcher also set the focus, `WindowPattern.Close` right after closing the page's dialog never reached the host (no close request in its log, the window still there, no dialog); 0 of 57 kept launches with `AutoSetFocus` off, and none in the fixture's runs.
- **Needs the user's decision:** choice 4 (a bound now or with M4); whether the spike-only lost close request is worth more time. **Needs the user at the PC:** why Invoke took 0.5–1.1 s on the input desktop, and whether `AutoSetFocus` was what took the foreground in the earlier spike's fallback (`SW_SHOWNOACTIVATE`), since both need a launch on the user's desktop.
- **Not verified:** `Spar.WindowTests` on NativeAOT (another agent's test run held its output folder; the cast is checked under NativeAOT by the Notebook's suite); the input desktop; the Sandbox lanes; Windows 10.

## 2026-11-19, Renderer sharing between test windows and the long-path failure (investigation; implementation record)

Two window-test behaviours left unexplained by "The Windows Sandbox runner (implementation record; choices for review)": a quick session end held 3.0–3.1 s in the full suite (put down to another window's renderer being ended, so `SessionEndHostTests` ran in a non-parallel collection), and one failure of `InstallPathTests` with no message kept. Spike: [`spikes/renderer-sharing`](evidence/renderer-sharing/RESULTS.md). Windows 11, Runtime 157.0.4341.60, CoreCLR Debug.
- **Test windows do not share a renderer.** With 2, 4 and 8 test windows open at once, each had one renderer of its own by its main frame (`GetProcessExtendedInfos`), no pid shared; the profile root had one renderer more, the anchor's. Ending window 0's renderer left every other page answering an evaluation in 0.5–0.8 ms with the same renderer pid, and only window 0's log had "renderer exited" (2, 4 and 8 windows). With `spikes/webview-profiles`' host, four windows on `https://app.localhost` in one folder kept four renderers under four distinct InPrivate names even with `--renderer-process-limit=1`, and shared one with that limit on one InPrivate name or on the default profile (3 of 3 each): Chromium shares a renderer only within one profile, once its renderer limit is reached. Spar's test windows always have distinct names among the windows open at once, so the leftover-renderer check and a test's renderer kill are safe, and no browser argument or profile change is needed (none was costed against the 700 ms budget: the change below costs nothing per test).
- **The cause of the 3 s: nested session-end holds on the shared UI thread.** A window's session end holds `WM_ENDSESSION` in a message loop of its own (`MessagePump.Until`); a second window's `WM_ENDSESSION` sent meanwhile on the same thread is dispatched inside that loop, and the outer handler cannot return before the inner one. In the probe, a quick session end (17–52 ms alone) held 2,052–2,083 ms under another window's 2 s hold and 3,071–3,092 ms under one whose renderer had died (3 of 3 each); sent the other way round it stayed 17–48 ms (9 of 9). In the full suite's run where both tests logged their times, the quick test's message went about 20 ms before the renderer test's.
- **Why that one took 3 s: a renderer that died during a close was waited for.** With recovery off (decision above, choice 20), `ProcessFailed` with `RenderProcessExited` only logged "nothing is recovered", so the page's document stayed open: the page guard's request waited its 2 s and the close work its 1 s (3,021–3,072 ms, 9 of 9).
- **Fixed:**
  - **The host** (`BrowserView.OnProcessFailed`, `WebViewBridge.OnRendererExited`): during an approved close or a session end, a renderer or browser exit now ends the page's document ("closed (renderer exited)"), so the close request goes on without the page's answer and its close work is not waited for, as the two-stage close already does for a document that goes away mid-request. Recovery stays off. The probe's session end with its renderer ended took 326–395 ms (9 of 9). A renderer exit outside a close now also logs "closed (renderer exited)" instead of "(navigation)".
  - **The tests:** the three session-end tests of `HostTests` moved into `SessionEndHostTests`, one class, so they never overlap; its `DisableParallelization` collection is gone, so the rest of the suite runs beside it. The renderer test (renamed `A_renderer_lost_during_the_session_end_is_neither_reloaded_nor_waited_for`) held 331–461 ms in 40 suite runs. A first version that asserted the whole hold under 1.5 s failed once in a solution-wide `dotnet test` (1,792 ms; 388–1,792 ms in 8 such runs): with every test project running at once, WebView2 reported the lost renderer 11–872 ms after the kill (4 runs with log times; 4–5 ms on an idle machine), and the window ended 20–61 ms after the report. The test now checks, by the host log's times, that the window is destroyed within 500 ms of the report, with "closed (renderer exited)" and neither "did not answer within" nor "page close work timed out"; with the host change taken out it failed 3 of 3 (holds 3,067–3,087 ms).
  - **`InstallPathTests`:** every child's output is kept (`install-path/<time>-<pid>.log` and a line in `install-path.txt` under the project's results), the failure message names that file and carries the output, a child that outlives the 2-minute limit is reported with what it wrote (before: a bare `OperationCanceledException`), and the copy's deletion retries for up to 15 s and never hides the test's own failure (before: a delete that threw in `finally` replaced it).
- **The long-path failure did not reproduce:** 50 full runs passed (below); the child took 2,522–3,311 ms, once 7,784 and once 8,351 ms (a 5 s stall in its first window, the known unexplained 5 s wait); the copy was deleted at the first attempt every time. Its cause is unknown; the next failure keeps its message and the child's output.
- **Verified:** the full window suite (106 tests) 30 times as the test executable and 10 times through `dotnet test --project tests/Spar.WindowTests -c Debug --no-build`, 0 failures in 40 runs, then 10 more runs of the executable with the renderer test's final checks, 0 failures (`spikes/renderer-sharing/results/suite-runs.txt`); per run the quick session end held 54–109 ms, the renderer test's 331–461 ms and the slow one 3,999–4,058 ms; a window test 302–317 ms median (budget 700). `dotnet test --solution Spar.slnx -c Debug`: 947 of 949 passed; the two failures are the static-state check (in `Spar.Tests` and `WireShapes.EndToEndTests`) for a new `IUIAutomation2` field in `Spar.Testing`'s black-box interop, from separate uncommitted work in the same tree, not this change. In two of seven `--no-build` repeats `Notebook.EndToEndTests.JourneyTests.A_taken_or_invalid_title_is_explained_in_the_form` (Browser surface, not the WebView2 host) timed out at 30 s; not investigated. `Spar.BuildTests` 6 of 6; `eng/check-size.cs` clean. The Production Notebook's size after the change was not measured (the host change is a few lines in `BrowserView` and `WebViewBridge`).
- **Choices for review:**
  1. **Session-end tests share one class rather than a collection.** A collection across classes would do the same; one class keeps the reason next to the tests. Recommendation: keep.
  2. **The document ends on a browser exit too, not only a renderer exit,** during a close: the page goes with the browser. Recommendation: keep.
  3. **The probe is named `Spar.WindowTests`** to reach the internals given to that project; it is a spike and never built with the solution. Recommendation: keep while the spike exists.
- **Needs the user's decision:**
  - **M3, several windows on one UI thread:** (a) handle the session end once per thread (one hold for all windows) rather than per window, since nested holds lengthen each other (whether Windows itself sends the windows' messages one after another is not known); (b) a hung page's "Reload page" and "Close anyway" end renderers, and today the Production path ends every renderer of the environment, which would end every window's page; ending only the window's own (by main frame, as test windows do) is the likely choice, but a renderer shared by two windows of the app's profile, once the limit is reached, still takes both. Recorded in the plan's M3 list.
  - **Keeping every test run's report:** the failure's message was lost because neither `dotnet test` here nor CI keeps a report by default (xUnit's `-result-trx` and the test platform's report options exist). Proposed: CI and the documented commands keep a TRX report per test step under `artifacts/test-results/`; not done here.
- **Not verified:** Windows 10, other Runtimes, a small-memory machine (where an app's windows on one profile would share renderers without any argument; inferred); Windows' own order of `WM_ENDSESSION` to several windows of one thread; the window suite on NativeAOT after the change (only CoreCLR was run; both lanes share the host code); the cause of the long-path failure.

## 2026-11-19, Review of the two investigations: what is still unexplained (implementation record)

Rerun after "Invoke on the hidden desktop and the Notebook's size steps" and "Renderer sharing between test windows and the long-path failure":
- **Static state:** the new `IUIAutomation2` interface's generated IID field was missing from `tests/static-state/reviewed.txt`, so the solution run failed `Every_static_field_of_Spars_assemblies_is_reviewed`; added, as for the other UI Automation interfaces.
- **Runs:** the solution run (Window and BlackBox traits left out) 3 of 3 with 865 tests and no failure after that; the NativeAOT window suite 25 times (106 tests each): 24 clean, **1 run with 1 failure whose message was not kept** (the first three runs were filtered to their totals); the Notebook's NativeAOT end-to-end executable 3 of 3; the Production Notebook 5,236,736 B, unchanged by the two investigations.
- **Still unexplained, seen once each:**
  - **The NativeAOT window failure above.** It did not recur in the 22 runs whose full output was kept.
  - **A Notebook browser journey under the full solution run:** `JourneyTests.Typing_saves_by_itself_and_the_preview_follows` failed after 97 ms in the first solution run after a rebuild (its message not kept), and the renderer investigation saw `A_taken_or_invalid_title_is_explained_in_the_form` time out at 30 s in 2 of 7 `--no-build` solution runs. The journeys passed 5 of 5 alone and in 3 later solution runs. Both are the Browser surface (loopback and the browser automation library), not the WebView2 host.
- **What would make them observable:** keeping each run's TRX report (the renderer investigation's suggestion), in CI and in the documented commands, so a one-off failure keeps its message.

## Every test run keeps a TRX report (2026-11-19)

- **Why:** two failures in the investigations above (one NativeAOT window test, one Notebook browser journey) were seen once each and lost their messages: neither CI nor the documented commands kept a report, and the console output had been filtered to totals. The user chose to keep a TRX report of every run.
- **How:** `--report-xunit-trx` sits beside `--results-directory` in the six places that set it (Spar.Sdk's source-mode test defaults in `src/Spar.Sdk/Sdk/Sdk.targets`, and the five `Microsoft.NET.Sdk` test projects). It is built into xUnit v3 (4.0.3) under Microsoft.Testing.Platform, so no package is added. xUnit names the file `<user>_<machine>_<UTC time>.trx`, one per run, so a later passing run never overwrites a failure; the TRX itself records the same user and machine names, and it stays in the disposable `artifacts/test-results/<project>/`. The NativeAOT lane's executables use xUnit's own runner, which writes a report only when asked: CI and `eng/wsl/step.sh` run them with `-result-trx artifacts/test-results/<project>/aot-<UTC time>.trx`. The Sandbox runner already kept its guest's `results.trx`; its host project now writes one too. Consumer apps outside this repository are unchanged (the setting is source-mode only).
- **Checked:** a build and the CI test steps (the solution run 865 tests, `Spar.WindowTests` 106, the Notebook's window tests 10, no failures): every test project wrote one new report, and no `TestResults/` folder was left. A one-test probe project outside the repository whose test writes a line of output and fails: its TRX holds both the failure message and the output line. `Spar.Tests`' NativeAOT executable with `-result-trx`: the report has 46 passed and 2 not executed, as its console summary.
- **Size:** one full local run adds about 1.9 MB under `artifacts/test-results/` (11 TRX files, the largest `Spar.Tests`' at 0.7 MB; 11 window screenshots; text logs). CI uploads only that folder, has no caches, and now keeps each upload 14 days (`retention-days`) instead of the repository default of 90. Locally the reports accumulate until `artifacts/` is cleared; the Sandbox runs (33 MB here) are the larger part.

## Black-box tests on CI: the runner's own desktop (2026-11-19, for the user's review)

- **What CI showed (PR #2, first runs of steps 8b and 8c on GitHub Actions):** every black-box launch that needs the app ready failed on the Windows runners, in both lanes and in the package tests' consumer. In `Spar.WindowTests` the app's `CreateCoreWebView2Controller` failed with `0x80070578` (invalid window handle) and the launch timed out without `ui-ready`. In the package tests' consumer the app was ready, then UI Automation never found its page's heading within 10 s. Tests without a WebView passed, and so did the in-process window tests on the runner's desktop. The TRX reports of the decision above held each message.
- **A probe on the runner** (a temporary CI step, removed again): the runner is an interactive session (session 2, user `runneradmin`, `WinSta0` with `WSF_VISIBLE`, input desktop `Default`; Windows Server 2025, WebView2 Runtime 156.0.4314.58). WindowHost's Production executable was ready in 1,746 ms on the runner's desktop, and failed with `0x80070578` on a desktop created in `WinSta0`, both when `STARTUPINFO.lpDesktop` named `WinSta0\spar-probe` and when it named only `spar-probe`. On this PC (Windows 11, Runtime 157) the same new desktop works (decision "The black-box fixture", 50 of 50 launches).
- **Decision (taken under the PR's Auto-fix, for the user's review):** when the test process runs on a CI service, `SparBlackBox.Placement` defaults to `ShownWithoutActivation` on the runner's own desktop: `CI` set to `true` or `1` (GitHub Actions, GitLab and most others) or `TF_BUILD` set to `true` (Azure Pipelines). The hidden desktop is there to protect a person at the screen, and a CI runner has none; the shown placement is the one decision "Step 8b's shape" named as the fallback. A developer's machine keeps the hidden desktop, and the Sandbox runner keeps its own placements. Alternatives not taken: choosing by the OS edition (it is not known that Windows Server, rather than the Runtime build, is the cause), relaunching on the shared desktop after a failure (it hides the failure), and skipping black-box tests on CI (the Production executable would be untested there). Checked by a test of the variable rules; on CI by the next run.
- **Also fixed:** the fixture names the hidden desktop with its window station (`<station>\spar-black-box-<pid>`). A desktop name alone is resolved by the window-station connection rules, so a test process in another station than the user's (a service-like station made for the check, `Service-0x0-…$`) had its app connect to `WinSta0`, where that desktop does not exist, and end at once with `0xC0000142`; with the full name the app there was ready in 310–383 ms. In that station UI Automation then found no window, so a test process outside `WinSta0` still cannot run black-box tests (not needed today: CI runs in `WinSta0`). `Spar.Tests` references the WebView2 package itself where Spar.Sdk does not (Linux), so the slot-order test finds `WebView2.h` on every OS.

## M1b step 9: the deferred checks (2026-11-19; one user decision)

Spike: `spikes/webview2-flags/` (background agent; WebView2 Runtime 157.0.4341.60, CoreCLR with the managed API, every WebView on a desktop of the driver's own; raw net logs disposable under `artifacts/`). The arguments went through `ICoreWebView2EnvironmentOptions::get_AdditionalBrowserArguments`, as Spar's `EnvironmentOptions` passes them.

- **`--host-resolver-rules="MAP app.localhost ~NOTFOUND"` through the options object works,** quoted exactly so, before or after `--disable-blink-features=FileSystemAccessLocal`: 0 loopback connections in 253 runs, against 16 per run (12–18, 3 navigations, all to `[::1]:443`) without it; unquoted, the browser keeps only `MAP`. First Navigate to page-ready 53.5 against 54.2 ms median (25 runs each; three more pairs of 60 matched). The rare 5 s first-load stalls (the existing pitfall) looked more frequent with it (8 of 304 against 2 of 255, p ≈ 0.12), so a larger sample alternated the variants run by run on fresh profiles: 5 of 400 with it, 4 of 400 without (p = 1.00; pooled 13 of 704 against 6 of 655, p = 0.17). In 8 of those 9 the document's request came about 5 s after `Navigate`; the cause is still unknown. **Built:** the host passes the rule always, before the pickers argument (`BrowserEnvironment.NoAppHostResolution`), for apps and test windows alike; the page-picker opt-in drops only the pickers argument. A window test counts connections on `[::1]:443` and `127.0.0.1:443` across two navigations and expects none; with the rule taken out it saw 4. The Production Notebook is 5,237,248 B, one 512 B step more than before. Checked: the solution run (865), the window suite on CoreCLR and NativeAOT (116 each), the Notebook's window (10) and black-box (6) tests, and the build tests with the production boundary (6).
- **SmartScreen:** with `IsReputationCheckingRequired = false`, no net log named a SmartScreen host (7 of 7, 4 on fresh profiles). With it on, each fresh profile sent 4 `nav-edge.smartscreen.microsoft.com/.../navigate/3` (one per document, frames included), 1 `data-edge` and 1 `telem-edge` request. The only other Microsoft endpoint was `config.edge.skype.com`, 2 GETs on a profile's first run either way. Decision "SmartScreen default in WebView2" stands, and its "Not verified" item is closed.
- **`--js-flags=--noexpose_wasm` has no effect** in WebView2: `WebAssembly` stays an object in the page, a same-origin frame and dedicated, shared and service workers; `--no-expose-wasm` and `--jitless` do not remove it either. Without `'wasm-unsafe-eval'` every instantiation is refused in all five; with it, all five instantiate. No cost either way (54.4 against 54.2 ms).
  - **User decision (asked 2026-11-19):** accept and document it. Windows keeps the global with every module refused; Linux has no global. The difference is in `AllowWebAssembly()`'s documentation (code should try to compile rather than test the global) and in plan section 3b. A window test on WindowHost checks the refusal in the page and in a dedicated and a shared worker, each refusal reported as a `script-src` violation. Not taken: deleting the global with the injected script (workers keep it, no gain over the policy), and searching further for an engine switch.
- **The find API:** `ICoreWebView2_28::get_Find`, `ICoreWebView2Environment15::CreateFindOptions`, `ICoreWebView2Find` and `ICoreWebView2FindOptions`, stable since Runtime 139.0.3405.78 by Microsoft's release notes for SDK 1.0.3405.78, which is Spar's minimum: no fallback is needed. On 157 with `SuppressDefaultFindDialog` set, match counts were exact (8, 6, 7 and 0), next and previous moved and wrapped, stop worked, and no window appeared; unsuppressed, the browser's find bar opened as its own popup window, so `FindInPage()` always suppresses it. Runtime 139 itself was not run: Microsoft's download page offers Fixed Version Runtimes 153–157 only, so nothing was downloaded.
- **Not tested:** the cause of the 5 s stalls (an ETW trace of a stalled run is the next step), Ctrl+F, find in PDFs, Windows 10, the spike under NativeAOT (the arguments take the same options interface), and Runtime 139.

## M1b budgets measured at the exit (2026-11-19)

- **Measured** (`docs/evidence/m1b-budgets.md`; `tests/Spar.WindowTests/BudgetTests.cs`, which records and never asserts), Release, both test lanes, WebView2 157 on the development PC:
  - **Launch to `ui-ready`, warm (≤ 400 ms):** WindowHost's Production NativeAOT executable through the black-box fixture, the profile reused after one warm-up launch, from the process's creation: 359.5 ms (338.1–390.2, n = 10) and 375.7 ms (346.8–430.2, n = 10). Met, with the least margin of the M1b budgets: the spike measured 261–370 ms for a bare React page, and M1b step 2 about 310–345 ms; the host has gained the security defaults, the injected script and the log since. A first measurement gave each launch a new data root (397.8 ms), which measures a first start; the test reuses the root.
  - **Small unary round trip on the desktop bridge (≤ 1 ms):** timed by the page over 200 echoes: 0.10 ms median, p95 0.20 ms, in both lanes (the page timer's resolution is 0.1 ms).
  - **App process working set (≤ 60 MB):** 42.3 MB (39.8–42.5) 2 s after `ui-ready`; with the browser's processes about 358 MB.
  - **Window test (≤ 700 ms):** 324.3 ms CoreCLR, 342.6 ms NativeAOT.
  - **Notebook NativeAOT executable (≤ 6 MB):** 5,237,248 B.
- **Not measured here:** the launch of the Notebook itself (WindowHost is the measured app: its page is a Vite bundle with the SDK, as the Notebook's), a cold start after a reboot, and other machines; M4 sets the real budgets.

## M1c's first questions (user decisions, 2026-11-19)

- **Software GPU detection: spike the methods first.** A spike measures the accuracy and start-up cost of a throwaway EGL display reading `GL_RENDERER` (llvmpipe, softpipe, lavapipe, SwiftShader), a `/dev/dri` render-node access check, and both combined, in WSLg (llvmpipe and, where present, its D3D12 driver), a container and a real GPU if one is reachable; the method is chosen from its numbers.
- **The option: `spar.Browser(b => b.HardwareAcceleration(HardwareAcceleration.Always))`,** with `Auto` (the default: acceleration off and cairo on a software GPU, the decision of 2026-11-14), `Always` and `Never` (also for a buggy real driver). Linux only at first, and documented as such. It is not an `Allow…` method: it is not a security opt-in.
- **`connect-src` on Linux: `spar://app`, named explicitly as on Windows, after a check** in WebKitGTK 2.52 and 2.54 that `fetch` to `spar://app` works under it and that `ws:`/`wss:` to any host are refused.
- **Linux window tests: Spar ships the Dockerfile only** (CI builds the image; publishing one waits for releases, M4). They run under Xvfb in a container or in WSL, and **WSLg is allowed for local runs**, where the windows show on the Windows desktop. For agents that is real windows and input on the user's desktop, so `AGENTS.md`'s rule applies: ask first whether the user is using the monitor.

## Sandbox run 6: 31 of 32; the sign-out's last line, and the window kept at session end (2026-11-19)

- **Run** (user-allowed, `docs/evidence/m1b-sandbox.md`, "Run 6"): 31 of 32 passed, including the Production input checks (browser keys and the cut-down context menu on the Production executable, which M1b step 5 left), focus, the first click, and the close confirmation after run 5's change. The sign-out failed only on its final log line: the session end itself took 21 ms from `session ending` to `services disposed`, inside the 4 s budget.
- **Cause, as the log reads it:** Windows ended the process within 20 ms of the host destroying the window that held `WM_ENDSESSION`, before the handler wrote its last line and flushed the log.
- **Change (choice for review):** at session end the teardown keeps the window; `WM_ENDSESSION` returns after the page, the controller, the services, the final line and the log's flush, and a message of the window's own destroys it afterwards if the process is still there. Plan section 3's teardown order is unchanged outside session end. Locally the window suite passes on both lanes; whether a real sign-out now keeps the final line needs one more allowed user-session run.

## Sandbox run 7: the sign-out confirmed; a one-off in the first click's setup (2026-11-19)

- **The sign-out passed** with the window kept until `WM_ENDSESSION` returns: every session-end line reached the log, the last `WM_ENDSESSION held 45 ms` (`docs/evidence/m1b-sandbox.md`, "Run 7"). The change of "Sandbox run 6" stands; M1b step 8's "a real sign-out" is done.
- **One failure, in a check's setup:** the first-click test's helper window, clicked with real input, did not take the foreground within 5 s (31 of 32 passed; the same check passed in run 6, and the Tab test with the same setup passed in run 7). Its cause is not known; both tests now record the foreground window and a screenshot when it happens. With runs 4–7, M1b's user-session checks have each passed at least once on the current host: the Production input checks, focus, the first click, Tab after activation, the close confirmation, the sign-out and the clean machine.

## The Linux host, first slice (implementation record; choices for review; 2026-11-19)

Plan M1c, build step 1: the host over WebKitGTK 6.0 with the main-loop thread, the `spar://app` scheme, the bridge through a relay in an isolated script world, document scoping, the teardown behind the close request with the loop kept until the web processes exit, and recovery on `web-process-terminated`; foreign frames tested against the bridge. Evidence: `docs/evidence/m1c-host.md`.
- **Built:** `src/Spar.Linux` (GLib, GObject, GIO, GTK 4, WebKitGTK 6.0, JavaScriptCore and libsoup 3 through `[LibraryImport]`, loaded at the first call; signal handlers and the scheme callback are `[UnmanagedCallersOnly]` with a `GCHandle` as user data, freed once the loop has ended): `MainLoop` (one thread, `GMainLoop` on the default context, a `SynchronizationContext` posting through `g_idle_add_full`, one source per burst, the stall probe's named scopes), `SchemeServer`, `ScriptBridge` with `BridgeRelay.js`, `BrowserView`, `AppWindow` with its close, `AlertDialog` (`GtkAlertDialog` without a nested loop), `WebProcesses`, `LinuxApp` and `SparLinuxHost`. `Spar.Sdk` adds `Spar.Linux` for `linux` in `SparPlatforms` (by reference from source, as a package otherwise), and the generator hands it to `app.Run()` behind `OperatingSystem.IsLinux()`. The SDK's `webKitChannel()`, chosen by `hostChannel()` on `spar://app`. Tests: `LinuxHostUnitTests` everywhere; `LinuxHostTests` in a real window on Linux with a runner's X server (four tests, and the Notebook's numbers); the bridge probe app `tests/apps/BridgeProbe`.
- **Shared with the Windows host, moved into the core** so neither host duplicates them: `HostLog` (now `Spar.Hosting.HostLog`; the process-age helper stays per host), `PageResponder` (what the origin answers: the page from memory, 404, 405, the style nonce, the policy composed off the UI thread; the Windows `PageServer` now uses it), `CallbackScope` next to the stall probe, and `SparFrontend.IsImageMemory`. `ReadyLineTests` now runs on every OS.
- **Numbers** (WSL2 Ubuntu 24.04, WebKitGTK 2.52.6, Xvfb, llvmpipe): the Notebook as one NativeAOT executable of 4,676,160 B (budget 8 MB), linking `libc` and `libm` only, with no Windows host in it; launch to `ui-ready` median 211.7 and 210.4 ms in two runs of 11 with `GSK_RENDERER=cairo` and `WEBKIT_DISABLE_DMABUF_RENDERER=1`, and 577.0 and 581.6 ms as built (budget 350 ms); close request to exit 33–35 ms (530–540 ms as built); total PSS 233 MB (504 MB as built); round trip, page-timed mean of 1,000, 0.263 and 0.231 ms in two runs of the Production NativeAOT probe; recovery 102 ms from the loss to the reloaded page's `ui-ready`; web processes gone 17–49 ms after the window was destroyed (510–545 ms as built).
- **Choices made while building, for review:**
  - **The relay's meeting point:** the relay runs in the isolated world `spar` (the only world with the handler `spar`), injected at document start into the top frame for `spar://app/*` only, and checks `location` itself. It meets the page's SDK on the top window as DOM events with one string as `detail`: the page dispatches `spar:send`, the relay posts it; the host evaluates `sparRelay.deliver([...])` in the `spar` world, which dispatches `spar:receive`. `sparRelay` is a `const` of that world, so nothing is defined on the page's window. Any script of the top document can dispatch `spar:send`, as any script can call `chrome.webview.postMessage` on Windows; a frame of another origin cannot reach the top window, and has neither relay nor handler (tested).
  - **The page side picks the transport by its address:** `hostChannel()` takes WebView2's port when there is one, the relay's channel when `location.protocol` is `spar:`, loopback otherwise. The relay sets no marker the page could test; on `spar://app` it is always there.
  - **Document epochs:** a document closes at `WEBKIT_LOAD_STARTED` and the next opens at `WEBKIT_LOAD_COMMITTED`, only when the committed address is on `spar://app` (a foreign top document gets no session). Messages between the two are dropped, as on Windows.
  - **Messages wait for the services on the reader, not the window for the services:** the view is created, attached, loading and presented at once (plan 3's order); a document's `BridgeSession` is created by the reader for its first message, after the services are built, so the page may connect while they are still being built. Windows waits for the bridge before navigating; Linux does not.
  - **Host to page:** each message is encoded as a JavaScript string literal where it is produced (off the loop; quotes, backslashes, control characters, U+2028, U+2029 and lone surrogates escaped, so the script is valid UTF-8); the loop checks the document and hands what queued up in one turn to one `evaluate_javascript`.
  - **Scheme responses:** a file stored in the executable is handed to WebKit in place (`g_bytes_new_static` over the image), anything else (a decoded file, a document with a nonce) as a copy; a request before the page is prepared is held with a reference and answered when it is; `report-uri` posts are answered 204 and not read until step 4. The scheme is registered secure and CORS-enabled on the view's context, the process's default one.
  - **Recovery as on Windows:** one silent reload after a lost web process (crash or memory limit), given back at the next `ui-ready`; a second loss before it is exit 5 with a `GtkAlertDialog` (none in the automated mode). A process the host ends itself (`TERMINATED_BY_API`, step 3) is not recovered here. The skill's "visible message and recovery" is read as Windows' accepted policy (decision "The Windows host's first-slice choices accepted").
  - **The wait for the web processes:** after the window is destroyed the loop keeps running while the app's descendants named `WebKit*` exist, polled every 5 ms off the loop through `/proc/<pid>/task/*/children`, for at most 3 s; then a warning names what is left. The app's own child processes are not waited for.
  - **The close request:** the window manager's close (`close-request`, answered `TRUE`) is a `CloseReason.User` request to the backend's guard, with "Close anyway / Keep open" in a `GtkAlertDialog` for an ask. The page's guard waits for the heartbeat that carries it (step 3), as on Windows' first slice.
  - **Folders:** the network session's data folder is `SparFolders.Profile`, still named `WebView2` on Linux, and its cache `cache/WebKit` under the data root. Renaming the profile per OS changes `SparFolders` and its tests, so it is left for step 4, which passes the folders explicitly by design.
  - **What step 1 already checks of step 2:** no display is exit 6 (from `gtk_init_check`), and a data folder or window that cannot be created is exit 70, each with a line on standard error and in the log; no dialog yet.
  - **Linux host tests run from outside, for now:** the bridge probe as a separate process, its log read by the test, the window manager's close sent as a `WM_DELETE_WINDOW` client message through libX11 (`X11Close`), a web process ended with SIGKILL. They skip unless the runner names the probe (`SPAR_LINUX_HOST_APP`) and an X server of its own (`SPAR_LINUX_TEST_DISPLAY`), so CI and WSLg never open a window by accident. Step 7's Window surface replaces the runner. `Spar.Tests` now allows unsafe code for the X11 import.
  - **The package test** publishes for the platform the current OS lacks (`linux` on Windows, `windows` on Linux) to keep checking "no host for this OS" now that a Linux build has a host.
- **Deviations from the design:** none in behaviour beyond the points above. Not built: the page's close guard and the heartbeat (step 3), the session end through logind, the start-up checks, single instance and theme (step 2), the navigation lock, permissions and page security (step 4).
- **Found:** on a software GPU with the defaults (GTK's GL renderer, WebKit's acceleration), presenting the first window holds the main loop about 320 ms, and the web processes take 510–545 ms to exit; with step 2's software-GPU settings neither happens (pitfalls, "Linux host"). The `connect-src spar://app` value is the one `spikes/linux-gpu-csp` confirmed.
- **Not verified:** a real desktop, Wayland, a real GPU, HiDPI and IME; the dialogs (never shown in these runs); service and shared workers on `spar://app`; two windows in one process (the scheme is registered once per context).

## M1c: the software-GPU detection method and `connect-src` on Linux (2026-11-19; from the spike's numbers, for the user's review)

Spike: `spikes/linux-gpu-csp/` (background agent; WSL Ubuntu 24.04 with WebKitGTK 2.52.6, Ubuntu 24.04 and Fedora 43 containers with 2.52.6 and 2.54.2; NativeAOT probe, 0 warnings). The user left the method to the spike's numbers (decision "M1c's first questions").

- **Detection: the render node first, then EGL, with a virtual-driver filter.** No `/dev/dri/renderD*` the user can open means software (0.03–0.17 ms warm; the case in WSL as the user is set up). When one is open, `drmGetVersion` names its driver, and `vgem` or `vkms` count as software; otherwise a surfaceless EGL display with a GLES context reads `GL_RENDERER` (llvmpipe, softpipe, lavapipe, SwiftShader and the like mean software): about 21 ms warm, cold 31–114 ms, of which GTK reuses most (EGL then `gtk_init` cost only 8 ms more than `gtk_init` alone). Without a context the EGL driver name says `swrast` even on D3D12, so it cannot decide. GDK's own GL context was not taken: it needs `gtk_init` first, which leaves 71 threads running, after which `setenv` is not safe. In every environment all three methods said "software"; with `GALLIUM_DRIVER=d3d12` forced, EGL read `D3D12 (Intel(R) UHD Graphics 770)` (through `/dev/dxg`, which WebKit's sandboxed web process cannot reach). Not tested: a real GPU with a native driver.
- **The default takes effect** on 2.52.6 and 2.54.2 under Xvfb: `WEBKIT_DISABLE_DMABUF_RENDERER=1` (which sets the hardware-acceleration policy to never by itself) plus `GSK_RENDERER=cairo`: total private memory 255 → 82.5 MB (196 → 73–82 MB on Fedora), the web process 126 → 20 MB, page load about halved. Step 2 builds it; M1c step 1 measured the Notebook's launch to `ui-ready` at 211 ms with it and 577–582 ms without (over the 350 ms budget).
- **`connect-src spar://app` behaves as intended** on both versions: same-origin `fetch`, XHR and `EventSource` work from the page and a dedicated worker; every `ws:`, `wss:`, `http:` and `https:` target is refused with 0 connections (127.0.0.1, localhost, `app` on any port), and so is `spar://other`. `'self'` would let `wss://app` through. A violation's `blockedURI` is the full URL; workers are blocked too but fire no violation event and send no report (a gap for the report channel to note in step 4). Not tested with the real Spar host (a stand-in host served the pages), nor shared and service workers.

## Linux start-up: prerequisites, single instance, theme and the software-GPU default (implementation record; choices for review; 2026-11-19)

Plan M1c, build step 2. Evidence: `docs/evidence/m1c-startup.md`, its runner `docs/evidence/m1c-startup/wsl.sh`.
- **Built:** in `src/Spar.Linux`, `Prerequisites` (the display, GLib, GTK 4 and its version, WebKitGTK 6.0 and its version, the helpers beside the loaded library or in `libexec`, `bwrap` and `xdg-dbus-proxy`, the trial `bwrap` run), `StartupFailure` (stderr and `app.log` always, then the app's hook, then `GtkAlertDialog` on a loop of its own with `GSK_RENDERER=cairo` unless the user set one, or kdialog, zenity and xmessage without GTK 4), `SingleInstance` (`instance.lock` plus a Unix socket), `GpuCheck` (render node, `drmGetVersion`, surfaceless EGL), `WindowTheme` (saved choice, portal, GTK settings; CSS provider, dark preference, opaque view background, X11 background pixel), `ChildProcess` (`posix_spawnp`), and the `Libc`, `Egl` and `X11` imports. In the core: `StartupProblemKind.NoGraphicalSession` (exit 6), `HardwareAcceleration` with `spar.Browser(b => b.HardwareAcceleration(…))`, and `SavedColorScheme`, now shared by both hosts (Windows' `WindowTheme` reads it; nothing else changed there, apart from one log line when an app sets the option). `docs/app-model.md` 2, 3.15 and 3.17 and their check are updated; the bridge probe gained a relaunch log line, an opt-in own start-up message and the option from the environment.
- **Numbers** (WSL2 Ubuntu 24.04, WebKitGTK 2.52.6, GTK 4.14.5, Xvfb, llvmpipe; the bridge probe as a Production NativeAOT executable unless said; `docs/evidence/m1c-startup.md` has each run): every exit code forced and observed, in the last two runs. No display: 6, reported 1.9–2.9 ms after launch. A display nobody serves: 6 in 18.5–19.9 ms. WebKitGTK hidden: 3, its message shown at 124–139 ms. GTK hidden: 3 in 4.3 ms (no message program in WSL). Helpers hidden: 5, message at 77–84 ms. `bwrap` hidden: 3, message at 207 ms, most of it scanning WSL's `PATH`, which holds the Windows folders (573 ms before `/usr/bin` came first). The sandbox denied: 5, message at 99–109 ms. WebKitGTK "too old" (a Development build with the minimum raised): 4, message at 115 ms. A relaunch hands over 1.6–1.9 ms after its host starts and exits 0 in 3.3–3.8 ms; a stopped first instance gives exit 8 after 2.06–2.09 s and, resumed, drops the stale relaunch. The software-GPU check costs 0.0–0.1 ms where no render node can be opened (the case in WSL; 2.7–3.3 ms on CoreCLR); its EGL half, run in the test process, read llvmpipe in 22–26 ms first (82.6 ms once, cold) and 9.5–10.3 ms again, and `D3D12 (Intel(R) UHD Graphics 770)`, hardware, in 118–196 ms with Mesa's D3D12 driver forced. Six threads exist when the host sets the environment (ten on CoreCLR), all the runtime's. The Notebook (Production NativeAOT, `Auto`): 4,864,944 B (+188,784 B over step 1), launch to `ui-ready` median 223.9 and 226.1 ms in two runs of 11 (211.8–245.6; budget 350 ms), close to exit median 25.2 and 43.6 ms, total PSS 231–233 MB; from the host's start, the checks take 16.8 ms (the trial run 2.5 ms of it), the GPU check 0.1 ms and the theme 1.2 ms. Tests: on Linux the solution's 960 (897 passed, 63 skipped), the start-up and host tests 94 of 94 against the Development probe and 90 (4 skipped, Development-only) against the Production one. Theme: ten launches alternating light and dark, 0 samples of the other scheme, the first viewable sample in the theme's colour each time.
- **Choices made while building, for review:**
  - **Order:** the display (exit 6), single instance, the browser folder, then the library checks and the trial run, then the environment, the theme and GTK. The design listed `gtk_init_check` before the WebKit checks; here no check starts GTK (the libraries are loaded, never initialised), so the environment can still be set when a problem is found: `GSK_RENDERER=cairo` is set for the message only when there is one, and for the app only on a software GPU. A display that is named but unreachable is found by `gtk_init_check` after that, still exit 6.
  - **No exit 7 on Linux:** the design defines no Linux OS minimum, and a glibc too old fails before .NET runs. GTK older than 4.10 (no `GtkAlertDialog`) is "too old" (4), shown without GTK.
  - **The hook for exit 6:** `spar.OnStartupProblem` runs for the new public kind `NoGraphicalSession` too, though nothing graphical can follow: it is the app's only way to add to stderr and the log. On Linux the hook runs on the process's main thread, before any window.
  - **Messages name the command:** from `/etc/os-release` (`ID`, `ID_LIKE`), `apt` for Debian and Ubuntu and `dnf` for Fedora, the package's names on both otherwise. The journal is not written, only stderr and `app.log`; the design's journal line is left for M3's logging work.
  - **The trial run:** `bwrap --ro-bind / / --dev /dev --proc /proc --unshare-all --die-with-parent true` through `posix_spawnp`, its output kept for the message, 5 s at most; `bwrap` is looked for in `/usr/bin` first, then on `PATH`. .NET's `Process` is not used (nothing else in the image needs it).
  - **Single instance:** `instance.lock` in the data root, held by .NET's `FileShare.None` (an exclusive non-blocking `flock`; a held lock is an `IOException` with `EWOULDBLOCK`) and by the host's own `flock` on the same file, so it holds even with .NET's file locking turned off; a lock file the user cannot open (`EACCES`) is "another user" (exit 9). The socket's name is the lock file's device and inode in hexadecimal, `spar-<dev>-<ino>.sock`, in `XDG_RUNTIME_DIR` when it is absolute, at most 60 characters long and only the user's, else in the abstract namespace; both ends compare `SO_PEERCRED` users. The frame is Windows' (length, send time, `RelaunchMessage`) plus the launcher's activation token (`XDG_ACTIVATION_TOKEN`, else `DESKTOP_STARTUP_ID`), which the running window gets through `gtk_window_set_startup_id` before `gtk_window_present`; the send time is `CLOCK_MONOTONIC`. A background thread serves the socket, one connection at a time, with 2 s timeouts; it stops accepting at the teardown, and the socket's name goes before the lock is released.
  - **No hash in the socket's name:** SHA-256, as on Windows, would go through OpenSSL on Linux; the two numbers are unique on their own.
  - **The software-GPU default as decided,** with one addition: a user who set both `GSK_RENDERER` and `WEBKIT_DISABLE_DMABUF_RENDERER` gets no detection, since nothing would change. `setenv` never overwrites, and both variables reach every child process, the app's own included (pitfall). The surfaceless EGL context uses `EGL_KHR_no_config_context` where offered: Mesa's surfaceless display offered no GLES 2 configuration to `eglChooseConfig` (found by the test).
  - **The theme:** the portal's `ReadOne` (then `Read`) on the main thread before GTK, with `NO_AUTO_START` and a 250 ms limit, so a missing portal costs one failed call (about 3 ms with WSL's portal, which lacks Settings) and is never started; GTK's `gtk-application-prefer-dark-theme`, or a theme name ending in `-dark`, when no portal answers, read before the host sets the preference; the portal's `SettingChanged` followed live while the window follows the system (tested with a fake portal on a private bus). GTK's own settings are not followed live.
  - **One layer beyond the design, the X window's background pixel:** on X11 without a compositor the window first showed what was on the screen beneath it, for about 6 ms (a white frame in dark mode after a light window). The host realises the window just before presenting it and gives the X window the theme's colour as its background (`XSetWindowBackground` through GDK's X11 surface), as Windows' class brush does; then 0 wrong samples. Wayland surfaces skip it.
  - **Linux window tests run one at a time** (`LinuxWindowsCollection`): with no window manager every window opens at the same place under Xvfb, and an overlapping window spoils the frames a theme test reads.
- **Not verified:** a real desktop (GNOME, KDE, Wayland, a compositor); a real GPU with a native driver (the EGL path ran only in the test process, never in an app with an openable render node); the kdialog, zenity and xmessage fallbacks (none is installed in WSL); exit 9; focus after a relaunch with a real activation token; the portal on a real desktop; Fedora (the helper folder in `libexec` and the `dnf` hints come from package indexes); containers (Docker Desktop was not running; bubblewrap mount namespaces in WSL hid the pieces instead).

## Linux hangs: heartbeat, reload and shutdown prompt (implementation record; choices for review; 2026-11-20)

Plan M1c, build step 3: the hung-page heartbeat, "Reload page" through `webkit_web_view_terminate_web_process`, the close that meets a hung page, the UI-thread stall probe on the GLib main loop and the shutdown hang policy, on the Linux host, with the shapes and the Wait schedule decided for Windows (decisions "Hangs: the page's hook, the Production stall probe and "Exit now"", "Hangs: heartbeat, stall probe and shutdown prompt", "The page hang hook gets its async form; the heartbeat's start moves to step 4"). Evidence: `docs/evidence/m1c-hangs.md`, its runner `docs/evidence/m1c-hangs/wsl.sh`.
- **Built:** the Windows host's `PageWatch` (with `PageWatch.Closing.cs`, `PageCloseResult` and `ICloseQuestions`) and `ShutdownWatch` moved into the core, in a new folder `src/Spar/Hangs/` (namespace `Spar.Hosting`; `src/Spar/Hosting` holds 39 of 40 files), unchanged in behaviour, behind a seam: `IPageWatchHost` (post to the UI thread, visibility, ping, close request, end close, reload a hung page, show a prompt) and `IHostPrompt` (`HostPromptRequest`, `PromptAnswer`); the host reports its documents, pongs, close answers and the engine's report through `PageWatch`'s methods. `WindowsPageHost` is the Windows side (the bridge, the view, `PromptDialog`); `LinuxPageHost` the Linux one. On Linux: the relay answers `ping` with `pong` (and `{"dialog":true}`), the host reads the pong on the loop; documents opened at `LOAD_COMMITTED` arm the heartbeat; `close-request`, `close-answer` and `close-ended` reach the page's guards (`windows.onCloseRequested`) through the document's session, before the backend's guard; `notify::is-web-process-responsive` reported as the engine's; "Reload page" ends the web process and reloads when `TERMINATED_BY_API` comes, without using up the automatic reload, which still ignores `TERMINATED_BY_API`; a hung page's web process ended before its window is destroyed; the hang hook (`spar.OnPageUnresponsive`) started with the services and given 3 s at exit; the shutdown watch from the teardown's start; `GtkAlertDialog`s that the host can take down (`GCancellable`); the Development triggers of Windows on Linux, and `SPAR_DEV_PROMPT_ANSWERS`. Tests: `LinuxHangTests` (nine, against the bridge probe in a real window under Xvfb), the probe's new plan steps, a page that hangs before it connects (`hang.html`), the window count on the display.
- **Numbers** (WSL2 Ubuntu 24.04, WebKitGTK 2.52.6, GTK 4.14.5, Xvfb without a window manager; the bridge probe as a Development build and as a Production NativeAOT executable; host monotonic times, since WSL's wall clock jumped about 1.2 s twice in one run): the alarm 19.9 s after an endless loop began and 19.9 s after the document of a page that hangs before it connects opened, in both builds; no alarm on an idle page or on work of 3 s and 10 s; the longest heartbeat tick on the main loop 0.031 ms (NativeAOT), 0.57–0.64 ms (CoreCLR Debug); "Reload page": the web process ended 1.8–2.0 ms and the new document's `ui-ready` 68–72 ms after the choice (Windows: 85–140 ms); the Wait schedule on a 500 ms heartbeat 1.9, 3.9, 7.9, 15.9, 15.9 s, as on Windows; a close 1 s into a hang: "Wait / Close anyway" 5,016–5,019 ms after the request, again 18.8–20.0 s after Wait, Close anyway to exit 0 in 35–37 ms with the web process ended first; a silent page guard asked about 5,014–5,063 ms after the request; the shutdown prompt 10.0 s after the teardown began and again at 40.5 s, Exit now exit 10 with no web process left, and in Production a prompt taken down when a 15 s disposal finished, exit 0; a 300 ms stall reported as 300.1–300.2 ms in the named callback; `is-web-process-responsive` never changed. Tests: Linux hang tests 9 of 9 (Development) and 5 with 4 skipped (Production); steps 1 and 2's Linux tests 92 with 2 skipped; the Linux solution 969 (897 passed, 72 skipped); Windows: the solution 969 (915 passed, 54 skipped), the window suite 118 (106 passed, 12 skipped), the NativeAOT lane 634 (596 passed, 38 skipped), 0 failures anywhere. The Notebook on Linux 4,960,320 B (+95,376 B), `ui-ready` median 226.3 ms, close 43.1 ms, with no Development trigger or stall probe in its image.
- **Choices made while building, for review:**
  1. **One implementation for both hosts.** Duplicating `PageWatch` (about 700 lines of rules for dialogs, close requests and the Wait schedule) in the Linux host was the alternative; the move changed no Windows behaviour (the window suite's close and hang tests pass) and no log line.
  2. **The relay answers the heartbeat, not the page's SDK,** as Windows' injected script does: the ping never reaches the page (the relay checks the exact `{"type":"ping","seq":N}` and a TAB and answers in its own world, on the page's main thread, so a hung page leaves it unanswered). The host reads a message of at most 128 characters that starts `{"type":"pong",` on the loop, without the document's session and before the reader, which waits for the app's services: so a page is pinged from its document's creation, also before it connects and while the services are built. Longer or unreadable ones go to the session as before.
  3. **The page's close guards on Linux** need the document's session, which exists once the page has sent its first message; a page that has not connected gets the ping only, as a page without the SDK does on Windows.
  4. **"Reload page" reloads on `TERMINATED_BY_API`,** which the spike saw 0–3 ms after the call, never right after `webkit_web_view_terminate_web_process`; a process that ended otherwise is recovered as before.
  5. **The prompts on Linux are modal `GtkAlertDialog`s on the main loop,** the first button (Wait, Keep waiting) the default and the cancel button, so Escape and closing the dialog choose it; taken down through their `GCancellable` on the loop only. The hung-page dialog is owned by the window; the shutdown prompt by none. While the modal dialog is open, GTK does not pass the window manager's close of the window on (pitfall), so a close that meets a hung page is asked once the dialog is answered, which on Windows is the same for a person (the task dialog disables its owner).
  6. **The main loop keeps running until the shutdown is done:** the window's bridge, the hang hook and the services are stopped and disposed off the loop while it runs, so the shutdown prompt can show; the handles native callbacks use are freed after the loop ends. A shutdown stuck on the loop itself, which Spar never blocks, gets no prompt (GTK has one thread); the stall probe logs it.
  7. **"Exit now" on Linux** writes its line, flushes the log, sends SIGKILL to the app's `WebKit*` descendants that are still there, then `_exit(10)`. Exit now is the user's choice to end the app, and a web process left behind would outlive it by about 10 s.
  8. **Visibility on Linux** is the window mapped and its toplevel neither minimised nor suspended (`GDK_TOPLEVEL_STATE_SUSPENDED`, GTK 4.12; never set on 4.10).
  9. **`SPAR_DEV_PROMPT_ANSWERS`** (Development only, trimmed from Production, one queue per run): `first`, `second` or `none`, each optionally `@<ms>` (500 ms by default), answers the next prompt after it shows and takes it down; the tests' answers come from it, never from input. Windows' tests answer through `TDM_CLICK_BUTTON` and the window tests' `ICloseQuestions`.
  10. **Engine report:** a `notify::is-web-process-responsive` that turns false is one state with the heartbeat's alarm; turning true again is only logged, since the pong is what ends a hang.
- **Found while verifying:** the probe's page re-ran its plan after "Reload page" and hung again (the probe now runs no steps after a reload); a test's close sent to every window of the process also closed the open dialog, as Wait (it now goes to the main window only).
- **Not verified:** real input on the Linux dialogs and prompt (every answer was scripted); WebKit's own unresponsive report in a run; the visibility gate on a real window manager (Xvfb has none); a real desktop, Wayland, a compositor; a shutdown stuck on the main loop itself; the Notebook's own page hung; workers, frames and CPU stress.

## The browser profile folder per OS (user decision, 2026-11-20)

- **Decision:** the browser profile folder under the app's data root is named after its engine: `WebView2` on Windows (unchanged) and `WebKit` on Linux, matching Linux's cache folder `cache/WebKit`. Nothing has shipped, so no migration is needed. M1c step 4, which passes the network session's folders explicitly, makes the change in `SparFolders` and its tests; `spar.Folders(f => f.Profile(…))` still replaces it on either OS.
- **Not taken:** one neutral name (`Browser`) on both OSes, which hides which engine wrote the folder, and keeping `WebView2` on Linux, which names the wrong engine.

## M1c step 3's review: the Windows prompts keep their button ids (2026-11-20)

- **Found by rerunning M1b's hang scenarios on Windows** (`docs/evidence/m1b-hangs/scenarios.sh`, on a desktop of the run's own so nothing reached the user's screen), since the step moved the Windows hang code into the core: 17 of 20 passed, and the three whose second answer was "Close anyway" (202) or "Keep waiting" (300) failed. The shared code gave every prompt the ids 200 and 201, so a press of 202 or 300 found no button and nothing happened; a person clicking was not affected.
- **Fixed:** `HostPromptRequest` carries a `HostPromptKind` (`HungPage`, `HungPageClose`, `Shutdown`), and the Windows host maps it back to M1b's ids: 200 Wait, 201 Reload page, 202 Close anyway, 300 Keep waiting, 301 Exit now. The rerun passed 20 of 20 (idle, finite work of 3 and 10 s three times each, endless hangs at five phases with Reload page, recovery after 25 s of work, the Wait schedule, the silent and hung closes with Close anyway and exit 0, the shutdown prompt with Keep waiting then Exit now and exit 10, the shutdown that finishes, and both automated cases). Not rerun: the two scenarios that need the copy keeping the stall probe.

## Linux page security (implementation record; choices for review; 2026-11-20)

Plan M1c, build step 4: the navigation lock in `decide-policy`, the same policy and headers, the `permission-request` gate, and the network session's folders passed explicitly, with the browser profile folder renamed per OS (decision "The browser profile folder per OS"). Evidence: `docs/evidence/m1c-page-security.md`, its runner `docs/evidence/m1c-page-security/wsl.sh`.
- **Built:** in `src/Spar.Linux`, `PageGuard` (the lock on navigation, new-window and response decisions, the permission gate by the request's GType name, `query-permission-state`, an external address opened with `g_app_info_launch_default_for_uri` on a thread of its own), a capture-phase `GtkEventControllerLegacy` on the view that notes real input, the violation listener in the relay (`BridgeRelay.js`) with its settings written in by `ScriptBridge.RelayScript`, its batches read on the bridge's reader without a session, `report-uri` answered 204 and its body read with `g_input_stream_read_all_async` within `ReportLimiter`, one `CspViolationLog` per window disposed at the teardown, the session's folders read back from its data manager and recorded in `BrowserProfile` (now a service on Linux too), `spar.Folders(f => f.Profile(…))` checked for an absolute path (exit 70), and web processes left 3 s after the window is gone ended. In the core: `SparFolders.ProfileFolderName` (`WebView2` on Windows, `WebKit` elsewhere), `NavigationRules` in a new folder `src/Spar/Security/` (`src/Spar/Hosting` holds 39 of 40 files) with Windows' external-address rule moved there and the declared-frame match, the listener's and the endpoint's limits as `CspViolationLog` constants (Windows' `PageScript` and `PageServer` now refer to them), and a violation key without the position when there is no source file. `docs/app-model.md` (the profile folder per OS, the Linux gate) and its stand-in `SparApplication.cs`. Tests: `LinuxPageSecurityTests` (14, in a real window under Xvfb, two with real input through XTEST), `LinuxHostUnitTests` (the lock's addresses, frame sources, external addresses, every request kind, queries, the folder names), `CspViolationLogTests`, the relay's listener in `webkit.test.ts`; the bridge probe declares `Permission.Geolocation`, takes `BRIDGE_PROBE_PROFILE`, and gained the steps of `src/security.js` and a worker.
- **Numbers** (WSL2 Ubuntu 24.04, WebKitGTK 2.52.6, GTK 4.14.5, Xvfb without a window manager; the same in the Development build and the Production NativeAOT probe unless said): 6 script navigations ignored by the lock and the page on its first document (`location` to `https://`, `spar://other`, `data:`, `blob:`, a link's click, a meta refresh); `file:` refused by WebKit, a form by `form-action 'none'`, `window.open` and a script's click on `target="_blank"` by WebKit's popup blocker; one violation: 1 line, 1 listener batch, 1 `report-uri` report read; 2,000 distinct in 57–78 ms of page time: 100 lines, 1 batch, 20 reports read and 1,980 refused unread, 0 dropped; 100,000 distinct (Development) in 3.0 s: 101 lines (one says the group limit was passed), 1 summary, 1 batch, 34 read, 99,966 refused, no hang alarm; a user's click reached the stub handler about 1.0 s after the host's line; the Notebook 5,073,776 B (+113,456 B), 0 violation lines, `ui-ready` median 217.2 and 224.5 ms in two runs of 11, close 37.4 and 40.4 ms, PSS 234–235 MB, no `CspAdvice`, `PageSecurityMismatch`, `DevelopmentTriggers` or `SPAR_DEV_` in its image. Tests: Linux solution 1,039 (953 passed, 86 skipped); page security 14 (13 passed, the opt-in flood one skipped) on both builds; steps 1–3's Linux tests 158 (156 passed, 2 skipped); Windows: solution 1,039 (971 passed, 68 skipped), window suite 118 (106 passed, 12 skipped), NativeAOT `Spar.Tests` 704 (652 passed, 52 skipped) and `Spar.WindowTests` 118 (106 passed, 12 not run), 0 failures anywhere.
- **Choices made while building, for review:**
  1. **Frames per `FrameSrc` on Linux:** a navigation to an address the app declared for frames goes ahead (scheme, host with `*.`, port with `*`; paths left to the page's own `frame-src`). Windows' lock still cancels every frame navigation off the origin, so there `FrameSrc` works for `'self'` only; aligning Windows with Linux is the user's call.
  2. **`about:blank` and `about:srcdoc` refused for frames too:** WebKitGTK's decision has no main-frame flag, and a script's `location = "about:srcdoc"` commits in the window, replacing the page. Refusing them leaves a frame its first, blank document and a `srcdoc` frame empty, a difference from Windows. Letting them through and loading the app's page again when one committed in the window (tried) lost the page's state and left a web process that made the app abort after its exit.
  3. **A declared frame address in the window:** a load that starts right after the lock let such an address through is taken for it, stopped at its start, and its main-frame response ignored as a backstop; the document and its bridge stay (the bridge's epoch now ends at `LOAD_STARTED` only after a decision for the app's origin). The site still received the window's request. The guess is wrong only when a frame's decision comes between the window's own and its start: the app's own navigation is then stopped, or its epoch ends at its commit. A foreign document that commits anyway brings the app's page back.
  4. **Web processes with no window left are ended:** a `WebKitWebProcess` still there 3 s after the window is gone gets SIGKILL, and the network process, which holds stored data, is waited for again and never ended; WebKit kept one after case 3, and the app then aborted (134) about 10 s after its "exit 0". The close takes about 3 s in that case only.
  5. **A user's action is WebKit's flag plus real input:** WebKit's gesture flag was also set for script about 400 ms after a host-started load, and a script's click on a `target="_blank"` link then opened the address with the desktop's handler. An address now opens only when a button, key or touch event reached the view within 5 s (Chromium's transient activation).
  6. **Permission kinds:** geolocation, camera, microphone (both for a request of both) and clipboard reading by their declarations; the device list with the camera or the microphone; notifications, screen capture, pointer lock, media keys, storage access, XR and unknown kinds always denied. Pointer lock works without a request on WebView2, so a page that needs it works on Windows only.
  7. **`query-permission-state`** answers "denied" for the undeclared and "prompt" for the declared; WebKit shows "denied" to the page as "prompt", and a feature it granted in the document as "granted".
  8. **Refusals are logged 100 times a run,** then one line; Windows logs each.
  9. **A violation without a source file is grouped without its position** (the core's key, so Windows too): WebKitGTK's event says 1:1 where its report names none, which split one violation into two lines.
  10. **Listener batches go to the log from the reader without a session,** so a page that never connects still reports; a batch longer than 256 Ki characters is dropped.
  11. **`report-uri`** is answered 204 at once; the body is read afterwards without blocking the loop, into a native buffer of 16 KiB + 1 freed in the callback.
- **Deviations from the design:** `srcdoc` frames do not load on Linux (choice 2); the lock lets a declared frame address's request out before it stops it in the window (choice 3).
- **Found:** WebKitGTK sends a `report-uri` report for every distinct violation (WebView2 sent 89 per document); violations in a worker raise no event and send no report (the worker's `eval`, `new Function` and cross-origin `fetch` were blocked); a redirect of a declared frame to another origin is refused by `frame-src` before the lock is asked; notifications, clipboard reading and the device list are refused by WebKit itself without a user's gesture or a device, and after a click only notifications and pointer lock reached the host; Mesa writes `~/.cache/mesa_shader_cache`, the only thing outside the app's folder under the run's `HOME` (pitfalls).
- **Not built:** the autoplay gate on Linux (WebKitGTK ignores `autoplay=()`; its website policies were not set), the page's error reports (`errors`) on Linux, history navigation and downloads (step 6).
- **Not verified:** camera, microphone and screen capture requests (never asked for in an unattended run), a clipboard request, media keys, storage access and XR; the desktop's real handler, the OpenURI portal, and the focus of the opened browser under Wayland; service and shared workers; a real desktop.

## Linux WebAssembly off by default (implementation record; choices for review; 2026-11-20)

Plan M1c, build step 5: `JSC_useWasm` through libc `setenv` before GTK starts, `false` by default and `true` with the opt-in, as decided in "WebAssembly off on Linux; CSP on every response (evidence)"; a real-host test both ways in the page and in workers; shared and service workers; web-process restarts. Evidence: `docs/evidence/m1c-wasm.md`, its runner `docs/evidence/m1c-wasm/wsl.sh`.
- **Built:** `src/Spar.Linux/EngineEnvironment.cs` (`JSC_useWasm` set with overwrite 1, read back with `getenv`, a start-up line, a warning per other inherited `JSC_*`); `LinuxApp` resolves the page security after the prerequisite checks, sets the variable before the GPU check, and hands the security to `AppWindow`, which no longer resolves it; exit 70 if the variable cannot be set. Tests: two real-host theories in `LinuxPageSecurityTests` (default and opt-in; the page, a dedicated, a shared and a service worker; an inherited value of the other kind; the policy; the web process's environment; a lost web process; "Reload page"), the Notebook's web processes in `LinuxHostTests`, the setting's line in `LinuxHostUnitTests`; the bridge probe's step `wasm` (`frontend/src/wasm.js`, `wasm-worker.js`), `again-<step>` for a step after a reload, and its opt-in build `-p:BridgeProbeWebAssembly=true`. `tests/Spar.Tests` stays at 40 files. No public shape changed; Windows is unchanged.
- **Numbers** (WSL2 Ubuntu 24.04, WebKitGTK 2.52.6, GTK 4.14.5, Xvfb without a window manager; Development builds and Production NativeAOT executables alike unless said): by default `typeof WebAssembly` `"undefined"` in the page, a dedicated and a shared module worker, also after a lost web process and after "Reload page" (Development), `JSC_useWasm=false` in the web process although the test passed `true`; with `AllowWebAssembly()` `"object"` and the 41-byte module's `add(2, 3) = 5` in all three, the same after both restarts, `JSC_useWasm=true` although the test passed `false`, `'wasm-unsafe-eval'` in `script-src`; a service worker never registers from `spar://app` (`TypeError: serviceWorker.register() must be called with a script URL whose protocol is either HTTP or HTTPS`); page security, setting and GPU check together 0.2 ms of the start (17.6 to 17.8 ms); both NativeAOT probes 4,416,736 B; the Notebook 5,094,768 B (+20,992 B), `ui-ready` median 224.4 ms (215.9–239.3, n = 11), close 38.9 ms, PSS 231.2 MB, 0 violation lines, `JSC_useWasm=false` in its web process before and after a loss. Tests: WebAssembly tests 9 of 9 (Development), 7 with 2 skipped (Production); steps 1–4's Linux tests 181 (176 passed, 5 skipped); Linux solution 1,048 (958 passed, 90 skipped); Windows solution 1,048 (976 passed, 72 skipped), NativeAOT `Spar.Tests` 713 (657 passed, 56 skipped); 0 failures; the size check 0 errors.
- **Choices made while building, for review:**
  1. **Where the setting is read:** the host resolves the page security (`PageSecuritySetup.Resolve`) in `LinuxApp`, on the main thread after the prerequisite checks and single instance, instead of in the window. By then `Program.cs` has already run once (it built the `SparApp`), and the resolver takes the build's copy from the generated catalog, so no app code runs again; the Development comparison with the run's declaration and the overrides line move with it, earlier in the log. A launch that hands over to the running instance resolves nothing. Not taken: reading the catalog alone (it would skip the Development check) or a separate build-time flag for the host (a second copy of one declaration).
  2. **`AllowBrowserEnvironmentOverrides()` does not reach `JSC_useWasm`:** the variable is part of the page security the app declares in C#, not a browser override, so it is overwritten with the declaration in every build and with every option; an inherited value is named in the start-up line. Keeps the strict default; the alternative was letting the opt-in hand the variable to the environment.
  3. **Other `JSC_*` variables pass, each named in a warning at start-up:** the ignored-environment rule of plan section 3c is a block-list of exact names, and clearing JavaScriptCore's other options would change that rule. They matter (the spike's `JSC_useJIT=false` made scripts about 35 times slower, an incoherent pair aborted the app, and an unknown name is only logged by JavaScriptCore), so each is visible in `app.log`. Clearing them all, or adding some to step 6's list, is the user's call.
  4. **A variable that cannot be set stops the start** with exit 70 and an error line (the app's configuration could not be applied), rather than running with an engine setting the app did not declare. Not seen in a run.
  5. **The test app's opt-in is a second build** (`-p:BridgeProbeWebAssembly=true`, one `#if` around the `ContentSecurity` statement), since the host uses the build's page security: an environment variable cannot change it, which is the design. The runner builds it from a copy of the probe's folder, so its `bin` and `obj` stay apart; no new app.
  6. **Service workers are pinned as refused:** the test asserts the registration's `TypeError`, so a WebKitGTK that starts allowing them on custom schemes fails the test and gets a check of its own.
- **Found:** WebKitGTK 2.52.6 rejects `navigator.serviceWorker.register()` on `spar://app` (pitfalls, "Linux host"), so the plan's service-worker question has no WebAssembly side on Linux; shared workers run and inherit the variable; `rsync --exclude bin/` dropped Vite's `bin` from a copied app (pitfalls, "Tooling"); the page-security runner's `git clone` always failed harmlessly, since `isolate` creates the copy's folder first (this runner only copies).
- **Not built:** nothing of the step is left; step 6's ignored environment overrides on Linux are still to build.
- **Not verified:** frames (the spike saw the variable reach them; the probe's policy and lock refuse its same-origin frames), worklets and nested workers, a second window or a process swap, WebKitGTK 2.54 with Spar's host, a real desktop; Windows' window suite (unchanged code, not rerun).

## Linux security defaults (implementation record; choices for review; 2026-11-20)

Plan M1c, build step 6: section 3c on the published Linux executable, "no developer extras, the ignored environment overrides, and the GPU workaround variables still working", with Windows' opt-ins (decision "Security defaults (implementation record; choices for review)"). Evidence: `docs/evidence/m1c-security-defaults.md`, its runner `docs/evidence/m1c-security-defaults/wsl.sh`.
- **User decision (2026-11-20), superseding choice 3 of "Linux WebAssembly off by default":** a Production build removes every inherited `JSC_*` variable before WebKit starts; `AllowBrowserEnvironmentOverrides()` lets them through again; a Development build keeps them for debugging; `JSC_useWasm` always follows the C# declaration (`AllowWebAssembly()`), in every build and with every option. Each one is named in the start-up log: removed and named in Production, passed and named where kept. Asked for with it, as evidence: whether a JavaScriptCore option exposes a debugging object to the page in a release WebKitGTK 2.52; it does not (Found).
- **Built:** `EngineEnvironment.Filter` (after single instance, before any WebKit library is loaded: the names from libc's `environ`, `unsetenv` checked with `getenv`, the same removal in .NET's copy, a warning per variable without its value, exit 70 if one stays) and `Decide`, its pure rule; `BrowserDefaults` on Linux (the view's settings in one log line, `download-started`, `run-file-chooser`, `context-menu`, and GTK's inspector keys stopped at the window); `PageGuard` ignoring back-or-forward navigations unless `AllowHistoryNavigation()`, with `HostReloads()` for the host's own reloads; the opt-ins' documentation for Linux (`AllowBrowserEnvironmentOverrides()`'s list and risk, `AllowDevTools()`, and that `AllowAcceleratorKeys()` and `AllowPasswordSaving()` change nothing there) in `BrowserOptions`, `docs/app-model.md` and its stand-in; the probe's run-time opt-ins (`BRIDGE_PROBE_BROWSER`), its `steps` command and page steps; six real-host tests (twelve cases) in `LinuxHostTests`, the rules in `LinuxHostUnitTests`, `XTest` moved into `X11Frames.cs` with keys, any button and window titles; three markers in `ProductionBoundary`. `tests/Spar.Tests` stays at 40 files. Windows is unchanged.
- **Numbers** (WSL2 Ubuntu 24.04, WebKitGTK 2.52.6, GTK 4.14.5, Xvfb without a window manager, XTEST; Development builds and Production NativeAOT executables alike unless said) without the opt-in no inspector server listened (each of the two variables in its own run), the web process stayed under `bwrap` with `WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS=1`, and neither the web nor the network process had any of the three; with it each server listened in the app's process, the web process ran without `bwrap` and both processes had all three; `JSC_useDollarVM=true` absent from a Production web process, present in Development's and with the opt-in; `JSC_useWasm=false` in every case although the test passed `true`; `__NV_DISABLE_EXPLICIT_SYNC`, `WEBKIT_DISABLE_COMPOSITING_MODE` and Spar's `WEBKIT_DISABLE_DMABUF_RENDERER` in the web process in every case; the filter under 1 ms, the settings 0.00 ms; Ctrl+Shift+I opened no window in Production (with or without `AllowDevTools()`) and GTK's inspector with its warning (two windows) in Development; `GTK_DEBUG=interactive` opened it in both; plain text had no menu in Production and Inspect Element alone with `AllowDevTools()` or in Development; an editable field kept Cut, Copy, Paste and Select all; 13 browser key combinations reached the page and changed nothing (zoom, document, windows); downloads: 1 cancelled (the app's own file), 2 refused by the lock (`blob:`, `data:`), no file, and with `AllowDownloads()` none cancelled and none written within 2 s; the picker: the page got `cancel`, and with `AllowPagePicker()` GTK's own dialog opened; history: `history.back()` from the second document cancelled and the page kept, button 8 inert, and with `AllowHistoryNavigation()` 3 documents; the probe 4,425,296 B (+8,560 B); the Notebook 5,095,056 B (+288 B), `ui-ready` median 228.3 ms (213.2–255.2, n = 11) and 218.6 ms (212.2–226.4) in an earlier run of the step, close 39.9 ms, PSS 231.9 MB, 0 violation lines. Tests: this step's 125 of 125 (Development), 123 with 2 skipped (Production); steps 1–6's Linux classes 212 (209 passed, 3 skipped; Production 201 passed, 11 skipped); the Linux solution 1,079 (977 passed, 102 skipped); the production boundary on Linux passed; Windows solution 1,088 (1,004 passed, 84 skipped), NativeAOT `Spar.Tests` 753 (685 passed, 68 skipped), build 0 warnings; 0 failures; the size check 0 errors.
- **Choices made while building, for review:**
  1. **The three WebKit overrides are removed in every build, Development included,** as Windows does with its eight; plan 3c says "in Production". A developer who wants the remote inspector writes the opt-in, as on Windows; Development already has devtools.
  2. **Removal, not ignoring:** the variables leave the process (libc and .NET's copy) before WebKit's libraries load, so WebKit, JavaScriptCore and every child process (web, network, a program the app starts) miss them alike; nothing else in Spar reads them. Kept or removed, a variable is named without its value, as on Windows (step 5 logged the values of other `JSC_*` options; `JSC_useWasm`'s replaced value is still in its own line).
  3. **The settings are set explicitly although they are WebKit's defaults** (script clipboard, script windows, both `file://` accesses, top-level `data:`), so a changed default never reaches an app unseen; Production logs the values set, Development reads them back, as on Windows. The line costs nothing measurable (0.00 ms).
  4. **The context menu follows Windows exactly,** by WebKit's stock actions (Cut, Copy, Paste and Select all in editable fields; Inspect Element where devtools are on; elsewhere no menu; none in the automated mode), described in the log in Development only, so Production carries no description (a boundary marker).
  5. **GTK's own inspector keys are stopped outside a Development build** (found here: GTK 4 opens its inspector on Ctrl+Shift+I and Ctrl+Shift+D by default, and it can change any object of the process). A key controller in the window's bubble phase, after the page had the keys; `AllowDevTools()` does not bring it back, since it is not the page's devtools. Not taken: changing the user's GSettings (every GTK app) or a capture-phase filter (the page would lose the keys).
  6. **Downloads are cancelled in the session's `download-started`;** a `download` link to a `blob:` or `data:` address is refused earlier by the navigation lock, so on Linux `AllowDownloads()` covers the app's own files only. Letting such links through the lock would need WebKit to tell a download from a navigation, which its navigation action does not.
  7. **History:** WebKit decides the host's reload after a lost or ended web process as a back-or-forward navigation (Found), so the host marks its own reload and the next navigation to the app's origin goes ahead.
  8. **A signal a default depends on that WebKit lacks stops the start** (exit 70, "The window could not be created"), never a start with the default missing. Not seen.
  9. **The probe's opt-ins come from the environment at run time** (`BRIDGE_PROBE_BROWSER`): browser options are not read by the build, so no second build; the WebAssembly build stays the only one.
- **Found:** a release WebKitGTK 2.52.6 refuses `JSC_useDollarVM=true` ("ERROR: invalid option", a restricted option) in the app and its web processes, so `typeof $vm` stayed `"undefined"` with the variable kept (Development, or the opt-in) and removed (Production); GTK 4's inspector keybinding is on by default (`org.gtk.gtk4.Settings.Debug`), so before this step every Production Spar app on Linux opened GTK's inspector on Ctrl+Shift+I; `GTK_DEBUG=interactive` still opens it at start, in Production too: it is not among plan 3c's overrides, and adding it (or GTK's other debugging variables) to the list is the user's call; with both inspector variables set, WebKitGTK opened only the HTTP one, each alone listened in the app's own process; the host's reload after a lost web process arrives as a back-or-forward navigation (4 of 4 WebAssembly recovery tests failed until it was let through); WebKitGTK binds no browser key, not even its inspector's in a release build, and ignores the mouse's back button; `/proc/<pid>/environ` shows a process's starting environment, not `unsetenv`'s changes; xUnit v3 joins filters of different kinds with AND (pitfalls, all).
- **Not built:** reload keys for Linux Development builds (the development loop, M1d); a `decide-destination` for allowed downloads (none was written under Xvfb, not investigated further); anything for `GTK_DEBUG`.
- **Not verified:** a real desktop (the portal's file chooser, a saved download, the full menu, WebKit's inspector opening from Inspect Element; a row in the plan's table), Wayland, a real GPU's drawing with the workarounds, WebKitGTK 2.54, other distributions' schema defaults; Windows' window suite (no Windows code changed, not rerun).

## GTK_DEBUG removed in Production (2026-11-20)

The open question of "Linux security defaults (implementation record; choices for review)": `GTK_DEBUG=interactive`, which GTK reads itself at its start, opened GTK's inspector (which can change any object of the process) in both builds, Production included.

- **User decision:** a Production build removes `GTK_DEBUG` before GTK starts, as it does the `JSC_*` options: `AllowBrowserEnvironmentOverrides()` lets it through again, and a Development build keeps it for debugging. Only this name: GTK's rendering choices (`GSK_RENDERER`, `GDK_DEBUG`) pass, like the GPU workarounds.
- **Built:** `EngineEnvironment.Decide` treats `GTK_DEBUG` like a `JSC_*` option, with its own log line (named, never its value); `Filter` already runs before `gtk_init_check`. The unit test's environment gained `GTK_DEBUG`, `gtk_debug` and `GDK_DEBUG`; the real-host test, now `GTK_DEBUG_interactive_opens_GTKs_inspector_only_in_Development_or_with_browser_environment_overrides`, checks the windows with and without the opt-in. `AllowBrowserEnvironmentOverrides()`'s documentation, `docs/app-model.md`, `docs/design/security.md` 3c, the plan, the pitfall and the skill say so.
- **Results (WSL, Xvfb, `docs/evidence/m1c-security-defaults/wsl.sh`):** the Production NativeAOT probe with `GTK_DEBUG=interactive` opened only the app's window, and with the opt-in the app's window and GTK's inspector; the Development probe opened both either way. Step 6's tests 126 of 126 in Development, 124 with 2 skipped in Production NativeAOT; the Linux solution 1,089 (103 skipped); the production boundary test passes. Windows: the solution builds with 0 warnings, the size check and `tests/AppModel.Tests` (11) pass.
- **Not verified:** a real desktop (as for step 6); steps 1–5's Linux tests were not rerun, since only this rule changed.

## M1c step 7's shape: Linux window and black-box tests (user decisions, 2026-11-20)

Plan M1c, build step 7, before building. The image question (Dockerfile only, WSLg allowed locally after asking) was decided on 2026-11-19 ("M1c's first questions").

- **Display: a private Xvfb per test process.** The test host starts its own Xvfb (`GDK_BACKEND=x11`, XTEST for trusted input, an abstract socket only), so a Linux window test never shows on a desktop, as Windows' offscreen, never-activated window never does. A setting chosen while building (named in the implementation record) uses the session's display instead, such as WSLg, for watching a test locally; for agents that is the user's desktop, so `AGENTS.md`'s "ask first" applies.
- **A missing prerequisite is an explicit skip, and a failure on CI.** No Xvfb, no WebKitGTK 6.0 at the supported minimum, or user namespaces blocked for the web-process sandbox: the window test skips with the start-up check's own message, and when `CI=true` the same condition fails the test, so CI cannot pass by skipping.
- **The black box on Linux: full parity, after a spike.** A short spike reads the Production executable's window and a `GtkAlertDialog`'s buttons over AT-SPI from C# in the test's private Xvfb and D-Bus session, with its latency; then `SparBlackBox` on Linux gets `app.Window` and `WaitForDialogAsync`, so both hosts pass the same host-lifecycle suite, exit codes and dialogs included.
- **Layout: `Spar.WindowTests` runs on both OSes.** One host-lifecycle suite with no OS branches; the Linux host tests now in `tests/Spar.Tests` that need a window move there on the Window surface, those that need a process of their own become black-box tests, and `tests/apps/BridgeProbe` folds into `tests/apps/WindowHost` where it can. `tests/Spar.Tests` stays within 40 files, with no size exception.

## Linux window tests (implementation record; choices for review; 2026-11-20)

Plan M1c, build step 7, the Window surface on Linux (decision "M1c step 7's shape: Linux window and black-box tests"). Evidence: `docs/evidence/m1c-window-tests.md`, its runner `docs/evidence/m1c-window-tests/wsl.sh`. The black box on Linux, the CI image and `online`/`offline` are other parts of the step and are not built here.
- **Built:**
  - **A host-neutral `ISparTestWindow`** (`src/Spar/Testing/SparTestWindow.cs`, internal): engine requests in the isolated world `spar-automation`, script in the page's world, navigation, trusted `SparTestKey` and `SparTestPointer` events (each completes once the page received it), screenshots, renderer processes, the close operations, and the page's events through `ISparTestPageEvents` (documents, console, errors, dialogs); the engine script comes with the request (`SparTestAutomation`). `SparTestCallException`, `SparTestScriptException` and `SparTestWindowUnavailableException` replace `SparDevToolsException`. `PageSession`, `SparKeyboard`, `SparMouse`, `SparPage` and `WindowSurface` use only it; Windows' DevTools transport (contexts, `Runtime.callFunctionOn`, `Input.*`, `Page.captureScreenshot`, dialogs) moved into `src/Spar.Windows/Testing/TestPage.cs`; `CloseReport` moved into the core beside `ICloseQuestions`. No public API changed.
  - **`src/Spar.Linux/Testing/`:** `TestEnvironment` (per test process: a private Xvfb on an abstract socket, `-displayfd`, from display :140; a private `dbus-daemon`; `HOME` and every `XDG_*` folder under `artifacts/linux-homes/test-host/<stamp>-<pid>/`; `XDG_RUNTIME_DIR` in `/dev/shm`; `GDK_BACKEND=x11`; libc's and .NET's environment alike; both helpers tethered through their standard input so they end with the process; leftovers of dead processes deleted), `TestWindowProcess` (the display check, `EngineEnvironment.Filter` as a Production app does it, the libraries, WebKitGTK and the sandbox trial, the first app's WebAssembly setting and the software-GPU default, once, before GTK; `MainLoop.StartShared`; scheme turns; window places; first-load turns), `TestWindow` (the app's own `AppWindow` with a `BrowserSource`, automated, logging to the app's log only, the leftover-renderer check), `TestPage` (the engine and a relay as document-start scripts of `spar-automation`, a page-world script for console and errors, `call_async_javascript_function`, `script-dialog`, `load-changed`, `load-failed`, `notify::uri`, `get_snapshot` with PNG and crops), `XInput` (XTEST on a connection of its own), `TestViews`, `TestNative`. The host: `BrowserSource` (an ephemeral network session and a `WebKitWebContext` of the window's own, the driver's set-up before the first load, a realized hook), `IAppWindowOwner.WebKitProcesses` (an app waits for every WebKit process below it, a test window for its own), the Linux close request with `ICloseQuestions`, `CloseReport` and the session end's rules and budgets (the guards' 3 s, the page's 2 s, 4 s in all; `TeardownAsync` takes a deadline), `CloseAsUser` (`gtk_window_close`), `WindowTheme.Fixed` with a display and CSS class of the theme's own, `SparLinuxHost` as an `ISparTestWindowHost`. The engine (`PageEngine.js`, still one file): `inputCount`/`inputAfter` (trusted input counted, so a host whose input travels outside the page can wait for it), `insertText`, `landed` telling an unarmed watcher apart, and elements clipped by a scrolling ancestor scrolled into view.
  - **Tests:** `tests/Spar.WindowTests` on both OSes (`WindowSupport.SkipUnlessSupportedAsync`, `SkipUnlessWindows`, `SkipUnlessBlackBox`; origins through `HostPage.Origin` and `Address`); new `PageSecurityTests` (a script's navigation and `window.open` refused, the policy on a 200 and a 404, an undeclared permission refused without a prompt, neither the relay nor its handler in the page's world) and `HostTests.A_lost_renderer_is_recovered_once_and_the_page_connects_again`, on both hosts; `PageApiTests.Page_errors_reach_the_apps_log_through_the_host` split out; the static-state list. `tests/Spar.Tests` keeps 40 files; `tests/apps/WindowHost` builds for Linux unchanged (`WinExe` is no obstacle).
- **Per-test choices for Windows-only mechanisms:** `SessionEndHostTests` (5) skip on Linux, since the Linux host does not follow logind's session end; the session end's close rules run on Linux through `CloseTests.A_session_end_runs_both_guards…`. `InstallPathTests` skips on Linux (Windows' `MAX_PATH`; a Linux long path belongs to the black box). `BlackBoxFixtureTests` (12) and `BudgetTests.The_Production_app_starts_to_ui_ready…` skip on Linux until the Linux fixture. `ThemeTests.Each_window_keeps_its_own_scheme_beside_another` skips on Linux (choice 3). `PageApiTests.Page_errors_reach_the_apps_log_through_the_host` skips on Linux (the host's report is not built there). `NativeThemeAsync` has a Linux counterpart (GTK's dark preference of the window's display and the theme's colour); `SendMessageAsync` throws `PlatformNotSupportedException` on Linux. The Sandbox tests stay Windows' explicit ones.
- **Linux tests before and after:** before, on Linux, `Spar.WindowTests` skipped all its window tests (the OS check), and `tests/Spar.Tests` ran `LinuxHostTests` (18 cases), `LinuxHangTests` (9), `LinuxPageSecurityTests` (18), `LinuxStartupTests`, `LinuxStartupUnitTests` and `LinuxHostUnitTests` against BridgeProbe from outside. After: all of those stay and pass (222 cases: 217 passed, 5 skipped, the Development probe), and `Spar.WindowTests` runs on Linux (94 passed, 30 skipped). None was moved (deviation, choice 1). In-process counterparts: `LinuxHostTests`' calls, second document and close by `HostTests.A_navigation_with_calls_in_flight…`, `CloseTests.A_close_asks_the_page_then_the_backend…`, `HostTests.The_page_renderer_is_found…` and `PageSecurityTests.The_bridge_relay_and_its_handler…`; its lost web process by `HostTests.A_lost_renderer_is_recovered_once…`; `LinuxHangTests`' closes of a hung or silent page by `CloseTests.A_hung_page_is_closed_anyway…`, `Wait_keeps_the_request_open…`, `A_silent_guard_gets_wait_or_close_anyway…` and `In_the_automated_mode_a_silent_guard_is_closed_anyway`; `LinuxPageSecurityTests`' script navigations, headers and permissions by `PageSecurityTests`, and WebAssembly by `PageDefaultsTests`. Out of process only: the foreign frame and declared frame addresses (a frame source in the build's policy), the second loss with exit 5 (the probe's slow page), the Notebook's numbers, the environment overrides, inspectors, `GTK_DEBUG` and browser keys (before WebKit loads, or a Production build), downloads, the picker, the menu and history (run-time opt-ins), the hang dialogs, the Wait schedule, the shutdown prompt with exit 10 and the stall probe (native dialogs, a Production build), the session's folders (a persistent session) and every WebAssembly case (the engine's setting is the process's).
- **Numbers** (WSL2, Xvfb, WebKitGTK 2.52.6; Windows 11, Runtime 157): a Linux window test of open, the 8-step journey and close 237.7 ms median (p95 246.1) on CoreCLR and 225.0 ms (p95 240.6) on NativeAOT, against 500 ms: open 94.7 ms, journey 129.4 ms, close 13.6 ms, the view with its session 8.0 ms; the first window of a test process (check, Xvfb, D-Bus, GTK, window) 492 ms on CoreCLR and 273 ms on NativeAOT; click 16.3 ms, key 1.3 ms, fill 1.5 ms, screenshot 21 ms, role lookup 0.37 ms. The whole window suite 48.9 s (CoreCLR) and 50.5 s (NativeAOT, a 19,623,696-byte executable). Windows in the same session: 325.6 ms median (p95 345.5) on NativeAOT (298 ms in M1b step 8a), its suite 86.8 s (CoreCLR) and 78.0 s (NativeAOT). Tests: the Linux window suite 124 (94 passed, 30 skipped) on both lanes, 0 failures in the last 5 CoreCLR runs and the NativeAOT run; Linux host tests 222 (217, 5 skipped); the Linux solution 1,089 (986, 103 skipped); the Windows window suite 124 (112, 12 explicit Sandbox) on both lanes; the Windows solution 1,089 (1,004, 85 skipped); the Notebook's 10 window journeys; `tests/AppModel.Tests` 11; Vitest 266; builds with 0 warnings on both OSes; the size check 0 errors; 0 processes left by the window tests after every Linux run.
- **Choices made while building, for review:**
  1. **No Linux host test was moved out of `tests/Spar.Tests`:** each also runs on the Production NativeAOT probe (steps 1–6's runners), which an in-process window (the Test variant) cannot replace; the in-process counterparts above were added instead, and the out-of-process tests stay until the black-box step moves them. Alternative: delete the ones with a counterpart now, losing their Production runs until then.
  2. **The Linux surface's input waits for the page:** every XTEST key and pointer event is followed by an engine wait until the page received a trusted event (1 s at most), as a DevTools input call returns after the renderer handled it; each event takes the process's input lock, which raises the target window (pointer) or gives it the focus (keys) first. A press the engine's armed watcher never saw is clicked again (both hosts).
  3. **One colour scheme at a time on Linux:** WebKitGTK reads the default display's dark preference for every view, so a window whose scheme differs from the open ones waits until they closed, in order; `System` counts as the app's saved choice or light. The test of two schemes at once skips on Linux.
  4. **Off-layout characters, and shifted ones typed alone, go through spare keycodes** (bound at level 0, keycodes with no evdev key first, never one that is down, one such stroke at a time, two GTK turns after binding), so the page sees no Shift event, as on Windows; their `code` is "Unidentified" where Windows gives "", and WebKitGTK names a non-BMP key "Unidentified" with no `keypress`. `KeyTests` accept either; `SparKeyboard`'s documentation says so.
  5. **Windows are placed** on a 6400 × 4200 Xvfb screen in 25 places of 1280 × 840, moved when realized, before they show.
  6. **A window's renderer is the web process that appears while it loads its first page,** windows taking turns at that (15 s at most each); one started for a reload is claimed when it is the only new one and no window is loading. Not taken: per-window environment markers (`setenv` in a multi-threaded process), or no leftover check on Linux.
  7. **Phases on Linux carry Windows' names** where the tests read them: "environment" (the shared process ready), "controller requested" and "controller" (the view and its ephemeral session), "first navigation completed", "navigation failed", "closing".
  8. **The prerequisite check is a probe window:** `WindowSupport` opens WindowHost's window once; `SparTestWindowUnavailableException` (internal) skips Spar's window tests with its message, or fails them with `CI=true`. An app's own window tests get the exception as a failure: no public skip API was added without asking.
  9. **`SPAR_TEST_DISPLAY=session`** uses the session's `DISPLAY` instead of a private Xvfb (still a private D-Bus and homes); never used here.
  10. **A test process's engine settings are its first app's:** a window of an app whose WebAssembly declaration differs throws, naming why; the browser environment is filtered as a Production app does (every `JSC_*`, `GTK_DEBUG` and the overrides removed).
  11. **An engine call that does not answer within its wait is a retry on Linux** (100 ms at least), and the page's state is logged once as a warning.
- **Deviations:** no test moved (choice 1); BridgeProbe not folded into WindowHost (nothing in-process needed its pages).
- **Found** (pitfalls, "Linux host", M1c step 7): the scheme per process, the scheme handler per web context, the non-BMP and code names, XTEST's keymap levels and spare keycodes WebKit names, the rebinding race, windows mapped over others, scroller clipping, the first-load turn kept by a closed window.
- **Not verified:** the CI image and a container; Wayland, a window manager, HiDPI, a GPU; the Notebook's window journeys on Linux (still skipped by their own OS check); `SPAR_TEST_DISPLAY=session`; the prerequisite skips on a machine without Xvfb, D-Bus, XTEST or user namespaces (the messages exist; no run lacked them); two test processes at once on one machine; the Windows budget's rise to 325.6 ms against M1b's 298 ms (one run, not investigated).

## The Linux black box: AT-SPI over GDBus; the dialog's title; SIGTERM as a session end (spike result and user decisions, 2026-11-20)

Spike: `spikes/linux-blackbox-atspi/` (WSL Ubuntu 24.04, GTK 4.14.5, WebKitGTK 2.52.6, at-spi2-core 2.52.0, the Production NativeAOT `tests/apps/BridgeProbe`, a private Xvfb and D-Bus session per run).

- **Route: AT-SPI through GIO's GDBus,** with no libatspi: about 400 lines plus 140 of imports; finding the app by pid (`GetConnectionUnixProcessID` on the accessibility bus, so a stopped app is never called) and reading its title 1.2 ms p50. libatspi also worked (0.8 ms) but is a package WebKitGTK does not pull in and brings libdbus and process-wide state.
- **What GTK 4 needs:** a private session bus that can activate at-spi2-core's `org.a11y.Bus`; the registry starts on GTK's first call. An accessibility bus without the registry is not enough ("Unable to register the application"); `GTK_A11Y=none` hides the app, so the fixture strips it. Launch to the window over AT-SPI 83.8 ms p50 (n = 20). The CI image adds `at-spi2-core` (three packages, 142.5 KB download, 491 KB installed, from an `apt-get -s` simulation).
- **Window and close:** the title is the frame's `Name` (20 of 20); GTK 4 windows expose `window.close`, which goes through `close-request` exactly like `WM_DELETE_WINDOW` (the page's guard kept the window both ways); close to exit 46.2 ms p50.
- **Dialogs:** a `GtkAlertDialog` is a modal dialog-role window with an empty title; `Cache.GetItems` reads it in one call (0.4–0.5 ms, 40 of 40 equal to a tree walk). `DoAction(click)` answers 256–262 ms later (GTK holds a keyboard-activated button about 250 ms), an XTEST click 45–53 ms. The relaunch message 20 of 20 (exit 8 each), the hung-page dialog 20 of 20.
- **Kill with the parent:** .NET 11's `ProcessStartInfo.KillOnParentExit` left 0 processes in 25 of 25 runs with the test process SIGKILLed at 0–300 ms or after `ui-ready`; a plain `Process.Start` left all 8 in 25 of 25. NativeAOT reader 3,072,024 B, 0 warnings.
- **User decision: `SparNativeDialog.Title` on Linux is the app's name,** as Windows' task dialog shows it, substituted by the fixture and documented as such, so the lifecycle suite needs no OS branch.
- **User decision: SIGTERM and SIGINT start the session-end close on Linux,** as `WM_ENDSESSION` does on Windows: both guards asked with `CloseReason.SessionEnd`, nothing can keep the window, the services disposed, the final line written and the log flushed within Windows' budget; a second signal exits at once. The black box's `EndSessionAsync` sends SIGTERM on Linux. Found by the spike: today the Linux host exits 143 at once on SIGTERM (10 of 10).
- **Not verified:** Wayland, a real desktop, the Notebook, the rest of the automation interface (page elements, bounds, focus, context menus), an installed CI container.

## The Linux CI image and job; online and offline on Linux (implementation record; choices for review; 2026-11-20)

Plan M1c, build step 7, the CI image and the page's `online`/`offline` events (decisions "M1c's first questions": Spar ships the Dockerfile only and CI builds it; "M1c step 7's shape"; "Network status"). Evidence: `docs/evidence/m1c-ci-image.md`, raw logs in `docs/evidence/m1c-ci-image/`.
- **Built:**
  - **`eng/linux-ci/Dockerfile`:** Ubuntu 24.04 pinned by digest. WebKitGTK 6.0 with GTK 4, bubblewrap, xdg-dbus-proxy, Xvfb, xauth, xkbcomp, XTEST, the D-Bus session daemon, at-spi2-core, fontconfig and DejaVu, clang and zlib1g-dev, iproute2 and procps. Node 24.22.3, pinned with its SHA-256. The SDK `global.json` names, passed as `DOTNET_SDK_VERSION` and checked against the SHA-512 in Microsoft's release metadata. User `spar`, uid 1000, in place of the base image's `ubuntu`. The image is 2,406,111,741 B (587.8 MB gzipped). A cold build took 314 s (apt 156 s, 278 packages, 217 MB downloaded); a fully cached one, 1.1 s.
  - **`eng/linux-ci/run.sh`:** inside the container, `copy` (the read-only `/src` into `~/repo`, without `.git`, `bin`, `obj`, `node_modules`, `artifacts` and `spikes`), `client`, `build` (Spar.WindowTests and Notebook.EndToEndTests), `check` (bwrap's namespaces and the switch, each with a plain message), `window` (CoreCLR, trait `Surface=Window`, both projects), `windowaot` (both published with NativeAOT, `-trait Surface=Window`, TRX each), and `blackbox` (trait `Surface=BlackBox`, CoreCLR). A run with no test fails; in `blackbox`, so does any skipped test.
  - **`eng/linux-ci/network-switch.sh`:** runs a command in an unprivileged user namespace that owns a new network namespace. Its only link is `lo`, with a default route through it. A loop that is root of that user namespace removes the route on `offline` and adds it back on `online`, read from a FIFO named by `SPAR_TEST_NETWORK_SWITCH`. The command runs in a nested user namespace as the caller's uid and gid with no capability, so WebKit's bwrap starts as it does outside. The window invocations, both lanes, run under it.
  - **The `linux-window` job** in `.github/workflows/ci.yml` (ubuntu-latest, 45 min). It prints the runner's user-namespace sysctls, then builds the image with `docker/build-push-action` (pinned SHAs, v4.4.1 and v7.4.0) and buildx's GitHub Actions cache (`scope=linux-ci`, `mode=max`). It starts one container (`--init --shm-size=1g --security-opt seccomp=unconfined -e CI=true`, the workspace read-only at `/src`) and runs `check`. The run.sh steps follow as their own workflow steps. Finally it does `docker cp` of the TRX results, uploads `test-results-linux-window` (14 days), and removes the container. The existing matrix jobs are unchanged except for comments: the Windows window steps say Linux runs in this job.
  - **`tests/Spar.WindowTests/NetworkStatusTests.cs`:** opens WindowHost, records the page's `online`/`offline` events, then switches offline. It asserts the `offline` event, `navigator.onLine` false and Echo answering. It switches back and asserts `online`, `navigator.onLine` true, Echo again, exactly `offline:false` then `online:true`, and a clean page. It records the time from the request to each event. On Windows it skips with why: the page offline is checked in Windows Sandbox. On Linux without the switch it skips, and with `CI=true` it fails. It runs alone in a `DisableParallelization` collection, because the switch is the whole namespace's.
  - **The Notebook's window journeys on Linux:** `NotebookWindow` no longer skips off Windows. A missing prerequisite fails a journey, as an app's own window tests do, per choice 8 of "Linux window tests". `SharedBrowser` is now `JourneyTests`' class fixture, not an assembly fixture, so a window-only run starts no Chromium.
- **Numbers** (Docker Desktop 29.9.3 on WSL 2, 32 CPUs, WebKitGTK 2.52.6). Spar.WindowTests on the Window trait: 66 tests, 57 passed and 9 skipped, CoreCLR 55 s and NativeAOT 54 s (a 19,656,832 B executable). The Notebook's window journeys: 10, 7.9 s on CoreCLR and 5.5 s on NativeAOT. The page's `offline` came at a median of 3,086 ms after the request (2,871–3,697) and `online` at 2,949 ms (2,918–2,963), over 13 runs. Build: 157 s + 137 s, 0 warnings. These counts were taken with two fixes applied to the container's copy only (see "Depends on").
- **Depends on** (found here, owned by the black-box work; the coordinator assigned both):
  1. **Window-test processes exit 1 after passing:** at `ProcessExit` the test environment ends its tethered Xvfb while GTK's loop still holds the display, and GDK's IO-error handler calls `_exit(1)`. It happened in every container run before the fix and in none after, with no process left.
  2. **The one-scheme gate deadlocks** when a test holds one window open and opens another while a window of the other scheme waits. Before: 3 of 4 runs without the switch and 4 of 8 with it failed (3–5 tests at 30 s). After a stand-in (ThemeTests alone): 0 of 3 CoreCLR and 0 of 3 NativeAOT.

  Until both land, the job's window steps fail.
- **Choices made while building, for review:**
  1. **Docker's AppArmor profile is kept; only seccomp is unconfined.** Moby's `docker-default` template sets AppArmor ABI 3.0, which does not mediate user namespaces. `apparmor=unconfined` would put the container under Ubuntu 24.04's `apparmor_restrict_unprivileged_userns` for unconfined processes. That restriction is what breaks unconfined `unshare` on GitHub's ubuntu-24.04 runners (actions/runner-images#10443). This is reasoning, not a run: Docker Desktop has no AppArmor. The first CI run settles it (the sysctl print, then `check`). Not taken: `--privileged`, `CAP_SYS_ADMIN`, or a sysctl on the runner.
  2. **The network switch is a namespace around the whole window invocation,** not a `docker network disconnect`. The disconnect also removed the route, but it needs a helper outside the container and takes the build offline. The test process cannot make its own namespace, since `CLONE_NEWUSER` needs a single thread. A side effect: the window tests run with no outside network, which they do not need.
  3. **A skipped black-box test fails the `blackbox` step on CI** (deliberately, consistent with "a missing prerequisite … fails on CI"). Today Spar.WindowTests' black-box tests skip on Linux (9 of 21, waiting for the Linux fixture), so the step fails until the black-box work lands. The alternative of leaving the step out would let CI pass without it. Watch: the moved Linux host tests skip without `SPAR_LINUX_WASM_APP`, so the job must build that probe or the rule must name them.
  4. **The image takes Ubuntu's packages unpinned** (security updates at each rebuild; the layer cache keeps them until the Dockerfile changes), while the base digest, Node and the SDK are pinned and checked. No window manager yet.
  5. **The copy leaves `.git` out:** a live working tree changed it under `tar`, and the build needs no repository, even with `ContinuousIntegrationBuild=true`.
  6. **The job builds only what it runs** (two test projects), not the solution: the ubuntu matrix lanes still build and test everything else on the runner.
- **Not verified:** the job on GitHub (the gha cache, AppArmor with `docker-default`, timings, `docker cp`). Also: a clean run from `copy` with both fixes in the shared tree; the Notebook's black-box tests in the container; a window manager. Two failures were seen once each and are not explained: a NativeAOT `KeyTests` "€" with only `keydown`/`keyup` (1 of 3), and the Notebook's conflict journey with its text before the final newline after `Control+End` (1 of 3).

## The Linux black box and SIGTERM (implementation record; choices for review; 2026-11-20)

Plan M1c, build step 7, part 2 (decisions "M1c step 7's shape: Linux window and black-box tests" and "The Linux black box: AT-SPI over GDBus; the dialog's title; SIGTERM as a session end"). Evidence: `docs/evidence/m1c-blackbox.md`, its runner `docs/evidence/m1c-blackbox/wsl.sh`, raw logs in `docs/evidence/m1c-blackbox/`.
- **Built:**
  - **SIGTERM and SIGINT as the session end** (`src/Spar.Linux/SessionEnd.cs`, `LinuxApp`, `AppWindow.EndSession`): the first signal starts the session-end close of `WM_ENDSESSION` (the page's guard with `sessionEnd` at most 2 s, the backend's in what is left of 3 s, neither able to keep or ask, an open close request replaced), then the services' disposal, all cut at 4 s from the signal; then the final line `session end: <outcome>; SIGTERM held <ms> ms` and the log flushed. A disposal still running at 4 s is cut there: WebKit's processes are killed and the process exits at once. The exit code is 0, the window close's, as a Windows app that outlives `WM_ENDSESSION` exits (its teardown gives 0; a process Windows ends after the message has no code of its own). A second signal logs one line and exits at once with 128 + its number (143, 130). The handler is .NET's `PosixSignalRegistration`, cancelled so the runtime's default does not run, registered once the window has started and only by `LinuxApp`; it only posts the close to the main loop and starts the wait on the thread pool.
  - **A test window's session end** (`ISparTestWindow.EndSessionAsync`, internal): Windows sends `WM_QUERYENDSESSION` and `WM_ENDSESSION`; Linux runs the signal's path without the signal, the test's services the last work, the hold reported as the phase `session end held <ms> ms`. `SessionEndHostTests` (5) use it, and the black box's `EndSessionAsync`, on both hosts.
  - **`SparBlackBox` on Linux** (`src/Spar.Testing/BlackBox/Linux/`, six files, about 1,250 lines): `LinuxDesktop` (one private Xvfb per test process from :180, `-displayfd`, abstract socket only, `KillOnParentExit`; a second one, `InputDisplay`, for Spar's tests that send XTEST input; one `dbus-daemon --session` per launch, `KillOnParentExit`; the prerequisites: Xvfb, dbus-daemon, an `org.a11y.Bus` service, libgio), `LinuxAppProcess` (the app with `KillOnParentExit` and its whole environment, every process of the launch tagged `SPAR_BLACK_BOX_LAUNCH=<id>`, its bus `SPAR_BLACK_BOX_BUS=<id>`, so the tree is found by environment after the system adopted it; `SIGSTOP`, `SIGKILL`, `SIGTERM`), `Gio.cs` and `DBusClient.cs` (GDBus synchronous calls of 2 s with `NO_AUTO_START`, replies as managed values, no variadic function), `AtSpiBus` (the accessibility bus from the launch's session bus, the app's connection by pid through the bus daemon) and `AtSpiAutomation` (`IBlackBoxAutomation`). The data root (`TestDataRoots.NextBlackBox`, under `artifacts/linux-homes/black-box/<run>/`) holds `home`, `data` (`XDG_DATA_HOME`, `app.LocalAppData`), `config` (`XDG_CONFIG_HOME`, `RoamingAppData`), `cache`, `state` and `tmp` (`TMPDIR`, `TempFolder`); `XDG_RUNTIME_DIR` is `/dev/shm/spar-bb-<root hash>-<test pid>`, shared by launches on one root (the single-instance socket is there). `SparBlackBoxPublish` is on by default on Linux too.
  - **The automation, member by member:** `TitleAsync`, the frame's `Name`; `CloseWindowAsync`, the frame's `window.close` action; `FindDialogAsync`, a dialog or alert node that shows, walked for its labels (a button's own label left out) and push buttons, its `Title` the app's name (substituted, as decided); `PressAsync`, the button's `click` action; `FindAsync`, `InvokeAsync` (`DoAction(0)`) and `ValueAsync` (`Text.GetText`) on WebKit's nodes, matched by their `computed-role` attribute and name, a native node by its AT-SPI role, a text element by its own text (U+FFFC out); `BoundsAsync` (window extents), `FocusAsync` (`GrabFocus`), `WindowPartsAsync` (window extents; no handle, no title bar), `TopLevelWindowsAsync` (roles and menu items), `DialogButtonBoundsAsync` and `DumpAsync`; `EndSessionAsync` sends `SIGTERM` and waits for the exit or 5 s. A node answering `UnknownMethod`, `UnknownObject`, `ServiceUnknown`, `NameHasNoOwner`, `Disconnected` or `NoReply` is gone, never a failure.
  - **The lifecycle suite on both hosts:** `BlackBoxFixtureTests` (10 of its 12 tests on Linux), `SessionEndHostTests` (5) and `BudgetTests`' black-box budget; `WindowSupport.SkipUnlessBlackBox` skips with `BlackBoxPlatform.Problem` and fails with `CI=true`. The Notebook's black-box tests (6) run on Linux too.
  - **Found by the Notebook's black-box tests on Linux, fixed:** a browser profile folder that is relative or cannot be created stopped a Linux app with exit 70 and only a log line, where Windows shows its start-up message; the Linux host now reports it as Windows does (`StartupFailure.ReportConfiguration`: the line, then GTK's alert dialog or a message program, the automated mode excepted), and says "browser profile folder" as Windows does, not "browser data folder".
  - **Found after the runs, fixed:** the runtime folders of roots that several launches share stayed in `/dev/shm` (116 after ten runs); `LinuxDesktop` deletes the test process's own at exit (folders only) and those of gone test processes at its first launch (checked once: 0 left).
  - **The Linux host tests moved** from `tests/Spar.Tests` to `tests/Spar.WindowTests/Linux/` (`LinuxHostTests`, `LinuxHangTests`, `LinuxPageSecurityTests`, `LinuxStartupTests`, `X11Frames`, `X11Close`), on `ProbeApp`, a launcher over `SparBlackBox` (traits `Surface=BlackBox` and `App=BridgeProbe`, one collection run alone); `LinuxAppRun.cs` deleted; `LinuxSessionEndTests` (5) added; `LinuxHostUnitTests` (with the scheme gate's new test) and `LinuxStartupUnitTests` stay. `tests/Spar.Tests` has 33 files. BridgeProbe stays a tested app of its own beside WindowHost (`SparTestedApplication` with `Aliases="BridgeProbeApp"`, both define `Program`): its page runs the plan each test names and its `Program.cs` takes the browser opt-ins, a frame address, a profile folder, a slow disposal and its own start-up message from the environment, which only a Production build that reads them at run time can try; folding them into WindowHost would put those switches into the window tests' own app.
- **Choices made while building, for review:**
  1. **`PosixSignalRegistration`, not `g_unix_signal_add`:** the runtime installs its own handlers for both signals; GLib's would replace them with its own `sigaction`, and in a test process it would take the runner's Ctrl+C.
  2. **Exit 0 at session end, 128 + the signal for a second one;** a disposal still running at 4 s is cut with exit 0 too (the session ended; Windows ends such a process without a code of its own).
  3. **Signals are taken only once the window has started;** before that (the start-up checks, tens of ms) the runtime's default ends the process with 143 or 130.
  4. **One D-Bus session per launch**, neither the Window surface's per-process session nor one per test process: each app gets its own accessibility bus and portals, which end with it. The Window surface's Xvfb is not reused either: it lives in `Spar.Linux`, and the black box must work without an in-process window.
  5. **Dialogs are walked, not read with `Cache.GetItems`** (the spike's recommendation): GTK 4's cache holds only nodes a client has reached (pitfalls).
  6. **WebKit's elements are invoked with `DoAction(0)` without `GetActions`**, which WebKitGTK never answers; GTK's buttons use `GetActions` and `click`.
  7. **Inherited `WEBKIT_*` and `JSC_*` are removed from a Linux app's environment, as `WEBVIEW2_*` is on Windows, but `o.Environment` may set them** (Windows refuses `WEBVIEW2_*` there), so the tests of the host's filter can give them to the Production app. The session's desktop, bus, accessibility switches (`GTK_A11Y=none` hides the app) and launcher tokens are removed, and the fixture's own variables refused.
  8. **Internal options for Spar's own tests** (`Display`, `SessionBus`, `Wrapper`, `DevelopmentVariables`, `SparProject.Built`), so the moved start-up tests keep their mount namespaces, missing display and fake portal, and the runner's Development, WebAssembly and Notebook builds (`SPAR_LINUX_HOST_APP`, `SPAR_LINUX_WASM_APP`, `SPAR_LINUX_NOTEBOOK`) still run. No public API changed; the public members' documentation names the Linux behaviour.
  9. **Five hang checks that needed the Development build's scripted answers now run on the Production probe too,** answered through AT-SPI (`ProbeApp.AnswerAsync`): Reload page, a page hung before it connected, a close that meets a hung page, a silent guard, and WebAssembly after Reload page. The Wait schedule (a 500 ms heartbeat), the automated mode, the stall trigger, a raised WebKitGTK minimum and the second loss's exit 5 still need the Development build (the runner's `windowdevprobe`).
  10. **Two fixture tests stay Windows-only, with their reasons:** the job object's (Linux's `KillOnParentExit` shows only when the test process dies; measured by the spike) and the network `LOCALAPPDATA` (Linux mounts a network file system at a local path). Two were renamed: `…driven_through_UI_Automation` to `…driven_through_the_accessibility_API`, `…when_Windows_ends_the_process…` to `…when_the_OS_ends_the_process…`.
  11. **The CI image's two findings, fixed in the tree** (the coordinator's request): the one-scheme gate is reentrant per holder (the test that opens a window, as the test framework's hook names it, or its test app; `SchemeGate`, unit-tested), every other request keeping the fair queue; and nothing is ended at `ProcessExit` in a Linux test process, so GDK never meets a closed X connection there (the helpers end through their tether once the process is gone).
- **Gaps in automation parity:** the fixture's real input (internal, Windows Sandbox's) throws `PlatformNotSupportedException` on Linux; `WindowParts` has no handle and no title bar (GTK 4 draws its own); bounds are window coordinates (GTK 4 on X11 knows no window position); `TopLevelWindow.Handle` is 0 and its class is the AT-SPI role; context-menu items, focus and bounds are built but no Linux test reads them.
- **Numbers:** Linux (WSL2, Xvfb, GTK 4.14.5, WebKitGTK 2.52.6, at-spi2-core 2.52.0; Production NativeAOT apps): launch to ready (the `ui-ready` line and the window over AT-SPI) 228–255 ms; warm launch to `ui-ready` 213.1 ms median (n = 10), working set 159.4 MB, 374.5 MB with WebKit's processes; a page button found and invoked over AT-SPI 123–147 ms (a walk of about 100 nodes); `window.close` to exit 23–49 ms; the failed relaunch's dialog 2,094–2,149 ms after the launch (its 2 s wait included), "Close" to exit 8 270–283 ms; `SIGTERM` or `SIGINT` to exit 0 22–37 ms (hold 11–29 ms); with a 15 s disposal held 3,998–4,001 ms and exit 0 at 4,004–4,008 ms; a second signal to exit 143 7–8 ms; a test window's session end 26–31 ms, 4,000 ms with slow guards and disposal. Tests on Linux: `Spar.WindowTests` 192 (164 passed, 28 skipped) five times in a row with exit 0 and 0 processes left (316–319 s each; an earlier series of five had one failure of part 1's `PageApiTests.A_form_is_filled…`, not seen again), NativeAOT 192 (164, 16 skipped, 12 explicit); the probe tests 67 on the Development build (64, 3 skipped) and with the WebAssembly build and the Notebook (62, 5 skipped); the solution 1,028 (987, 41 skipped); the Notebook's black box 6 of 6; the boundary test 1 of 1; builds with 0 warnings. Windows: the solution 1,028 (1,005, 23 skipped), `Spar.WindowTests` 192 (112, 80 skipped) on CoreCLR and NativeAOT (112, 68 skipped, 12 explicit), the Notebook's black box 6 and window journeys 10, `tests/AppModel.Tests` 11, the size check 0 errors, the boundary test 1 of 1. WSL's DNS was down for the last runs, so they set `NuGetAudit=false` in the environment (NU1900 otherwise; packages from the cache).

- **Not verified:** a desktop's real session end (systemd, logind), Wayland, GNOME, KDE; these tests in the CI image; two test processes at once; the fixture's real input on Linux; the Windows Sandbox lanes (they need the user).
- **Addendum: the black box in the CI container** (2026-11-21, the coordinator's follow-up; choices for review, continuing the list above):
  12. **Traits, so every CI run is `--no-skips`:** `Probe=Development` (needs the probe's Development build: the Wait schedule, the automated mode, the raised WebKitGTK minimum, the second loss's exit 5), `Probe=None` (runs the Notebook, not the probe), `OS=Windows` (`BlackBoxFixtureTests`' job object and network `LOCALAPPDATA`), `Kind=Measurement` (the flood's numbers, also `Explicit`: an explicit test still counts as skipped in `dotnet test`'s totals), `Needs=WritableProcSys` (a real denial of user namespaces through bwrap's `--disable-userns`, which writes `/proc/sys`; a container keeps it read-only).
  13. **`eng/linux-ci/run.sh` gains `apps`** (the probe's Development build with `SparDevServer=false`, its WebAssembly build from a copy of its folder, the Notebook for linux-x64) and **`blackboxdev`** (`App=BridgeProbe` without `Probe=None` on the Development build); **`blackbox`** runs `Surface=BlackBox` without `Probe=Development`, `OS=Windows`, `Kind=Measurement` and `Needs=WritableProcSys` with `SPAR_LINUX_WASM_APP` and `SPAR_LINUX_NOTEBOOK`. `check` uses the mounted tree's network switch, since the workflow runs it before `copy` (it failed there before). The `linux-window` job gets the two steps and a 90-minute limit (each black-box run takes about 5 minutes; the sequence about 30 in the container on this machine).
  14. **The image adds `python3-gi` and `libglib2.0-bin`** in a layer of its own (18 packages, 7.2 MB to download): the theme test's fake Settings portal and the external-link test's `gio mime` check.
  15. **Found in the container, fixed:** the session end's final line could come after the process ended when the normal exit was quicker (`LinuxApp` now waits for it before closing the log; `LinuxSessionEndTests.A_page_guard_that_keeps…` failed once before the fix, passed after), and `An_app_not_ready_in_time…` raced other tests' roots (`TestDataRoots.Created`, per flow).
  16. **Container runs** (image `spar-linux-ci:m1c-step7`, `check copy client build window windowaot apps blackbox blackboxdev`): run 1 before the fixes (diagnostic); runs 2 and 3 after them. `Spar.WindowTests` window 66 (62 passed, 4 skipped) in all three, NativeAOT the same; `blackbox` 80 (79 passed, 1 failed each: run 2 the session-end line, since fixed; run 3 `LinuxStartupTests.A_missing_bwrap…`, whose GTK start-up message did not show, not reproduced); `blackboxdev` 63 (run 2: 62, one exit 139 of the Development probe after its window was destroyed in `A_page_that_hangs_before_it_connects…`, not reproduced; run 3: 63 of 63); the Notebook's black box not reached in the container (the step stops at its first failed run; 6 of 6 in WSL); the Notebook's window journeys 9 or 10 of 10 (typed characters lost or a stale count: the other agent's journeys, not changed here). No skip in any `--no-skips` run; 0 test processes left in the container after each run.
- **Addendum: the container failures' causes** (2026-11-21, the coordinator's second follow-up; choices for review, continuing the list above; evidence `docs/evidence/m1c-blackbox.md`, "The container failures: causes and fixes", and `docs/evidence/m1c-blackbox/container-failures.txt`):
  17. **Typed keys: the keyboard is held per window.** The display's one keyboard state was the cause (another window's Control+End interleaved with this one's), so `XInput` gives the keyboard to one window from its first key down to its last key up (others wait; a stroke left half-done passes on after 5 s). Kept with it: a window whose document lacks the focus is given the focus, and the key waits until the document has it (`focused`, a new page-engine operation); each key is confirmed by its own event type (`inputCount` and `inputAfter` take `type`); a key that does not arrive is a warning in the app's log, not a failure (the test's own assertion decides); the server grab around the focus and the key stays as a guard, though it alone did not help. Looped runs of the 10 journeys: 5 of 10 failed before, 9 of 20 with the focus wait, 0 of 20 with the keyboard held.
  18. **The Notebook's save updates from the latest state** (`setCurrent(latest => …)`): it rebuilt the state from the one it was called with, which dropped an edit React had not applied yet (a new vitest reproduces it).
  19. **The black box's Xvfb runs with `-noreset`:** it has no long-lived client, and a server that resets when its last client leaves refused connections meanwhile (42 of 3,000 back-to-back opens). The Window surface's Xvfb keeps the test process's GTK connection for the whole run and is not changed.
  20. **A window's dialogs are taken down, and their answers waited for, before the window is destroyed** (`WindowDialogs`, at most 1 s, a warning past it): `GtkAlertDialog` otherwise destroyed a freed window when the answer of a dialog cancelled just before the teardown was read (exit 139). The alternative, holding a reference on the dialog's window, needs the window, which GTK does not expose.
  21. **The loop thread clears GDK's current GL context before it ends** (`gdk_gl_context_clear_current`): left to the thread's exit, its finalisation aborted the process (exit 134 after "exit 0") with hardware acceleration `Always`.
  22. **The stall-probe test waits for the development trigger's own line,** since another stall can come first under load.
  23. **The relay tags every message with its document's random id** (16 hex digits and a TAB; `BridgeRelay.js`, `DocumentTags`), found after the three: a Notebook page's first call and `ready` came while no document was open, between a load's start and its commit, and were dropped (4 of 65 looped runs of the window journeys). The host now holds a not-yet-committed document's messages (at most 256) and hands them to it at its commit, ignores those of the last four documents that have gone, and drops the hold, with a warning, when a document elsewhere commits or the web process ends. A replaced document's late messages stay a warning, as on Windows. The page's SDK and the wire format are unchanged; the tag is between the relay and the host only. The Windows host's bridge is unchanged: its journeys never showed this.
  24. **Item 23 is withdrawn** (the same day): the relay's document tags were reverted before any run of the full sequence. In a copy whose relay tagged every message, the dropped `call` and `ready` of 5 failing runs all carried the tag of the document the reload replaced, not a new document's: the search journey reloaded while its first page was still starting. The host is unchanged; the journey waits for its first page's empty list before it reloads. The host's warning for a going-away document's messages stays, as on Windows.
  25. **The keyboard hold of item 17 is now the input hold:** one window holds the display's input from its first key or button down to its last up, and other windows' keys, pointer events (motion and wheel included) and focus changes wait; a hold left by a failed test passes on after 5 s. Found after the reverted item 23: a click on "New page" opened no dialog once in 25 runs of the journeys (not instrumented; the pointer shares the display's one state as the keyboard does). Then 30 of 30 runs of the 10 window journeys.
  26. **The Linux session end starts its last work when the window is gone, in time or not** (`SessionEnd.RunAsync`): it had started it only when the window closed within the 4 s, so a test window whose teardown outlasted the budget under load kept its services until the test's end (`SessionEndHostTests`' slow case, once in the full sequences). The final line still reports "the window did not close in time" then; an app's last work is its normal disposal, so apps are unchanged.
  27. **Runs:** three full sequences in fresh containers on the final tree, one after another, every step green: `window` 66 (62 passed, 4 skipped) and the journeys 10 of 10, `windowaot` the same, `blackbox` 80 of 80 and the Notebook's 6 of 6, `blackboxdev` 63 of 63, each time; before them the journeys 30 of 30 looped runs, `Spar.WindowTests` in WSL 192 (164 passed, 28 skipped) and on Windows 192 (112 passed, 80 skipped). Not run: the Windows Sandbox lanes.

## The Linux CI container's AppArmor profile (implementation record; choice for review; 2026-11-21)

The first run of the `linux-window` job on GitHub (run 44879469247, Docker 29.4.2, kernel 7.0.0-1020-azure, `kernel.apparmor_restrict_unprivileged_userns = 1`) failed `run.sh check`: `bwrap: Failed to make / slave: Permission denied`. This supersedes choice 1 of "The Linux CI image and job; online and offline on Linux", whose reasoning held only in part: under `docker-default` (ABI 3.0) the user namespace was created, as reasoned, but the profile's `deny mount,` refuses the mounts bwrap makes in its own namespaces. Docker Desktop's VM has no AppArmor, so no local run could show it.
- **Chosen:** a profile of our own, `eng/linux-ci/apparmor-profile` (`spar-linux-ci`): Moby's `docker-default` template with `mount,` and `pivot_root,` allowed in place of `deny mount,`, ABI 3.0 as Moby declares it, every other rule kept. The job loads it with `sudo apparmor_parser -r` and starts the container with `--security-opt apparmor=spar-linux-ci`. The container keeps Docker's default capabilities, without `CAP_SYS_ADMIN`, so its processes can mount only in namespaces they own. The runner's sysctls are unchanged; loading the profile is the job's one change to the runner, which is discarded after the job.
- **Not taken:** `apparmor=unconfined`, which puts the container under the restriction of unconfined user namespaces (namespaces without capabilities, so bwrap still cannot mount; actions/runner-images#10443); `sysctl kernel.apparmor_restrict_unprivileged_userns=0`, which lifts that restriction for every process on the runner; `--privileged` or `CAP_SYS_ADMIN`; turning WebKit's sandbox off, which Spar refuses in Production.
- **Checked:** the profile compiles with `apparmor_parser -Q -K` (Ubuntu 24.04's parser, WSL); `run.sh check` now prints the profile it runs under. Not yet verified: the job on GitHub with it (the next run).

## Linux test runs write no core dumps (user decision, 2026-11-21)

The crash dumps of M1c's Linux runs had built up in Windows' `%TEMP%\wsl-crashes`: 10 files, 55.8 GB, deleted with the user's agreement. WSL's kernel, Docker Desktop's included, pipes every crashed process's core to `/wsl-capture-crash` (`core_pattern` `|/wsl-capture-crash %t %E %p %s`), and a crashed test app's core is about 6 GB. Some tests crash apps on purpose, so every run can add more.
- **Decided:** the maintained Linux runners (`eng/linux-ci/run.sh`, `eng/wsl/step.sh` and the M1c evidence runners `docs/evidence/m1c-*/wsl.sh`) set the core limit to 1 byte (`prlimit --core=1 --pid $$`) unless `SPAR_CORE_DUMPS=1`, which keeps the dumps for reading a crash with `gdb`, as the GTK dialog crash was read. Every process they start inherits the limit. The spikes' scripts are left as they are.
- **Why 1 byte:** the kernel ignores the core limit for a piped `core_pattern`, except a limit of exactly 1, which it treats as "no dump". WSL's default limit is already 0, and dumps were written anyway. Measured in WSL: a `sleep` ended by `SIGSEGV` left a 446 KB dump under limit 0 and none under limit 1 (the shell no longer reported "core dumped"). bash's `ulimit -c` counts in blocks, so it cannot set 1 byte.
- **Not taken:** WSL's own setting for the number of dumps it keeps, in `%UserProfile%\.wslconfig`, which is the user's machine setting and not the repository's; a limit set by the black-box fixture for the apps it launches, which a test run started by hand would then need. That run still writes dumps.
- **Addendum (2026-11-21, the second run):** under `spar-linux-ci`, bwrap started, but WebKit's sandbox could not mount its new `/proc`: `bwrap: Can't mount proc on /newroot/proc: Operation not permitted`, in every window test and in `run.sh check`'s new probe (`bwrap … --proc /proc --dev /dev true`). The runner's kernel log had no AppArmor denial for it. The kernel lets a user namespace mount a new procfs only where the existing `/proc` is fully visible, and Docker masks parts of it (files such as `/proc/kcore` and `/proc/keys` covered by other mounts). The container now also runs with `--security-opt systempaths=unconfined`, which leaves `/proc` and `/sys` unmasked. What the masks protected stays protected otherwise: the profile keeps `docker-default`'s `deny` rules for `/proc/kcore`, `/proc/sysrq-trigger` and writes in `/proc` and `/sys`, the container has no `CAP_SYS_ADMIN`, and the tests run as uid 1000. This is reasoning from the kernel's rule and the absent denial; the next run settles it. If `check` fails again, it prints the mounts on `/proc`. Docker Desktop's VM passed the same probe without the option; that difference is not explained.

## The black box gives each launch's session bus no display (implementation record; 2026-11-21)

The bullet "Addendum (2026-11-21, the second run)" just above, under "Linux test runs write no core dumps", belongs to "The Linux CI container's AppArmor profile": it records the `systempaths=unconfined` change.
- **The third CI run of the `linux-window` job:** with `/proc` unmasked, every window test passed on the runner (CoreCLR and NativeAOT, the Notebook's journeys too), and one black-box test failed: the app was ready after 0.5 s, but GTK logged "Unable to register the application: Timeout was reached" 25 s later, and the fixture found no window over AT-SPI in 30 s. The cause was in the black box, not the runner, and reproduced in WSL. at-spi2-core's bus launcher publishes its bus's address on the display's root window (`AT_SPI_BUS`), and the registry and GTK read it before asking their own session bus. All launches of a test process share one Xvfb, so parallel launches overwrote each other's address, and a registry registered on another launch's bus. Measured with a script that starts a private session bus, `org.a11y.Bus` and the registry, eight at a time for ten rounds: with a shared display, 10 of 80 started (one per round) and the rest timed out after 25 s; without a display, 80 of 80 in 104–112 ms. Fixed in two parts. A launch's session bus gets no `DISPLAY`, so no launcher publishes an address and every client uses its own session bus. And the fixture starts the accessibility bus and the registry before the app, as a desktop session has them, failing the launch with its own message after 20 s. The pre-start alone turned the hang into that message once in each of three runs of the black-box tests in WSL; with both parts, three runs had 0 failures (88 tests each: 77 passed, 11 skipped).

## M1c step 7's choices accepted (user decision, 2026-11-21)

The user accepted, as built, the choices recorded for review in "Linux window tests", "The Linux CI image and job; online and offline on Linux", "The Linux black box and SIGTERM" (choices 1–27 with its addenda), "The Linux CI container's AppArmor profile" (with `systempaths=unconfined`, from the addendum of the second run) and "The black box gives each launch's session bus no display". The Docker image `spar-linux-ci:m1c-step7` stays on the user's machine for local container runs.

## M1c budgets measured at the exit (implementation record; 2026-11-21)

Plan M1c, "Exit". Evidence: `docs/evidence/m1c-exit.md`, raw logs and the runner in `docs/evidence/m1c-exit/`. The method is the M1b exit's (decision "M1b budgets measured at the exit"): `tests/Spar.WindowTests/BudgetTests.cs` in Release on both test lanes, WindowHost's Production NativeAOT executable through the black-box fixture, 1 warm-up launch and 10 on the same data root, 20 window tests, 200 echoes. In WSL 2 Ubuntu 24.04.5 on the development PC, WebKitGTK 2.52.6 and GTK 4.14.5, on private Xvfb displays only, on the code of `308364a` (CI green).
- **Measured, every budget met:**
  - **Launch to `ui-ready`, warm (≤ 350 ms):** 220.9 ms (195.3–229.5, n = 10) with the CoreCLR test lane and 213.2 ms (205.2–231.5, n = 10) with the NativeAOT lane, from the process's creation; the launch alone, twice, 217.1 and 222.5 ms. On CI's runner 267.0 ms.
  - **Small unary round trip on the desktop bridge (≤ 1 ms):** median 0.00 ms, p95 1.00 ms (0.00–4.00, n = 200) on both lanes, at WebKitGTK's 1 ms page timer; the mean of the 200 readings, which estimates a call under one tick, 0.285 ms (CoreCLR) and 0.245 ms (NativeAOT).
  - **Window test, open, the 8-step journey and close (≤ 500 ms):** 189.8 ms CoreCLR (p95 221.7) and 189.4 ms NativeAOT (p95 236.1); on CI's runner 301.7 ms.
  - **Notebook NativeAOT executable (≤ 8 MB):** 5,141,248 B, the same in CI. It ran alone in an empty folder to `ui-ready` on the black box's private Xvfb, 12 times, and after a lost web process.
  - **Total PSS with the web engine (recorded):** WindowHost and its WebKit processes 2 s after `ui-ready`, 200.0 MB (199.8–202.8, n = 10) and 199.9 MB in a test process with no WebKit of its own; 166 MB inside the whole class, where the test process's own WebKit windows take a share of the shared libraries, so the class's figure is not the one recorded. The Notebook, one run: 233.2 MB.
- **Built for the measurement** (test code only): `BudgetTests` records the total PSS on Linux (`/proc/<pid>/smaps_rollup` of the launch's processes, the same launches as the working set) and the round trip's mean on both hosts; the Notebook's Linux test also records its launch from the process's start, from the host's log.
- **The Notebook's own launch** (not the budget's measure, which is WindowHost's as at the M1b exit): 305.7–311.2 ms median from the process's start in four runs of 11, 281.6–316.9 ms by the test's clock in six; steps 1–6 recorded 210–228 ms with the same test, when the launcher gave the app no session bus, and CI's runner recorded 391.5 ms (374.1–472.2), over 350 ms. One run with GTK's accessibility off (`GTK_A11Y=none`, in an edited copy only) gave 283.9 ms, inside the normal runs' spread, so the cause is not found. The budget is not changed.
- **WebKitGTK older than 2.48, exit 4:** `LinuxStartupUnitTests.WebKitGTK_2_48_is_the_oldest_supported` (the decision with the real minimum) and `LinuxStartupTests.A_WebKitGTK_older_than_required_is_exit_4_with_a_message` (the real check on the probe's Development build with the minimum raised above the installed version) passed in WSL; the second passed in CI's `blackboxdev` step of run 44888383564 at `308364a`. No older WebKitGTK was run.
- **The shared host-lifecycle suite** (`tests/Spar.WindowTests`, from CI run 44888383564, not run again): 87 tests in 13 classes; Windows passed 86 on both lanes (`NetworkStatusTests` is Linux's, Windows checks offline in the Sandbox), Linux 81 on CoreCLR (62 on the NativeAOT lane, which in CI runs the window surface only). Windows only: `InstallPathTests` (`MAX_PATH`), two `BlackBoxFixtureTests` (the job object, a network `LOCALAPPDATA`); not yet on Linux: the page's error reports (left by M1c step 4); one test window's own scheme (WebKitGTK follows the process); the loopback connection count (port 443 needs root). The Linux host's own classes add 61 tests in the Production probe's black-box step and 63 on its Development build.
- **Not verified:** an older WebKitGTK at run time, the cause of the Notebook's slower launch, a cold start, a real desktop or GPU, the edited `BudgetTests` run on Windows (built only).

## The Notebook's Linux launch: the portal started inside the launch (implementation record; 2026-11-21)

Follows "M1c budgets measured at the exit", which left the cause of the Notebook's slower launch open (306–311 ms from the process's start against 210–228 ms in steps 1–6). Evidence: `docs/evidence/m1c-notebook-launch.md`, raw logs and the runner in `docs/evidence/m1c-notebook-launch/`.
- **Measured:** an interleaved A/B in WSL on Production NativeAOT executables launched directly, a new data root each launch, 1 warm-up and 10 launches per condition, rotating order, private Xvfb per condition. From the process's start to `ui-ready`, median: today's Notebook 227.2 ms with no session bus (the launcher of steps 1–6), 295.6 ms on a private bus with the accessibility bus and registry started first (the black box), 219.9 ms with `xdg-desktop-portal` also started first, 227.5 ms on a bus that cannot start a portal; GTK's accessibility off 308.9 ms and WebKit's off (`WEBKIT_A11Y_BUS_ADDRESS` empty) 317.0 ms, both at the black box's time. The Notebook of `97d26c5` (step 6) built and run beside it: 229.6, 291.9 and 223.5 ms in the same three conditions, so code changes are not the cause. WindowHost: 156.1, 225.1 and 146.9 ms.
- **The cause:** the phase from the window shown to the page committed grew from about 45 to about 120 ms. A sandboxed WebKit process asks `org.freedesktop.portal.Desktop` for real-time threads (`WTF::RealTimeThreads`: `g_dbus_proxy_new_for_bus_sync` on the main thread, without `DO_NOT_AUTO_START`, then `MakeThreadRealtimeWithPID`), which on the black box's fresh bus starts the portal and waits for it: about 155 ms in a bus monitor, its GTK backend failing 15 times for want of a display. A desktop session has the portal running, so this was the test environment's cost, not a user's. Accessibility (`68db043`'s registry start included) costs nothing measurable.
- **Fixed in the black box:** `LinuxDesktop.StartPortal` starts the portal on the launch's bus after the accessibility bus and before the app, as a desktop has it; a bus without a portal refuses at once and a portal that fails is left to the app, neither failing the launch. Spar's host is unchanged and accessibility stays on. Before and after, through the fixture: WindowHost's launch (`BudgetTests`, profile reused) 217.1 and 222.5 ms → 146.2 and 153.1 ms; the Notebook's test from the process's start 305.7–311.2 ms → 226.2 ms; PSS unchanged at 200.0 MB. The black box's Linux tests after the change: 82, 77 passed, 5 skipped, 0 failed. Each launch costs the fixture about 80 ms more before the app in WSL, nothing where no portal is installed.
- **The 350 ms budget on a desktop's footing:** met, the Notebook about 220 ms and WindowHost about 147 ms. The M1c exit's 213–222 ms for WindowHost included about 70 ms of the portal's start; the measured budget line stays as recorded, with this correction beside it.
- **Not explained:** CI's 391.5 ms for the Notebook; its image installs no portal, so the cause above does not apply there.
- **Not verified:** a real GNOME or KDE session, which process (web or network) made the request, a CI run with the change.

## The Linux CI job in two parts (implementation record; 2026-11-21)

The `linux-window` job took 18 minutes at `308364a`: about 2.5 for the image (cached), the container, the copy and the build, 1.6 for the window tests on CoreCLR, 2.6 on NativeAOT, 5.1 for the black-box tests on the Production probe and 5.6 on the Development probe. Two changes (`a20c1e2`):
- **The Development-probe step runs only `Probe=Development`** (4 tests: the automated mode, the Wait schedule, the exit-5 second loss, the raised WebKitGTK minimum), not all 63 probe tests again: every other one ran on the Production probe, which is what apps ship. In xUnit's filter, two `--filter-trait` options select either trait, so the step takes one.
- **The job is a matrix of two parts on runners of their own,** `window tests (Linux container)` and `black-box tests (Linux container)`, each repeating the image, the copy and the build. Neither is a required check on `main`, so the rename changes no rule.
- **Measured** in the first run (44898907157): window tests 6.8 minutes, black-box tests 9.4, every check green; the PR's slowest job is now the black-box part, then Windows' `tests` lane (7.0). Not taken: running the probe tests in parallel, which send real input, read pixels and count windows on one shared display, so they would need a display per launch and would compete for the runner's 4 CPUs in timing-sensitive tests.

## M1d's decide-first questions (user decision, 2026-11-21)

- **Where page tests run** and **typed page-side mocks:** already settled by "Page tests: @spar/app/testing" (2026-11-16). Page tests run in Node with Vitest against `fakeSpar()`, a fake backend on the wire keyed by the generated commands, so a wrong command, value or error case fails `tsc`. The research review's browser-mode mocks are not needed, since that decision chose Node over Vitest's browser mode.
- **The build identity, decided (user, 2026-11-21): a minimal identity in M1d, the rest in M2.** M1d embeds in the executable its variant, the git revision, whether the tree was dirty and the contract fingerprint. The host writes them in its first log line, and `spar doctor` shows them. M2 adds the publish receipt (tool versions, the page payload's hashes) and devtools' report of sources newer than the build. Not taken: all of it in M2, or all of it in M1d, which is already the largest M1 slice.

## M1d step 1's shape: Debug through the app's own origin (user decisions, 2026-11-21)

The mechanism is decided ("Debug serves Vite through the app's own origin", 2026-11-14): in a Development build the host fetches each page request from the Vite dev server in-process, on `https://app.localhost` and `spar://app`. Decided now:
- **How Vite starts: the app's own `package.json` `dev` script** (`npm run dev` in the frontend folder), so an app author can change the command. Chosen over Spar running Vite's script with Node directly, which would have avoided npm's wrapper on Windows; stopping the whole process tree and finding the server's address must therefore work through that wrapper.
- **A dev server Spar did not start:** `SPAR_DEV_SERVER_URL` in the environment (for an orchestrator such as Aspire, or a developer running Vite themselves), and the MSBuild property `SparDevServerUrl`, read at build time; with either, the app proxies to that address and starts nothing. Chosen over the environment variable alone, or neither in M1d.
- **When Vite fails to start or dies:** the window shows a Development error page with Vite's last output and a Retry button, and the app keeps running, so the backend and the debugger stay attached; the same text goes to `app.log`. Chosen over stopping with the start-up message.
- **Vite's output:** to `app.log` under the category `Spar.DevServer`, and to the app's console when it has one (`dotnet run`, `dotnet watch`). Chosen over `app.log` only.

## Debug through the app's own origin (implementation record; choices for review; 2026-11-21)

Plan M1d, build step 1 (decisions "Debug serves Vite through the app's own origin" and "M1d step 1's shape: Debug through the app's own origin"). Evidence: `docs/evidence/m1d-dev-server.md`, its runners `docs/evidence/m1d-dev-server/DevRun.cs` (Windows), `linux-run.mjs` and `wsl.sh` (Linux), raw logs in `docs/evidence/m1d-dev-server/logs/`. Design: `docs/design/dev-loop.md`, section 4, "As built".
- **Built:**
  - **The core's `src/Spar/DevServer/`** (eight files, about 1,400 lines), behind the feature switch `Spar.DevServer.IsSupported`: `DevServerSettings` (the switch and the build's values), `DevServerAddress` (the loopback rule), `DevServerState` (the plugin's record), `DevServerLauncher` (Node's check, `npm run dev`, a process's identity, a tree's end), `DevServerOutput` (the server's output file read into the log), `DevServerSession` (find, reuse or start, watch, retry), `DevServerProxy` (the request rules and the Debug policy) and `DevErrorPage`.
  - **The hosts:** `WindowsApp` and `LinuxApp` check Node and start the session after their prerequisites (Linux after every `setenv`); `AppWindow.UseDevServer` and `PageServer.UseDevServer` / `SchemeServer.UseDevServer` route every request but the violation reports to the proxy, read the forwarded headers on the UI thread (`ICoreWebView2HttpRequestHeaders`, declared and in the slot test; `webkit_uri_scheme_request_get_http_headers` and `soup_message_headers_get_one`), hold the request (a deferral; a reference), fetch on the pool and answer back on the UI thread; a server that stops navigates the window to its start address, which is the error page.
  - **`PagePolicy`** composes the Debug policy with the server's socket (`devServerSocket`): `ws://127.0.0.1:<port>` in `connect-src`, and each document's nonce in `script-src` and `style-src` (`style-src` not with `AllowInlineStyles()`); the one-marker split became a marked policy with a replace, which also serves `StyleNonce()`.
  - **`HostLog.For(category)`:** a logger of another Spar category (`Spar.DevServer`), to the log file until the app's logging exists and through it after, never on standard error.
  - **Spar.Sdk:** `SparUsesDevServer` (Development, a frontend, `SparDevServer` not false) turns the switch on, off with `Trim="true"` elsewhere; `Spar.DevServer.Frontend`, `Spar.DevServer.Intermediate` (the project's `obj/`) and `Spar.DevServer.Url` (`SparDevServerUrl`) are runtime configuration values, Development only.
  - **The Vite plugin** (`src/Spar.Client/src/vite/devServer.ts`, wired into `index.ts`'s `config` and `configureServer`, not for a preview or Vitest's `mode: "test"`): `html.cspNonce` set to the placeholder `spar-dev-nonce`; 127.0.0.1, the port and `strictPort`; `server.ws` (Vite 8's name for `server.hmr`'s socket) at `ws://127.0.0.1:<port>`; for a server the app started, the record, the identity at `/__spar/dev-server`, the record removed at close when still its own, its standard input read (and its end ending the server), and the 60 s watchdog.
  - **Development-only trigger:** `SPAR_DEV_INPUT` gains `key:Enter` (a `keyDown` with its text, so a focused button acts), for the Retry check.
  - **Tests:** `tests/Spar.Tests/DevServerTests.cs` and `src/Spar.Client/test/devServer.test.ts` (the evidence lists them); the boundary test's markers for this step and a control in `Spar.dll`; three static fields reviewed (the switch, the request limit, the forwarded headers) and the generated IID of the new COM interface.
- **Choices made while building, for review:**
  1. **The host composes the Debug policy and the nonce, not the plugin.** The design said the plugin sends the policy in Debug (`docs/design/security.md`, "Sent as a response header"); through the proxy the host replaces every header Vite sends with its own (`PagePolicy`, the one composer), worker scripts included, and swaps a fresh nonce for the plugin's placeholder in each HTML response. Why: one composer instead of a second one in TypeScript, and a server without the plugin, or a misconfigured one, can never loosen the policy; an HTML document without the placeholder is refused with the error page that names `spar()`. The plugin's own header for a page served straight from Vite (a browser on its URL, the loopback session of step 2) is not built: step 2 decides where that policy comes from.
  2. **The Debug nonce goes in `script-src` too.** `@vitejs/plugin-react`'s preamble is an inline module script, so the strict `script-src 'self'` needs the document's nonce in Debug; Release is unchanged. Vite also puts the nonce on its client's `<script>` and in `<meta property="csp-nonce">`, which its client reads for every `<style>` it injects.
  3. **The port is the app's choice, not Vite's:** a free 127.0.0.1 port (bound and released) passed as `SPAR_DEV_SERVER_PORT` and taken with `strictPort`, so the HMR client's port is known before Vite listens; a race with another program fails the start visibly, and Retry picks another. A server a developer runs keeps its config's port (5173 by default), also `strictPort`, so the client's port matches.
  4. **The record is the plugin's, the identity the app's:** the plugin writes its real address, Spar's instance id, Vite's pid and its start time once it listens (through a temporary file and a rename); the app reuses a record only when that process is alive with that start time (within 5 s) and `/__spar/dev-server` answers the instance. npm's and Vite's output is never parsed.
  5. **The server's output goes to `obj/spar-dev-server.log`, never to a pipe of the app's:** the server outlives every app run, and a pipe would break at the first restart. Each run reads the file (a reused server's from its end) into `app.log` under `Spar.DevServer` and to standard output (standard error keeps the host's own lines); the last 60 lines go on the error page. `NO_COLOR=1` and `npm_config_update_notifier=false` keep the file plain.
  6. **The server's standard input:** the app's own when it is a pipe (it then ends with the launcher's input, as decided), otherwise a pipe whose writing end the launched process inherits, so a console or an IDE's null device never ends it and Vite never reads the console (its shortcuts would take the developer's keys). Vite 8 never reads its input, so the plugin does, or a closed pipe would not end it (pitfalls).
  7. **Ending a server as a whole tree:** `Process.Kill(entireProcessTree: true)` on Vite's process (its bundler's workers below it; npm and the wrappers above it then end) and on the launcher this run started; only for a record that does not answer and on Retry, never at the app's exit, and never a process whose start time does not match.
  8. **Death is seen by polling the pid each second;** a server Spar did not start is not watched (no pid): its failure shows at the next document request or Retry. The window then navigates to its start address, which is the error page; Vite's own client's reload is not relied on.
  9. **The error page:** 503 for a document request (502 for anything else), what happened, the last 60 lines (escaped), what to do, and Retry with `autofocus`; its style and script are files of the proxy's own route (`/_spar/dev-server/page.css`, `page.js`), so the strict policy and Trusted Types hold; Retry posts to `/_spar/dev-server/retry`, which runs a new attempt (reusing an answering server, else starting one) and answers when it ends, then the page loads `/` again. An external server's Retry asks it again.
  10. **What the proxy refuses (security):** a server off this machine (only `http` on 127.0.0.0/8, `[::1]` or `localhost`, with nothing after the origin; the variable and the property alike, shown on the error page); every method but GET and HEAD (405); every `/_spar/` path but its own three (404), so the host's endpoints are never Vite's; a path that would leave the server's origin (it is appended, never resolved, then checked); Vite's redirects and encoded bodies (502); bodies over 64 MiB (502); and every request header but `Accept` and `Sec-Fetch-*` (no cookies, no conditional headers, so no 304). Requests off the app's origin were already 403 at the host. Not refused: Vite's own development surface on that origin (its `/@fs/` within Vite's `server.fs.allow`, `/__open-in-editor`, which its overlay uses) is reachable by the page through the proxy, as by any page on Vite's own origin; the page is the app's code under development, and the policy keeps every other origin away from both.
  11. **Node's check** looks for `npm` and `node` (`npm.cmd`, `node.exe` on Windows) in `PATH`'s folders on the main thread before the window (a few file checks), only when the app is to start the server; without them `StartupProblemKind.Missing` (exit 3) with the Node.js download page and `-p:SparDevServer=false`. `spar doctor`'s version check stays with step 6.
  12. **The session never reaches a signature a Production build keeps:** a first version passed it to the window's and page server's constructors, and `DevServerSession` stayed in the Production image's signature metadata (the boundary scan found the name, no code); it now arrives through `UseDevServer` methods called behind the switch, and the image has none of the step's markers. The Production Notebook went from 5,244,416 B (`HEAD`) to 5,243,904 B.
  13. **A request waits for the attempt under way** (the first load waits for Vite instead of showing the error page while it starts), with 120 s per request and 60 s for a new server to record its address; neither is a call deadline (decision "No default deadlines"): both bound a development tool.
  14. **No change to CI:** the new tests run in the existing steps (`Spar.Tests` on both lanes, the TypeScript SDK's `npm test`, the build tests' boundary); no CI step runs a Debug build with its development server.
- **Numbers** (the Notebook; the evidence has every run): 0 violations on both hosts; Fast Refresh 36 ms median on WebView2 (35–43) and 32 ms on WebKitGTK (31–37), state kept 5 of 5 on both, against the spike's 23 ms on a smaller app; CSS 47 ms median on both, Tailwind's rebuild included (the spike: 9 ms, plain CSS); a reused server checked in 25–29 ms, `ui-ready` 595–663 ms on Windows and 500–514 ms on Linux, against 448 and 387 ms with the bundle embedded; a new server ready in 640–721 ms on Windows and 264–346 ms on Linux; Vite killed to the error page in 452 and 361 ms, Retry to a new server ready in 0.7 and 0.26 s; a page load's 34–35 proxied requests 0.5–24.1 ms each.
- **A worker in a real Debug window:** a module worker added to the Notebook's frontend for one run, fetched through the proxy, had `eval` refused by its own response's policy on both hosts (`docs/evidence/m1d-dev-server.md`).
- **Runs:** Windows: `dotnet build Spar.slnx -c Debug` with 0 warnings; the CoreCLR tests without window and black-box 1,069 (1,046 passed, 23 skipped); `Spar.WindowTests` 192 (112 passed, 80 skipped, as before); the Notebook's window journeys 10 of 10 and black box 6 of 6; its page tests 12 of 12; the NativeAOT lane `Spar.Tests` 734 (7 skipped), `WireShapes.Tests` 79, `Notebook.Tests` 12, `Spar.WindowTests` 192 (67 skipped, 13 not run) and `Notebook.EndToEndTests` 20 (4 not run); `Spar.BuildTests` 6 of 6 (the production boundary included); `Spar.PackageTests` 12 of 12; the TypeScript SDK's 273 tests and `npm run check`; `tests/AppModel` 11; `eng/check-size.cs` clean. Linux (WSL, `docs/evidence/m1c-blackbox/wsl.sh sync client appmodel build test window windowaot devprobe wasmprobe notebookexe windowdev windowdevprobe notebook`): 1,069 (41 skipped), `Spar.WindowTests` 192 (28 skipped) and its NativeAOT lane 192 (15 skipped, 13 not run), the probe's black box 67 (5 skipped) and on its Development build 67 (3 skipped), the Notebook's black box 6 of 6, 0 failures and 0 processes left (one tethered `dbus-daemon` wrapper was still there right after the NativeAOT lane, gone at the next census); then `sync boundary`: the production-boundary test 1 of 1. Two small changes came after the full suites (the proxy takes a broken body read as an unreachable server; the session's output stops before the services are disposed): after them `Spar.Tests` (739), the build, package and Linux boundary tests above, and the Debug runs `final`, `worker` and `kill` ran again, all passing.
- **Not verified:** `dotnet watch` restarts and F5 (steps 3 and 6); the overlay through the proxy; `SparDevServerUrl` on Linux; the missing-Node dialog itself (the runs were automated); a real console as the app's input; a page served straight from Vite.

## M1d step 2's shape: loopback in the development loop (user decisions, 2026-11-21)

Settled before (plan section 4): the launch profiles `Desktop` (the default, first) and `Loopback`; one address and token across restarts; an orchestrator may set them through the environment; a desktop that cannot start stops with an error suggesting `dotnet run -lp Loopback` and never falls back. Decided now, each the recommended option:
- **Selection:** `SPAR_HOST=Loopback` in the environment, which the `Loopback` profile sets; the app's command-line arguments stay its own. Chosen over a command-line switch Spar would read and remove.
- **The browser:** the first run of a dev session opens the default browser at the page; later restarts do not, since the open tab reconnects. The address with its token is also printed and logged. Chosen over never opening it, or opening it at every start.
- **A backend restart:** the open page waits for the backend at the same address and token, then reloads by itself. Chosen over a banner with a Reload button.
- **The port:** the first run takes a free port and records it, with the token, in the project's `obj/` folder; later runs reuse both. `SPAR_LOOPBACK_PORT` and `SPAR_LOOPBACK_TOKEN` override them, for an orchestrator. Chosen over a fixed port derived from the app's id.

## Loopback in the development loop (implementation record; choices for review; 2026-11-22)

Plan M1d, build step 2 (decision "M1d step 2's shape: loopback in the development loop"). Evidence: `docs/evidence/m1d-loopback.md`, its runners `docs/evidence/m1d-loopback/loopback-run.mjs` (both OSes), `watch-run.mjs` and `wsl.sh`, raw logs in `docs/evidence/m1d-loopback/logs/`. Design: `docs/design/dev-loop.md`, section 4, "Launch profiles".
- **Built:**
  - **`src/Spar.Loopback`:** `SparLoopbackHost` (public, hidden from IntelliSense; `Chosen()` reads `SPAR_HOST`), `LoopbackApp` (one run: folders and log as the desktop's Development run, the page from the development server or the packed page, the address, the services, the host, the printed and logged address, the browser, the signals, the teardown), `LoopbackSession` (the record, the dev-session rule, the orchestrator's variables, the owner-only file) and `BrowserOpener` (`SPAR_LOOPBACK_BROWSER`). `LoopbackHost` gained a fixed port and token, the development server's proxy (`DevServerProxy`, step 1's code, one per host name), the run id with `POST /_spar/session` and the channel's `X-Spar-Run` header, and a report sink (`CspViolationLog` for an app).
  - **The generator:** `HostEmitter` asks `SparLoopbackHost.Chosen()` first when the compilation has the type, before the desktop hosts.
  - **Spar.Sdk:** `SparUsesLoopback` (Development apps); the target `SparLoopbackReference` adds the `Spar.Loopback` project reference (source mode) or its package's assembly by path (package mode, with a `PackageDownload` in every variant) and `FrameworkReference Microsoft.AspNetCore.App`, at build time only; the runtime configuration value `Spar.Loopback.SessionFile` (`obj/spar-loopback.json`).
  - **The SDK:** `src/Spar.Client/src/restart.ts` (`awaitRestart`), called by the loopback channel when a dev session's channel ends.
  - **Linux:** the no-display start-up problem of a Development build names `dotnet run -lp Loopback`.
  - **The Notebook's `Properties/launchSettings.json`:** `Desktop` first, then `Loopback` (`SPAR_HOST=Loopback`).
  - **Tests:** `tests/Spar.Tests/LoopbackSessionTests.cs` (the choice by `SPAR_HOST`, the session rule, the variables, the file's protection on both OSes, the opener's command, the host's fixed address, probe, header and refusals, a port in use); `src/Spar.Client/test/restart.test.ts` (the waiting, the reload once, the stops, the loop guard, an in-flight call ending lost with nothing resent, no waiting without a run or before `open`); a generator test; the boundary test's markers for this step, with two controls in `Spar.Loopback.dll`; three static fields reviewed.
- **Choices made while building, for review:**
  1. **The reference is added at build time, never seen by restore.** NuGet's restore walks project references by path, without their `AdditionalProperties`, and every variant shares the restore output in `obj/`; a reference conditioned on the variant would make a Release build (or a black-box publish inside a Debug solution build) restore again and rewrite it, and leave the other variant's build on the wrong restore. So `SparLoopbackReference` adds the project reference (or the package's assembly) and the ASP.NET Core framework reference in a target before `AssignProjectConfiguration` and `ProcessFrameworkReferences`, outside restore. Checked: `obj/project.assets.json` kept its time across a Release and a Debug build; the Release output has no `Spar.Loopback.dll`, no ASP.NET Core framework in its runtime configuration and no loopback entry in its dependencies. In package mode the package is a `PackageDownload` in every variant (no reference, no dependency), referenced by path in Development.
  2. **Development only, not the app's Test variant:** a test project gets the loopback host through `Spar.Testing.Browser`, as before; the app's Test build has no loopback reference and no `SPAR_HOST` branch.
  3. **`SPAR_HOST`:** `Loopback` or `Desktop` (or unset), case-insensitive; any other value stops the app with a message naming both and exit 70, the app's own start failure. A Production build has no loopback code and never reads the variable.
  4. **A dev session,** which the decision left to define: the record's last-write time is when a run last held it (written at start, touched each minute and at exit, so a killed run is counted to within a minute); a run continues the session when the record is readable and was held less than **4 hours** ago; otherwise it is a new session, which opens the browser. `dotnet watch` and repeated `dotnet run`s have gaps of seconds; a night away starts a new one.
  5. **A new session keeps the port and takes a new token.** "Later runs reuse both" holds within a session; across sessions the token changes, so a page left open from yesterday is refused (401) and says "open the address it printed" instead of silently joining today's run. The port stays, so the page's origin, and its storage, stays.
  6. **The orchestrator's variables:** either one set means an orchestrator runs the app: each given value is used, a missing token is new for every run (an open page then cannot reconnect, so an orchestrator should set both), a missing port is any free one, no record is read or written, and no browser opens. A port outside 1–65535 or a token that is not base64url of 16 to 48 bytes stops the start (exit 70).
  7. **The token in a file (the decision's choice), and what it means:** whoever reads it can call every command of the developer's Development build as the developer, from any process that can reach 127.0.0.1. It is written owner-only from its creation (Unix mode 0600; on Windows a protected access list granting only the current user, nothing inherited from the folder, checked with `icacls`), through a temporary file of the same protection and a rename. This protects it from other users of the machine only: the developer's own programs can read it, as they can read the project's sources and build output (plan section 3c, "What the controls defend"). `docs/design/security.md`'s "writes the token nowhere else" gains this exception, Development only.
  8. **How the page knows a restart:** a dev session's channel names the backend's run (`X-Spar-Run`, a random id per process); the test host sends none, so its pages never wait. When such a channel ends, and only for a document that received `open`, the page looks at `POST /_spar/session` with its token (the bridge's rules: `Host`, token, `Origin`) after 250 ms, 500 ms, then every second, and every 5 s after a minute; it reloads once when a different run answers, and stops waiting, with a console line, when the same run answers (the channel ended for another reason), the token is refused (a new session) or the path is unknown (not this app). A run that is stopping answers 503, so a page never takes it for the live one. Calls in flight have already ended `transport` / `lost` (execution unknown) when the channel closed; nothing is resent.
  9. **The loop guard:** a document that loses its backend within 5 s of its `open`, after an automatic reload, counts; after three such reloads in a row the page stops reloading and says the backend keeps stopping (session storage holds the count). A document that stayed connected 5 s resets it. With the run check, a backend that never comes back, or comes back refusing the token, never causes a reload at all.
  10. **A port in use:** the run waits up to 3 s (the previous run releasing it); then, if this app answers there with the token, it stops ("already runs on loopback", exit 70); if another program holds it, the run takes a free port and starts a new session (the browser opens). With `SPAR_LOOPBACK_PORT`, a held port stops the start.
  11. **The opener:** unset is the system's handler (`UseShellExecute`: `ShellExecuteEx` on Windows, `xdg-open` on Linux); `none` opens nothing; anything else is a command line (double quotes group words), with `{url}` replaced, or the address appended. The tests and the evidence runs use a recorder; no run opened the developer's browser, so the default handler is not verified.
  12. **The address on standard output** (`Spar: the page is at http://127.0.0.1:<port>/#token=… (<how>). Ctrl+C stops the app.`) and in `app.log` (Information), as decided; the log is in the per-user Development folder.
  13. **The development server on loopback:** the same `DevServerProxy`, one per host name the page uses (127.0.0.1, `localhost`); its `/_spar/dev-server/` routes come before the token check (the error page has no SDK), and its Retry, a POST, must carry this origin's `Origin` (403 otherwise), since a page on another site could post it with the Host check passing. The server's `Stopped` event changes nothing on loopback (there is no window to navigate); Vite's own client notices.
  14. **No single-instance lock on loopback:** the port is the lock between loopback runs. A loopback run and a desktop run of the same Development build may run together and share its data folder.
  15. **Windows has no no-display check,** so only Linux's message gained the hint; on Windows the desktop's existing start-up failures are unchanged.
  16. **The restart code ships in the SDK's loopback transport,** which every bundle carries (`hostChannel` picks the transport at run time); it is inert unless a channel names a run, which only a Development loopback host does. The Production Notebook grew by 512 B (5,243,904 to 5,244,416 B), the page's share.
  17. **Signals:** Ctrl+C, `SIGTERM` and `SIGQUIT` end a loopback run cleanly (the channels close, the record is touched, the services are disposed, exit 0); checked with `SIGTERM` on Linux only.
  18. **No change to CI:** the new tests run in the existing steps.
- **Numbers:** a backend restart to the page reloaded by itself: 771 ms after the new run listened on Windows, 891 and 906 ms on Linux (SIGKILL, SIGTERM), mostly the look interval; the host listening 54–85 ms after the run's start; a reused Vite checked in 21–26 ms; 0 CSP violations in the page and in `app.log` on both OSes.
- **Runs:** Windows: `dotnet build Spar.slnx -c Debug` with 0 warnings; the CoreCLR tests without window and black-box 1,098 (1,075 passed, 23 skipped); `Spar.WindowTests` 192 (112 passed, 80 skipped, as before); `Notebook.EndToEndTests` 27 (23 passed, 4 skipped); the Notebook's page tests 12 of 12; the NativeAOT lane of `Spar.Tests` 762 (7 skipped); `Spar.BuildTests` 6 of 6 (the production boundary with the new markers and controls); `Spar.PackageTests` 12 of 12 (a first run failed 3, MSB3106: `NuGetPackageRoot` has no trailing slash under the tests' `NUGET_PACKAGES`; fixed with `EnsureTrailingSlash`); the TypeScript SDK's 283 tests and `npm run check`; `eng/check-size.cs` clean; the loopback, desktop and `dotnet watch` runs of the evidence. Linux (WSL, `docs/evidence/m1c-blackbox/wsl.sh sync client appmodel build test window windowaot notebook`, then `sync boundary`): 1,098 (41 skipped), `Spar.WindowTests` 192 (28 skipped) and its NativeAOT lane 192 (15 skipped, 13 not run), the Notebook's black box 6 of 6, 0 processes left, the production-boundary test 1 of 1; `docs/evidence/m1d-loopback/wsl.sh loopback loopbackterm nodisplay desktop` all passing. After the full suites, two small changes (the package-mode path above; the loopback run also reports an `AggregateException` from building the services as a start failure): after them the solution build, `Spar.PackageTests` and the Windows loopback run again (the reload 796 ms after the new run listened), all passing.
- **Not verified:** the system's default-browser handler (no run opened a browser of the user's); a graceful Ctrl+C in a Windows console; `dotnet watch` restarts (it never restarted in three tries; step 3); the development server's error page and Retry on loopback; running a package-mode app on loopback (the package tests build it, none runs it); the loop guard and an in-flight call during a restart in a real browser (Vitest only).
- **Left behind by a first run:** a Windows loopback run with a relative `LOCALAPPDATA` (pitfalls) wrote a log and one Notebook page into the user's own `%LOCALAPPDATA%\com.example.Notebook.Development`; the agent could not remove it.

## M1d step 3's shape: dotnet watch and Hot Reload (user decisions, 2026-11-22)

Settled before ("Hot Reload signal", plan section 4): after each C# edit Hot Reload applies, the host's `MetadataUpdateHandler` sends `backend-updated` (a revision, the time, the updated types' names) to the open pages; the SDK offers `onBackendUpdate(handler)`, a no-op the bundler removes in Release; a dismissable toast by default; contract and DTO edits restart the app instead of patching. Decided now, each the recommended option:
- **Turning the toast off is the page's setting:** `configureSpar({ backendUpdateToast: false })`, beside `onBackendUpdate`; the toast is page UI. Chosen over a Development option in `Program.cs`.
- **F5 in Visual Studio and VS Code is verified by the user, guided, later:** the build verifies what it can without an IDE (the launch profiles, the debugger attaching from the command line), and the F5 runs in both IDEs go to the plan's table "Checks that need the user at the PC". Chosen over driving the IDEs on the user's desktop now.

## dotnet watch and Hot Reload (implementation record; choices for review; 2026-11-22)

Plan M1d, build step 3 (decision "M1d step 3's shape: dotnet watch and Hot Reload"). Evidence: `docs/evidence/m1d-hot-reload.md`, its runners `docs/evidence/m1d-hot-reload/watch-run.mjs` (loopback, both OSes), `WatchDesktop.cs` (Windows, `dotnet watch` on a desktop of its own) and `wsl.sh` (Linux), raw logs in `docs/evidence/m1d-hot-reload/logs/`. Design: `docs/design/dev-loop.md`, section 4, "Hot Reload signal" and "`dotnet watch`".
- **Built:**
  - **The handler:** `src/Spar/HotReload/BackendUpdates.cs` (a new folder: `Hosting/` has 39 files), named by `[assembly: MetadataUpdateHandler]`. `UpdateApplication(Type[]?)` acts only when `Spar.Development.IsSupported` and `MetadataUpdater.IsSupported`; it numbers the update, names the types, writes `{"revision":N,"time":"…","types":[…]}` and calls the listeners. Three static fields reviewed (`Gate`, `_listeners`, `_revision`).
  - **The hosts:** `WebViewBridge` and `ScriptBridge` listen when supported, post each update to their UI thread and send `backend-updated` to the current document (`BridgeSession.SendEvent`, which M1a built and nothing used until now); `LoopbackHost.SendEvent` sends it to every open document, from `LoopbackApp`. Each logs `Hot Reload applied revision N (types); sent to …`.
  - **The SDK:** `onBackendUpdate(handler)` (returns its unsubscribe) and the `BackendUpdate` type in `@spar/app/sdk`; `configureSpar` takes `backendUpdateToast` (its options are now the exported `SparConfiguration`); `src/backendUpdate.ts` (handlers, the payload read field by field), `src/overlay.ts` (the toast); `fakeSpar().backendUpdate({ types })` for page tests, `FakeBackendUpdate` re-exported by the generated `@spar/app/testing`.
  - **The restart rule:** `Emit/ShapeEmitter.cs`; the generator adds `Spar.ContractShape.g.cs` in the Development variant.
  - **The Notebook:** its `useNotebook` fetches the list again on each backend update; a page test checks it.
  - **Records and checks:** `docs/app-model.md` 6.14 with a compiled example (the stand-ins in `tests/AppModel/sdk/` gained `onBackendUpdate`, `BackendUpdate`, `SparConfiguration` and `FakeSpar.backendUpdate`, and the stand-in `configureSpar` now resets an omitted `onFailure` as the real one does; `AppModel.Tests` expects 37 blocks), the boundary test's markers, the tests listed in the evidence, the pitfalls, the plan's step note and its two F5 rows, and the skills (`spar-typescript-conventions`, `spar-contract-generation`, `spar-testing`, both hosting skills).
- **Choices made while building, for review:**
  1. **The restart mechanism: an enum member named from the generated contract code.** `dotnet watch` and the IDEs restart only for a rude edit, and no documented API lets an app ask for a restart (an app that exits makes `dotnet watch` wait for the next file change). Renaming an existing enum's member is a rude edit (`ENC0020`) in every mode. Considered: a type named from the hash (deleting a type is also rude, but less direct); the `HotReloadAutoRestart` property or `DOTNET_WATCH_RESTART_ON_RUDE_EDIT` (they change what happens on a rude edit, not whether a contract edit is one); and relying on today's behaviour, where the catalog's changed initializer is only `ENC0118`, a warning on which `--non-interactive` restarts but an interactive session patched (measured, the evidence's "Why the shape is needed").
  2. **The hash covers the whole generated `Spar.Contracts.g.cs`,** not only the wire fingerprint: also the command limits, the interface contracts, the app id and the page security, which the catalog's static initializer holds and Hot Reload never runs again. So a CSP edit in `Program.cs` restarts too (it also changes `csp.json`, which the Vite plugin reads). The generated code has no comments or locations from the app, so a body or doc-comment edit keeps it; `ContractShapeTests` classifies 4 edits that keep it and 14 that change it.
  3. **Development variant only:** Production is never hot reloaded, and the Test variant runs inside tests; generator tests without a variant see no shape file.
  4. **The payload:** the revision counts per process from 1 (an update patches the whole process; a restart starts again from 1); `time` an ISO 8601 string (as the wire's `DateTimeOffset`); `types` full names, a compiler-generated type (a state machine, a closure) folded into the type around the outermost generated one, and a top-level compiler type (`<PrivateImplementationDetails>`) left out, which an interactive control run showed in the raw list.
  5. **Who receives it:** on the desktop hosts the current document only, and none between documents; on loopback every open document. An update is never queued for a page that connects later: that page runs the patched code anyway.
  6. **The page side is reached behind `import.meta.env?.DEV === true`,** at each use. Vite 8.3.8's build folds it and drops the branch with or without `?.` (checked on a scratch build), so every bundle Spar embeds carries none of it, including a Debug build with `SparDevServer=false` and the Test variant: there `onBackendUpdate` does nothing and no toast shows, though the host still sends the event. The `?.` is needed: WireShapes' test page loads the SDK without Vite, and a first version with `import.meta.env.DEV` threw there (15 browser tests failed until it changed). The SDK's `tsconfig.json` and `tsconfig.test.json` now list `vite/client` for the type.
  7. **`configureSpar` sets every setting on each call,** so `backendUpdateToast` is `true` again when a later call leaves it out, as `onFailure` returns to its default; `fakeSpar()` resets it with the hooks.
  8. **The toast:** bottom right, in a host element (`data-spar-dev-overlay`) with an open shadow root that adopts a constructed stylesheet (light and dark by `prefers-color-scheme`), `role="status"`, a Dismiss button, 8 s, at most three (the oldest goes). Text: "Backend updated: MarkdownPageStore (revision 1)" with short type names, or "Backend updated (revision N)" when none.
  9. **`fakeSpar().backendUpdate`** is new public page-test API, beyond the decision's list, so a page's own `onBackendUpdate` handler can be tested; `docs/app-model.md` 6.14 mentions it.
  10. **Not set by Spar.Sdk: `HotReloadAutoRestart`.** Visual Studio documents it as restarting the process on a rude edit instead of ending the debug session; with the shape, every contract edit is one. It is a per-app development preference, so it is left to the app or the template (step 6); proposed for the template.
  11. **Not changed: `dotnet watch` killing a `WinExe` app on Windows.** It asks with a Ctrl+C to `dotnet run`'s console, which a GUI-subsystem app has none of, waits 5 s, then ends the tree, Vite included (9–15 s from the save to the new run against 3.1–4.8 s on Linux, where `SIGTERM` closes the app at once). A fix would be Development-only: attach to the parent's console when there is one (`AttachConsole(ATTACH_PARENT_PROCESS)`) and answer its Ctrl+C as an app close, which would also give a terminal's Ctrl+C to `dotnet run`. Left for a decision: it changes how a Development build treats its console, and its check needs a real terminal.
  12. **The loopback loop guard is unchanged:** three reloads into documents that each lost their backend within 5 s stop the reloads, and Linux's 3.5 s contract restarts reached that with edits a second apart. A developer saving three contract edits that quickly sees the page's "keeps stopping" message and reloads by hand; the evidence runs wait 5 s.
  13. **F5 without an IDE:** no command-line managed debugger is installed, and `vsdbg` is licensed for Visual Studio and VS Code only, so none was attached. Checked instead: the Debug apphost started directly as an IDE does (both profiles, two working directories, `DOTNET_MODIFIABLE_ASSEMBLIES=debug`), and a 30 s suspension of the window's process (as a debugger's break) with no hung-page dialog or warning. The F5 runs are the two new rows of the plan's table.
  14. **No change to CI:** the new tests run in the existing steps (`Spar.Tests`, `Spar.Generator.Tests`, the SDK's `npm test`, the page tests, the build tests' boundary); no CI step runs `dotnet watch`.
- **Numbers** (the Notebook; the evidence has every run): a method body to the host's `backend-updated` 65–174 ms after the save once `dotnet watch` is warm, 0.7–2.7 s for a session's first edit (the 4 s budget met), the toast or the page's refetch about 0.1 s after that; 8 contract restarts per OS on loopback, each `ENC0020`, the client regenerated, the page reloading by itself: 9.1–14.6 s from the save to the new run on Windows (5 s of `dotnet watch`'s wait, then its kill) and 3.1–4.8 s on Linux; in the window a DTO edit to the restarted `ui-ready` 10.8–10.9 s (Windows) and 6.8–8.3 s (Linux); 0 CSP violations; the Production Notebook 5,244,928 B (+512 B against step 2's record).
- **Runs:** Windows: `dotnet build Spar.slnx -c Debug` with 0 warnings; the CoreCLR tests without window and black-box 1,126 (1,103 passed, 23 skipped); `Spar.WindowTests` 192 (112 passed, 80 skipped, as before); `Notebook.EndToEndTests` 27 (23 passed, 4 skipped); the NativeAOT lane of `Spar.Tests` 768 (7 skipped); `Spar.BuildTests` 6 of 6; `Spar.PackageTests` 12 of 12; the TypeScript SDK's 299 tests and `npm run check`; the Notebook's page tests 13; `AppModel.Tests` 11; `eng/check-size.cs` clean; the evidence's `dotnet watch` runs (loopback 0 failures, the window, the control without the shape, F5's apphost runs). Linux (WSL, `docs/evidence/m1c-blackbox/wsl.sh sync client appmodel build test window windowaot notebook`, then `sync boundary`, then `docs/evidence/m1d-hot-reload/wsl.sh watch desktop interactive interactivenoshape`): 1,126 (41 skipped), `Spar.WindowTests` 192 (28 skipped) and its NativeAOT lane 192 (15 skipped, 13 not run), the Notebook's black box 6 of 6, 0 processes left, the production-boundary test 1 of 1, the `dotnet watch` runs all passing.
- **Not verified:** F5 in Visual Studio and VS Code (the plan's table); a managed debugger attached from the command line; interactive `dotnet watch` on Windows; a contract library's edit under `dotnet watch`; a real terminal's Ctrl+C.
- **Left behind:** nothing found: no process of the runs, no WebView2 process under their roots, no new entry in the user's `%LOCALAPPDATA%`. Two Linux runs of an earlier `wsl.sh` killed their own script with its session and left `dotnet watch` running in the WSL copy; they were ended, and the script now signals only the session it started.

## Step 3's two follow-ups (user decisions, 2026-11-22)

From the implementation record "dotnet watch and Hot Reload" (choices 11 and 12), each the recommended option:
- **Windows restarts under `dotnet watch`:** a `WinExe` app has no console, so `dotnet watch`'s Ctrl+C never reaches it; it waits 5 s and kills the whole tree, Vite included. Decided: in a Development build only, the app attaches to its parent's console and treats its Ctrl+C as a normal close, so a restart is immediate and Vite is reused, as on Linux. Chosen over recording it as a limitation.
- **The loopback page's reload guard:** fast Linux restarts tripped it after three contract edits about a second apart. Decided: a reload counts against the guard only when the page then gets no backend at all (no `open`), not when a newer run answers; a backend that keeps failing still stops the loop. Chosen over leaving the guard as it is.

## Step 3's follow-ups: the parent's console and the reload guard (implementation record; choices for review; 2026-11-22)

The user decisions "Step 3's two follow-ups", built on `e03a935`. Evidence: `docs/evidence/m1d-hot-reload.md`, "Follow-up: the parent's console and the reload guard"; its runners gained `WatchDesktop.cs`'s `ctrlc` step and `--free-console`, `watch-run.mjs`'s `--settle` and `wsl.sh`'s `EDITS`, `SETTLE` and `LOGNAME_WATCH`.
- **Built:**
  - **`src/Spar/HotReload/ConsoleInterrupt.cs`:** `TryAttach(log)` attaches to the parent's console (`AttachConsole(ATTACH_PARENT_PROCESS)`) in a Development build on Windows, and clears an inherited "ignore Ctrl+C" (`SetConsoleCtrlHandler(NULL, FALSE)`); `Listen(log, interrupted)` also registers `SIGINT` and `SIGQUIT` (Ctrl+C, Ctrl+Break) and cancels the default ending when `interrupted` handled them. Both do nothing outside Development, off Windows, or when the attach fails (the process has a console; the parent has none).
  - **The window:** `WindowsApp` calls `ConsoleInterrupt.Listen` before `UiThread.Run`, with `AppWindow.CloseFromConsole`, which posts `WM_CLOSE` (the close button's path, guards included), or lets the default ending happen when no window exists yet.
  - **Loopback:** `LoopbackApp` calls `ConsoleInterrupt.TryAttach` at the start of its run; its own `SIGINT`, `SIGTERM` and `SIGQUIT` stop then receives the console's Ctrl+C.
  - **The reload guard (`src/Spar.Client/src/restart.ts`, `loopback.ts`):** `backendOpened`, called when a dev session's document gets `open`, clears the count and the run the page reloaded for; `awaitRestart` takes whether the document got `open`. A document that got it always waits; one that did not waits only when an automatic reload made it (`spar.restart.run` in session storage), and that reload counts; the third in a row stops the reloads ("stopped before the page could connect, 3 times in a row"). The 5 s rule is gone. `RestartOutcome` gained `none` (nothing to wait for).
  - **Tests:** `restart.test.ts` (quick restarts a newer run answers never count, six in a row all reload; a backend that keeps failing before `open` stops at three; `open` starts the count again; a reloaded document whose channel never started waits past the run it reloaded for; nothing to wait for without a run or a reload; and the same through the loopback channel); `BackendUpdatesTests.A_process_with_its_own_console_is_left_as_it_is`; the boundary test's markers `ConsoleInterrupt`, "Attached to the parent's console" and "from the parent's console", with controls in `Spar.dll`.
- **Choices made while building, for review:**
  1. **The attach is per host, not in the core's start:** the window host attaches and registers in one call just before its UI thread; the loopback run attaches first thing in its run, before the web host starts. An attach after any signal handler exists in the process left the console's Ctrl+C unseen (the loopback run's first try: exit `0xC000013A` in all 8 restarts), so an app whose own `Program.cs` registers a handler before `app.Run()` may meet the same; an attach in `SparApplication.Create` would cover more, but runs for test hosts and every variant's start too.
  2. **Ctrl+C and Ctrl+Break only:** closing the terminal (`CTRL_CLOSE_EVENT`) and the system's shutdown keep their default handling; the session end is `WM_ENDSESSION` (M1b).
  3. **No window yet:** a Ctrl+C before the window exists is not handled, so it ends the process as before; the log says so.
  4. **`Exe` apps are left alone:** an app built as `Exe` has its own console, never attaches and keeps .NET's default Ctrl+C (the process ends at once); only `WinExe` apps were the problem.
  5. **The guard's state is in session storage** beside the count: the run the page reloaded for, so a reloaded document whose channel never even started still knows which run to wait past.
  6. **No change to CI.**
  7. **The class is `ConsoleInterrupt`, not `ParentConsole`:** the boundary marker `ParentConsole` matched the Production method `StartupFailure.WriteToParentConsole` (a substring search), so the class was renamed after the `dotnet watch` runs; the terminal-like Ctrl+C checks ran again after the rename (pitfalls).
  8. **`StartupFailure.WriteToParentConsole` (Production code) and the attach:** it attaches to the parent's console to write a start-up failure when the app has no standard error, then frees it. The window host's start-up failures are reported before its attach, so they are unchanged; a report after it would find its own attach refused and write its line only to the log and the dialog, and never free Spar's attach.
- **Numbers** (the Notebook): Windows window, a DTO edit to the restarted `ui-ready` 5.4–6.0 s (10.8–10.9 s before), `dotnet watch`'s Ctrl+C to exit 0 in 71–77 ms (killed after 5 s before); Windows loopback, 8 contract restarts to the new run 4.3–5.4 s (9.1–14.6 s before) and the page reloaded 5.0–6.5 s (10.2–15.6 s), one Vite for the whole session, each reuse checked in 23–34 ms, no lost-server violation; a terminal-like Ctrl+C closed the window in 39 ms and stopped the loopback run in 7 ms; Linux loopback with the next edit 0.3 s after each reload, 8 restarts all reloaded (3.9–8.2 s), where step 3 stopped at the third.
- **Runs:** Windows: `dotnet build Spar.slnx -c Debug` with 0 warnings; the CoreCLR tests without window and black-box 1,127 (1,104 passed, 23 skipped); `Spar.WindowTests` 192 (112 passed, 80 skipped); `Notebook.EndToEndTests` 27 (23 passed, 4 skipped); the NativeAOT lane of `Spar.Tests` 769 (7 skipped); `Spar.BuildTests` 6 of 6 after the rename (the boundary had failed on `ParentConsole` before it), and `Spar.Tests` 774 again after it; the TypeScript SDK's 304 tests and `npm run check`; `eng/check-size.cs` clean; the evidence's runs. Linux (WSL, `docs/evidence/m1c-blackbox/wsl.sh sync client build test`, then `sync boundary`; `docs/evidence/m1d-hot-reload/wsl.sh watch` with `EDITS=dto,error,command,limit SETTLE=300`): 1,127 (41 skipped), the production-boundary test 1 of 1, the quick-edits run 0 failures.
- **Not verified:** a real terminal's Ctrl+C and closing a terminal; a Development app started straight from a terminal without `dotnet run` (no inherited standard handles, so after the attach its output may reach the terminal); an app that registers its own signal handler before `app.Run()`; a backend that keeps failing before the page connects, in a real browser (unit tests only); the Linux window tests and NativeAOT window lane after the follow-up (the changed code is Windows-only and the SDK's loopback transport, which those tests do not run as a dev session); `dotnet watch` on Windows after the class's rename (a mechanical rename; the Ctrl+C checks ran after it).

## M1d step 4's shape: tests in the template (user decisions, 2026-11-22)

Asked before building step 4 (plan, "M1d", step 4; section 5's "Template defaults"), each the recommended option:
- **The `Window` and `Browser` traits:** test authors write them by hand, `[Trait("Surface", "Window")]` and `[Trait("Surface", "Browser")]`, as Spar's own tests do, and Spar checks them: a test that starts the Window or Browser surface without the matching trait fails with a message naming the trait to add. Chosen over Spar attributes that carry the trait (one more API) and over the hand-written trait alone (a forgotten one runs a window test in the fast invocation).
- **Page tests:** Vitest with `@spar/app/testing` runs as `npm test` in the app's `frontend/`; `dotnet test` runs only the C# tests. The template's CI workflow and its guide run both. Chosen over running Vitest as one test of the fast test project, which would need Node in every C# test run.
- **Where the test projects are written:** as the template's content now, in the place step 6's `dotnet new spar-app` template will use; `Spar.PackageTests` copies that content into its consumer, so the files are written once and proven on both OSes in this step. Chosen over keeping them as strings in the package tests until step 6.

## Tests in the template (implementation record; choices for review; 2026-11-22)

Plan M1d, build step 4 (decision "M1d step 4's shape: tests in the template"; plan section 5, "Template defaults"). Evidence: `docs/evidence/m1d-template-tests.md`, with the package tests' timings on both OSes and the probe `docs/evidence/m1d-template-tests/trait-probe/`. Design: `docs/design/testing.md`, "Projects and lanes" and "Template defaults".
- **Built:**
  - **The template's content,** `src/Spar.Templates/content/spar-app/`: `MyApp/` (`MyApp.csproj`, a `WinExe` with `SparAppId` `com.example.MyApp`, `TreatWarningsAsErrors` and `MSBuildTreatWarningsAsErrors`; `Program.cs` with the window's size and background; `Greetings/GreetingService.cs`, the contract `greeting` with `GreetAsync(string)` returning `Result<Greeting, GreetError>` and the case `NameMissing`; `frontend/`, React 19.3.1 + TypeScript 7.0.2 + Vite 8.3.8 with `@vitejs/plugin-react`, the fixed `"@spar/app": "file:../obj/spar/client"`, the Spar Vite plugin, `hostChannel()`, `ready()` after the first render, a committed `package-lock.json`, and `src/greet.ts`, the page's call and what it shows); `MyApp.Tests/` (two command tests through `GreetingClient`); `MyApp.EndToEndTests/` (`Browser/`: `SharedBrowser`, a class fixture, and one Playwright test; `Window/`: one window test; `BlackBox/`: one launch of the Production executable that invokes Greet with no name and reads the declared error's text; the `Browser` folder and Playwright left out of the NativeAOT lane); `TestSettings.cs` with `[assembly: CaptureConsole]` in both test projects; `frontend/src/greet.test.ts` (three page tests with `fakeSpar()`: a value, the declared error, a lost connection) and `vitest.config.ts`, run by `npm test`.
  - **Spar.Sdk:** `--results-directory` for every test project, `$(SparTestResultsDirectory)`: `artifacts/test-results/<project>` in source mode (with `--report-xunit-trx`, as before), `<project>/obj/test-results` from packages; no trailing separator.
  - **The trait check:** `SparTestCleanup.Begin(testId, currentTest, surfaces)` (the hooks pass the values of the test's `Surface` trait, read from `IXunitTest.Traits` and `ICodeGenTest.Traits`, which xUnit merges from the method, the class and the assembly), `SparTestCleanup.CurrentSurfaces()`, and `SparTestApp.StartAsync`'s `RequireSurfaceTrait`, which throws before anything starts. `tests/static-state/reviewed.txt`'s line for `Watched` says what it now holds.
  - **Spar's tests:** `[Trait("Surface", "Browser")]` on `WireShapes.EndToEndTests.BrowserBridgeTests` and `BudgetTests` and on `Notebook.EndToEndTests.JourneyTests`; `WireShapes.Tests/SurfaceTests.cs` with four new tests (no trait, another value, no surface, the class's trait) and the trait on its existing browser test.
  - **`Spar.PackageTests`:** the main consumer is the template's content with `MyApp` renamed `Notes` (`ConsumerFiles.WriteFromTemplate`); `PackageTests` keeps the build-machinery tests on it, and the new `TemplateTests` runs the fast tests' results check, their NativeAOT lane, `npm test`, the browser, window and black-box invocations, the trait check on a window test written without its trait, and the Playwright version check; both classes share one collection, and the first build moved to `Consumer.FirstAsync`. The consumer's runs get `SPAR_TEST_ARTIFACTS` pointing at the repository's `artifacts/`.
  - **Records:** the plan's step note, `docs/design/testing.md` (and its first line, where an earlier edit had put row 7's new cells above the title: moved back into row 7), `docs/app-model.md` sections 1 and 6.12 (prose only; no block changed), `spar-testing` and `spar-msbuild`, four pitfalls, and `docs/evidence/m1c-blackbox/wsl.sh`'s new step `packagetests`.
- **Choices made while building, for review:**
  1. **Where the content lives: `src/Spar.Templates/content/spar-app/`,** the layout of a `dotnet new` template package (`content/<template>/`), beside the other packages Spar ships; step 6 adds the package project and `.template.config/template.json` there. There is no project in `src/Spar.Templates/` yet, so nothing builds the content in place; the package tests build it from packages only. `AGENTS.md`'s layout table does not list the folder yet (not edited; proposed with this record).
  2. **`CaptureConsole` comes from the template's own files,** a one-line `TestSettings.cs` per test project, not from Spar.Sdk: whether console output goes into each test's result is the app's choice (`spar-msbuild`: settings users choose live in the app), the attribute is the same in both xUnit flavours, so no linked file per lane is needed, and a project removes it by deleting the line. Spar's own test projects do not get it. The alternative was a linked source per lane with `SparCaptureConsole=false` as the opt-out.
  3. **Results from packages go to `obj/test-results/`** (`$(BaseIntermediateOutputPath)` under the project), with no TRX report: one TRX per run would pile up in `obj/`; a CI workflow adds `--report-xunit-trx` (the template's workflow, step 6). `SparTestResultsDirectory` is a new public property.
  4. **xUnit1051 needs no override:** it is a warning in xunit.analyzers 2.3.0, and the template's `TreatWarningsAsErrors` or `MSBuildTreatWarningsAsErrors` alone makes it an error (the evidence's table); Spar.Sdk raises nothing.
  5. **The check's details:** the trait's name and value are compared ignoring case; the check runs at the start of `StartAsync`, before the app's thread; an app started where no hook watches (a fixture, an assembly fixture, `SparTeardownHook=false`) is not checked, since there is no test to read; `SparTestCleanup.Begin` gained a parameter (a public, editor-hidden API of the hook, always the same version as the SDK's hook file).
  6. **The black box is not checked.** Nine classes carry `Surface=BlackBox`, but the Windows Sandbox lane's tests (`Sandbox=…`, explicit) and `Spar.WindowTests.BudgetTests`' black-box budget (a class with `Surface=Window`) launch `SparBlackBox` without it; checking would move the budget test into the black-box invocations and add a trait to every Sandbox test. A later check could exempt explicit tests.
  7. **The browser traits leave CI unchanged:** CI's fast invocation filters out only `Window` and `BlackBox`, so Spar's browser tests still run there; the template's own workflow (step 6) can run `Surface=Browser` as its own invocation.
  8. **The page tests test `greet.ts`, not the component,** with no DOM library: a component test needs happy-dom or a testing library, which an app adds when it wants one; the component is covered by the browser and window tests. Vitest runs without the Spar Vite plugin (`vitest.config.ts`).
  9. **The browser fixture is a class fixture,** so a window-only or black-box-only run starts no Chromium (pitfalls, "An xUnit assembly fixture starts for every test of the assembly").
  10. **Each project sets its own properties** (`TargetFramework`, `Nullable`, `ImplicitUsings`, both warning settings) instead of a `Directory.Build.props` at the solution root, so each file reads on its own; the Playwright version is pinned in the end-to-end project (1.64.1, checked against the repository's by `TemplateTests`).
  11. **The renaming rule is `MyApp` and `myapp`** (the npm package name `myapp-frontend`); step 6's `template.json` needs the lower-case form as well as the name.
  12. **The template's consumer replaces the string-built main consumer** (its `App.EndToEndTests` and `App.BlackBoxTests` are gone); the central-package-management and library consumers keep the string-built app, so the package tests still cover a project and assembly with different names and a frontend in `ui/`. About 67 s more of runs on Windows (the evidence), 18 tests where there were 12.
  13. **On Linux the template's window and black-box invocations skip on a machine without Xvfb, `dbus-daemon` and WebKitGTK 6.0,** whatever `CI` says: the hosted Ubuntu runner of CI's package-tests lane has none of them. Proposed: run them in the `linux-window` job's image as well. Not done here: CI is not changed by this step.
  14. **`npm test` is started by npm's full path** (pitfalls: `npm.cmd` by its bare name looks for npm in the working directory).
- **Numbers:** `Spar.PackageTests` 18 of 18 on Windows in 3 min 1 s and on Linux in 4 min 35 s (Linux's first build 97 s, with an npm cache that lacked the template's packages); in the template's consumer, the fast tests' first build 14.2 s (Windows), their NativeAOT publish 29.6 s (Windows) and 15.9 s (Linux) with 2 of 2 passing, `npm test` 3 of 3 in 1.5 s and 0.4 s, the window, browser and black-box invocations 1 of 1 each; the trait failure in 2.4 s (Windows). The probe: the assembly's trait and `CaptureConsole` worked on CoreCLR and NativeAOT (3 of 3 each).
- **Runs:** Windows: `dotnet build Spar.slnx -c Debug` with 0 warnings; the CoreCLR tests without window and black-box 1,131 (1,108 passed, 23 skipped); `Spar.WindowTests` 192 (112 passed, 80 skipped); `Notebook.EndToEndTests` 27 (23 passed, 4 skipped); the NativeAOT lane of `Spar.Tests` 769 (7 skipped), `WireShapes.Tests` 83, `Notebook.Tests` 12, `Notebook.EndToEndTests` 20 (4 not run); `Spar.BuildTests` 6 of 6; `AppModel.Tests` 11; the TypeScript SDK's `npm run check` and 304 tests; `eng/check-size.cs` clean. Linux (WSL, `docs/evidence/m1c-blackbox/wsl.sh sync client appmodel build test`, then `window`, then `packagetests`): 1,131 (41 skipped), `Spar.WindowTests` 192 (28 skipped), 0 processes left, `Spar.PackageTests` 18 of 18.
- **Not verified:** CI (not run until the first push), and in particular the package tests on the hosted runners; the Linux NativeAOT window lane and the Notebook's Linux black box after the hook's change; a template app made by `dotnet new` (step 6); a test project without `ImplicitUsings` (the hook, as before, relies on them); the results folder as an IDE's test explorer sees it; Linux without at-spi2-core (not part of the skip's check).
- **Left behind:** nothing on Windows (no test-host root, WebView2 profile or process of these runs; no new entry in `%LOCALAPPDATA%`). In the WSL copy, the window test's test-host root and `/dev/shm/spar-test-*` folder, as every Linux window run leaves until the next one.

## M1d step 5's shape: the page side of start-up (user decisions, 2026-11-22)

Asked before building step 5 (plan, "M1d", step 5; section 3d), each the recommended option:
- **The splash:** the app writes its splash (a mark, text) in `index.html`, inside one element with a known id; the Vite plugin adds its styles, the delays and their CSP hash. Chosen over a plugin option that generates the markup, which gives less control over the look.
- **Readiness:** the page reports `ui-ready`, which also removes the splash, only when the app calls `connection.ready()` once its first view has rendered, as the Notebook and the template already do. This replaces section 3d's automatic default with a manual opt-in (and `docs/app-model.md` 6.3's open question, `ready` against `spar.ready()`): there is one way, and it is the app's first real view, not the bundle's first frame.
- **`setColorScheme("light" | "dark" | "system")`:** built in step 5, completing what M1b left: the host already saves the choice and applies it at the next start.

## The page side of start-up (implementation record; choices for review; 2026-11-22)

Plan M1d, build step 5 (decision "M1d step 5's shape: the page side of start-up"; plan section 3d). Evidence: `docs/evidence/m1d-page-startup.md`, with the frame logs in `docs/evidence/m1d-page-startup/`. Design: `docs/design/theme.md`, section 3d ("Built (M1d step 5)").
- **Built:**
  - **The colours from `Program.cs`:** `WindowColorsAnalysis` (Spar.Generator) reads `spar.Window(w => w.Background(light, dark))` as `PageSecurityAnalysis` reads the page security, and the analyzer reports SPAR106 (new, Error, `Spar.PageSecurity`) for a colour that is not a constant `#rgb`/`#rrggbb` or a `Background` call outside a readable `spar.Window` statement; the client step writes `theme.json` (`#rrggbb`, lower case, `#ffffff`/`#1f1f1f` without a call) beside `csp.json`.
  - **The Vite plugin** (`src/vite/pageStyle.ts`): `spar(options)` with `splash: { delay, minimumVisible, fadeIn, fadeOut }`; an `order: "post"` `transformIndexHtml` that adds the meta, the theme style and, for a page with `#spar-splash` outside comments, the splash style and `aria-busy="true"` on `<body>`.
  - **The hashes:** `PageHtml.StyleHashes` for `<style id="spar-theme">` and `<style id="spar-splash-style">`, composed into `style-src` by `PageResponder` (both desktop hosts), the loopback app and the Browser surface (packed page, or the folders' top-level HTML).
  - **The SDK:** `src/splash.ts` (the frame watch from `connectSpar`, the removal), `connection.ready()` once per document with `ready` after the removal, `setColorScheme` (`src/colorScheme.ts`, exported from `@spar/app/sdk` with `ColorScheme` and `ColorSchemeError`), and `fakeSpar()` answering it.
  - **The hosts:** `HostCommands` (the bridge's commands under the contract `spar`, one per window), `SavedColorScheme.Write`, `WindowTheme.Choose` and `AppWindow.ChooseSchemeAsync` on Windows and Linux, the Windows test window's emulated scheme following the choice.
  - **Apps:** the Notebook's splash (a notebook mark and its name, styled in `styles.css`), the template's commented-out example, and the test app `tests/apps/StartupProbe` (in `Spar.slnx` and `Spar.WindowTests`).
  - **Tests:** the SDK's `pageStyle`, `splash` and `colorScheme` tests (26 new; 330 in all), `HostCommandsTests` and a style-hash test in `Spar.Tests`, `WindowColorsTests` (4) in `Spar.Generator.Tests`, `ThemeFileTests` (4) in `Spar.Sdk.Tasks.Tests`, `StartupTests` (6) and the explicit `StartupFrameTests` (22 starts) in `Spar.WindowTests`; `tests/static-state/reviewed.txt` gained seven fields.
  - **Records:** the plan's step note and a row in "Checks that need the user at the PC", `docs/design/theme.md`, `docs/app-model.md` 6.3 (the manual mode and its open question gone; the example compiled against the stand-ins, which gained `connectSpar`, `hostChannel` and `setColorScheme`'s `Call`) and section 7, the skills `spar-typescript-conventions`, `spar-page-security`, `spar-contract-generation`, `spar-webview2-hosting`, `spar-webkitgtk-hosting` and `spar-testing`, ten pitfalls. `AGENTS.md`'s layout table does not list `tests/apps/StartupProbe` yet (not edited; proposed with this record).
- **Choices made while building, for review:**
  1. **The splash's id is `spar-splash`;** every child of it is "the mark" (`#spar-splash > *` fades in), so an app can put a mark and a line of text in it. The app writes `role` and `aria-label`; the plugin adds only `aria-busy` on `<body>` and never changes the app's element. An element inside an HTML comment is not a splash.
  2. **The host hashes the plugin's two styles from the served HTML,** by their exact opening tags, as it already hashes the SRI import map, rather than the plugin writing hashes into a file the host reads. One rule then covers the packed page, loopback and the Browser surface, and nothing but those two elements is ever allowed. As with the import map, an app could write `<style id="spar-theme">` by hand and get its own text allowed; it is its own page.
  3. **The theme style is `:where(html){color-scheme:light dark;background-color:…}`** with the dark colour under `prefers-color-scheme: dark`: zero specificity, so every rule of the app's wins; inserted after `<meta charset>` (which must stay within the first 1024 bytes), else first in `<head>`. The meta is added only when the page has none, so an app that wants `content="light"` keeps it.
  4. **SPAR106 is an error,** like SPAR101–104: a computed colour would make the page's style disagree with the window. `Size` and `MinSize` stay free (the start-up probe computes its width).
  5. **`setColorScheme` is the host's own command through the ordinary call path,** contract `spar` (one of the package's reserved subpaths, so no app contract can have it), command `setColorScheme`, request the JSON string, no value: it gets the bridge's ids, admission, cancellation and telemetry, and `fakeSpar()` can script it like any command. A session whose host gives no `HostCommands` (the console harness, unit tests) knows it as an unknown command.
  6. **It applies at once** (the user's question): window colour, title bar, WebView background and `prefers-color-scheme` together, the same path as a change of the system's theme, so at most the measured mixed frame. Saved first, off the UI thread, then applied; the call completes once the window has applied it; one window's choices are taken one at a time in the order sent. `system` returns to the system's scheme at once (on Linux the portal is read off the loop when none was followed since the start, and followed from then on; GTK's own settings are now read before the host sets any, also when the app chose).
  7. **The declared error is `notSaved`, without a payload:** the window took the choice, the data folder could not be written, the next start shows the earlier choice; the reason goes to the host's log, not to the page. An invalid name is `rejected` / `invalid-request` before it leaves the page.
  8. **On loopback the choice is saved only** (the data root is the window's), and the browser keeps its own scheme; the host logs which. The Browser surface does the same with the test's data root.
  9. **`fakeSpar()` answers `setColorScheme` with success until a test scripts it** (`spar.on(setColorScheme).fail("notSaved")`), unlike an app command nobody scripted, which is refused: it is the SDK's, and most page tests do not mean to test it; `spar.calls(setColorScheme)` lists the choices.
  10. **A Linux test window that calls it changes every open test window's scheme** (GTK's dark preference is the process's): accepted and written down (pitfalls, `spar-webkitgtk-hosting`); `StartupTests` runs in a collection of its own. A Windows test window emulates the window's new scheme on its page, since its InPrivate profile's scheme does not reach the page.
  11. **`ready()` without a splash sends at once, as before;** with one, after the removal and two frames (each frame waited for at most 100 ms, so a hidden page still reports). Later calls in a document do nothing, so React's StrictMode double effect sends one `ready`.
  12. **The minimum visible time counts from frames the SDK saw,** not from the animation's timeline, and the frame request at the bundle's start became a frame watch: `connectSpar` requests frames while the page has a splash, until the first frame at or after the mark's appearance, and the minimum counts from that frame, or from now when none has rendered since the mark was due. The measurements (WebKitGTK under Xvfb: a mark due during a long task appeared at the first frame after it and, counted from the timeline, stayed 111–126 ms; one frame request did not change that; WebView2: the compositor animated the mark through the task) leave no rule right on both engines without seeing the frames. This one never showed the mark for less than 512 ms, and costs up to 400 ms more on screen when the bundle starts after the mark was due, and after a long task on WebView2. A page without a splash requests no frame. Rejected after measuring: the spike's rule (the first frame's callback more than 100 ms late; it missed the case, and Chromium's time stamp hid the block) and a check of `document.timeline.currentTime` at `connectSpar` (a 111–116 ms flash on WebKitGTK). An alternative to weigh: a small hashed inline module from the plugin that watches frames from the page's parse, which would remove the cost after a slow download; not built.
  13. **Reduced motion keeps the delay and drops the fades** with `animation-timing-function: step-start` on the same 120 ms animation (the mark appears at once when the delay ends), not the spike's 1 ms step, which did not cause a frame on GTK4 in one run; not measured on the engines.
  14. **The timing options are whole milliseconds from 0 to 10,000,** checked when the plugin is created; the minimum and the fade-out reach the SDK as custom properties of the cover (`--spar-splash-minimum`, `--spar-splash-fade-out`), not as attributes on the app's element.
  15. **The Notebook gets a real splash** (the user's question): a polished sample shows the feature as an app uses it, and a warm start never shows it; its mark is a notebook outline drawn for it, in the app's accent colour of each scheme.
  16. **The start-up probe is a test app of its own,** with a first page whose start the window's width picks (`STARTUP_PROBE_START`, 40 pixels a step; a first page has no address to carry it) and a later page whose address picks it. The frame tests are explicit (a measurement of minutes), with the assertions every start must keep (0 wrong-colour frames, at most one appearance, none on a fast start).
- **Numbers:** the host's `ui-ready` 16–32 ms after the splash left on WebView2 (15 starts) and 9–17 ms on WebKitGTK (8); the frame tests, 22 starts × 5 runs per engine: 0 wrong-colour frames and at most one appearance in every run, the mark on screen at least 512 ms (WebKitGTK) and 700 ms (WebView2) as built, against 111–126 ms on WebKitGTK with the timeline count; the mark's appearance 431–468 ms after the navigation on WebView2 whatever ran meanwhile, 406–417 ms on WebKitGTK when nothing blocked it and 810–814 ms when 600 ms of work did; a window's first page `first-paint` 64–92 ms after its navigation on WebView2 (no first-frame hold).
- **Runs:** listed in the evidence's "Runs", with counts.
- **Not verified:** see the evidence's "Not verified": on-screen windows (added to the user's table), a real Linux desktop with a GPU, WebView2's first-frame hold of the spike, a real slow download, reduced motion on the engines, `system` following a later system change, the Debug development server's nonce on the two styles in a real Debug window, CI.

## M1d step 6's shape: the template package and the spar tool (user decisions, 2026-11-22)

Asked before building step 6 (plan, "M1d", step 6; section 6a in `docs/design/agents.md`), each the recommended option:
- **Two parts:** 6a is the template package (`dotnet new spar-app` over the content step 4 built, its options, `global.json`, the launch profiles, the local tool manifest) and the from-scratch guide; 6b is the `spar` tool (`spar doctor`, `spar init frontend`) and the minimal build identity of "M1d's decide-first questions". Each is built, verified on both OSes and committed on its own; the guide gets a short tools section when 6b lands. Not taken: one step, a much larger change to review at once.
- **The `spar` tool is one framework-dependent .NET tool package,** at Spar.Sdk's version, through the template's local tool manifest (`dnx spar` also works). Its code is AOT-compatible with warnings as errors, so runtime-specific NativeAOT tool packages can follow without changes. Not taken now: NativeAOT packages per runtime (four builds, each OS's published on that OS).
- **The end-to-end test project is created by default,** with an option that leaves it out (its name settled when 6a is built). The app and the fast test project are always created. Not taken: only on request, or always with no option.
- **`spar doctor` in M1d checks the machine, the project and the build identity:** the .NET SDK against `global.json`, Spar.Sdk resolution, Node and npm (including ones a version manager installed), WebView2 or WebKitGTK and the AOT prerequisites; the frontend folder, the `@spar/app` dependency, the Vite plugin, the lockfile and `node_modules`, the contract snapshot and the launch profiles; and the app's identity. Each finding has a stable id, a severity and its fix, with `--json` and distinct exit codes. M2 adds the security overrides, the effective policy, the third-party notices, and the Docker and Playwright checks.

## The template package (implementation record; choices for review; 2026-11-22)

Plan M1d, build step 6, its first half 6a (decision "M1d step 6's shape: the template package and the spar tool"; plan section 1, "Packages"). Evidence: `docs/evidence/m1d-template-package.md`, with the package tests' timings on both OSes and the guide's runs. The guide: `docs/guide/getting-started.md`.
- **Built:**
  - **The package** `src/Spar.Templates/Spar.Templates.csproj`: `PackageType` Template, packed with Spar's seven other packages at the same version (`0.1.0-dev.N`), nothing compiled into it; `content/spar-app/` as authored, minus `bin/`, `obj/` and `node_modules/`, with `.template.config/` and `.gitignore` (`NoDefaultExcludes`). Target `SparTemplateVersions` packs a copy of `template.json` from `obj/` with `PACKED_DOTNET_SDK_VERSION` (the repository's `global.json` `sdk.version`) and `PACKED_SPAR_SDK_VERSION` (`$(PackageVersion)`) filled in, and fails on an unreadable SDK version, a missing placeholder or a backslash. In `Spar.slnx` under `/src/`.
  - **`template.json`:** identity `Spar.Templates.App.CSharp`, short name `spar-app`, name "Spar desktop app", type solution; the symbols `appName` (the name's `safe_namespace` form, replacing `MyApp` in files and renaming paths) and `appNameLower` (`myapp`); `--platforms` (`both`, `windows`, `linux`; `both` by default) written through a switch into `<SparPlatforms>`; `--app-id` (text) coalesced with `com.example.<name>` into `<SparAppId>`; `--no-end-to-end-tests` (bool, false) excluding `MyApp.EndToEndTests/` and its solution line; two constant symbols filling `global.json`. `dotnetcli.host.json` names the three options with no short aliases.
  - **New content:** `global.json` (`SPAR_DOTNET_SDK_VERSION` with `rollForward` `latestFeature`, `msbuild-sdks` `Spar.Sdk` `SPAR_SDK_VERSION`, the test runner Microsoft.Testing.Platform), `MyApp.slnx` (three projects, the third in a template condition), `MyApp/Properties/launchSettings.json` (`Desktop` first, `Loopback` with `SPAR_HOST`), `.gitignore` (`bin/`, `obj/`, `node_modules/`, `.vs/`, `*.user`), and `<SparPlatforms>windows;linux</SparPlatforms>` in `MyApp.csproj`.
  - **`Spar.PackageTests`:** the fixture packs eight packages, installs the packed template into `template-hive` under the run's folder and makes the main consumer with `dotnet new spar-app -n Notes` (`--no-update-check`), then writes only its `nuget.config`; `UseSdk` changes the template's `global.json` in place. The new `NewAppTests` (5): the files `dotnet new` wrote are the template's content renamed, with no tool manifest and no `nuget.config`; `global.json`'s versions; both platforms, the app id, the profiles in order and the solution's three projects; a new app with `--no-end-to-end-tests` whose `dotnet test` in its folder passes 2 tests; one with `--platforms <this OS> --app-id org.example.notes.Single` whose app builds, and one for the other OS whose project says so. `ConsumerFiles.WriteFromTemplate` is gone. 23 tests.
  - **The guide** `docs/guide/getting-started.md` (Starlight front matter `title` and `description`): prerequisites with install commands, `dotnet new install Spar.Templates`, `dotnet new spar-app`, `dotnet run` and `-lp Loopback`, `dotnet watch`, `dotnet test` per project and trait, `npm test`, Playwright's Chromium, the NativeAOT lane, `dotnet publish`, and where things live.
  - **Records:** the plan's step note with "Left: 6b" and a row in "Checks that need the user at the PC" (F5 of a new app in both IDEs), the evidence's scripts (`guide-win.sh`, `guide-linux.sh`, `linux-stop.sh`, `loopback-check.cjs`), `docs/app-model.md` section 1 (the solution in the layout and a paragraph on `dotnet new`; no code block changed), `docs/design/build.md` (the package in "Packages"), `docs/design/testing.md` ("Template defaults"), `spar-msbuild` and `spar-testing`, `AGENTS.md`'s `src/` row and a `docs/guide/` row, six pitfalls, and `docs/evidence/m1d-hot-reload/WatchDesktop.cs`'s `--command` and `edit:` (for the guide's window runs; its other steps unchanged).
- **Choices made while building, for review:**
  1. **Versions come in through the template engine,** not a generated `global.json`: the pack fills two constants in `template.json`, and the engine writes them over the placeholders of the authored `global.json`. A `global.json` written by `WriteLinesToFile` ended with CR LF on Windows after LF lines (pitfalls), and `template.json` never reaches the app. The authored `global.json` holds the placeholders, so it is not a valid file in the repository; nothing runs `dotnet` there.
  2. **`rollForward` `latestFeature`** on the repository's SDK version (11.0.100): a user with a later 11.0 feature band builds without editing, where the repository keeps `latestPatch` for its own reproducibility. No `allowPrerelease`.
  3. **One name, derived, instead of `sourceName`:** `-n my-app` makes `my-app/my_app/my_app.csproj`, with `my_app` everywhere inside; with `sourceName` the folders were `my-app` and the paths in the solution and test projects `my_app` (pitfalls). The cost: the folders take the namespace form, not the name as typed. Non-ASCII letters stay (valid in C#); the npm name is the lower-case form.
  4. **`--platforms` is one choice of three** (`both`, `windows`, `linux`), not a list option (`--platforms windows --platforms linux`): three values cover every case and read plainly in `-h`; the project always says `SparPlatforms`, `windows;linux` included, so the property is visible where it is changed.
  5. **`--app-id`, default `com.example.<name>`:** the name with every character outside `[A-Za-z0-9_.]` made `_` and a part's leading digit given a `_`, so the default always passes SPAR130's format (`com.example._1x._2y` for `1x.2y`). `dotnet new` does not validate a given id; the build does. `com.example` builds, and the guide and the option's help say to replace it before shipping.
  6. **`--no-end-to-end-tests`** for the option the user decision left unnamed: it reads as what it does, and the CLI's own `--no-…` options are the same shape.
  7. **No `nuget.config` in a new app:** Spar's packages come from the user's NuGet sources (nuget.org once published), like any package; a config would pin a source the app does not need. The package tests and the guide's runs put theirs beside the app or above it.
  8. **No contract snapshot in the template:** its fingerprint follows the app's names, so the first build writes it; the guide says to commit it, since a CI build fails on a missing one (SPAR100).
  9. **No `SparFrontendPath` in the project:** `frontend/` is the default, so the property would only repeat it.
  10. **No post-action restore, and `primaryOutputs` the solution only:** the first build restores anyway, and a restore right after `dotnet new` would need the app's NuGet sources before the user has seen the folder (the package tests write theirs after).
  11. **Not in 6a:** the local tool manifest (6b; a manifest naming a package that does not exist yet breaks `dotnet tool restore`), and the template's CI workflow that step 4's decision mentions: it cannot run before a push, so it is proposed with 6b or later rather than added unrun.
  12. **The package is in `Spar.slnx`:** it builds an empty assembly with the solution (0 warnings), so the IDE shows the content beside the other packages.
  13. **The guide's path is `docs/guide/getting-started.md`,** a folder for user-facing guides apart from the design records; Starlight front matter, shell blocks only. Its commands are the package tests' and were run by hand on both OSes; a check that runs the guide's shell blocks from the Markdown (as `tests/AppModel` extracts `docs/app-model.md`'s blocks) is proposed, not built: the package tests already run the same commands on the same template, and the guide has no C# or TypeScript block to compile.
  14. **Playwright's Chromium in the guide is `pwsh … playwright.ps1 install chromium`,** Playwright's own documented way, which needs PowerShell on Linux too; the full install (about 719 MB on Windows) rather than `--only-shell`, which CI uses.
- **Numbers:** `Spar.Templates` 29,350 B, packed in 1.1–2.5 s; `dotnet new spar-app` 0.24–0.9 s; `Spar.PackageTests` 23 of 23 on Windows in 3 min 33 s and on Linux in 4 min 47 s (18 before, 3 min 1 s and 4 min 35 s), the new app without its end-to-end project's `dotnet test` 13.0 s on Windows; the guide's runs from packages: the new app's window `ui-ready` 12.7 s after the first `dotnet run` on Windows (its first build included; the host's own 1,267 ms) and at 584 ms on Linux, a method body under `dotnet watch` applied 0.84 s (Windows) and 0.4 s (Linux) after the save, the published executables 4,739,072 B (Windows) and 4,671,672 B (Linux) at `ui-ready` in 356 and 220 ms, Playwright's Chromium 719 MB.
- **Runs:** Windows: `dotnet build Spar.slnx -c Debug` with 0 warnings; the CoreCLR tests without window and black-box 1,150 (1,127 passed, 23 skipped); `Spar.WindowTests` 220 (118 passed, 102 skipped); `Notebook.EndToEndTests` 27 (23 passed, 4 skipped); the TypeScript SDK's `npm run check`, 330 tests and `npm run build`; `Spar.BuildTests` 6 of 6; `Spar.PackageTests` 23 of 23; `eng/check-size.cs` 586 files clean; the guide's commands (the evidence's table). Linux (WSL, `docs/evidence/m1c-blackbox/wsl.sh sync client appmodel build test`, then `packagetests`): 1,150 (1,109 passed, 41 skipped), `Spar.PackageTests` 23 of 23 in 4 min 47 s with 0 processes left; the guide's commands.
- **Not verified:** CI; `dotnet new install Spar.Templates` from nuget.org (the same package installed by id from a local feed); the prerequisites' install commands (package names checked with `dpkg` on Ubuntu 24.04 only); `playwright.ps1` on Linux and `--with-deps`; F5 of a new app in both IDEs (added to the user's table); an on-screen window of a new app; `Spar.WindowTests` in WSL and the NativeAOT lanes of Spar's own projects (nothing they build changed); a name with characters npm refuses (`-n 1Nötes.2x` gives the npm name `_1nötes._2x-frontend`, not built).
- **Left behind:** nothing on Windows (no process of the runs, no WebView2 process of their roots, no entry in the user's `%USERPROFILE%\.templateengine` or `%LOCALAPPDATA%`; the guide runs' `%TEMP%\spar-guide-m1d6a` deleted after, their app folders and profiles left in the disposable `artifacts/webview2-profiles/m1d6a-guide/`). In WSL, `/var/tmp/spar-guide-m1d6a` deleted after, and, as every Linux window run leaves until the next, the test-host roots in the copy.

## M1d step 6b's shape: the spar tool (user decisions, 2026-11-22)

Asked before building step 6b (plan, "M1d", step 6; `docs/design/agents.md`), each the recommended option:
- **The tool's package is `Spar.Tool`, its command `spar`.** The runtime package already has the id `Spar`, so a tool cannot take it, and `dnx spar` would fetch the runtime. Through the template's local tool manifest the tool runs as `dotnet spar doctor` (or `dotnet tool run spar …`), and without one as `dnx Spar.Tool doctor`; this corrects the design's `dnx spar …`. Not taken: renaming the runtime package (for example to `Spar.Runtime`) so the tool could be `Spar`, which touches every package reference, the SDK and the package tests.
- **The build identity is read from the executable.** The build embeds the variant, the git revision, whether the tree was dirty and the contract fingerprint as a small marked block in the app; `spar doctor --app <exe>` reads it from the file without running the app, and the host writes it in its first log line (decision "M1d's decide-first questions"). This works on any built or published executable. Not taken: a record under `obj/`, which cannot tell what a given executable, such as a published copy, was built from.
- **`spar init frontend` wires an existing Vite frontend and can scaffold one first.** It adds the `@spar/app` dependency, the Vite plugin and, if needed, `SparFrontendPath`; it shows the diff and asks before writing (`--yes` skips the question), and prints the snippet when `vite.config` is too unusual to edit. `--framework react|vue` first runs Vite's official scaffolder at a pinned version. Not taken: wiring only, with `--framework` later.

## The spar tool (implementation record; choices for review; 2026-11-22)

Plan M1d, build step 6, its second half 6b (decisions "M1d's decide-first questions", "M1d step 6's shape: the template package and the spar tool" and "M1d step 6b's shape: the spar tool"). Evidence: `docs/evidence/m1d-spar-tool.md`. The guide: `docs/guide/getting-started.md`, section 8.
- **Built:**
  - **The build identity:** `src/Spar.Generator/Emit/IdentityEmitter.cs` writes `Spar.BuildIdentity.g.cs` in every app and variant, `SparBuildIdentity.Block`, the UTF-8 literal `[spar-build-identity/1 variant=… revision=… dirty=… fingerprint=…]`; the catalog names it (`SparCatalog.BuildIdentity`); `src/Spar/Runtime/BuildIdentity.cs` parses and describes it; the Windows, Linux and loopback hosts write it in their first line (`Host started … ms after the process; build Production, revision … (no uncommitted changes), contract fingerprint ….`). `Spar.Sdk`'s target `SparBuildIdentity` (apps, real builds) sets `SparBuildRevision` from the SDK's `SourceRevisionId` and `SparBuildDirty` from `git --no-optional-locks status --porcelain` in the work tree's root, both `CompilerVisibleProperty`s, and writes `obj/…/spar-build-identity.txt`; `SparBuildIdentityDesignTime` reads that record in design-time builds; `SparBuildIdentity=false` leaves the two values out.
  - **`src/Spar.Tool`:** the package `Spar.Tool`, command `spar` (`PackAsTool`, framework-dependent, RID-agnostic, `IsAotCompatible`), packed with the other eight at the same version; 25 source files in `Doctor/`, `Frontend/`, `Identity/`, `Init/`, `Machine/` and `Project/`, with `BuildIdentity.cs` and `FrontendInstallState.cs` linked as source. `spar doctor [--project] [--app] [--json]`, `spar init frontend [--project] [--frontend] [--framework react|vue] [--yes]`, `spar --version`, help.
  - **Shared with the build:** `src/Spar.Sdk.Tasks/FrontendInstallState.cs`, the rule "`node_modules/.package-lock.json` is not older than `package-lock.json`", now used by `SparFrontendInstall` and by doctor.
  - **The template:** `.config/dotnet-tools.json` naming `spar.tool` at `SPAR_SDK_VERSION` (the constant that also fills `global.json`), `isRoot`, `rollForward` false.
  - **Tests:** `tests/Spar.Tool.Tests` (79: the machine checks on `FakeMachine`, the project checks and init's edits on a copy of the template's app under `artifacts/tool-tests/`, init's flow with a fake npm, the identity's format and reader, the command line and exit codes, the tool's minimums against the hosts' sources); `Spar.Generator.Tests` `BuildIdentityTests` (6); `Spar.BuildTests` reads the identity of the harness's NativeAOT executable and CoreCLR assembly and of the Notebook's published executable against the repository's HEAD and `git status` (7 tests, 1 new); `Spar.PackageTests` packs nine packages, `NewAppTests` checks the manifest, and `ToolTests` (4) runs `dotnet tool restore` (and checks the package holds the WebView2 loaders), `dotnet spar doctor --json` clean and with `node_modules` moved away, the identity from the published executable and the Debug build, and `dotnet spar init frontend --framework react` with the pinned scaffolder in a new app that then builds; `BlackBoxFixtureTests` checks the host's first line on WindowHost's executable (both OSes).
  - **Records:** the guide's section 8, its tree and its table; `docs/app-model.md` section 1 (the manifest in the layout and in the paragraph on `dotnet new`; no code block changed); `docs/design/agents.md` (the tool's line corrected to `dotnet spar …` and `dnx Spar.Tool …`, with what was built); `docs/design/build.md` (the tool in "Packages"); `AGENTS.md`'s `src/` row; `spar-msbuild`, `spar-testing` and `spar-contract-generation`; eleven pitfalls; the plan's step note and a row in "Checks that need the user at the PC" (init's question in a real terminal); the evidence's scripts `guide-win.sh` and `guide-linux.sh`.
- **Choices made while building, for review:**
  1. **The block's format:** one printable ASCII line in a UTF-8 literal, versioned in its marker (`/1`), values limited to `[A-Za-z0-9._-]` and empty for unknown, keys a reader does not know ignored. A literal's bytes sit in the IL image and in a NativeAOT executable as written (read from both by the build tests), so one reader serves every build. Not taken: an assembly attribute (NativeAOT keeps no attribute that nothing reads) or a Win32 resource (none in a Linux ELF). It is in every variant, Test included, and in every compilation the generator runs in; only apps get a revision and a tree state.
  2. **The revision is the .NET SDK's `SourceRevisionId`,** which Source Link in the SDK reads from `.git` without a process (worktrees and packed refs included); the target depends on `InitializeSourceControlInformation`, as the common targets ask of a reader. An app with `EnableSourceControlManagerQueries=false` gets no revision.
  3. **"Dirty" is `git --no-optional-locks status --porcelain` of the whole work tree listing anything:** a tracked change, staged or not, or an untracked file that is not ignored, with submodules as git reports them; its standard error discarded. Not taken: the project's folder only (a referenced library's change would be missed), `diff-index` (it misses a new file the build compiles), comparing the index in-process. `--no-optional-locks` keeps the build from taking `index.lock` from the developer's own git commands.
  4. **An `Exec` of git, not a compiled task:** the step runs on every build, and in source mode a Spar task runs under `TaskHostFactory`, a task host per build; the target costs 151 ms on the Notebook, git's process most of it. In package mode a task would run in the node; proposed if the cost matters.
  5. **Design-time builds read the record and run no git:** a design-time build writes the generator's editorconfig too, and without the record it wrote the two values empty, so the next build compiled again (measured). The record is the last build's, so an IDE's in-memory compile may show an identity one commit old.
  6. **Incrementality:** the identity changes on a commit (already a recompile: the SDK's `InformationalVersion` carries the revision) and when the tree turns dirty or clean; each recompiles the C# once, a page or document edit's first change included, and relinks a NativeAOT publish; the client and frontend steps never rerun for it. The no-op build: +0.14 s (0.695 s against 0.555 s medians, warm server), within the 1.5 s budget. The block has its own file, so Hot Reload's shape and the contracts' code never change with it (a body edit under `dotnet watch` applied in 0.60 s).
  7. **The first log line:** the identity is appended to each host's existing first line ("Host started …; build …"), and on loopback put before "app id …, folder …", which readers match last. Not taken: a line of its own.
  8. **Doctor's ids** (stable, for agents; a message may change): machine `dotnet-sdk`, `spar-sdk`, `spar-tool-version`, `node`, `node-version-manager`, `npm`, `webview2-runtime`, `windows-nativeaot`, `windows-long-paths`, `linux-gtk4`, `linux-webkitgtk`, `linux-sandbox-tools`, `linux-icu`, `linux-openssl`, `linux-clang`, `linux-zlib`; project `project`, `frontend-folder`, `frontend-spar-dependency`, `frontend-vite-plugin`, `frontend-scripts`, `frontend-lockfile`, `frontend-node-modules`, `frontend-type-check`, `contract-snapshot`, `launch-profiles`; identity `build-identity`, `build-identity-contracts`, `build-identity-dirty`. A check that passes says so as information; information is also what is not wrong (long paths off, Spar.Sdk not restored yet, no build yet, no project in the folder, Spar's own source tree).
  9. **Exit codes, one set for both commands:** 0 nothing to fix (init: written, or nothing to do), 1 warnings (init: nothing written), 2 errors (init: a step failed), 3 init written except Vite's configuration (the snippet printed), 64 a wrong command line, 70 the tool itself failed (sysexits' `EX_USAGE` and `EX_SOFTWARE`).
  10. **Severities:** errors for what stops a build or a run (no SDK, no Node or npm, a Node older than Vite 8 runs on, no WebView2 Runtime or one older than the host's minimum, no GTK 4 or WebKitGTK 6.0 or older than the host's minimums, no `bwrap` or `xdg-dbus-proxy`, no frontend, no `@spar/app` dependency, no plugin, no `dev` script, no lockfile, a `global.json` without Spar.Sdk); warnings for what bites later (Node before 24, publishing's prerequisites, ICU, OpenSSL, a stale or missing `node_modules`, a missing or stale snapshot, the profiles, no `build` script, the type-check scope, a tool at another version than Spar.Sdk, a build whose contracts differ from the page's client, a Production build from a dirty tree).
  11. **Node through version managers:** only when `node` is not on `PATH` are their documented folders searched (variables first, then defaults): nvm (POSIX), nvm-windows (`NVM_HOME`, `%APPDATA%\nvm`, `%LOCALAPPDATA%\nvm`), fnm, Volta, mise, asdf (POSIX). The version is read from the folder's name, no found binary is run, and the fix is the manager's own way to put it on `PATH`.
  12. **Windows' machine checks:** the WebView2 Runtime through the loader's `GetAvailableCoreWebView2BrowserVersionString`, as the host checks it, against the host's minimum (a test reads the host's source to keep them equal), with the loader in the tool package as native assets (three DLLs of about 190 KB); NativeAOT's C++ build tools through `vswhere -requires …VC.Tools.x86.x64` (or `ARM64`) and `vcvarsall.bat`, as the ILCompiler package's own script finds them; long paths from the registry, as information.
  13. **Linux's machine checks:** the libraries loaded by name (`NativeLibrary.TryLoad`, as the host loads them, never initialised), with GTK's and WebKitGTK's versions from their exports against the host's minimums; the sandbox tools and `clang` on `PATH`; ICU and zlib's development library in the library folders, OpenSSL loaded; install commands for apt and dnf by `/etc/os-release`'s family with the host's own package names (`libwebkitgtk-6.0-4`/`webkitgtk6.0`), Microsoft's for NativeAOT (`clang zlib1g-dev`/`clang zlib-devel`), and `libicu-dev`/`libicu`, `openssl`/`openssl-libs`, `libgtk-4-1`/`gtk4` (only Ubuntu's names were checked); the display is not checked, since doctor may run over SSH.
  14. **Shared checks:** the install rule is shared as source with the build's task; the contract snapshot is compared as the build compares it (CRLF read as LF, against the newest `obj/**/contracts.snapshot`) in the tool's own code, because the build's check is MSBuild property functions on every build and a task for it would add a task host per build in source mode; the other project checks are ones the build does not make.
  15. **The project as its file says it:** an app is a project with `Sdk="Spar.Sdk"`, an `Exe` or `WinExe` output and no `IsTestProject`, in the folder or one level down (a solution's folder); its properties are the file's literal values, not evaluated (no MSBuild in the tool, and fast), so a `SparFrontendPath` set in an imported file is not seen.
  16. **Where the identity comes from:** `--app <file>`, or the newest file under the project's `bin/` named after its assembly that holds a block; an apphost is read through the DLL of the same name beside it; a compressed single-file bundle has none that can be read, which doctor says. Doctor still checks the project with `--app`.
  17. **init stages a scaffold under `obj/spar/init/<id>/`** and moves it into place only on yes, so the authored tree is untouched until then; without a terminal and without `--yes` it runs nothing, the scaffolder included, and says what it would do; one question covers every change.
  18. **init edits text, never reserializes:** `package.json` at the reader's byte offsets, `@spar/app` first in `dependencies` (npm's order), the file's indentation and line ending, `dev` and `build` scripts added when missing; `vite.config.{js,mjs,ts,mts}` only with exactly one `plugins: [ … ]` array once comments and strings are masked, keeping the file's quotes and semicolons, the import after the last one, `sparPlugin` when `spar` is taken; `.cjs`/`.cts`, a configuration without the array or with nested `plugins` arrays, and a partly wired file get the snippet (exit 3); `SparFrontendPath` at the end of the first unconditional `PropertyGroup` with a comment, or replaced. The folder must be inside the app's, and not `bin/`, `obj/` or `node_modules/`.
  19. **init installs after wiring:** `npm install --ignore-scripts --no-audit --no-fund` whenever `package.json` changed or the lockfile or `node_modules` is missing, since the build's `npm ci` needs a lockfile in step; with a stand-in `obj/spar/client/package.json` (`@spar/app` 0.0.0, as the template's lockfile was made) when the build has not written the client yet.
  20. **The scaffolder is create-vite 9.3.4** (the latest, published 2026-11-05), run as `npm create --yes --ignore-scripts vite@9.3.4 <folder> -- --no-interactive --no-immediate --template react-ts|vue-ts`; its own `README.md`, `.gitignore` and lint configuration stay.
  21. **The scaffold's type-check gap is reported, not changed:** Vite's `tsconfig.json` has only references, so the build's `tsc --noEmit -p .` checks nothing (a type error passed); doctor warns with two fixes. Proposed, not built: the build's type-check following references (`tsc -b`) when the configuration has them.
  22. **The tool's own code:** a hand-written argument parser (no `System.CommandLine` dependency); JSON read with `JsonDocument` and written with `Utf8JsonWriter`, no reflection; processes through .NET 11's `Process.RunAndCaptureText`, npm on Windows through `cmd.exe` by its full path; the machine behind `IMachine`, so every machine check is tested with a fake. A NativeAOT publish of it gave 0 warnings and ran (7.5 MB); not shipped.
  23. **The tool is not in the static-state list:** it runs one command in a process of its own and hosts no app; its static fields are read-only (two time limits, a few arrays of names, generated regex caches).
  24. **`Spar.Tool.Tests` is a test project of its own,** not part of `Spar.Tests` (37 files of its folder's 40), in `Spar.slnx`; the package tests give every tool command `DOTNET_CLI_HOME` in the run's folder, and share one NativeAOT publish of the consumer between the existing publish test and `ToolTests`.
- **Numbers:** the Notebook's no-op Debug build 0.695 s with the identity against 0.555 s without (medians of 16 each, warm server; isolated 2.43 against 2.27 s); the target 151 ms; the Production Notebook 5,279,744 B (5,274,112 B at HEAD, +5,632 B); `Spar.Tool` 365,974 B, packed in 1.5–2.6 s; `dotnet spar doctor` 0.6–1.5 s; `dotnet spar init frontend --framework react --yes` 11.0–12.8 s (npm's downloads included); `Spar.PackageTests` 28 of 28 on Windows in 2 min 23–30 s and on Linux in 3 min 58 s.
- **Runs:** the evidence's table. Windows: build 0 warnings; CoreCLR tests 1,237 (1,214 passed, 23 skipped); `Spar.WindowTests` 220 (118 passed, 102 skipped); `Notebook.EndToEndTests` 27 (23 passed, 4 skipped); the TypeScript SDK's 330 tests; `Spar.BuildTests` 7 of 7; `Spar.PackageTests` 28 of 28; `eng/check-size.cs` 627 files clean; the guide's tools section from packages. WSL: build 0 warnings; CoreCLR tests 1,237 (1,196 passed, 41 skipped); `Spar.WindowTests` 220 (170 passed, 50 skipped, 0 processes left); `Spar.PackageTests` 28 of 28 in 3 min 58 s (0 processes left); the guide's tools section from packages. `Spar.PackageTests` and `Spar.BuildTests` ran again on Windows after the tool package's `PrivateAssets` fix (28 of 28 in 2 min 23 s, 7 of 7).
- **Not verified:** doctor on a machine that really lacks something (fakes only); the version managers other than nvm-windows on a real machine; init's question in a real terminal (the user's table); `--framework vue` with the real scaffolder; a compressed single-file bundle; CI; `dnx Spar.Tool` from nuget.org; macOS.

## M1d step 7's shape: runtime choices (user decisions, 2026-11-23)

Asked before building step 7 (plan, "M1d", step 7; decisions "App authors choose the runtime and their own AOT warning policy" and "App-specific settings stay in the app"), each the recommended option:
- **The recipes live on a new guide page,** `docs/guide/publishing-choices.md`, linked from the getting-started guide's section 7, understandable on its own; its project-file snippets are built by a test. Not taken: a section of the getting-started guide (one longer page), or the page plus commented-out lines in the template's app project.
- **An app accepts its own AOT and trim warnings with plain MSBuild in its own project, and they stay visible:** the accepted IDs stay warnings rather than errors, so every publish still shows them. Spar adds no API for it. Not taken: a Spar item naming the accepted assemblies (new SDK machinery), or `NoWarn` (it hides whole assemblies from later publishes, since SqlClient's warnings are assembly-level under the SDK's default `TrimmerSingleWarn=true`).
- **The SqlClient single-file recipe is tested by a package test without a server:** a consumer published with the recipe has the executable alone in its publish folder, and `Open` against a closed port fails with `SqlException`, not `DllNotFoundException`; on Windows and Linux, in CI. Not taken: an explicit lane against SQL Server 2022 in a container, or the documentation alone with `spikes/sqlclient-aot` as the evidence.
- **Two CoreCLR publications are tested on both OSes:** framework-dependent and self-contained single file, from packages, each started to `ui-ready` on Windows and Linux in the package tests. Not taken: ReadyToRun as a third shape, or Windows only.

## Runtime choices (implementation record; choices for review; 2026-11-23)

Plan M1d, build step 7 (decisions "App authors choose the runtime and their own AOT warning policy", "CoreCLR as the app's escape hatch (evidence)", "App-specific settings stay in the app" and "M1d step 7's shape: runtime choices"). Evidence: `docs/evidence/m1d-runtime-choices.md`. The guide: `docs/guide/publishing-choices.md`.
- **Built:**
  - **The guide** `docs/guide/publishing-choices.md`, linked from the getting-started guide's section 7: the defaults and what an app may choose in its own project; CoreCLR framework-dependent and self-contained single file (what each ships and needs, the runtime identifier, the WebView2 loader, Linux's libraries, a measured table); accepting a library's warnings with `WarningsNotAsErrors` (`IL2104;IL3053`), what that accepts, `-p:TrimmerSingleWarn=false` to read the per-member warnings, never `NoWarn`; `InvariantGlobalization=false` with its cost and ICU on Linux; Microsoft.Data.SqlClient as the worked example, with what managed networking changes and what Spar did not test. Six project blocks, each after a marker `<!-- project-snippet: name -->`.
  - **Spar.Sdk** (`Sdk.targets`): the WebView2 loader's `None` item is `ExcludeFromSingleFile` unless the app sets `IncludeNativeLibrariesForSelfExtract`; the black-box publish copies every file of the app's publication but `*.pdb` and `*.dbg` beside the tests (it copied the executable alone); Spar's test projects default `ValidateExecutableReferencesMatchSelfContained` to false. Spar's defaults are unchanged: NativeAOT, `InvariantGlobalization=true`, no accepted warnings.
  - **Tests** (`Spar.PackageTests`, 38, from 28): `GuideSnippets` reads the page's blocks and `GuideSnippetTests` fails on a block no test builds; `FrameworkDependentTests` and `SingleFileTests` (`CoreClrTests.cs`) make a new app each (`Folio`, `Atlas`), add the guide's block, publish with the guide's command, check the files, read the identity with `dotnet spar doctor --app` (the DLL beside the apphost; the single file itself) and start the app to `ui-ready` through its own black-box tests and a start-time test the run adds (five launches), with every `PATH` folder holding a WebView2 loader removed; `SingleFileTests` also runs the app's fast tests and, on Windows, publishes without native self-extraction (the loader beside); `SqlClientTests` makes `Ledger` with SqlClient 7.1.2 and a probe mode in its `Program.cs`: the NativeAOT publish without the accepted warnings fails on `IL2104`/`IL3053`, with the recipe it passes showing them as warnings and is the executable alone, the probe gets `SqlException` against a closed loopback port and `NotSupportedException` in invariant mode, and on Windows without managed networking the executable alone gets `DllNotFoundException`; `TemplateTests`' black-box test adds the start-time test for the NativeAOT row and checks the executable alone beside the tests. `Consumer.OnceAsync` (work shared by a class's tests), `Consumer.Measure` (`measurements.txt` beside `timings.txt`), and `Tool.RunAsync`'s `limit` (the process tree ended after it).
  - **Records:** this entry; the guide's link in `getting-started.md`; `AGENTS.md` (`docs/guide/` and `tests/` rows); `docs/design/build.md` and `docs/design/hosts.md`; `spar-msbuild` and `spar-testing`; ten pitfalls; the plan's step note; the evidence and its scripts `guide-win.sh`, `guide-linux.sh` and `sql-probe.sh`.
- **Choices made while building, for review:**
  1. **The loader in a single file follows .NET's rule for native libraries** (SDK machinery, not a recipe line): beside the executable by default, inside the bundle with `IncludeNativeLibrariesForSelfExtract`. Without a mark the publish failed (NETSDK1236), so Spar had to mark it either way. Not taken: always bundling it (forcing extraction on apps that did not ask for it) or setting `IncludeNativeLibrariesForSelfExtract` for the app (an app setting Spar would set).
  2. **The guide's single-file block includes `IncludeNativeLibrariesForSelfExtract`,** so the publication is one file; the guide says what extraction writes (`%TEMP%\.net\<app>\…`, or `DOTNET_BUNDLE_EXTRACT_BASE_DIR`) and how to keep the loader beside it instead. Not taken: the two-file default as the block.
  3. **The black box copies the whole publication** (debug symbols aside), for every shape: a CoreCLR app's black-box tests could not run with the executable alone, and a NativeAOT app with a native sidecar (SqlClient's SNI DLL without managed networking) would not have either. For a NativeAOT app without sidecars nothing changes (the executable alone, checked). Files of an earlier shape left in the test project's `obj/…/spar-apps/` would be copied too (not handled; the package tests give each shape its own app).
  4. **`ValidateExecutableReferencesMatchSelfContained=false` in Spar's test projects:** the .NET SDK skips that check for VSTest test projects because they call into a referenced executable rather than run it, which is what Spar's test projects do too; Microsoft.Testing.Platform projects keep it, and a self-contained app's fast and end-to-end tests failed with NETSDK1151. A project that sets the property keeps its value. Checked: a self-contained app's fast tests (2 of 2), its black-box tests and its window test passed, with no runtime files in the test output.
  5. **`WarningsNotAsErrors` is the whole MSBuild for "these IDs stay warnings":** read in the ILCompiler 11.0.0 targets (`--nowarnaserr`) and in `Microsoft.Common.CurrentVersion.targets` (the default of `MSBuildWarningsNotAsErrors`), and run: exit 0 with the three warnings printed under the template's `TreatWarningsAsErrors` and `MSBuildTreatWarningsAsErrors`. No `IlcTreatWarningsAsErrors` is needed. The per-member view is a command-line `-p:TrimmerSingleWarn=false` publish for reading, which fails; per-assembly detail exists only as `TrimmerSingleWarn` metadata on an SDK-internal item (targets read, not run) and is not in the guide. The guide advises against accepting the per-member IDs, which the app's own code shares.
  6. **The guide's markers and their test:** `<!-- project-snippet: name -->` right above a fenced `xml` block, parsed line by line; every block is complete and goes before `</Project>`, so a test adds several at once as an author pastes them. The SNI package's block carries `Version="7.1.2"` (without it NU1015 outside central package management).
  7. **Test layout:** three new consumers in classes of their own, so they run beside the main consumer's collection: the suite went from 28 tests in 2 min 23–30 s to 38 in 2 min 37 s and 3 min 16 s (two runs) on Windows, and runs in 3 min 30 s and 3 min 38 s on Linux (3 min 58 s before, with colder caches). Each shape is its own new app rather than one app switched between shapes, whose earlier shape's files stay in `bin/` and `obj/`.
  8. **Invariant mode is checked on the recipe's executable with `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`,** not with another NativeAOT publish; the recipe's `InvariantGlobalization=false` is what lets the same executable reach the network. The Windows-only check that the executable alone needs the SNI DLL without managed networking costs one more NativeAOT publish (about 9 s warm).
  9. **The guide reports SqlClient's own caveat:** its documentation calls `UseManagedNetworkingOnWindows` a switch "for testing and debugging purposes only" and says managed networking does not support non-domain Windows authentication. The guide keeps the single-file recipe, as the user decided, and says how to keep the native library beside the executable instead. The user may prefer the two-file publication as the guide's default for Windows.
  10. **The guide's table uses the sequential guide runs** (Windows on an offscreen desktop, Linux on a private Xvfb), not the package tests' numbers, which were taken beside other consumers' builds.
- **Found, for the user to decide:** a CoreCLR publication carries Spar's test and development code. `Spar.dll`, `Spar.Windows.dll` and `Spar.Linux.dll` in the framework-dependent folder hold `SparTestSession`, the development server's and Hot Reload's types (`DevServer`, `BackendUpdates`); they are switched off by the Production runtime configuration (`Spar.TestHost.IsSupported`, `Spar.Development.IsSupported`, `Spar.DevServer.IsSupported` all false), but not removed, since only NativeAOT (or trimming) removes code behind a feature switch. The loopback host is not there (Production never references it). AGENTS.md's rule that Production builds contain no test or development code, and the boundary test that proves it, hold for the NativeAOT executable only. The guide says so. Options, none taken: trimming the self-contained single file (`PublishTrimmed`, which would also need the app's libraries to be trim-safe, the reason an app leaves NativeAOT), separate Production builds of Spar's assemblies without that code (new packages), or accepting it for CoreCLR and saying so in the rule.
- **Numbers:** sizes without debug symbols, Windows / Linux: NativeAOT 4,745,216 / 4,679,880 B; framework-dependent 1,812,344 B in 10 files / 1,574,689 B in 9; single file 81,168,450 / 78,956,833 B (compressed, Windows: 41,156,342 B); `InvariantGlobalization=false` +218,112 / +185,424 B; the SqlClient recipe 15,043,072 / 15,239,176 B. Start to `ui-ready`, medians of five (guide runs): NativeAOT 378 / 201 ms, framework-dependent 423 / 263 ms, single file 441 / 265 ms; one first start of a fresh single file in the package tests took 5,645 ms (not isolated). SqlClient 7.1.2 in a new app: 3 assembly-level warnings, 31 per-member ones. The closed-port probe: 3.1–3.4 s on Windows, 2.5 s on Linux (`Connect Timeout=3`).
- **Runs:** the evidence's "Runs", with counts and times on both OSes.
- **Not verified:** Windows integrated authentication, named pipes and shared memory with managed networking; any real SQL Server (by decision); a machine without .NET for the single file (both machines had it); a framework-dependent app with .NET in a custom location (`DOTNET_ROOT` was set in WSL); CoreCLR cross-publishing; the single file without native self-extraction started (published only); a compressed single file started; ReadyToRun (not taken); CI, including its hosted Linux runner, where the start parts skip; macOS and arm64.

## Step 7's two findings (user decisions, 2026-11-23)

Asked after building step 7 (implementation record "Runtime choices (implementation record; choices for review; 2026-11-23)", "Found, for the user to decide", and choice 9):
- **Spar's test and development code in a CoreCLR publication is accepted for now.** `Spar.dll`, `Spar.Windows.dll` and `Spar.Linux.dll` keep the test session, the development server and Hot Reload's types in a framework-dependent or single-file publication, switched off by its runtime configuration; only NativeAOT removes them, so `AGENTS.md`'s rule that Production builds contain none holds for the NativeAOT executable, which the boundary test proves. To look at later: whether that code can move into assemblies of its own that only the Development and Test variants reference, as the loopback host already does (`Spar.Loopback`), so a CoreCLR publication has none of it either. Not taken: a second Production build of the three assemblies selected by Spar.Sdk now, or a test of the publication's switches (the user chose to accept it as it is for now).
- **The guide keeps the Windows SqlClient publication as built:** the single-file block (managed networking, the SNI package excluded) stays optional, with SqlClient's own caveat that the switch is meant for testing and debugging, what Spar did not test, and that leaving the block out keeps the native library beside the executable. Not taken: the two-file publication as the guide's Windows default.

## M1d exit's shape (user decisions, 2026-11-24)

Asked before the M1d exit (plan, "M1d", "Exit"), each answer the recommended option where one was offered:
- **Budgets: M1d's two, and every earlier line again.** The no-op build of the Notebook with the frontend step (≤ 1.5 s) and Hot Reload of a method body (≤ 4 s) are measured on Windows and Linux; `BudgetTests` runs again on both OSes, so the launch, the round trips, the window test, the Notebook executable's size and the memory lines are checked on M1d's code, which added the build identity, the start-up styles and Development code switched off to every app. Not taken: M1d's two only.
- **The IDE and on-screen checks are the user's, prepared by the agent.** The automated exit work comes first; then one short checklist from the table's rows (the Notebook's F5 in Visual Studio and VS Code, a new app's F5, `spar init frontend`'s question in a real terminal, the start-up splash on an on-screen window), with the packages already built, for the user to run at the PC. M1d closes when the user reports the results. Not taken: closing with the rows open, or the agent driving the IDEs through computer use.
- **One proposed follow-up goes into the exit: the template's CI workflow** (proposed in "The template package (implementation record; choices for review; 2026-11-22)", choice 11). The other proposals stay open: the Linux package tests' window and black-box runs in CI's Linux image (step 4's record, choice 13), the build's type-check following project references (`tsc -b`, the spar tool's record, choice 21), and the layout rows `AGENTS.md` lacks.

## M1d budgets measured at the exit (implementation record; 2026-11-24)

Plan M1d, "Exit" (decision "M1d exit's shape (user decisions, 2026-11-24)"). Evidence: `docs/evidence/m1d-exit.md`, raw files and runners in `docs/evidence/m1d-exit/`. The method is the M1b and M1c exits' (`BudgetTests` in Release on both test lanes, WindowHost's Production NativeAOT executable through the black-box fixture, 1 warm-up launch and 10 on the same data root, 20 window tests, 200 echoes), with `WireShapes`' `BudgetTests` for the M1a lines; on the development PC, Windows and WSL 2 Ubuntu 24.04.5, on `134d50e` with no product code changed.
- **M1d's two:**
  - **No-op build of the Notebook with the frontend step (≤ 1.5 s):** met. Windows 1.32 s (1.29–1.35, n = 10), Linux 0.48 s (0.42–1.46, n = 10), Debug, with the MSBuild server warm; isolated (`Spar.BuildTests`, no server) 9.54 s and 2.13 s, n = 1 each. The Windows warm figure was 0.695 s in the 6b record (22 November).
  - **Hot Reload of a method body applied (≤ 4 s): missed on Windows, met on Linux.** On the Notebook under `dotnet watch -lp Loopback`, from the write to "changes applied": the first edit of a session 4.70 s (1.13–4.86, n = 4) on Windows, over 4 s in the 3 sessions where it was written about 2 s after "Waiting for changes", and 2.93 s (2.58–3.34, n = 4) on Linux; later edits 0.12 s (0.07–0.21, n = 12) and 0.11 s (0.06–0.20, n = 12). **Cause as measured:** the first update waits 3.7 s after the solution update for work `dotnet watch` does once after its start (its Hot Reload capabilities line comes then); an edit written 8.4 s after the session started took 1.06 s; Spar's generator is 0.086 s of the Notebook's compilation. **A new app from packages** applied its first edit, written at its window's `ui-ready` (as early in its session), in 0.85 s (0.84–0.86, n = 3): the Notebook's workspace holds Spar's six source projects where an app's holds packages, which is the likely cause (inferred, not traced). The budget is not changed: for the user to decide whether it is read on an app from packages, on the Notebook from source, or once the session has warmed.
- **Every earlier line again, all met except one at its budget:**
  - **Browser test (≤ 250 ms), Windows: 247.80 ms (240.01–269.14, n = 9) and 250.33 ms (244.59–255.88, n = 9), over by 0.33 ms in the second run;** Linux 64.88 ms. **Cause: this machine since 2026-11-16, not Spar's code.** The M1a commit (`d745ea5`), extracted and run today with the same Playwright 1.64.1 and Chromium build 1248, measures 244.06–250.06 ms, against 122.5–136.5 ms when it was recorded; `ffca7e4`, `9e675cc` and HEAD measure 245.51–256.29 ms. The same holds for the command start (0.86–0.90 ms in the repository, 0.98–1.04 ms for `d745ea5`, against 0.20–0.24 ms at M1a) and the loopback round trip (1.20–1.30 ms, against 0.70), and Windows process starts and isolated builds are slower than in earlier records (`git status` 144–154 ms against 97–126 ms; the isolated no-op 9.54 s against 1.63–1.64 s). Linux in WSL on the same PC is where M1a left it. What changed on Windows was not found (updates on 17, 20 and 21 November; Defender's real-time protection on; no setting was changed to test either).
  - Command test start (≤ 2 ms): Windows CoreCLR 0.86 and 0.90 ms, NativeAOT 0.71 and 0.76 ms; Linux 0.44 and 0.29 ms. Loopback round trip (≤ 2 ms): 1.20 and 1.30 ms; Linux 0.80 ms. Console harness (≤ 3.5 MB): 3,259,904 B and 3,430,832 B. Notebook NativeAOT (≤ 6 MB / ≤ 8 MB): 5,279,744 B and 5,186,848 B.
  - Launch to `ui-ready`, warm (≤ 400 / ≤ 350 ms): Windows 319.50 ms (300.70–398.40, n = 10) and 302.05 ms (292.60–326.20), Linux 152.35 and 151.40 ms (the launch alone 145.40 ms). Desktop bridge round trip (≤ 1 ms): Windows 0.10 ms (p95 0.20), Linux 0.00 ms (p95 1.00, mean 0.285–0.310 ms at the 1 ms page timer). Working set (≤ 60 MB): 45.8 and 45.9 MB, 374 MB with the browser's processes. Window test (≤ 700 / ≤ 500 ms): Windows 304.34 and 294.76 ms, Linux 239.46 and 229.29 ms. Linux PSS (recorded): 199.2 MB for the launch alone.
- **Built for the measurement** (no product or test code): the runners `win.sh` and `wsl.sh` (the M1c exit's runner with M1d's two budgets, the M1a lines, the harness's size and the first-edit sessions), `bisect-win.sh` (the M1a tests at earlier commits from `git archive`, in `%TEMP%`), `collect.sh` (the raw files with paths, the user's name and loopback tokens redacted), and `prepare-user-checks.sh`.
- **The user's checks** of M1d (the plan's table) are prepared as one checklist, `docs/evidence/m1d-exit/user-checklist.md`, with the packages packed and three new apps made in `%TEMP%\spar-guide-m1d6b\work` (`Notes` built, `Init` without its frontend, `Theme` with the splash on, a 1.5 s first render and `setColorScheme`'s buttons, published) and the Notebook's executable in `artifacts\m1d-exit\notebook-win`. M1d closes when the user reports them.
- **Not verified:** the cause of the Windows slowdown, the traced cause of the first edit's time, a cold start, other machines, a new app's first edit on Linux.

## The template's CI workflow (implementation record; choices for review; 2026-11-24)

The follow-up the M1d exit took in (decision "M1d exit's shape", third point; "The template package", choice 11; "Tests in the template", choices 3 and 7). Evidence: `docs/evidence/m1d-exit.md`, "The template's CI workflow". Guide: `docs/guide/getting-started.md`, the file list and section 9.
- **Built:** `src/Spar.Templates/content/spar-app/.github/workflows/ci.yml`; `tests/Spar.PackageTests/Workflow.cs` (a reader of the workflow files' YAML subset) and `WorkflowTests.cs` (4 tests); the guide's section 9.
- **Choices, for review:**
  1. **Every new app gets the workflow,** with no option to leave it out: a repository not on GitHub deletes one file. It runs on pushes to `main` and on pull requests, cancels a pull request's older run, and has `contents: read` only, as Spar's own workflow.
  2. **One job, `build-test`, a matrix of the runners `--platforms` names:** `windows-latest` and `ubuntu-latest` as items of a list under template conditions (`#if (platforms != 'linux')`), so the authored file stays valid YAML and a one-OS app lists one runner; `fail-fast: false`; 30 minutes.
  3. **Steps:** restore; build (Debug, which installs the page's packages, writes the client and type-checks); the fast tests; the page tests (`npm test`); the fast tests published with NativeAOT and run, failing when no test ran; Playwright's headless shell (`--only-shell`, `--with-deps` on Linux); the browser tests; the window and black-box tests (Windows only); `dotnet publish` (Release); the uploads. Each kind of end-to-end test is its own step, as the guide runs them; `--no-end-to-end-tests` removes those four steps.
  4. **No window or black-box tests on GitHub's Ubuntu runner:** its AppArmor setting refuses the unprivileged user namespaces of WebKitGTK's sandbox (decision "The Linux CI container's AppArmor profile"); Spar's own CI runs them in its container with an AppArmor profile, which Spar does not publish (decision "M1c's first questions": Spar ships the Dockerfile only). Proposed for later (M4, with a published image): run them in that image. On Windows the black box uses the runner's desktop, since GitHub sets `CI`.
  5. **The NativeAOT lane is the fast tests'** (the guide's), not the end-to-end project's: it keeps the job short; the hosts' NativeAOT window tests are Spar's own CI's.
  6. **Pins:** the same commits as `.github/workflows/ci.yml` (checkout v7.0.1, setup-dotnet v6.0.0, setup-node v7.1.0, upload-artifact v7.0.2), each with its release in a comment; `WorkflowTests` fails when they differ from the repository's.
  7. **`ContinuousIntegrationBuild=true`,** so the committed contract snapshot is checked (SPAR100), and `DOTNET_CLI_USE_MSBUILD_SERVER=false` (pitfalls: the server did not start in time on a fresh runner). No package caching and no faster-disk step: kept simple; an app adds them.
  8. **`bash` for every step** (`defaults.run.shell`), Git for Windows' on the Windows runner, so one `run` block serves both OSes and the local test runs the same text.
  9. **Results:** `--report-xunit-trx` on every `dotnet test` and `-result-trx` on the NativeAOT executable, into each project's `obj/test-results/`; uploaded as `test-results-<runner>` (`*/obj/test-results/`) even when a step failed, and the published executable without `*.pdb` and `*.dbg` as `<app>-<runner>`, both kept 14 days.
  10. **Linux packages:** only `clang` and `zlib1g-dev` for NativeAOT; Chromium's libraries come from Playwright's `--with-deps`; no WebKitGTK, since no Linux window test runs.
  11. **No `dotnet spar doctor` step:** it reports the machine, which the workflow itself sets up; an app can add it.
  12. **Proved locally, from packages:** `WorkflowTests` reads the generated file (no YAML package added: a reader of the subset, which throws on anything outside it), checks runners and steps per option on three new apps, checks the pins, and runs the main consumer's `run` blocks in order with the workflow's `env` and `RUNNER_OS` through `bash --noprofile --norc -eo pipefail`, as GitHub does. It leaves out the `sudo apt-get` step (a system change), the Chromium download (into the user's folders; the package tests' browser test needs Chromium already), the actions, and `CI` (with it the black box would show its window on the developer's desktop).
- **Runs:** `Spar.PackageTests` 42 of 42 on Windows (5 min 27 s) and 42 on Linux in WSL (40 passed, the 2 Windows-only skipped; 3 min 28 s), `WorkflowTests` 4 of 4 on each; the workflow's steps from packages, 9 on Windows (the NativeAOT lane 24.4 s, the others 1.3–7.8 s) and 7 on Linux.
- **Not verified:** the workflow on GitHub; `pwsh` with `--with-deps` on the Ubuntu runner; the end-to-end tests from packages on GitHub's Windows runner.

## M1d exit's budgets and the CI workflow (user decisions, 2026-11-24)

Asked after the exit's measurements (implementation records "M1d budgets measured at the exit (implementation record; 2026-11-24)" and "The template's CI workflow (implementation record; choices for review; 2026-11-24)"), each the recommended option:
- **The Hot Reload budget (≤ 4 s) is read on a new app from packages,** the shape app authors have: its first edit of a `dotnet watch` session applied in 0.85 s (0.84–0.86, n = 3) on Windows, met. The Notebook's figure, built in source mode with Spar's own projects in its watch workspace, is recorded beside it, not as the budget's measure: its first edit 4.70 s on Windows when saved about 2 s after `dotnet watch` was ready (1.1 s when saved 8 s in), 2.93 s on Linux; later edits about 0.1 s on both. Not taken: reading it on the Notebook after `dotnet watch`'s start-up work, or recording a miss and tracing the 3.7 s wait before M1d closes.
- **The browser test (≤ 250 ms) is recorded as met on Windows, with this machine's slowdown as the cause:** 247.8 and 250.3 ms, the M1a commit measuring 244–250 ms on the same PC today (122–137 ms at M1a); command start and the loopback round trip doubled there too, Windows only. The budget is unchanged; the PC's slowdown is looked at separately, and M4 replaces the provisional budgets with measured ones. Not taken: treating it as a miss and finding the cause before M1d closes.
- **The template's CI workflow is accepted as built,** its twelve choices included: every new app gets it, one job matrix over the runners `--platforms` names, no Linux window or black-box tests on GitHub's Ubuntu runner (proposed for M4 with a published image), the fast tests' NativeAOT lane, and the repository's own action pins checked by a test. Not taken: a template option that leaves the workflow out.

## The IDE checks at the M1d exit (results of the user's checks, run by the agent; 2026-11-24)

The plan's rows "F5 of the Notebook's `Desktop` profile in Visual Studio", "… in VS Code with C# Dev Kit" and "F5 of a new app from the template", from `docs/evidence/m1d-exit/user-checklist.md`, checks 1–3. The user asked the agent to run them through computer use while the monitor was not in use; Visual Studio 18 Community and VS Code with C# Dev Kit 3.40.215 were driven by clicks only (the IDE tier allows no typing), so code edits were made on disk, or in VS Code by Edit > Undo of a change made on disk, then File > Save. The apps' own windows could not be seen (their access was denied), so a window's page was checked through the app log and the page itself through the Loopback profile in the agent's browser. With the user's agreement the agent trusted the two folders in VS Code (Workspace Trust) and set `csharp.experimental.debug.hotReload` in a VS Code user settings file it created and removed afterwards. Edits were undone; the repository was left clean.
- **Passed, Visual Studio, the Notebook:** F5 of `Desktop` built (Spar's steps under Visual Studio's MSBuild) and reached `ui-ready`; a breakpoint in `PagesService.ListAsync` hit; a body edit of `MarkdownPageStore.ListAsync` was applied from the Hot Reload button ("Hot Reload applied revision 1 (Notebook.Pages.MarkdownPageStore)" sent to the page); adding `string? Excerpt = null` to `PageSummary` was refused ("Renaming enum value 'Shape_…' requires restarting the application") and "Restart affected projects" rebuilt with the regenerated client, which then needed the page's test fixtures to gain `excerpt: null` (below), after which the window came back; the `Loopback` profile served the page, a page load hit the breakpoint, 30 s later Continue let it finish, and stopping debugging ended the app.
- **Passed, Visual Studio, a new app:** `Notes.slnx` loaded its three projects with 0 errors, warnings and messages and `Desktop` as the first profile; F5 built and reached `ui-ready`; Greet answered "Hello, Ada!" (through Loopback) and a breakpoint in `GreetAsync` hit with the name; Test Explorer listed the five tests with their `Surface` traits and ran the two fast ones, 2 passed.
- **Passed, VS Code, both apps:** "C#: Notebook [Desktop]" and "C#: Notes [Desktop]" reached `ui-ready`; breakpoints in `ListAsync` and `GreetAsync` hit through the Loopback configurations ("Hello, Grace!"); the loopback address is printed in the Debug Console and an open page of the same dev session reloads by itself; the Testing view ran the fast tests, 2 of 2.
- **Found:**
  1. **A breakpoint in code the page calls right after `backend-updated` stalls Visual Studio's apply.** The page fetched its list on the update and hit the breakpoint while Visual Studio still showed "Applying Code Changes…"; Continue stayed disabled, "Stop Applying Code Changes" did not end it, the Watch window read "Busy…", and after stopping the session Hot Reload was unavailable in Visual Studio until it was restarted. The host sends the event from the update handler; sending it after the handler has returned may avoid it (not tried).
  2. **VS Code's Hot Reload applied nothing,** in the Notebook (source mode) and in the new app from packages alike: its ".NET Hot Reload" output says "Source '<E:\…\X.cs>' doesn't match output PDB: no document" for each source, then "No code changes were found" on each save. The PDB written by VS Code's own pre-launch build records the paths with a lowercase drive letter (`e:\…`, `c:\…`); after a command-line build it had `E:\…`, but VS Code's next launch rebuilt it in lowercase. Not run: a plain .NET app as a control, so whether this is Spar's or C# Dev Kit's is not known. The contract edit's refusal in VS Code (its step 4) was therefore not seen.
  3. **Visual Studio picks `Notes.EndToEndTests` as a new app's startup project** (no `.vs` folder before), not `Notes`, though `Notes` is first in the `.slnx`.
  4. **Visual Studio's Test Explorer leaves an empty `TestResults/`** in the new app's root (created at its first run). Its first run also aborted with "build failures" and no error shown; the second passed and the command-line build was clean.
  5. **VS Code lists the end-to-end tests only after its prompt "Test assembly not found … Build the project to discover tests?",** and shows them by class; the `Surface` traits do not appear as tags.
  6. **CA1822 is reported as a message on the template's `GreetAsync`** ("does not access instance data and can be marked as static"), which a contract command cannot be.
  7. **In source mode, Visual Studio stops on an `OperationCanceledException` in `LoopbackHost.ProxyAsync`** (`response.Body.WriteAsync(…, context.RequestAborted)`) when the browser cancels a proxied request; Spar's projects are user code there. An app from packages would not stop on it (not checked).
  8. **The checklist was wrong in two places:** a loopback run that starts a new dev session asks the default browser to open the page (decision "Loopback in the development loop"), and the `PageSummary` edit also needs the page's test fixtures updated, since responses write nullable properties explicitly.
- **Not verified:** the windows themselves (the toast, the edited titles, no hung-page dialog 30 s after a breakpoint in the Desktop profile), VS Code's Hot Reload (item 2), checks 4 (`spar init frontend` in a real terminal) and 5 (the start-up page on an on-screen window), which stay the user's.

## CA1822 on contract commands (user decision, 2026-11-24)

Finding 6 of "The IDE checks at the M1d exit": CA1822 ("Member does not access instance data and can be marked as static") is reported on the template's `GreetAsync`, and on any command of a contract class that uses no instance data. A static `[SparCommand]` method is not a command (`ContractAnalysis.IsCommand` skips it without a diagnostic), so following the advice removes the command from the page. Asked how Spar should handle it (a suppressor in Spar's analyzer, changing the template's command to use instance data, or leaving it), the user chose **a suppressor**: Spar's analyzer assembly hides CA1822 on `[SparCommand]` methods of `[SparContract]` classes only, in every app, recorded here with its reason. It is a `DiagnosticSuppressor`, so it changes no severity in `.editorconfig` and needs no `#pragma`; the contract class's other methods keep the diagnostic.

## The IDE findings after the M1d exit (implementation record; 2026-11-24)

At the user's request the agent worked through the eight findings of "The IDE checks at the M1d exit". The user said the monitor was not in use, and the agent drove VS Code and Visual Studio again by clicks.
- **Finding 2, VS Code's Hot Reload, is C# Dev Kit's, not Spar's.** A plain console app fails the same way in VS Code 1.147 with C# Dev Kit 3.40.215: the ".NET Hot Reload" output says "Source 'E:\…\Program.cs' doesn't match output PDB: no document", then "No code changes were found", and the app keeps running the old code. The app is `dotnet new console` with empty `Directory.Build.props` and `.targets` of its own, kept in `artifacts/m1d-exit/hr-control`.
  - **The cause:** VS Code's build is given the project with the drive letter in lower case, and the compiler records the sources as it got them (`dotnet build e:\…\x.csproj` writes `e:\…` into the PDB for any project). Roslyn's Hot Reload then looks the open document up in the PDB by its exact path, which has `E:\` (`CommittedSolution.TryReadSourceFileChecksumFromPdb`, the "no document" message).
  - **The setting:** `csharp.experimental.debug.hotReload` is not honoured in a folder's `.vscode/settings.json` (no Hot Reload button appears). The agent set it in a user settings file it created and removed again.
  - **A possible fix:** a `PathMap` from the lower-case drive to the upper-case one (`e:\=E:\`) makes such a build record `E:\…`, checked from the command line. This matches the research note under "Tooling: from earlier versions, not rechecked" in `docs/pitfalls.md`. Whether VS Code then applies edits is not checked yet. Spar does not set it until that check passes and the user agrees, since it would be a default in every app's Development build.
- **Finding 3, the startup project, is fixed in the files.** `dotnet sln list` shows the cause: the solution's projects sorted by path, where `Notes.EndToEndTests/` comes before `Notes/` ('.' sorts before '/'), and Visual Studio starts the first. The template's `.slnx` and the repository's `Spar.slnx` now mark the app `DefaultStartup="true"`, the `.slnx` attribute for the startup project of a solution opened for the first time. The SDK's parser accepts it (`dotnet sln list` and the builds). Not checked in Visual Studio yet.
- **Finding 4, the empty `TestResults/`, is ignored.** Visual Studio's Test Explorer creates the folder beside the solution for its own runs, outside the `--results-directory` that Spar passes to `dotnet test`. The template's `.gitignore` now lists `/TestResults/`, as the repository's already did. The first run's "build failures" was not seen again (not repeated).
- **Finding 5, VS Code's test discovery and traits,** is C# Dev Kit's behaviour: it lists a test project's tests once the project is built, and does not show traits as tags. Not changed.
- **Finding 6, CA1822, has a suppressor** (user decision "CA1822 on contract commands"). `Spar.Generator` ships `CommandStaticSuppressor`, whose suppression id is `SPARSUP001`. `Spar.Generator.Tests` checks it against the SDK's own `MarkMembersAsStaticAnalyzer`, referenced from the SDK's analyzers folder: CA1822 is suppressed on a contract class's command and still reported on the class's other method and on a `[SparCommand]` method outside a contract. Not checked in either IDE yet.
- **Finding 7, the stop on the proxy's `OperationCanceledException`, is fixed.** The loopback host's request handler now ends a request the browser abandoned without letting the exception leave Spar's code (`catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)`, around all its routes). This covers the asset, bridge and report routes as well as the proxy. Not checked under Visual Studio yet.
- **Finding 1, Visual Studio's stalled apply: the guessed cause was wrong.** The hosts already hand each update on: the Windows and Linux bridges post it to their UI thread, and loopback queues it on each document's channel. So `backend-updated` already leaves after the update handler has returned, and "send it after the handler returns" changes nothing.
  - **The repeat was cut short.** A click in Visual Studio's breakpoint margin opened ReSharper's quick-actions menu, which then took every click and ignored the close request, so the agent ended the instance it had started (no edits were open). On the second attempt the agent's screen tool stopped answering, so the Notebook was started under the debugger and nothing more was seen.
  - **Open:** whether the stall needs a page call to hit a breakpoint during Visual Studio's apply, and whether delaying the event would avoid it.
- **Finding 8, the checklist,** was corrected in the exit commit.
- **Not verified:** everything marked "not checked" above, in the IDEs; VS Code's Hot Reload with the `PathMap`; the user's checks 4 and 5.

## VS Code's Hot Reload with a PathMap, and the startup project (check results; 2026-11-24)

Follow-up checks to "The IDE findings after the M1d exit". The agent ran them through clicks once its screen tool answered again: the agent ended its stuck Visual Studio instance and that instance's Notebook, and the tool came back.
- **The `PathMap` fixes VS Code's Hot Reload.**
  - **The plain console app:** with the drive letter mapped (`<PathMap>$(PathMap),e:\=E:\</PathMap>`, written generically from `$(MSBuildProjectDirectory)`), VS Code's own build recorded `E:\…` in the PDB. A body edit saved in the editor was applied at once: the running app went from "tick 14: original" to "tick 15: edited".
  - **A Spar app:** the same property in the checklist's new app from packages (`%TEMP%\spar-guide-m1d6b\work\Notes`, added and then removed). "C#: Notes [Desktop]" reached `ui-ready`, and the body edit of `GreetAsync` was applied on save, with the app logging "Hot Reload applied revision 1 (Notes.Greetings.GreetingService); sent to document …".
  - **The contract edit:** adding `string? Note = null` to `Greeting` was refused ("1 project(s) require a full restart", ENC0020 on the generated code). "Rebuild and Apply Changes" rebuilt, and the build stopped on the page test `greet.test.ts`, whose `Greeting` fixture lacked `note` (TS2741). This is the expected result of a response-type edit, as in finding 8.
  - **Not done:** Spar does not set the `PathMap` yet; that waits for the user's agreement, since it would be a default in every app's Development build on Windows. The toast itself was not seen, because the window was not visible to the agent.
- **`DefaultStartup` works in Visual Studio.** The checklist's new app had its `.slnx` given the template's new attribute and its `.vs` folder set aside, then was restored afterwards. Visual Studio opened it with `Notes` as the startup project.
- **VS Code restored packages into the user's NuGet folder** (`%USERPROFILE%\.nuget\packages\spar.sdk\0.1.0-dev.guide…`), because it was started without the checklist's optional variables, as the checklist warns.
- **Still not checked:** under Visual Studio, Loopback in source mode no longer stopping on a cancelled proxy request; CA1822 hidden on `GreetAsync` in either IDE (it needs freshly packed packages); a repeat of the stalled apply (finding 1).

## The drive-letter PathMap in Debug builds (user decision, 2026-11-24)

Asked whether Spar should map the drive letter for VS Code's Hot Reload ("VS Code's Hot Reload with a PathMap, and the startup project"), the user said **add it**. `Spar.Sdk` now sets the map in every project that uses it, apps and their test projects alike, when all of these hold:
- the configuration is `Debug`, so Release and Production builds are untouched;
- the build runs on Windows, and the project's folder starts with a drive letter (not a UNC path);
- the build has no deterministic source paths (`DeterministicSourcePaths`, on in `ContinuousIntegrationBuild` builds), whose own map from the source roots stays as it is;
- the project has not set `SparMapDriveLetter=false`, the way to turn it off.

The map is the project's drive in lower case to the same drive in upper case (`e:\=E:\`), appended to any `PathMap` the project already has. Built from an upper-case path it changes nothing, so Visual Studio and command-line builds record the same paths as before. Only the project's own drive is mapped: a source on another drive keeps the path it was given. The property follows the `Microsoft.NET.Sdk` import in `Sdk.targets`, because `DeterministicSourcePaths` gets its default there. `CallerFilePath` values in Debug builds are mapped the same way.

Checked:
- `dotnet msbuild -getProperty:PathMap` on the Notebook through `e:/…` gives `e:\=E:\`; with `-p:Configuration=Release`, or `-p:ContinuousIntegrationBuild=true`, it is empty.
- `Spar.PackageTests` builds a new app from the packed SDK through its project path with the drive in lower case. Its PDB names `Program.cs` by the path with the upper-case drive, and no document starts with a lower-case one; on Linux the same test checks only `Program.cs`.

Not checked again in VS Code: that Hot Reload applies edits in a new app made from the packed template with no map added by hand. The agent saw it with the same map added by hand ("VS Code's Hot Reload with a PathMap, and the startup project"); the plan's table "Checks that need the user at the PC" keeps the row.

## The checks at the PC at the M1d exit (check results; 2026-11-24)

The user ran the M1d rows of "Checks that need the user at the PC", guided by the agent step by step from `docs/evidence/m1d-exit/user-checklist.md`. The agent first rebuilt everything the checklist uses from `9107661` (`prepare-user-checks.sh`, 72 s: packages `0.1.0-dev.guide20261124212346`, the Notes, Init and Theme apps, the Notebook's Production executable), since the earlier preparation predated the IDE fixes and the drive-letter map. Results as the user reported them, with the app logs and a dump read by the agent:
- **`spar init frontend` in a real terminal: passed.** The 18 new files and the two diffs, then "Write these changes? [y/N]"; Enter gave "Nothing written.", exit code 1, no `frontend`; `y` wrote it and ran npm, exit 0, then `dotnet build` succeeded; `dotnet spar doctor` printed the report with its `frontend-type-check` warning. It also warned that the user's default Node was 20.20.2; with Node 24 selected the warning went away.
- **The start-up page on an on-screen window: passed.** The Notebook's Production executable, started twice from Explorer: no flash, no splash. The Theme app: the circle faded in and out around its held first render; dark, light and system switched window, title bar and page together; started again after dark, it was dark from its first frame. Not done: the other Windows theme (optional, it changes the user's setting), a slow first start after a reboot, the Linux desktop with a GPU.
- **Visual Studio, the Notebook: passed, and finding 1 explained.** The body edit of `MarkdownPageStore.ListAsync` was applied, with the toast. With the breakpoint of the checklist still set in `PagesService.ListAsync`, Visual Studio then showed the app as running (Continue and the step buttons greyed, Pause and Stop enabled) with the breakpoint's line still highlighted, and neither F5 nor Pause did anything; the window did not respond. The app's log has "Hot Reload applied revision 1 … sent to document …" and then nothing for 8 minutes, not even the heartbeat's alarm: the whole process was stopped. The Notebook's page refreshes its list on `backend-updated` (`onBackendUpdate(() => void refresh())`), so its `ListAsync` call hit the breakpoint while Visual Studio was still applying the update, and Visual Studio lost track of the stop. Stop Debugging ended it. That Visual Studio's Hot Reload stayed unavailable (the button greyed with a new edit) until both instances were closed; the earlier one had no window left and the agent ended it at the user's request. After a restart, with the breakpoint disabled, the next edit was applied, with the toast, and the window kept working. This is Visual Studio's, not Spar's: the update and its event are as designed. Loopback in source mode no longer stopped on the cancelled request.
- **Visual Studio, the new app: passed.** No CA1822 on `GreetAsync`; F5 greeted the typed name. ReSharper reports `GreetingService` and the Notebook's `ListAsync` as "never used", since only the generated dispatch reaches them.
- **VS Code, the new app: passed, after two snags.**
  - **The first run hung.** The window stayed blank; Vite had begun with "Re-optimizing dependencies because vite config has changed" (the earlier Visual Studio run had started it through `C:\`, VS Code through `c:\`). The process did not respond, Pause did nothing, two threads each used a full core and 21 were suspended. A full dump (`dotnet-dump collect`, 164 MB, kept in `artifacts/m1d-exit/hang/`, not committed) shows the debugger's suspension half done: the two busy threads, entering managed code from I/O completions (`ThreadPoolBoundHandle.OnNativeIOCompleted`, and one with no managed frame), in cooperative mode with "Debug Suspend Pending, Debug Will Sync"; Spar's UI thread idle in `GetMessage`, the main thread in `Thread.Join`. Runtime 11.0.26.53018. Nothing of Spar's is on the busy threads. Stop and a new start loaded the page normally; not repeated since.
  - **Hot Reload applied nothing at first,** because the agent's command opened the folder through `$env:TEMP`, which on this machine is the 8.3 short path (`C:\Users\<USER>~1\…`), while the build records the long one. Opened through the long path, the body edit was applied on save with the toast, and the greeting changed. The run's PDB names every source with an upper-case `C:\`, so the drive-letter map did its part.
  - **The contract edit** (`string? Note = null` on `Greeting`) was refused: "Hot Reload applied changes to 1 project(s), but 1 project(s) require a full restart", offering "Rebuild and Apply Changes".
- **Not run by the user:** the Notebook under VS Code, and the 30 s after a breakpoint in the Notebook's own window (the table keeps both); the Windows Sandbox and other milestones' rows.
- **Follow-ups agreed with the user, to build next:** the build's type-check following project references (`tsc -b`), so a Vite scaffold's `tsconfig.json` is checked; `spar doctor` printing only what needs attention by default, with `--verbose` for every check; the dev-server state file probed before it is read, so the debugger shows no first-chance `FileNotFoundException` while Vite starts; `[SparContract]` and `[SparCommand]` marked as meaning implicit use for ReSharper and Rider. Proposed by the agent during the checks: starting Vite with the drive letter in upper case, so switching IDEs does not make it re-optimize.

## The M1d check follow-ups (implementation record; choices for review; 2026-11-25)

The five follow-ups of "The checks at the PC at the M1d exit", built at the user's request ("both after the checks", then "add it to the list", then "Yes, start"). The shapes in detail are the agent's, for review.
1. **The type-check follows project references.** `SparFrontendTypeCheck` reads the frontend's `tsconfig.json` and, when it has a non-empty `"references"` array (a regular expression over the text, `"references"\s*:\s*\[\s*\{`, inside the target, so evaluation reads no file), runs `tsc -b --noEmit`; otherwise `tsc --noEmit -p .` as before.
   - **Checked by hand on Vite's scaffold** (create-vite 9.3.4 `react-ts`, TypeScript 7.0.2): with `export const broken: number = "text"` in `src/`, `-p .` exited 0 and `-b --noEmit` exited 1 with TS2322 at the file and line. Under TypeScript 6.0.3 (the SDK's own) `-b --noEmit` also reported it.
   - **Why not build mode always:** on a config without references it wrote `tsconfig.tsbuildinfo` beside the config, into the app's authored folder, under both versions. With references, the referenced configs' `tsBuildInfoFile` decides where the state goes (Vite's scaffold: `node_modules/.tmp/`); a config without one gets its `.tsbuildinfo` beside it, as `npm run build` (`tsc -b`) already writes.
   - **Not taken:** a compiled task parsing the JSON, as doctor does. The regular expression can be fooled by a commented-out reference, which then runs build mode on a config whose own options apply; doctor parses the JSON with comments skipped.
   - **Doctor:** `frontend-type-check` is now information ("tsconfig.json references other configs, so the build type-checks them in build mode (tsc -b --noEmit).") for such a config, and absent otherwise; it is no longer a warning. Its id stays.
   - **Tests:** `Spar.Tool.Tests` expects the information; `Spar.PackageTests` (`Init_frontend_wires_…`) adds a type error to the scaffolded page, expects the build to fail with `probe.ts(1,14): error TS2322`, removes it and builds again.
2. **`spar doctor` prints what needs attention.**
   - **The default text output:** the header line; `Project: ` with the project finding's message (which project, or why none was found); then, under their areas, only warnings and errors with their fixes; then one line such as `Nothing to fix; 18 checks passed (spar doctor --verbose lists them).` or `0 errors, 1 warning; 17 checks passed (spar doctor --verbose lists them).`
   - **`--verbose` (`-v`)** prints the earlier report, every finding by area.
   - **`--json`** is unchanged and still holds every finding, so agents lose nothing. Exit codes are unchanged.
   - On the Notebook the default output is three lines.
   - The getting-started guide and the agents design say so.
3. **No first-chance exception while Vite starts.** `DevServerState.Read` asks `File.Exists` before opening `obj/spar-dev-server.json`, which is missing on every poll until Vite writes it. The `IOException` catch stays for the races. Visual Studio's Output window had shown a `FileNotFoundException` per poll.
4. **Contracts and commands count as used in ReSharper and Rider.**
   - **The annotations:** `Spar.dll` declares, internal, `JetBrains.Annotations.MeansImplicitUseAttribute` with its two flag enums, in JetBrains' published shape, which those tools read by full name.
   - **Where they go:** `[SparContract]` carries it with `InstantiatedNoFixedConstructorSignature`, since a contract class is created from dependency injection. `[SparCommand]` carries it with `Access`.
   - **Not taken:** `WithMembers` on `[SparContract]`, which would also hide a contract class's unused helpers.
   - **Not checked yet:** what ReSharper shows (a user check), and whether an interface contract's methods without `[SparCommand]` need more than this. `Spar.Tests` checks the attribute data.
5. **Vite starts with the drive letter in upper case.** `DevServerSettings` upper-cases the drive letter of the frontend and `obj` folders on Windows, so `npm run dev` gets the same working directory from Visual Studio and from VS Code. VS Code's lower-case `c:\` had made Vite re-optimize its dependencies ("vite config has changed") on each switch between the IDEs. That re-optimization was under way when VS Code's run hung, but the dump showed no link between the two. Tested in `Spar.Tests`; not yet run in an IDE.
- **Tests:**
  - **Windows:** the solution passed (1,440 tests: 1,327 passed, 113 skipped). The package tests passed 41 of 42 in the full run. The template's black-box test failed once: its published app did not show "Type a name first." within 10 s, with no IDE or other app running. It passed when run alone. Nothing changed here reaches a Production app's page; this is the first time this timeout has been recorded.
  - **WSL** (Ubuntu 24.04, Xvfb): the solution passed (1,242 tests: 1,201 passed, 41 skipped), and the package tests passed (40, 2 skipped as on every Linux run).

## The M1d follow-ups checked without the IDEs (check results; 2026-11-25)

- **CI:** every job passed on `952ec9f` (both OSes: tests, build tests, package tests, AOT; the Linux container's window and black-box tests).
- **ReSharper's view, through JetBrains' command-line InspectCode** (`JetBrains.ReSharper.GlobalTools` 2026.3.0.2, installed under `artifacts/resharper-cli/`, solution-wide analysis on). The Notebook could not tell anything apart: its `Program.cs` and the generated dispatch reach `PagesService` with or without the annotations. So the check used a scratch project outside the repository, with only `Spar.dll` as a plain reference and no generator:

  | Class in the scratch project | What InspectCode reported |
  |---|---|
  | `[SparContract]` class with a `[SparCommand]` method, nothing calls either | nothing |
  | the same class shape under an attribute of its own without the annotation (the control) | `UnusedType.Global` for the class, `UnusedMember.Global` for the method |
  | a `[SparContract]` class holding an unused field, an unused public method without `[SparCommand]` and an unused private method | all three still reported |
  | a `[SparContract]` interface whose method has no `[SparCommand]` | `UnusedMember.Global` for the method |
  | the alternative not taken, `WithMembers`, on an attribute of the scratch project's own | the interface's method no longer reported, and the class's three unused members not reported either |

  So the annotation does what decision "The M1d check follow-ups" meant, and the template, whose one contract is a class, shows no "never used". An interface contract's methods are still reported unless each carries `[SparCommand]`; whether Spar should do more is open for the user. Not run: ReSharper inside Visual Studio, which uses the same engine.
- **Vite's cache and the drive letter.** `npm run dev` started four times in the Notebook's frontend, Vite 8.3.8 under Node 24.21.0: `E:` then `E:` kept the dependency cache's config hash (`76e80791`) and did not re-optimize; `e:` changed it (`30bf6088`), with "Re-optimizing dependencies because vite config has changed"; `E:` again changed it back and re-optimized. Spar now always starts Vite from the upper-case spelling (`Spar.Tests`, `On_Windows_the_folders_get_an_upper_case_drive_letter`), so an app started from either IDE keeps one hash. Not run: the switch between the two IDEs itself.

## Interface contracts keep the narrow annotation (user decision, 2026-11-25)

- **Decided:** `[SparContract]` keeps marking only the type as used, so ReSharper and Rider still report unused fields and helpers inside a contract class (decision "The M1d follow-ups checked without the IDEs"). An interface contract's method counts as used only with `[SparCommand]`, and that is documented instead of marking every member: `docs/app-model.md` section 3.3 and the remarks on `SparCommandAttribute`.
- **Not taken:** `WithMembers` on `[SparContract]`, which covers interface methods but hides a contract class's unused members too.

## The IDE and theme checks after the M1d follow-ups (check results; 2026-11-25)

The user ran these at the PC, guided, on the Notebook (`Desktop` profile); the agent read Vite's files between the runs.

- **VS Code:** F5, then a breakpoint in `PagesService.ListAsync` hit by reloading the window (F5 in the page), 30 s, Continue: no hung-page dialog, the window kept working. Then, with the breakpoint removed, a method body edit (`MarkdownPageStore.ListAsync`'s title) applied by Hot Reload: the toast showed and the list showed the new titles. The second VS Code installation the user opened first had Hot Reload off (`csharp.experimental.debug.hotReload`), so the edit applied only after a restart with it on. No hang this time.
- **The switch to Visual Studio:** Vite ended with VS Code's run (its process gone, `obj/spar-dev-server.json` removed). Visual Studio's F5 started a new one from `E:\Ghidello\Git\spar.v7\samples\Notebook\frontend`; the dependency cache's config hash stayed `4ee0c3ef`, as after VS Code's run, and Vite's log has no "Re-optimizing". So decision "The M1d check follow-ups" item 5 holds in the IDEs.
- **Visual Studio:** the same breakpoint, 30 s, Continue: no hung-page dialog, the window kept working. The breakpoint was hit twice per reload: the Notebook's page runs under React's `StrictMode`, which in development mounts each component twice, so the effect that lists the pages runs twice; a production bundle mounts once.
- **The other Windows theme:** with Windows in light mode, the Notebook started light from the first frame, with no flash.
- **Left in the plan's table:** the splash mark on a slow cold start (the first start after a reboot), and WebKitGTK's animation on a real Linux desktop with a GPU.

## The splash on WSLg's display; a GPU needs a real Linux machine (check results; 2026-11-25)

The user allowed WSLg windows on their desktop for the plan's row "a real Linux desktop with a GPU". The new `windowsession` step of `docs/evidence/m1c-blackbox/wsl.sh` runs the window tests with `SPAR_TEST_DISPLAY=session` on WSLg's `DISPLAY=:0` (its XWayland under the WSLg compositor) instead of a private Xvfb, with a watcher that reports whether a WebKit process mapped Mesa's D3D12 driver.

- **No GPU reached WebKit.** The web process mapped `libgallium-25.2.8`, `libEGL_mesa`, `libGLX`, `libgbm` and `libvulkan`, but not `libd3d12` or `libdxcore`. Spar's software-GPU check found no render node the user can open (WSL's `/dev/dri/renderD128` belongs to the `render` group, which the user is not in, and its driver is the kernel's virtual `faux` bus, most likely `vgem`, which the check counts as software (not confirmed with `drmGetVersion`); the GPU is behind `/dev/dxg`) and set `WEBKIT_DISABLE_DMABUF_RENDERER=1` and `GSK_RENDERER=cairo`, as decided in M1c. Turning that off would not help: WebKit's sandbox blocks `/dev/dxg`, and with Mesa's D3D12 driver forced the web process cannot create its EGL display and crashes (pitfall "WSLg without `render` group access uses llvmpipe"). **So the GPU question needs a real Linux machine with a native driver;** the plan's row keeps it.
- **The splash on WSLg's compositor, in software:** `StartupFrameTests`, 22 starts × 5 runs, 22 of 22 passed in 228 s ([`frames-webkitgtk-wslg.txt`](evidence/m1d-page-startup/frames-webkitgtk-wslg.txt)). Every run: 0 frames of another colour, the mark at most once. The same starts showed it as under Xvfb on 2026-11-22 (`frames-webkitgtk-final.txt`), on screen 512–523 ms or 1,215–1,335 ms, with one run at 498 ms (`bundle +200 ms, 600 ms of work in a later task`, run 4): above the 400 ms minimum, 14 ms under the 512 ms that every Xvfb run kept. So WebKit drew the mark only in the page's own frames there too, and the SDK's rule held.
- **A first run by mistake:** the step was not yet among the script's steps that take xUnit options, so the filter was read as step names and all 220 window tests ran on WSLg's display (5 min 32 s): 166 passed, 50 skipped, 4 failed, each through XTEST input on that display (a click that "did not reach the page" twice, typed emoji ending after "o", "€" off the US keyboard). They pass on the private Xvfb in every run. The input reached only the test's windows on WSLg's X server. The script now passes options to `windowsession`.

## The Linux GPU check does not block M1d (user decision, 2026-11-25)

- **Decided:** the plan's row for WebKitGTK's splash on a real Linux machine with a GPU stays open, and M1d merges without it. No such machine is at hand, and WSL cannot give WebKit a GPU (decision "The splash on WSLg's display; a GPU needs a real Linux machine"). Until then the SDK's rule is measured in software only, where it held on Xvfb and on WSLg's display.
- **Not taken now:** booting Linux from a USB stick on the user's PC (Intel UHD 770, a native driver), which stays possible later.

## The cold start after a reboot (check results; 2026-11-25)

- **What ran:** the user restarted the PC (booted 21:28:14 UTC) and, before anything else, started the Production NativeAOT Notebook published from `e3f141f` (5,280,256 B) from Explorer, 2 min 27 s after the boot.
- **Seen:** no splash mark and no flash. **The app's log:** the host started 41.3 ms after the process, the first document at 255.6 ms, `ui-ready` at 328.7 ms (WebView2 Runtime 158.0.4370.33). Under the splash's 350 ms entrance delay, so no mark was the expected result.
- **So a slow cold start does not happen on this machine,** and the mark on a slow start rests on the frame tests (WebView2 and WebKitGTK, every run with the mark at most once and no wrong frame) and on the user's check of the splash around a held first render (decision "The checks at the PC at the M1d exit"). The item leaves the plan's table; the row keeps only the Linux GPU check.
- **CI:** every job passed on `48cb706`.

## M2's decide-first questions (user decisions, 2026-11-25)

Asked one at a time after M1 merged; the user took the agent's recommendation on each. The plan's M2 section lists them with what was not taken.

1. **Five slices, each with its own exit and merge:** M2a the devtools core and its first surface; M2b `spar dev`; M2c `spar snap` and scenes; M2d the optional OpenTelemetry layer, client telemetry and Aspire; M2e the agent guide, the app-author plugin and the evaluation. Not taken: three slices, or one branch.
2. **One devtools core in C#, with the `spar` CLI and an MCP server as thin adapters over the same commands,** so the evaluation compares them on equal terms; the surface that loses is deleted. Not taken: the CLI only first, or MCP only.
3. **Tools reach the running app through a local socket with an owner-only ACL:** a named pipe on Windows, a Unix socket in the app's runtime folder on Linux, found through the session lockfile with its bearer token. No port, so no page in a browser can reach it. Not taken: HTTP on loopback with the `Host` check, or a relay the app connects out to.
4. **Page input from the devtools on the developer's own desktop:** in-process CDP `Input.*` on Windows (trusted), the injected DOM script on Linux with each result marked synthetic. XTEST stays in tests: on a real desktop it moves the user's own pointer, and Wayland has none. Not taken: the DOM script on both.
5. **`spar dev` is Spar's own supervisor:** it builds, starts the app with the development server kept across restarts, and on `reload` builds again and returns the compiler diagnostics at file and line in-band, or restarts the app. Every process it starts goes in a job object (Windows) or a process group (Linux) from creation, with a closed stdin. Hot Reload stays with `dotnet watch` and the IDEs. Not taken: driving `dotnet watch`, or both modes.
6. **Scenes live in the app's test project,** as C# beside the tests, run by `spar snap` through the test host (the app's own `Program.cs` with service overrides, the safety defaults and a fixed `TimeProvider`), so the app's own builds carry no scene code. Not taken: Development-only code in the app, or data files.
7. **Aspire: a documented recipe first** (an AppHost that starts the Spar app as an executable resource with the dashboard's OTLP variables); a hosting package only if the recipe needs more than a few lines. Not taken: a `Spar.Aspire.Hosting` package from the start.
8. **The evaluation:** Claude Code headless, 10 tasks on the Notebook and a new app, 3 profiles (plain `dotnet`, the `spar` CLI, the `spar` MCP server), 3 runs each (90 runs), reporting pass rate, tokens and time. Not taken: 5 × 3 × 2, or 15 × 3 × 5.
9. **`spikes/` goes early in M2, as its own change:** each spike's `RESULTS.md` and `results/` move to `docs/evidence/`, every citation (34 files under `src/`, `tests/`, `eng/` and `samples/`, 17 in the design records and skills) points there, a check confirms no build or test depends on a spike, then the folder is deleted. Not taken: leaving it to M4.

## `spikes/` deleted (implementation record; 2026-11-25)

M2's decide-first question 9 (decision "M2's decide-first questions"), as M2's first change.

- **Moved:** the 64 remaining spikes' `RESULTS.md` and `results/` (2,688 files, 4.7 MB of them results) to `docs/evidence/<name>/`, with `git mv`, so their history follows. `app-model-check` had moved earlier. Two helpers that the M1b evidence script `docs/evidence/m1b-host-details/sandbox.sh` runs (`find-client.ps1`, `close-client.ps1`) moved beside it.
- **Deleted:** the rest of `spikes/` (2,582 tracked files: the spikes' code and projects) and its untracked build output. The code is in git history at commit `24f5db0`, the last one with `spikes/`. Each moved record now starts with a note linking its folder there on GitHub.
- **Citations:** 486 citations of `spikes/<name>`, as a whole spike or its record, now name `docs/evidence/<name>`: in the plan, the design files, the pitfalls, the research review, `docs/app-model.md`, the evidence pages, the skills, `.editorconfig` and 34 code comments under `src/` and `tests/`.
  - **Left as they were:** the records' own text, which says how each spike was run; and the 15 paths into a spike's code in the design records (for example `spikes/d1-pipeline/Spike.Runtime/SparCalls.cs`), which name that history.
  - **In this log,** which is append-only, only the 9 Markdown link targets changed, from `../spikes/<name>/RESULTS.md` to `evidence/<name>/RESULTS.md`. Every other `spikes/<name>` in earlier entries means `docs/evidence/<name>/` for the record, and the commit above for the code.
  - The one link from a record into a spike's code (`sandbox-signout`'s `system-restart.sh`) points at that commit on GitHub.
- **Kept:** the `spikes/` conventions for new spikes (the build's and `eng/check-size.cs`'s exclusions, `Spar.BuildTests`' copy, `AGENTS.md`'s layout row, now saying the folder is empty).
- **Checks:** 255 relative Markdown links in the tracked files, 0 broken; Windows: `dotnet build Spar.slnx` with 0 warnings, `eng/check-size.cs` clean (644 files), `AppModel.Tests` 11 of 11, `Spar.Tests` 788 (781 passed, 7 skipped); WSL: the Linux copy synced and `dotnet build Spar.slnx` passed. Not run: the window, black-box, build and package suites, which no change here reaches (comments and documents only).

## M2a's decide-first questions (user decisions, 2026-11-25)

The user accepted the agent's eight recommendations together. The plan's M2a section lists them with what was not taken.

1. **Development builds only,** behind a feature switch trimmed from every other variant and checked by the production boundary test; on in every Development run, the owner-only socket and the token being the gate; `SparDevtools=false` in the project leaves it out. Not taken: off unless the run starts it, or also in Test builds.
2. **Discovery through `obj/spar-devtools.json`** in the app's project, owner-only like `obj/spar-loopback.json`: the process id, the run id, the socket's path, the token and the build identity; removed at exit, stale when its process and run are gone. The CLI looks from the current folder or `--project`, as `spar doctor` does. Not taken: the per-user data folder.
3. **Newline-delimited JSON-RPC 2.0 on the socket,** one request per line, ids unique per connection, bounded results, images as base64 PNG inside the result; the MCP adapter of M2e maps onto it. Not taken: HTTP over the socket, or the bridge's encoding.
4. **One page engine:** `PageEngine.js` moves from `Spar.Testing` into the hosts, behind the devtools and test switches, so the devtools find elements exactly as the window tests' locators do. Not taken: a second script.
5. **Elements by reference or locator:** `tree` gives short references (`e12`) valid for the current document, an older one answers `stale_target`, and every action also takes the tests' locators (role and name, label, text, test id). Not taken: one of the two alone.
6. **`call`, `calls`, `logs`:** `call` runs any contract command through the page's own dispatch (its own DI scope, `ISparCall` naming the devtools as caller), with no page needed; `calls` keeps the last 200 with timing, outcome, origin and the UI action behind them; `logs` merges the backend log, the page's console and errors, and CSP violations, tagged by source, in UTC, bounded, with `--since`. Not taken: no `call`.
7. **Flat CLI commands:** `spar tree`, `spar screenshot [--out file]`, `spar click <target>`, `spar fill <target> <text>`, `spar wait <target>`, `spar contracts`, `spar call <contract.command> [json]`, `spar calls`, `spar logs`, `spar version`; text by default, `--json`, `spar doctor`'s exit codes. Not taken: `spar devtools <tool>`.
8. **`screenshot` is the page as PNG** (CDP `Page.captureScreenshot` on Windows, `webkit_web_view_get_snapshot` on Linux), a page-drawn title bar included, nothing else of the desktop. Not taken: the whole window from the OS.

## M2a: the devtools (implementation record; choices for review; 2026-11-26)

M2a's five build steps (plan, "M2a: the devtools core and the CLI"), after decisions "M2's decide-first questions" and "M2a's decide-first questions". Evidence: `docs/evidence/m2a-devtools.md`; user guide: `docs/guide/devtools.md`. The choices below were the agent's, for the user's review.

1. **The engine is embedded by Spar.Sdk into the app's own assembly, not into the hosts.** The decision said the engine "moves into the hosts"; a resource of Spar.dll would ship in every app, Production included, and the production boundary test looks for `__sparAutomation`. So the one source is `src/Spar.Sdk/Sdk/Devtools/PageEngine.js`: Spar.Sdk embeds it (`Spar.Devtools.PageEngine.js`) and the contract snapshot (`Spar.Devtools.Contracts`) in Development builds of apps only, and `Spar.Testing` embeds the same file for the window tests. The hosts' page automation is the window tests' `TestPage`, shared, with `answerDialogs: false` for the devtools.
2. **The run's id travels as `run`.** Each request may name the run it is for; another run answers `stale_target`. The first version used `target` for it, which the element's `target` object also used (found by the CLI's tests).
3. **An action lists the calls made while it ran:** from its start until no call has run for 300 ms (3 s at most). The research review's rule says the calls it caused; nothing tells a call the click caused from one the page made for another reason meanwhile (the smoke runs' first click listed the page's own first load), so the output says "calls while it ran" and the guide explains it.
4. **Limits and defaults:** an action waits 5 s for its element, `call` 30 s for its command (the command is not cancelled when the wait ends: unfinished, never failed), `calls` keeps 200, `logs` 1,000 entries and shows `info` and above unless `--level debug`, `tree` at most 2,000 elements (5,000 with `--limit`), a request line at most 1 MiB, 8 connections, a `call` value at most 256 KiB in full.
5. **References:** the engine numbers the elements of each snapshot (`e1`…) and keeps them as weak references in its own world, so a new document has none; a reference the last tree did not give, or whose element left the page, is `stale_target` at once, and one taken before the document changed is refused by the server before the engine sees it. A second tree of the same document renumbers.
6. **Linux's click** is the engine's `syntheticClick`: the element made actionable as for a trusted click, then `pointerdown`, `mousedown`, focus, `pointerup`, `mouseup` and `click` at its click point; typing is `execCommand("insertText")`. Each answer carries `synthetic: true`. Keys are not part of M2a's tools.
7. **The socket:** on Windows .NET's `NamedPipeServerStreamAcl` with a protected list granting the current user (the token's user) full control, a name with 16 random bytes, any number of instances; .NET creates instances with `PIPE_REJECT_REMOTE_CLIENTS` (not checked by a test here). On Linux a 0600 socket in `XDG_RUNTIME_DIR` (else a new 0700 folder under the temporary folder), and `SO_PEERCRED` against `geteuid` on every connection. The token is 32 random bytes, compared in constant time.
8. **`ISparCall.Caller`** (`SparCaller.Page`, `Test`, `Devtools`) is new public API: the decision named the caller in `ISparCall`, and tests' calls are told apart the same way. `docs/app-model.md` and its stand-in say so.
9. **The discovery file** names the app by its id without the Development suffix (`com.example.Notebook`), and is left behind when the app is killed rather than closed; the tool then reports that the recorded process has ended.
10. **The tool's redirected output is UTF-8** (no BOM), for every command, `spar doctor` included, and its numbers are culture-invariant; a screenshot goes to `obj/spar-devtools/` unless `--out` names a file.
11. **Loopback has the backend tools only;** the page tools answer `unavailable` with the reason.

- **Tests:** Windows, the solution 1,475 (1,362 passed, 113 skipped), window tests included; WSL, the solution without window tests 1,277 (1,236 passed, 41 skipped), and `Spar.WindowTests` on the private Xvfb 220 (170 passed, 50 skipped; no process left); the NativeAOT lane of `Spar.Tests` 803 (7 skipped), its 20 devtools tests included; `Spar.BuildTests` 7 of 7 (the Notebook's Production NativeAOT executable carries none of the 7 new type markers and 4 literals, which the control finds in Spar.dll); `AppModel.Tests` 11; `eng/check-size.cs` clean (664 files). The smoke sequence on both systems and loopback on Windows, in the evidence.
- **Not verified:** the devtools under an IDE's F5 and across `dotnet watch` restarts; a real Linux desktop (Wayland) and pages that check `isTrusted`; CI.

## Review of non-production code in CoreCLR publications (user decision, 2026-11-26)

- **Before release, everything of Spar's that a Production CoreCLR publication carries but is not production code is reviewed** (plan M4, "What a Production CoreCLR publication carries that is not production code"). It follows decision "Step 7's two findings", which accepted the test and development code switched off in framework-dependent and single-file publications for now. M2a added to it: the devtools' server, tools, call record and log in `Spar.dll`, and each host's `DevtoolsPage`, all behind `Spar.Devtools.IsSupported`, which only NativeAOT trims away. The page engine and the contract snapshot are not in that list: Spar.Sdk embeds them in the app's own assembly in Development builds only. The review lists every such type, resource and dependency, decides for each how it leaves the publication, and adds a test on the publication itself; until then the production boundary test covers the NativeAOT executable only.

## M2b's decide-first questions (user decisions, 2026-11-26)

Asked before building M2b (plan, "M2b: `spar dev`"); the user accepted every recommendation:
1. **A reload is asked for explicitly:** `spar reload` reaches the running `spar dev` over an owner-only local socket of its own, found through `obj/spar-dev.json` with a token (the devtools' transport and protocol), so an agent reloads when its edits are complete and never gets a build of a half-saved change. Not taken: `spar dev` also watching the project's files (`--watch`); or file watching only.
2. **The app keeps running until the new build succeeds:** `spar dev` builds the ordinary Development output in `bin/`, starts the app from a copy of it (so on Windows the build never meets a locked file, and the IDE can build meanwhile), and swaps only after a successful build; a failed build leaves the old app running and answers with the diagnostics. Not taken: stop, build, start, so a failed build leaves nothing running.
3. **The app is stopped through the devtools:** a request that runs the host's own session end (the path of Windows' `WM_ENDSESSION` and Linux's `SIGTERM`: close guards within their budget, the services disposed, 4 s at most), then a kill if the app is still there; an app built with `SparDevtools=false` is killed at once. Not taken: always kill at once; or the OS's session end sent from outside, which a loopback app on Windows lacks.
4. **The window:** on the user's desktop; later restarts take the previous window's place and size and do not take the focus; `spar dev --private` runs the app where the user cannot see it (a desktop of its own on Windows, a private Xvfb on Linux), where the devtools still work. Not taken: the desktop only, with restarts not taking the focus; or plain restarts.
5. **A launch profile** from `Properties/launchSettings.json`, as `dotnet run` and the IDEs read it (`spar dev -lp Loopback`; by default the first profile that runs the project), so the environment and arguments match F5; `spar dev` starts the built executable itself, not through `dotnet run`. Not taken: a `--host window|loopback` option without profiles.
6. **`spar reload` answers after the build and, when it succeeded, once the new run reports `ui-ready`** (or exits, or 60 s pass), so the next `spar tree` finds the new page. The answer lists errors and warnings at file, line and column (C#, Spar's diagnostics and the TypeScript type-check, all from MSBuild's diagnostics), the build's time, the new run id and the start time, or the app's exit code and last output lines when it failed to start; text or `--json`, with `spar doctor`'s exit codes (0, 1 warnings, 2 errors). Not taken: answering right after the build.
7. **One `spar dev` per project,** in the foreground, printing the app's output and each reload's result, until Ctrl+C or `spar dev --stop`, which end everything in its job; a second `spar dev` says where the first runs and exits 64; when the app exits on its own, `spar dev` keeps running and says so, and `spar reload` starts it again. Not taken: `spar dev` ending with the app.

## M2b: spar dev (implementation record; choices for review; 2026-11-26)

M2b's four build steps (plan, "M2b: `spar dev`"), after decisions "M2's decide-first questions", item 5, and "M2b's decide-first questions". Evidence: `docs/evidence/m2b-spar-dev.md`; user guide: `docs/guide/spar-dev.md`. The choices below were the agent's, for the user's review.

1. **The run's copy is under `obj/spar-dev/run-a` and `run-b`, not `bin/`.** The build is the ordinary Debug build into `bin/`; the app runs from a copy, and the two folders take turns so the next run's never holds a file the running app uses. They are in `obj/` because they are `spar dev`'s private staging (`AGENTS.md`), and because `spar doctor --app` takes the newest build under `bin/`. A copy is brought up to date file by file (size and time), so a reload copies the app's assembly and little else (the Notebook's Debug output is 2.5 MB, 15 files).
2. **The build:** `dotnet build <project> -c Debug -t:Build -getProperty:RunCommand -getProperty:TargetDir`, in the session's job or group, with a closed stdin. The diagnostics come from MSBuild's own file loggers (`-flp1:…;ErrorsOnly`, `-flp2:…;WarningsOnly` under `obj/spar-dev/`), not from the console's text; each build restores (a reload after a package change needs it; the reloads' builds, the no-op restore included, took 0.5 to 3.9 s in the smoke runs). Its MSBuild nodes and compiler server are the session's own (`MSBUILDNODEHANDSHAKESALT` and `SharedCompilationId` with the session's id), so they stay warm between reloads, are never shared with an IDE's build, and end with the session.
3. **The type-check now runs `tsc` in the project's folder** (Spar.Sdk, `SparFrontendTypeCheck`), with the frontend's folder as `-p` (or `-b`). From the frontend's folder its errors named `src/main.tsx(27,14)`, which MSBuild, the IDEs and `spar dev` read against the project's folder; now `frontend/src/main.tsx(27,14)`. `Spar.PackageTests` checks the new form. `spar dev` also leaves out the `Exec` task's MSB3073 ("exited with code 1") when the tool it ran reported its own errors, and makes a relative file full against the project's folder.
4. **The stop is a new devtools tool, `stop`,** not a CLI command: each host starts its own session end and answers before it runs. Windows sends `WM_ENDSESSION` (with `ENDSESSION_LOGOFF`) to its own window from a pool thread, the path a sign-out takes; Linux runs the `SIGTERM` path (a later signal is then ignored); loopback takes its Ctrl+C path. `spar dev` waits 6 s for the exit (the session end's 4 s and a margin), then kills the app's process. The session ends measured held 72 ms on Windows. A build with `SparDevtools=false` is killed at once.
5. **A kill ends the app's process alone, not its tree.** WebView2's and WebKit's processes end with their host; the development server, which the app started, is kept for the next run (decision "M2's decide-first questions", item 5) and ends with the session's job or group. The smoke runs left no process on either system.
6. **The window's place:** before a restart `spar dev` reads the previous window's rectangle (the process's visible, unowned top-level window, `GetWindowRect`) and passes it as `SPAR_DEV_WINDOW=left,top,width,height`, which a Development build's Windows host takes for its first window when the rectangle is on a monitor now (`MonitorFromRect`); a Production build never reads it (the production boundary's new marker). No focus: the restarted process gets `SW_SHOWNOACTIVATE` through `STARTUPINFO`, which its first `ShowWindow(SW_SHOWDEFAULT)` takes. A maximized window comes back at its maximized rectangle, not maximized (not built: the maximized state, since `SW_SHOWMAXIMIZED` activates). On Linux nothing: GTK 4 has no window position, and the window manager places the window.
7. **Processes on Windows:** one job object for the session (`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`), which each child joins at creation (`PROC_THREAD_ATTRIBUTE_JOB_LIST`), with three inherited handles (`NUL` as stdin, the two output pipes), in a process group of its own (`CREATE_NEW_PROCESS_GROUP`, so the terminal's Ctrl+C is `spar dev`'s alone), a console program with a hidden console of its own (`CREATE_NO_WINDOW`). The launcher is the tool's own, through CsWin32, after the black-box fixture's.
8. **Processes on Linux:** .NET 11's `StartDetached` (`setsid`: a session and process group of its own, no terminal signals) with `KillOnParentExit`, `/dev/null` as stdin; at its end `spar dev` sends `SIGKILL` to each child's group. If `spar dev` itself is killed outright, `KillOnParentExit` ends the direct child only, and what it started (the development server, MSBuild's nodes) ends on its own idle timeout (pitfall).
9. **`--private`:** on Windows a desktop of its own made by `spar dev` (`CreateDesktop`, never switched to, freed with the session), the app's processes started on it through `STARTUPINFO.lpDesktop`; on Linux an Xvfb that picks its own display (`-displayfd`), on an abstract socket only, with `DISPLAY`, `GDK_BACKEND=x11` and no `WAYLAND_DISPLAY` for the app; the session's D-Bus is kept. Xvfb missing is an error naming the package.
10. **Ready is the host's phase line on stderr:** `ui-ready` for a window host, and `loopback listening` for loopback, whose page is in a browser `spar dev` does not open (the first loopback run waited the whole 60 s for a `ui-ready` that needs a browser page). The line also gives the run's id. A run that is not ready within 60 s answers `starting` with its last 40 lines, and `spar reload` exits 2 for it, as for a run that exited.
11. **Reloads run one at a time;** a `reload` that arrives during a build waits for it, then builds again, so its answer covers the files as they were when it asked.
12. **`spar dev`'s socket reuses the devtools' code:** `DevtoolsListener.cs`, `JsonRpc.cs` and `OwnerOnlyFile.cs` are linked into the tool (the listener takes a name prefix, `spar-dev-`), the methods are `initialize` (the token), `reload`, `status` and `stop`, duplicate ids are refused, and `spar reload` and `spar dev --stop` use the devtools' client. One `spar dev` per project is an exclusive `obj/spar-dev.lock`, held open for the session; a second one reads `obj/spar-dev.json` to say where the first runs.
13. **Launch profiles:** `commandName: Project` profiles only; `commandLineArgs` split as a simple command line; `workingDirectory` against the project's folder, else the project's folder; `DOTNET_LAUNCH_PROFILE` set to the profile's name, as `dotnet run` sets it. `RunArguments` is not used: a Spar app has an apphost.

- **Tests:** Windows, the solution 1,495 (113 skipped, 0 failed), window tests included; WSL, the solution without window tests 1,297 (41 skipped) and `Spar.WindowTests` 220 (50 skipped), 0 failed; the NativeAOT lane of `Spar.Tests` 805 (7 skipped); `Spar.BuildTests` 7 of 7 with the new marker; `Spar.PackageTests` 42 of 42 (one timing failure in a run beside the WSL window tests, passed on the rerun); the smoke runs on both systems (`docs/evidence/m2b-spar-dev.md`).
- **Not verified:** the window's place and focus on the user's own desktop (a row of the plan's table "Checks that need the user at the PC"), an IDE building while `spar dev` runs, a real Linux desktop, `spar dev` killed outright, Ctrl+C in a real terminal, CI (no CI job runs `spar dev`).

## M2b: the checks at the PC (implementation record; 2026-11-26)

The plan's row for `spar dev` on the user's own desktop (table "Checks that need the user at the PC"), run with the user on the Notebook, `spar dev` in the app's Terminal panel (data folders under `artifacts/webview2-profiles/m2b-spar-dev/desktop/`), each reload run by the agent 10 s after the user said they were ready, while they typed in Notepad:
- **Place and focus: passed.** After a reload the new window opened in the moved and resized window's place and size, and the typing stayed in Notepad throughout (built 1.2 s, page ready 1.6 s from the start).
- **A maximized window: as designed.** It came back over the same area, not maximized, and the typing stayed in Notepad (choice 6 of "M2b: spar dev (implementation record; choices for review; 2026-11-26)").
- **An IDE build while `spar dev` ran: failed, fixed, passed.** Visual Studio's build failed with MSB4186: the type-check's new `$([System.IO.Path]::TrimEndingDirectorySeparator(…))` (choice 3) does not exist in Visual Studio's MSBuild, which runs on .NET Framework, and no `dotnet` build reaches that MSBuild. The frontend's folder is now given as `frontend\.`, which needs no trimming; `dotnet build` and Visual Studio's `MSBuild.exe` (18 Community) both report a type error at `frontend/src/main.tsx(27,14)`, and the user's Visual Studio build then passed with the window open (nothing locked).
- **Ctrl+C in `spar dev`'s terminal: failed, fixed, passed.** The first time the app took the Ctrl+C itself ("Ctrl+C from the parent's console: the app closes"): a Development build of a Windows app attaches to its parent's console and turns Ctrl+C back on for `dotnet watch` (decision "Step 3's two follow-ups"), so the process group of choice 7 did not keep it out. Its ordinary close removed its discovery file, `spar dev` found no devtools and killed it at once. Now `spar dev` sets `SPAR_DEV_SUPERVISOR` (its process id) for the runs it starts, and a run with it does not attach to the console (`ConsoleInterrupt`, Development builds only); and a run that had devtools gets the same 6 s to end before a kill even when its discovery file is gone. The second time: no dialog, "the app ended through its session end" (`WM_ENDSESSION` held 52 ms, exit 0), and no process of the run left.
- **Left for the user to decide:** whether a maximized window should come back maximized even though `SW_SHOWMAXIMIZED` takes the focus.
- **Tests after the two fixes:** `Spar.Tests` 810 (7 skipped), `Spar.Tool.Tests` 114, `Spar.BuildTests` 7 of 7, `Spar.PackageTests` 42 of 42, on Windows.

## A restarted maximized window stays unmaximized (user decision, 2026-11-26)

Asked after the checks at the PC (decision "M2b: the checks at the PC (implementation record; 2026-11-26)"): a maximized window that `spar dev` restarts comes back over the same area but not maximized, as built (choice 6 of "M2b: spar dev (implementation record; choices for review; 2026-11-26)"), because `SW_SHOWMAXIMIZED` would take the focus from whatever the user is typing in. Not taken: restoring the maximized state at the cost of the focus.

## A restarted maximized window comes back maximized, without the focus (implementation record; 2026-11-26)

Supersedes "A restarted maximized window stays unmaximized": asked whether it could be configurable, the user chose to try first whether Windows could restore the maximized state without the focus, which would need no option. Spike `spikes/maximized-noactivate` (its `RESULTS.md`), on a private desktop: every show command that maximizes (`SW_SHOWMAXIMIZED`, `SW_MAXIMIZE` after a show, `SetWindowPlacement` with `SW_SHOWMAXIMIZED`) activated the window, and `WS_MAXIMIZE` at creation with a place given was ignored; a hidden window given its normal place (`SetWindowPlacement` with `SW_HIDE`), marked `WS_MAXIMIZE` and sized to its monitor's work area plus the sizing frame, then shown with `SW_SHOWNA`, was maximized (`IsZoomed`, the same rectangle as a real maximize), never activated, and its Restore went back to the normal place.
- **Built:** `spar dev` passes a maximized window as `SPAR_DEV_WINDOW=left,top,width,height,maximized`, the four numbers its normal place (the placement's, from workspace to screen coordinates by the work area's corner); a Development build's Windows host then does the above (`WindowPlacement.MaximizeHidden`, the frame from `GetSystemMetricsForDpi` at the window's DPI) and shows it with `SW_SHOWNA`. No option: the default is maximized again, without the focus.
- **At the PC (user, 2026-11-26):** after a reload the maximized Notebook came back maximized (the button showed Restore), the typing stayed in Notepad, and Restore went back to the window's earlier normal place.
- **Not verified:** a second monitor, scales other than 100 %, a page-drawn title bar.

## M2c's decide-first questions (user decisions, 2026-11-26)

Asked before building M2c (plan, "M2c: `spar snap` and scenes"); the user accepted every recommendation:
1. **A scene is a static method in the end-to-end test project marked `[SparScene("name")]`,** taking a `SparScene` whose `StartAsync<Program>(o => …)` starts the app on the Window surface with the scene's defaults (the fixed clock, light theme, a fixed window size) and the scene's own options, then drives the page with `app.Page` as a window test does; the capture is taken when the method returns, and the scene disposes the app. Spar's generator lists the scenes (no reflection), and every scene also runs as a window test, so a broken scene fails `dotnet test`. Not taken: an xUnit test calling `app.SnapAsync`, run through `dotnet test --filter`; a scene class with separate setup and steps.
2. **`spar snap` builds the end-to-end project (Debug) and runs its test executable with `--spar-scene <name> --spar-snap-out <folder>`,** answered by Spar's generated code before the test framework starts: one app start, no test platform. Not taken: `dotnet test --filter-trait`; a separate runner project.
3. **No steps beyond the scene's own C#;** exploring by hand is `spar dev` with the devtools. Not taken: a steps file of devtools actions.
4. **One fixed instant per scene** (`2026-01-15T09:30:00Z` unless the scene sets its own), for the backend's `TimeProvider` and the page's `Date` and `Date.now`, both advancing in real time from it; timers are not frozen. Not taken: a frozen clock advanced by hand; the backend only.
5. **The same PNG every run on the same machine and OS:** 1280 × 800 CSS pixels at a device scale of 1, light theme, `prefers-reduced-motion: reduce`, the caret hidden, the capture after 200 ms of idle (no bridge call, no animation, fonts loaded), checked by a test that snaps twice and compares the bytes; nothing promised across machines or between Windows and Linux. Not taken: bundled fonts; no idle wait.
6. **`obj/spar-snap/<scene>/` in the end-to-end project (or `--out`):** `page.png`, `tree.txt` and `tree.json`, `calls.json`, `problems.txt`; a one-line summary per scene; no comparison with a stored image in M2c. Not taken: committed baselines with `--check`; printing the files.
7. **`spar snap --list`, `spar snap <scene>...`, `spar snap --all`,** with `--project`, `--out` and `--json`; exit codes 0 written, 2 a scene failed (its exception and the last lines of its log), 64 a wrong command line. Not taken: `spar scene list` and `spar scene snap`.

## M2c: spar snap and scenes (implementation record; choices for review; 2026-11-26)

M2c's four build steps (plan, "M2c: `spar snap` and scenes"), after decisions "M2's decide-first questions", item 6, and "M2c's decide-first questions". Evidence: `docs/evidence/m2c-spar-snap.md`; user guide: `docs/guide/spar-snap.md`; `docs/app-model.md` 6.15. The choices below were the agent's, for the user's review.

1. **A capture runs through the test executable's own xUnit runner, not before the test framework** (decision item 2 asked for the latter). A scene run from the generated module initializer hung: until a module initializer returns, the runtime holds every other thread at the assembly's first type, and the scene's continuations run on the thread pool. xUnit v3's generated entry point calls the test platform's builder hooks only in server mode, so no hook runs before xUnit's console runner either. So the module initializer only registers the scenes and answers `--spar-scene-list` (which touches nothing else), and `spar snap` runs `<test executable> -trait SparScene=<name>` with `SPAR_SNAP_SCENE` and `SPAR_SNAP_OUT`, which the scene's generated test reads to write its files. Still no test platform and no session: about 1 s of xUnit's start per scene.
2. **The generated scene tests run in the CoreCLR lane only:** xUnit's NativeAOT lane finds its tests with a source generator of its own, which does not see another generator's output. `Spar.WindowTests`' own scene tests (`SceneTests`) are ordinary tests and run in both.
3. **A scene's window renders in software on Windows** (`--disable-gpu`, an environment of its own in the test process): with the GPU, two captures differed in single anti-aliased pixels by one level (a rounded corner's 169 or 170); in software, three captures of each Notebook scene were the same bytes. WebKitGTK under the test's Xvfb renders in software already.
4. **Reduced motion is emulated on Windows only** (CDP `Emulation.setEmulatedMedia`, in one call with the colour scheme, since each call replaces the last); WebKitGTK cannot emulate it. On both, the capture finishes every finite animation and pauses an endless one at its start, through the page's own Web Animations, before it waits for idle.
5. **The caret is hidden by `caret-color: transparent !important` on the root element** (CSSOM, which the page's Content Security Policy allows); an element that sets its own `caret-color` as important would still show it. **The pointer moves to the page's bottom-right corner** before the capture: the hover style of the last clicked element showed in one capture and not in another.
6. **The page's size:** on Windows CDP `Emulation.setDeviceMetricsOverride` (1280 × 800, device scale 1), whatever the window and the monitor; on Linux the view's size request and the window fitted to it (`gtk_window_set_default_size(1, 1)`), on the test's Xvfb at scale 1.
7. **The page's clock is a script of the page's world, added before the page's own** (CDP `Page.addScriptToEvaluateOnNewDocument` without a world, a WebKit user script): `Date` with no arguments and `Date.now` count from the scene's instant plus the real time since the scene started, so a navigation does not restart it; `Date` with arguments, `Date.parse`, `Date.UTC` and `instanceof Date` are unchanged. `Intl`'s own "now", `performance.now()` and the time zone (the machine's) are not changed.
8. **`tree.json` has its keys sorted** (WebKitGTK returned them in a varying order), and **`calls.json` keeps each call's time and duration**, which differ between runs; the smoke compares it without them.
9. **Calls are recorded in test builds when a recorder is set:** the dispatcher records when the devtools' switch or the test host's is on (both read at each use, so Production trims both); a scene sets its recorder when the app is built (`SparTestSession.Built`), before its window opens, so the page's first calls are in `calls.json`.
10. **The end-to-end project is found by text:** a project file whose folder's C# files contain `[SparScene(` (also qualified, or with `Attribute`), from `--project`, the current folder, or the folders beside an app's; two candidates are an error naming them.
11. **`spar snap` builds with session-private MSBuild nodes and compiler server,** as `spar dev` does, which a single command run cannot keep warm: `--list` with the build took 5.8 to 10.8 s on Windows; `--no-build` (and no restore) captures one scene in about 2.5 s.
12. **An unknown scene exits 64,** a wrong command line; a failed scene or build exits 2 with the test's failure lines.
13. **The Notebook says "now" for anything under a minute,** instead of counting seconds: a scene's sidebar read "now" or "1 second ago" depending on how long the scene took, and a label that changes every second while nothing changes is noise anyway. The Notebook gained three scenes (`empty`, `new-page-dialog`, `groceries`).

- **Tests:** Windows, the solution 1,514 (113 skipped, 0 failed) on an idle machine; WSL, the solution 1,310 (41 skipped) and `Spar.WindowTests` 223 (50 skipped), 0 failed; the NativeAOT lane of `Spar.Tests` 805 (7 skipped) and the scene tests 2 of 2 under NativeAOT; `Spar.BuildTests` 7 of 7; `AppModel.Tests` 11; `Spar.PackageTests` 41 of 42, the template's black-box journey intermittent locally (3 of 4 runs failed today; CI passed it on every M2b commit); every Notebook scene the same bytes twice on both systems (`docs/evidence/m2c-spar-snap.md`).
- **Not verified:** captures across two machines (not promised); a high-DPI monitor (the viewport's scale is overridden, not observed at 150 %); WebKitGTK with a GPU; scenes in the NativeAOT lane (choice 2); CI.

## M2d's decide-first questions (user decisions, 2026-11-26)

Asked before building M2d (plan, "M2d: the optional OpenTelemetry layer, client telemetry and Aspire"), on what the M0 decisions left open; the user accepted every recommendation:
1. **A package reference to `Spar.OpenTelemetry` and `spar.Telemetry(otel => …)` in `Program.cs`,** whose lambda takes the OpenTelemetry builder for the app's own instrumentation and resource; the build gives the page its module when the package is referenced; nothing is exported until `OTEL_EXPORTER_OTLP_ENDPOINT` is set. Not taken: the reference alone with fixed defaults; an MSBuild property.
2. **The backend exports traces, metrics and logs:** Spar's `Spar` activity source and meter, the app's and Spar's `ILogger` lines through Spar's logger factory at the log file's levels, and what the app adds; OpenTelemetry's standard variables honoured (`OTEL_TRACES_SAMPLER`, `OTEL_EXPORTER_OTLP_PROTOCOL`, `OTEL_EXPORTER_OTLP_HEADERS`, `OTEL_RESOURCE_ATTRIBUTES`). Not taken: traces only.
3. **The page sends traces only:** a span per bridge call (its backend span's parent), `traced(name, fn)` spans and the document's load; its errors and console messages reach the backend's log already. Not taken: page logs through `/_spar/otlp/v1/logs`.
4. **The relay forwards only `POST /_spar/otlp/v1/traces`** (protobuf or JSON) behind the bridge's origin, frame and token checks, refuses a body over 1 MiB (413) and more than 20 requests a second per document (429), and adds `OTEL_EXPORTER_OTLP_HEADERS`. Not taken: the spike's 4 MiB without a rate limit; every `/v1/*` signal.
5. **An Aspire recipe in a guide page** (Aspire 13.x, `AddProject` with the launch profile, the dashboard's OTLP variables; window and loopback; loopback advertising `127.0.0.1`), checked by a test outside the solution that builds the recipe's AppHost against Spar's packages, runs the app headless under it and reads the app's and the page's spans from the dashboard's OTLP endpoint, on demand and in CI's package-test job. Not taken: a one-off evidence run; a hosting package.
6. **The official SDK's size is accepted,** measured on the Notebook and kept as a budget line; the Spar-owned minimal exporter stays parked. Not taken: building the minimal exporter now.

## M2d: the optional OpenTelemetry layer, client telemetry and Aspire (implementation record; choices for review; 2026-11-26)

M2d's build (plan, "M2d: the optional OpenTelemetry layer, client telemetry and Aspire"), after decisions "Optional OpenTelemetry layer", "Browser telemetry through the host: confirmed", "Browser telemetry: how the page starts it" and "M2d's decide-first questions". Evidence: `docs/evidence/m2d-telemetry.md`; user guide: `docs/guide/telemetry.md`; `docs/app-model.md` 3.18 and 6.16. The choices below were the agent's, for the user's review.

1. **The core owns the routes, the layer owns the relay.** `Spar` has `/_spar/otlp/*` (`Spar.Telemetry.TelemetryRoute`: the 404 without the layer or an endpoint, 405 with `Allow: POST`, the configuration, 415, 429, 413) behind an internal `ITelemetryRelay` that `spar.Telemetry()` sets on the builder; `Spar.OpenTelemetry` has the OpenTelemetry SDK and the relay. So an app without the package carries no OpenTelemetry code, and every host answers the same.
2. **The rate limit is per window on the desktop hosts and per app on loopback** (decision item 4 said per document): the route is made once per window, or once for the loopback host, with a bucket of 20 and 20 a second. A reload keeps its window's bucket.
3. **The relay speaks the endpoint's own protocol** (`OTEL_EXPORTER_OTLP_PROTOCOL`, gRPC by default as for .NET's exporter): over `http/protobuf` and `http/json` it posts the page's bytes to `{endpoint}/v1/traces`; over gRPC it frames the same protobuf as one uncompressed gRPC message to `opentelemetry.proto.collector.trace.v1.TraceService/Export` over HTTP/2 and maps `grpc-status` to the page's HTTP status (16 → 401, 7 → 403, 8 and 14 → 503, 3 → 400, others 502). JSON cannot go over gRPC (415). Checked against the Aspire dashboard's real gRPC server.
4. **The page's settings:** the desktop hosts add `globalThis.__sparTelemetry = Object.freeze({serviceName, delayMs})` to the host-injected script only when the relay has an endpoint (on Linux a WebKit user script limited to `spar://app/*`); loopback's page asks `POST /_spar/otlp/config` with its token. `delayMs` is `OTEL_BSP_SCHEDULE_DELAY` (5000 ms by default), so the page exports at the backend's pace.
5. **The page bundles its chunk when the app references the layer,** read from the resolved references (`SparPageTelemetry`, `false` to keep it out), written by the client step as `telemetry.json` beside the package's `csp.json`, and given to the page as `import.meta.env.SPAR_TELEMETRY` by the Vite plugin. With it false, `vite build` drops the start and the lazy import: the Notebook's bundle without the layer has no chunk and no telemetry code, only the loopback channel's one-line `telemetryRelay` method.
6. **The chunk is prebundled when the SDK is built** (`vite.telemetry.config.ts`: OpenTelemetry's browser builds, `@opentelemetry/sdk-trace` 3.1.1, `exporter-trace-otlp-proto` 0.301.2, `instrumentation-document-load` 0.70.1 and their dependencies, eleven packages), so an app's `@spar/app` package needs no npm dependency of its own. Their licences are written beside it as `licenses.md` in the sections of Vite's `.vite/license.md`, and Spar's Vite plugin appends them to the app's licence file when the bundle has the chunk, so the packing step embeds them as the app's third-party notices: the app's minifier drops a licence comment in the chunk.
7. **The page has a synchronous context only:** OpenTelemetry's `StackContextManager`, so `traced` parents the calls started inside it before its first `await`, and the document-load instrumentation nests its spans (without it, `documentFetch` was a trace of its own). Spar's call span is `contract/command`, kind CLIENT, with `rpc.system=spar`, `rpc.service`, `rpc.method` and `spar.outcome` named as on the backend's span (`success`, `error`, or the failure's kind with `error.type` and an error status).
8. **The chunk starts beside the app's start and never delays it:** calls made before it loads have no page span (the backend's span is then a root); on Windows the Notebook's first `pages/list` ran before it, on Linux after. On close the connection's closing work flushes it for at most 1 s.
9. **`Spar.Sdk` gives the layer the SDK's version:** `<PackageReference Include="Spar.OpenTelemetry" />` without a version gets `$(SparSdkVersion)`, or a `PackageVersion` under central package management unless the app has one; from source the reference becomes one to `src/Spar.OpenTelemetry`.
10. **The black box removes `OTEL_*` variables** from a launched app's environment unless the test sets them, so a developer's OTLP settings never send a test's telemetry anywhere.
11. **The Notebook sample turns telemetry on** (`spar.Telemetry()`), as the guide's example and the end-to-end test's app. Its NativeAOT executable on Windows grows from 5,308,416 to 11,263,488 B, over its plan budget of 6 MB: the user's decision (see the plan's budget table).
12. **The Aspire check runs on Linux and in CI only:** a run on Windows wrote DCP's `mruPorts.list` and `state` under the real `%USERPROFILE%\.dcp` (ASP.NET Core's Data Protection also reads `%LOCALAPPDATA%\ASP.NET\DataProtection-Keys` through the known-folder API, whatever `LOCALAPPDATA` says), so `AspireTests` skips on Windows outside GitHub Actions; on Linux it gives the app host a private home and an Xvfb of its own. It uses Aspire 13.6.5 (17.0.0 shipped 2026-11-18, after the decision named 13.x) without the Aspire CLI's bundle (`AspireUseCliBundle=false`, ASPIRE010 in the check's own project), reads the dashboard's telemetry API (`/api/telemetry/resources` and `/spans`, unsecured with `ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS`), and checks the app's logs and the page's spans: the template's page makes no call by itself, so the page-to-backend link is checked by the Notebook's black-box test instead.
13. **`POST /_spar/otlp/config` answers 204 while telemetry is off** (no layer, or no endpoint), where decision item 4 and the routes' design said 404 for every path: loopback's page asks on every load, and a 404 is a failed request in the browser's console, which failed seven of the Notebook's browser journeys (their pages must log nothing). The traces route stays 404.
14. **The Notebook's black-box telemetry test invokes the sidebar button until its call is made** (at most four times, the count in its output): the first Invoke right after the button appeared made no call in 4 of 6 runs on Windows, `InvokeAsync` reporting success (pitfall "A black-box Invoke right after a page button appears can do nothing"). The fixture itself is not changed in M2d.
15. **The production-boundary test lost four markers that an app with the layer carries legitimately** (`tests/Spar.BuildTests/ProductionBoundary.cs`, each reason beside the list): `LoopbackHost`, which is also `System.Uri`'s internal flag and arrives with .NET's HTTP stack (the file's summary already warned of it); `Spar.Loopback` and `Spar.Testing`, which are the arguments of `Spar.dll`'s `InternalsVisibleTo` attributes, kept as metadata once OpenTelemetry's reflection makes NativeAOT give constructed types theirs (the compiler's graph: the app's `Action<SparBuilder>` gets metadata, so the `Spar` module becomes reflectable and its attributes are constructed); and `ICoreWebView2HttpRequestHeaders`, which the Windows host's telemetry route now reads in Production for a request's `Content-Type`. Each assembly and the development server keep markers of their own types (`SparLoopbackHost`, `LoopbackChannel`, `LoopbackApp`, `/_spar/channel`; `SparTestApp`, `SparTestSession`, `SparBlackBox`; `DevServerProxy`, `DevServerSession`). The Notebook's Production executable with the layer had all four and none of the others.

- **Tests:** Windows, the solution 1,552 (113 skipped, 0 failed); the NativeAOT lane of `Spar.Tests` 842 (7 skipped), `Notebook.Tests` 12 and `Notebook.EndToEndTests` 21 (4 not run, the browser journeys), the publishes without warnings; WSL, the solution 1,347 (41 skipped), `Spar.WindowTests` 223 (50 skipped) and the Notebook's black box 7, 0 failed; the SDK's Vitest 348; `AppModel.Tests` 11; `Spar.PackageTests`' Aspire check and guide-block tests on Linux 3 of 3. `Spar.BuildTests` 7 of 7 (after choice 15); `Spar.PackageTests` on Windows 41 of 44 (the Aspire check skipped), the two failures the template app's black-box journey (`Type a name first.` never shown after its button's Invoke), intermittent since M2c in apps without the layer and probably the lost Invoke of choice 14. `Spar.WindowTests`' "Two captures of a scene are the same bytes" failed 1 of 6 runs here and 2 of 8 on M2c's commit, so it was flaky before M2d.
- **Not verified:** the Aspire check on Windows (CI only, choice 12) and the `Loopback` profile under Aspire; the dashboard's OTLP API key through the relay (checked against a fake handler only); Aspire 17.0.0; the 1 s flush at a window's close with a real endpoint; CI.

## The OpenTelemetry layer's budget line (user decision, 2026-11-26)

Asked after M2d's build, whose choice 11 put the Notebook's NativeAOT executable at 11,263,488 B on Windows and 11,494,184 B on Linux, over its 6 MB and under its 8 MB budget. The user chose a separate budget line for the layer: the Notebook keeps the layer, and its own line is measured without it. Not taken: dropping the layer from the Notebook and running the black-box telemetry test on a test app.

- **The Notebook's line** stays ≤ 6 MB / ≤ 8 MB, measured from M2d on with the layer left out (its package reference and `spar.Telemetry()` removed): 5,308,416 B / 5,220,048 B at M2d.
- **The layer's line** is the difference with it, ≤ +7 MB on each OS (measured +5,955,072 B / +6,274,136 B, about 15 % and 10 % headroom, as the table's other budgets were set from their spikes), and its page chunk ≤ 25 KB gzip (measured 20,471 B). The numbers follow the table's rule (set from the measurement with headroom) and are the user's to change; M4 replaces them with measured budgets like the others.
- The layer's cost is mostly .NET's HTTP and TLS stack, which an app that already makes HTTPS requests has: such an app pays less (the spike's +4.3 MB), so the line is a ceiling for the Notebook's case, an app with no HTTPS of its own.

## A scene's capture waits for the page's pending timeouts (implementation record; for review; 2026-11-26)

`Spar.WindowTests.SceneTests.Two_captures_of_a_scene_are_the_same_bytes_at_its_size` failed intermittently on Windows from M2c on (2 of 8 runs at 48801e8, 1 of 6 at a40fd10), against decision "M2c: spar snap and scenes (implementation record…)", choices 3 to 7, which promise the same PNG every run on one machine. With the test saving both PNGs on a mismatch, 7 of 40 runs differed. Every time it was the same 2,352 pixels in a 107 × 22 box, and nowhere else: the test app's "Loading…" layer over its covered button, which the page removes with `setTimeout(…, 400)`. The trees were the same, since the layer has no role. The idle wait (`SceneCapture`) watched bridge calls, animations and fonts. It could not see a timer still to fire, and by about 400 ms after load the page had been quiet for its 200 ms in about one run in six. GPU rendering, the caret, animations and the page clock were not involved.

1. **The scene's page script tracks the page's timeouts:** beside the clock, at the start of every document in the page's world, `setTimeout` records each pending timeout's due time, `clearTimeout` and `clearInterval` forget it, and a non-enumerable `__sparSceneTimer()` answers how far away the soonest is. Intervals are endless and not counted; `requestAnimationFrame` loops are not either. The timers are still the system's: nothing is frozen or fast-forwarded (decision "M2c's decide-first questions", item 4).
2. **The capture is busy while a timeout is due before its 10 s limit runs out;** one due later cannot fire before the capture anyway. So a layer removed after 400 ms is gone in every capture, and a message the page hides after 5 s is too: such a scene waits for it. A page that keeps re-arming a short timeout is busy until the limit, as an endless animation would be.
3. **A page still busy at the limit is a problem in `problems.txt`** ("the page was not idle after 10 s (…); captured as it was"); before, it was captured silently.
4. **A test for it:** the scene `pending-timeout` sets a timeout that adds a button 1 s after the scene ends, and `A_capture_waits_for_the_pages_pending_timeouts` reads the button in the capture's tree. With the timeout check turned off, it failed 3 of 3 runs.

Not taken: a MutationObserver quiet period (it does not see a timer still to fire either); a longer fixed quiet period (slower for every scene, and still a race with a longer timer); changing the test app (an app's own page can do the same).

- **Windows:** before, 7 of 40 runs of the test failed. After, 30 consecutive runs of it passed, then 30 consecutive runs of `SceneTests` (90 tests, the new one included). The generated scene tests passed 3 of 3. The test takes about 3.0 s instead of 2.9 s.
- **WSL:** 10 consecutive runs of `wsl.sh window --filter-class Spar.WindowTests.SceneTests` passed (3 tests each), from a private copy of the script with its own `base` (see the pitfall on the shared copy). The generated scene tests passed 3 of 3.
- **Notebook:** `spar snap --all` gave every file the same bytes as the run before, with 0 problems; `--all --no-build` took 6.7 s (M2c's evidence: 6.8 to 6.9 s).
- **Not verified:** CI; the full solution (the change reaches only a scene's page script and its capture).

## Black-box invokes are confirmed (implementation record; choices for review; 2026-11-26)

M2d's telemetry test found a black-box `InvokeAsync` that reported success while the page never got the press (pitfalls, "A black-box Invoke right after a page button appears can do nothing"), the probable cause of the template app's intermittent black-box journey since M2c. Measured on Windows 11 with WebView2 on WindowHost's plain HTML page, so not React's doing:

- **The cause is timing in the browser, not the fixture's search.** UI Automation's first look into the WebView shows only the window's 3 caption buttons; the page's tree (its document element, ARIA role `document`, then its 17 buttons) follows 40–100 ms later. Presses sent 16–83 ms after the document first appeared were lost in 5 of 30 launches, most often when the tree was already complete at the first look; none sent 150 ms or more after it were lost (36 launches, at 150, 300 and 400 ms). A lost press leaves its element in the tree, its name readable, for at least 1.5 s; `Invoke` returns `S_OK`. Why Chromium drops it was not found: the browser's process is not ours to trace, and the fix does not depend on it.
- **Chromium confirms a press.** It raises UI Automation's Invoked event (`UIA_Invoke_InvokedEventId`, 20009) on the element when the page has dispatched the click: 28 of 28 landed presses had the event 1–73 ms after the invoke, 2 of 2 lost ones had none in 1.5 s. A press that removes its own element (WindowHost's dialog button that hides the dialog, a link to the second page) gets no event, the event going with its node; its element is gone from the tree (`UIA_E_ELEMENTNOTAVAILABLE`) about 80 ms after the press.
- **Linux does not lose them:** WebKit's `DoAction` press as the first action after the launch landed in 32 of 32 launches (4 runs of the new test, before the fix).

The fixture now (choices 1–4 are for review):

1. **The ready check looks into the page once.** After the `ui-ready` line and the window, `WaitForReadyAsync` asks once for the page's document, which makes the browser build the page's tree, and notes the time it is first seen. `StartedAfter` is still taken before that look.
2. **An app's first action through the tree waits for the page to settle:** `InvokeAsync` and the internal `FocusAsync` (UI Automation's `SetFocus` reaches the page the same way) wait, once per launch, until the page's document has been in the tree for `PageSettleTime`, polling every 25 ms until it appears, within the action's timeout. 500 ms on Windows, about 6 times the latest loss measured and 3 times the earliest delay with none; zero on Linux. The first invoke after a launch now takes 520–640 ms (24 launches) instead of 30–150 ms. A page reached by a navigation is not waited for again: its first press after the navigation landed in 3 of 3 runs, 14 ms after it was sent.
3. **An invoke waits for the browser's confirmation.** On Windows a page element's invoke (one with an ARIA role; a native element is not waited for) registers an `IUIAutomationEventHandler` for the Invoked event on the element (`TreeScope_Element`) before pressing, on the UI Automation thread, and `InvokeAsync` returns on the event, on the element gone from the tree (checked every 50 ms), or on the app's exit (a press that quits it); the registration is removed after. The new interop: `IUIAutomation`'s slots 29–33 (`AddAutomationEventHandler`, `RemoveAutomationEventHandler`, the latter `[PreserveSig]`, since an element whose app is gone may fail it) and the fixture's first implemented COM interface, `IUIAutomationEventHandler`, a `[GeneratedComClass]` (`InvokedHandler`). Linux has no confirmation: AT-SPI's `DoAction` answers once WebKit did the action.
4. **An unconfirmed press fails loudly and is never repeated.** With no confirmation within the action's timeout (10 s by default), `InvokeAsync` throws a `TimeoutException` saying the press was not repeated: a press can still reach the app late, and AGENTS.md's rule on replaying applies to a press that may start a backend command. With the settle time set to zero for the check, 2 of 3 runs of the new test failed with that exception instead of passing with a lost press. Tests never retry an invoke themselves: the telemetry test's loop is gone.

- **Tests:** `BlackBoxFixtureTests.An_invoke_as_the_first_action_after_the_launch_lands` (8 launches, each invoking a page button as its first action and checking its effect) lost 4 of 24 launches before the fix (3 runs: 7, 6 and 7 of 8 landed) and 0 of 24 after on Windows; `A_press_that_removes_its_element_or_leaves_the_page_is_taken` (the dialog's button, the link, the second page's button); `BlackBoxInvokeUnitTests`, the locator on a fake accessibility client: one press and a failure with no confirmation, the removed element counted as taken, the settle wait before the first invoke only, and a page that never appears failing without a press. The slot test counts 8 interfaces.
- **Verified:** Windows, `Spar.WindowTests` 128 passed (102 skipped, 0 failed), the Notebook's black box 7 of 7 (the telemetry test with one invoke), `Spar.WindowTests`' NativeAOT executable with the black-box fixture's and the new unit tests 27 of 27 (published with warnings as errors), the UI Automation slot test (8 interfaces), `AppModel.Tests` 11 of 11, and `Spar.PackageTests` in 4 runs, 43 of 44 each (the Aspire check skipped), the template app's black-box journey passing in every run; WSL, `Spar.WindowTests` 180 passed (50 skipped, 0 failed), the Notebook's black box 7 of 7, `Spar.PackageTests` 42 of 44 (the two Windows-only publication checks skipped). `eng/check-size.cs` clean.
- **Not verified:** CI; Windows Sandbox's lanes (`FocusAsync` now waits as an invoke does there); a slower machine's settle time (a loss there fails the invoke loudly rather than passing); why Chromium drops the early press.

## M2's first CI run after the merges: two test fixes (implementation record; 2026-11-26)

CI on `cc4765b` (the scene and black-box fixes merged into `m2`) failed two jobs, both in tests, not in Spar:
- **Linux package tests:** the Aspire check (M2d choice 12) waited 3 minutes for the app's logs in the dashboard and failed. The hosted `ubuntu-latest` runner has no WebKitGTK 6.0 (the package tests that launch an app on Linux were already "not executed" there for that reason), so the app could not start. The check now calls the same guard as those tests (`TemplateTests.SkipWithoutLinuxDesktop`: WebKitGTK 6.0, Xvfb and `dbus-daemon`): it runs on a Linux machine with a desktop stack, such as WSL here, and in CI on Windows only. Not taken: installing WebKitGTK on the runner, which would also turn on the template's Linux window and black-box runs there, a change of its own.
- **Linux black box:** `LinuxPageSecurityTests.Every_response_carries_the_policy_and_the_headers_the_worker_and_404_included` read the app's log while a 3.6 KB line was still being written, and its pattern's `$` matched the end of the part written so far, so the JSON was cut at byte 3,631. `ProbeApp.Log` now returns the log up to its last complete line, for every test that reads it. Earlier runs had passed; it is a race of the helper, older than M2.

## M2e's decide-first questions (user decisions, 2026-11-26)

Asked before building M2e (plan, "M2e: the agent guide, the app-author plugin, the MCP adapter and the evaluation"), after reading how dotnet/skills evaluates its skills (one `eval.yaml` per skill: `stimuli` with a prompt, deterministic graders, an optional LLM-judged rubric and `constraints`; its runner, Vally, is not public and drives the GitHub Copilot CLI) and the official C# MCP SDK (`ModelContextProtocol.Core` 2.4.0, marked AOT-compatible, a stdio server transport, no hosting dependency). The user accepted every recommendation:
1. **`spar mcp`, a command of `Spar.Tool`,** serves MCP over stdio with `ModelContextProtocol.Core` (the low-level server, tools registered by hand, no reflection); each tool forwards one JSON-RPC request to the running app's devtools socket or to `spar dev`'s; the publish is checked for zero AOT and trim warnings. Not taken: a hand-written MCP server; a separate package.
2. **Both surfaces cover the same set:** the devtools' commands, `spar dev`'s `reload`, `spar snap` and `spar doctor`; the agent starts `spar dev` itself in both profiles. Not taken: the devtools only on MCP.
3. **Four skills,** written from the user guides and `docs/app-model.md` with compiled examples, each under 500 lines: `spar-app`, `spar-dev-loop` (the surface the evaluation keeps), `spar-testing` and `spar-shipping`. Not taken: one large skill; one per guide page.
4. **The guide reaches an app three ways:** the plugin in `plugins/spar/` with `.claude-plugin/marketplace.json` at the root, its version written from the SDK's at pack time; the same skills in the `Spar.Tool` package, which `spar skills` copies into the app's `.agents/skills/`, writing only on change; a short `AGENTS.md` in the template's app naming the commands and the plugin. Not taken: inside `Spar.Sdk` only; the plugin only.
5. **The evaluation uses dotnet/skills' `eval.yaml` format** (stimuli, deterministic graders, constraints; no LLM judge in the first run), run by Spar's own runner in `eng/eval/` (a C# file-based app) that drives `claude -p` with JSON output and writes a Markdown and JSON report under `artifacts/eval/`. Not taken: Vally; an LLM judge for every task.
6. **Ten tasks:** five on the Notebook (a seeded bug seen only on the page; a command and its button; a failing call explained from the logs; a CSP violation fixed; a layout change confirmed on a scene) and five on a new app from the template (a contract with a declared error handled on the page; a window test; a scene captured; telemetry turned on; a NativeAOT publish with its size reported), each graded by hidden checks run after the agent ends, never by the agent's own words. Not taken: an LLM judge.
7. **The runs happen in Spar's Linux CI image** with Node and Claude Code added, one container per run on a fresh copy of the task's app, no network but the model's API, the user's credentials passed by the user through the environment and never written to a file, a model the user picks (default the current Sonnet), at most 300k tokens and 20 minutes per run; Windows only for tasks the Linux host cannot do. Not taken: on the developer's desktop; in Windows Sandbox.
8. **The surviving surface:** the higher pass rate; within one task, fewer median tokens; within 10 % of those, the CLI. The loser's code, docs and tests are deleted in M2e, with every run's numbers recorded. Not taken: keeping both.
9. **Spar's own skills are evaluated in M4;** M2e evaluates the app-author skills, each on at least 5 stimuli with and without the skill. Not taken: every skill now.

## M2e: the agent guide, the plugin, the MCP adapter and the evaluation (implementation record; choices for review; 2026-11-26)

Built after decision "M2e's decide-first questions" (`docs/evidence/m2e-evaluation.md`, user guide `docs/guide/agents.md`, the devtools guide's "From an MCP client"). Choices made while building, for review:
1. **`spar mcp` runs the CLI's own commands in-process,** each tool's arguments turned into the command line the CLI takes (`McpTools.CommandLine`) and its output captured, rather than calling the app's socket itself: one code path for both surfaces, so they cannot drift; `screenshot` adds the PNG as an image block, and an exit code other than 0 or 1 marks the result as an error. `timeout` is a devtools argument only. A test fails if a devtools command has no tool.
2. **Every code block of a skill is an excerpt** of a sample, the template or a guide, named by a `<!-- source: path -->` marker and checked line by line (`Spar.Tool.Tests`, `Excerpts`), with `// …` for lines left out: the skills' examples are code Spar builds and tests, as decision item 3 asked ("compiled examples"), without a second copy to keep compiling. They describe only what `src/` has: native services, window kinds, streams and subscriptions are left out until built.
3. **`spar skills` writes to `.agents/skills/` beside the app's solution** (the app's folder without one), byte-compared, and removes only files inside the skill folders it ships. The plugin's `plugin.json` carries the repository's `VersionPrefix` (checked by a test); a release sets the full version. No licence field: the repository has none yet.
4. **The template's app gets `AGENTS.md`** (commands, where the skills come from, three rules), worded to fit an app made without end-to-end tests.
5. **The evaluation's runner** (`eng/eval/`): one container per run from the agent image, an internal network with a proxy to `api.anthropic.com` only, the user's credentials passed by name; tokens counted as input, output and cache writes, cache reads reported apart; `validate` proves each task's checks fail on its seed and pass with its reference, offline; `regrade` checks a run folder again from its diffs after a check is fixed, keeping the first result. The evaluation's Notebook is the sample without its browser journeys (no Chromium in the image) and Sandbox tests, with a `Directory.Build.props` for what the repository's gives it.
6. **A second round** of five runtime-only bug reports was added (beyond the decided 90 runs) after round 1 showed the tasks did not need the running app, at the user's request; from it the `dotnet` profile's feed has no `Spar.Tool`.
7. **Spar.Sdk finds Spar.Loopback in any restore folder** (`NuGetPackageFolders`), not only `NuGetPackageRoot`, found by the evaluation's offline containers; and **`spar snap`'s project search** treats a sibling folder that vanishes while it reads as empty (CI on Windows).

- **Tests:** `Spar.Tool.Tests` 148, three runs; `Spar.PackageTests` on Windows 43 of 44 (Aspire skipped); `Spar.BuildTests` 7 of 7; the tool's NativeAOT publish without warnings; `run.cs validate` 15 of 15 tasks; the evaluation 135 runs (`docs/evidence/m2e-evaluation.md`).
- **Found, not fixed here:** `spar tree` shows no text content, so a status message is invisible to an agent reading the tree; publishing a non-Production variant is not refused, though `docs/design/security.md` decides it.
- **Not verified:** the skills' effect (every profile passed alike), Windows agent runs, other agents than Claude Code, the plugin installed from the marketplace, CI on the last commits.

## Both devtools surfaces kept (user decision, 2026-11-26)

The evaluation (decision "M2e's decide-first questions", items 7 and 8) could not tell the surfaces apart: round 1, 90 runs, every profile 30 of 30 (after the pantry-notes regrade), median tokens 13,880 (dotnet), 19,014 (cli), 19,772 (mcp); round 2, 45 runs of runtime-only bug reports, every profile 15 of 15, 11,340 / 13,031 / 13,245; the devtools were used in four of fifteen tasks, and MCP tools only in round 1 (18 calls). The rule of item 8 gave the CLI by its tie-break. Asked whether both could stay, the user kept both: the CLI is the surface the skills teach, and `spar mcp` is opt-in, configured in an agent's MCP client and documented in the devtools guide.

- **Why both:** `spar mcp` is a thin adapter over the CLI's own commands (decision above, choice 1), so keeping it costs a dependency (`ModelContextProtocol.Core`, AOT-checked), a tool per new command (a test enforces it) and its tests; it serves agents without a shell, returns screenshots as images and lets a client allow tools one by one.
- **Why the CLI first:** it needs no configuration and costs an agent nothing until it runs a command, while an MCP client loads every tool's description into each session (about 4 % more tokens in round 1).
- **Not taken:** deleting the MCP server by the rule's tie-break; a third round with a larger app, where the devtools may matter more.
- This supersedes "exactly one interactive surface survives" (plan, M2; decision "M2's decide-first questions", item 2).

## The non-Production publish guard (implementation record; choices for review; 2026-11-26)

The M2e evaluation found that a non-Production publish was not refused (decision "M2e: the agent guide, the plugin, the MCP adapter and the evaluation", "Found, not fixed here"), though decision "2026-11-13, Security defaults and overrides" and `docs/design/security.md` require it. Built in `src/Spar.Sdk/Sdk/Sdk.targets`: the target `SparCheckPublishVariant` runs for an app (`SparIsApp`) whose `SparVariant` is not `Production`. It fails with SPAR140, naming the variant, the configuration, how to publish Production and the opt-out. With `SparAllowNonProductionPublish=true` it is a warning instead, and the warning starts "NOT A RELEASE BUILD". The same file serves source mode and packages.

Choices for review:

1. **SPAR140**, a new number for publishing checks, after the app id's SPAR130. One id for both the error and the warning, so a log search finds either.
2. **Hooked `BeforeTargets="PrepareForPublish"`.** It is the first of the .NET SDK's `_CorePublishTargets`, which all three publish paths run: after `Build`, with `NoBuild`, and the optimized NativeAOT path (`UseOptimizedPublish`, opt-in in .NET 11). A hook `BeforeTargets="Publish"` would run after the files were copied (pitfalls, "A `BeforeTargets` hook runs after the target's `DependsOnTargets`"). The check does not key on `_IsPublishing`. A test project's NativeAOT-lane publish passes that global property to its `SparTestedApplication` reference, which keeps it, so the lane would fail. Visual Studio's publish does not set it either. The cost: a refused publish builds the variant first (`Build` comes before `PrepareForPublish`). Nothing reaches the publish folder, and the tests check that the folder does not exist.
3. **Only apps are checked.** A test project's publish is its NativeAOT lane (plan section 5). It is test code by purpose and is never shipped, and its own Debug or Release variant decides nothing it carries. The app inside it is a project reference built in the Test variant with `Build`. The app's publish targets never run there, so the check never sees it. This answers D3's open question of how "publishing a non-Production variant is an error" applies to test variants (decision "2026-11-13, D3: variant builds run in-proc", "Not tested"). An app's Test variant, published directly (`-c Release -p:SparVariant=Test`), is refused like Development. A test project's lane publish is outside the rule.
4. **No internal route.** D3's evidence expected the SDK to need "an internal route that only variant builds may use". None of the SDK's own publishes is a non-Production publish of an app. The black-box publish (`SparBlackBoxPublish`) is the app's Production variant. The NativeAOT lane and the Windows Sandbox payloads publish test projects. `spar dev` and `spar snap` run `dotnet build -c Debug` and never publish.
5. **The opt-out stays a warning under warnings as errors.** When an app opts in and its variant is not Production, the SDK adds SPAR140 to `MSBuildWarningsNotAsErrors`. The template's app sets `MSBuildTreatWarningsAsErrors`, so without this the opt-out would still fail the publish. No other warning changes, and nothing is suppressed: the warning still prints. In this repository the build tests run the opt-out under `Directory.Build.props`' `MSBuildTreatWarningsAsErrors`.
6. **The error's advice** is "-c Release, with no SparVariant set", because an explicit `SparVariant=Test` with Release is refused too.

- **Tests:** `Spar.BuildTests`, `HarnessTests.Publishing_a_non_Production_variant_fails_unless_the_app_opts_in` (source mode). A Debug publish fails with SPAR140 and writes no folder; `-c Release -p:SparVariant=Test` fails the same way; the Debug publish with the opt-out passes, with the warning (CoreCLR, to keep it short). `Spar.PackageTests`, `PackageTests.A_Debug_publish_fails_unless_the_app_opts_in`, does the same for the template's app from packages. The Release publishes of the harness and of the package consumer now check that SPAR140 does not appear.
- **Checked by hand, Windows:** the harness's Debug publish (error), its Debug publish with the opt-out (warning, exit 0), Release with `SparVariant=Test` (error), and Release (no SPAR140). The SDK's own publishes: the Notebook's fast tests in the NativeAOT lane (`-c Release -p:SparTestLane=Aot`) published, and their executable passed 12 of 12; `Notebook.EndToEndTests` built with its black-box publish; `Spar.SandboxTests` built, publishing both its payloads. None reported SPAR140.
- **Verified, Windows:** `Spar.BuildTests` 8 of 8 (2 min 50 s); `Spar.PackageTests` 44 of 45 (3 min 50 s; the Aspire check is skipped on Windows outside CI), its NativeAOT lane and black-box journey included; the skills tests 12 of 12; `eng/check-size.cs` clean.
- **Not verified:** Linux (WSL) and CI; Visual Studio's Publish command, which is expected to reach `PrepareForPublish` like `dotnet publish` but was not run; the Windows Sandbox lanes themselves (only their payloads were published).
