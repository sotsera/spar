# Test framework spike: xUnit v3 vs TUnit — results

Recorded 2026-11-13 on the development machine: Windows 11 Pro 10.0.26320, 32 logical CPUs, SDK 11.0.100, `global.json` test runner Microsoft.Testing.Platform (MTP). Playwright for .NET 1.64.1 with the cached Chromium headless shell. Other agents and builds ran on the machine throughout; see [Noise](#noise).

## Versions

| Package | Version | Published | Notes |
|---|---|---|---|
| `xunit.v3` | 4.0.3 | 2026-11-09 | From the repository's `Directory.Packages.props`. In 4.0.3 it is only a wrapper: its sole dependency is `xunit.v3.mtp-v2` [4.0.3], so the root file's two entries resolve to the same thing. Pulls MTP 2.5.2. Reflection-based discovery. |
| `xunit.v3.aot` | 4.0.3 | same family | Spike-local. xUnit's Native AOT flavour: separate `*.aot` package family, source-generated registration, needs `XUNIT_AOT` for the assertion library, net9.0+ and C# only. Added because plain `xunit.v3` cannot be AOT-published (below). |
| `TUnit` | 1.84.0 | 2026-11-12 (one day old) | Latest stable on NuGet. Has `net11.0` assets in `TUnit`, `TUnit.Core`, `TUnit.Engine` and `TUnit.Assertions`. Runs on MTP 2.5.3 (`TUnit.Engine` depends on `Microsoft.Testing.Platform` 2.5.3). The `TUnit` metapackage also brings `Microsoft.Testing.Extensions.CodeCoverage` 18.12.0, `…Telemetry` and `…TrxReport`. Release cadence: 1.83.8 came out on 2026-11-11, the day before. |

## What was built

| Item | Contents |
|---|---|
| `Shared/` | Pure functions under test (`Calc`, `Primes`, `Roman`, `Slug`) and a concurrency probe. Linked into every suite. |
| `XunitSuite` | `xunit.v3`. 207 tests: 40 facts in 4 classes, a 60-row inline theory, an 80-row member-data theory, a 20-row two-argument theory, 3 async tests that use `TestContext.Current.CancellationToken` (one with `Timeout`), 1 statically skipped test, 1 output test, 2 browser journeys. Plus 8 parallelism probes (category `Probe`, excluded from timings). |
| `TUnitSuite` | The same suite written idiomatically for TUnit (`[Test]`, `[Arguments]`, `[MethodDataSource]`, `[Skip]`, `[Timeout]`, awaited `Assert.That`). Cancellation through `TestContext.Current!.Execution.CancellationToken` and through an injected `CancellationToken` parameter. |
| `XunitAotSuite` | `xunit.v3.aot`, compiling **the same source files** as `XunitSuite` (linked) with `XUNIT_AOT`. No source changes were needed. |
| Browser journeys | Two tests per suite (save a note and reload; stale-contract rejection) against `spikes/loopback`'s `Loop.App` using its `LoopbackApp.cs` (linked) and `AppPathMetadata.targets` (imported). The app is the existing Debug build; it is **not** a `ProjectReference`, because building it from here would write into `spikes/loopback`. |
| Switches | `-p:SpikeBrowser=false` leaves out Playwright and the journeys. `-p:SpikeScale=true` adds 2,000 more facts in 100 classes (`*/Scale/`). |
| `measure.ps1`, `summarize.ps1` | Interleaved timing harness and its summary. Raw data goes to `../../artifacts/test-frameworks/` (`timings.csv`, `aot.csv`, `scale.csv`). |

Every project builds with the repository's `Directory.Build.props` (`TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`) and `.editorconfig`, with **no suppressions or severity overrides**. All three suites pass: 215 tests, 214 passed, 1 skipped.

## Results (observed)

### Builds and `dotnet test` (207-test suite, 6 interleaved rounds, order rotated each round)

Milliseconds, wall clock: **median (min–max)**. "MTP" is the duration the platform prints in its summary.

| Step | xunit.v3 | TUnit | xunit.v3.aot |
|---|---:|---:|---:|
| Restore after deleting `bin`/`obj` | 458 (440–511) | 469 (449–1,523) | 467 (445–1,371) |
| Clean build (`--no-restore`) | 780 (721–899) | 905 (861–1,950) | 845 (813–858) |
| Incremental build (one test file touched) | 626 (597–716) | 735 (697–774) | 719 (689–743) |
| No-op build | 485 (447–1,130) | 474 (447–498) | 499 (455–550) |
| `dotnet test --no-build`, first run after the clean build | 6,497 (6,320–6,577) | 6,775 (6,645–6,828) | 6,379 (6,252–6,745) |
| … MTP duration | 5,699 (5,548–5,798) | 5,977 (5,855–6,022) | 5,601 (5,488–5,939) |
| `dotnet test --no-build`, warm (12 runs) | 4,049 (3,917–4,135) | 3,967 (3,899–4,050) | 3,990 (3,876–4,141) |
| … MTP duration | 3,235 (3,146–3,326) | 3,149 (3,096–3,253) | 3,160 (3,103–3,320) |
| Unit tests only (205, no browser), warm | 1,603 (1,575–1,636) | 1,458 (1,433–1,546) | 1,551 (1,483–1,584) |
| … MTP duration | 812 (794–822) | 667 (647–729) | 745 (722–775) |
| `dotnet test` with its implicit (no-op) build | 4,423 (4,325–4,568) | 4,390 (4,259–4,433) | 4,325 (4,267–4,415) |
| `dotnet test --no-build --list-tests` | 1,337 (1,288–1,382) | 1,297 (1,240–1,344) | 1,304 (1,248–1,347) |
| Test exe, list (own default runner, see note) | 262 (254–273) | 425 (397–569) | 227 (217–243) |
| Test exe, run all but probes (own default runner) | 2,967 (2,906–3,180) | 3,096 (3,068–4,232) | 2,899 (2,871–3,265) |

Notes:
- **The two browser journeys are about 2.4 s of every full run** (warm full run minus unit-only), for all three. At this size the frameworks are within ~5 % of each other; TUnit's unit-only run is ~145 ms faster than plain xUnit's.
- **The first run after a build is ~2.4 s slower for all three**, browser or not (the scale runs below show the same ~1–1.7 s penalty without browsers). It is the same for each framework, so it is not a framework cost. Likely cause (not verified): first load or scanning of freshly written binaries.
- **Running the exe directly is not the same UX.** An `xunit.v3` exe started without `dotnet test` uses xUnit's own console runner (`-list tests`, `-trait-`), not MTP; MTP options such as `--list-tests` are rejected ("unknown option"). `UseMicrosoftTestingPlatformRunner=true` would switch it (not tried). The TUnit exe is an MTP app. `-list tests` collapses theories (60 lines); MTP discovery reports all 215 tests for every suite.
- Discovery under `dotnet test` is ~1.3 s for every suite, mostly host start-up.

### At scale: 2,000 more facts (2,205 unit tests, 3 rounds; warm runs 5 rounds)

| Step | xunit.v3 | TUnit | xunit.v3.aot |
|---|---:|---:|---:|
| Clean build | 1,202 (1,152–2,039) | 2,548 (2,384–3,373) | 2,053 (2,033–2,071) |
| Incremental build | 1,108 (1,050–1,840) | 2,513 (2,451–3,200) | 1,975 (1,953–2,125) |
| `dotnet test --no-build`, first after build | 3,271 (3,250–3,273) | 5,849 (5,801–5,876) | 3,287 (3,259–3,314) |
| … MTP duration | 2,327 (2,324–2,344) | 4,899 (4,852–4,917) | 2,333 (2,290–2,338) |
| `dotnet test --no-build`, warm | 2,254 (2,187–2,495) | 4,152 (4,137–4,230) | 2,232 (2,190–2,298) |
| … MTP duration | 1,306 (1,253–1,460) | 3,205 (3,190–3,297) | 1,299 (1,256–1,340) |
| `--list-tests` via `dotnet test` | 1,932 (1,924–1,999) | 1,696 (1,691–1,700) | 1,878 (1,761–1,899) |
| NativeAOT publish (2 runs) | fails (below) | 83.3 s, 90.4 s | 31.8 s, 32.2 s |
| AOT exe size | — | 28,882,432 B | 18,793,472 B |
| AOT exe run, 2,205 tests (5 runs) | — | 355 ms (339–424); reports 136–152 ms | 318 ms (314–403); reports 76–87 ms |

- Under JIT, TUnit spends about **0.9 ms more per test** than xUnit (3.2 s vs 1.3 s for 2,205 trivial tests, warm). Turning off TUnit's HTML and JSON reports (`TUNIT_DISABLE_HTML_REPORTER`, `TUNIT_DISABLE_JSON_REPORT`) saved ~0.2 s; `--maximum-parallel-tests 4` changed nothing. xUnit with `--parallel all` ran them in 1.26 s, `--parallel none` in 1.64 s.
- TUnit's source generator costs ~1.3 s of build time per 2,000 tests over reflection xUnit; `xunit.v3.aot`'s generator ~0.85 s.
- Under NativeAOT both are fast; TUnit's publish is ~2.7× slower and its exe ~50 % larger at this size.

### Parallelism (8 probes that each wait 250 ms: 4 in one class, 4 in classes of their own)

| | xunit.v3 / xunit.v3.aot | TUnit |
|---|---|---|
| Default | Collections in parallel; **a class is a collection, so tests in one class run one after another.** Observed max concurrency 5 (4 own classes + 1 from the shared class), 1.93 s. Default thread limit = logical CPUs (32), algorithm "conservative". | **Every test in parallel**, including tests in one class. Observed max concurrency 8, 1.10 s. Default limit "auto-detected" (not measured beyond 8). |
| Limit from the command line | `--max-threads 2` → max 2. `--parallel none|collections|all`; `--parallel all` → max 8, 1.0 s. | `--maximum-parallel-tests 2` → max 2 (also `TUNIT_MAX_PARALLEL_TESTS`). |
| Serialise in code | Put the tests in one class (what the journeys do), or `[Collection(..., DisableParallelization = true)]`; 4.0 adds `[assembly: Parallelization(Mode, MaxThreads, Algorithm)]` and per-level opt-outs. | `[NotInParallel("key")]` (what the journeys do), `[ParallelGroup]`, `[ParallelLimiter<T>]`. **Forgetting it runs browser/process tests concurrently.** |

### NativeAOT publication (`-c Release -p:PublishAot=true`, repository warnings as errors)

| | xunit.v3 | TUnit | xunit.v3.aot |
|---|---|---|---|
| With the browser journeys | **Fails**: 97 trim/AOT errors in xUnit's own assemblies (`xunit.v3.core`, `.common`, `.runner.common`, `.runner.inproc.console`: IL2026, IL2055, IL2057, IL2060, IL2065, IL2067, IL2070, IL2072, IL2075, IL2077, IL2080, IL3000 ×8, IL3050 ×15; counted with `TrimmerSingleWarn=false`). Playwright never appears, probably because reflection discovery leaves the test methods unreachable for the AOT compiler (inference: a suppressed build would then contain no tests; not tried). | **Fails** only on Playwright: 44 errors in `Microsoft.Playwright` (IL2026 ×12, IL3050 ×14, IL2075 ×6, IL2070 ×4, IL3000 ×2, IL3002, …: reflection-based `System.Text.Json`, `Assembly.Location`). | **Fails** only on Playwright, same 44 errors. |
| Without the journeys (`-p:SpikeBrowser=false`) | **Fails**, same xUnit errors. | Succeeds, **0 warnings**. | Succeeds, **0 warnings**. |
| Publish time, 3 interleaved rounds | — | clean 29.4 s (28.6–30.6), incremental 26.2 s (25.3–27.1) | clean 23.2 s (22.5–24.9), incremental 18.6 s (17.7–18.8) |
| Publish folder (no `.pdb`) | — | `TUnitSuite.exe` 21,591,040 B **plus 11 code-coverage files, 11,125,144 B** (instrumentation engines for x86/x64/arm64, `msdia140.dll`) | `XunitAotSuite.exe` 16,223,232 B only |
| AOT exe run, 205 tests, 6 runs | 251 ms (240–253); reports 36–47 ms | — | 274 ms (271–348); reports 62–70 ms |
| AOT exe `--list-tests` / `-list tests` | 210 ms (203–217) | — | 203 ms (201–210) |
| Default exe UX | MTP | — | xUnit native console runner |

**Playwright under AOT (diagnostic only).** To learn whether the journeys could work at all, TUnit was published once with `-p:TreatWarningsAsErrors=false` on the command line into `obj/` (not a project change; the output was deleted afterwards). Both journeys failed at `Playwright.CreateAsync` with a `NullReferenceException` in `Connection.WrapApiCallAsync`. The loopback app itself started fine. **Playwright's .NET client cannot run in a NativeAOT test exe, whichever framework is used**; browser tests have to run on CoreCLR.

A side finding: `AppPathMetadata.targets` stores the app path relative to `$(OutDir)`, so it breaks in any publish folder at a different depth (first diagnostic run: "cannot find the file"). A framework launcher needs to resolve the app another way for published tests.

### Output, logs, skips and failures under MTP

| | xunit.v3 | TUnit |
|---|---|---|
| Passing test's output, default `dotnet test` | Not shown. | Not shown. |
| … with `--output Detailed` | `ITestOutputHelper` lines under "Standard output" for that test. | `TestContext.Output.StandardOutput`, `Console.WriteLine` and `GetDefaultLogger()` lines (prefixed `Information:`) under "Standard output" for that test. |
| `Console.WriteLine` | **Not captured by default**: printed raw into the run's stdout with no test attribution (seen in every run). With `[assembly: CaptureConsole]` it is attributed to the test like `ITestOutputHelper`. | Captured per test by default. |
| Other channels | `TestContext.Current.SendDiagnosticMessage` did not appear at all (needs diagnostic messages enabled; not pursued). `--show-live-output` streams `ITestOutputHelper`. | Logger has levels; `--log-level` filters. |
| Files written by default | None (an empty `TestResults` folder in the working directory). | **An HTML report and a JSON report in `TestResults/` of the current working directory on every run** (~650 KB per run for 215 tests), also next to a published exe. Off with `TUNIT_DISABLE_HTML_REPORTER` / `TUNIT_DISABLE_JSON_REPORT`, or redirect with `--results-directory`. This conflicts with "generated files live under `obj/`" unless the framework sets it. |
| Agent-environment detection | MTP 2.5 says it defaults `--show-stdout/--show-stderr` to "Failed" when it detects an AI agent. With and without the agent's environment variables (stdout redirected in both cases) the output above was identical. A real interactive terminal was not tried. | Same. |
| Skip | `[Fact(Skip = "…")]`: listed as `skipped` with the reason, exit code 0. `Assert.Skip`, `SkipUnless`/`SkipWhen` for dynamic skips; `--fail-skips` turns skips into failures. | `[Skip("…")]`: listed as `skipped` with the reason, exit code 0. `Skip.Test/When/Unless` for dynamic skips; `--zero-tests-policy strict` fails an all-skipped run. |
| Deliberate failure, JIT (two tests: string and 4-item array, reverted afterwards) | `Assert.Equal() Failure: Strings differ` with ↓/↑ markers at **pos 17**; for the array, "Collections differ **at index 2**" plus the item diff with markers at pos 4. Stack has file:line plus 2 reflection-invoke frames. Exit code 2. | `Expected to be equal to "…" but received "…"` (no position marker); the assertion expression is echoed (`at Assert.That(…).IsEqualTo(…)`). Array with `IsEquivalentTo`: "collection does not contain expected item: gamma" (no index). Message printed twice (summary line and exception). File:line, TUnit frames hidden. Exit code 2. |
| Deliberate failure, NativeAOT | Same diff messages ("Collections differ", markers at pos 2), **no stack frames at all** (documented AOT limitation). | Same messages; with an ordinal comparer and `CollectionOrdering.Matching`, "item at index 2 does not match: expected gamma, but was gamme". Method frame shown, no file:line. |

### Central package management, analyzers and MSBuild

| | xunit.v3 / xunit.v3.aot | TUnit |
|---|---|---|
| CPM | Works with the spike's `Directory.Packages.props` that imports the root one. No NU warnings. | Same. No NU warnings. |
| Analyzers under warnings-as-errors | Compiled clean first time. Probe: `Task.Delay(1)` without the token → **xUnit1051 error**; constant as `actual` → **xUnit2000 error**. Both useful. | One error on first build: **TUnitAssertions0005** (`Assert.That(true)` with a constant) — fair. Probe: un-awaited `Assert.That` → **TUnitAssertions0002** + CS4014; ignored injected token → CA2016 (repository rule). Every assertion is `async` and must be awaited. |
| AOT-unsafe test code | Not flagged by a normal build; `xunit.v3` fails only at publish, inside xUnit. | `IsEquivalentTo` without a comparer is `[RequiresUnreferencedCode]`: **passes a normal build, fails AOT publish** (IL2026). Building with `-p:EnableAotAnalyzer=true -p:EnableTrimAnalyzer=true` surfaces it in a normal build. |
| Properties the package sets | `OutputType Exe`, `IsTestingPlatformApplication`. | `OutputType Exe`, `IsTestProject`, `GenerateProgramFile=false`, `GenerateAssemblyInfo=true`, `TestingPlatformCaptureOutput=false`, and **`LangVersion=latest` when the project does not set it**. Adds global usings `TUnit.Core` and static `TUnit.Core.HookType`. |
| Extras in the box | MTP telemetry extension (both frameworks). | Code coverage (native files in output and publish), TRX, HTML and JSON reports, a GitHub reporter and artifact upload (`TUNIT_DISABLE_GITHUB_REPORTER`, `TUNIT_DISABLE_ARTIFACT_UPLOAD`; CI behaviour not tested). Referencing `TUnit.Engine` + `TUnit.Assertions` instead of the metapackage should avoid coverage (not tried). |

## Noise

- The harness interleaved the suites and rotated their order each round. Load from other agents was visible: 14 `dotnet`, 7 `MSBuild` and 2 `VBCSCompiler` processes during the run, average CPU load 2 % at the sample taken.
- Apart from single round-1 outliers (TUnit clean build 1,950 ms, restores of 1.4–1.5 s, one no-op build of 1.1 s), the spread of each step is within about ±5 % of its median. Differences of under ~100 ms in the small-suite table are not meaningful.
- The compiler server and MSBuild nodes were warm throughout (no `build-server shutdown`), so build times are inner-loop times, not CI cold builds.
- The scale runs of AOT publication have only 2 samples each.

## Not tested or skipped

- **Real terminal UX**: all output was redirected; MTP's interactive progress and its human-vs-agent defaults in a TTY were not observed.
- **IDE behaviour** (Visual Studio / VS Code test explorers, debugging, Hot Reload of tests): not run.
- **Edge (`msedge`) journeys**: only Chromium headless shell was used.
- **xUnit with `UseMicrosoftTestingPlatformRunner=true`**, running the published xUnit exe through MTP, and `dotnet test` against published AOT exes: not tried.
- **Linux and CI runners**, TUnit's GitHub reporter and artifact upload: not run.
- **TUnit without the metapackage** (no code coverage) and TUnit's reflection mode (`--reflection`, `EnableTUnitSourceGeneration=false`): not measured.
- **TUnit's default parallel limit** above 8 concurrent tests: not measured.
- **Cause of the first-run penalty** after each build: not investigated.
- **Playwright under AOT** was checked only once, with warnings allowed on the command line; no suppression was added to any project.
- **xUnit's shared fixtures, TUnit's `ClassDataSource`/dependency injection, hooks, retries**: not exercised.

## Findings for the plan

1. **At Spar's expected sizes, speed does not decide it.** For ~200 tests plus two journeys, all three are within ~5 %; the browser journeys and host start-up dominate. At 2,000+ tests under JIT, xUnit is ~2.5× faster per run and builds ~1.3 s faster.
2. **"xUnit 4 supports NativeAOT" means a different package.** Plain `xunit.v3` (the chosen package) is reflection-based and cannot be AOT-published under warnings-as-errors (97 errors inside xUnit). `xunit.v3.aot` 4.0.3 published with zero warnings from the same unchanged test source. Choosing `xunit.v3` keeps the AOT path open as a package swap plus `XUNIT_AOT`, within its documented limits (no generic test methods, no `params`, no stack traces when published, some APIs obsolete).
3. **TUnit is AOT-ready by default, but costs more when AOT-published**: ~2.7× publish time and +50 % exe at 2,200 tests, plus 11 MB of coverage binaries unless the metapackage is avoided, and some assertions (`IsEquivalentTo` without a comparer) are not AOT-safe.
4. **Browser tests cannot be NativeAOT with Playwright for .NET 1.64.1, whatever the framework** (44 trim/AOT errors; fails at runtime when forced). Any AOT test exe must leave browser journeys out; they run on CoreCLR.
5. **Default parallelism suits xUnit better for Spar's process- and browser-driven tests.** xUnit serialises a class by default; TUnit parallelises everything, so every app-launching test needs `[NotInParallel]` or a limiter.
6. **Output**: TUnit captures `Console` per test by default; xUnit needs `[assembly: CaptureConsole]` (verified). Under MTP both hide passing output unless `--output Detailed`.
7. **Failure messages**: xUnit's are more precise (position markers, collection index); TUnit's echo the assertion expression.
8. **Repository hygiene**: TUnit writes HTML and JSON reports into `TestResults/` of the working directory on every run; Spar would need to turn them off or redirect them. TUnit also sets `LangVersion=latest` when unset.
9. **Maturity**: TUnit 1.84.0 was one day old and 1.83.8 two days old; `xunit.v3` 4.0.3 (2026-11-09) is a patch release of the 4.0 line.
