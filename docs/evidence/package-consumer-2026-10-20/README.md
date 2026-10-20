# External package execution — 2026-10-20

This is a partial delivery of issue #12. A18 has actual package-consumer execution and desktop evidence. The external Visual Studio guide walkthrough and final M1-00 reconciliation remain open; the prior source-consumer A13 pass is not reused as package acceptance.

Follow-up: PR #26 merged as `d13238f` after its [corrected required CI passed](https://github.com/sotsera/spar/actions/runs/39377913914). The subsequent [external Visual Studio walkthrough](../milestone-acceptance-2026-10-20/README.md) passed, and the [milestone matrix](../../19-milestone-1-acceptance.md) reconciles the remaining acceptance evidence. The chronological pending statements below describe this earlier run.

## Baseline and reproduction

Work began on PR #25 commit `07354da8f88623638812e0e712346f6290faea38` while its CI ran. That PR subsequently merged; this change is rebased onto the integrated main before submission. Package/runtime implementation hashes are retained in `sources.sha256`; package hashes in `receipt.json` identify the exact five tested `0.1.0` nupkgs. The package README was expanded after those artifacts were built; this does not change the tested binaries/targets. Nothing was published to public NuGet.

The clean external run used:

```powershell
pwsh -NoProfile -File eng/Pack-Local.ps1 -Output artifacts/packages-external-3
pwsh -NoProfile -File eng/Test-ExternalConsumer.ps1 -Feed artifacts/packages-external-3 -EvidenceDirectory artifacts/external-consumer/attempt-3 -VerifyRender
dotnet build Spar.slnx --disable-build-servers
dotnet test --solution Spar.slnx --no-build
```

SDK `10.0.402`, Node `24.21.0`, npm `11.19.0`, Windows x64 and installed WebView2 were used. Publication receipts record exact tool versions and source/payload hashes. The consumer had its own `.feed` and empty `.packages`, and no generated bindings, source-project references into Spar, or junctions. Its first build targeted the application directly, so a solution build could not mask a missing restore dependency. Example project/configuration files are retained under `consumer/`; `eng/New-PackageConsumer.ps1` reconstructs the full application-owned source tree.

## Observed results

- Solution build passed; 65 managed tests passed, none failed or skipped.
- External restore, contract generation and direct application build passed. Restore graphs contained only the consumer's own contract project plus package dependencies.
- The actual native development session applied CSS HMR and backend method Hot Reload. Document/mount, local counter 7, backend counter 2 and entered text were retained. Revision 1 displayed **Hello from packages, Spar**. Coordinated rebuild/restart produced generation 2 and reset the backend counter. Session cleanup checked retained OS process handles and unrelated-process survival. The helper's `development.json` marks its generic rendering field `not-exercised`; the explicit external driver capture and reviewed `development.png` provide the rendering evidence.
- Default NativeAOT publication passed. The actual Release package host passed development/inspection exclusion before publication execution. A copied payload rendered, completed typed backend/UI interaction and exited. `aot.png` was visually reviewed.
- A consumer-owned dependency emitted and executed `DynamicMethod` IL. Persisted CoreCLR configuration plus `SparStrictAot=true` failed with attributable IL3050 and produced no success receipt. Explicit self-contained CoreCLR publication passed and its copied native UI displayed **Hello from generated IL, Spar**. `coreclr.png` was visually reviewed. This is the public consumer configuration, not a framework-source modification.

## External VS Code debugger

The third fresh editor consumer used `eng/Test-VSCodeDevelopment.ps1 -ConsumerDirectory <external-root> -VerifyRender`, VS Code `1.142.0`, C# `2.160.4` and C# Dev Kit `3.40.215`. Its package feed was the same as the terminal/publication run. `vscode/receipt.json`, public coordinator events, breakpoint events and the IDE Hot Reload log record:

- A real breakpoint in the external `SampleBackend.cs`, Continue and typed readiness.
- Frontend HMR plus IDE-owned backend Hot Reload with the same document/mount and retained counters/text. `vscode/patched.png` was visually reviewed: yellow controls, **Hello debugger, Spar**, revision 1.
- Broken-source CS0103 and corrected method update; an actual ENC0014 unsupported base-class edit followed by Spar's explicit restart.
- In-flight retirement handling, contract change with failed CS7036 build and recovery, a changed matching fingerprint, native graceful close and debugger Stop.
- 45 independently retained process handles; all observed session processes exited. No SDK watcher owned backend updates in this lane.

The IDE's local timestamps in its logs use its environment time formatting; coordinator timestamps are UTC. Correlate session/generation/process identity instead of equating those formatted clocks.

## Retained failures and limits

- First clean restore exposed an SDK restore-order error (`NETSDK1112`): analyzer properties appeared only after package targets loaded. Ordinary SDK properties now appear in the generated application before first restore. `failures/fresh-restore.log` retains the diagnostic.
- First development attempt requested watcher startup with `build=false`; the coordinator correctly rejected `watch_build_required`. The driver now requests the required initial build.
- First AOT binary compiled, but receipt generation failed because a trailing backslash escaped the closing Windows argument quote. The source-directory argument now ends in `/.`, with no argument passed for the repository default. See `failures/receipt-path.log`.
- First VS Code attempt had two solution files and timed out waiting for the project system. The external harness now uses the consumer's existing complete solution. Second attempt mixed an 8.3 workspace path with full project paths; the language server treated the edited source as a separate file-based program and reported no code changes. The harness now canonicalizes the directory before editor launch. Both failure receipts are retained; the second confirms process cleanup. The subsequent complete pass is independent of those failures.
- A separate external Visual Studio consumer was built and prepared. Desktop app access timed out at the approval prompt before the walkthrough, so no external Visual Studio F5/Hot Reload/Stop acceptance is claimed. The unused test IDE process was stopped by retained identity; the pre-existing user IDE was preserved.
- CI coverage is authored under the existing required check and must pass on this PR. The noninteractive CI suite does not assert desktop pixels or IDE gestures.
- A clean Windows machine without the SDK/runtime, public package release, comprehensive frontend distribution notices, and final reconciliation of all milestone scenarios remain outside this evidence. No milestone completion claim follows from A18 alone.

## CI follow-up: partial log writes

[PR #26's first required run](https://github.com/sotsera/spar/actions/runs/39374534213) passed the managed tests, CoreCLR bridge/lifecycle, production and compatibility suites, local packing and external consumer suite. It failed in the final copied-AOT bridge probe: `Get-Content` returned an unfinished JSON record while the process was writing its `UserDataFolder` string. `ConvertFrom-Json` raised an unterminated-string error. The saved stdout subsequently contained 138 valid newline-terminated records; the harness then forcibly cleaned up the probe. This failed run does not prove graceful completion. The error and failed receipt are retained under `ci-framing/`.

The external package suite and editor lane had been exercised locally before PR submission; the separate legacy native/bridge probe was not rerun for that package change. That distinction matters: local success did not cover this exact CI path, and earlier probe passes did not expose the write timing.

The native host, bridge and production drivers now share `Read-ProbeEvents.ps1`. Polling parses only newline-terminated records from a shared-read snapshot. A partial trailing record waits for the next snapshot; malformed complete JSON still fails immediately. After actual process exit and output drain, `-Completed` rejects any unterminated record rather than silently discarding it.

`Test-ProbeEvents.ps1` holds a real writer open and splits JSON, CRLF and a multibyte UTF-8 character across flushes. It reproduces the old reader's exception deterministically and verifies the corrected reader, final truncation detection and malformed-record rejection. CI runs this regression before native suites.

Local validation after the fix: the framing regression, solution build and all 65 managed tests passed. A fresh production publish passed Release boundary checks and copied execution. A separate `SparNativeProbe=true` AOT publish was copied outside the checkout and matched CI's four-file payload inventory. Both CoreCLR and copied-AOT native lifecycle suites and all three bridge scenarios passed without forced cleanup. `-VerifyRender` captures were reviewed for production, native initial/resized windows, and both bridge document types. Receipts, representative captures and changed-source hashes are retained in `ci-framing/`; the corrected head still needs its own upstream CI result.
