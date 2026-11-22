# Working on Spar

Read [the plan](docs/plan.md) for scope and the current milestone, the [design file](docs/design/) for the area you are touching, and [decisions](docs/decisions.md) before changing anything a decision covers. Section numbers such as "plan section 3c" are global; the plan's "Design" table says which file holds each. Check [known pitfalls](docs/pitfalls.md) for the area you are touching, and add new ones you find. Load only the sections the task needs.

## Boundaries

- Write code from scratch. Earlier Spar versions and other frameworks are research input, not sources to copy. Turn useful findings into Spar requirements, then implement them independently from official platform documentation.
- Target .NET 11 (`global.json`). Treat NativeAOT and trimming warnings as errors, with no suppressions unless the user agrees to one. Production code takes no reflection fallback. Switching an app to CoreCLR is a publishing decision, never a workaround for an AOT warning.
- Agree each analyzer severity override with the user (diagnostic ID, severity, scope, reason) before adding it. Record agreed overrides, with their reasons, in the analyzer section of the root `.editorconfig`. Overrides for some projects or folders only, such as tests, are path-scoped sections there, never nested `.editorconfig` files or `#pragma` in source. MSBuild, SDK and NuGet warnings fail the build outside `spikes/`; an exception is agreed the same way, ID by ID, and listed with its reason in `MSBuildWarningsNotAsErrors`.
- Keep the consumer model: one user app project, one fast test project, and one optional end-to-end test project (browser and window tests, the black-box executable, containers). Tests run the app's own `Program.cs` inside the test process (plan section 5). A framework feature must not need application-specific branches. Settings that one app or one of its dependencies needs, such as SqlClient's `InvariantGlobalization=false`, belong in that app's project, never in Spar's defaults.
- Keep C# and TypeScript source files under 800 lines, with 1,500 as the hard limit, and folders under 40 source files. `dotnet run eng/check-size.cs` enforces this outside `spikes/`. An exception is a line in `eng/size-exceptions.txt`, agreed with the user, and its ceiling only goes down.
- Generated files live under `obj/`, never in authored folders. The one exception is the committed contract snapshot, `<Project>.contracts.snapshot` (plan section 2). Write outputs only when their content changes.
- Runnable output and ordinary publications go in each project's `bin/`. Generated files, caches and private publish staging go in its `obj/`. The root `artifacts/` is only for web-engine profiles, test data roots and test results (`artifacts/test-results/`), and for outputs deliberately collected for inspection or transfer; everything there must be disposable.
- Production builds contain no test, development, loopback-host or devtools code. Prove this with a test, not by inspection.
- Never block the window's UI thread (the WebView2 STA, the GLib main loop), and keep the work on it as short as possible, so the window stays responsive. Backend commands, I/O, synchronous waits and long CPU work run elsewhere (plan section 3).
- Cancellation or a timeout does not prove work stopped. Never replay a backend command automatically.
- In this repository (tests, spikes, local runs), keep WebView2 user-data folders under `artifacts/webview2-profiles/` and pass them explicitly. Never use `%LOCALAPPDATA%` for them. On Linux, keep WebKitGTK data and cache folders, and each test run's private `HOME` and `XDG_*` folders, under `artifacts/linux-homes/<run>/`. Shipped apps use a per-user app folder instead (plan section 3c).
- Tests and spikes leave no per-user system registrations on the developer's machine, such as notification-area icon entries. Use Windows Sandbox, a container or a fake. When a real registration cannot be avoided, use a fixed path under `artifacts/` and remove only the entries that point inside this repository (see the pitfalls).
- Security follows the three levels in plan section 3c: always-on checks, strict defaults with an explicit C# override, and page-only limits. Anything that is a security risk is blocked by default; an app unblocks it only through an explicit option whose documentation explains the risk. Don't weaken a default without a visible override.

## Evidence

- Verify behaviour by running it: native windows, AOT publication and IDE behaviour need real execution.
- A check that needs the user present, sound, a camera or microphone, or a change to the user's settings is never run unattended. Add it to the plan's table "Checks that need the user at the PC".
- Real input (mouse, keyboard, drag and drop, focus) may be simulated or driven in Windows Sandbox, a Linux container or VM, or WSL. On the user's own desktop, through computer use or injected input, first ask whether they are using the monitor, and go ahead only after they say no.
- Report observed passes, skipped checks and untested scenarios separately. Earlier results do not verify a new change.
- Record each decision, with the numbers behind it, in `docs/decisions.md`. Append; do not rewrite history.

## Documentation

- Make each user-facing document (README, guides, package docs) understandable on its own. Cite official Microsoft documentation where an external reference adds needed detail. Do not name or compare other frameworks or earlier Spar versions there. Skills, code comments and file names name no other framework either. "Framework" here means other desktop and application frameworks: the UI libraries an app's frontend may use, such as React, Vue or Angular, may be named.
- User-facing pages are plain Markdown in this repository; the documentation site (Astro Starlight) is built from them. Their examples compile, like those in `docs/app-model.md`.
- `docs/app-model.md` shows, with compiled examples, how an app is written. Keep it in step with the decisions: a change to the contract, client or configuration design updates it and its check in the same change. The check is `tests/AppModel`, run by `dotnet test` through `tests/AppModel.Tests` (it needs `npm ci --ignore-scripts` in `tests/AppModel` once): every block against stand-ins, and the C# blocks Spar can build also through real Spar (`tests/AppModel/Real`, with stand-ins in `tests/AppModel/Planned` only for what is not built yet). When Spar gains an API the planned stand-ins cover, remove its file from `Planned`, and when it can build a left-out contract, take it out of `Real`'s exclusions.
- `docs/plan.md`, `docs/design/`, `docs/decisions.md`, `docs/app-model.md`, `docs/research-review.md`, `docs/pitfalls.md` and the spike evidence (`spikes/`, `docs/evidence/`) are design records and are exempt. The history and comparisons in them are evidence for the decisions.

## Project skills

`.claude/skills/` holds Spar-specific guidance for working on Spar itself:

| Skill | Use for |
|---|---|
| `spar-csharp-conventions` | Writing or reviewing C# |
| `spar-typescript-conventions` | Writing TypeScript and JavaScript, and the shape of the generated clients |
| `spar-msbuild` | Project files, props and targets, build variants, output locations |
| `spar-contract-generation` | Contract analysis, generated dispatch and serializers, the TypeScript client |
| `spar-testing` | The test host, test projects and lanes, and the C# side of dispatch that tests drive |
| `spar-native-interop` | Win32 and COM declarations, layouts, callbacks, handle ownership |
| `spar-webview2-hosting` | The Windows WebView2 host, STA work, the bridge, devtools access, shutdown |
| `spar-webkitgtk-hosting` | The Linux WebKitGTK host, GLib main-loop work, the bridge, devtools access, shutdown |
| `spar-page-security` | The page's policy declared in C# (CSP, permissions), its analyzer, the headers, the navigation lock, the host-injected script, violation reports, SRI and Trusted Types |

Each M1 slice adds the skill its work needs (plan, "M1"): testing and the C# side of the bridge in M1a (`spar-testing`), page security in M1b (`spar-page-security`), the development loop in M1d. Performance and Windows Sandbox skills come with M4. Skills for app authors are not kept here: they go in the `plugins/spar/` plugin.

## Agent plugins

`.claude/settings.json` enables plugins from the [dotnet/skills](https://github.com/dotnet/skills) marketplace: `dotnet-msbuild` (including its binlog MCP server), `dotnet`, `dotnet11`, `dotnet-upgrade`, `dotnet-advanced` and `dotnet-test`.

- Use their skills for MSBuild targets and incremental builds, AOT compatibility, P/Invoke and COM, .NET 11 APIs, and running tests.
- They give advice, not decisions. Where a skill conflicts with `docs/decisions.md`, the decision log wins.
- Keep Spar-specific guidance in this repository rather than copying or editing those skills.

## Layout

| Path | Contents |
|---|---|
| `src/` | Framework projects: `Spar` (runtime and bridge), `Spar.Generator` (generator and analyzer), `Spar.Sdk.Tasks` (the build steps as MSBuild tasks: the client step), `Spar.Loopback` (the loopback host, development and test variants only), `Spar.Windows` (the Windows host over WebView2), `Spar.Linux` (the Linux host over WebKitGTK 6.0 and GTK 4), `Spar.Testing` (the test host, the Window surface with its page API, the black-box fixture with its UI Automation and AT-SPI clients, and the internal Windows Sandbox runner with real input for its guest) and `Spar.Testing.Browser` (the Browser surface, for end-to-end projects), `Spar.Sdk` (props and targets), `Spar.Client` (the TypeScript SDK, an npm project), and `Spar.Templates` (the `dotnet new spar-app` template; today its `content/spar-app/` only, the app and its two test projects with `MyApp` as the name, which `Spar.PackageTests` copies into its consumer; the package and `template.json` come in M1d step 6) |
| `samples/` | Polished sample apps and their tests (`Notebook`, its `frontend/` with its page tests against `@spar/app/testing`, `Notebook.Tests`, and the browser and window journeys and black-box tests in `Notebook.EndToEndTests`); like the test projects and test apps under `tests/`, they write `<Project Sdk="Spar.Sdk">`, which here is the `0.0.0-source` package of `eng/source-sdk/` (source mode) |
| `tests/` | Framework tests: `Spar.Tests` (runtime and bridge), `Spar.Generator.Tests` (in-memory compilation), `Spar.Sdk.Tasks.Tests` (the client step and the generated TypeScript), `WireShapes.Tests` and the browser tests `WireShapes.EndToEndTests` with their app, its page and the library whose contracts it serves (`WireShapes.Library`, a class contract and an interface contract the app implements) under `tests/apps/`, the window tests `Spar.WindowTests` (both hosts and the page API, in real windows, the black-box fixture's own tests on its Production executable, and in `Linux/` the Linux host's tests that run an app as a process of its own, through the black box) with their apps `tests/apps/WindowHost` and `tests/apps/BridgeProbe`, the console harness `tests/apps/Spar.Harness` (the core as a Production NativeAOT executable, run by `Spar.BuildTests`), the Linux host's unit tests in `Spar.Tests` (its tests in real windows run in WSL with `docs/evidence/m1c-blackbox/wsl.sh`), the shared vectors in `tests/conformance/`, the static-state test and its reviewed list in `tests/static-state/`, the documentation check of `docs/app-model.md` in `tests/AppModel` (run by `tests/AppModel.Tests`), and `Spar.BuildTests` (a clean copy of the tree built in isolation; in `Spar.slnx` but not built with it, run with `dotnet test --project tests/Spar.BuildTests`), and `Spar.PackageTests` (Spar packed into a feed of its own and used by an app in a temporary folder outside the repository, by a second one whose solution manages package versions centrally, and by a third that serves the contract of a library it gets as a package; in `Spar.slnx` but not built with it, run with `dotnet test --project tests/Spar.PackageTests` after the TypeScript SDK's `npm run build`), and `Spar.SandboxTests` (the Windows Sandbox runner's suite: `Spar.WindowTests` and `Notebook.EndToEndTests` published with NativeAOT and their `Sandbox`-tagged tests run in one sandbox, each reported as a test; in `Spar.slnx` but not built with it, run with `dotnet test --project tests/Spar.SandboxTests`; its user-session lane only with `SPAR_SANDBOX_DESKTOP=allow`, which agents set only after asking the user) |
| `eng/` | Repository checks: `check-size.cs` and its exceptions file; `source-sdk/`, the Spar.Sdk `0.0.0-source` package that forwards to `src/Spar.Sdk/Sdk/`, pinned in `global.json` and served by the root `nuget.config` (`pack.cs` rebuilds it; a test checks it is current); `wsl/`, the CI Linux lane run in WSL on a fresh clone (`setup.sh`, then `step.sh` per step; `docs/evidence/m1a-linux.md`); `linux-ci/`, the Linux CI image's Dockerfile, the steps CI runs in it (`run.sh`) and the network switch of the online/offline test (`network-switch.sh`; `docs/evidence/m1c-ci-image.md`); `editor-diagnostics.cs`, the editor check through VS Code's C# language server, run from a copy outside the repository (`docs/evidence/m1a-editor-diagnostics.md`) |
| `.github/workflows/` | CI on GitHub Actions (decision "CI on GitHub Actions; the first slice"); not run until the first push |
| `plugins/spar/` | The app-author skills plugin, versioned and released with the SDK; its marketplace file is at the root (not created yet) |
| `spikes/` | Throwaway M0 experiments, versioned in git without their generated folders. Each has a `RESULTS.md`. A new spike keeps its raw evidence in its own `results/`, never only in `obj/`, `bin/` or `artifacts/`. The M0 spikes' raw logs and screenshots under the root `artifacts/` are the agreed exception: they are disposable, and each `RESULTS.md` is the record. Before deleting a spike, move its `RESULTS.md` and `results/` to `docs/evidence/<name>/` and update the links. Check which other spikes build on it first. |
