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

## Not done or not verified

- The size in a Production executable (no bridge there yet); the spans exported through the optional OpenTelemetry layer (M2); trace context on streams and subscriptions (M3).
