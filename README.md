# Spar

Spar is a C# framework for desktop applications with a web frontend, hosted in WebView2 on Windows and in WebKitGTK on Linux.

You write the backend as ordinary C# services and mark the operations the frontend may call. The build generates:
- the C# dispatch and serialization code;
- a typed TypeScript module per contract, imported from the app's `@spar/app` package;
- in Release builds, an embedded copy of the built frontend.

An app chooses its target platforms with one property, and publishes by default as a single [NativeAOT](https://learn.microsoft.com/dotnet/core/deploying/native-aot/) executable for each of them.

This repository targets .NET 11. It is at milestone M1a: build machinery, contracts and the core, with no window yet. Nothing here is usable as a framework yet.

- [Plan](docs/plan.md): scope, settled decisions, open questions and milestones, with the design by area in [`docs/design/`](docs/design/).
- [Decisions](docs/decisions.md): measured outcomes of the M0 spikes and the decisions taken from them.
- [Agent guide](AGENTS.md): how to work in this repository.

## Prerequisites

- Windows 11 x64.
- .NET SDK 11.0.100, selected by `global.json`.
- Node.js 24 or later.
- The [WebView2 Evergreen Runtime](https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution).
- For NativeAOT publishing on Windows: the Visual Studio C++ build tools ([prerequisites](https://learn.microsoft.com/dotnet/core/deploying/native-aot/#prerequisites)).
- For the Linux host: a Linux x64 system, WSL with WSLg, or Docker, with GTK 4 and WebKitGTK 6.0. NativeAOT publishing on Linux also needs `clang` and the `zlib` development package.
