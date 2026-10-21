# Spar

Spar (`Sotsera.Spar`) is a Windows desktop framework built around a C# backend and a framework-neutral web SPA. It prioritizes SPA HMR, supported C# Hot Reload, coordinated restarts, and development controls usable by editors and AI tools.

Development uses CoreCLR. Production defaults to NativeAOT, with explicit, attributed CoreCLR compatibility for application dependencies that require it.

## Run the application

The ordinary application is [samples/Spar.NativeSample](samples/Spar.NativeSample/Spar.NativeSample.csproj). Its [contract](samples/Spar.NativeSample.Contracts/BackendContract.cs) exports greeting and counter operations; its React/Vite frontend calls generated bindings. It contains no failure switches, cancellation-ignoring operations, probe commands, or test reports.

On Windows x64, install SDK `10.0.402`, Node `24.21.0` / npm `11.19.0`, PowerShell 7.4 or newer, and WebView2 Runtime. From the checkout:

```powershell
dotnet build tools/Spar.Dev --disable-build-servers
Push-Location samples/Spar.NativeSample/frontend
npm ci --ignore-scripts --include=optional
Pop-Location
./tools/Spar.Dev/bin/Debug/net10.0-windows/Spar.Dev.exe samples/Spar.NativeSample/spar.dev.json
```

Edit `frontend/src/App.tsx`, `frontend/src/styles.css`, or the greeting in `SampleBackend.cs`. Use `restart` for a coordinated rebuild/restart, and `stop` or Ctrl+C to end the session. VS Code's **Spar: development session** task starts the same application. See the [minimal application guide](docs/23-minimal-application.md) for source layout, debugger setup, publication, and the fixture migration map.

After stopping development, publish with the installed Visual Studio C++ native build tools:

```powershell
dotnet publish samples/Spar.NativeSample -c Release -o artifacts/minimal/publish --disable-build-servers
```

Copy the output directory and run `Spar.NativeSample.exe`. The frontend assets are embedded; Node and a frontend server are not needed. To start outside this repository using public packages, follow the [package consumer guide](docs/18-package-consumer.md).

## Status and verification

[Milestone 1](docs/19-milestone-1-acceptance.md) records execution evidence for A01–A18 on the selected Windows baseline, including native ownership, both editor lanes, copied AOT/CoreCLR delivery and external packages. Those historical scenarios now live in [fixtures/Spar.NativeSample](fixtures/Spar.NativeSample/Spar.NativeSample.csproj). Their acceptance harness deliberately exercises failures and unfinished work. New application and fixture validation must be read separately from that historical evidence; clean-machine qualification and public distribution remain deferred.

```powershell
dotnet build Spar.slnx --disable-build-servers
dotnet test --solution Spar.slnx --no-build
```

The tests launch real processes and edit isolated copies. The [integration guide](docs/22-integration-test-helpers.md) documents native/editor suites and ownership. Production build tools, inspection, and readiness observation are excluded from the Release host.

## Start here

- [Product direction](docs/02-product-direction.md) — goals, architecture, and open decisions.
- [First milestone](docs/03-milestone-1.md) — implementation sequence and acceptance criteria.
- [Implementation kickoff](docs/05-implementation-kickoff.md) — first experiments, baseline choices, and topics to study.
- [Milestone issues](docs/04-milestone-1-issues.md) — upstream tracking issue, implementation issues, and dependencies.
- [Reference projects and licenses](docs/01-reference-projects.md) — Tauri, CodeAlta, NeoAstra, Reactor, and previous Spar.
- [Agent guidance](AGENTS.md) — working rules for this repository.
- [Repository skills](docs/20-agent-skills.md) — focused testing/MSBuild workflows, pinned provenance, and validation limits.
- [Continuous integration](docs/21-continuous-integration.md) — change selection, retained coverage, full qualification and artifacts.
- [Integration test helpers](docs/22-integration-test-helpers.md) — explicit fixture/session ownership and standalone scenario commands.
- [Minimal application](docs/23-minimal-application.md) — ordinary source, package defaults, and scenario migration map.
