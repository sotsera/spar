# Agent facilities and observability

Part of the Spar design. [`docs/plan.md`](../plan.md) has the goal, the settled decisions, the open questions and the milestones, and lists every design file. Section numbers are global: "section 6" anywhere in the plan, the design files or `docs/decisions.md` means the heading of that number, wherever it lives.

## 6. Agent facilities

- **Devtools:** an in-process devtools endpoint, only in Development and test builds, enabled per session. A lockfile with a bearer token. One core in C#, with the `spar` CLI and an MCP server (`spar mcp`, opt-in) as thin adapters over it, both kept after the evaluation (decision "Both devtools surfaces kept"); the endpoint is a local socket with an owner-only ACL (a named pipe on Windows, a Unix socket in the app's runtime folder on Linux), never a port. Page input on the developer's desktop is in-process CDP `Input.*` on Windows and the injected DOM script on Linux, each result marked synthetic there (decision "M2's decide-first questions"). Small tools:
  - `tree`, `screenshot`;
  - `click`/`fill`/`waitFor`;
  - `contracts`, `call`, `calls`, `logs`;
  - `version`.

  They read the host's instrumentation, so `calls` and readiness come for free. On Linux the same tools need a non-CDP implementation.

  **M2a's shape** (decision "M2a's decide-first questions"): on in every Development run and absent from every other variant (`SparDevtools=false` leaves it out); found through `obj/spar-devtools.json` (process, run, socket, token, build identity); newline-delimited JSON-RPC 2.0 on the socket; the window tests' page engine (`PageEngine.js`) moved into the hosts and shared, so elements are found as the tests' locators find them; elements named by a `tree` reference (`e12`, `stale_target` once the document changed) or a locator; `call` through the page's own dispatch with the devtools as caller, `calls` the last 200 with the UI action behind each, `logs` merging the backend log, the page's console and errors, and CSP violations; flat CLI commands (`spar tree`, `spar click <target>`, `spar call <contract.command> [json]`, …) with `--json` and `spar doctor`'s exit codes; `screenshot` the page alone as PNG. **Built in M2a** (decision "M2a: the devtools (implementation record; choices for review; 2026-11-26)", `docs/evidence/m2a-devtools.md`, user guide `docs/guide/devtools.md`): the run's id travels as `run`, an action lists the calls made from its start until the app is quiet, the engine and the contract snapshot are embedded in the app's assembly by Spar.Sdk, and loopback has the backend tools only.
- **`spar dev`:** builds, launches, and reloads on request. A failed reload returns compiler diagnostics at file:line in-band. It is Spar's own supervisor, not a wrapper of `dotnet watch`: the development server is kept across restarts, every process it starts goes in a job object (Windows) or a process group (Linux) from creation with a closed stdin, and Hot Reload stays with `dotnet watch` and the IDEs (decision "M2's decide-first questions"). **Built in M2b** (decisions "M2b's decide-first questions" and "M2b: spar dev (implementation record; choices for review; 2026-11-26)", `docs/evidence/m2b-spar-dev.md`, user guide `docs/guide/spar-dev.md`): `spar dev [-lp profile] [--private]`, `spar reload [--json]` and `spar dev --stop` over the session's own owner-only socket (`obj/spar-dev.json`); the app runs from a copy of the build under `obj/spar-dev/` and is stopped through the devtools' `stop`, the host's own session end; a restarted window on Windows takes the previous one's place without the focus.
- **`spar snap`:** a one-shot capture with named scenes and a fixed `TimeProvider`. It builds or reuses the development build, starts it offscreen, waits for UI ready, optionally replays an input script (steps addressed by accessible name or test id), writes a PNG, the accessibility tree and the bridge call log, then exits. No session, no port. Scenes are C# in the app's test project, run through the test host (the app's own `Program.cs` with service overrides and the safety defaults), so the app's own builds carry no scene code. **Built in M2c** (decisions "M2c's decide-first questions" and "M2c: spar snap and scenes (implementation record; choices for review; 2026-11-26)", `docs/evidence/m2c-spar-snap.md`, user guide `docs/guide/spar-snap.md`): `[SparScene("name")]` methods in the end-to-end test project, each also a window test; `spar snap <scene>...|--all|--list` builds that project and captures each scene through its test executable into `obj/spar-snap/<scene>/` (`page.png`, `tree.txt`, `tree.json`, `calls.json`, `problems.txt`); no input script (decided: the steps are the scene's C#); the same PNG every run on one machine.
- **`spar doctor`** (user decision): checks the machine and the project, and reports each finding with a stable id, a severity and the fix. Text by default, showing only what needs attention, the project it checked and how many checks passed, with `--verbose` for every check (decision "The M1d check follow-ups"); `--json` (every finding) and distinct exit codes for agents.
  - **Machine:**
    - the .NET SDK against `global.json`, and `Spar.Sdk` resolution;
    - Node and npm versions;
    - on Windows, the WebView2 Runtime, the AOT prerequisites and long paths;
    - on Linux, WebKitGTK, GTK, ICU, OpenSSL, plus `clang` and `zlib` for AOT, each with the install command;
    - Docker and Playwright browsers, only when the end-to-end tests need them.
  - **Project:** the `SparFrontendPath` folder, the `@spar/app` dependency, the Vite plugin, the `build` script, the lockfile and `node_modules`, the contract snapshot, the launch profiles.
  - **Third-party notices** (user, 2026-11-15; decision "Source maps stay out of the executable; third-party notices are reported and copyleft is flagged"): the packages the executable's notices cover, with packages that have no licence or a copyleft one (GPL, AGPL, LGPL) flagged.
  - **Shared checks:** the project checks are the same code the build uses for its diagnostics.
- **`spar init frontend`** (user decision): wires an existing frontend (made with `npm create vite`, a Vue scaffolder, …) into a Spar app.
  - It adds the dependency, the Vite plugin and, if needed, `SparFrontendPath`.
  - It shows the diff before writing, or prints the snippet when `vite.config` is too unusual.
  - `--framework react|vue` can run the official scaffolder first.
- **The `spar` tool** ships with the same version as `Spar.Sdk` through the local tool manifest: the package `Spar.Tool`, its command `spar`, run as `dotnet spar …` in an app (or `dotnet tool run spar …`), and as `dnx Spar.Tool …` without a manifest; `dnx spar` would fetch the runtime package `Spar` (decision "M1d step 6b's shape: the spar tool"). As built (M1d step 6b; decision "The spar tool (implementation record; choices for review; 2026-11-22)", `docs/evidence/m1d-spar-tool.md`): `src/Spar.Tool`, framework-dependent, AOT- and trim-clean; `spar doctor` with the machine, project and identity checks above, findings `{id, area, severity, message, fix}`, text or `--json`, exit codes 0 (nothing to fix), 1 (warnings), 2 (errors), 64 (usage), 70 (the tool failed); `spar init frontend` with `--framework react|vue` through `npm create vite@9.3.4`, a diff and a question before writing, `--yes`, nothing written without a terminal and without `--yes`, exit 3 when Vite's configuration got the snippet instead; the build identity block, which the host also writes in its first log line.
- **Agent guide:** ships in the SDK in Agent Skills format.
- **Skills for app authors, as a plugin** (user, 2026-11-14): besides the copy inside the SDK, the app-author skills are published as an agent plugin that users install, from a marketplace in this repository: the plugin in `plugins/spar/`, the marketplace file at the root (decided, user, 2026-11-14).
  - These are different from `.claude/skills/`, which guide work on Spar itself.
  - The plugin version follows the SDK version, so a skill never describes an API the installed SDK lacks.
  - Its skills get the same evaluations as the other skills.
- **Evaluation:** uses the .NET skills `eval.yaml` format (prompts, graders, tokens, time, activation, pass rate).
  - Profiles compared: plain `dotnet` tools, then `spar` CLI, then `spar` MCP. Only one interactive surface was to survive; the evaluation could not tell them apart, and both are kept, the CLI the one the skills teach and the MCP server opt-in (decision "Both devtools surfaces kept"). Unused tools are removed.
  - The first run: Claude Code headless, 10 tasks on the Notebook and a new app, the 3 profiles, 3 runs each, reporting pass rate, tokens and time.
  - Spar's own skills get evaluations too, and a skill that doesn't help is removed.

## 6b. Observability: optional OpenTelemetry, backend and client (user decisions)

- **Always on, BCL only (M1 core):** one `Activity` and one `spar.call.duration` sample per bridge command. No listener means no work; about +105 KB.
  - **Built in M1a** (decisions "Bridge instrumentation built" and "Bridge instrumentation: every call, from receipt, with a flood budget"): the activity source and the meter are both named `Spar`.
    - **Spans:** `{contract}/{command}`, kind Server, from the message's arrival, a child of the page's `traceparent` (else a new trace, never an ambient activity), tagged `rpc.system=spar`, `rpc.service`, `rpc.method`, `spar.call.id`, the request and response sizes, `spar.queue.seconds` and `spar.outcome`, with a `started` event when the command gets its slot. A failure sets status Error and `error.type` to its code; an exception adds an `exception` event. Every admitted call gets one; a call refused at the door gets one within the document's budget (20, then 5 per second).
    - **`spar.call.duration`:** a histogram in seconds, one sample per call that names a command, from arrival to outcome (queue time included), tagged `rpc.system`, `rpc.service`, `rpc.method`, `spar.outcome` (`success`, `error` for a declared error, or the failure's kind) and, for failures, `error.type`.
    - **Names the app does not have** are `unknown` in span names and tags. With an activity, a failure's correlation id is its trace id.
- **Optional layer (an opt-in package, never required):** the OpenTelemetry SDK plus the OTLP exporter, for traces, metrics and logs.
  - It starts only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set; no endpoint is configured by default.
  - +4.3 MB on an app that already uses HTTPS; AOT-clean, with export verified from the AOT exe.
- **Client telemetry through the backend:**
  1. The page uses the OpenTelemetry JS web SDK (opt-in module, for example `@spar/app/telemetry`), exporting to a relative path such as `/_spar/otlp/v1/traces`.
  2. Each host serves that path, and the backend forwards the bytes unchanged to the configured OTLP endpoint, only when the layer is enabled.
  3. `traceparent` on every call message gives one trace from a page interaction to backend spans (HTTP, SQL).

  No CORS, and no credentials in the page. Verified on Windows, loopback and Linux: every page call span had its backend child.
- **Split:**
  - The core SDK and hosts carry the tracing hook, trace context on every call, `/_spar/otlp/*` routing (404 without a relay), and a bounded flush before a desktop window closes (without it no page spans arrived).
  - The optional `Spar.OpenTelemetry` package holds the SDK, exporter and relay. The relay forwards bytes unchanged (http/protobuf or gRPC), adds `OTEL_EXPORTER_OTLP_HEADERS`, and backs off with an immediate 503 after a failure.
  - The optional page module loads lazily (27 KB gzip), only when the host reports telemetry on. Nothing waits before mount (user decision):
    - desktop hosts put the settings in the script they inject at document creation, read synchronously;
    - loopback asks `POST /_spar/otlp/config` in parallel with the mount;
    - an app without the layer has no telemetry code and makes no request.
  - No click spans by default (an app opt-in); the SDK offers an explicit span to group an interaction's calls. No `zone.js`.
  - Service names come from `OTEL_SERVICE_NAME`; the page reports as `<name>-page`.
  - Still to cover: trace context on streams and subscriptions.
- **Milestones:**
  - M1: the BCL instrumentation and `traceparent` in the protocol;
  - M2: the optional layer, client export and the Aspire dev experience: first a documented AppHost recipe that starts the app as an executable resource with the dashboard's OTLP variables, and a hosting package only if the recipe needs more than a few lines.
- **Later:** a Spar-owned minimal OTLP exporter (parked).
