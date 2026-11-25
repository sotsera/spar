# Bridge message encoding: results

> A spike. Its code was deleted with `spikes/` at the start of M2; it is in git history at [`spikes/bridge-encoding/`](https://github.com/sotsera/spar/tree/24f5db0942833047c81fa692908410596f3b8960/spikes/bridge-encoding) (commit `24f5db0`), and this record and its `results/` moved here. Paths under `spikes/` below name that history.

**Question.** Which encoding should the bridge protocol (plan section 2c) use: (A) the spikes' letter-prefixed text
lines, (B) JSON envelopes as `spikes/wire-details` proposed, or (C) something better?

## Summary and recommendation

Recorded 2026-11-14 on the development machine (Windows 11 Pro 10.0.26320, 32 logical processors, SDK 11.0.100,
NativeAOT win-x64, WebView2 Runtime 157, Playwright's `chrome-headless-shell` 156.0.8078.4, WebKitGTK 2.52.6 in WSL
Ubuntu 24.04). Every number below was observed in this folder; the tables are in `results/`.

**Speed and size do not decide it.**
- **End to end, A, B and C perform the same within run-to-run noise** on WebView2 and on the loopback channel. For
  example:
  - unary round trip on WebView2: 59 / 79 / 61 µs mean (ranges overlap: 56–94, 61–102, 60–64);
  - 100k rows at batch 1000: 163 / 164 / 163 ms on WebView2 and 66 / 69 / 59 ms on loopback;
  - push at 20,000 events/s: p99 1.37 / 1.24 / 1.23 ms, with nothing dropped by any encoding;
  - cancel → confirmed: p99 0.2–0.3 ms on WebView2.
- **The codec cost is nanoseconds, against 60–400 µs per round trip.**
  - Page decode of a small result: B is 14–24% *faster* than A (one `JSON.parse`), C 4–24% slower than A.
  - For item batches all three equal `JSON.parse` of the payload alone (0.98–1.01×).
  - In C# (NativeAOT), A decodes a call in 65 ns, C in 220 ns and B in 324 ns.
  - B's one real cost: the host must skip over `arg` before the generated reader parses it, so a large argument is
    scanned twice. For a 223 KB argument, decode plus validate took 533 µs for B against 231 µs for A and 220 µs for C.
- **Size:** B and C add 20–90 bytes per message.
  - That is +31–33% on a small unary exchange (250 → 333/329 bytes), and +0.02% on 100k rows at batch 1000.
  - It matters only for tiny frames: 100k rows at batch 1 grow by 19% (B) and 16% (C); one push event per frame grows
    by 54% (B) and 45% (C). The batching rules (bytes or time per batch) already avoid tiny frames.
- **`PostWebMessageAsJson` (variant Bj) is worse.**
  - Streams ran at half speed: 100k rows took 328 ms against 163 ms.
  - Host CPU rose by about 50% (2,969 against 2,094 ms per run).
  - It throws `E_INVALIDARG` for a `\ud800` escape that `JSON.parse` accepts.
  - Keep strings.

**Robustness, extensibility and readability decide it:**

| | A letters | B JSON envelope | C JSON header + TAB + payload |
|---|---|---|---|
| New header field in an existing kind | **breaks old peers** (positional: an old decoder read `q … - 500 {…}` as a bad argument, `invalid-request`) | ignored by old peers | ignored by old peers |
| Damaged payload (truncated, two frames joined) | id recovered, call failed precisely | **page: id lost** (one `JSON.parse` fails), the call hangs until its timeout; host: id kept only if it came before the damage | id recovered, as A |
| A field shifted by a stray space | **silently misparsed** (contract `be nch` made the fingerprint the trace field); caught only because the payload then failed | impossible | impossible |
| Call ids | canonical decimal checked by both sides | JSON number: the page accepted `1e10000`-style ids (Infinity), `2^53+1` (rounded to `2^53`) and `-7` until a safe-integer check is added | as B |
| Duplicate keys | n/a | last wins: `{"type":"cancel","id":7,"id":8}` cancels 8 | as B, in the header |
| Trace context | own syntax; `encodeURIComponent` **throws** `URIError` on a lone surrogate in `tracestate` | JSON strings; .NET `GetString` throws on a `\ud800` escape (must be read tolerantly) | as B |
| Buffered response | header then the buffer, unchanged | the body must be spliced into the envelope (or written into it) | header then the buffer, unchanged |
| Large argument (host) | one pass | two passes (skip, then read) | one pass |
| Log and devtools | compact; needs the letter table | self-describing; one JSON value per line | self-describing; not one JSON value per line (header TAB payload) |

**Recommendation: C, a JSON header object, a TAB, then the payload's own JSON.**
- It keeps A's strengths: the payload untouched, the id recoverable, one pass on the host, and buffered bodies sent
  as they are.
- It keeps B's strengths: named fields, unknown fields ignored, JSON escaping instead of a private syntax, and an
  agent can read a line without a table.
- It costs 20–90 bytes and tens of nanoseconds per message, which no measurement could see end to end.
- B is the close second if "every line is one JSON value" matters more to the user than payload isolation.
- A should not be kept: its positional fields cannot grow.

Rules C needs, whichever encoding is chosen:
- **id:** a safe non-negative integer, checked on both sides (`Number.isSafeInteger`; .NET range check);
- **duplicate header keys:** rejected (the DTO bit-mask rule);
- **unknown types and fields:**
  - an unknown header field is ignored;
  - an unknown type without a known call id is ignored;
  - an unknown type for a pending call fails that call as a protocol error, so that a call can never hang;
- **version:** `hello` carries the page's `protocol` and `open` the host's; the host sends only the types the page
  announced;
- **trace fields:** a malformed `traceparent` or `tracestate` (a lone-surrogate escape included) is dropped, never a
  reason to refuse the call;
- **framing:** one message per `postMessage` string, one per line on the loopback channel. Compact JSON never contains
  a raw newline or TAB, which was verified for both writers.

## Method

| Part | Role |
|---|---|
| `Bridge.Core` | The three codecs over one `Frame` model (`LetterCodec` = A, `EnvelopeCodec` = B, `HeaderCodec` = C), with `Utf8JsonReader`/`Utf8JsonWriter` and no reflection. Also the payload writers (10-column rows of about 228 bytes, push events of about 70 bytes, notes), the robustness corpus, and `BridgeSession`, a transport-neutral host: unary `echo`/`save`/`fail`/`wait`, the pull stream `rows` with credits, and the subscription `watch` (a drop-oldest ring filled by a producer thread). Buffered results, exactly one terminal message per call, and a memory for cancels that overtake their call. |
| `Bridge.Bench` | NativeAOT console: `sizes`, `cost` (median of 7 samples of at least 40 ms each, encodings interleaved), `robust` (page → host cases), `samples`, and `corpus` (`corpus.js` for the page). |
| `Bridge.Desktop` | NativeAOT WebView2 host. Offscreen window, shown with no activation and closed after each run. Profile in `artifacts/webview2-profiles/bridge-encoding`. Origin `https://app.spar.invalid`; every request under it is answered. Frames go over `postMessage` strings (`PostWebMessageAsString` / `TryGetWebMessageAsString`), or JSON for Bj (`PostWebMessageAsJson` / `GetWebMessageAsJson`). Page main-thread time comes from in-process CDP `Performance.getMetrics` (`TaskDuration`). |
| `Bridge.Loopback` | NativeAOT Kestrel host on 127.0.0.1, with the spikes' channel: one long-lived NDJSON response per document, host to page, and POSTed lines from page to host. It launches the cached `chrome-headless-shell` with its own profile in `artifacts/bridge-encoding/chromium-profile`, reads `TaskDuration` over CDP on the page target, and kills only the process tree it started. Unary calls also go over the channel here, so that the encoding is exercised; the plan's `fetch` unary path is encoding-neutral and was not re-measured. |
| `page/bench.js` | One classic script for every engine. `mode=micro`: decode and encode cost of A/B/C, interleaved, plus the page robustness cases. `mode=full&enc=…`: the message mix end to end. `mode=probe`: what WebView2's message APIs do with awkward strings. |
| `linux/` | `run-webkit.sh` plus `webkit_micro.py`: `mode=micro` in WebKitGTK (GTK 3, WebKit2 4.1, `Gtk.OffscreenWindow`, ephemeral web context). |
| `run-all.ps1`, `summarize.mjs` | Run everything on Windows, one process at a time (3 runs of each), then write `results/*.md`. |

The message mix of a full run:
- 300 warm-up calls;
- 2,000 sequential `echo` calls (77-byte argument);
- 2,000 `save` calls carrying `traceparent` and `tracestate`;
- 3 × 100k rows at batch 1000 and 3 × at batch 100 (credits window 8, refilled in halves);
- 20,000 events/s for 3 s, at up to 1,000 per frame and then at up to 20 per frame;
- 200 cancels of calls that run until cancelled;
- one `response-serialization` failure and one unknown command.

Every run passed every functional check, with every encoding: all results ok, every row delivered, 200 of 200 cancels
confirmed, both failures classified, no event out of order, no malformed frame on the host.

Reproduce from the repository root:

```powershell
pwsh spikes/bridge-encoding/run-all.ps1 -Runs 3
wsl -d Ubuntu-24.04 -- bash spikes/bridge-encoding/linux/run-webkit.sh
node spikes/bridge-encoding/summarize.mjs
```

## Results

Full tables:
- sizes: `results/sizes.md`;
- C# cost: `results/cost-csharp.md`, and `results/cost-csharp-run1.md` (an earlier run with the same code);
- JavaScript cost: `results/micro-js.md`;
- end to end: `results/e2e-webview2.md`, `results/e2e-loopback.md`;
- robustness: `results/robustness-csharp.md`, `results/robustness-js.md`, `results/probe-webview2.md`;
- what each encoding looks like in a log: `results/samples.md`;
- the run log: `results/run-log.txt`.

Raw JSON is in `artifacts/bridge-encoding/{desktop,loopback,webkitgtk}/`.

### 1. Size (UTF-8 bytes)

| Exchange | Payload | A | B | C |
|---|---:|---:|---:|---:|
| unary call + result (note) | 206 | 250 | 333 (+33%) | 329 (+32%) |
| … with `traceparent` | 206 | 304 | 405 | 401 |
| … with `traceparent` + `tracestate` | 206 | 350 | 460 | 456 |
| cancel + cancelled | 0 | 12 | 57 | 57 |
| 100k rows, batch 1000 (127 messages) | 22,842,090 | 22,843,240 | +4,702 (+0.02%) | +3,896 (+0.02%) |
| 100k rows, batch 100 (1,252 messages) | 22,842,990 | 22,854,039 | +0.20% | +0.17% |
| 100k rows, batch 1 (125,002 messages) | 22,941,990 | 24,042,037 | +19.1% | +15.8% |
| 60k push events, 20 per frame | 4,406,260 | 4,439,311 | +3.1% | +2.6% |
| 60k push events, 1 per frame | 4,463,242 | 5,123,293 | +53.9% | +44.5% |

The trace field costs the same in every encoding, about 55 bytes for `traceparent`, plus the key names in B and C (14
and 13 bytes).

### 2. Encode and decode cost

C# on NativeAOT, ns per message (`results/cost-csharp.md`):

| | A | B | C |
|---|---:|---:|---:|
| encode a result header around a buffered body | 16–22 | 38–49 | 39–51 |
| encode + UTF-16 string for WebView2, 1000-row batch | 57,852 | 59,036 | 59,225 |
| decode a unary call (note) | 66 | 324 | 220 |
| decode + validate it, starting from the WebView2 string | 181 | 447 | 343 |
| decode credits / cancel | 17 / 12 | 117 / 86 | 121 / 92 |
| decode + validate a 223 KB argument | 231,043 | **532,759** | 220,100 |

JavaScript, ns per message, median of 3 runs (`results/micro-js.md`):

| Engine | small result A / B / C (payload alone) | 1000-row batch A / B / C (payload alone) | encode call A / B / C | encode cancel A / B / C |
|---|---|---|---|---|
| WebView2 157 | 272 / 235 / 291 (169) | 188,608 / 187,952 / 187,162 (182,927) | 82 / 156 / 109 | 14 / 42 / 43 |
| Chromium 156 headless | 262 / 216 / 272 (157) | 201,987 / 201,307 / 199,338 (200,654) | 87 / 155 / 116 | 14 / 45 / 44 |
| WebKitGTK 2.52 (WSL) | 310 / 270 / 330 (193) | 355,263 / 355,263 / 346,667 (350,000) | 144 / 177 / 181 | 14 / 57 / 58 |

### 3. End to end

Median of 3 runs, range in brackets; the full tables have more rows.

| Measure | WebView2 A | B | C | Bj | Loopback A | B | C |
|---|---:|---:|---:|---:|---:|---:|---:|
| unary echo, mean µs | 58.9 (56–94) | 78.5 (61–102) | 61.2 (60–64) | 63.6 (61–107) | 438 (411–544) | 399 (370–614) | 405 (386–456) |
| unary echo, p50 / p99 ms | 0.1 / 0.2 | 0.1 / 0.2 | 0.1 / 0.2 | 0.1 / 0.2 | 0.4 / 0.8 | 0.4 / 0.7 | 0.4 / 0.8 |
| unary traced save, mean µs | 58.8 | 64.3 | 60.7 | 76.4 | 383 | 405 | 469 |
| 100k rows batch 1000, ms | 163 (157–169) | 164 (158–167) | 163 (158–167) | **328** (309–352) | 66 (49–107) | 69 (54–97) | 59 (48–98) |
| … page task ms | 76.6 | 76.7 | 77.0 | 58.8 | 66.2 | 67.0 | 58.6 |
| 100k rows batch 100, ms | 189 | 189 | 193 | 327 | 108 | 116 | 117 |
| push 20k/s, p50 / p99 ms | 0.56 / 1.37 | 0.58 / 1.24 | 0.68 / 1.23 | 0.74 / 1.33 | 0.52 / 1.37 | 0.56 / 1.08 | 0.60 / 1.05 |
| push 20k/s, page task ms per 3 s | 42.1 | 43.2 | 47.2 | 36.8 | 62.4 | 74.2 | 70.2 |
| push at ≤20 per frame (about 3,080 frames), p99 ms | 0.94 | 1.09 | 1.14 | 1.13 | 2.00 | 1.90 | 2.34 |
| … page task ms per 3 s | 73.9 | 71.7 | 81.3 | 77.0 | 163 | 154 | 187 |
| cancel → confirmed, p99 ms | 0.2 | 0.2 | 0.3 | 0.3 | 0.8 | 1.3 | 0.9 |
| host CPU per run, ms | 2,094 | 1,953 | 1,891 | **2,969** | 1,328 | 1,250 | 1,250 |

- Every encoding delivered all 100,000 rows and 59.7k–60k of 60k push events, with no gaps. The event count is a little
  below 60,000 because the run ends at a fixed time.
- With Bj, the page's own handler time is almost zero (1 ms per stream against 33 ms), because WebView2 parses the
  JSON before the page sees it. The stream still takes twice as long: the work moved, and grew.

### 4. Robustness

Page decoders (`results/robustness-js.md`; the outcome was the same on all three engines, only the error text
differs):
- **Payload text:** escaped and raw `\n`, U+2028 and U+2029 decoded correctly in every encoding. A `\ud800` escape
  decoded to a lone surrogate, as `JSON.parse` does.
- **Truncated or concatenated frames:** A and C failed the frame and named call 7; B could not name any call (id −1).
- **Ids:**
  - A rejected a 10,000-digit id, `2^53+1`, `07` and `-7`;
  - B and C, which carry JSON numbers, accepted the 10,000-digit id (it parsed to Infinity), `2^53+1` (it became
    `2^53`) and `-7`; they rejected only `07`, which is invalid JSON.
- **A newer host:**
  - a new kind was `unrecognized` with its id, in every encoding;
  - a new header field was malformed for A (`i 7 0 3 […]`) and ignored by B and C.
- **Page encoders:**
  - A's `encodeURIComponent` threw `URIError` on a lone surrogate in `tracestate`;
  - B and C sent `"\ud800"` there, which .NET's `GetString` rejects (see below);
  - every encoding threw on a `bigint` argument (the generated encoder's job, `spikes/wire-details`).

Host decoders (`results/robustness-csharp.md`):
- **Rejected cheaply in every encoding:** an empty frame, a BOM, ids with 10,000 digits (0.6–4.3 µs), ids above
  `2^53`, and fractional ids.
- **A misparses a stray space silently:** in `q 7 be nch save …` the fingerprint moved into the trace field. Only the
  invalid payload stopped it.
- **A drops part of `tracestate` at a raw space:** `k=a b` became `k=a`.
- **B and C reject the whole call on a lone-surrogate escape in `tracestate`** (`InvalidOperationException` from
  `GetString`). The trace rule needs a tolerant read there.
- **Duplicate keys:** B and C let a repeated `id` win as the last one. A has no keys to repeat.
- **Inside the argument:** a lone-surrogate escape or invalid UTF-8 passes header decoding and structural validation in
  every encoding. It fails only at `GetString`, which is the generated reader's job (`spikes/serializer-fuzz`).
- **A 10 MB argument:** decoding located it in 1.1 µs (A) and 0.4 µs (C), and skipped through it in 624 µs (B).

WebView2 transport (`results/probe-webview2.md`). These apply to every encoding:
- **Page → host, a string with a lone surrogate:** `TryGetWebMessageAsString` fails with `E_INVALIDARG`. Only
  `GetWebMessageAsJson` sees it, as `"…\ud800…"`.
- **NUL ends a string message in both directions:** `a…b\nc\0d` arrived as `a…b\nc`. All three encodings escape NUL
  inside JSON, so only raw header text could carry one.
- **Host → page, `PostWebMessageAsString` with a lone surrogate:** the page received U+FFFD.
- **`PostWebMessageAsJson`:**
  - a raw lone surrogate arrived as U+FFFD;
  - an escaped one (`\ud800`), text that is not JSON, and two JSON values each threw `E_INVALIDARG`;
  - a 20-digit number lost precision;
  - `__proto__` became an own key and a duplicate key kept the last value, as with `JSON.parse`.
- **Page `postMessage(object)`:** NaN and Infinity became `null`, `undefined` was dropped, a `Date` became a string,
  and a `bigint` threw.

### 5. Fit with the wire rules and the trace field

- **Buffered responses.**
  - A and C send the buffered body unchanged after a header.
  - B must splice the body's keys into the envelope (`{"type":"result","id":7,` plus the body without its `{`), or the
    writer must write the envelope first and reset it to that prefix on failure. Both work; B couples the envelope to
    the body writer.
  - Stream items are identical in all three: each batch is the item array.
- **`response-serialization` with `execution`.** All three carry it in the result body
  (`{"ok":false,"failure":{"kind":"response-serialization","execution":"completed"}}`), and the page classified it in
  every run. The outcome then lives in two places in every encoding: the message kind
  (`result`/`not-executed`/`outcome-unknown`/`cancelled`) and `failure.kind`. Folding the kinds into `result` with a
  failure kind would simplify the SDK whatever the encoding (a protocol choice, not measured).
- **Credits:** 8, 40 and 40 bytes per `credits` message; equal end to end.
- **Exactly one terminal message.**
  - Every encoding needs the decoder to know which types are terminal. A newer terminal type would leave an older page
    waiting, hence the version and unknown-type rules in the recommendation.
  - Damage that B cannot attribute to a call has the same effect, until the call's own timeout.
- **Trace field.** A's `-` / `traceparent;escaped-tracestate` is a third syntax beside the HTTP headers. B and C use
  the W3C names as JSON keys, which match the loopback headers one for one.

## Problems met

- **` ` and ` ` written in tool input became raw characters in two source files** (`Bridge.Bench/Program.cs`
  and `page/bench.js`; the same escape in `Corpus.cs` survived). C# treats U+2028 as a line break, so the build failed
  with CS1010. `String.Replace` in PowerShell did not help, because the replacement text was converted too. The escapes
  were finally written from character codes (`[char]92 + 'u2028'`). This is a tooling pitfall for agents editing files
  that need these escapes.
- **The first loopback channel never reached the page.** Kestrel's `StartAsync` did not send the headers until a flush,
  so the page's `fetch` waited. An explicit `FlushAsync` after `StartAsync` fixed it.
- **A `Start-Job` run of the loopback host died with its PowerShell process.** Other `chrome-headless-shell` processes
  were found afterwards, but their command lines (Playwright's `--disable-field-trial-config`, no spike profile) showed
  they belonged to another session. They were left alone. Every later run was invoked directly, and confirmed its own
  browser had exited.
- **WSL runs were cut short by `SIGTERM`** (exit 143) 4 times, once while running, 3 times within a second of starting.
  At the same time another session's `linux-memory` spike was running WebKit in the same distribution. The runs were
  retried until 3 succeeded. Those 3 ran beside that load, and WebKitGTK's `performance.now()` has 1 ms granularity, so
  treat the WebKitGTK figures as indicative.
- **Timer granularity:**
  - the push producer's `Thread.Sleep(1)` ran at about 15.6 ms ticks, so frames carried about 315 events (191 frames).
    A second push phase capped frames at 20 events to load the per-frame path;
  - `performance.now()` is coarsened to 0.1 ms in Chromium. Unary latency is therefore also given as the loop mean.
- **One WebView2 browser took 6.5 s to exit** (the first probe run), as `docs/pitfalls.md` describes. Every run waited
  for the browser to exit before the next one started.

## Not tested

- **WebKitGTK end to end:** no Linux host was built for the script message handler and `evaluate_javascript`, and the
  loopback page was not run under WebKit. Only the codec cost and the page robustness cases ran there.
- **B with "`arg` last" read in place by the generated reader.** That would avoid the second pass at the price of a
  key-order rule. A length-prefixed header (a decimal byte count) was not built either: UTF-8 bytes in C# and UTF-16
  units in JavaScript would disagree, so it would need `TextEncoder` work on the page.
- **Loopback unary over `fetch`** (the plan's path): the encoding only changes its body, not its framing.
- **Very large unary results** through each encoding; uploads (page-to-host streams); several concurrent streams and
  subscriptions on one channel.
- **Not measured:** compression; memory per encoding; ARM64; WebView2 `CoreWebView2SharedBuffer`.
- **Fixes the recommendation lists but this spike did not make:** a duplicate-key check in the JSON header readers, a
  safe-integer id check in the B and C page decoders, and tolerant trace reading.

## Paths created

- In the repository:
  - `spikes/bridge-encoding/`;
  - `artifacts/bridge-encoding/`: raw JSON, `corpus.js`, the run log, and the headless Chromium profile
    `chromium-profile/`;
  - `artifacts/webview2-profiles/bridge-encoding/`.
- Outside the repository: only `~/spar-spikes/bridge-encoding/` in WSL. It holds `home/` (the private `HOME` and every
  `XDG_*_HOME`), `page/`, `out/` and `webkit_micro.py`. Nothing else was written outside the repository.
- Read only: Playwright's browser cache under `%LOCALAPPDATA%\ms-playwright`.
