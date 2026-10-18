# Spar

Spar (`Sotsera.Spar`) is a Windows desktop framework being built from scratch around a C# backend and a framework-neutral web SPA, inspired by Tauri.

The priority is an excellent development experience: SPA HMR, supported C# Hot Reload, coordinated application restarts, and inspection and control for AI agents. VS Code comes first, with Visual Studio support alongside it.

Production targets NativeAOT by default, with CoreCLR compatibility for applications that add AOT-incompatible dependencies. Development uses CoreCLR for debugging and Hot Reload.

## Current status

The managed Hot Reload probe works under the watcher, VS Code, and Visual Studio. The Win32/WebView2 sample now calls a C# backend through generated, permission-checked contracts under CoreCLR and a copied NativeAOT executable. Document retirement retains unfinished work and suppresses stale replies. SPA-server coordination comes next. See the [typed bridge guide](docs/10-typed-bridge.md), [native host](docs/09-native-host.md), and [managed probe results](docs/07-managed-probe.md); no full milestone acceptance scenario is complete yet.

## Run the first slice

Install SDK `10.0.402`, selected in `global.json`, then run from this checkout:

```powershell
dotnet build Spar.slnx --disable-build-servers
dotnet test --solution Spar.slnx --no-build
dotnet watch --project experiments/Spar.HotReloadProbe run
```

For the first native window (requires the installed WebView2 Runtime):

```powershell
dotnet run --project samples/Spar.NativeSample
dotnet publish samples/Spar.NativeSample -c Release -o artifacts/native/publish --disable-build-servers
```

The sample uses CoreCLR for Debug and NativeAOT for Release publishing. See the [native host guide](docs/09-native-host.md) for C++ prerequisites, copied-payload checks, and current limits.

The [contract file](samples/Spar.NativeSample/BackendContract.cs) generates the C# adapter, JSON metadata, and framework-neutral JavaScript/TypeScript bindings during the build. See the [bridge guide](docs/10-typed-bridge.md) for the supported DTO surface and actual WebView checks.

The tests edit disposable copies under `artifacts/` and launch real `dotnet watch` processes. The [editor probe guide](docs/07-managed-probe.md) covers VS Code automation, F5 setup, and the recorded Visual Studio manual pass. These are development experiments, not shipping Spar packages.

## Start here

- [Product direction](docs/02-product-direction.md) — goals, architecture, and open decisions.
- [First milestone](docs/03-milestone-1.md) — implementation sequence and acceptance criteria.
- [Implementation kickoff](docs/05-implementation-kickoff.md) — first experiments, baseline choices, and topics to study.
- [Milestone issues](docs/04-milestone-1-issues.md) — upstream tracking issue, implementation issues, and dependencies.
- [Reference projects and licenses](docs/01-reference-projects.md) — Tauri, CodeAlta, NeoAstra, Reactor, and previous Spar.
- [Agent guidance](AGENTS.md) — working rules for this repository.
