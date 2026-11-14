# Cost of making the pending call a real `Promise`: results

**Question.** The generated TypeScript client returns a pending call object `Call<T, E>` from each unary command.
Today (prototype "form C", `spikes/ts-results/forms/c-call/sdk.ts`) it is a thenable class that implements
`PromiseLike<Result<T, E>>` and has `match` and `unwrap`. The proposal is to make it a real `Promise` subclass,
`class Call<T, E> extends Promise<Result<T, E>>`, with `static get [Symbol.species]() { return Promise; }` and the same
`match`/`unwrap` on the prototype, constructed by the client with an executor that stores `resolve`. What does that
cost per call, in time, microtask ticks and heap, on V8 (Node and WebView2) and on JavaScriptCore (WebKitGTK 6.0), and
does any behaviour change that app authors would notice?

## Verdict

**The per-call cost is negligible against a bridge round trip.**
- Against a plain native `Promise<Result>`, the proposal adds at most **+91 ns on WebView2**, +95 ns on Node and
  +261 ns on WebKitGTK. The WebView2 bridge measured 59–79 µs per unary round trip (`spikes/bridge-encoding`), so
  that is **0.15%** on WebView2 and 0.44% at worst on JSC.
- Against form C (today), the proposal is **faster or equal** for `await call.match(h)` and `await call.unwrap()`
  on V8 (−7 to −8 ns on WebView2). It is slower for `(await call).match(h)`: +19 ns on WebView2, +23 ns on Node,
  +69 ns on JSC. On JSC every style costs 40–70 ns more than form C.
- **Heap: the proposal needs no more than form C on V8, and usually less.**
  - A pending call keeps 75 bytes (WebView2), the same as a native promise; form C keeps 163.
  - 10,000 outstanding calls with awaiting consumers peaked at 5.3–6.3 MB on WebView2, against 4.6–5.4 MB for native
    promises and 6.7–7.3 MB for form C.
  - On JSC, process RSS per outstanding call with a consumer was between 8% below and 3% above form C (926–1,258
    against 969–1,224 bytes).

**Behaviour: nothing that worked with form C changes; against plain promises, `await call` is 1 tick later.**
- **Microtask ticks** are identical on all three engines. The subclass never needs more ticks than form C, and is 1
  tick earlier in several cases. Against a native promise:
  - `await call` on a pending call continues **1 tick later** (2 if the call had already settled), because `await`
    calls `PromiseResolve`, which wraps any promise whose `constructor` is not `Promise`;
  - `call.then(...)` costs the same ticks as on a native promise;
  - `call.match`/`call.unwrap` cost the same as a native `.then(r => r.match(h))`.
- **Order:**
  - Two calls consumed in the same style always continue in settle order (all 10 same-style cases, every variant,
    every engine).
  - Between two calls consumed in different styles, the order is exactly form C's. 12 of 50 style pairs differ from
    plain native promises, the same 12 for form C.
  - Against an unrelated native promise settled later in the same task, an awaited call is overtaken, as with form C
    today. A `.then` consumer now keeps native order; with form C it did not.
  - Only code that depends on microtask order *within one task* could notice. Across tasks (separate bridge
    messages) nothing changes.
- **Parity:**
  - `call instanceof Promise` is now `true` (form C: `false`), and the tag is `[object Promise]`.
  - `then`, `catch` and `finally` return plain `Promise`s.
  - Success, declared errors, undeclared errors, infrastructure failures and both abort paths produce the same results
    as form C.
  - Unhandled-rejection counts are identical in all nine scenarios: 0 where nothing rejects unhandled, 1 for an
    un-awaited failing `unwrap()` and for an un-awaited `match` whose handler throws. The subclass adds no rejection
    noise of its own.

**Two side findings:**
- **Construct the subclass directly in the client.**
  - The `PromiseCall.of` wrapper from `spikes/frontend-frameworks` costs about twice as much as the proposal
    (+164 ns over native on WebView2).
  - It is up to 1 tick later than form C, and it keeps form C's allocation plus a promise.
- **Keep the `Symbol.species` override.**
  - Without it, `then`/`finally` return another `Call` that has a misleading `match`.
  - That variant costs +127 to +145 ns over native on WebView2, and adds a tick to `match`/`unwrap`.

An **exploratory variant**, not part of the proposal, sets `Call.prototype.constructor = Promise` instead of a species
getter.
- `PromiseResolve` and `SpeciesConstructor` then treat the call as a native promise, so its ticks and order were
  identical to native promises everywhere.
- Its cost was +17 to +25 ns over native for `await call` on every engine.
- The price: `call.constructor` reports `Promise` (still `instanceof Call`), and `Promise.resolve(call) === call`.
  Whether that is acceptable is a design question for the decision log; this spike only measured it.

## Versions and machine

Recorded 2026-11-14.
- Machine: Windows 11 Pro Insider Preview 10.0.26320, Intel Core i9-13900K (24 cores, 32 threads), 96 GB.
  WSL 2 Ubuntu 24.04, kernel 6.18.58.1-microsoft-standard-WSL2, 32 CPUs and 46 GB visible to it.
- **Node** 24.21.0, V8 13.6.233.17-node.53, x64, run with `--expose-gc`.
- **WebView2** Runtime 157.0.4341.46 (Chromium 157), hosted by the existing NativeAOT host of `spikes/desktop-origin`
  (`Origin.Win`, `bin/dev` build, which has `--extra-filter`).
  - The host window is offscreen and never activated.
  - The profile is in `artifacts/webview2-profiles/desktop-origin-call-promise-cost`, passed explicitly.
  - Browser arguments: `--enable-precise-memory-info --js-flags=--expose-gc`.
  - The page is served from `127.0.0.1` with COOP/COEP headers, so it is cross-origin isolated: `performance.now()`
    then resolves 5 µs (`results/probe-memory-webview2.json`).
- **WebKitGTK 6.0**: WebKit 2.52.6, GTK 4.14.5 (Ubuntu 24.04 packages), driven from Python GObject.
  - The `WebKitWebView` is never put in a presented window, so nothing is shown.
  - Ephemeral network session; the page is loaded from `file://`.
  - JavaScriptCore is the engine. `performance.now()` resolves only 1 ms there.
- TypeScript 7.0.2 (type check) and rolldown 1.2.19 (bundling, not minified). The output keeps native classes and
  `#private` fields.

## Method

| Part | Role |
|---|---|
| `src/forms/core.ts`, `src/forms/c-call/sdk.ts` | Form C, **copied unchanged** from `spikes/ts-results/forms/` (checked with `diff`). |
| `src/promise-call-of.ts` | `spikes/frontend-frameworks/shared/promise-call.ts`; only its type import points at the copy above. |
| `src/wire.ts` | Stand-in transport. A call registers its `resolve`; `deliver(outcome)` settles the oldest one, building the `Result` (native, subclass) or passing the raw outcome (form C, whose `invoke` adds `.then(o => new Result(o))`). A queue that keeps its capacity, so it allocates nothing per call. |
| `src/variants.ts` | The six shapes (below), each with the same abort rule as form C: an aborted call resolves to a `cancelled` failure and never rejects. |
| `src/bench.ts` | Engine-neutral benchmark: parity, ticks, order, timing, heap, burst. |
| `src/node-main.ts`, `src/page-main.ts`, `page/bench.html` | Node runner; page runner for WebView2 and WebKitGTK. |
| `src/native-alone.ts` | Control: the native baseline in a Node process that never defines a Promise subclass. |
| `tools/build.ts`, `tools/run-webview2.ts`, `tools/summarize.ts` | Bundling into `obj/bundle/`; the WebView2 run (a static server on a random loopback port plus the desktop-origin host); the tables in `results/summary.md`. |
| `linux/run-webkit.sh`, `linux/webkit_bench.py` | The WebKitGTK 6.0 runs in WSL. |

Variants:
1. `native`: `new Promise<Result>(resolve => enqueue(resolve))`, the baseline.
2. `formC`: form C unchanged. The transport returns a native `Promise<Outcome>`, and `invoke` wraps
   `.then(o => new Result(o))` in the thenable `Call`.
3. `subclass`, **the proposal**: `class Call extends Promise<Result>` with `static get [Symbol.species]()` returning
   `Promise`, and `match(h) { return this.then(r => r.match(h)); }` and `unwrap()` likewise. It is built by the client
   with `new Call(resolve => enqueue(resolve))`.
4. `of`: the frontend-frameworks prototype, `PromiseCall.of(formC call)`.
5. `noSpecies`: as 3, without the species override.
6. `ctor` (exploratory): as 3 without species, plus `Call.prototype.constructor = Promise`.

Consumption styles, all awaited by the app:
- (a) `(await call).match(h)`;
- (b) `await call.match(h)`;
- (c) `await call.unwrap()`.

The native baseline has no methods, so for (b) and (c) it uses `await p.then(r => r.match(h))` and
`await p.then(r => r.unwrap())`. The tick and order tests add `call.then(...)` and `await Promise.all([call])`.

Measurements:
- **Time per call.**
  - One monomorphic function per (variant, style): create the call, let the transport settle it, consume it, in a
    sequential loop.
  - The batch is calibrated so that a sample takes at least 60 ms (0.5–2 M calls).
  - 3 warm-up rounds and 7 measured rounds, with the 18 pairs in rotating order.
  - 3 runs per engine, each a new process or page load.
  - Reported: the median of per-run medians, the range of per-run medians, and the min–max of all samples.
  - In this loop the settle happens before the `await`, in the same task. That is the same work, in a different
    order. The burst below covers a call that is still pending when the app awaits it.
- **Ticks.**
  1. The consumer starts awaiting a pending call, and a macrotask lets every preparatory job run.
  2. The transport settles the call and immediately queues a ticker: a `queueMicrotask` chain that counts its steps.
  3. The consumer records the ticker's count when its next line runs. 0 means it ran before the first ticker step.
  - "Settled" repeats this with a call settled in an earlier task.
- **Order.**
  - X then a native promise N, settled in one task: who continues first?
  - Two calls of the same variant, A then B, for all 25 style pairs, pending and already settled. This test was
    added after the main runs; the variant code is the same.
- **Heap, V8.**
  - Bytes allocated per call on the timed path, from the heap counter: `process.memoryUsage().heapUsed` on Node,
    `performance.memory.usedJSHeapSize` on WebView2.
  - The counter is read around batches of 128 and 384 calls after a forced GC, and the two are differenced, which
    removes the measurement's own constant cost. 15 repetitions; the median is reported.
  - A `{a, b}` literal is the control. It measured exactly 40 bytes on Node (no pointer compression) and 20 on
    WebView2 (pointer compression).
  - Retained bytes per outstanding call: 20,000 calls, alone or each with an awaiting async consumer, heap after a
    forced GC. 3 per run, 9 in all.
- **Heap, JSC.**
  - JSC gives pages neither a heap counter nor a GC, so each reading starts a **fresh WebKitGTK process**.
  - It creates 100,000 calls (with or without consumers), and the Python host reads the `WebKitWebProcess` VmRSS
    before and after.
  - 3 readings each. The garbage made while creating the calls (for example the executor closure) is included.
  - The control is a `{a, b}` literal plus its array slot: 40–41 bytes.
- **Burst.**
  - 10,000 calls with awaiting consumers, a macrotask, then all settled in one task.
  - Reported: the time until the last consumer continues (mean of 15 bursts × 3 runs, so that WebKit's 1 ms clock
    averages out), and on V8 the heap growth before GC (peak) and after a forced GC (retained).
- **Parity**, run first on a cold engine:
  - shape: `instanceof`, tag, what `then`/`catch`/`finally` return, `Promise.resolve` identity, `all`/`race`/`allSettled`;
  - outcomes through `match`, `unwrap` and `await`;
  - the interceptor for unhandled infrastructure failures;
  - abort before the result and an already-aborted signal;
  - nine unhandled-rejection scenarios (`unhandledrejection` / `unhandledRejection` listeners).

Reproduce, from this folder (`npm install` first):

```powershell
npx tsc -p tsconfig.json; node tools/build.ts
1..3 | % { node --expose-gc obj/bundle/bench-node.mjs --out results/raw/node-$_.json }
node --expose-gc obj/bundle/bench-node.mjs --order --out results/raw/order/order-node.json
1..3 | % { node obj/bundle/native-alone.mjs > results/raw/controls/control-native-alone-node-$_.json }
node tools/run-webview2.ts --runs 3
node tools/run-webview2.ts --runs 1 --query mode=order --name order-webview2   # then move it to results/raw/order/order-webview2.json
wsl -d Ubuntu-24.04 -- bash linux/run-webkit.sh all 3
node tools/summarize.ts
```

## Results

All tables: `results/summary.md`. Raw reports: `results/raw/` (`<engine>-<run>.json`; `memory/` for the JSC RSS
readings; `order/`; `controls/`; `*.host.txt` for the WebView2 host's summary lines).

### Time per call, ns (median of per-run medians [range of per-run medians]; change against native)

WebView2 (the Windows host):

| Variant | `(await call).match(h)` | `await call.match(h)` | `await call.unwrap()` |
|---|---:|---:|---:|
| native Promise (baseline) | 44 [44–45] | 65 [64–66] | 62 [62–74] |
| form C thenable (today) | 116 [116–117] +72 | 95 [94–98] +30 | 93 [93–110] +31 |
| **Promise subclass, direct (proposal)** | **135 [135–136] +91** | **88 [87–88] +24** | **85 [83–85] +23** |
| Promise subclass via `of` | 208 [208–209] +164 | 170 [168–173] +105 | 166 [165–168] +104 |
| Promise subclass, no species | 171 [168–171] +127 | 210 [210–210] +145 | 205 [204–207] +143 |
| `constructor === Promise` (exploratory) | 69 [68–69] +24 | 90 [89–90] +25 | 85 [85–87] +23 |

Node (V8):

| Variant | `(await call).match(h)` | `await call.match(h)` | `await call.unwrap()` |
|---|---:|---:|---:|
| native Promise (baseline) | 53 [52–53] | 68 [67–69] | 66 [65–66] |
| form C thenable (today) | 126 [125–127] +73 | 103 [102–106] +35 | 99 [98–101] +32 |
| **Promise subclass, direct (proposal)** | **149 [148–149] +95** | **99 [99–100] +31** | **96 [95–96] +29** |
| Promise subclass via `of` | 232 [231–237] +179 | 186 [184–187] +118 | 182 [181–184] +116 |
| Promise subclass, no species | 184 [183–187] +131 | 233 [231–236] +165 | 228 [228–232] +162 |
| `constructor === Promise` (exploratory) | 72 [72–73] +19 | 100 [99–101] +33 | 96 [96–99] +30 |

WebKitGTK 6.0 (JSC):

| Variant | `(await call).match(h)` | `await call.match(h)` | `await call.unwrap()` |
|---|---:|---:|---:|
| native Promise (baseline) | 65 [61–69] | 113 [109–120] | 111 [107–118] |
| form C thenable (today) | 257 [250–271] +193 | 210 [202–216] +97 | 198 [196–206] +88 |
| **Promise subclass, direct (proposal)** | **326 [324–351] +261** | **250 [240–261] +137** | **246 [238–261] +135** |
| Promise subclass via `of` | 549 [528–584] +484 | 479 [460–498] +366 | 477 [450–492] +366 |
| Promise subclass, no species | 376 [357–385] +311 | 591 [557–605] +479 | 578 [553–589] +467 |
| `constructor === Promise` (exploratory) | 82 [80–92] +17 | 250 [237–265] +137 | 240 [238–261] +130 |

Spread:
- Within a run, most samples stayed within 10% of their median, with single outliers up to +30% on V8 and +35% on
  JSC; the full min–max is in `results/summary.md`.
- Node control: the native baseline alone, in a process that never defined a subclass, measured 47.5–51.0 /
  64.7–69.1 / 63.1–67.5 ns. In the main runs it measured 52–53 / 67–69 / 65–66. So defining the subclasses did not
  disable a fast path for ordinary promises: the difference is at most 5 ns on `await`, within the within-run spread.

In proportion: the proposal's worst case over native is 91 ns on WebView2. That is 0.15% of 59 µs and 0.12% of 79 µs.
On JSC it is 261 ns, 0.44% / 0.33% of the same WebView2 round trip; a WebKitGTK bridge round trip has not been
measured.

### Microtask ticks from the settle to the consumer's next line

Identical in every run and on all three engines.

| Variant | await (pending) | await (settled) | match | unwrap | then (pending) | Promise.all (pending / settled) |
|---|---:|---:|---:|---:|---:|---:|
| native Promise | 0 | 0 | 1¹ | 1¹ | 0 | 1 / 1 |
| form C thenable (today) | 2 | 2 | 2 (settled: 1) | 2 (settled: 1) | 1 | 3 / 3 |
| **Promise subclass (proposal)** | **1** | **2** | **1** | **1** | **0** | **2 / 3** |
| subclass via `of` | 3 | 2 | 3 (settled: 1) | 3 (settled: 1) | 2 | 4 / 3 |
| subclass, no species | 1 | 2 | 2 | 2 | 0 | 2 / 3 |
| `constructor === Promise` | 0 | 0 | 1 | 1 | 0 | 1 / 1 |

¹ native through `.then(r => r.match(h))` / `.then(r => r.unwrap())`.

Where the ticks come from:
- **`await` on the subclass.** `await` calls `PromiseResolve(%Promise%, call)`, which returns the value itself only if
  `call.constructor === Promise`. The subclass's constructor is `Call`, so `await` makes a new promise and resolves
  it with the call as a thenable.
  - If the call is still pending, the `NewPromiseResolveThenableJob` runs before the settle (it calls `call.then`),
    and only the reaction remains: **+1**.
  - If the call has already settled, both jobs follow the `await`: **+2**.
- **Form C** pays the same thenable job, plus the hop inside `invoke` (`.then(o => new Result(o))`): +2 when pending.
- **The direct subclass** has no inner hop, so it is one tick earlier than form C for pending calls, and never later.
- **`match`/`unwrap`** are `this.then(...)`: species gives a plain promise, and awaiting a plain promise adds
  nothing. So they cost one hop, as the native `.then` equivalent does.
- **The `ctor` variant** passes the `constructor === Promise` test and behaves exactly like a native promise.

### Order

Two calls settled in one task: X first, then an unrelated native promise N.

| Variant | X awaited | X `.match`/`.unwrap` | X `.then` | X in `Promise.all` |
|---|---|---|---|---|
| native Promise | X first | N first¹ | X first | N first |
| form C (today) | N first | N first | N first | N first |
| **Promise subclass (proposal)** | N first | N first | **X first** | N first |
| `constructor === Promise` | X first | N first¹ | X first | N first |

¹ native through `.then(r => r.match(h))`, consumed with `await`.

Two calls of the same variant, A then B, every pair of the five styles, pending and already settled (50 cases):

| Variant | order kept | same style reversed | cases that differ from native |
|---|---:|---|---:|
| native Promise | 38/50 | none | 0 |
| form C (today) | 34/50 | none | 12 |
| **Promise subclass (proposal)** | 34/50 | none | **12, the same 12 as form C** |
| subclass via `of` | 34/50 | none | 12 |
| subclass, no species | 36/50 | none | 6 |
| `constructor === Promise` | 38/50 | none | 0 |

Identical on Node, WebView2 and WebKitGTK.

### Heap

V8, bytes allocated per call on the timed path (median; the runs agreed within 1% except one Node sample):

| Variant | WebView2 a / b / c | Node a / b / c |
|---|---|---|
| native Promise | 260 / 336 / 336 | 695 / 704 / 704 |
| form C (today) | 472 / 512 / 515 | 1,144 / 1,181 / 1,128 |
| **Promise subclass (proposal)** | **432 / 356 / 336** | **1,056 / 888 / 848** |
| subclass via `of` | 768 / 692 / 674 | 1,765 / 1,552 / 1,513 |
| subclass, no species | 576 / 812 / 792 | 1,352 / 1,841 / 1,800 |
| `constructor === Promise` | 264 / 356 / 336 | 696 / 841 / 800 |

The native Node baseline's ~700 bytes per call, against 260 on WebView2, is mostly pointer size: Node is built without
pointer compression (control object: 40 bytes against 20).

Bytes retained per outstanding call, pending alone / with an awaiting consumer (a / b / c):

| Variant | WebView2 (heap after GC) | Node (heap after GC) | WebKitGTK (RSS, fresh process, no GC) |
|---|---|---|---|
| native Promise | 75 / 328 / 392 / 415 | 176 / 648 / 800 / 800 | 310 / 559 / 687 / 686 |
| form C (today) | 163 / 575 / 659 / 647 | 360 / 1,144 / 1,232 / 1,216 | 460 / 1,224 / 1,073 / 969 |
| **Promise subclass (proposal)** | **75 / 488 / 436 / 408** | **176 / 960 / 840 / 792** | **395 / 1,258 / 988 / 926** |
| subclass via `of` | 291 / 703 / 651 / 623 | 641 / 1,425 / 1,305 / 1,257 | 1,044 / 2,012 / 1,799 / 1,741 |
| subclass, no species | 75 / 583 / 771 / 743 | 176 / 1,160 / 1,552 / 1,504 | 393 / 1,242 / 1,681 / 1,623 |
| `constructor === Promise` | 75 / 343 / 435 / 408 | 176 / 648 / 840 / 792 | 386 / 612 / 982 / 932 |

The WebKitGTK readings ranged within ±4% of their median (3 fresh processes each).

### Burst: 10,000 outstanding calls with awaiting consumers, settled in one task

Time until every consumer continued, in ms (mean of 45 bursts). V8 also shows the heap with all calls outstanding:
peak before GC / retained after GC, in MB.

| Variant | WebView2 a / b / c ms | WebView2 peak a / b / c MB | Node a / b / c ms | WebKitGTK a / b / c ms |
|---|---|---|---|---|
| native Promise | 0.82 / 1.32 / 1.25 | 4.6 / 5.4 / 5.4 | 1.37 / 1.66 / 1.65 | 0.69 / 1.13 / 1.16 |
| form C (today) | 1.97 / 1.84 / 1.85 | 6.7 / 7.3 / 7.2 | 2.25 / 2.30 / 2.29 | 1.56 / 1.64 / 1.69 |
| **Promise subclass (proposal)** | **1.19 / 1.45 / 1.31** | **6.3 / 5.6 / 5.3** | **1.35 / 1.52 / 1.64** | **1.20 / 1.40 / 1.31** |
| subclass via `of` | 2.03 / 1.61 / 1.54 | 9.7 / 8.9 / 8.7 | 2.29 / 1.92 / 1.85 | 2.18 / 2.47 / 2.42 |
| subclass, no species | 1.30 / 1.26 / 1.34 | 7.8 / 10.1 / 9.8 | 1.64 / 2.26 / 2.39 | 1.33 / 2.09 / 1.91 |
| `constructor === Promise` | 0.75 / 1.28 / 1.36 | 4.6 / 5.6 / 5.3 | 0.97 / 1.51 / 1.59 | 0.64 / 1.29 / 1.18 |

Retained after GC on WebView2: native 3.1 / 3.8 / 3.8 MB, form C 5.4 / 6.3 / 6.2, proposal 4.6 / 4.0 / 3.8.

In this realistic order (the app awaits a pending call; the settle comes in a later task), the proposal settles
10,000 calls in 1.2–1.5 ms on WebView2, or 120–145 ns per call. That is faster than form C (1.8–2.0 ms), and on JSC
too (1.2–1.4 against 1.6–1.7 ms), although the sequential loop above put the proposal behind form C on JSC.

### Parity

Identical in every run on all three engines. Proposal against form C:

| Check | form C (today) | Promise subclass (proposal) |
|---|---|---|
| `call instanceof Promise` | false | **true** |
| `Object.prototype.toString.call(call)` | `[object Object]` | `[object Promise]` |
| `call.then(...)`, `catch`, `finally` | `then` → plain Promise; no `catch`/`finally` | plain `Promise` each (species); the result has no `match` |
| `Promise.resolve(call) === call` | false | false (wrapped; this is the extra tick) |
| `await`, `Promise.all`, `race`, `allSettled` | Result / fulfilled | same |
| success / declared error / undeclared error / infrastructure failure via `match` | `ok:5` / `notFound:n1` / `failure:protocol/unknownErrorCase` / `failure:transport/disconnected` | same |
| same via `unwrap` | resolves 5 / rejects `SparError(notFound)` / `SparError(gone)` / `SparError(disconnected)` | same |
| `match` without `failure` on an infrastructure failure | resolves `undefined`, 1 interceptor call | same |
| abort before the result; already aborted signal | `match` → `failure:cancelled`, `unwrap` rejects `SparError(cancelled)`, `await` → Result, the late frame changes nothing | same |
| unhandled rejections: failure never consumed, only awaited, matched, `then` discarded, `finally` discarded, aborted unconsumed, `unwrap` caught | 0 each | 0 each |
| unhandled rejections: un-awaited failing `unwrap()`; un-awaited `match` with a throwing handler | 1 each | 1 each |
| `Call.resolve(1)` | no such method | returns a `Call` (inherited static; its `match` would fail on a non-`Result`) |

Consequence of the last row: the generated client should not expose the `Call` constructor as a value, or should not
document its inherited statics (`resolve`, `all`, `withResolvers`, …). Exporting it only as a type avoids the question.

## Not measured

- **A real bridge round trip** with the subclass. The transport here is a stand-in that settles synchronously. The
  comparison uses the 59–79 µs WebView2 figure from `spikes/bridge-encoding`.
- **A WebKitGTK bridge round trip**: none exists yet, so the JSC numbers are put against the WebView2 figure.
- **JSC allocation per call**: there is no page heap counter, so only retained RSS from fresh processes is reported.
  It is coarse and includes uncollected garbage.
- **First-call (unoptimised) cost**: parity runs on a cold engine but is not timed. Every timing is after warm-up.
- GC pause impact, devtools display and async stack traces.
- Streams and subscriptions: unchanged by the proposal.
- A transport that rejects the call: form C's contract says a call never rejects except through `unwrap`.
- Type-level behaviour, lint rules (`no-floating-promises`), React `use()`, TanStack Query, and bundle size. Some are
  covered in `spikes/frontend-frameworks` and `spikes/ts-results`.
- Playwright's `chrome-headless-shell` and Playwright WebKit: not needed, since the real WebView2 and WebKitGTK 6.0
  hosts ran.
- The native-alone control (subclasses not loaded) ran only on Node, not on WebView2 or JSC.

## Pitfalls found

These are for `docs/pitfalls.md`, which this spike did not edit:
- **`performance.memory` returns a new snapshot object on every read.** Caching `const m = performance.memory` and
  reading `m.usedJSHeapSize` later always gives the same number. The first WebView2 run measured 0 bytes everywhere
  because of this.
- **`performance.now()` resolution:**
  - WebKitGTK (`file://` page): 1 ms;
  - Chromium/WebView2: 100 µs unless the page is cross-origin isolated (COOP `same-origin` + COEP `require-corp`),
    then 5 µs.
  Short intervals need many repetitions and a mean, or isolation.
- **WebKitGTK caps `navigator.hardwareConcurrency` at 8** (here: 32 CPUs in WSL).
- **The `v1`/`v2` builds of `spikes/desktop-origin` predate `--extra-filter`**; only the `bin/dev` build has it.
- **Windows environment variables are not passed into `wsl -d … -- bash script`** (for example `RUNS=1`). Pass
  arguments instead.
