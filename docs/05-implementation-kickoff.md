# Implementation kickoff

Date: 2026-10-18. Status: execution started; the [managed probe](07-managed-probe.md) records actual outcomes and remaining checks. This document remains the sequence for subsequent deliveries.

Read with [product direction](02-product-direction.md), [milestone 1](03-milestone-1.md), and the [upstream issue breakdown](04-milestone-1-issues.md). This document breaks down the first engineering decisions; it does not replace A01–A18 or claim they pass.

## Starting position and baseline policy

Spar is greenfield. Requiring .NET 10 or .NET 11 is acceptable if it enables a better development experience, simpler implementation, or stronger AOT compatibility. Do not add older-framework fallbacks or multi-targeting merely to expand coverage.

Compare .NET 10 and .NET 11 using the same small probes, then select one initial supported runtime/language/editor baseline. Distinguish the SDK that builds Spar, the application's target framework, the development runtime/debugger, and the published delivery mode. An AOT consumer still needs a build toolchain, while a published application has different installation requirements.

The first comparison selected stable .NET 10 for implementation: both releases demonstrated the same debugger capabilities and this slice found no necessary RC advantage. Keep .NET 11 as a reproducible comparison lane and raise the product requirement if later probes justify it. The full support promise must include a tested Visual Studio path; a newer SDK alone does not establish editor integration.

Microsoft released [.NET 11 RC2](https://devblogs.microsoft.com/dotnet/dotnet-11-rc-2/) on 2026-10-13 with a go-live license; the announcement identifies Visual Studio 2026 Insiders and VS Code with C# Dev Kit as supported development paths. RC2 is now installed locally. Test the updated release-channel Visual Studio installation against the selected SDK; decide the supported release/tooling baseline deliberately before publishing a support promise.

### Local observations

These are discovery results, not compatibility evidence:

| Component | Observed | Next action |
| --- | --- | --- |
| .NET SDK | 10.0.400, 10.0.402, and 11.0.100-rc.2.26504.105 installed, alongside older SDKs | `global.json` now pins stable 10.0.402; retain the RC comparison lane |
| Default `dotnet` selection | Initial unpinned checkout selected 11.0.100-rc.2.26504.105; repository selection is now 10.0.402 | Keep SDK selection deliberate when upgrading |
| Visual Studio | Community 2026, stable; About displays 18.11.0, October 2026 Feature Update; earlier discovery reported build 18.11.12306.390 | Manual console debugger probe passed on the .NET 10 project; repeat with the future native/SPA consumer |
| Native C++ tools | VS discovery reports the x64/x86 C++ tools component | Verify Windows SDK/linker prerequisites with an actual AOT publish |
| VS Code | 1.142.0, x64 | Establish a reproducible profile and record C# debugger/Dev Kit versions |
| VS Code extensions | C# Dev Kit 3.40.210; C# 2.160.4; .NET Install Tool 3.2.0; ESLint 3.0.34; Prettier 12.4.0 | Actual C# debugger probe passed on .NET 10 and 11; consumer/native integration remains ahead |
| Node/npm | Node 24.21.0, npm 11.19.0; host nvm reports default v24.21.0 | Use the selected Node 24 line for the frontend experiment and record the project version when scaffolding |
| WebView2 Runtime | Registry reports 155.0.4283.62 | Prove actual host startup; choose SDK and minimum runtime capabilities separately |
| Windows | Registry reports build 26220.9739 and display version 25H2 | Record the actual test environment; choose the supported Windows floor separately |

[Node's release table](https://nodejs.org/en/about/previous-releases) lists Node 20 as EOL and Node 24 as LTS. The user updated .NET, Visual Studio, and VS Code, and selected Node 24 through nvm; the table reflects subsequent verification. The agent made no installations or machine-wide configuration changes.

## First delivery: prove the managed editing loop

Work against [upstream issue #2](https://github.com/sotsera/spar/issues/2). Foundation documentation was squash-merged in [PR #13](https://github.com/sotsera/spar/pull/13), upstream commit `ee94b70`. The implementation branch `feat/2-hot-reload-probe` initially depended on that documentation branch; its four subsequent commits were then replayed onto the new upstream `main`, excluding the old foundation commits. The refreshed implementation tree matched the pre-rebase tree exactly before this status update. Earlier source hashes, commit IDs, and CI receipts remain historical evidence.

The first experiments used SDK `11.0.100-rc.2.26504.105` / `net11.0`; the same complete VS Code scenario also passed on SDK `10.0.402` / `net10.0`. No necessary .NET 11 advantage was demonstrated in this slice. `global.json` therefore selects stable SDK `10.0.402` with roll-forward disabled and previews excluded; both projects now target `net10.0`. Requiring .NET 11 remains an option when a concrete benefit is proved. The manual Visual Studio console probe now passes on this project baseline; the integrated consumer/native/AOT gates still precede a full product support promise. Central stable test dependencies are xUnit v3 `4.0.2` and AwesomeAssertions `9.6.0`; MTP is selected in `global.json`. No frontend toolchain has been scaffolded yet.

Build one disposable CoreCLR console probe under `experiments/`, with no SPA, WebView, production framework packages, or custom compiler. It needs a greeting method, mutable counter, process/instance identity, and an update-observation hook. Commands should exercise the method repeatedly so the next invocation demonstrates new behavior.

Run the same edit script under terminal `dotnet watch`, VS Code F5, and Visual Studio F5:

1. Hit a breakpoint where applicable; continue and capture the process, instance, counter, and current greeting.
2. Change a method body and verify new behavior with retained process/state.
3. Observe whether `MetadataUpdateHandler` is invoked, on what thread, and when relative to visible behavior.
4. Make an edit rejected by that lane, record the diagnostic, and exercise its supported restart/relaunch flow.
5. Introduce a compile error, correct it, and record whether the previous application remains usable.
6. Stop and account for the probe and watcher/debugger-owned process lifetime.

Repeat only the decisive cases on the alternative SDK to identify material differences. Use isolated SDK configurations so the comparison is real. Do not promise arbitrary edits are patchable or that Hot Reload reruns initialization.

Study [supported VS Code Hot Reload](https://code.visualstudio.com/docs/csharp/debugging#_hot-reload), [MetadataUpdateHandler](https://learn.microsoft.com/en-us/visualstudio/debugger/hot-reload-metadataupdatehandler?view=visualstudio), and [dotnet watch](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-watch). The VS Code page contains a dated November 2023 caveat about metadata update callbacks; this makes current behavior a test question, not proof of current failure or success.

Before choosing notification/recovery mechanics, use the [Reactor/NeoAstra/Tauri source review](06-backend-reload-research.md). Reactor supplies a concrete metadata-update adapter and renderer recovery design; its VS Code preview runs through `dotnet watch`, which must be tested separately from debugger-owned F5. Include callback thread/dispatch behavior and rapid-update coalescing in the probe. Backend object-graph copying is not a milestone requirement.

**Decision gate:** record the selected SDK/runtime/editor versions, one update owner per lane, actual patch notifications, and debugger restart strategy. If automatic notification is unavailable, test an explicit editor refresh/status action or another supported mechanism. Never report a successful backend patch merely because a file was saved. Avoid private debugger APIs and a custom Roslyn delta engine.

Also establish the minimal solution/build/test configuration: central dependency versions, `.editorconfig`, xUnit v3/MTP/AwesomeAssertions, and a real build/test CI job once there is meaningful code to check. Enforce working CI checks through the milestone's branch-protection follow-up.

## Second delivery: prove the native/AOT foundation

Work against [upstream issue #3](https://github.com/sotsera/spar/issues/3). Start with one native window, one WebView2, and one local HTML document. Publish and execute the copied NativeAOT payload immediately.

Study the [WebView2 threading model](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/threading-model), native loader/interface definitions, callback ownership, resize/close behavior, error handling, and browser-profile ownership. WebView2 requires an STA UI thread with a message pump. Async callbacks must not outlive their owners unnoticed, and blocking the UI thread can prevent completion.

The interop route is a decision to prove. [NativeAOT does not support built-in Windows COM interop](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/). Investigate the smallest supported [source-generated COM/ComWrappers](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/comwrappers-source-generation) and P/Invoke route, using the official WebView2 ABI. Inspect any proposed managed wrapper's actual dependencies and execution path. A successful managed build or an AOT annotation does not prove compatibility.

**Decision gate:** visible rendering under both CoreCLR and AOT; native startup failure, close, and pending-callback cleanup demonstrated; actual loader/runtime prerequisites recorded. Choose a Windows/x64 floor and interop approach from these results. Additional desktop UI frameworks or native helper binaries require a concrete benefit demonstrated by the probe.

## Then build one integrated application

The next slices follow the existing issue sequence, using the same greeting/counters/delayed operation throughout:

| Slice | Study/decide | Demonstrable result |
| --- | --- | --- |
| Typed bridge, #4 | Export/DTO limits, generated direct dispatch and JSON metadata, TypeScript output, contract identity, permissions, native document authority | Real typed CoreCLR/AOT calls; rejected requests never enter handlers; navigation/retirement cannot receive stale responses |
| Frontend loop, #5 | Minimal adapter contract: start command, working directory, actual URL, readiness, exit; origin policy and HMR transport | One launch action, real React/Vite HMR, supported SPA state preserved, no unnecessary C# rebuild |
| Backend loop, #6 | Patch notification, update revision versus process generation, build activation, contract regeneration, restart authority | State-preserving patch or explicit full restart; compile-error recovery; rapid edits cannot activate stale builds |
| Shared AI controls, #7 | Structured commands/events, authenticated session controls, WebView inspection/actions, bounded operations | Agent verifies a changed result through actual UI; commands distinguish acceptance, completion, and timeout |
| Editors, #8/#9 | Supported launch/debugger APIs and extension testing; reuse the coordinator | Real breakpoint/edit/relaunch/stop evidence; VS Code adapter automation and a recorded Visual Studio pass |
| Delivery, #10/#11/#12 | Packaged assets, development-only boundary, AOT diagnostics and explicit compatibility selection, public package consumption | Copied AOT app; real incompatible dependency working under CoreCLR; external consumer using local packages |

Let [Vite and its framework plugin](https://vite.dev/guide/) own frontend HMR. Study Reactor's tooling patterns when building diagnostics/inspection/editor behavior, with source provenance if code is adapted. Reuse the reference review's lessons without inheriting another project's renderer or old Spar implementation.

## Editor recommendations for consumer projects

When project scaffolding is introduced, generate [VS Code workspace extension recommendations](https://code.visualstudio.com/docs/configure/extensions/extension-marketplace#_workspace-recommended-extensions) in `.vscode/extensions.json`:

- Recommend `ms-dotnettools.csdevkit` for the C# backend; it brings the C# extension and .NET Install Tool dependencies.
- Recommend `dbaeumer.vscode-eslint` and `esbenp.prettier-vscode` when the selected frontend template includes the corresponding local packages and configuration. Keep recommendations appropriate to the chosen frontend tooling.
- Keep extensions user-installable recommendations, not automatic installations or build/runtime requirements. Preserve existing recommendations when adding Spar to an existing project.
- Include project formatting/lint configuration alongside the recommendations. Record tested extension versions separately for reproducible editor acceptance; recommendations themselves identify extensions rather than locking versions.

Exercise this editor setup with the public sample/external consumer when available. A general template system is not a prerequisite for the initial Hot Reload probe.

## Design decisions to settle through those slices

- **Update and restart ownership:** `dotnet watch` can restart on unsupported edits. Verify that its configured behavior and the coordinator do not compete to replace the same application. Debugger updates belong to the debugger in editor lanes.
- **Version identities:** distinguish process generation, backend update revision, and contract fingerprint. A successful patch can keep the same process while invalidating displayed data.
- **Safe retirement:** stop admission, request shutdown, account for unfinished handlers/callbacks, observe actual exit, then activate a replacement. Cancellation is not proof of completion; mutations are never replayed. Forced exit is an explicit distinct action.
- **Compile failures:** one running process plus an edit-not-applied status is sufficient. Keep it available only while contracts remain compatible; allow a stopped/error outcome instead of adding rollback stores, snapshots, or concurrent backend versions.
- **Session-owned frontend:** keep the SPA server through expected backend restarts and stop it when the terminal/debug/workspace owner ends. Combine owner-disconnect detection with a Windows process-lifetime safeguard, and test abrupt exits as well as graceful Stop. The [.NET 11 process API announcement](https://devblogs.microsoft.com/dotnet/process-api-improvements-in-dotnet-11/) describes `KillOnParentExit`; verify actual API availability and behavior on the selected SDK rather than assuming the announcement alone proves support. Native [Windows job objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects) are the platform mechanism to investigate. Never kill unowned external servers or processes by name.
- **Small memory observer:** investigate [WebView2 environment process discovery](https://learn.microsoft.com/en-us/microsoft-edge/webview2/reference/win32/icorewebview2environment8) and OS process counters. A dev-tool sampler can show backend/host private bytes and working set separately from the browser-environment total. Browser processes can be shared within the profile scope, and working-set sums include shared pages; these are not exact per-page physical RAM figures. Keep this optional and outside production code.
- **Generator/build integration:** generated C#, JSON metadata, and TypeScript must agree without source-writing loops or relying on private output paths. Test rebuild behavior, IDE design-time builds, contract edits, and local package consumers.
- **Permissions and origins:** a development URL alone is not authorization. Bind exported calls and inspection actions to the owned session and current native document. Keep production assets and development control policy distinct.
- **AI/editor boundary:** use one coordinator/service model for CLI, tests, and adapters. UI automation and agent commands are development features with verifiable production exclusion. Add an MCP adapter only when the shared controls work.
- **Compatibility delivery:** application dependencies may justify explicit CoreCLR production selection; framework AOT regressions and unrelated publish failures remain failures. General automatic cause classification is not a prerequisite for the first working route.

## Scope and evidence discipline

Create projects/packages as responsibilities become real. Initially, a small probe, minimal host, and consumer sample are enough; avoid prebuilding a plugin system, transport framework, renderer, installer, multi-window architecture, or cross-platform layers.

Keep a short evidence record with each probe/PR: question, exact versions and commands, expected/observed behavior, source revision/hashes, artifacts, conclusion, and remaining limits. A GUI or debugger pass requires actual interaction; source inspection and mocks cannot substitute. The [managed probe ledger](07-managed-probe.md) records watcher, VS Code, and manual Visual Studio console scenarios; the [native host proof](09-native-host.md) adds visible local HTML and copied AOT execution. Integrated editor and full milestone acceptance remain ahead.

The first result we want is modest and decisive: **edit a C# method, observe changed behavior with retained state, explain how every supported lane detects the update or restarts, and select the baseline from that evidence.** The next result is a visibly rendering AOT window. Once both are established, the rest of the framework can grow around mechanisms we have actually demonstrated.
