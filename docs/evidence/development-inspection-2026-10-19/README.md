# Structured native inspection evidence — 2026-10-19

Executed locally on Windows 10.0.26220 with .NET SDK 10.0.402, PowerShell 7.6.5, Node 24.21.0, and the installed WebView2 Runtime. Baseline: upstream squash `947e669cd94b5d08d994b9b90309c90fa49b4ff7`; implementation inputs are hashed in `implementation.sha256`. The [guide](../../15-development-inspection.md) describes the protocol and its limits.

## A12: actual agent-driven edit verification

`eng/Test-DevelopmentInspection.ps1` starts an isolated React/backend fixture through the real coordinator with `--inspect`, without `--probe`. It uses the same generic operations available to a CLI owner. [Commands](commands.jsonl), [public events](coordinator.jsonl), [initial DOM](initial-snapshot.json), [edited DOM](edited-snapshot.json), [console/errors](frontend-logs.json), and the [cleanup receipt](inspection-receipt.json) are retained.

- Typed text into the actual input; clicked local/backend counters; a duplicate mutation ID was rejected and the backend counter stayed at one.
- Edited the React heading and backend greeting, then observed the new text through generic snapshots. Both counters and typed text survived the updates. The [edited viewport](edited.png) was visually reviewed: heading, greeting, state, and connection status match the transcript.
- Captured an intentional console marker and JavaScript exception. Introduced a C# compiler error, collected its CS0103 diagnostic through `diagnostics`, corrected it, and observed the recovered greeting.
- A 50 ms timeout on a 1,200 ms synchronous UI action reported unfinished work. A second mutation was rejected as busy; the original action later completed once. No mutation was replayed.
- Rejected incorrect session, generation, contract, document, oversized selector/text, invalid timeout, and arbitrary evaluation requests. Reloaded the page in the same process, rejected its old document, restarted the backend, and rejected the retired generation. The [restarted viewport](restarted.png) was visually reviewed: counters reset, text input empty, corrected greeting connected.
- Stopped while another timed-out native action remained unfinished. The callback completed against a retired document before graceful session cleanup; an unknown outcome was not reported as success. Actual retained process handles exited and an unrelated Node process survived.
- A separate session without opt-in rejected inspection. A separate broken-build restart returned correlated `build_failed` completion and `status: stopped`, followed by observed cleanup.

These establish A12 for the documented CLI/native surface. The snapshot is bounded DOM data; actions use synthetic DOM events. They do not establish OS input, a full accessibility tree, frames/shadow roots, or completion of asynchronous work triggered by a click. Capture receipts deliberately say review is required; the visual review above supplies that separate evidence.

## Regressions and production boundary

| Check | Observed outcome |
| --- | --- |
| Required solution build | Passed, zero warnings/errors |
| Required solution tests | 51 passed, zero skipped |
| Backend updates with native capture | Three scenarios passed: patch/error/restart, startup/dependency/build races, explicit force during retirement. [Receipt](backend-receipt.json); reviewed [patch](backend-patched.png) and [latest recovered generation](backend-recovered.png). |
| Session ownership | Ten scenarios passed, including HMR/restart, owner EOF/crash, malformed input, frontend/application exits, startup/port failures, and credential denial. [Receipt](session-receipt.json). No desktop capture requested in this regression run. |
| Project launch | Four automated lifecycle cases passed: native close, simulated debugger termination, contract retirement, coordinator crash. [Receipt](project-launch-receipt.json). This is not a new manual Visual Studio acceptance pass. |
| Installed VS Code debugger | Native breakpoint/Hot Reload/restart/contract/error/Stop scenario passed; 45 retained processes exited. [Receipt](vscode-receipt.json); [patched native window](vscode-patched.png) reviewed. |
| CoreCLR native host and bridge | Lifecycle/error/ownership checks and typed roundtrips/close-with-work passed. [Host](coreclr-host-receipt.json), [bridge](coreclr-bridge-receipt.json). |
| Strict NativeAOT publication | Passed; copied payload contains only the EXE, WebView2 loader, license, and notice. No development or compiler assemblies. |
| Copied AOT native host and bridge | Passed; actual render/resize and typed bridge success captures reviewed. [Host receipt](aot-host-receipt.json), [bridge receipt](aot-bridge-receipt.json), [initial](aot-host-initial.png), [resized](aot-host-resized.png), [typed success](aot-bridge-bridge.png). |
| Release inspection exclusion | Metadata/resource check confirms no inspection API, types, callbacks, or embedded script. [Receipt and assembly hash](release-boundary.json). |

The required Windows CI job now includes native inspection and Release exclusion. CI results belong to the final PR head; local passes do not establish a future CI result. The existing manual Visual Studio evidence remains in its [own record](../project-launch-2026-10-19/README.md). Full production SPA asset delivery and final production-control exclusion remain issue #10.

## Failures and artifact handling

The final transcript retains expected failure outcomes (compiler failure, frontend exception, invalid/stale requests, busy rejection, and unfinished timeout) alongside successful recovery. The failed-restart transcript is retained separately. Public details redact session credentials and the scoped HTTP path; screenshot filenames are generated by the application.

An early local test attempt failed because its duplicate-ID assertion reused the original successful completion instead of waiting for the new rejection. The driver was corrected to wait explicitly for `command-rejected`; subsequent full runs passed. That initial attempt remains under ignored `artifacts/inspection/probe/native-inspection-6b9d21f1c3a149ef9dcc5c58557d70de` and is not counted as acceptance. Tests preserve partial logs on failure; no fixture junctions were recursively deleted.

No manual IDE step is needed for this new inspection surface. Optional memory observation, an MCP adapter, editor inspection toggles, and broader consumer layouts remain follow-ups.
