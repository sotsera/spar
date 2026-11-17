# M1a: the bridge in C#, and the framework tests

Recorded 2026-11-15, on the first slice (`docs/evidence/m1a-first-slice.md`). What it shows: the decided bridge messages (plan section 2c) encode and decode as the shared vectors require, the per-document session follows the protocol rules, and the generator and serializers hold their rules, under CoreCLR and NativeAOT. Decision: "Bridge in C#: codec, session and tests".

## Environment

As in the first slice: Windows 11 Pro 10.0.26320, SDK 11.0.100, `Microsoft.CodeAnalysis.CSharp` 5.9.0, `xunit.v3` and `xunit.v3.aot` 4.0.3, warnings as errors with the trim and AOT analyzers on. Not run on Linux.

## What was added

| Path | Contents |
|---|---|
| `src/Spar/Bridge/` | `BridgeMessage` (the 14 types of protocol 1), `BridgeCodec` (decode: a TAB always, one header object, duplicate keys malformed, unknown fields skipped, ids safe integers from 1, trace values validated and dropped rather than refused; encode: one canonical field order per type), `BridgeSession` (hello and open, the fingerprint once, increasing ids, dispatch off the receiving thread, cancels including one that overtakes its call (remembered 30 s), one terminal per call, events only to pages that listed them, detail payloads only when the host allows, nothing after close). |
| `tests/conformance/bridge-messages.json` | The shared vectors: 24 canonical messages, 10 other valid forms, 16 malformed ones with the id a refusal may use. |
| `tests/Spar.Tests/` | 74 vector checks (canonical encode and decode, other forms, malformed), 13 session tests, 6 dispatcher tests. |
| `tests/Spar.Generator.Tests/` | In-memory compilation: 17 diagnostic tests (each ID once, with its located text), 8 generator tests (every supported shape compiles, catalog entries with wire names and time limits, class contracts registered and interface ones not, error codes and `[SparName]`, Test-variant files only in `Test`, an invalid command left out, LF only, the fingerprint stable under reformatting and changed by a type change). |
| `tests/apps/WireShapes/`, `tests/WireShapes.Tests/` | A test app echoing every supported wire type, and 20 run-time serializer tests through real dispatch. |

## Results (observed)

| Check | Result |
|---|---|
| `dotnet test --solution Spar.slnx` | 148 of 148 passed: Spar.Tests 93, Spar.Generator.Tests 25, WireShapes.Tests 20, Notebook.Tests 10; results in `artifacts/test-results/<project>/`, no `TestResults/` |
| Spar.Tests, three more CoreCLR runs | 93 of 93 each, 1.21–1.23 s |
| NativeAOT lane (`-p:SparTestLane=Aot`) | Spar.Tests 93 of 93 (publish 14 s, 16,454,656 B); WireShapes.Tests 20 of 20 (13 s, 16,408,064 B); Notebook.Tests 10 of 10 (10 s, 16,475,136 B); 0 warnings |
| No-op build of the solution (10 projects) | 692–841 ms (3 runs), within the 1.5 s budget |

**Session behaviour covered:** open with the negotiated protocol, the document id and the host fingerprint; values and declared errors as `result` with `ok`; a call before hello and a call after a fingerprint mismatch rejected; a running call cancelled and confirmed `stopped`; a cancel that overtakes its call answered `cancelled-before-start` without running it; repeated and lower ids dropped with no reply; a malformed call with a new id rejected; an unknown message for a pending call failing that call, and its late result dropped; nothing sent after close; details only from a Development session; events only to a page that listed `event`; `ready` and `closing-done` reaching the host.

**Serializer rules covered at run time:** a round trip of every supported type (`string` with non-ASCII, `bool`, `int` extremes, `long` extremes as strings, `double`, `Guid` in lower-case `D`, `DateTimeOffset` with its offset, nullable values, lists, arrays, nested records); nullable values written as `null`; missing optional values read as `null`; unknown properties skipped without being decoded (a lone-surrogate escape inside one is fine); twelve malformed requests refused before the command runs (a missing required value, a duplicate property, trailing content, a string for an `int`, a number for a `long`, `+1`, `007` and `-0` for a `long`, a double overflowing to infinity, a date without an offset, an invalid GUID, nesting deeper than 64); nesting of 30 accepted; declared errors with and without payloads; a NaN result as `response` / `serialization` / `completed`.

**Bugs the new tests found, now fixed:** arrays and `IReadOnlyList<T>` shared one generated method name; an array property could not take the reader's list; a date without an offset was accepted and read in local time (new pitfall).

## Not done or not verified

- **The TypeScript side of the vectors:** the SDK and its decoder do not exist yet, so the vectors were checked only against C#.
- **Not run:** Linux, and CI.
- **Bridge limits:** no maximum message size, no in-flight limit and no send queue bound yet (M1a, "bridge limits and admission").
- **Session features not built:** streams, credits, items and end are answered as not supported until M3; there is no heartbeat.
- **Not tested:** `traceparent` reaching the call's activity, since the BCL instrumentation is not built.
- **Generator output snapshots** (whole-file comparisons) are not written; the generator tests check entries and structure instead.
- **Still missing from the serializers:** the lone-surrogate rule for declared string values, and the wire types listed in the first slice's evidence.
