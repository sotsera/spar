# SPAR reference projects and license review

Reviewed: 2026-10-18. Scope: architectural references for a fresh Windows-first SPAR implementation.

Read alongside [product direction](02-product-direction.md) and [milestone 1](03-milestone-1.md).

## Decision context

SPAR will retain its name, package identity, and repository identity, but start its implementation afresh. These projects supply design ideas and evidence. This document does not select a runtime dependency or authorize wholesale source imports. The previous SPAR is a regression reference, not the starting codebase.

The review covered selected source and documentation in CodeAlta, Reactor, and the previous SPAR; NeoAstra's architecture, RPC, security, platform, and distribution documentation; and Tauri's architecture and capability documentation. No upstream application was built or benchmarked during this review. License verification below is against actual files, not GitHub's license badge.

## Comparison

| Project | Role and implementation | Most relevant contribution | Recommended relationship to SPAR |
| --- | --- | --- | --- |
| [Tauri](https://github.com/tauri-apps/tauri) | Web frontend, Rust backend, system WebViews, runtime and distribution tooling | Cohesive application framework; explicit IPC and capabilities; frontend development integration | Product and architecture benchmark |
| [CodeAlta](https://github.com/CodeAlta/CodeAlta) | React/TypeScript/Vite application with a .NET backend, hosted through NeoAstra | Real application requirements: lifecycle, terminals, recovery, generated contracts, automation | Integration reference; not a framework to extract wholesale |
| [NeoAstra](https://github.com/NeoAstra/NeoAstra) | .NET desktop framework using platform WebViews and native window backends | AOT-oriented RPC generation, asset hosting, authorization, desktop services, browser automation | Closest framework comparison; evaluate individual mechanisms or dependency options when useful |
| [Microsoft.UI.Reactor](https://github.com/microsoft/microsoft-ui-reactor) | Declarative C# components reconciled into native WinUI controls | Developer feedback, analyzers, inspection, automation, Hot Reload tooling | Development-experience reference; no requirement to adopt its native UI renderer |
| Previous SPAR | Windows/Win32/WebView2, C#, generated TypeScript contracts, AOT/CoreCLR modes | Ownership, document authority, selected-resource handles, visible lifecycle verification | Preserve lessons and scenario descriptions; write new implementation |

## Verified licenses

Revisions identify the material reviewed, not a claim that every upstream repository was exhaustively audited. CodeAlta and Reactor use the source revisions inspected earlier in this review; NeoAstra and Tauri were resolved when checking licenses.

| Project | Verified revision | License and primary evidence |
| --- | --- | --- |
| CodeAlta | `78e8bf28a8f7677edf446a2c64da9861a08ac4fe` | BSD-2-Clause: [license.txt](https://github.com/CodeAlta/CodeAlta/blob/78e8bf28a8f7677edf446a2c64da9861a08ac4fe/license.txt) |
| NeoAstra | `7220b91b7c860c4bbee9488b19146918d9afe5d7` | BSD-2-Clause: [license.txt](https://github.com/NeoAstra/NeoAstra/blob/7220b91b7c860c4bbee9488b19146918d9afe5d7/license.txt) |
| Microsoft.UI.Reactor | `e16819520b6452d93fdeba9f59e2b81a47febd1c` | MIT: [LICENSE](https://github.com/microsoft/microsoft-ui-reactor/blob/e16819520b6452d93fdeba9f59e2b81a47febd1c/LICENSE) |
| Tauri | `136a28eaa879e8aa706f9b2e05a050c46dcc1f4f` | Workspace declares `Apache-2.0 OR MIT`: [Cargo.toml](https://github.com/tauri-apps/tauri/blob/136a28eaa879e8aa706f9b2e05a050c46dcc1f4f/Cargo.toml). [MIT text](https://github.com/tauri-apps/tauri/blob/136a28eaa879e8aa706f9b2e05a050c46dcc1f4f/LICENSE-MIT), [Apache text](https://github.com/tauri-apps/tauri/blob/136a28eaa879e8aa706f9b2e05a050c46dcc1f4f/LICENSE-APACHE-2.0). The core crate inherits the workspace license. |
| Previous SPAR | Local checkout reports `6d07b46`; license read directly | MIT, copyright 2026 Sotsera: [local LICENSE](E:/Ghidello/Git/spar/spar/LICENSE). This was not a clean-working-tree or signature verification. |

### What this means for reuse

- **BSD-2-Clause:** retain copyright, conditions, and disclaimer in redistributed source; reproduce them in documentation or other accompanying material for binary redistribution. This applies to both CodeAlta and NeoAstra.
- **MIT:** include the copyright and permission notice with copies or substantial portions. This applies to Reactor and previous SPAR, and to Tauri material available under MIT.
- **Tauri's dual license:** the workspace expression offers a choice for covered code. Its README describes MIT or MIT/Apache licensing where applicable, so check the selected component and any file-level terms before copying. Under the Apache route, preserve the license and applicable notices, mark modified files, and retain relevant NOTICE attribution. Apache also contains an express patent grant with conditions.
- **Assets and dependencies:** repository code licensing does not automatically cover every bundled font, image, library, browser loader, or tool. Tauri's [README](https://github.com/tauri-apps/tauri/blob/136a28eaa879e8aa706f9b2e05a050c46dcc1f4f/README.md#licenses) separately labels its logo CC-BY-NC-ND; do not import that branding into SPAR.

Reactor's [third-party notices](https://github.com/microsoft/microsoft-ui-reactor/blob/e16819520b6452d93fdeba9f59e2b81a47febd1c/ThirdPartyNoticeText.txt) include Yoga, md4c, D3, and other attribution. NeoAstra's [third-party notices](https://github.com/NeoAstra/NeoAstra/blob/7220b91b7c860c4bbee9488b19146918d9afe5d7/THIRD-PARTY-NOTICES.md) cover its distributed WebView2 loader and distinguish external platform dependencies. Inspect the actual selected package version when shipping; this review is not a transitive dependency license audit.

For a source adaptation, record upstream URL, commit, paths, license, and local changes, and carry the applicable notices. For a new implementation inspired by a design, retain a reference explaining the idea without claiming upstream implementation or test results as SPAR's own. Retaining SPAR's existing MIT licensing is the proposed continuation; no new root license or repository metadata was installed by this documentation task.

## Tauri: borrow the complete product shape

Tauri separates application APIs, the runtime abstraction, native window/WebView integration, frontend bindings, and build/distribution tools. Its capability system associates permissions with windows and WebViews. This is a useful model for SPAR's boundaries, even though SPAR initially needs only Windows. Sources: [architecture](https://v2.tauri.app/concept/architecture/), [capabilities](https://v2.tauri.app/security/capabilities/), [process model](https://v2.tauri.app/concept/process-model/).

Borrow:

- One coherent path from project creation through development to publication.
- A framework-neutral web frontend with explicit, typed backend operations.
- Host-enforced permissions, constrained assets/navigation, and per-document authority.
- A clear distinction between the application host and browser subprocesses.
- Build-time validation and generated configuration/contracts.

Adapt selectively: Tauri documents that registered application commands are broadly callable by default unless additionally constrained. SPAR should require permissions on exported application operations from the beginning. Do not assume that the presence of a capability file protects every registered method.

Defer its breadth: mobile, multiple desktop platforms, a general plugin ecosystem, installers, and signed update infrastructure are outside milestone 1. Rust hot-restart behavior also does not establish C# Hot Reload behavior.

## CodeAlta: borrow integration lessons

Its [desktop guide](https://github.com/CodeAlta/CodeAlta/blob/78e8bf28a8f7677edf446a2c64da9861a08ac4fe/src/CodeAlta/README.md) describes React/Vite, generated RPC, packaged local assets, and a shared .NET backend also used by its terminal frontend. Node is needed for frontend builds rather than the installed UI.

Borrow the separation of backend services from presentation, realistic startup/error/recovery states, and the ability to inspect and operate the UI through automation. Use its window lifecycle and renderer recovery work as scenario inspiration. Keep application concerns such as terminals, agent providers, and Git workflows outside SPAR's runtime.

Its [project file](https://github.com/CodeAlta/CodeAlta/blob/78e8bf28a8f7677edf446a2c64da9861a08ac4fe/src/CodeAlta/CodeAlta.csproj) explicitly disables AOT and publishes framework-dependent tools. Its [plugins](https://github.com/CodeAlta/CodeAlta/blob/78e8bf28a8f7677edf446a2c64da9861a08ac4fe/doc/plugins.md) are trusted in-process code. Neither is the release model selected for SPAR. Application-level maturity must not be treated as evidence of SPAR-compatible AOT deployment.

## NeoAstra: borrow framework mechanisms

NeoAstra already addresses the closest technical problem: a .NET host with system WebViews, generated RPC, local assets, native integration, and development tooling. Its [RPC documentation](https://github.com/NeoAstra/NeoAstra/blob/7220b91b7c860c4bbee9488b19146918d9afe5d7/doc/rpc-and-bindings.md) is particularly relevant.

Borrow:

- Generated dispatch and serialization metadata rather than reflection-based application invocation.
- Contract fingerprints and explicit rejection of stale bindings.
- Cancellation, session teardown, bounded work, and structured error behavior.
- Shared automation primitives usable by tests and AI tools.
- Explicit separation of development assets/origins from production asset hosting.

Its [security documentation](https://github.com/NeoAstra/NeoAstra/blob/7220b91b7c860c4bbee9488b19146918d9afe5d7/doc/capabilities-and-security.md) says permissionless registered application methods remain available after bridge admission; explicit capabilities do not filter those methods. SPAR's exported operations should have explicit permissions. Linux whole-view trust limitations are useful future reference, but do not belong in the initial Windows architecture.

Its [delivery documentation](https://github.com/NeoAstra/NeoAstra/blob/7220b91b7c860c4bbee9488b19146918d9afe5d7/doc/delivery-and-updates.md) distinguishes generated packaging from target-host qualification and keeps self-update experimental or store-managed. Borrow that evidence discipline. No decision has been made to depend on NeoAstra; any such decision must demonstrate a benefit against SPAR's Windows/AOT/development requirements.

## Reactor: borrow the developer experience

Reactor's [reconciler](https://github.com/microsoft/microsoft-ui-reactor/blob/e16819520b6452d93fdeba9f59e2b81a47febd1c/src/Reactor/Core/Reconciler.cs) creates and updates WinUI controls. This is a different renderer from SPAR's SPA. Its [README](https://github.com/microsoft/microsoft-ui-reactor/blob/e16819520b6452d93fdeba9f59e2b81a47febd1c/README.md) describes analyzers, CLI diagnostics, developer inspection, automation, and Hot Reload tooling alongside an experimental public API.

Borrow the product principles: useful diagnostics before runtime, inspectable live state, short edit/observe cycles, discoverable APIs, and automation designed into the development experience. For SPAR, apply those principles to backend calls, contract generation, reload state, SPA errors, and the actual WebView.

The follow-up [backend reload source review](06-backend-reload-research.md) traces Reactor's metadata-update adapter, renderer state migration, and watcher-backed VS Code preview, alongside NeoAstra's orchestration and Tauri's Rust restart loop. Use it when implementing the initial backend Hot Reload probe.

Do not import a C# UI DSL, reconciler, or WinUI dependency merely to obtain these benefits. Reactor's [AOT matrix](https://github.com/microsoft/microsoft-ui-reactor/blob/e16819520b6452d93fdeba9f59e2b81a47febd1c/docs/aot-support.md) also shows why development reflection and release compatibility need explicit separation. Its Hot Reload implementation is reference material, not proof that the same behavior works for SPAR.

## Previous SPAR: preserve evidence and reduce consumer complexity

Local source: `E:\Ghidello\Git\spar\spar`. The parent directory also contains generated external-consumer fixtures; it is not all framework source.

Preserve the scenarios around native STA ownership, cancellation versus actual completion, document retirement, scoped native file handles, visible rendering before shutdown, and distinct host/browser resource measurements. Consult the [progress ledger](E:/Ghidello/Git/spar/spar/docs/implementation/progress.md) and [package evidence](E:/Ghidello/Git/spar/spar/docs/implementation/evidence/sp-22-packages.md) for historical results and unresolved environments.

Improve the consumer experience in the fresh design: examples should use public APIs; generated dispatch should not require repetitive handwritten adapters; mode/output configuration should live in package defaults; one development coordinator should replace scattered launch machinery. Do not carry forward old SDK pins, task IDs, numerical resource limits, or historical permissions as new decisions.

## Recommended borrowing order

1. Tauri's cohesive developer workflow and host/frontend boundary.
2. Reactor's diagnostic, inspection, and automation principles.
3. NeoAstra's generated contracts, lifecycle, and local asset mechanisms.
4. Previous SPAR's failure scenarios and ownership lessons.
5. CodeAlta's realistic application integration cases after the minimal workflow works.

The first implementation should be new, small, and independently verified. Borrowing a design does not require adopting the upstream dependency graph.

The first actual runtime dependency/projection is the official WebView2 native SDK. Its pinned version, BSD-3-Clause license, retained notices, ABI paths, and adaptations are recorded in the [native host provenance](09-native-host.md#webview2-source-and-license-provenance). The SDK license and separately installed browser-runtime distribution terms are distinct.
