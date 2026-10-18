# Spar

Spar (`Sotsera.Spar`) is a Windows desktop framework being built from scratch around a C# backend and a framework-neutral web SPA, inspired by Tauri.

The priority is an excellent development experience: SPA HMR, supported C# Hot Reload, coordinated application restarts, and inspection and control for AI agents. VS Code comes first, with Visual Studio support alongside it.

Production targets NativeAOT by default, with CoreCLR compatibility for applications that add AOT-incompatible dependencies. Development uses CoreCLR for debugging and Hot Reload.

## Current status

Planning only; no framework implementation or runnable build is present yet. The first milestone will prove the complete edit, debug, inspect, and publish loop in one small application. Previous Spar work supplies reference evidence, not the new implementation.

## Start here

- [Product direction](docs/02-product-direction.md) — goals, architecture, and open decisions.
- [First milestone](docs/03-milestone-1.md) — implementation sequence and acceptance criteria.
- [Implementation kickoff](docs/05-implementation-kickoff.md) — first experiments, baseline choices, and topics to study.
- [Milestone issues](docs/04-milestone-1-issues.md) — upstream tracking issue, implementation issues, and dependencies.
- [Reference projects and licenses](docs/01-reference-projects.md) — Tauri, CodeAlta, NeoAstra, Reactor, and previous Spar.
- [Agent guidance](AGENTS.md) — working rules for this repository.
