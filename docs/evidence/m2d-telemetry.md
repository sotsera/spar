# M2d: the optional OpenTelemetry layer, client telemetry and Aspire

Recorded 2026-11-26. What it shows: plan M2d, build steps 1 to 4 (section 6b, `docs/design/agents.md`): `Spar.OpenTelemetry` with `spar.Telemetry(…)`, the backend's traces, metrics and logs over OTLP, the page's spans relayed through `/_spar/otlp/v1/traces` on every host, one trace from a page call to its backend span, and the telemetry guide's Aspire recipe checked from Spar's packages. Decisions "Optional OpenTelemetry layer", "Browser telemetry through the host: confirmed", "Browser telemetry: how the page starts it", "M2d's decide-first questions" and "M2d: the optional OpenTelemetry layer, client telemetry and Aspire (implementation record; choices for review; 2026-11-26)". The user-facing description is `docs/guide/telemetry.md`; the app model's is `docs/app-model.md` 3.18 and 6.16.

## Environment

- **Windows:** Windows 11 Pro 10.0.26320, SDK 11.0.100, WebView2 Runtime 158.0.4370.33. The black box ran the Notebook's Production NativeAOT executable on a desktop of its own.
- **Linux:** WSL Ubuntu 24.04 (kernel 6.18.58.1), GTK 4.14.5, WebKitGTK 2.52.6, the repository's copy in `/var/tmp/spar-m1c-blackbox/repo` (`docs/evidence/m1c-blackbox/wsl.sh`): the black box's own Xvfb and D-Bus per app; the Aspire check's app host under `dbus-run-session`, an Xvfb of its own (abstract socket only) and a private home. Nothing opened on WSLg's display.
- **Packages:** OpenTelemetry .NET 1.21.0 (`OpenTelemetry`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`, `OpenTelemetry.Extensions.Hosting`); OpenTelemetry JS `@opentelemetry/api` 1.10.0, `sdk-trace` 3.1.1, `exporter-trace-otlp-proto` 0.301.2, `instrumentation-document-load` 0.70.1; Aspire 13.6.5.

## What was added

| Path | Contents |
|---|---|
| `src/Spar/Telemetry/` | `TelemetryRoute` (the routes, their limits, the page's settings), `ITelemetryRelay` |
| `src/Spar.Windows/PageServer.cs`, `AppWindow.cs` | The route on `WebResourceRequested` (body read on the STA, deferral, forward on the pool); the settings appended to the host-injected script |
| `src/Spar.Linux/SchemeServer.cs`, `BrowserView.cs`, `AppWindow.cs`, `Interop/WebKit.cs` | The route on the scheme handler (asynchronous body read); the settings as a user script of the page's world |
| `src/Spar.Loopback/LoopbackHost.cs`, `LoopbackApp.cs` | The route behind the token, with `/_spar/otlp/config` |
| `src/Spar.OpenTelemetry/` | The package: `spar.Telemetry(…)`, `OtlpSettings`, `OtlpRelay` (OTLP/HTTP and gRPC) |
| `src/Spar/Hosting/SparRunningApp.cs`, `SparApplication.cs`, `ReportLimiter.cs` | `SparStartup` (the providers started when the services are built), the builder's relay, the limiter's sizes |
| `src/Spar.Client/src/tracing.ts`, `telemetryBoot.ts`, `connection.ts`, `loopback.ts`, `sdk.ts`, `vite/index.ts`; `src/Spar.Client/src/telemetry/`, `vite.telemetry.config.ts` | The hook and `traced`, the start, `traceparent` on calls, the loopback relay's address, the Vite define and the licences; the chunk and its bundling |
| `src/Spar.Sdk/Sdk/Sdk.targets`, `src/Spar.Sdk.Tasks/SparClient.cs`, `Client/ClientPackage.cs` | The reference's version and its source-mode swap, `SparPageTelemetry`, `telemetry.json`, the `.md` files of the compiled SDK |
| `src/Spar.Testing/BlackBox/SparBlackBoxOptions.cs` | `OTEL_*` removed from launched apps unless a test sets them |
| `samples/Notebook/` | `spar.Telemetry()` and the package |
| `samples/Notebook.EndToEndTests/BlackBox/TelemetryTests.cs`, `OtlpReceiver.cs` | The black-box test with an OTLP/HTTP endpoint of its own and a protobuf span reader |
| `tests/Spar.Tests/Telemetry/` | The route's, the relay's and loopback's tests (38) |
| `src/Spar.Client/test/tracing.test.ts`, `telemetry.test.ts` | The hook's tests and the shipped chunk's (18) |
| `tests/Spar.PackageTests/AspireTests.cs`, `GuideSnippets.cs` | The Aspire check; the telemetry guide's blocks |
| `tests/AppModel/` | `spar.Telemetry()` and `traced` in the document's app, their stand-ins, the real build with the layer |

## Results (observed)

### One trace from the page's call to the backend's span

The Notebook's black-box test (`TelemetryTests`) launches the Production executable with `OTEL_EXPORTER_OTLP_ENDPOINT` at the test's own receiver (`http/protobuf`, `OTEL_BSP_SCHEDULE_DELAY=200`), waits for the page's first span, opens a page by its sidebar button (again when the first Invoke made no call, record choice 14: in 4 of 6 runs on Windows) and closes the app once both spans of its `pages/get` arrived. Passed on both systems; the spans it received, `resourceFetch` left out (`m2d-telemetry/windows-blackbox-spans.txt`, `linux-blackbox-spans.txt`):

```text
Windows
com.example.Notebook pages/list kind=2 trace=9e763bdd… span=7d39f021… parent=
com.example.Notebook-page documentFetch kind=1 trace=2c9df1b0… span=340e9ac3… parent=bf9b0f29…
com.example.Notebook-page documentLoad kind=1 trace=2c9df1b0… span=bf9b0f29… parent=
com.example.Notebook pages/get kind=2 trace=0fa06938… span=ca5ff44b… parent=414faf3d…
com.example.Notebook-page pages/get kind=3 trace=0fa06938… span=414faf3d… parent=

Linux
com.example.Notebook pages/list kind=2 trace=f189aff7… span=76bd4de4… parent=cd76ce3d…
com.example.Notebook-page documentFetch kind=1 trace=8474cbf8… span=265c9ca2… parent=e6058c6a…
com.example.Notebook-page documentLoad kind=1 trace=8474cbf8… span=e6058c6a… parent=
com.example.Notebook-page pages/list kind=3 trace=f189aff7… span=cd76ce3d… parent=
com.example.Notebook pages/get kind=2 trace=d051cfe3… span=6e11e311… parent=ebd45802…
com.example.Notebook-page pages/get kind=3 trace=d051cfe3… span=ebd45802… parent=
```

Kind 3 is CLIENT, 2 SERVER, 1 INTERNAL. Each backend `pages/get` is the child of the page's span of the same call; on Windows the first `pages/list` ran before the page's chunk loaded, so its backend span is a root (choice 8 of the record). The same run received the backend's logs and metrics (`/v1/logs`, `/v1/metrics`). Before the page had a context manager, `documentFetch` was a trace of its own (choice 7).

### The Aspire dashboard

- **A prototype on Windows:** an app host (Aspire 13.6.5) started the in-repository Notebook (Development build, `Desktop` profile) on a desktop of its own, the dashboard unsecured over HTTP. The dashboard's `/api/telemetry/resources` listed `notebook` (logs, traces, metrics) and `notebook-page` (traces): the page's spans had gone through the relay over gRPC to the dashboard's real gRPC endpoint. `/api/telemetry/spans` (`m2d-telemetry/aspire-prototype-spans.json`) held `documentLoad` with its `documentFetch` and 42 `resourceFetch` spans, and the backend's two `pages/list` spans (React's development double render), each a root since both calls ran before the chunk loaded. This run wrote DCP's `mruPorts.list` and `state` under the real `%USERPROFILE%\.dcp`, which already existed; no process outlived it. So the permanent check does not run there (choice 12).
- **`AspireTests` on Linux:** a new app from the template with the guide's package block and `Program.cs` line, an app host with the guide's `AppHost.cs`, built from this run's packages (`Spar.PackageTests`), started under `dbus-run-session` on an Xvfb of its own with a private home: `ledger` with logs and `ledger-page` with traces, `documentLoad` among them. Passed, with the two guide-block tests, in 45 s after the packing.

### Without the layer, without an endpoint

- The Notebook built without the package: `telemetry.json` `{"page":false}`, and its Release bundle had no telemetry chunk and no telemetry code, only the loopback channel's `telemetryRelay` method (one line).
- With the package and no endpoint: the host-injected script has no settings, the traces route is a 404 and the settings route a 204 (`TelemetryRouteTests`), and the page never imports the chunk (`telemetry.test.ts`, "without injected settings", "a 204"). Before the 204, the Notebook's seven browser journeys failed on the console's "Failed to load resource: 404" (record choice 13).

### The relay and the routes

`tests/Spar.Tests/Telemetry/` (38): the 404s, 405 with `Allow: POST`, 415, 429 past 20 at once, 413 over 1 MiB without a forward, the settings' JSON and the script's escaping (`</script>`); the relay's OTLP/HTTP path and headers, JSON over HTTP, gRPC framing (a zero byte, a big-endian length, the bytes) to the trace service over HTTP/2 with `te: trailers`, six `grpc-status` mappings, JSON refused over gRPC without a request, the 503 pause with no retry, the endpoint's status passed through; the standard variables; loopback's routes behind the token (401 without it).

### The page

`tracing.test.ts` (7): no `traceparent` without telemetry; with a tracer, the call carries its span's `traceparent` and ends it as `success`, `error` (declared) or the failure's kind and code; no span for a call refused before it starts. `telemetry.test.ts` (11), against the shipped chunk: calls inside `traced` share its trace, a call after its first `await` does not; a flush posts protobuf to the relay with the relay's headers; the injected settings start it; loopback asks `/_spar/otlp/config` with its token; a 204, a 404, invalid settings or a network error leave it off.

### Size

| | Windows | Linux |
|---|---|---|
| Notebook NativeAOT executable without the layer | 5,308,416 B | 5,220,048 B |
| With it | 11,263,488 B | 11,494,184 B |
| Difference | +5,955,072 B | +6,274,136 B |

The page chunk after the Notebook's own `vite build`: 66,808 B minified, 20,471 B gzip, 18,041 B Brotli, a lazy chunk; the SDK's unminified bundle is 189,207 B. The Windows map file (`IlcGenerateMapFile`) puts the code of .NET's HTTP and TLS stack (`System.Net.Http`, `System.Security.Cryptography`, `System.Net.Security`, `System.Net.Sockets`, `System.Formats.Asn1`) at about 1.1 MB, OpenTelemetry's SDK and exporter at about 0.45 MB and `System.Text.RegularExpressions` (OpenTelemetry's metrics) at 0.3 MB, with their type data and metadata on top. The Notebook makes no HTTPS request of its own, so it pays for the whole stack; the spike's +4.3 MB was for an app that already did.

## Not verified

- **Windows outside CI for the Aspire check** (it writes to the user's profile), and the `Loopback` profile under Aspire: the guide describes it, no run checked it.
- **The dashboard's OTLP API key:** the check runs the dashboard unsecured, so `OTEL_EXPORTER_OTLP_HEADERS` reaching the endpoint is checked only against the fake handler.
- **Aspire 17.0.0**, released 2026-11-18.
- **The 1 s flush on a window's close** with a real endpoint: the black-box test's page spans were exported before the close (200 ms batch delay).
- **CI:** not run yet.

## Test runs

- **Windows:** `dotnet test --solution Spar.slnx -c Debug` 1,552 (1,439 passed, 113 skipped, 0 failed). A first run failed nine: the package's file list (`telemetry.json`), seven Notebook browser journeys on the console's 404 for `/_spar/otlp/config` (now a 204), and the scene-bytes test, which also failed 2 of 8 runs on M2c's commit (flaky before M2d). The NativeAOT lane: `Spar.Tests` 842 (7 skipped), `Notebook.Tests` 12, `Notebook.EndToEndTests` 21 (4 not run), the publishes without warnings.
- **Linux (WSL):** the solution without window and black-box tests 1,347 (41 skipped), `Spar.WindowTests` 223 (50 skipped), the Notebook's black box 7, 0 failed.
- **SDK:** `npm run check` (TypeScript 6 and 7) clean, `npm test` 348.
- **Documentation:** `AppModel.Tests` 11 (39 blocks, the document's app built with real Spar and the layer).
- **Build tests:** `Spar.BuildTests` 7 of 7, after the production boundary dropped four markers an app with the layer carries legitimately (record choice 15); before, it found `LoopbackHost` (System.Uri's flag), `Spar.Loopback` and `Spar.Testing` (Spar.dll's `InternalsVisibleTo` arguments, kept as metadata) and `ICoreWebView2HttpRequestHeaders` (the telemetry route's header read) in the Notebook's Production executable, and none of the other markers.
- **Package tests on Windows:** 41 of 44 passed, the Aspire check skipped (it runs on Windows in CI only); the two failures are the template app's black-box journey, intermittent since M2c in apps without the layer.
