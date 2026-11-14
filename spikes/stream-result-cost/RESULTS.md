# Cost of one `Result` per stream item: results

**Question.** The generated TypeScript client exposes backend streams and subscriptions as `AsyncIterable`. Two ways
to report a failure partway through are open (plan 2c, `docs/app-model.md` 6.2):
- **in-band**: every item the `for await` loop receives is an opaque `Result<T, E>` (class with `#private` fields,
  `match`/`unwrap`, a `Symbol.toStringTag` getter), and a terminal failure arrives as one more `Result`;
- **throwing iterator**: the loop receives plain items `T`, and after the last item the iterator throws a typed error
  (`spikes/wire-details`).

What does one `Result` per item cost when the loop is big, when the consumer is slow, and for a steady push of small
events, on V8 (Node, WebView2) and JavaScriptCore (WebKitGTK 6.0)? Is a hybrid clearly better?

## Verdict

**One `Result` per item is not a measurable cost against the bridge, for big loops, slow consumers or push.**
- **Big loop, 1M rows, WebView2:**
  - The baseline (plain items, throwing iterator) is 170 ns per row, including the page's own decode (140 ns per row).
  - Per row, the prototype `Result` adds **+7 ns** with `match` and a handler literal (what app code writes), +3 ns
    with a shared handler, and −2 ns (noise) with `unwrap`.
  - That is **0.4%** of the bridge's measured ~1.63 µs per row end to end (100k rows in 163 ms, `spikes/bridge-encoding`).
  - On JSC it is +16 / +8 / +8 ns over 320 ns; on Node +8 / +3 / +3 ns over 305 ns.
  - No variant produced a task longer than 1.3 ms on WebView2 and no long task (> 50 ms) on any engine. The page's
    task is one batch: decode plus the app's loop over 1,000 rows, 0.4–1.3 ms.
- **Allocation is the visible part, and it comes from the handler literal more than from the `Result`.**
  - On WebView2, `match` with a fresh literal allocates **+97 bytes per row** over the baseline's 243.
  - With a shared handler it is +27 bytes, with `unwrap` +37. A lean `Result` (below) with a shared handler or
    `unwrap` is +15–19.
  - On Node (no pointer compression) the figures double: +249 / +88 / +71 over 602.
  - The time cost of that garbage is inside the numbers above. On Node, 1M rows ran 11 scavenges (2.4 ms) instead of 8
    (1.8 ms).
- **A slow consumer: the `Result` changes nothing in what is retained, as long as it is made when the item is handed
  out.**
  - With the app stalled and the credit window full (8 × 1,000 rows), the page keeps **89 bytes per buffered row** on
    WebView2 in every lazily wrapping shape, the baseline included; 145 on Node.
  - JSC's RSS growth was 368 bytes per row for both shapes (64,000 rows held, fresh process each).
  - Only a client that wraps at decode time ("eager") keeps more: +36 bytes per row on WebView2 (+0.29 MB for the full
    window), +71 on Node, +91 RSS on JSC. **So wrap at hand-out, not at decode.**
  - Time with a consumer that awaits per item is unchanged within noise: a macrotask per item costs 3.26 µs on
    WebView2, and every `Result` shape is within ±21 ns of it.
- **Push, 20,000 events/s for 3 s, WebView2:**
  - The page spent 7.3 ms per second with plain items and 7.4–7.8 ms with any `Result` shape (+0.1 to +0.3 ms/s).
  - The timer-free burst gives the same picture: 210 ns per event, +2 to +15 ns with a `Result`.
  - That is about 0.03% of one core at 20,000 events/s.

**What does cost something is layering the stream as an async generator over another async iterable.** The
prototype's `stream()` (`spikes/app-model-check/sdk/c-call/sdk.ts`) wraps the transport's async iterable in an
`async function*`. That shape cost +103 ns per row and +321 bytes per row on WebView2 (1M loop); push cost +2.1 ms/s.
The same layering with plain items and a throw still cost +65 ns and +128 bytes. A hand-written iterator over the
transport buffer, as measured here for both designs, avoids it. Whichever design is chosen, the generated client
should not stack async generators. That is ten times the cost of the `Result`.

**Hybrids (exploratory):**
- **One `Result` per transport batch** was the fastest shape everywhere: −18 ns per row on WebView2 (152 against
  170 ns), −36 to −43 ns on Node and JSC, and −59 bytes per row.
  - The gain comes from skipping the per-row `next()` promise and iterator result, not from having fewer `Result`s:
    the baseline already has no `Result`.
  - The price: the app writes a nested loop, and the bridge's batch size becomes visible in the API.
  - 18 ns per row is 1.1% of the bridge's per-row cost, so it is not a reason to pick it. It is an option for a
    later bulk API (for example `rows.batches()`) if a real app shows the need.
- **Plain items plus `stream.result`** (a separate promise of the terminal `Result`) cost the same as the baseline
  (−4 to +2 ns, +0 bytes). It brings back the problem in-band delivery solves: a loop that never reads
  `stream.result` misses the failure silently.
- **Neither hybrid is clearly better** on the measured costs. The choice between in-band and throwing can be made on
  API grounds: cost does not decide it.

## Versions and machine

Recorded 2026-11-14.
- Machine: Windows 11 Pro Insider Preview 10.0.26320, Intel Core i9-13900K, 96 GB.
  WSL 2 Ubuntu 24.04 (32 CPUs visible).
- **Node** 24.21.0, V8 13.6.233.17-node.53, x64, `--expose-gc` (allocation runs add `--min-semi-space-size=64`).
- **WebView2** Runtime 157.0.4341.46 (Chromium 157), hosted by the existing NativeAOT host of `spikes/desktop-origin`
  (`Origin.Win`, `bin/dev` build, which has `--extra-filter`). This spike only runs that host.
  - Its window is offscreen and never activated; the page reported `visibilityState: "visible"`.
  - Profile: `artifacts/webview2-profiles/desktop-origin-stream-result-cost`, passed explicitly.
  - Browser arguments: `--enable-precise-memory-info --js-flags=--expose-gc`. The allocation runs use
    `--js-flags="--expose-gc --min-semi-space-size=64"`.
  - The page is served from `127.0.0.1` with COOP/COEP, so it is cross-origin isolated and `performance.now()`
    resolves 5 µs.
- **WebKitGTK 6.0**: WebKit 2.52.6, GTK 4.14.5 (Ubuntu packages), driven from Python GObject.
  - The `WebKitWebView` is never put in a presented window, so nothing is shown, and the page is `hidden`.
  - Ephemeral network session; the page is loaded from `file://`. `performance.now()` resolves 1 ms.
  - Run from `~/spar-spikes/stream-result-cost` with private `HOME`, `XDG_*_HOME` and `XDG_RUNTIME_DIR`.
- TypeScript 7.0.2 (type check) and rolldown 1.2.19 (bundling, not minified). Classes and `#private` fields stay native.
  The npm install is 54 MB in `node_modules/` (ignored).

## Method

| Part | Role |
|---|---|
| `src/sdk/core.ts`, `src/sdk/c-call/sdk.ts` | The SDK prototype, **copied unchanged** from `spikes/app-model-check/sdk/` (checked with `diff`): its `Result` class and its `stream()` async generator. |
| `src/rows.ts` | The payload: rows of six fields (`id`, `name`, `qty`, `price`, `status`, `version` as a decimal string that becomes a `bigint`), about 110 bytes of JSON each. Decoded with `JSON.parse` and then built **field by field with type checks**, as the generated client does (plan 2). |
| `src/wire.ts` | Stand-in bridge, shared by every variant (see below). |
| `src/variants.ts` | The stream shapes: hand-written async iterators over the shared feed, plus the prototype's generator layering. |
| `src/consumers.ts` | What the app writes for each shape: one function per (shape, style), so call sites stay monomorphic. The per-row work is the same: add `qty`, count the row. |
| `src/bench.ts` | Scenarios, checks and measurements. |
| `src/node-main.ts`, `src/page-main.ts`, `page/bench.html` | Node runner; page runner for WebView2 and WebKitGTK. |
| `tools/build.ts`, `tools/run-webview2.ts`, `tools/run-all.sh`, `tools/summarize.ts` | Bundling into `obj/bundle/`; the WebView2 runs; every run in order; the tables in `results/summary.md`. `tools/peek.cjs` prints one raw report. |
| `linux/run-webkit.sh`, `linux/webkit_bench.py` | The WebKitGTK runs in WSL, and the RSS and CPU readings of the web process. |

**The stand-in bridge** (`wire.ts`):
- A simulated host sends batches of 1,000 rows under credits: window 8, refilled in halves as the app consumes
  (plan 2c).
- Each batch is one `MessageChannel` message, so it arrives as its own task, as a bridge message does. The message
  carries a batch index.
- The page decodes the matching pre-built JSON text (8 distinct texts, built before measuring). So the page pays a
  real batch's decode, and no string is cloned per message.
- A terminal message follows: completed, or a failure (`response-serialization`, as `spikes/wire-details` proposes).
- The feed hands rows to the iterator and clears each slot, so a consumed row is not kept alive by the buffer.

**Variants** (same feed, same rows; only what the loop receives differs):

| Key | What the loop receives | App code |
|---|---|---|
| `plain` (baseline) | plain rows; the failure rejects `next()` with `SparError` | `try { for await (const row of s) … } catch` |
| `resultMatch` | prototype `new Result({ ok: true, value })` per row, made when the iterator hands it out; the failure is one more `Result` | `item.match({ success: row => …, failure: e => … })`, a fresh literal per item |
| `resultShared` | same | `item.match(SHARED)`, one frozen handler object made once (it cannot close over loop locals) |
| `resultUnwrap` | same | `const row = item.unwrap()` inside `try` |
| `leanMatch`, `leanShared`, `leanUnwrap` | a `Result` with the same public shape (`#private`, `match`, `unwrap`, tag) that stores the success value directly: one object per row instead of `Result` + outcome | as above |
| `eagerMatch` | prototype `Result`, made when the batch is decoded, so the buffer holds `Result`s | literal `match` |
| `batch` (exploratory) | one `Result` per batch; its value is the batch's rows | `const rows = b.match({ success: r => r, failure: … }); for (const row of rows) …` |
| `tail` (exploratory) | plain rows; the loop always ends normally; `stream.result` is a `Promise<Result<void>>` | loop, then `(await s.result).match(…)` |
| `genPlain` | the prototype's layering: a transport async iterable of outcome objects, wrapped by an `async function*` that yields rows and throws | as `plain` |
| `genResult` | the prototype's own `stream()`: `for await (const o of transport) yield new Result(o)` | as `resultMatch` |

**Checks**, run first on each engine: 2,500 rows (5 batches of 500) then the failure terminal, and the same without
the failure.
- Every variant delivered all 2,500 rows.
- Every variant reported the failure: `SparError(response-serialization)` for `plain`, `resultUnwrap`, `leanUnwrap`
  and `genPlain`; the `failure` handler with `CallError(response-serialization)` for the others.
- No variant reported a failure on the completed stream. All passed on every engine.
- Every timed run also checks the row count and the `qty` sum.

**Scenarios:**
- **Big loop.**
  - 100k and 1M rows (100 and 1,000 batches), fast consumer.
  - 2 warm-up and 7 measured rounds at 100k; 1 warm-up and 3 measured at 1M. Variants rotate in each round, with a
    forced GC between runs where available.
  - Reported:
    - ns per row, from the wall time of the whole stream;
    - the longest batch task: from the batch message to the moment the iterator finds the buffer empty, which
      covers decode plus the app's loop over that batch;
    - long tasks over 50 ms (`PerformanceObserver("longtask")`, WebView2 only);
    - GC count and time (Node `PerformanceObserver("gc")`).
- **Slow consumer, time.** The app awaits after each row:
  - a resolved promise (100k rows);
  - a macrotask (`MessageChannel`, 20k rows), which keeps the credit window full, at 7,999–8,000 rows buffered;
  - `setTimeout(0)` every 100 rows (12k rows, five variants, 1 round).
  - V8 also reports the peak heap growth sampled at each batch, without a forced GC. It is garbage-dominated.
- **Slow consumer, retained memory.**
  - The app takes one item and stalls. The host fills the window (8 × 1,000), the page forces a GC and reads the
    heap. Then it divides by the rows held: buffered rows plus what the app holds (1 row, or 1 batch for `batch`).
  - 3 readings per run, 9 per variant (V8).
  - JSC has no heap counter and no page GC. Instead, a fresh WebKitGTK process per reading records the web
    process's RSS growth while holding a window of 8 and of 64 batches, 3 readings each.
- **Allocation per row** (V8): the heap counter around streams of 2 and 6 batches, differenced (4,000 rows).
  - Separate processes/page loads with a 64 MB young generation, so no scavenge falls inside a measurement.
  - Node's GC observer saw 0 GCs in all 540 samples. WebView2 has no page GC observer, but its 15 repetitions per run
    agreed within 1%.
  - 15 repetitions × 3 runs; the median is reported.
- **Push.**
  - A `setTimeout(1)` loop delivers what is due at 20,000 events/s as one batch per tick, for 3 s, 2 rounds per run.
    It reports page busy time per second: the sum of tick tasks from delivery to the drained buffer.
  - The achieved rate is engine-dependent: 196 ticks/s on WebView2, 65 on Node on Windows.
  - A timer-free **push burst** also runs on every engine: 60,000 events in batches of 100 (5 ms worth at
    20,000/s), each its own task, back to back. It reports wall time per event, task hop included.
- **Runs:** 3 per engine, each a new process or page load: `results/raw/<engine>-<n>.json`,
  `alloc-<engine>-<n>.json`, `retained/`. Reported values are the median of per-run medians, with the range of
  per-run medians in brackets. "Δ" is the median over runs of (variant − baseline) within the same run.

Reproduce, from this folder (`npm install` first): `bash tools/run-all.sh` (Node, WebView2, then
`wsl -d Ubuntu-24.04 -- bash spikes/stream-result-cost/linux/run-webkit.sh all 3` from the repository root, then the
summary). It takes about 35 minutes.

## Results

All tables, with every variant and the min–max of per-run medians: `results/summary.md`. Below are the variants that
matter for the decision.

### Big loop, fast consumer: ns per row (Δ against the baseline)

| Variant | WebView2 100k | WebView2 1M | Node 1M | WebKitGTK 1M |
|---|---:|---:|---:|---:|
| page decode alone (no iterator) | 140 | 140 | 265 | 214 |
| **plain + throw (baseline)** | **174** | **170** | **305** | **320** |
| Result, `match` literal | 184 (+10) | 177 (+7) | 313 (+8) | 335 (+16) |
| Result, `match` shared handler | 180 (+5) | 173 (+3) | 310 (+3) | 327 (+8) |
| Result, `unwrap` | 180 (+6) | 172 (−2) | 309 (+3) | 327 (+8) |
| lean Result, `match` literal | 182 (+7) | 176 (+4) | 312 (+6) | 333 (+13) |
| lean Result, shared / `unwrap` | 178 / 177 (+5 / +4) | 172 / 171 (+1 / −1) | 308 / 306 (+2 / +1) | 324 / 325 (+5 / +7) |
| Result made at decode (eager) | 191 (+17) | 179 (+9) | 320 (+14) | 340 (+20) |
| one Result per batch (exploratory) | 154 (−19) | 152 (−18) | 268 (−36) | 277 (−43) |
| plain + `stream.result` (exploratory) | 175 (+2) | 168 (−4) | 305 (−0) | 322 (+2) |
| async generator, plain + throw | 229 (+56) | 235 (+65) | 404 (+97) | 426 (+106) |
| prototype `stream()`, Result | 281 (+107) | 276 (+103) | 422 (+114) | 442 (+122) |

- Per-run spreads were about ±2 ns on WebView2 at 100k, and up to ±10 ns at 1M; see the summary.
- WebKitGTK at 100k is quantized by its 1 ms clock (33 ms per sample), so only its 1M column is shown here.
- **Longest batch task:**
  - WebView2: 0.43–0.58 ms for the baseline, at most 1.07 ms for any `Result` shape, 1.29 ms for the async generator;
  - JSC: 1 ms, the clock resolution;
  - Node: at most 1.2 ms, except one 7.4 ms task during a GC (`tail`, 1M).
  - **No long task over 50 ms** on WebView2 in any scenario.
- **GC, Node, 1M rows (count / ms per run):** baseline 8 / 1.8; `match` literal 11 / 2.4; shared or `unwrap` 9 / 1.8–1.9;
  eager 12 / 2.8; batch 4 / 1.0; prototype `stream()` 20 / 3.6.
- **Against the bridge:** the WebView2 bridge moved 100k rows in 163 ms, about 1.63 µs per row end to end
  (`spikes/bridge-encoding`). On that scale:
  - +7–10 ns (`match` literal) is 0.4–0.6%;
  - +3–6 ns (shared handler or `unwrap`) is 0.2–0.4%;
  - the prototype's generator layering, +103–107 ns, is 6.5%.

### Allocation per row, fast loop (bytes; Δ against the baseline)

| Variant | WebView2 | Node |
|---|---:|---:|
| **plain + throw (baseline)** | **243** | **602** |
| Result, `match` literal | 340 (+97) | 851 (+249) |
| Result, `match` shared handler | 270 (+27) | 690 (+88) |
| Result, `unwrap` | 280 (+37) | 673 (+71) |
| lean Result, literal / shared / `unwrap` | 329 / 263 / 257 (+86 / +19 / +15) | 810 / 640 / 643 (+208 / +38 / +40) |
| Result made at decode (eager) | 371 (+128) | 866 (+263) |
| one Result per batch (exploratory) | 184 (−59) | 299 (−303) |
| plain + `stream.result` (exploratory) | 243 (+0) | 589 (−13) |
| async generator, plain + throw | 371 (+128) | 1,234 (+632) |
| prototype `stream()`, Result | 564 (+321) | 1,434 (+832) |

- The baseline's 243 bytes per row on WebView2 are mostly the row itself and its parse garbage. Batch, which skips
  the per-row iterator promise and result object, shows the decode alone allocates at most 184.
- The **handler literal** costs about 70 bytes per item on WebView2: the object plus two closures, against the
  shared handler. The lean `Result` saves only 7–23 bytes against the prototype's `Result` + outcome. V8 removes
  part of that allocation already (escape analysis after inlining `match`).
- JSC allocation per row could not be measured: there is no page heap counter.

### Slow consumer

Retained while the app is stalled with the credit window full (bytes per row held):

| Variant | WebView2 (heap, forced GC; 8,000 rows) | Node (same) | WebKitGTK RSS, 8,000 rows | WebKitGTK RSS, 64,000 rows |
|---|---:|---:|---:|---:|
| **plain + throw (baseline)** | **89** | **146** | **719** | **368** |
| Result (made at hand-out) | 89 (−1) | 145 (−1) | 715 (−4) | 368 (−0) |
| lean Result | 89 (−1) | 145 (−1) | 717 (−2) | 368 (−0) |
| Result made at decode (eager) | 126 (+36) | 218 (+71) | 843 (+124) | 460 (+91) |
| one Result per batch | 88 (−1) | 145 (−1) | 726 (+7) | 369 (+1) |
| plain + `stream.result` | 78 (−11) | 128 (−18) | 721 (+3) | 369 (+1) |
| prototype `stream()` / generator plain | 88 / 89 | 144 / 145 | 704 / 696 | 366 / 366 |

- V8 readings agreed within 1 byte per row across 9 readings. JSC readings were 3 fresh processes each: within ±3%
  at 64,000 rows, and up to 5% at 8,000.
- JSC's RSS includes uncollected decode garbage, which explains the larger per-row figure at the smaller window.
- **Unexplained:** `tail` read 11 bytes per row less than the baseline on WebView2 (18 on Node) in all 9 readings. It
  uses the same feed and the same rows. It is lower, not higher, and does not affect the verdict.
- The whole window of 8 × 1,000 rows kept 0.71 MB on WebView2 with or without lazy `Result`s, and 1.0 MB with eager ones.

Time with a consumer that awaits per row (ns per row; Δ):

| Variant | WebView2 microtask (100k) | WebView2 macrotask (20k, window full) | Node macrotask | WebKitGTK microtask | WebKitGTK macrotask |
|---|---:|---:|---:|---:|---:|
| **plain + throw (baseline)** | **196** | **3,256** | **1,452** | **380** | **84,850** |
| Result, `match` literal | 205 (+8) | 3,267 (+4) | 1,552 (+114) | 390 (+10) | 89,400 (−200) |
| Result, shared / `unwrap` | 198 / 200 (+0 / +3) | 3,242 / 3,278 (−12 / +21) | 1,475 / 1,470 (+24 / +20) | 370 / 380 (+0 / +0) | ±5,000 noise |
| eager | 208 (+11) | 3,295 (+16) | 1,421 (−13) | 400 (+20) | noise |
| prototype `stream()` | 290 (+94) | 3,422 (+166) | 1,610 (+142) | 500 (+120) | noise |

- With `setTimeout(0)` every 100 rows, measured on WebView2 and Node with five variants, every variant took the same
  time within noise: 47.0–47.8 µs per row on WebView2.
- The peak heap growth before GC was 2.2–3.5 MB on WebView2, and 3.0 MB with `Result`s against 2.5 MB for the baseline.
- Node's macrotask figures vary ±100 ns run to run. Its `match` literal +114 has a range of +43 to +129.

### Push

WebView2, 20,000 events/s for 3 s from a 5 ms timer (196 ticks/s, 2 rounds × 3 runs):

| Variant | page busy ms per s | Δ | ns per event | longest tick ms |
|---|---:|---:|---:|---:|
| **plain + throw (baseline)** | **7.3** [7.1–8.3] | — | 365 | 1.19 |
| Result, `match` literal | 7.5 | +0.3 | 376 | 0.23 |
| Result, shared / `unwrap` | 7.5 / 7.4 | +0.2 / −0.1 | 376 / 370 | ≤ 0.22 |
| lean Result (any style) | 7.5–7.7 | +0.1 to +0.2 | 375–387 | ≤ 0.61 |
| eager | 7.8 | +0.3 | 389 | 0.16 |
| one Result per batch | 7.0 | −0.3 | 351 | 0.19 |
| plain + `stream.result` | 7.5 | +0.2 | 374 | 0.17 |
| async generator, plain / prototype `stream()` | 9.5 / 9.5 | +1.4 / +2.1 | 474 / 475 | ≤ 0.21 |

- Every variant received 19,960–19,968 events per second.
- Node, same scenario (65 ticks/s, about 300 events per tick): baseline 10.2 ms/s; `Result` shapes +0.0 to +0.6;
  generator layering +2.1 / +2.5.
- **Push burst** (60,000 events in batches of 100, one task each; ns per event, task hop included):

  | Engine | Baseline | `Result` shapes | Batch | Generator layering |
  |---|---:|---:|---:|---:|
  | WebView2 | 210 | +2 to +15 | −21 | +57 / +92 |
  | Node | 335 | +7 to +20 | −37 | +89 / +125 |
  | WebKitGTK | 1,367 | −17 to +33 (clock-limited) | −50 | +117 / +133 |

  At 20,000 events/s, a +10 ns `Result` cost is 0.2 ms per second of page time.

## Not measured, and skips

- **WebKitGTK timer scenarios were skipped** (`slow-timer`, timer-driven `push`). The web view is not in a presented
  window, so the page is `hidden`, and WebKit throttles its DOM timers.
  - In a first run (`results/raw/webkitgtk-quick-timers-throttled.json`), 120 `setTimeout(0)` calls took 110 s:
    about 0.92 s each, after the first few fired within 1.4–1.6 ms.
  - The timer-driven push got 11 ticks in 2 s.
  - The timer-free push burst covers push cost on JSC instead. Push CPU per second on a visible WebKitGTK page was
    not measured.
- **JSC allocation per row** and JSC GC counts: there is no page heap counter or GC hook. Only RSS retention is
  reported.
- **GC time in WebView2**: there is no page GC observer. WebView2's longest-task and long-task figures include any GC
  pause, and none exceeded 1.3 ms.
- **A real bridge.** The stand-in delivers batches over a `MessageChannel` in the same page and decodes pre-built
  text. The comparison uses `spikes/bridge-encoding`'s 1.63 µs per row end to end on WebView2; no WebKitGTK bridge
  stream figure exists yet.
- **Subscription event unions** (`{ type: "event", value }`, `gap`, …) were not modelled: rows were delivered
  directly. The wrapper object costs the same in both designs.
- **Cancellation** (`break` out of the loop, abort) and its confirmation were not exercised. The iterators implement
  `return()` but no test calls it.
- **First-use (cold) cost**: the checks run on a cold engine but are not timed. Every timing is after warm-up.
- **React-like consumers** (a state update per item, rendering) and devtools display of `Result` objects.
- **Bundle size** of the variants, and type-level ergonomics (`for await` with `match` against `try`/`catch`).
- **Several concurrent streams**, and batches sized by bytes or time instead of rows.

## Pitfalls found

For `docs/pitfalls.md`, which this spike did not edit:
- **A `WebKitWebView` that is never presented is a hidden page, and WebKit throttles its timers.**
  - `document.visibilityState` is `"hidden"`. After the first few `setTimeout` calls, each fired about 0.92 s later.
  - `MessageChannel` tasks still ran, but slowly: about 85 µs per hop in this headless setup, against 3.3 µs on
    WebView2.
  - Timer-based measurements (rates, pacing) need a visible window or a timer-free driver.
- **On Windows, Node's `setTimeout(1)` fires about every 15 ms** (65 ticks per second observed). A timer-driven
  rate on Node delivers about 300 events per tick at 20,000/s, not 20.
- **A clock-resolution probe that loops until it sees a sub-microsecond step never ends on a 1 ms clock.** Each
  iteration waits a full millisecond on WebKitGTK. Bound the loop by iterations (a first WebKitGTK run here hung for
  minutes).
- **Async generators stacked over another async iterable cost more per item than any `Result` wrapper.** The SDK
  prototype's `stream()` does this: +100 ns and +320 bytes per item on WebView2. Generate a hand-written iterator over
  the transport buffer.
- **Wrap stream items when the iterator hands them out, not when a batch is decoded.** Eager wrapping makes a
  stalled consumer keep 40% more memory per buffered row (+36 bytes on WebView2).

## Leftovers

- `~/spar-spikes/stream-result-cost/` in WSL (2.8 MB: page copy, private HOME/XDG folders, raw outputs).
- `artifacts/webview2-profiles/desktop-origin-stream-result-cost` (57 MB WebView2 profile, disposable).
- `artifacts/stream-result-cost-run-all.log` (progress log of the last full run, disposable).
- `node_modules/` (54 MB) and `obj/bundle/` in this folder, both ignored by git.
