---
title: Traces, metrics and logs with OpenTelemetry
description: Export a Spar app's traces, metrics and logs over OTLP, with the page's spans in the same traces, and view them in the Aspire dashboard.
---

# Traces, metrics and logs with OpenTelemetry

A Spar app can export what it does over OTLP, OpenTelemetry's protocol, to any viewer or collector that reads it: the Aspire dashboard while you develop, a collector or a monitoring service later. The backend exports its traces, metrics and logs; the page exports its spans through the backend, so one click that calls two commands is one trace, from the page's call to the backend's work.

Nothing is exported until you give the app an endpoint: an app with telemetry turned on behaves exactly like one without it while `OTEL_EXPORTER_OTLP_ENDPOINT` is unset, and its page loads none of the telemetry code.

## Turning it on

Add the package to the app's project. It takes the version of the Spar SDK the project uses, so leave the version out:

<!-- project-snippet: telemetry-package -->
```xml
<ItemGroup>
  <PackageReference Include="Spar.OpenTelemetry" />
</ItemGroup>
```

With central package management (`Directory.Packages.props`), the SDK adds the `PackageVersion` too, unless the file has one.

Then call `Telemetry` in `Program.cs`:

<!-- code-snippet: telemetry-program -->
```csharp
spar.Telemetry();
```

That is all the page needs as well: the build sees the package and gives the page its telemetry module, which the page loads only when the app has an endpoint.

`Telemetry` takes the OpenTelemetry builder, for the app's own instrumentation and resource attributes. For example, with the `OpenTelemetry.Instrumentation.Http` package, the spans of the app's outgoing HTTP requests:

```csharp
spar.Telemetry(otel => otel
    .WithTracing(tracing => tracing.AddHttpClientInstrumentation())
    .WithMetrics(metrics => metrics.AddHttpClientInstrumentation()));
```

## What is exported

From the backend:

| Signal | What |
|---|---|
| Traces | A span for each command the page calls, named `contract/command` (for example `pages/save`), with its outcome; and the spans of the app's own instrumentation. |
| Metrics | `spar.call.duration`, a histogram of the commands' durations by contract, command and outcome; and the app's own meters. |
| Logs | Every `ILogger` line of the app and of Spar, at the levels the app's log file has. The page's errors and console messages are in them, since they reach the backend's log. |

From the page, through the backend:

- A span for each command the page calls, the parent of the backend's span of the same call.
- The document's load: how long the page and each of its files took.
- The spans of `traced`, which groups an interaction's calls under one span.

The page exports traces only: its errors and console messages already reach the backend's logs.

## Grouping calls with `traced`

`traced(name, run)` from `@spar/app/sdk` makes one span around `run`, and the calls `run` starts become its children, so an interaction reads as one trace:

```ts
import { traced } from "@spar/app/sdk";
import { pages } from "@spar/app/pages";

await traced("open page", () => Promise.all([pages.get({ id }), pages.list()]));
```

The span ends when `run` returns, or when the promise it returns settles. The page keeps no context across an `await`: a call started after `run`'s first `await` starts a trace of its own. Without telemetry, `traced` only runs `run`.

## The settings

The standard OpenTelemetry variables apply, read when the app starts:

| Variable | What it does |
|---|---|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Where to export. Without it, nothing is exported. |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | `grpc` (the default), `http/protobuf` or `http/json`. |
| `OTEL_EXPORTER_OTLP_HEADERS` | Headers for the endpoint, such as an API key: `key=value` pairs separated by commas. |
| `OTEL_SERVICE_NAME` | The service name of the backend's telemetry; the page's is the same with `-page` added. Without it, the app id (`SparAppId`). |
| `OTEL_RESOURCE_ATTRIBUTES` | More resource attributes, as `key=value` pairs. |
| `OTEL_TRACES_SAMPLER`, `OTEL_BSP_SCHEDULE_DELAY` and the others | As the OpenTelemetry SDK documents them. The page's exporter uses the same batch delay as the backend's. |

[Observability with OpenTelemetry](https://learn.microsoft.com/dotnet/core/diagnostics/observability-with-otel) describes the .NET side of OpenTelemetry, and its exporter's settings.

## How the page's spans reach the endpoint

The page never talks to the endpoint, and never sees its address or headers. It posts its spans to its own origin, `/_spar/otlp/v1/traces`, and the backend forwards each request's bytes unchanged to the endpoint, with the configured headers. The page's other requests are unaffected: a strict content security policy keeps working, with nothing added to it.

The forwarding has limits, so a page cannot flood the endpoint or the backend:

- Only OTLP traces, as protobuf or JSON: anything else is refused (415).
- 1 MiB at most per request (413), and 20 requests a second (429).
- After the endpoint could not be reached, requests are refused at once (503) for 5 seconds. Nothing is retried by the backend; the page's exporter retries a few times and then drops the spans.
- Without the package, or without an endpoint, every `/_spar/otlp/` path is a 404, except the page's request for its settings, which gets an empty 204 so the browser's console shows no failed request.

The page's spans hold the addresses of the page's files and the names of the commands it calls; command requests and results are never in them.

## What it costs

The OpenTelemetry SDK and its OTLP exporter bring .NET's HTTP client and TLS into the app if it does not have them already. The Notebook sample's NativeAOT executable on Windows grows from 5.3 MB to 11.3 MB; an app that already makes HTTPS requests pays less. The page's module is about 18 KB compressed, loaded after the page's start and only with an endpoint.

## Viewing it in the Aspire dashboard

The Aspire dashboard shows traces, metrics and logs as they arrive, and an Aspire app host starts the app with the dashboard's endpoint already set. Create an app host with Aspire's own template (Aspire 13; see [the Aspire documentation](https://aspire.dev/get-started/app-host/)):

```sh
dotnet new aspire-apphost -n MyApp.AppHost
dotnet add MyApp.AppHost reference MyApp
```

Then start the app from it, in `AppHost.cs`:

<!-- code-snippet: aspire-apphost -->
```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.MyApp>("myapp", launchProfileName: "Desktop");

builder.Build().Run();
```

`dotnet run --project MyApp.AppHost` starts the dashboard and the app with its window. The app's service is `myapp` and its page's `myapp-page`: open a trace of `myapp-page` to see a call with the backend's span under it.

To work on the page in a browser instead, start the `Loopback` profile: `launchProfileName: "Loopback"`. The app serves its page on `http://127.0.0.1` with a port of its own, and prints the address, with the session's token, in its output; open it from the app's console log in the dashboard. Use that address as printed, with `127.0.0.1` rather than `localhost`.

The dashboard's own settings, such as its addresses and keys, are in [the dashboard's configuration](https://aspire.dev/dashboard/configuration/).

## Without Aspire

Any OTLP endpoint works the same way. Set the variables before starting the app, for example for a collector listening for OTLP over HTTP on this machine:

```powershell
$env:OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:4318"
$env:OTEL_EXPORTER_OTLP_PROTOCOL = "http/protobuf"
.\MyApp.exe
```

A published app reads the variables too. Whether its users' machines should export, and where, is your app's choice: Spar sets none of them.
