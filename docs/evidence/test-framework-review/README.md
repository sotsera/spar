# Test lessons from the M0 spikes (review, 2026-11-14)

The user asked for every spike to be reviewed for its testing lessons before the test framework is designed. Three read-only reviews covered 47 spikes; `page-heartbeat` was still running and is not included. Nothing was built or run for the review. The proposal built from it is in `docs/plan.md` section 5, "Test framework proposal".

Groups:
- **Build and contracts (20):** `d1-pipeline`, `d3-variants`, `asset-packing`, `package-consumer`, `npm-ignore-scripts`, `esproj`, `contract-syntax`, `contract-prototypes`, `app-model-check`, `ts-results`, `ts-compiled-contracts`, `result-api`, `spar050`, `msbuild-task-analyzer`, `serializer-bench`, `serializer-fuzz`, `wire-details`, `test-frameworks`, `aspire`, `TestResults`.
- **Windows host (15):** `d8-automation`, `desktop-origin`, `csp-navigation`, `theme-startup`, `windows-titlebar`, `windows-sandbox-checks`, `webview-profiles`, `startup-prerequisites`, `native-services`, `cswinrt`, `resource-usage`, `loopback`, `react`, `bridge-encoding`, `browser-telemetry`.
- **Linux, frontend and data (12):** `linux-desktop`, `linux-wsl-desktop`, `linux-integration`, `linux-memory`, `linux-loopback`, `webkit-wasm-off`, `sqlclient-aot`, `sqlclient-linux`, `sqlclient-tool`, `frontend-frameworks`, `call-promise-cost`, `stream-result-cost`.

## Cross-cutting lessons

Counts are the number of spikes that show the lesson.

1. **Timing noise and stalls (13 build, 5 Windows, 5 Linux).**
   - Causes seen: concurrent agents and builds, about 5 s process-start stalls (MSBuild nodes and TaskHosts, WebView2 first controller), first-run penalties, 15.6 ms Windows timer ticks, 1 ms WebKitGTK clocks, and other WebKit apps on the machine cutting PSS by up to 66 MB.
   - What worked: interleaved runs, medians with ranges, same-process ratios, in-process timers, and detecting foreign processes.
2. **Explicit readiness and launched-process lifecycle (8 Windows, 7 build, 5 Linux).**
   - **Readiness:** a ready line on stdout (`SPAR_READY` with URL, token and fingerprint), `ui-ready`, a double `requestAnimationFrame` before the first input, or a ready POST.
   - **Lifecycle:** close stdin to stop, kill with the parent, kill after a deadline, then assert that no processes are left.
   - **Failures seen:** a dead host blocking a FIFO, a crash in a callback that stayed silent, `dotnet watch` trees left running, zombies in Docker without `--init`.
3. **Offscreen, shown, never-activated windows (11 Windows).** A hidden window never renders on either engine, and `ui-ready` never comes. Anything that needs focus fails offscreen: clipboard, user activation, the first click on an inactive window. Those go to Windows Sandbox or to the user.
4. **Profiles and process ownership (8 Windows, 5 Linux).**
   - **Profiles:** one profile per run, deleted only after the browser has exited, with a timeout. Exits of 6.4–6.5 s came from SmartScreen, now off by default.
   - **Ownership:** only kill or count what the test started. Track processes by `--user-data-dir` or a job object. 14 foreign `msedgewebview2` processes were present in one run.
5. **Isolation from the user's machine (8 Windows, 7 Linux, 6 build).**
   - **Linux:** private `HOME`, every `XDG_*` folder, `XDG_RUNTIME_DIR` and a D-Bus session per run. `linux-integration` wrote to the real WSL home before this rule existed.
   - **Windows:** a camera prompt once reached the user's desktop (`csp-navigation`). Real input, dialogs, tray icons and theme changes ran only in Windows Sandbox.
   - **Builds:** MSBuild server off, no node reuse, a unique compiler id, isolated NuGet and HTTP caches, unique package versions, a clean-clone scenario.
6. **Printed but not asserted, or "skipped" reported as passed (9 build, 1 frontend).**
   - Probe tables, `RuntimeProbes` returning 0, engine probes exiting 0 when the driver was missing, negative TypeScript files checked by hand.
   - Most frontend in-page tests recorded rather than asserted.
7. **Exact expected-diagnostic sets (8 build).** C# generator and analyzer tests, the TypeScript negative files, and the MSBuild task analyzer compare an exact set of diagnostic IDs and locations. Only four automate it. Info diagnostics are visible only in SARIF.
8. **Differential oracles (7 build).** Generated serializers against System.Text.Json, a strict reference parser, Node `JSON.parse`, `tsc --declaration`, and installs with and without scripts.
9. **JIT/AOT parity (6 build, 3 data).** The same tests or seeds run JIT and AOT, with exception messages normalised.
10. **Fakes and seams carried the tests (7 Windows, frontend).**
    - `TimeProvider`, service hooks and the in-process factory.
    - A file-dialog seam and fake media devices.
    - Fault-injection delays, an external-hit recorder and an OTLP sink.
    - The scripted fake transport (`setTransport`) for pages.
11. **In-page harnesses reporting to the host (5 Linux and frontend).** Each spike used a different reporting channel. A WebKitGTK view that is never presented throttles its timers, so pages need a host tick or a visible window.
12. **Racy or vacuous waits (6 Windows).**
    - A non-awaited predicate passed at once.
    - CDP calls kept running after their timeout.
    - Fixed sleeps, and `IDOK` sent before the dialog was visible.
    - Telemetry lost without a flush before close.
13. **Structured output plus summarizers (8 Windows).** JSON lines and tagged log lines, with results kept in files, because MTP hides the output of passing tests.
14. **The production boundary was checked by string scans only (3–4).** `AGENTS.md` requires a test. `d3-variants`' marker-byte scan is the strongest pattern seen.
15. **Test outputs out of authored folders (4).** `spikes/TestResults/` was created by a `dotnet test` run from `spikes/`; `--results-directory` under `obj/` avoids it.
16. **Proving incremental builds (5 build).**
    - Target-skip lines read from binlog replay.
    - Write-time snapshots.
    - "Authored files untouched" checks by hash and modification time.

## Reusable pieces found

- **Build:**
  - `contract-syntax/Harness.cs`, which asserts `(id, located text)` diagnostics;
  - the `spar050` corpus format (`Case`/`Kind`/`Why` headers and `//~ ID` markers over SARIF);
  - `app-model-check/tools/check-bad.mjs`, an exact-code TypeScript negative runner;
  - `serializer-fuzz` (seeded cases, a strict oracle, child-process crash isolation, a ddmin minimiser);
  - `d3-variants/scripts/Isolate.ps1` and its marker scan;
  - `package-consumer/run-consumer.ps1`.
- **Launch and lifecycle:**
  - `loopback/LoopbackApp.cs`: ready line, stdin lifecycle, `KillOnParentExit`;
  - `react/InProcessApp.cs` and `SparTestHooks.cs`: the in-process factory, 20–27 ms warm start;
  - `webview-profiles/ProcessTools.cs`: processes tracked by profile folder.
- **Desktop:**
  - `d8-automation/Devtools/Automation.cs`: in-process CDP tree, selectors, trusted input;
  - `theme-startup` frame capture;
  - `windows-titlebar` native UIA dump and the Sandbox input guard;
  - `windows-sandbox-checks` Sandbox runner.
- **Linux:**
  - `linux-integration/wsl/lib.sh`: `start_host`, `send`, `wait_mark`;
  - `xtool.c`: XTEST and window-manager control;
  - `linux-memory/wsl/env.sh`: isolation;
  - the `linux-loopback` and `linux-desktop` Dockerfiles;
  - library hiding with bwrap (`linux-wsl-desktop`, `sqlclient-linux`).
- **Frontend:**
  - `frontend-frameworks/spar-app-src/sdk/testing.ts`: the scripted fake transport;
  - `shared/harness.ts`;
  - `tools/run-csp.mjs`: a CSP-enforcing server with Playwright.
- **Measurement:**
  - the `call-promise-cost` and `stream-result-cost` cross-engine runners (Node, WebView2, WebKitGTK) and their summarizers;
  - the `serializer-bench` BenchmarkDotNet configuration with an equality guard in `GlobalSetup`.
- **Data (SQL Server tool, not Spar):**
  - the `sqlclient-aot` and `sqlclient-linux` container fixtures (free port, secret through the environment, readiness from log line plus query, guaranteed removal);
  - the checksum fingerprint of seed data;
  - cancellation checked on the server.

## Open questions the reviews raised

They are carried into the plan's proposal as decisions for the user.
