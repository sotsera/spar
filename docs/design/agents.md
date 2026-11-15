# Agent facilities and observability

Part of the Spar design. [`docs/plan.md`](../plan.md) has the goal, the settled decisions, the open questions and the milestones, and lists every design file. Section numbers are global: "section 6" anywhere in the plan, the design files or `docs/decisions.md` means the heading of that number, wherever it lives.

## 6. Agent facilities

- **Devtools:** an in-process devtools endpoint, only in Development and test builds, enabled per session. A lockfile with a bearer token. Small tools:
  - `tree`, `screenshot`;
  - `click`/`fill`/`waitFor`;
  - `contracts`, `call`, `calls`, `logs`;
  - `version`.

  They read the host's instrumentation, so `calls` and readiness come for free. On Linux the same tools need a non-CDP implementation.
- **`spar dev`:** builds, launches, and reloads on request. A failed reload returns compiler diagnostics at file:line in-band.
- **`spar snap`:** a one-shot capture with named scenes and a fixed `TimeProvider`. It builds or reuses the development build, starts it offscreen, waits for UI ready, optionally replays an input script (steps addressed by accessible name or test id), writes a PNG, the accessibility tree and the bridge call log, then exits. No session, no port.
- **`spar doctor`** (user decision): checks the machine and the project, and reports each finding with a stable id, a severity and the fix. Text by default; `--json` and distinct exit codes for agents.
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
- **The `spar` tool** ships with the same version as `Spar.Sdk` through the local tool manifest (`dnx spar …` also works).
- **Agent guide:** ships in the SDK in Agent Skills format.
- **Skills for app authors, as a plugin** (user, 2026-11-14): besides the copy inside the SDK, the app-author skills are published as an agent plugin that users install, from a marketplace in this repository: the plugin in `plugins/spar/`, the marketplace file at the root (decided, user, 2026-11-14).
  - These are different from `.claude/skills/`, which guide work on Spar itself.
  - The plugin version follows the SDK version, so a skill never describes an API the installed SDK lacks.
  - Its skills get the same evaluations as the other skills.
- **Evaluation:** uses the .NET skills `eval.yaml` format (prompts, graders, tokens, time, activation, pass rate).
  - Profiles compared: plain `dotnet` tools, then `spar` CLI, then `spar` MCP. Only one interactive surface survives. Unused tools are removed.
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
  - M2: the optional layer, client export and the Aspire dev experience.
- **Later:** a Spar-owned minimal OTLP exporter (parked).
