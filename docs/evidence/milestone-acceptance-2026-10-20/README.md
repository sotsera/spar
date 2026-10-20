# External Visual Studio acceptance — 2026-10-20

The package-consumer Visual Studio walkthrough passed on merged baseline `d13238f70d59168891630e8ec0081f5f431aea42` (PR #26). This supplies the remaining editor execution for issue #12 and the fresh documented setup in A01. The [milestone matrix](../../19-milestone-1-acceptance.md) reconciles the other scenarios separately.

## Fresh setup

Five local `0.1.0` packages were rebuilt from that baseline. The [receipt](receipt.json) records their hashes, tool versions, final consumer source hashes, process/document/contract identities, and profile lease checks. The fresh consumer was created outside the checkout, with isolated `.feed` and `.packages`, using:

```powershell
pwsh -NoProfile -File eng/Pack-Local.ps1 -Output artifacts/milestone-acceptance/packages
pwsh -NoProfile -File eng/New-PackageConsumer.ps1 -Directory <fresh-external-directory> -Feed artifacts/milestone-acceptance/packages
# From the external directory:
dotnet build backend/Spar.NativeSample.csproj --disable-build-servers
Push-Location backend/frontend
npm ci --ignore-scripts --include=optional
Pop-Location
```

The [first direct application build](fresh-build.log) passed with zero warnings/errors. `Consumer.slnx` loaded exactly the application and its own contract project. No Spar source project was loaded. Visual Studio Community 2026 **18.11.0, Stable** is captured in [Help → About](vs-about.png); the executable file version is retained separately in the receipt. SDK `10.0.402`, Node `24.21.0`, npm `11.19.0`, installed WebView2 and CoreCLR Debug were used.

Before the IDE build, the disposable backend received two empty classes, `InitialBackendBase` and `ReplacementBackendBase`, and initially inherited the former. This creates the documented unsupported-edit case; it is not a package/runtime modification.

## Actual IDE and native actions

The agent operated Visual Studio and the native app through the computer-use tool. F5, Continue, Apply Code Changes, solution builds and Shift+F5 were actual IDE actions. The first supported greeting edit and deliberate undefined-name edit were typed in the IDE and saved. CSS, correction, base-class and contract edits were written directly to the disposable source files; Visual Studio observed those saved changes before its subsequent apply/build action.

| Step | Observed result |
| --- | --- |
| F5 and breakpoint | Debug / **Spar development** launched the packaged consumer. The debugger stopped on the actual `GreetAsync` return ([capture](vs-breakpoint.png)). The breakpoint was removed, F5 continued execution, and the [native window](vs-native-ready.png) displayed **Hello, Spar / Connected**. |
| Frontend HMR | Native OS clicks set both counters to 2; the input received `External VS retained state`. Changing the CSS accent to `#e9b44c` visibly produced [yellow controls](vs-css-hmr.png), retaining counters and text. |
| Supported backend patch | Apply Code Changes displayed **Hello from external Visual Studio, Spar**, revision 1, with retained state ([capture](vs-backend-patched.png)). Public events retain the same PID 55904, document and React mount. |
| Invalid edit and recovery | Applying `MissingGreetingSymbol` produced the [undefined-name diagnostic](vs-compile-error.png). The [native UI retained the valid greeting/state](vs-invalid-retained.png). Correction plus Apply Code Changes produced **Hello recovered externally, Spar**, revision 2, in the same process/document/mount ([capture](vs-recovered.png)). |
| Unsupported edit | Changing only the base class in the declaration produced the [restart-required diagnostic](vs-unsupported.png). Automatic rebuild was unchecked. **Edit** dismissed the popup; its restart action was not used. |
| Normal close and relaunch | The native X button ended session 1 gracefully. All 13 retained handles exited. Ctrl+Shift+B succeeded, then F5 launched a new process/session/document with zero counters and empty text ([capture](vs-relaunched.png)). |
| Contract retirement and failed build | Adding `int Revision` to `CounterReply` ended session 2 with `contract_source_changed` and graceful cleanup. Ctrl+Shift+B rebuilt the contract and failed the app with [CS7036](vs-contract-error.png); the [build output](vs-contract-build-failed.png) records one succeeded and one failed project. |
| Corrected contract and F5 | Adding the constructor argument `7` gave a [successful build](vs-contract-build-passed.png). F5 launched a third session with the changed matching fingerprint. An actual native click completed a counter mutation to 1 ([capture](vs-contract-recovered.png)). |
| Debugger Stop | Actual Shift+F5 terminated application PID 37800 ([IDE capture](vs-debugger-stop.png)). Its coordinator recorded `forced_cleanup / owner_disconnected`; all 13 retained handles exited. This is distinct from the two graceful sessions. |

The three public logs are [session 1](vs-session-1.jsonl), [session 2](vs-session-2.jsonl), and [session 3](vs-session-3.jsonl). Fingerprints changed from `5f384a796cdfac56662f2a8e4e88daceef82abfdf9d3720b904da302e912ca0d` to `9ed337f2bfae07213041ca561feab350835c41df4abbeaf99ed1c3d44e8ca43e` only after the contract rebuild. Each session records the debugger as backend update owner; no SDK watcher ran alongside it.

An independent collector retained OS process handles and creation times while the sessions ran. [Final observations](vs-processes.json) show **39 retained handles, all exited**. Four short-lived identities in the first startup had already exited before observation; they are explicitly marked absent, not counted as retained-handle proof. After exit, all three `.spar-profile.lock` files opened exclusively. Assertions against the completed, newline-framed event logs also checked state retention, revisions, changed fingerprint, contract retirement and the distinct shutdown outcomes.

All native and IDE captures linked above were visually reviewed. This is an interactive desktop pass, separate from [PR #26's successful required CI](https://github.com/sotsera/spar/actions/runs/39377913914). The last merged implementation also has 65 passing managed tests and the native/package regressions retained in the [package evidence](../package-consumer-2026-10-20/README.md); this documentation-only follow-up does not invent a new full regression run.

## Interruptions and limits

Early file-dialog input changed while the operator was using the computer. UI actions paused until the operator explicitly requested continuation. The first request for access to the newly launched native app timed out; access succeeded on retry after the operator approved it. Neither interruption is counted as a failed application session or as completed interaction.

One capture of an occluded native window returned Visual Studio pixels. It was not retained as native evidence; the target was refreshed and brought forward before the reviewed capture.

The external solution uses the generated absolute-path launch profile. Visual Studio automatic restart, toolbar Restart, source edits inside bootstrap, and a second breakpoint after relaunch are not separately claimed. Whole-session relaunch resets SPA state in this lane. The package test is on a developer machine; clean Windows without the SDK/runtime, general distribution readiness and public NuGet publication remain outside this proof.
