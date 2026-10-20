# Spar

Spar (`Sotsera.Spar`) is a Windows desktop framework being built from scratch around a C# backend and a framework-neutral web SPA, inspired by Tauri.

The priority is an excellent development experience: SPA HMR, supported C# Hot Reload, coordinated application restarts, and inspection and control for AI agents. VS Code comes first, with Visual Studio support alongside it.

Production targets NativeAOT by default, with CoreCLR compatibility for applications that add AOT-incompatible dependencies. Development uses CoreCLR for debugging and Hot Reload.

## Current status

The same React sample publishes as NativeAOT with embedded production assets and matching generated contracts. A copied application renders and calls C# without a frontend server, with production inspection/control exclusion checked. Explicit, attributed CoreCLR compatibility supports application dependencies that need runtime code generation, while strict AOT remains available. See the [production guide](docs/16-production-delivery.md) and [compatibility guide](docs/17-dependency-compatibility.md).

Local packages build and run an external consumer through public APIs, including frontend/backend updates, coordinated restart, default AOT and explicit CoreCLR publication. The [consumer guide](docs/18-package-consumer.md) has executed VS Code and Visual Studio walkthroughs. [Milestone 1 acceptance](docs/19-milestone-1-acceptance.md) now links evidence for A01–A18 and records the remaining clean-machine and distribution-readiness work. Local packages are not a public release.

The native sample runs React/Vite with generated, permission-checked C# calls in an owned development session. The terminal/AI watch lane and the local VS Code adapter apply supported backend edits in place and refresh read data while preserving state. Coordinated restart retains Vite, waits for actual retirement, and builds current sources; corrections recover stopped sessions automatically. Both installed debugger lanes have native breakpoint, Hot Reload, diagnostic recovery, relaunch, rendering, and Stop evidence. Visual Studio rebuilds and relaunches the whole session for restart-required edits. See the [backend update guide](docs/12-backend-updates.md), [VS Code debugger guide](docs/13-vscode-development.md), and [milestone status](docs/03-milestone-1.md).

## Run the first slice

For the SPA development loop, install the pinned .NET SDK `10.0.402`, Node `24.21.0` / npm `11.19.0`, and WebView2 Runtime. Prepare dependencies, then start the coordinator:

```powershell
dotnet build tools/Spar.Dev --disable-build-servers
Push-Location samples/Spar.NativeSample/frontend
npm ci --ignore-scripts --include=optional
Pop-Location
./tools/Spar.Dev/bin/Debug/net10.0-windows/Spar.Dev.exe samples/Spar.NativeSample/spar.dev.json
```

Edit `App.tsx`/`styles.css` in the frontend or the greeting in `SampleBackend.cs`. Type `stop` or press Ctrl+C to end the session. VS Code provides **Tasks: Run Task → Spar: development session**. See the [backend update guide](docs/12-backend-updates.md) for controls, failed-build recovery, and current limits.

For native F5 debugging, load the local adapter using the [VS Code guide](docs/13-vscode-development.md), then select **Spar: native development (local adapter)**. Use **Spar: Restart backend** for coordinated restart; C# Dev Kit is the only Hot Reload owner in this lane.

The [Visual Studio guide](docs/14-visual-studio-development.md) provides an ordinary **Spar development** project launch profile and an isolated manual walkthrough. The native acceptance pass covers F5/breakpoints, frontend HMR, backend Hot Reload, compiler recovery, whole-session relaunch, and debugger Stop with process cleanup. Visual Studio owns backend updates; restart-required edits use close, rebuild, and F5.

For agent-driven verification, start the coordinator with `--owner-stdin --inspect`. The [inspection guide](docs/15-development-inspection.md) documents structured status, DOM snapshots, native viewport screenshots, bounded click/type, console/errors, build diagnostics, restart, and Stop. Inspection requires a Debug host and explicit opt-in; requests bind to the current session/generation/document/contract, and timeouts retain unfinished work. The native acceptance scenario verifies edits and cleanup through these generic operations.

For the precursor probes and full managed tests:

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

The sample uses CoreCLR for Debug and NativeAOT with the built React SPA for Release publishing. See the [production guide](docs/16-production-delivery.md) for prerequisites, copied-payload checks, receipts, and limits. The precursor native/bridge probes require a separate `-p:SparNativeProbe=true` Release test build.

The [contract file](samples/Spar.NativeSample.Contracts/BackendContract.cs) generates the C# adapter, JSON metadata, and framework-neutral JavaScript/TypeScript bindings during the build. Its separate assembly keeps wire metadata outside implementation Hot Reload. See the [bridge guide](docs/10-typed-bridge.md) for the supported DTO surface and actual WebView checks.

The tests edit disposable copies under `artifacts/` and launch real `dotnet watch` processes. The [editor probe guide](docs/07-managed-probe.md) covers VS Code automation, F5 setup, and the recorded Visual Studio manual pass. These are development experiments, not shipping Spar packages.

## Start here

- [Product direction](docs/02-product-direction.md) — goals, architecture, and open decisions.
- [First milestone](docs/03-milestone-1.md) — implementation sequence and acceptance criteria.
- [Implementation kickoff](docs/05-implementation-kickoff.md) — first experiments, baseline choices, and topics to study.
- [Milestone issues](docs/04-milestone-1-issues.md) — upstream tracking issue, implementation issues, and dependencies.
- [Reference projects and licenses](docs/01-reference-projects.md) — Tauri, CodeAlta, NeoAstra, Reactor, and previous Spar.
- [Agent guidance](AGENTS.md) — working rules for this repository.
- [Repository skills](docs/20-agent-skills.md) — focused testing/MSBuild workflows, pinned provenance, and validation limits.
