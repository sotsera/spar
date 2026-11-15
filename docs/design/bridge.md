# Bridge protocol

Part of the Spar design. [`docs/plan.md`](../plan.md) has the goal, the settled decisions, the open questions and the milestones, and lists every design file. Section numbers are global: "section 2c" anywhere in the plan, the design files or `docs/decisions.md` means the heading of that number, wherever it lives.

## 2c. Bridge protocol

- **One text message protocol on every host:**
  - call ids;
  - credit-based flow control for streams;
  - an explicit cancel message;
  - exactly one terminal message per call;
  - `traceparent` reserved in every call.
- **Transports:**
  - Windows: `postMessage` (0.2 ms unary).
  - Linux: the script message handler, plus `evaluate_javascript` (0.2–0.3 ms).
  - Loopback: `fetch` for unary calls, and one streamed channel per document (the 6-connection browser limit blocked per-call streams).
  - Neither WebView streams intercepted responses usefully, so streams always use credits.
- **Which window may call what (decided, user, 2026-11-14):**
  - **Default:** every window of the app may call every command, because every window runs the app's own code from its one origin.
  - **Allow-list:** a window can declare one in C#, for example `w.Commands(c => c.Allow<IConnections>())`, with the name settled with the native-API design. A call outside it is refused before it runs (`rejected`, `forbidden-command`, `notStarted`).
  - **Docs:** they recommend a list for any window that shows content the app does not fully control.
- **Deadlines (decided, user, 2026-11-14):** calls and streams have no default deadline or idle timeout, because some calls wait on a person (pickers) or run long (copies).
  - On the page, a deadline is the web's `AbortSignal.timeout(ms)` passed as the call's `signal`.
  - In C#, an optional backend limit (for example `[SparCommand(Timeout = "00:00:30")]`, name to settle in M1) is folded into the command's cancellation token.
  - Both end like any cancellation: `cancelled` only when the backend confirms, otherwise outcome unknown. Never retried.
- **Cancellation:** the page is told "cancelled" only when the backend confirms the command ended on its token (for a stream, after its iterator was disposed). Otherwise it gets the real result, or outcome unknown (`unconfirmed` after the confirmation timeout, `lost` when the connection drops). Never retried. The page sees these as `CallError` with `execution` (decision "Contract syntax"), which replaced the spike's `CancelledError`, `NotExecutedError` and `OutcomeUnknownError`; a confirmed cancel is `kind: "cancelled"`, with `execution` `notStarted` or `stopped` (decision "`CallError` kinds").
  Details from `spikes/react` ("Cancellation"):
  - **A cancel that overtakes its call** (it arrived first, on another connection) is remembered by the host for 30 s. The call never runs, and the page gets not-executed `cancelled-before-start`. Implemented, but no test provoked it.
  - **An `OperationCanceledException` from another token** (an internal timeout, say) is not a confirmation. It is reported as `failed`, which is outcome unknown.
  - **Leaving a `for await` loop with `break`** cancels and waits for the confirmation. If none comes, the `break` itself throws outcome unknown.
  - **A pull stream stops at its next `yield`,** even when its code ignores the token (16–17 ms, one timer tick). Only a long stretch without a yield or an await on the token stays unconfirmed.
  - **Work that a command starts and does not await** is invisible to the host; "cancelled" covers only the command's own code. This is a contract limit to document.
  - **A navigation starts a new document epoch** (`NavigationStarting` on WebView2): the old document's calls are cancelled and their late replies dropped. On loopback a closed channel or request does the same.
- **Subscriptions:** a bounded drop-oldest buffer with visible `gap` events. Resumption is chosen at three levels (see M3).
  - **The `events` slot (decided, user, 2026-11-14).** Subscription items are `Result`s whose `match` takes `success` (the app's values only), `events` and the usual `errors` and `failure`.
    - `events.reset` is required, because ignoring it leaves stale state; `gap`, `reconnecting` and `resumed` are optional.
    - Each subscription offers only the events it can emit: a coalesced one has no `gap`.
    - The generated type is `Subscription<T>`, an `AsyncIterable` of `Result<T, E, SubscriptionEvents>`.
    - Checked in `spikes/app-model-check`: a missing `reset` fails `tsc` (TS2322, naming `reset`), and so do a misspelled event and a missing `events` object.
  - **Coalesced subscriptions** (decided, user, 2026-11-14): `[SparSubscription(Coalesce = true)]`, name to settle in M3. The subscription keeps only the newest value and replaces older ones silently, with no `gap` events. It suits state that only matters as its latest value: resource usage, progress, connection status.
    - The page sees each value at most once and always ends on the newest. A resumed subscription starts with the current value.
    - The capacity is 1 by definition, so `Capacity` is not allowed with it (a contract diagnostic).
- **Measured:** 100k rows in about 160 ms on Windows; 20,000 push events per second delivered completely.
- **The spikes' encoding.** This is evidence for M1, **not a decided wire format.** The code is `spikes/d1-pipeline/Spike.Runtime/SparCalls.cs`, `SparStream.cs` and `Spike.Tool/SparSdk.ts`; the trace field is in the newer copy under `spikes/browser-telemetry/d1-pipeline/`.
  - **Framing:** one message per `postMessage`, or one per line of a streamed response. Each is a kind letter, the call id, the kind's fields separated by spaces, then compact JSON (which never contains a raw newline).
  - **Host to page:**

    | Message | Meaning |
    |---|---|
    | `o doc` | channel open, for document `doc` |
    | `r id json` | unary result (the `ok` envelope) |
    | `i id dropped json-array` | a batch of stream items; `dropped` counts items a subscription dropped before it |
    | `e id` | completed |
    | `c id` | cancelled and stopped (the confirmation) |
    | `n id code json` | not executed |
    | `u id code json` | outcome unknown |
    | `z` | flush before close (telemetry copy) |

  - **Page to host:**

    | Message | Meaning |
    |---|---|
    | `h doc` | hello, opens the document's channel |
    | `q id contract command fingerprint trace json` | unary call |
    | `s id contract command fingerprint trace batch credits json` | stream or subscription call |
    | `k id credits` | more credits |
    | `x id` | cancel |
    | `z` | close work done (telemetry copy) |

  - **Codes seen:** not executed `invalid-request`, `unknown-command`, `contract-mismatch` (with the host's fingerprint) and `cancelled-before-start`; outcome unknown `failed` (exception type and message as detail) and `failed-while-stopping`. The SDK also raised not-executed `unavailable` when the backend could not be reached.
  - **Credits:** the page grants `credits` batches in flight (default 8) and refills them in halves as it consumes. What is in flight is bounded by window × batch size. Proposals from the spike: a window configurable per stream (8 × 1,000 rows kept 7,000 rows in flight), and batches sized by bytes or time (for example 64 KB or 5 ms) rather than rows; batches of 1 were 20–130× slower.
  - **Trace field:** `-` when the call has no context; otherwise the `traceparent`, optionally followed by `;` and the URI-escaped `tracestate`. Streams always sent `-` (not instrumented). The host drops a `traceparent` over 128 characters and a `tracestate` over 8 KB; a malformed context starts a new trace and never fails the call. `traceparent` costs 55 bytes. Before the field existed, `q` and `s` had no `trace` position.
  - **Flush before close:** the desktop host sends `z`, the page runs its registered close work (the telemetry flush, `Promise.allSettled`) and answers `z`, and the host waits at most 1 s.
  - **Loopback endpoints** (`spikes/loopback/Loop.Loopback/LoopbackHost.cs`; every bridge call needs the bearer token, and the host's `Origin` when one is sent):

    | Endpoint | Use |
    |---|---|
    | `POST /_spar/call/{contract}/{command}` | unary call, with the fingerprint in `X-Spar-Contracts` (16 hex characters of the manifest's SHA-256) |
    | `POST /_spar/stream/{contract}/{command}` | one NDJSON response per stream call (superseded by the channel: 6-connection limit) |
    | `POST /_spar/channel` | one long-lived NDJSON response per document, host to page; Kestrel's `MinResponseDataRate` turned off |
    | `POST /_spar/send` | page-to-host lines (credits, cancel) |
    | `POST /_spar/ready` | `ui-ready` |

    Status codes: 200 result; 401 `unauthorized`; 403 `forbidden-origin` or `forbidden-host`; 409 `contract-mismatch` with the host's fingerprint; 400 `invalid-request` with the serializer's message, and 400 `cancelled-before-start`; 404 `unknown-command`; 499 cancelled; 500 outcome unknown. A closed connection cancels that document's calls. Assets are served without the token (they ship in the executable anyway).
- **Decided (user, 2026-11-14): encoding C.** Every bridge message is a JSON header, a TAB, then the payload's own JSON, following the measurements below. The letter-line tables above describe the spikes' messages, not the target encoding; M1 writes the header fields and the shared conformance vectors from them, together with the rules listed under "Whatever the choice".
- **Decided before the shared conformance vectors (section 2b); the comparisons are kept as evidence:**
  - **The encoding (decided, C; the comparison is kept as evidence):** the letter-prefixed lines above, or the JSON envelopes `spikes/wire-details` used (`{"id":7,"ok":…}`, stream items `{"type":"item","value":…}`, the end message `{"type":"end","ok":false,…}`, and an `"arg"` key only when the command takes one). Section 2's envelope examples carry no `id`.
    - **Measured by `spikes/bridge-encoding`** (WebView2, loopback Chromium, WebKitGTK codec only), comparing (A) letter lines, (B) JSON envelopes and (C) a JSON header, a TAB, then the payload's own JSON:
      - **Speed and size do not decide.** End to end the three are equal within noise: WebView2 unary 59–79 µs, 100k rows in 163–164 ms, push p99 1.2–1.4 ms. B and C add 20–90 bytes per message, +0.02% on batched streams.
      - **Recommendation: C.**
        - A cannot grow: an older decoder rejected a new header field, and a stray space shifted fields silently.
        - B loses the call id on a damaged frame, so that call hangs until its timeout. B also re-reads large arguments (533 µs against 220 µs for 223 KB).
        - C keeps the id, sends the buffered body unchanged, and its trace keys match the loopback headers.
      - **Whatever the choice, every encoding needs:**
        - version negotiation in `hello`/`open`;
        - "an unknown type for a pending call fails that call";
        - a safe-integer check on ids (B and C accepted Infinity, 2^53+1 and −7);
        - rejection of duplicate header keys (`"id":7,"id":8` cancelled call 8);
        - a tolerant read of trace values;
        - string messages only, never `PostWebMessageAsJson`, which halved stream speed, cost +50% host CPU and threw on `\ud800`.
  - **Stream failure delivery (decided, user, 2026-11-14: in-band).** Every item is a `Result`, so a terminal failure arrives in-band and a `for await` loop cannot skip it (`spikes/ts-results`, `docs/app-model.md` 6.2; decision "Stream failures in-band; start-up exit codes; the start-up hook"). The alternative, not chosen: the iterator yields the N items and then throws (`spikes/wire-details`).
    - **Measured by `spikes/stream-result-cost`** (WebView2 157, Node 24, WebKitGTK 2.52; 1M rows in batches of 1,000, credit window 8):
      - **Cost does not decide.**
        - One `Result` per item adds 3–10 ns per row on WebView2 (+8–16 on WebKitGTK) over plain items.
        - That is 0.4–0.6 % of the bridge's 1.63 µs per row.
        - No task over 50 ms.
        - A stalled consumer holds the same memory per buffered row as with plain items.
        - Push at 20,000 events/s costs +0.1–0.3 ms of page time per second.
      - **Allocation:** `match` with an inline handler object allocates +97 B per row on WebView2, mostly the literal and its closures. A shared handler or `unwrap` allocates +27–37 B.
      - **What does cost:** the prototype's `stream()`, an async generator layered over the transport's iterable. It costs +103 ns and +321 B per row on WebView2 (6.5 % of the bridge).
        - The generated client uses a hand-written iterator over the transport buffer, whichever delivery is chosen.
        - Items are wrapped when the iterator hands them out, not when a batch is decoded. Wrapping at decode keeps about 40 % more memory per buffered row in a stalled consumer.
      - **Hybrids (exploratory):**
        - One `Result` per batch was the fastest shape (−18 ns and −59 B per row), from skipping the per-row `next()`. It puts a nested loop and the batch size into app code, so at most it becomes a later bulk API.
        - Plain items plus a separate `stream.result` cost the same as the baseline, but a loop that never reads the result misses the failure silently.
  - **`CallError.kind` values (decided, user, 2026-11-14):** `cancelled`, `unavailable`, `rejected`, `failed`, `transport`, `response` and `protocol`, each with `code` and `execution` (see the decision log for the table). `execution` is `notStarted` only when the host is certain the command did not run; declared errors are never a `CallError`. Earlier state: the prototype in `spikes/contract-prototypes/ts-match/sdk.ts` had `transport`, `protocol`, `cancelled` and `internal`, and `application` for declared errors (with `execution: "completed"`). An undeclared code from a newer backend became `protocol` / `unknownErrorCase`. The writer failure that section 2 once called `response-serialization` is now `kind: "response"`, `code: "serialization"`. The reserved payload names come from this shape.
