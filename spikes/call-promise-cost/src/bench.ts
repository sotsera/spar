// Engine-neutral benchmark and parity checks. node-main.ts and page-main.ts supply the Host and print or post the report.
import { Result, SparError } from "./forms/c-call/sdk.js";
import { configureSpar, type CallError, type Outcome } from "./forms/core.js";
import { invokeFormC, invokeNative, invokeNoSpecies, invokeOf, invokePromiseCtor, invokeSubclass, variants, type Variant } from "./variants.js";
import { deliver, drop, okOutcome, pending, reserve } from "./wire.js";

export interface Host {
  readonly engine: string;
  readonly details: Record<string, unknown>;
  now(): number;
  /** Resolves in a later macrotask, after the current microtask queue has drained. */
  nextTask(): Promise<void>;
  /** Waits long enough for unhandled-rejection events to be dispatched. */
  settleEvents(): Promise<void>;
  /** V8 only: bytes in use on the JS heap (includes the young generation). */
  readonly heapUsed?: (() => number) | undefined;
  readonly gc?: (() => void) | undefined;
  /** Hosts without a JS heap counter: resident memory of the page's process, in bytes. */
  readonly rss?: (() => Promise<number>) | undefined;
  onUnhandledRejection(listener: () => void): void;
  log(line: string): void;
}

export interface Options {
  readonly sampleMs: number;
  readonly warmupRounds: number;
  readonly rounds: number;
  readonly burstSize: number;
  readonly burstRounds: number;
  readonly retainedCount: number;
}

const NAME = "bench.echo";
const OK = okOutcome(1);
const H = { success: (value: number) => value };
let sink = 0;

// ------------------------------------------------------------------ timed paths (one function per pair, monomorphic)
// Each iteration: create the call, let the stand-in transport settle it, consume it. The await is the app's.

async function nativeAwait(n: number) { let s = 0; for (let i = 0; i < n; i++) { const c = invokeNative<number>(NAME, undefined); deliver(OK); s += (await c).match(H) ?? 0; } return s; }
async function nativeMatch(n: number) { let s = 0; for (let i = 0; i < n; i++) { const c = invokeNative<number>(NAME, undefined); deliver(OK); s += await c.then(r => r.match(H)) ?? 0; } return s; }
async function nativeUnwrap(n: number) { let s = 0; for (let i = 0; i < n; i++) { const c = invokeNative<number>(NAME, undefined); deliver(OK); s += await c.then(r => r.unwrap()); } return s; }

async function formCAwait(n: number) { let s = 0; for (let i = 0; i < n; i++) { const c = invokeFormC<number>(NAME, undefined); deliver(OK); s += (await c).match(H) ?? 0; } return s; }
async function formCMatch(n: number) { let s = 0; for (let i = 0; i < n; i++) { const c = invokeFormC<number>(NAME, undefined); deliver(OK); s += await c.match(H) ?? 0; } return s; }
async function formCUnwrap(n: number) { let s = 0; for (let i = 0; i < n; i++) { const c = invokeFormC<number>(NAME, undefined); deliver(OK); s += await c.unwrap(); } return s; }

async function subclassAwait(n: number) { let s = 0; for (let i = 0; i < n; i++) { const c = invokeSubclass<number>(NAME, undefined); deliver(OK); s += (await c).match(H) ?? 0; } return s; }
async function subclassMatch(n: number) { let s = 0; for (let i = 0; i < n; i++) { const c = invokeSubclass<number>(NAME, undefined); deliver(OK); s += await c.match(H) ?? 0; } return s; }
async function subclassUnwrap(n: number) { let s = 0; for (let i = 0; i < n; i++) { const c = invokeSubclass<number>(NAME, undefined); deliver(OK); s += await c.unwrap(); } return s; }

async function ofAwait(n: number) { let s = 0; for (let i = 0; i < n; i++) { const c = invokeOf<number>(NAME, undefined); deliver(OK); s += (await c).match(H) ?? 0; } return s; }
async function ofMatch(n: number) { let s = 0; for (let i = 0; i < n; i++) { const c = invokeOf<number>(NAME, undefined); deliver(OK); s += await c.match(H) ?? 0; } return s; }
async function ofUnwrap(n: number) { let s = 0; for (let i = 0; i < n; i++) { const c = invokeOf<number>(NAME, undefined); deliver(OK); s += await c.unwrap(); } return s; }

async function noSpeciesAwait(n: number) { let s = 0; for (let i = 0; i < n; i++) { const c = invokeNoSpecies<number>(NAME, undefined); deliver(OK); s += (await c).match(H) ?? 0; } return s; }
async function noSpeciesMatch(n: number) { let s = 0; for (let i = 0; i < n; i++) { const c = invokeNoSpecies<number>(NAME, undefined); deliver(OK); s += await c.match(H) ?? 0; } return s; }
async function noSpeciesUnwrap(n: number) { let s = 0; for (let i = 0; i < n; i++) { const c = invokeNoSpecies<number>(NAME, undefined); deliver(OK); s += await c.unwrap(); } return s; }

async function ctorAwait(n: number) { let s = 0; for (let i = 0; i < n; i++) { const c = invokePromiseCtor<number>(NAME, undefined); deliver(OK); s += (await c).match(H) ?? 0; } return s; }
async function ctorMatch(n: number) { let s = 0; for (let i = 0; i < n; i++) { const c = invokePromiseCtor<number>(NAME, undefined); deliver(OK); s += await c.match(H) ?? 0; } return s; }
async function ctorUnwrap(n: number) { let s = 0; for (let i = 0; i < n; i++) { const c = invokePromiseCtor<number>(NAME, undefined); deliver(OK); s += await c.unwrap(); } return s; }

type Style = "await" | "match" | "unwrap";
const styles: readonly Style[] = ["await", "match", "unwrap"];
const timed: Record<Variant["key"], Record<Style, (n: number) => Promise<number>>> = {
  native: { await: nativeAwait, match: nativeMatch, unwrap: nativeUnwrap },
  formC: { await: formCAwait, match: formCMatch, unwrap: formCUnwrap },
  subclass: { await: subclassAwait, match: subclassMatch, unwrap: subclassUnwrap },
  of: { await: ofAwait, match: ofMatch, unwrap: ofUnwrap },
  noSpecies: { await: noSpeciesAwait, match: noSpeciesMatch, unwrap: noSpeciesUnwrap },
  ctor: { await: ctorAwait, match: ctorMatch, unwrap: ctorUnwrap },
};

// ------------------------------------------------------------------ generic consumers (ticks, order, burst, heap)

/* eslint-disable @typescript-eslint/no-explicit-any */
type AnyCall = any;
type Consumer = (call: AnyCall, mark: () => void) => PromiseLike<unknown>;
type TickStyle = Style | "then" | "all";

function consumer(variant: Variant, style: TickStyle): Consumer {
  switch (style) {
    case "await": return async (call, mark) => { const r = await call; mark(); return r.match(H); };
    case "match": return variant.methods
      ? async (call, mark) => { const x = await call.match(H); mark(); return x; }
      : async (call, mark) => { const x = await call.then((r: Result<number>) => r.match(H)); mark(); return x; };
    case "unwrap": return variant.methods
      ? async (call, mark) => { const x = await call.unwrap(); mark(); return x; }
      : async (call, mark) => { const x = await call.then((r: Result<number>) => r.unwrap()); mark(); return x; };
    case "then": return (call, mark) => call.then((r: unknown) => { mark(); return r; });
    case "all": return async (call, mark) => { const [r] = await Promise.all([call]); mark(); return r; };
  }
}

const native = variants[0]!;

/**
 * Microtask ticks between the transport settling the call and the consumer's code after the await running.
 * 0 = it ran before any microtask queued after the settle; each extra job hop adds 1.
 * `settled`: the call was already settled (in an earlier task) when the consumer started awaiting; ticks then count
 * from the start of the await.
 */
async function ticks(host: Host, variant: Variant, style: TickStyle, settled: boolean): Promise<number> {
  let tick = 0;
  let seen = -1;
  const step = () => { if (seen < 0 && tick < 64) { tick++; queueMicrotask(step); } };
  const call = variant.invoke(NAME, undefined);
  let done: PromiseLike<unknown>;
  if (settled) {
    deliver(OK);
    await host.nextTask();
    done = consumer(variant, style)(call, () => { seen = tick; });
    queueMicrotask(step);
  } else {
    done = consumer(variant, style)(call, () => { seen = tick; });
    await host.nextTask();
    deliver(OK);
    queueMicrotask(step);
  }

  await done;
  await host.nextTask();
  return seen;
}

/** Two calls settled in one task, X first then a native promise. Who continues first? */
async function order(host: Host, variant: Variant, style: TickStyle, nativeStyle: TickStyle): Promise<string> {
  const log: string[] = [];
  const x = variant.invoke(NAME, undefined);
  const n = native.invoke(NAME, undefined);
  const a = consumer(variant, style)(x, () => log.push("X"));
  const b = consumer(native, nativeStyle)(n, () => log.push("N"));
  await host.nextTask();
  deliver(OK);
  deliver(OK);
  await Promise.all([a, b]);
  return log.join("");
}

/** Two calls of the same variant settled in one task, A then B, consumed with styles sa and sb. "AB" = order kept. */
async function orderSame(host: Host, variant: Variant, sa: TickStyle, sb: TickStyle, settled: boolean): Promise<string> {
  const log: string[] = [];
  const a = variant.invoke(NAME, undefined);
  const b = variant.invoke(NAME, undefined);
  if (settled) {
    deliver(OK);
    deliver(OK);
    await host.nextTask();
  }

  const ca = consumer(variant, sa)(a, () => log.push("A"));
  const cb = consumer(variant, sb)(b, () => log.push("B"));
  if (!settled) {
    await host.nextTask();
    deliver(OK);
    deliver(OK);
  }

  await Promise.all([ca, cb]);
  return log.join("");
}

/** Added after the main runs: settle order between two calls of the same variant, for every pair of styles. */
export async function runOrderChecks(host: Host): Promise<Record<string, unknown>> {
  const tickStyles: readonly TickStyle[] = ["await", "match", "unwrap", "then", "all"];
  const result: Record<string, unknown> = { engine: host.engine, details: host.details, mode: "order" };
  for (const v of variants) {
    const row: Record<string, string> = {};
    for (const settled of [false, true]) {
      for (const sa of tickStyles) for (const sb of tickStyles) row[`${settled ? "settled" : "pending"}.${sa}/${sb}`] = await orderSame(host, v, sa, sb, settled);
    }

    result[v.key] = row;
  }

  return result;
}

// ------------------------------------------------------------------ heap

function median(values: readonly number[]): number {
  const sorted = [...values].sort((x, y) => x - y);
  const mid = sorted.length >> 1;
  return sorted.length % 2 ? sorted[mid]! : (sorted[mid - 1]! + sorted[mid]!) / 2;
}

/**
 * V8: bytes allocated per call on the timed path. The heap counter is read around two batch sizes (small enough that
 * no GC runs inside) and differenced, which removes the constant cost of the measurement itself.
 */
const SMALL = 128;
const LARGE = 384;

async function heapDelta(host: Host, run: (n: number) => Promise<number> | number, batch: number): Promise<number> {
  host.gc?.();
  const before = host.heapUsed!();
  sink += await run(batch);
  return host.heapUsed!() - before;
}

async function allocatedPerCall(host: Host, run: (n: number) => Promise<number> | number, repeats: number): Promise<{ median: number; min: number; max: number }> {
  const samples: number[] = [];
  sink += await run(LARGE);
  for (let i = 0; i < repeats; i++) {
    const small = await heapDelta(host, run, SMALL);
    const large = await heapDelta(host, run, LARGE);
    samples.push((large - small) / (LARGE - SMALL));
  }

  return { median: median(samples), min: Math.min(...samples), max: Math.max(...samples) };
}

const keep: object[] = new Array(LARGE);
/** Control for the method: one {a, b} literal is 20 bytes with pointer compression (Chromium), 40 without (Node). */
function controlObjects(n: number): number {
  for (let j = 0; j < n; j++) keep[j] = { a: j, b: j };
  return keep.length;
}

/** Bytes retained per pending call (and per pending call with an awaiting consumer). V8: heap after GC; JSC: RSS. */
async function retained(host: Host, variant: Variant, style: Style | "none", count: number): Promise<{ bytesPerCall: number; source: string; peakBytesPerCall?: number }> {
  const measure = host.heapUsed ? async () => { host.gc?.(); return host.heapUsed!(); } : host.rss!;
  drop();
  reserve(count);
  await host.nextTask();
  const before = await measure();
  const calls: unknown[] = new Array(count);
  const consumers: unknown[] = new Array(count);
  const consume = style === "none" ? undefined : consumer(variant, style);
  for (let i = 0; i < count; i++) {
    const call = variant.invoke(NAME, undefined);
    calls[i] = call;
    if (consume) consumers[i] = consume(call, () => {});
  }

  await host.nextTask();
  const peak = host.heapUsed ? host.heapUsed() : undefined;
  const after = await measure();
  const result: { bytesPerCall: number; source: string; peakBytesPerCall?: number } = {
    bytesPerCall: (after - before) / count,
    source: host.heapUsed ? "heap after gc" : "process RSS",
  };
  if (peak !== undefined) result.peakBytesPerCall = (peak - before) / count;
  for (let i = 0; i < count; i++) deliver(OK);
  if (consume) await Promise.all(consumers);
  calls.length = 0;
  drop();
  return result;
}

/** 10,000 outstanding calls with awaiting consumers, settled in one task. Heap growth on V8 hosts only. */
async function burst(host: Host, variant: Variant, style: Style, size: number): Promise<{ ms: number; peakBytes?: number; retainedBytes?: number }> {
  const consume = consumer(variant, style);
  reserve(size);
  host.gc?.();
  const heapBefore = host.heapUsed?.();
  let done = 0;
  let finish!: () => void;
  const all = new Promise<void>(resolve => { finish = resolve; });
  const mark = () => { if (++done === size) finish(); };
  for (let i = 0; i < size; i++) consume(variant.invoke(NAME, undefined), mark);
  await host.nextTask();
  const out: { ms: number; peakBytes?: number; retainedBytes?: number } = { ms: 0 };
  if (heapBefore !== undefined) {
    out.peakBytes = host.heapUsed!() - heapBefore;
    host.gc?.();
    out.retainedBytes = host.heapUsed!() - heapBefore;
  }

  const start = host.now();
  for (let i = 0; i < size; i++) deliver(OK);
  await all;
  out.ms = host.now() - start;
  if (pending() !== 0) throw new Error("wire not empty after burst");
  return out;
}

// ------------------------------------------------------------------ parity

interface Errors { readonly notFound: { readonly id: string } }
const handlersE = {
  success: (value: number) => `ok:${value}`,
  errors: { notFound: (e: { readonly id: string }) => `notFound:${e.id}` },
  failure: (e: CallError) => `failure:${e.kind}/${e.code}`,
};
const handlersNoFailure = { success: (value: number) => `ok:${value}`, errors: { notFound: (e: { readonly id: string }) => `notFound:${e.id}` } };
const declared: Outcome = { ok: false, call: NAME, code: "notFound", payload: { id: "n1" } };
const undeclared: Outcome = { ok: false, call: NAME, code: "gone", payload: {} };
const infra: Outcome = { ok: false, call: NAME, error: { kind: "transport", code: "disconnected", message: "host gone", execution: "unknown", call: NAME } };

function kindOf(value: unknown): string {
  if (value === undefined) return "undefined";
  if (value instanceof Result) return "Result";
  if (value instanceof Promise) return value.constructor === Promise ? "Promise" : `${(value.constructor as { name: string }).name} (Promise subclass)`;
  return typeof value === "object" && value !== null ? (value.constructor as { name: string }).name : String(value);
}

async function settledAs(value: PromiseLike<unknown>): Promise<string> {
  try {
    return `resolved ${String(await value)}`;
  } catch (error) {
    return error instanceof SparError ? `rejected SparError(${String(error.error.code)})` : `rejected ${String(error)}`;
  }
}

async function parity(host: Host, variant: Variant, unhandled: { count: number }, interceptor: string[]): Promise<Record<string, unknown>> {
  const r: Record<string, unknown> = {};
  const make = (signal?: AbortSignal): AnyCall => variant.invoke(NAME, undefined, signal ? { signal } : undefined);
  const match = (c: AnyCall, h: object) => variant.methods ? c.match(h) : c.then((x: Result<number, Errors>) => x.match(h as never));
  const unwrap = (c: AnyCall) => variant.methods ? c.unwrap() : c.then((x: Result<number, Errors>) => x.unwrap());

  // Shape.
  {
    const c = make();
    r.instanceofPromise = c instanceof Promise;
    r.toStringTag = Object.prototype.toString.call(c);
    r.constructorIs = kindOf(c);
    const t = c.then((x: unknown) => x);
    r.thenReturns = kindOf(t);
    r.thenResultHasMatch = typeof t.match === "function";
    r.finallyReturns = typeof c.finally === "function" ? kindOf(c.finally(() => {})) : "no finally";
    r.catchReturns = typeof c.catch === "function" ? kindOf(c.catch(() => {})) : "no catch";
    r.promiseResolveIsSame = Promise.resolve(c) === c;
    const all = Promise.all([c]);
    const race = Promise.race([c]);
    const settledAll = Promise.allSettled([c]);
    deliver(okOutcome(7));
    r.awaitGives = kindOf(await c);
    r.thenGives = kindOf(await t);
    r.allGives = kindOf((await all)[0]);
    r.raceGives = kindOf(await race);
    r.allSettledGives = (await settledAll)[0]!.status;
    const ctor = c.constructor as { resolve?: (v: unknown) => unknown };
    r.staticResolveGives = ctor === Promise ? "Promise" : typeof ctor.resolve === "function" ? kindOf(ctor.resolve(1)) : "no static resolve";
  }

  // Outcomes through match, unwrap and await.
  const outcomes: [string, Outcome][] = [["success", okOutcome(5)], ["declaredError", declared], ["undeclaredError", undeclared], ["infrastructureFailure", infra]];
  for (const [name, outcome] of outcomes) {
    const c = make();
    deliver(outcome);
    r[`${name}.match`] = await settledAs(match(c, handlersE));
    r[`${name}.unwrap`] = await settledAs(unwrap(c));
    r[`${name}.await`] = kindOf(await c);
  }

  // Without a failure handler, infrastructure failures go to the interceptor and match yields undefined.
  {
    const before = interceptor.length;
    const c = make();
    deliver(infra);
    r["infrastructureFailure.matchWithoutFailureHandler"] = await settledAs(match(c, handlersNoFailure));
    r["infrastructureFailure.interceptorCalls"] = interceptor.length - before;
  }

  // Abort: before the result frame, and with an already aborted signal. The late frame must change nothing.
  for (const when of ["abortBeforeResult", "abortedBeforeCall"] as const) {
    const controller = new AbortController();
    if (when === "abortedBeforeCall") controller.abort();
    const c = make(controller.signal);
    if (when === "abortBeforeResult") controller.abort();
    deliver(okOutcome(9));
    r[`${when}.match`] = await settledAs(match(c, handlersE));
    r[`${when}.unwrap`] = await settledAs(unwrap(c));
    r[`${when}.await`] = kindOf(await c);
  }

  // Unhandled rejections, per scenario.
  const scenarios: [string, () => Promise<void> | void][] = [
    ["failureNeverConsumed", () => { make(); deliver(declared); }],
    ["failureAwaitedOnly", async () => { const c = make(); deliver(declared); await c; }],
    ["failureMatched", async () => { const c = make(); deliver(declared); await match(c, handlersE); }],
    ["failureThenDiscarded", () => { const c = make(); deliver(declared); void c.then((x: unknown) => x); }],
    ["successFinallyDiscarded", () => { const c = make(); deliver(OK); if (typeof c.finally === "function") void c.finally(() => {}); }],
    ["abortedNeverConsumed", () => { const controller = new AbortController(); make(controller.signal); controller.abort(); deliver(OK); }],
    ["unwrapFailureCaught", async () => { const c = make(); deliver(declared); try { await unwrap(c); } catch { /* expected */ } }],
    ["unwrapFailureNotHandled (expected 1)", () => { const c = make(); deliver(declared); void unwrap(c); }],
    ["throwingHandlerNotHandled (expected 1)", () => { const c = make(); deliver(OK); void match(c, { ...handlersE, success: () => { throw new Error("handler"); } }); }],
  ];
  for (const [name, run] of scenarios) {
    const before = unhandled.count;
    await run();
    await host.settleEvents();
    r[`unhandled.${name}`] = unhandled.count - before;
  }

  if (pending() !== 0) throw new Error(`wire not empty after parity of ${variant.key}`);
  return r;
}

// ------------------------------------------------------------------ memory-only page (fresh process; JSC has no heap counter)

/**
 * One measurement in a freshly started page: process RSS (or the V8 heap) before and after creating `count` pending
 * calls of one variant, optionally each with an awaiting consumer. "control" allocates {a, b} literals instead.
 */
export async function runMemory(host: Host, variantKey: string, style: string, count: number): Promise<Record<string, unknown>> {
  const variant = variants.find(v => v.key === variantKey);
  if (!variant && style !== "control") throw new Error(`unknown variant ${variantKey}`);
  const measure = host.heapUsed ? async () => { host.gc?.(); return host.heapUsed!(); } : host.rss!;
  reserve(count);
  const keepCalls: unknown[] = new Array(count);
  const keepConsumers: unknown[] = new Array(count);
  await host.nextTask();
  const before = await measure();
  if (style === "control") {
    for (let i = 0; i < count; i++) keepCalls[i] = { a: i, b: i };
  } else {
    const consume = style === "none" ? undefined : consumer(variant!, style as Style);
    for (let i = 0; i < count; i++) {
      const call = variant!.invoke(NAME, undefined);
      keepCalls[i] = call;
      if (consume) keepConsumers[i] = consume(call, () => {});
    }
  }

  await host.nextTask();
  const after = await measure();
  const result = { engine: host.engine, details: host.details, mode: "memory", variant: variantKey, style, count, before, after, bytesPerCall: (after - before) / count, source: host.heapUsed ? "heap after gc" : "process RSS" };
  sink += keepCalls.length + keepConsumers.length;
  return result;
}

// ------------------------------------------------------------------ driver

export async function runAll(host: Host, options: Options): Promise<Record<string, unknown>> {
  const started = host.now();
  const report: Record<string, unknown> = { engine: host.engine, details: host.details, options, variants: variants.map(v => ({ key: v.key, label: v.label })) };
  const unhandled = { count: 0 };
  host.onUnhandledRejection(() => { unhandled.count++; });
  const interceptor: string[] = [];
  configureSpar({ onFailure: error => { interceptor.push(error.code); } });

  // Parity first, on a cold engine (behaviour does not depend on optimisation).
  const parityReport: Record<string, unknown> = {};
  for (const v of variants) parityReport[v.key] = await parity(host, v, unhandled, interceptor);
  report.parity = parityReport;
  host.log("parity done");

  // Ticks and order.
  const tickStyles: readonly TickStyle[] = ["await", "match", "unwrap", "then", "all"];
  const ticksReport: Record<string, Record<string, number>> = {};
  for (const v of variants) {
    const row: Record<string, number> = {};
    for (const s of tickStyles) {
      row[`pending.${s}`] = await ticks(host, v, s, false);
      row[`settled.${s}`] = await ticks(host, v, s, true);
    }

    ticksReport[v.key] = row;
  }

  report.ticks = ticksReport;
  const orderReport: Record<string, Record<string, string>> = {};
  for (const v of variants) {
    const row: Record<string, string> = {};
    for (const s of tickStyles) for (const ns of ["await", "then"] as const) row[`${s} vs native ${ns}`] = await order(host, v, s, ns);
    orderReport[v.key] = row;
  }

  report.order = orderReport;
  host.log("ticks done");

  // Timing: calibrate the batch on the baseline, then rounds with the pairs in rotating order.
  let n = 1024;
  for (;;) {
    const t0 = host.now();
    sink += await nativeAwait(n);
    if (host.now() - t0 >= options.sampleMs || n >= 1 << 21) break;
    n *= 2;
  }

  const pairs = variants.flatMap(v => styles.map(s => ({ key: `${v.key}.${s}`, run: timed[v.key][s] })));
  const samples: Record<string, number[]> = Object.fromEntries(pairs.map(p => [p.key, []]));
  for (let round = 0; round < options.warmupRounds + options.rounds; round++) {
    for (let k = 0; k < pairs.length; k++) {
      const pair = pairs[(k + round) % pairs.length]!;
      await host.nextTask();
      const t0 = host.now();
      sink += await pair.run(n);
      const ns = (host.now() - t0) * 1e6 / n;
      if (round >= options.warmupRounds) samples[pair.key]!.push(ns);
    }
  }

  report.timing = { callsPerSample: n, samples };
  host.log(`timing done (n=${n})`);

  // Heap.
  const heap: Record<string, unknown> = {};
  if (host.heapUsed) {
    heap.controlObjectBytes = await allocatedPerCall(host, controlObjects, 15);
    const allocated: Record<string, unknown> = {};
    for (const p of pairs) allocated[p.key] = await allocatedPerCall(host, p.run, 15);
    heap.allocatedPerCall = allocated;
  }

  if (host.heapUsed) { // JSC: see runMemory (fresh page per reading); RSS after the timing phase measures nothing
    const kept: Record<string, unknown> = {};
    for (const v of variants) {
      for (const s of ["none", ...styles] as const) {
        const runs = [];
        for (let i = 0; i < 3; i++) runs.push(await retained(host, v, s, options.retainedCount));
        kept[`${v.key}.${s}`] = runs;
      }
    }

    heap.retained = kept;
  }

  report.heap = heap;
  host.log("heap done");

  // Burst.
  const burstReport: Record<string, unknown[]> = {};
  for (let round = 0; round < options.burstRounds + 1; round++) { // round 0 is warm-up
    for (const v of variants) {
      for (const s of styles) {
        const result = await burst(host, v, s, options.burstSize);
        if (round > 0) (burstReport[`${v.key}.${s}`] ??= []).push(result);
      }
    }
  }

  report.burst = { size: options.burstSize, runs: burstReport };
  report.elapsedMs = host.now() - started;
  report.sink = sink > 0 ? "+" : "-";
  host.log("burst done");
  return report;
}
