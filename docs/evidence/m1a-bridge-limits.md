# M1a: bridge limits, admission and exception redaction

Recorded 2026-11-15. What it shows: each document's bridge refuses an oversized call by its id before reading its payload, runs a bounded number of commands with a bounded FIFO queue behind them, fails an oversized result alone, and turns an exception into a correlation id for the page and a log entry for the app. Decisions: "Bridge limits, admission and exception redaction: the behaviour" and "Bridge limits built".

## Measurements (`spikes/bridge-limits`, Windows 11, .NET 11, Node 24.21.0)

| What | Result |
|---|---|
| Memory per pending call (100,000 pending, no payload) | 622 bytes |
| Taking in one call message (UTF-8 encode, header, payload read) | 0.52 ms and 3.0 MiB at 1 MiB; 1.84 ms and 12 MiB at 4 MiB; 9.9 ms and 48 MiB at 16 MiB; 49 ms and 192 MiB at 64 MiB (3× the payload every time) |
| The page parsing one result (split at the TAB, `JSON.parse`) | 0.42 ms at 1 MiB; 1.30 ms at 4 MiB; 5.88 ms at 16 MiB; 22 ms at 64 MiB |
| Kestrel's default `MaxRequestBodySize` | 30,000,000 bytes (28.6 MiB) |

Raw output: `spikes/bridge-limits/results/`.

## Results (observed)

| Check | Result |
|---|---|
| `tests/Spar.Tests/BridgeLimitTests.cs` | 15 of 15: too-large by id and the next call runs; an oversized message with no call header dropped; slots, FIFO start and busy; a waiting call cancelled before it starts; the queue's byte bound; an oversized result failing alone; the correlation from `traceparent` with the log entry; Development's detail; a new correlation without a trace; no correlation on refusals; each option refused below 1 |
| `src/Spar.Client` tests | 130 of 130: correlation and detail on `CallError`, five malformed payloads adding nothing, and the 1 MiB batch split |
| `dotnet test --solution Spar.slnx` | 309 of 309, including the 200-calls-at-once browser test (it failed with `busy` while the queue's count was 64) |
| NativeAOT lane | Spar.Tests 142 of 142, Notebook.Tests 12 of 12, no warnings |
| Production Notebook | 2,909,184 B (the bridge session is not in it yet: there is no desktop host) |
| `-mt` build, size check | clean; 115 source files |

## Found and fixed

- The client step's writes raced between an app's variants sharing `obj/spar/client` (seen once); they are now under a lease. Four later builds from empty client folders passed, but none showed the lease waiting.

## Not done or not verified

- WebView2's and WebKitGTK's own limits on message size, and the limits under a real desktop host (M1b, M1c).
- Logging beyond the bridge's own entries (the per-app log of the test host, M1a; the instrumentation of section 6b, M1a).
