# SPAR product direction

Date: 2026-10-18. Status: agreed product direction with explicitly identified implementation proposals.

References: [project comparison and licenses](01-reference-projects.md), [first milestone plan](03-milestone-1.md).

## Purpose

SPAR lets developers build Windows desktop applications with a C# backend and a web SPA. Its defining feature is a coordinated development experience: frontend HMR, supported backend Hot Reload, predictable restarts, useful diagnostics, and direct inspection and control by both developers and AI agents.

Production applications default to NativeAOT. CoreCLR compatibility delivery exists for applications whose added dependencies cannot support AOT. Development uses CoreCLR to enable the editing/debugging workflow; this is independent of the production delivery choice.

## Confirmed direction

- Start implementation from scratch. Prior SPAR supplies lessons and test scenarios, not a production source/build dependency.
- Keep the Spar name and `Sotsera.Spar` package/namespace identity.
- Keep the existing repository identity: `sotsera/spar` is canonical for issues, milestones, and PR targets; `ghidello/spar` is the development fork. The local remote named `origin` points to the fork.
- Target Windows only for now.
- Make AOT the default production target; support CoreCLR when application dependencies require it.
- Drive the SPA's development server and use its native HMR mechanism.
- Apply backend Hot Reload wherever supported; restart the application/window when required.
- Make AI-driven development a first-class workflow.
- Prioritize VS Code while maintaining a pleasant Visual Studio experience.

The new implementation is being prepared in `E:\Ghidello\Git\spar.v2`, using the existing fork/upstream relationship. Previous source remains a separate reference checkout. Planning and subsequent implementation reach upstream through focused pull requests.

## Proposed initial architecture

Use Win32 and WebView2 for a small Windows host. The browser engine is supplied by the installed WebView2 Runtime. AOT publication does not imply one OS process or eliminate writable browser data.

Separate responsibilities without pre-creating a project for each box:

| Responsibility | Owns |
| --- | --- |
| Application runtime | Startup, native window/WebView lifetime, document identity, typed calls, production assets, shutdown |
| Contract/build tooling | Export validation, direct dispatch, JSON metadata, TypeScript bindings, frontend build and publication integration |
| Development coordinator | Frontend process, application generations, readiness, restart sequencing, diagnostics and session control |
| Editor adapters | VS Code/Visual Studio launch/debug integration and presentation of common coordinator state |
| Automation adapter | Structured session operations, UI inspection/actions, logs and evidence; CLI first, MCP adapter over the same services |

Application code should use ordinary .NET projects. The reference SPA is proposed to use React and Vite; the runtime and client remain framework-neutral. Installed applications use packaged local frontend assets, with no required Node process or production UI HTTP server.

The initial application has one native window and an in-process backend. If the backend cannot be hot-reloaded, restart the application process and recreate the window. Keeping a permanent shell while replacing backend workers is deferred until a concrete need justifies the extra transport and lifecycle model.

## Development session model

There is one coordinator per development session and one authority applying C# updates to each application process.

- Terminal/AI session: investigate supported `dotnet watch` integration first.
- VS Code session: the C# debugger owns supported code updates; SPAR coordinates frontend startup and whole-application restarts.
- Visual Studio session: Visual Studio owns supported code updates; use the same session model and ordinary launch/build integration.

Never run two Hot Reload engines against the same process. Do not assume undocumented debugger events, compiler protocols, or attach-time behavior. Milestone 1 first proves the necessary integration with pinned tooling. A custom Roslyn delta engine is not the default solution.

The coordinator launches the configured SPA command, discovers or receives its actual loopback URL, waits for readiness, and opens it in WebView2. The SPA tool and framework plugin own HMR. A fixed port, package manager, or framework is not a runtime requirement. A development session must authenticate its backend control channel and bind bridge access to the expected local document/session.

### Change behavior

| Change | Expected response | State expectation |
| --- | --- | --- |
| CSS or supported SPA module | Frontend HMR | Preserve state when the selected frontend plugin supports it |
| Supported C# implementation edit | Apply managed update | Preserve process and existing state; new behavior appears on subsequent execution |
| Backend update affecting displayed data | Notify frontend of a backend generation change | Application decides which data to invalidate/refetch |
| Exported API/DTO change | Regenerate, build matching outputs, restart and establish a fresh bridge | Backend state resets; stale client must not call the new contract |
| Startup configuration, dependency or unsupported code edit | Rebuild and restart application | State resets unless explicitly persisted by the application |
| Build error | Show file/line diagnostics and retain a clear failed-update state | Never activate partial output or claim the edit was applied |

Hot Reload does not rerun initialization or reconstruct every object. A WebView refresh alone does not reload the backend. Documentation and status must distinguish frontend HMR, backend patching, page refresh, and application restart.

Proposed restart sequence: coalesce edits; prepare a matching build; stop admission to the retiring generation; request owned shutdown; launch the new generation only after the old one has actually exited; wait for native and bridge readiness. If compilation fails, the previous compatible generation may remain available with an explicit stale-build status. If it has already stopped, remain stopped with actionable diagnostics. Never replay a backend mutation automatically.

## AOT and dependency compatibility

Production mode selection must be observable and reproducible:

1. Analyze and attempt the default AOT route with the application's actual dependencies.
2. If an application-added dependency requires unsupported dynamic behavior, report the dependency, affected API/diagnostic, and the CoreCLR alternative.
3. A compatibility selection records the delivery mode in the project/build result. An eventual automatic selector must classify the cause and report its choice; it cannot treat every failed publish as incompatibility.
4. Framework AOT regressions, ordinary compilation failures, missing toolchains, and unrelated native link failures remain failures.
5. Removing an incompatible dependency permits reevaluating AOT. Do not cache incompatibility forever.

The exact configuration interface and whether eligible fallback requires a persisted explicit selection or an opt-in automatic policy remain to be selected in milestone 1. The user-facing requirement is dependency-driven fallback, not silent mode changes. Strict AOT publication must remain available for CI.

Package AOT annotations alone are insufficient evidence. Development must surface compatibility diagnostics, and CI must publish and execute the real AOT output. Proposed CoreCLR compatibility publication is self-contained; exact packaging shape is a milestone decision rather than a reason to add an installer now.

## AI-first capabilities

An agent must be able to start a session, wait for actual rendered/bridge readiness, inspect failures, operate the UI, verify a changed result, request a restart, and stop the owned session.

Expose structured operations and events with session IDs, application-generation IDs, process identity, document/contract identity, and correlation IDs. Provide human-readable output alongside structured records. Distinguish command acceptance from completion; timeouts must identify unfinished work rather than report false success.

The minimum inspection surface includes frontend console/errors, backend/build diagnostics, a DOM or accessibility snapshot, screenshot capture, and bounded click/type actions against the actual WebView. Use the same underlying services for tests, CLI commands, and a future MCP adapter.

Development control endpoints and inspector capabilities are development-only. Production publication must exclude or disable them through a verifiable build boundary. Agent access does not broaden application permissions or authorize arbitrary shell/filesystem actions through the renderer.

## Correctness required from the first integrated application

- Explicitly exported backend operations with host-enforced permissions and bounded input/output.
- Generated dispatch and serialization suitable for AOT; stale contract rejection.
- Native document identity, constrained navigation, and separate development/production origin policies.
- Pending calls cannot deliver into a retired document; cancellation does not prove backend work stopped.
- Native/thread/callback ownership persists until actual release. Avoid production dependencies on inspection or measurement tools.
- Shutdown, debugger stop, renderer loss, and failed startup have distinct outcomes.
- Preserve browser profiles by default; do not equate application shutdown with browser-data deletion.
- Test actual visible WebView behavior and the published payload, not only browser mocks or managed unit tests.

## What makes the experience good

One documented start action should launch both sides and reach the application. Errors should lead to the relevant file and line. Status should say whether a change was applied, rejected, or restarted. Stop should clean up owned application/development processes. A new application should not require handwritten dispatcher adapters or knowledge of internal output-directory conventions.

Measure time from save to observable change, cold/warm start, restart, generation/build overhead, and cleanup. Capture distributions and tooling versions before selecting numerical promises. Measure AOT publication separately from the editing loop and browser resources separately from host resources.

## Deferred scope

Additional operating systems, mobile, multiple native windows, custom chrome, a native C# UI DSL, dynamic plugins, a persistent shell with backend workers, general streaming APIs, automatic state migration, installers, and signed self-updates are outside the first milestone. Native file pickers and broader desktop services can follow the development proof.

## Decisions needed at milestone kickoff

Choose and pin the .NET SDK, C# version, Windows baseline, architecture, WebView2 SDK/runtime floor, frontend tooling, VS Code/C# extensions, and Visual Studio version. Previous SPAR's .NET 11 RC1/C# split is historical evidence, not an inherited requirement. Confirm that the same application language/API baseline works across both editor lanes.

Also settle the supported Hot Reload integration hooks, restart/debugger relaunch mechanism, production compatibility-mode configuration, minimum frontend adapter contract, and first consumer-facing launch API. Record tested decisions rather than expanding these into a general platform before the small application works.

## Primary technical references

- [.NET NativeAOT behavior and limitations](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [dotnet watch](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-watch)
- [Supported C# code changes](https://learn.microsoft.com/en-us/visualstudio/debugger/supported-code-changes-csharp)
- [VS Code C# debugging and Hot Reload](https://code.visualstudio.com/docs/csharp/debugging#_hot-reload)
- [VS Code extension integration testing](https://code.visualstudio.com/api/working-with-extensions/testing-extension)
- [Vite HMR](https://vite.dev/guide/api-hmr)

These sources establish available mechanisms, not proof that SPAR's integrated workflow already works.
