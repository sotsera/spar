# Production delivery evidence — 2026-10-19

Baseline: merged upstream `9c1f1833a21a4ae45b7d5f448545e4ca8a26e8b4` (PR #23), with the implementation identified by [source hashes](implementation.sha256). Windows build 26220, .NET SDK 10.0.402, PowerShell 7.6.5, Node 24.21.0/npm 11.19.0, WebView2 155.0.4283.62. The [delivery guide](../../16-production-delivery.md) contains commands and the public bundle contract.

## A14: the actual production application

The normal command was `dotnet publish samples/Spar.NativeSample -c Release -o artifacts/production/final-publish --disable-build-servers`. It built the React SPA and matching generated contract, then published NativeAOT. [Publication receipt](publication.json) records the dirty-tree source snapshot, tools, contract/archive identity, and file hashes. The final copied executable SHA-256 is `47B52FB91125400356DC8616B7863AE56D06A8ACBE00D5FFD292FECF0C1AB4A1`.

`eng/Test-Production.ps1 -PublishDirectory artifacts/production/final-publish -EvidenceDirectory artifacts/production/verified -VerifyRender` copied the non-symbol output to a fresh directory under `%TEMP%`, outside the repository. All three runs reported NativeAOT, completed a typed greeting, rendered native pixels, exited normally, released their profile lease, and observed every retained browser process exit. No Node, dotnet, or coordinator process appeared in the application's descendants. No frontend server was launched; the document and assets used the intercepted `.spar.invalid` native origin. [Receipt and resource samples](production.json), [first run](run-1.jsonl), [second](run-2.jsonl), [third](run-3.jsonl).

The [fresh-profile screenshot](run-1.png) and subsequent captures were reviewed: the production heading, local/backend counters, input, `Hello, Spar`, `Connected`, and refresh button are visible. A separate computer-use pass opened the exact copied executable, observed the actual native accessibility tree, clicked **Backend counter**, waited for the asynchronous result, and confirmed **Backend counter: 1** in both pixels and accessibility text. See [counter screenshot](counter.png) and [native accessibility observation](counter.txt). This used external Windows input/inspection; no Spar development endpoint or browser debugging port was enabled. The window was closed afterward.

Production rejects `--spar-dev`, `--bridge-probe`, `--asset-probe`, and `--inspect`; the retained JSONL outcomes show exit failure rather than session/probe activation. An explicit nonexistent runtime directory produced an actual native startup failure: [missing-runtime.jsonl](missing-runtime.jsonl) names the WebView2 Evergreen Runtime and HRESULT `0x80070002`.

The [production boundary check](production-boundary.json) inspects Release metadata and the actual embedded ZIP. Inspection APIs/callbacks/script, development/compiler assembly references, old probe documents, and frontend development/report effects are absent. Only the production entry point and asset resource ship. Test-operation names remain in the shared sample contract, but production grants only greeting/counter/delay permissions.

## Measurements

These are small developer-machine observations, with warm OS/tool caches and no performance target. The three publication samples rebuild/type-check the SPA and publish AOT with restored tool/dependency caches: **13.944, 13.674, 14.007 seconds** ([raw measurements](publication-measurements.json)). They are not clean restore measurements.

The final non-symbol payload, including its source receipt, totals **4,155,218 bytes**. Browser installation and writable profile data are additional. Process-start to backend greeting completion was **556.9 ms** with a fresh profile, then **316.9 and 318.1 ms** with a reused profile. This timing uses native process/receipt timestamps; it is not a time-to-visible-pixels claim. The harness also retains its separate launch-to-observation duration.

One memory snapshot was taken per run after greeting/rendering. Host private bytes were **8.44 / 8.52 / 8.55 MiB**, with working sets **27.07 / 26.86 / 26.87 MiB**. The six attributed WebView2 processes totaled **169.41 / 157.93 / 157.04 MiB** private bytes and **334.80 / 315.03 / 309.77 MiB** working set. These are environment process totals, not exact page RAM; summed working sets can count shared resident pages more than once. Process IDs and creation timestamps are retained in the receipt. Development-tool memory is excluded.

## Regression evidence

| Check | Observed result |
| --- | --- |
| Solution build | Zero warnings/errors; [log](final-build.log). |
| Managed tests and generated TS calls | 65 tests passed, none skipped; [log](final-tests.log). `npm test --prefix eng/bridge-client-tests` passed. Production publish also type-checked/built the SPA. |
| Real CoreCLR and copied AOT bridge | Three cases each passed, including packaged CSS/JS, missing/query/foreign/metadata resource rejection, stale contracts, denied handlers, retired replies, navigation denial, and close with unfinished work. [CoreCLR](bridge-coreclr.json), [AOT](bridge-aot.json). Both packaged success captures were reviewed: [CoreCLR](coreclr-asset-bridge.png), [AOT](aot-asset-bridge.png). |
| Native lifecycle | Seven scenarios passed in each mode: [CoreCLR](native-coreclr.json), [AOT](native-aot.json). The AOT resize capture was reviewed: [image](aot-native-resized.png). |
| Development session | All ten HMR/ownership/startup/cleanup scenarios passed; [receipt](development.json). |
| Native inspection | All three generic inspection/disabled/failed-restart cases passed after correcting the production-only import; [receipt](inspection.json), reviewed [edited](inspection-edited.png) and [restarted](inspection-restarted.png) captures. |
| Backend updates | All three patch/restart/build-race/explicit-force scenarios passed; [receipt](backend.json), reviewed [patched](backend-patched.png) and [recovered](backend-recovered.png) captures. |
| IDE project launch | Four automated lifecycle cases passed; [receipt](project-launch.json). This is not a new manual IDE debugger pass. |

The required Windows job adds production publication, boundary checks, and copied execution while retaining a separately published native/bridge test fixture. The fixture is explicitly built with `SparNativeProbe=true`; it is never the default production payload. CI and desktop evidence have distinct scope. Final PR CI must pass on its own commit.

## First failures and remaining gaps

An initial sandboxed build failed without compiler diagnostics; the same build succeeded outside that restriction. The first publish compiled AOT but its receipt command failed on a trailing Windows path separator; the corrected quoted path succeeded. An inspection fixture initially failed because it imported the new production target from a relocated Debug project. The import is now conditional on production assets, and the full inspection suite passed. One rebuild was started while the watcher regression still owned the shared coordinator binary, causing a file-lock failure; after the suite exited, the final build/test sequence passed. Failed attempts remain under ignored `artifacts/production/`; none counts as successful acceptance.

The final archive stream-size guard was verified by the final managed build/tests and copied production run; earlier native fixture regressions exercised the same asset serving/retirement implementation before that additional input guard. Required CI executes the complete final revision.

A clean Windows machine without the .NET SDK/runtime was unavailable. Carry this into release readiness; no clean-machine certification follows from the copied developer-machine application. Fixed-runtime redistribution, Windows minimum-version coverage, signing/installers, and updates remain unqualified. Explicit dependency compatibility (#11 / A15–A16) and external NuGet consumption/fresh setup (#12 / A18–A01) remain open. Existing VS Code/Visual Studio acceptance was not rerun manually for this production-only UI path.

The first upstream run [39327463938](https://github.com/sotsera/spar/actions/runs/39327463938) passed production assertions and printed their success receipt, but GitHub's PowerShell wrapper propagated the expected native exit code `1` from the missing-runtime negative test. The test script now explicitly returns `0` only after every positive and negative assertion succeeds. A local reproduction using the same `LASTEXITCODE` wrapper returned `0`; [its copied-app receipt](exit-code-regression.json) is retained. This first CI run is a failure, not acceptance of the complete required job; its later AOT-fixture stages were skipped and require the new run.