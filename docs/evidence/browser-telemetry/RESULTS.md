# Browser telemetry spike: results

> A spike. Its code was deleted with `spikes/` at the start of M2; it is in git history at [`spikes/browser-telemetry/`](https://github.com/sotsera/spar/tree/24f5db0942833047c81fa692908410596f3b8960/spikes/browser-telemetry) (commit `24f5db0`), and this record and its `results/` moved here. Paths under `spikes/` below name that history.

Recorded 2026-11-13 on the development machine: Windows 11, SDK 11.0.100, Node 24.21, Docker Desktop (Linux containers on WSL2), WebView2 Runtime as installed, Playwright for .NET 1.64.1 with `chromium-headless-shell` 1248, Aspire 13.6.5 (from the NuGet cache).

**Question.** Can the page of a Spar app use the OpenTelemetry JS SDK without CORS and without credentials in the page, the same way on the Windows desktop host, the Linux desktop host and loopback, and can one trace run from a page interaction through a bridge call into backend spans?

**Answer: yes, on all three hosts.**

- The page exports OTLP/protobuf to a relative path, `/_spar/otlp/v1/{traces,logs}`.
- The host forwards the bytes unchanged to the app's configured collector. The forward goes over OTLP/HTTP, or over gRPC by framing the same protobuf. The host adds the configured headers, such as an API key.
- The SDK carries W3C `traceparent` on every call:
  - as a header on `fetch` calls;
  - as a field of the message protocol on `postMessage` / WebKit message calls.
- `SparDiagnostics` makes the backend Activity its child.
- **Every page call span joined its backend span** on all hosts: 5,601 of 5,601 on desktop, 2,811 of 2,811 on loopback, 5,601 of 5,601 on Linux, in the final runs.
- The Aspire dashboard showed `click → click → RPC links/preview (page) → links/preview (backend) → HTTP GET example.com` as one trace for each host.

## Layout (copies; the original spikes were not edited)

| Path | Source | Changes here |
|---|---|---|
| `d1-pipeline/` | `../d1-pipeline` (Generator, Runtime, Tool, Tasks, `Spike.targets`) | SDK: `CallTracer` hook, trace context per call, `onClosing`, `hostHeaders()`. Runtime: `SparInvoke`, `SparTraceContext`, `ISparTelemetryRelay`, `SparTelemetryRoutes`, and the trace field in `q`/`s` messages. |
| `loopback/Loop.Loopback` | `../loopback` | `traceparent`/`tracestate` from request headers; `POST /_spar/otlp/{**rest}` behind the token and origin check. |
| `react/React.App`, `React.Desktop`, `React.Telemetry` | `../react` | Desktop host: trace context from headers and messages; `/_spar/otlp/*` behind the Origin check; asks the page to flush before closing. Telemetry layer: `OtlpRelay`, `System.Net.Http` spans, `OTEL_SERVICE_NAME`. Frontend: `telemetryBoot.ts`, `telemetry.ts`, `callBench.ts`, a link-preview button. SQL Server is off by default. |
| `linux-desktop/` | `../linux-desktop` (no Dockerfile; the existing image was reused) | Linux host: the same three changes. `Linux.App` gets `SparDiagnostics` and the optional layer. `frontend-dist/` is a copy of this spike's Windows-built bundle (telemetry + self-test variant). |
| `aspire/Telemetry.AppHost` | new | Aspire 13.6.5 AppHost: the published desktop exe as an executable resource with `WithOtlpExporter()`. |
| `tools/` | new | `otlp-receiver.mjs` (OTLP/HTTP receiver with a protobuf decoder and a trace-tree report), `summarize.mjs`, `size-probes/`, and `Telemetry.Driver` (Playwright: loopback journeys and dashboard screenshots). |

Outputs: `artifacts/browser-telemetry/` holds the receiver JSONL, logs and PNGs. Profiles: `artifacts/webview2-profiles/browser-telemetry-desktop-selftest` and `browser-telemetry-linux-gtk3`.

## How it was run

```powershell
# Receiver (or the Aspire dashboard), then the app with the standard variables:
node spikes/browser-telemetry/tools/otlp-receiver.mjs 4318 artifacts/browser-telemetry/run.jsonl
$env:OTEL_EXPORTER_OTLP_ENDPOINT="http://127.0.0.1:4318"; $env:OTEL_EXPORTER_OTLP_PROTOCOL="http/protobuf"; $env:OTEL_EXPORTER_OTLP_HEADERS="x-spike-key=secret%20123"
# Windows desktop (AOT):
dotnet publish spikes/browser-telemetry/react/React.App -c Release -p:SparTelemetry=true -p:SparSelfTest=true -o spikes/browser-telemetry/react/React.App/bin/publish-telemetry-selftest
spikes/browser-telemetry/react/React.App/bin/publish-telemetry-selftest/React.App.exe --selftest --offscreen --telemetry [--telemetry-bench] [--no-flush]
# Loopback (Debug, embedded bundle) driven by headless Chromium:
dotnet build spikes/browser-telemetry/react/React.App -c Debug -p:SparDevServerBuild=false
dotnet spikes/browser-telemetry/tools/Telemetry.Driver/bin/Debug/net11.0/Telemetry.Driver.dll loopback spikes/browser-telemetry/react/React.App/bin/Debug/net11.0/win-x64/React.App.exe [--bench] [--no-flush]
# Linux: container from spar-linux-desktop:webkit2.52 with /src (repo, read-only), /work (volume), /work/artifacts (repo artifacts),
# Xvfb :99, then sync.sh; in /work/spikes/browser-telemetry/linux-desktop:
./publish.sh gtk3 -p:SparTelemetry=true
OTEL_EXPORTER_OTLP_ENDPOINT=http://host.docker.internal:4318 ... ./run.sh gtk3 <label> --selftest --telemetry [--telemetry-bench] [--no-flush]
# Reports:
node spikes/browser-telemetry/tools/otlp-receiver.mjs --report artifacts/browser-telemetry/run.jsonl
node spikes/browser-telemetry/tools/summarize.mjs artifacts/browser-telemetry/final-desktop.log
```

## Design as built

```
page (React)                                host (desktop or loopback)                   collector
------------                                --------------------------                   ---------
main bundle: telemetryBoot  ── POST /_spar/otlp/config ──▶  404 (no layer / no endpoint)
   │ 200 {serviceName, scheduledDelayMs, logs}            200 from ISparTelemetryRelay.PageConfig
   ▼ import("./telemetry")  (lazy chunk)
TracerProvider + BatchSpanProcessor + StackContextManager
UserInteraction (click, submit) + DocumentLoad instrumentation
configure({ tracer })  ── every call ──▶  fetch: traceparent/tracestate headers
                                          message: q id contract command fp <trace> json
                                          ─▶ SparCall.Trace ─▶ SparInvoke(..., trace, ct)
                                          ─▶ SparDiagnostics: Activity(parent = page span) ─▶ HttpClient span
OTLPTraceExporter/OTLPLogExporter (protobuf, fetch keepalive)
  ── POST /_spar/otlp/v1/traces ─▶ origin/token check ─▶ OtlpRelay ── bytes unchanged ─▶ OTLP/HTTP or gRPC
                                                          + OTEL_EXPORTER_OTLP_HEADERS
host close ── "z" ─▶ onClosing(flush) ── "z" ─▶ host closes (waits ≤ 1 s)
```

### SDK (`d1-pipeline/Spike.Tool/SparSdk.ts`, generated into `@spar/sdk`)

- **No telemetry dependency.**
  - `config.tracer?: CallTracer` defaults to undefined. `start(contract, command)` returns a `CallSpan`: the `{ traceparent, tracestate? }` context to send, and `end(outcome, error?)`.
  - Without a tracer, nothing is added to the call.
- **Fetch calls** add the `traceparent` and `tracestate` headers.
- **Message calls** get a trace field after the fingerprint:
  - `q id contract command fingerprint <trace> json`;
  - `s id contract command fingerprint <trace> batch window json`.

  `<trace>` is `-` when the call has no context. Otherwise it is the traceparent, optionally followed by `;` and the URI-escaped tracestate (tracestate values may contain spaces).
- **`onClosing(work)`.** A desktop host posts `z` before it closes. The SDK runs the registered work (`Promise.allSettled`) and answers `z`.
- **`hostHeaders()`** returns the loopback token, so the page's exporter can call its own host on loopback.
- **Streams always send `-`.** Stream commands are not instrumented in the backend; see "Not tested".

### Hosts (all three)

- **Trace context reaches the backend.** `SparCallRegistry.Start(document, id, SparTraceContext)` stores it on the `SparCall`. `SparDispatch.InvokeAsync` passes it to the app's `SparInvoke`.
  - `SparTraceContext.FromHeaders` drops oversized values (traceparent over 128 characters, tracestate over 8 KB).
  - `FromField` parses the message field.
- **Routes under `/_spar/otlp/`** pass the same checks as bridge calls:
  - Windows: POST plus `Origin == https://app.spar.local`.
  - Loopback: bearer token plus Origin.
  - Linux: POST, with no Origin or `spar://app`, as for the existing bridge.

  `SparTelemetryRoutes.HandleAsync` then answers:
  - 404 `telemetry-disabled` when no relay is registered;
  - `config` with the page config;
  - `v1/{traces|logs|metrics}` through the relay.

  Bodies over 4 MB get 413, and content types other than protobuf or JSON get 415.
- **Desktop close** (Windows `WM_CLOSE`, Linux `Close()`): when a relay is registered and the page has said hello, the host posts `z` and keeps the window open until the page answers `z`, or for at most 1 s.

### Backend (`React.App/SparDiagnostics.cs`, always on, BCL only)

- **The page's span becomes the parent.** `ActivityContext.TryParse(traceparent, tracestate, isRemote: true)` supplies the parent of `StartActivity("{contract}/{command}", Server, parent)`. A malformed context starts a new trace; it never fails the call.
- **Nothing is parsed without a listener**: `HasListeners()` is checked first.
- **Tags:** `rpc.system=spar`, `rpc.service`, `rpc.method`.

### Optional .NET layer (`React.Telemetry`)

- **Activation.** It is referenced only with `-p:SparTelemetry=true` and starts only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, as before.
- **Outbound HTTP spans.** It now also listens to `System.Net.Http`, the BCL's own HttpClient activities, without the instrumentation package.
- **Service name.** It honours `OTEL_SERVICE_NAME`; the page reports as `<name>-page`.
- **`OtlpRelay`** implements `ISparTelemetryRelay`.
  - **Configuration.** It reads `OTEL_EXPORTER_OTLP_{SIGNAL_}ENDPOINT`, `_PROTOCOL`, `_HEADERS` (URL-decoded) and `_TIMEOUT`.
  - **Protocol.** The default protocol is `grpc`, like the .NET exporter. With `grpc` it frames the protobuf as one uncompressed gRPC message over HTTP/2 and maps `grpc-status`; JSON bodies over gRPC get 415. With `http/protobuf` it passes the collector's status and body through.
  - **No spans of its own.** The relay runs inside `SuppressInstrumentationScope` with `Activity.Current` cleared, so relaying creates no spans. `relay-spans=0` in every run.
  - **Collector failures.** A failed connection gives 503. For the next 5 s the relay answers 503 at once, without trying the collector. The page's exporter retries 429/502/503/504 itself, a bounded number of times. **The backend never replays anything.**
- **Page config:** `{ serviceName, scheduledDelayMs, logs }`. The delay is `SPAR_TELEMETRY_PAGE_DELAY_MS` (spike), else `OTEL_BSP_SCHEDULE_DELAY` (which Aspire sets), else 5000.

### Page (`frontend/src`)

- **Two build flags.** `__SPAR_TELEMETRY__` and `__SPAR_BENCH__` are Vite `define`s, driven by `SparTelemetry` and the bench switch in `React.targets` (`SPAR_TELEMETRY=1|0`).
  - Without the layer, the boot code is dead code: the production bundle contains no OpenTelemetry code and never calls `/_spar/otlp/config`.
- **`telemetryBoot.ts`** (main bundle, 2.4 KB raw) runs before `createRoot`:
  - it POSTs `/_spar/otlp/config`;
  - on 200 it `import()`s `telemetry.ts`;
  - it never rejects.

  Mounting waits for it, so that the user-interaction instrumentation patches `addEventListener` before React registers its root listeners.
- **`telemetry.ts`** (lazy chunk) wires up the SDK:
  - **SDK packages.** `@opentelemetry/sdk-trace` 3.1.1 (`TracerProvider`, browser `BatchSpanProcessor`, `StackContextManager`), `W3CTraceContextPropagator`, `sdk-logs` 3.1.1, and the `exporter-{trace,logs}-otlp-proto` 0.301.2 exporters.
  - **Exporters** use relative URLs, with `hostHeaders()` as their headers.
  - **Instrumentation:** `UserInteractionInstrumentation` for click and submit, and `DocumentLoadInstrumentation`.
  - **Call spans.** The `CallTracer` creates a CLIENT span per call and injects it through the global propagator. On failure it sets an error status and emits a WARN log record.
  - **Logs** come from uncaught errors and unhandled rejections, plus one "page telemetry started" record.
  - **`onClosing(flush)`** is registered; `flush` never rejects. The spike exposes its hooks as `window.sparTelemetry`.
- **Spike-only measurement hook.** `callBench.ts` (only in bench builds) times sequential `notes.count` calls on either transport.

## Results (observed)

### Joined traces

The **receiver** (`tools/otlp-receiver.mjs --report`) decodes the protobuf. It counts every page CLIENT span with `rpc.system=spar` that has a backend SERVER span as its child in the same trace.

| Host (final runs, 3 runs each with the call bench) | Page call spans | Joined with a backend span | Backend roots (tracing switched off in the page) | Relay spans |
|---|---:|---:|---:|---:|
| Windows desktop, AOT (`publish-telemetry-selftest`) | 5,601 | **5,601** | 5,580 | 0 |
| Loopback, JIT Debug + headless Chromium | 2,811 | **2,811** | 2,790 | 0 |
| Linux WebKitGTK 4.1, AOT, Docker + Xvfb | 5,601 | **5,601** | 5,580 | 0 |

Both desktop transports joined, `fetch` and messages; the bench alternates them.

A click on **Preview** (desktop, receiver):

```
spar-notes-page | click             kind=1 +0.00ms   0.00ms
  spar-notes-page | click           kind=1 +0.00ms   0.30ms
    spar-notes-page | links/preview kind=3 +1.00ms 250.50ms      (client span, from the CallTracer)
      spar-notes  | links/preview   kind=2 +1.58ms 249.05ms      (SparDiagnostics, parent = page span)
        spar-notes | GET            kind=3 +6.17ms 244.40ms      (System.Net.Http, example.com)
      log spar-notes Information span=<links/preview>
```

The Linux trace has the same shape (`spar-notes-linux-page` → `spar-notes-linux`). On loopback there is a third `click` span: React registers a capture and a bubble listener on its root, and the instrumentation wraps each one.

**Aspire dashboard 13.6.5:**

- **Standalone** (`Aspire.Dashboard.exe` from the `aspire.dashboard.sdk.win-x64/13.6.5` package; `Dashboard__Otlp__AuthMode=ApiKey`, frontend unsecured, ports 18888/18889/18890):
  - the desktop exe through gRPC (`dashboard-desktop-trace.png`);
  - loopback through OTLP/HTTP (`dashboard-loopback-trace.png`);
  - the Linux container through gRPC to `host.docker.internal:18889` (`dashboard-linux-trace.png`).

  Each trace detail showed "Resources 2, Depth 5, Total spans 5": `click`, `click`, `RPC links/preview` (page), `links/preview` (backend), `HTTP GET 200 example.com:443`.
  - The traces list (`dashboard-traces.png`) shows every page call trace spanning `spar-notes-page (1)` and `spar-notes-<instance> (1)`, plus `documentLoad` traces.
  - The structured logs view (`dashboard-logs.png`) shows the page's "page telemetry started" next to the backend's per-call logs.
- **AppHost** (`aspire/Telemetry.AppHost`):
  - **What was run.** The published desktop exe was an executable resource with `WithOtlpExporter()`. Aspire injected `OTEL_EXPORTER_OTLP_ENDPOINT`, `_PROTOCOL` (grpc), `OTEL_SERVICE_NAME`, `OTEL_RESOURCE_ATTRIBUTES`, `OTEL_BSP_SCHEDULE_DELAY` and more (`apphost-resource.png`). The resource ran the self-test and finished with exit code 0.
  - **What it showed.** The trace detail (`apphost-trace.png`) shows the same five spans under `spar-notes-desktop-page` and `spar-notes-desktop`.
  - **First run.** The backend's telemetry appeared as a separate `spar-notes` app, because the layer hard-coded its service name. Honouring `OTEL_SERVICE_NAME` fixed it.
  - **Settings.** This run used `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true` and `ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true`, so no API key was involved there. The API-key forwarding was verified with the standalone dashboard.

### Forwarded headers and failure handling

- **Headers reach the collector.** `OTEL_EXPORTER_OTLP_HEADERS=x-spike-key=secret%20123` reached the receiver as `x-spike-key: secret 123` on relayed page requests, on every host. The page never had it.
- **Wrong API key** (standalone dashboard, `x-otlp-api-key=wrong`): the relay passed through **401**, and the page exporter dropped the batch (non-retryable). The right key returned 200.
- **Collector down** (`127.0.0.1:4399`, desktop):
  - **First attempt.** It took **2.03–2.05 s**: a refused TCP connect on Windows. The result was 503 and `SPAR_OTLP_RELAY_FAILED`.
  - **During the 5 s backoff.** The relay answered 503 in 0.0–0.2 ms.
  - **The page retried** traces three times and logs four times. Its explicit flush then resolved **false after 9.4 s**.
  - **Close stayed bounded.** The close confirmation took 0.2 ms, because nothing was buffered any more, and the host's wait is capped at 1 s anyway.
  - **Before the fix:** the page's `forceFlush` rejected, which failed the self-test, and every retry waited 2 s.

### Nothing is sent when disabled

| Case | Page | Requests | Collector |
|---|---|---|---|
| Production build without the layer (`SparTelemetry=false`) | `compiledIn:false`. The bundle has 0 `opentelemetry` strings and no `/_spar/otlp` path. | none to `/_spar/otlp` | nothing |
| Layer compiled in, `OTEL_EXPORTER_OTLP_ENDPOINT` unset (desktop ×4, loopback ×4) | `/_spar/otlp/config` → **404** (0.1–0.2 ms on the desktop); `enabled:false`; the telemetry chunk is **not requested** (asset lists show only `main` and `notes`) | 1 POST (config) | receiver got **0** lines |
| Self-test build without the layer, OTEL variables set | `compiledIn:false` | none | 0 |

The production exe contains no `OpenTelemetry` string; the telemetry exe does.

### Delivery before close

| Host | Without the host's close step | With it (host posts `z`, page flushes, answers `z`) |
|---|---|---|
| Windows desktop, default 5 s batch delay | **0 page spans delivered** in 2 of 2 runs (only the backend's own export, flushed at dispose) | all spans delivered, 3 of 3 runs. Close → confirmed in **9.0–20.5 ms** (0.2 ms when nothing was buffered); the message loop ended 24–37 ms after the close. |
| Linux WebKitGTK | (not run without the step) | all delivered, 3 of 3. Close → confirmed in **13.9–21.6 ms**; WebKit children exited 36–42 ms after the close. |
| Loopback, headless Chromium | Playwright `page.close({ runBeforeUnload })`: **nothing delivered**, 2 of 2 | Navigating away (`about:blank`): the SDK's `pagehide` flush delivered everything, 2 of 2. No host step exists on loopback. |

### Per-call overhead

Each figure is the mean of 9 × 300 sequential `notes.count` calls: 3 runs × 3 rounds, after warm-up. "Tracing on/off" toggles the page's `CallTracer` in the same enabled app. "Layer, no endpoint" and "no layer" are separate runs.

| Host / transport | No layer | Layer, no endpoint | Enabled, tracing off | **Enabled, tracing on** | Propagation (on − off) | Whole layer (on − no endpoint) |
|---|---:|---:|---:|---:|---:|---:|
| Windows AOT, `fetch` | 0.773 ms | 0.818 | 0.794 | **0.829** | +0.035 ms | +0.011 (noise), +0.056 vs no layer |
| Windows AOT, `postMessage` | 0.088 | 0.092 | 0.092 | **0.117** | +0.025 | +0.025 |
| Loopback JIT, `fetch` | — | 0.725 | 0.741 | **0.784** | +0.043 | +0.059 |
| Linux AOT, `fetch` (custom scheme) | — | 0.259 | 0.274 | **0.305** | +0.031 | +0.046 |
| Linux AOT, message | — | 0.189 | 0.198 | **0.235** | +0.037 | +0.046 |

- **Medians do not move on Windows** (0.8 / 0.1 ms, at 0.1 ms timer resolution). Linux page timers have 1 ms resolution, so its figures are means.
- **Propagation plus a page client span costs about 25–45 µs per call.** With the backend's span and log record as well, the total is about 25–60 µs.
- **Wire cost:** `traceparent` is 55 bytes, as a header or as the message field.

### Startup

| Host | Layer enabled | Layer, no endpoint | No layer |
|---|---|---|---|
| Windows AOT, UI ready from host start (n=3) | 292–306 ms | 239–263 ms | 254–263 ms |
| Linux GTK3 unsandboxed, UI ready (n=3) | 687–875 ms | 678–747 ms | — |

Page-side cost when enabled:

| Host | Config probe | Chunk load |
|---|---:|---:|
| Desktop | 2.2–2.6 ms | 5.9–6.3 ms |
| Loopback (JIT) | 5.2–5.7 ms | 5.4–5.9 ms |
| Linux | 2–3 ms | 6–7 ms |

- **Disabled cost:** the probe alone, 1.8–3 ms on the desktop and 5.4–5.5 ms on loopback, before React mounts.
- **Unexplained:** about 30 ms of the Windows UI-ready difference beyond probe and load. It is probably SDK initialisation and instrumentation registration, but that was not isolated.

### Export frequency and batching

- **Defaults:** `BatchSpanProcessor` with a 5 s delay, `maxExportBatchSize` 512 and a 2,048-span queue; Aspire's `OTEL_BSP_SCHEDULE_DELAY` replaces the delay.
- **Steady use:** at most one traces request and one logs request per delay interval while there is data, and nothing while idle.
- **The bench** (about 1,870 page spans in a few seconds) gave **4 trace exports**: three full 512-span batches when the queue filled, plus the remainder at flush. With 1 log export, that is 5 relayed requests per run on desktop and Linux, and 3 on loopback.
- **Payload:** about **171 bytes per page span** (87,646 bytes per 512 spans, protobuf).
- **Relay time per request:**

  | Host / protocol | Relay time |
  |---|---:|
  | Windows, 59–90 KB, http/protobuf to a local receiver | 2.3–6.5 ms |
  | Windows, first gRPC request to the dashboard (HTTP/2 connection setup) | 26–31 ms |
  | Loopback | 5–11 ms |
  | Linux, through Docker Desktop's host forwarding | 1.5–14.6 ms |

- **Transport:** the browser exporter (0.301.2) uses `fetch` with `keepalive` for bodies up to 60 KB; there is no `sendBeacon` and no XHR. Keepalive fetches were intercepted normally by WebView2's `WebResourceRequested` and by WebKitGTK's custom scheme.

### Sizes

**Page bundle** (Vite 8, minified; gzip -9 and Brotli via Node's zlib):

| Part | Raw | gzip | Brotli |
|---|---:|---:|---:|
| Production main bundle, no layer | 228,259 | 70,994 | — |
| Same app with the layer: main bundle | 230,682 (**+2,423**) | 71,813 (**+819**) | 62,011 |
| Lazy telemetry chunk, loaded only when enabled | **94,499** | **27,001** | **23,698** |

**Self-contained probes** (`tools/size-probes/measure.mjs`, each built alone):

| Probe | Raw | gzip |
|---|---:|---:|
| Traces only: provider, batch processor, OTLP/protobuf exporter, propagator, context manager | 75,690 | 20,572 |
| Traces + logs | 98,323 | 24,418 (+3.8 KB) |
| Traces + document-load + user-interaction | 97,400 | 25,986 (+5.4 KB) |
| The app's telemetry module, everything, including its own copy of the Spar SDK | 123,653 | 31,542 |

**Executables** (NativeAOT, strict; **0 warnings in every publish**):

| Build | Windows (win-x64) | Linux (linux-x64, GTK3) |
|---|---:|---:|
| Production, no layer | 6,245,888 | 7,022,896 |
| With the layer (traces, metrics, logs, relay) | 10,629,632 (**+4,383,744**) | 10,602,840 (**+3,579,944**) |
| Self-test + layer (measured builds) | 11,594,752 | 10,681,944 |

- **Windows:** the layer costs +4.38 MB, against +4.34 MB measured in `../react` without the relay. The relay and the HTTP/2 path add roughly 40 KB; this was not isolated.
- **Linux:** the production build embeds the copied telemetry-enabled bundle (a spike shortcut), so only the exe delta is meaningful.
- **Production boundary:** the Windows production exe contains no `OpenTelemetry` string.

## Failures and causes

1. **`@opentelemetry/sdk-trace-web` is deprecated in OpenTelemetry JS 3.x.**
   - npm says "Use @opentelemetry/sdk-trace package".
   - Its last stable release (2.12.0) requires `@opentelemetry/api <1.10`, while the 0.301 exporters require `^1.10`.
   - **Used instead:** `@opentelemetry/sdk-trace` 3.1.1, which has the browser platform built in; `WebTracerProvider` is gone, replaced by `TracerProvider` plus `StackContextManager`.
   - The logs API now lives in `@opentelemetry/api` 1.10 (`logs`), so `@opentelemetry/api-logs` was removed again.
2. **Page spans were lost on window close** (desktop, 2 of 2 runs, 5 s delay). WebView2 tears the page down before the batch is due.
   - **Fixed:** the host asks the page to flush (`z`) and waits at most 1 s.
3. **Telemetry broke the self-test when the collector was down.** `forceFlush` rejected, and each relay attempt waited 2 s for a refused connection.
   - **Fixed:** `flush` resolves false instead of rejecting, and the relay backs off for 5 s after a connection failure.
4. **The Aspire resource did not match the telemetry.** The layer named its service `spar-notes` regardless of `OTEL_SERVICE_NAME`.
   - **Fixed:** an orchestrator's name wins, and the page becomes `<name>-page`.
5. **Driver mistakes (spike tooling only).** Playwright strict-mode locators (`role=status` matched the new `<output>`; the label "Note" matched four elements); fixed with exact locators.
   - The first two loopback runs crashed the driver after the click, so their page spans were never exported: the browser closed before the batch delay.

## Downloads and leftovers

- **npm:** `npm install` added 25 packages in 19.6 s; one of them, `@opentelemetry/api-logs`, was removed again.
  - The 15 remaining `@opentelemetry/*` packages take 13.7 MB on disk.
  - Seven of the others take 2.1 MB. These are `require-in-the-middle` and `import-in-the-middle` (Node-only dependencies of `@opentelemetry/instrumentation`), `zone.js` 0.16.3 (auto-installed peer of `instrumentation-user-interaction`, unused and not bundled), and small helpers.
  - Download size was not measured separately.
- **NuGet on Windows:** none observed for this spike. Playwright 1.64.1, OpenTelemetry 1.19.1 and Aspire 13.6.5, including the dashboard and orchestration packages, came from the cache.
  - The cache gained `linux-x64` runtime, NativeAOT and ILCompiler 11.0.0 packs at 22:42–22:45 local. Nothing here restores `linux-x64` on Windows, so they are attributed to another agent; this is not verified.
- **Docker.**
  - No image was built; the existing `spar-linux-desktop:webkit2.52` was reused.
  - Volumes:
    - `spar-btel-nuget` was seeded by copying `spar-linux-desktop-nuget`, read-only, with no network.
    - The publish then restored about **10 MB** more: `opentelemetry`, `.api`, `.api.providerbuilderextensions` and `.exporter.opentelemetryprotocol`.
    - `spar-btel-work` held the sources.
  - Container `spar-btel-linux`.
  - **All three were removed.**
- **Aspire.** The AppHost logged that it used `%LOCALAPPDATA%\ASP.NET\DataProtection-Keys` (outside the repository; the standalone dashboard probably did the same). Setting `LOCALAPPDATA` for the process did not redirect it. Both Aspire runs stopped cleanly, and no `dcp`, dashboard or AppHost process remained.
- **Processes:** every receiver, dashboard, AppHost and app this spike started was stopped.

## Observed, skipped, not tested

**Observed** (in addition to the tables):
- Windows desktop AOT: 3 bench runs each for enabled, no endpoint and no layer. Also 2 close-without-flush runs, 3 close-flush runs, gRPC to the dashboard, a wrong key and a collector that was down.
- Loopback: 3 + 3 bench runs, plus 2 tab-close and 2 navigate-away runs.
- Linux: 3 + 3 bench runs, plus 3 close-flush runs and gRPC to the dashboard.
- Standalone dashboard: all three hosts. AppHost: the desktop exe as a resource.
- Strict AOT publishes, 0 warnings: four Windows variants, three Linux variants.

**Skipped:**
- GTK4 / WebKitGTK 6.0 and the sandboxed 4.1 variant (only GTK3 unsandboxed; its known ~690 ms startup is visible in the table).
- An OTLP/JSON page exporter (`exporter-trace-otlp-http`), for a protobuf-against-JSON size comparison.
- `exporter-metrics`, and Web Vitals from the page.

**Not tested:**
- **Streams and subscriptions.** The SDK sends `-` as their trace field, and stream commands have no backend span. A stream span would need `SparDispatch.PumpAsync` instrumentation.
- **The Linux user-close path.** Only programmatic `Close()` asks the page; `delete-event` and `close-request` are not wired.
- **Loopback tab close in a real browser.** In headless Chromium, `page.close` delivered nothing. Only `pagehide` through navigation was shown to work.
- **The loopback development session through Vite's proxy.** `/_spar/otlp` would pass through its `/_spar` middleware, but this was not run.
- **The AppHost with TLS** (Aspire's default https OTLP endpoint and developer certificate), and the AppHost with API-key auth. The standalone dashboard covered the key.
- **Sampling other than always-on.** The page decides, and the backend follows the sampled flag through its default parent-based sampler. This was not varied.
- **Rate limits** on `/_spar/otlp`, beyond the 4 MB body cap.
- **A foreign frame posting to the relay** on Linux, where there is no Origin (the same open issue as the bridge).
- **Several windows or documents sharing one relay.**
- **A `service.instance.id` for the page.** All runs of one app appear under one page resource in the dashboard.
- **zone.js context propagation** across `await` in the page. Without it, an interaction span covers only the synchronous handler. Calls made after an `await` inside a handler start their own trace.

## Recommendation for Spar

**Where the pieces live:**

| Piece | Where | Cost when unused |
|---|---|---|
| `CallTracer` hook, trace context on every call (`traceparent` header / message field), `onClosing`, `hostHeaders` | `@spar/sdk` (core, generated) | a few hundred bytes; no work without a tracer |
| `SparTraceContext` on `SparCall`, `SparInvoke(…, trace, ct)`, parent in `SparDiagnostics` | core runtime + always-on BCL diagnostics | one `HasListeners()` check per call |
| `/_spar/otlp/*` routing with the host's origin/token checks; 404 without a relay; the bounded "flush before close" exchange when a closing hook is registered | every host (core) | one 404 route |
| `ISparTelemetryRelay` implementation (OTLP/HTTP passthrough, gRPC framing, `OTEL_*` variables, backoff), the OpenTelemetry .NET SDK and exporter | the optional .NET layer (a `Spar.OpenTelemetry` package) | +3.6–4.4 MB when referenced |
| Page SDK setup (provider, exporters to relative paths, instrumentations, `CallTracer`, `onClosing(flush)`) | an optional client package, for example `@spar/app/telemetry`, compiled in only when the app references the layer and loaded lazily when the host says it is configured | 0 bytes without the layer; +0.8 KB gzip with it; +27 KB gzip chunk only when enabled |

**Rules this spike supports:**
- **The page never knows the endpoint or the credentials.** The host relays bytes unchanged, adds the configured headers, and never replays a request. Retries belong to the page exporter and are bounded.
- **Desktop hosts must ask the page to flush before closing.** Otherwise a session's page telemetry is lost. A wait of at most 1 s was enough; real flushes took 9–22 ms.
- **The page follows the backend's configuration:** `OTEL_SERVICE_NAME` (the page becomes `<name>-page`) and `OTEL_BSP_SCHEDULE_DELAY`. The relay follows the exporter's protocol default (gRPC) so that one setting serves both.

**Open choices for the user:**
- **How the page learns whether telemetry is on.** The config probe costs 2–6 ms before mount; a host-injected flag in the HTML would avoid it.
- **Whether to keep user-interaction spans.** They need telemetry before mount, and React produces 2–3 nested `click` spans per click. The alternative is explicit app spans.
- **Whether `zone.js` is worth it** for interaction spans that survive `await`. `zone.min.js` 0.16.3 is 36,166 bytes, 12,942 gzip, plus `@opentelemetry/context-zone`; the context manager was not tried.
