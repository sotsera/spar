# M1a: bridge instrumentation

Recorded 2026-11-15. What it shows: each bridge command runs in a server activity that continues the page's trace and records one `spar.call.duration` sample, and a failure's correlation id is the activity's trace id when there is one. Decision: "Bridge instrumentation built".

## Results (observed)

| Check | Result |
|---|---|
| `tests/Spar.Tests/BridgeTelemetryTests.cs` | 5 of 5: the span's name, kind, parent from `traceparent` and tags; a throwing command's Error status, `error.type` and `exception` event, with the page's correlation equal to the span's trace id; a call with no trace starts a new one even inside an ambient activity; one duration sample per command, tagged `success`, `error` and `failed`; an oversized result recorded as `response` |
| Cost, 20,000 echo calls through one session (Debug, three rounds) | no listener: 2.7, 3.2, 3.7 µs and 3,949, 3,947, 3,935 B per call; both listeners: 2.3, 2.9, 2.9 µs and 4,915, 4,917, 4,919 B per call |
| `dotnet test --solution Spar.slnx` | 314 of 314 |
| NativeAOT lane | Spar.Tests 147 of 147, no warnings |
| `-mt` build, size check | clean; 118 source files |

The cost was measured with a test written for the run and deleted afterwards. A Release build of the test project failed to launch ("app-launch-failed") and was not investigated; the Debug numbers above are what the suite runs.

## Follow-up: every call, from receipt, with a flood budget (2026-11-15)

Decision "Bridge instrumentation: every call, from receipt, with a flood budget". Observed: a call that waited 150 ms behind a running one has `spar.queue.seconds` and a duration of at least 0.1 s, and its `started` event 0.1 s or more after its span's start; a `busy` refusal records one sample tagged `rejected` / `busy` and one span with status Error; a call to a command the app does not have is the span `unknown/unknown` with `unknown` tags; 30 refusals (a page built for other contracts) record 30 samples, 20 spans and 20 log entries, and after a second on a manual clock the 31st is traced and the log says "10 refused calls of document doc-1 were not traced or logged". `tests/Spar.Tests` 151 of 151 (9 telemetry tests); `dotnet test --solution Spar.slnx` 318 of 318, twice; NativeAOT Spar.Tests 151 of 151; `-mt` and the size check clean (118 files).

## Not done or not verified

- The size in a Production executable (no bridge there yet); the spans exported through the optional OpenTelemetry layer (M2); trace context on streams and subscriptions (M3).
