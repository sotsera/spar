// Engine-neutral benchmark. node-main.ts and page-main.ts supply the Host and print or post the report.
import { SparError } from "./sdk/c-call/sdk.js";
import { combos, type Acc, type Combo, type Pace } from "./consumers.js";
import { batchText, decodeBatch } from "./rows.js";
import { open } from "./variants.js";
import { expectedSum, pool, type Feed } from "./wire.js";

export interface Host {
  readonly engine: string;
  readonly details: Record<string, unknown>;
  now(): number;
  /** Resolves in a later macrotask (a MessageChannel message), after the microtask queue has drained. */
  nextTask(): Promise<void>;
  /** setTimeout(ms). */
  timer(ms: number): Promise<void>;
  /** V8 only: bytes in use on the JS heap. */
  readonly heapUsed?: (() => number) | undefined;
  readonly gc?: (() => void) | undefined;
  /** Hosts without a heap counter: resident memory and CPU time of the page's process. */
  readonly proc?: (() => Promise<{ readonly rss: number; readonly cpuMs: number }>) | undefined;
  /** Timeline entries seen so far (Node: "gc"; Chromium: "longtask"), with performance.now() timestamps. */
  readonly entries: () => readonly { readonly type: string; readonly start: number; readonly duration: number }[];
  log(line: string): void;
}

export interface Options {
  /** Measured rounds per scenario; warm-up rounds come first. */
  readonly rounds100k: number;
  readonly rounds1M: number;
  readonly roundsSlow: number;
  readonly pushSeconds: number;
  readonly pushRate: number;
  readonly retainedReps: number;
  readonly only?: readonly string[] | undefined;
}

interface Sample {
  readonly scenario: string;
  readonly combo: string;
  readonly round: number;
  readonly items: number;
  readonly ms: number;
  readonly nsPerItem: number;
  readonly longestTaskMs: number;
  readonly busyMs: number;
  readonly tasks: number;
  readonly maxBuffered: number;
  readonly start: number;
  readonly end: number;
  peakHeapGrowth?: number;
  gcCount?: number;
  gcMs?: number;
  longTasks?: number;
  longTaskMaxMs?: number;
  cpuMs?: number;
  events?: number;
  ticks?: number;
}

const BATCH = 1000;
const WINDOW = 8;

function newAcc(): Acc {
  return { sum: 0, count: 0, failure: undefined };
}

interface RunSpec {
  readonly batches: number;
  readonly batchSize?: number;
  readonly window?: number;
  readonly fail?: boolean;
  readonly pace?: Pace;
  readonly measure: "drain" | "handler";
  readonly onDeliver?: (feed: Feed) => void;
}

async function runOnce(host: Host, combo: Combo, spec: RunSpec): Promise<{ ms: number; acc: Acc; feed: Feed; start: number; end: number }> {
  const batchSize = spec.batchSize ?? BATCH;
  const { feed, stream } = open(combo.kind, host, {
    batches: spec.batches, batchSize, window: spec.window ?? WINDOW, fail: spec.fail ?? false, measure: spec.measure, onDeliver: spec.onDeliver,
  });
  const acc = newAcc();
  const start = host.now();
  feed.start();
  await combo.consume(stream, spec.pace, acc);
  const end = host.now();
  const expected = spec.fail === true ? -1 : spec.batches * batchSize;
  if (expected >= 0 && (acc.count !== expected || acc.sum !== expectedSum(batchSize, spec.batches))) {
    throw new Error(`${combo.key}: got ${acc.count} items (sum ${acc.sum}), expected ${expected} (sum ${expectedSum(batchSize, spec.batches)})`);
  }
  if (expected >= 0 && acc.failure !== undefined) throw new Error(`${combo.key}: unexpected failure ${String(acc.failure)}`);
  return { ms: end - start, acc, feed, start, end };
}

function sample(scenario: string, combo: Combo, round: number, items: number, r: { ms: number; feed: Feed; start: number; end: number }): Sample {
  return {
    scenario, combo: combo.key, round, items, ms: r.ms, nsPerItem: (r.ms * 1e6) / items,
    longestTaskMs: r.feed.longest, busyMs: r.feed.busy, tasks: r.feed.tasks, maxBuffered: r.feed.maxBuffered, start: r.start, end: r.end,
  };
}

function rotate<T>(list: readonly T[], by: number): T[] {
  const k = by % list.length;
  return [...list.slice(k), ...list.slice(0, k)];
}

async function settle(host: Host): Promise<void> {
  host.gc?.();
  await host.nextTask();
  await host.nextTask();
}

// ------------------------------------------------------------------ functional check

async function checks(host: Host, list: readonly Combo[]): Promise<Record<string, unknown>[]> {
  const out: Record<string, unknown>[] = [];
  for (const combo of list) {
    // 2,500 items in 5 batches of 500, then the failure terminal.
    const failed = await runOnce(host, combo, { batches: 5, batchSize: 500, fail: true, measure: "drain" });
    const f = failed.acc.failure as unknown;
    const kind = f instanceof SparError ? `SparError(${String(f.error.code)})` : f !== undefined && f !== null && typeof f === "object" && "code" in f ? `CallError(${String((f as { code: unknown }).code)})` : String(f);
    const ok = failed.acc.count === 2500 && failed.acc.sum === expectedSum(500, 5) && (combo.throws ? f instanceof SparError : kind === "CallError(response-serialization)");
    const completed = await runOnce(host, combo, { batches: 5, batchSize: 500, fail: false, measure: "drain" });
    out.push({ combo: combo.key, failureItems: failed.acc.count, failure: kind, completedItems: completed.acc.count, completedFailure: String(completed.acc.failure), pass: ok && completed.acc.failure === undefined });
  }
  return out;
}

// ------------------------------------------------------------------ scenarios

async function bigLoop(host: Host, list: readonly Combo[], items: number, warmup: number, rounds: number, out: Sample[]): Promise<void> {
  const scenario = `big-${items >= 1_000_000 ? "1M" : `${items / 1000}k`}`;
  for (let round = -warmup; round < rounds; round++) {
    for (const combo of rotate(list, round + warmup)) {
      await settle(host);
      const r = await runOnce(host, combo, { batches: items / BATCH, measure: "drain" });
      if (round >= 0) out.push(sample(scenario, combo, round, items, r));
    }
    host.log(`${scenario} round ${round}`);
  }
}

async function slowLoop(host: Host, list: readonly Combo[], scenario: string, items: number, pace: Pace, measure: "drain" | "handler", warmup: number, rounds: number, out: Sample[]): Promise<void> {
  for (let round = -warmup; round < rounds; round++) {
    for (const combo of rotate(list, round + warmup)) {
      await settle(host);
      const base = host.heapUsed?.() ?? 0;
      let peak = base;
      const onDeliver = host.heapUsed !== undefined ? () => { const h = host.heapUsed!(); if (h > peak) peak = h; } : undefined;
      const r = await runOnce(host, combo, { batches: items / BATCH, pace, measure, ...(onDeliver ? { onDeliver } : {}) });
      if (round >= 0) {
        const s = sample(scenario, combo, round, items, r);
        if (host.heapUsed !== undefined) s.peakHeapGrowth = peak - base;
        out.push(s);
      }
    }
    host.log(`${scenario} round ${round}`);
  }
}

/** Bytes kept alive per buffered item while the app is stalled and the host has filled the credit window. */
async function retained(host: Host, list: readonly Combo[], window: number, reps: number, out: Record<string, unknown>[]): Promise<void> {
  pool(BATCH);
  for (let rep = 0; rep < reps; rep++) {
    for (const combo of rotate(list, rep)) {
      await settle(host);
      host.gc?.();
      const before = host.heapUsed?.();
      const procBefore = host.proc ? await host.proc() : undefined;
      const { feed, stream } = open(combo.kind, host, { batches: window + 4, batchSize: BATCH, window, fail: false, measure: "handler" });
      const iterator = stream[Symbol.asyncIterator]();
      feed.start();
      const first = await iterator.next();
      for (let i = 0; i < 10_000 && !feed.hostIdle; i++) await host.nextTask();
      host.gc?.();
      const after = host.heapUsed?.();
      const procAfter = host.proc ? await host.proc() : undefined;
      // Items kept alive: the buffer plus what the stalled app holds (one item, or one whole batch for the batch shape).
      const buffered = feed.buffered + (combo.kind === "batch" ? BATCH : 1);
      let n = 1;
      while (!(await iterator.next()).done) n++;
      out.push({
        combo: combo.key, rep, window, buffered, firstDone: first.done, drained: n,
        ...(before !== undefined && after !== undefined ? { heapBytes: after - before, bytesPerItem: (after - before) / buffered } : {}),
        ...(procBefore && procAfter ? { rssBytes: procAfter.rss - procBefore.rss, rssPerItem: (procAfter.rss - procBefore.rss) / buffered } : {}),
      });
    }
    host.log(`retained rep ${rep}`);
  }
}

/** Bytes allocated per item on a fast loop: the heap counter around 2 and 6 batches (no GC expected in between). */
async function allocation(host: Host, list: readonly Combo[], reps: number, out: Record<string, unknown>[]): Promise<void> {
  if (host.heapUsed === undefined) return;
  const measure = async (combo: Combo, batches: number) => {
    await settle(host);
    host.gc?.();
    const h0 = host.heapUsed!();
    const t0 = host.now();
    await runOnce(host, combo, { batches, measure: "drain" });
    const h1 = host.heapUsed!();
    return { bytes: h1 - h0, start: t0, end: host.now() };
  };
  for (const combo of list) await measure(combo, 2); // warm-up
  for (let rep = 0; rep < reps; rep++) {
    for (const combo of rotate(list, rep)) {
      const small = await measure(combo, 2);
      const large = await measure(combo, 6);
      out.push({ combo: combo.key, rep, small: small.bytes, large: large.bytes, bytesPerItem: (large.bytes - small.bytes) / (4 * BATCH), windows: [[small.start, small.end], [large.start, large.end]] });
    }
  }
}

/** Subscription-like push: `rate` events per second for `seconds`, two rounds in rotated order. A setTimeout(1) loop
 * delivers what is due as one batch per tick (the achieved tick rate is recorded; it depends on the engine). */
async function push(host: Host, list: readonly Combo[], rate: number, seconds: number, out: Sample[]): Promise<void> {
  const texts = new Map<number, string>();
  const text = (n: number) => { let t = texts.get(n); if (t === undefined) { t = batchText(0, n); texts.set(n, t); } return t; };
  for (let n = 1; n <= 400; n++) text(n);
  const once = async (combo: Combo, ms: number) => {
    const { feed, stream } = open(combo.kind, host, { batches: 0, batchSize: BATCH, window: WINDOW, fail: false, measure: "drain", push: true });
    const acc = newAcc();
    const done = combo.consume(stream, undefined, acc);
    const procBefore = host.proc ? await host.proc() : undefined;
    const start = host.now();
    let sent = 0, ticks = 0;
    for (;;) {
      await host.timer(1);
      const t = host.now() - start;
      if (t >= ms) break;
      const due = Math.min(Math.floor((t * rate) / 1000) - sent, 4000);
      if (due > 0) {
        feed.deliver(text(due));
        sent += due;
        ticks++;
      }
    }
    const end = host.now();
    const procAfter = host.proc ? await host.proc() : undefined;
    feed.finish(false);
    await done;
    if (acc.count !== sent) throw new Error(`${combo.key}: push delivered ${sent}, consumed ${acc.count}`);
    return { feed, start, end, sent, ticks, cpuMs: procBefore && procAfter ? procAfter.cpuMs - procBefore.cpuMs : undefined };
  };
  for (const combo of list) await once(combo, 300); // warm-up
  for (let round = 0; round < 2; round++) for (const combo of rotate(list, round * 6)) {
    await settle(host);
    const r = await once(combo, seconds * 1000);
    const s: Sample = {
      scenario: "push", combo: combo.key, round, items: r.sent, ms: r.end - r.start, nsPerItem: (r.feed.busy * 1e6) / r.sent,
      longestTaskMs: r.feed.longest, busyMs: r.feed.busy, tasks: r.feed.tasks, maxBuffered: r.feed.maxBuffered, start: r.start, end: r.end,
      events: r.sent, ticks: r.ticks,
    };
    if (r.cpuMs !== undefined) s.cpuMs = r.cpuMs;
    out.push(s);
    host.log(`push ${combo.key}`);
  }
}

function attachEntries(host: Host, samples: Sample[]): void {
  const entries = host.entries();
  for (const s of samples) {
    const inside = entries.filter(e => e.start >= s.start && e.start < s.end);
    const gcs = inside.filter(e => e.type === "gc");
    const longs = inside.filter(e => e.type === "longtask");
    if (gcs.length > 0 || host.engine === "node") { s.gcCount = gcs.length; s.gcMs = gcs.reduce((a, e) => a + e.duration, 0); }
    if (host.engine === "webview2") { s.longTasks = longs.length; s.longTaskMaxMs = longs.reduce((a, e) => Math.max(a, e.duration), 0); }
  }
}

function resolution(host: Host): number {
  let min = Infinity;
  for (let i = 0; i < 50 && min > 0.0005; i++) {
    const a = host.now();
    let b = host.now();
    while (b === a) b = host.now();
    if (b - a < min) min = b - a;
  }
  return min;
}

function pick(options: Options): readonly Combo[] {
  return options.only ? combos.filter(c => options.only!.includes(c.key)) : combos;
}

export async function runAll(host: Host, options: Options): Promise<Record<string, unknown>> {
  const list = pick(options);
  pool(BATCH);
  pool(500);
  const timerMs = await timerInterval(host);
  // A hidden WebKit page throttles DOM timers to about one per second after a short while (the first few are fast).
  const timersThrottled = timerMs > 50 || host.details["visibilityState"] === "hidden";
  const report: Record<string, unknown> = { engine: host.engine, details: { ...host.details, clockResolutionMs: resolution(host), setTimeout1Ms: timerMs, timersThrottled }, combos: list.map(c => ({ key: c.key, kind: c.kind, label: c.label })) };
  report["checks"] = await checks(host, list);
  report["decodeOnly"] = decodeOnly(host, 5);
  host.log("checks done");
  const samples: Sample[] = [];
  await bigLoop(host, list, 100_000, 2, options.rounds100k, samples);
  await bigLoop(host, list, 1_000_000, 1, options.rounds1M, samples);
  await slowLoop(host, list, "slow-microtask", 100_000, () => Promise.resolve(), "drain", 1, options.roundsSlow, samples);
  await slowLoop(host, list, "slow-task", 20_000, () => host.nextTask(), "handler", 1, options.roundsSlow, samples);
  const timerList = list.filter(c => ["plain", "resultMatch", "resultShared", "eagerMatch", "batch"].includes(c.key));
  let tick = 0;
  if (!timersThrottled) await slowLoop(host, timerList, "slow-timer", 12_000, () => (++tick % 100 === 0 ? host.timer(0) : Promise.resolve()), "handler", 0, 1, samples);
  const kept: Record<string, unknown>[] = [];
  await retained(host, list.filter(c => ["plain", "resultMatch", "leanMatch", "eagerMatch", "batch", "tail", "genPlain", "genResult"].includes(c.key)), WINDOW, options.retainedReps, kept);
  report["retained"] = kept;
  await pushBurst(host, list, samples);
  if (timersThrottled) report["skipped"] = ["slow-timer", "push (timer-driven)"];
  else await push(host, list, options.pushRate, options.pushSeconds, samples);
  await host.nextTask();
  await host.timer(50);
  attachEntries(host, samples);
  report["samples"] = samples;
  return report;
}

/** Separate process or page load, with a large young generation so no GC runs inside a measurement. */
export async function runAllocation(host: Host, reps: number, only?: readonly string[]): Promise<Record<string, unknown>> {
  const list = only ? combos.filter(c => only.includes(c.key)) : combos;
  pool(BATCH);
  const out: Record<string, unknown>[] = [];
  await allocation(host, list, reps, out);
  await host.nextTask();
  await host.timer(50);
  const entries = host.entries().filter(e => e.type === "gc");
  for (const o of out) (o as { gcInside?: number }).gcInside = (o["windows"] as number[][]).reduce((n, [a, b]) => n + entries.filter(e => e.start >= a! && e.start < b!).length, 0);
  return { engine: host.engine, details: host.details, mode: "alloc", alloc: out };
}

/** One retained-memory reading per fresh process (hosts without a heap counter: RSS). */
export async function runRetainedOnly(host: Host, comboKey: string, window: number): Promise<Record<string, unknown>> {
  const list = combos.filter(c => c.key === comboKey);
  const out: Record<string, unknown>[] = [];
  // Warm the code once on a small stream so the reading is not dominated by first-run compilation.
  await runOnce(host, list[0]!, { batches: 2, measure: "drain" });
  await retained(host, list, window, 1, out);
  return { engine: host.engine, details: host.details, mode: "retained", retained: out };
}

/** The page's decode alone (JSON.parse plus field-by-field rows), no iterator: 1M rows per sample, ns per row. */
function decodeOnly(host: Host, reps: number): number[] {
  const p = pool(BATCH);
  const out: number[] = [];
  let keep = 0;
  for (let rep = -1; rep < reps; rep++) {
    const t0 = host.now();
    for (let b = 0; b < 1000; b++) keep += decodeBatch(p.texts[b % p.texts.length]!).length;
    const ns = ((host.now() - t0) * 1e6) / 1_000_000;
    if (rep >= 0) out.push(ns);
  }
  if (keep === 0) throw new Error("unreachable");
  return out;
}

/** Mean interval of five chained setTimeout(1) calls. A hidden WebKit page throttles DOM timers (seconds each). */
async function timerInterval(host: Host): Promise<number> {
  const t0 = host.now();
  for (let i = 0; i < 5; i++) await host.timer(1);
  return (host.now() - t0) / 5;
}

/**
 * Push cost without timers: 60,000 events in batches of 100 (5 ms worth at 20,000/s), each batch its own task
 * (MessageChannel), back to back. Wall time per event, including the task hop, which is the same for every variant.
 * At 20,000 events/s the page then spends nsPerEvent × 20,000 ns per second.
 */
async function pushBurst(host: Host, list: readonly Combo[], out: Sample[]): Promise<void> {
  const text = batchText(0, 100);
  const events = 60_000;
  const once = async (combo: Combo, n: number) => {
    const { feed, stream } = open(combo.kind, host, { batches: 0, batchSize: BATCH, window: WINDOW, fail: false, measure: "drain", push: true });
    const acc = newAcc();
    const done = combo.consume(stream, undefined, acc);
    const start = host.now();
    for (let sent = 0; sent < n; sent += 100) {
      await host.nextTask();
      feed.deliver(text);
    }
    feed.finish(false);
    await done;
    const end = host.now();
    if (acc.count !== n) throw new Error(`${combo.key}: burst delivered ${n}, consumed ${acc.count}`);
    return { feed, start, end };
  };
  for (const combo of list) await once(combo, 6_000); // warm-up
  for (let round = 0; round < 3; round++) {
    for (const combo of rotate(list, round * 4)) {
      await settle(host);
      const r = await once(combo, events);
      out.push({
        scenario: "push-burst", combo: combo.key, round, items: events, ms: r.end - r.start, nsPerItem: ((r.end - r.start) * 1e6) / events,
        longestTaskMs: r.feed.longest, busyMs: r.feed.busy, tasks: r.feed.tasks, maxBuffered: r.feed.maxBuffered, start: r.start, end: r.end,
      });
    }
    host.log(`push-burst round ${round}`);
  }
}
