// What app code writes for each shape. One function per (shape, style), so every call site stays monomorphic.
// The per-item work is the same everywhere: add one field, count the item. `pace`, when given, is awaited after each
// item (the slow-consumer scenarios).
import type { CallError } from "./sdk/core.js";
import type { Result } from "./sdk/c-call/sdk.js";
import type { Row } from "./rows.js";
import type { Kind, LeanResult, TailStream } from "./variants.js";

export type Pace = (() => Promise<unknown>) | undefined;

export interface Acc {
  sum: number;
  count: number;
  failure: unknown;
}

type Consume = (stream: AsyncIterable<unknown>, pace: Pace, acc: Acc) => Promise<void>;

async function plainLoop(stream: AsyncIterable<unknown>, pace: Pace, acc: Acc): Promise<void> {
  let sum = 0, count = 0;
  try {
    for await (const row of stream as AsyncIterable<Row>) {
      sum += row.qty;
      count++;
      if (pace !== undefined) await pace();
    }
  } catch (error) {
    acc.failure = error;
  }
  acc.sum = sum;
  acc.count = count;
}

async function resultMatch(stream: AsyncIterable<unknown>, pace: Pace, acc: Acc): Promise<void> {
  let sum = 0, count = 0;
  for await (const item of stream as AsyncIterable<Result<Row>>) {
    item.match({
      success: row => { sum += row.qty; count++; },
      failure: error => { acc.failure = error; },
    });
    if (pace !== undefined) await pace();
  }
  acc.sum = sum;
  acc.count = count;
}

// A handler object made once. It cannot close over the loop's locals, so it writes to a shared accumulator.
const shared: Acc = { sum: 0, count: 0, failure: undefined };
const SHARED = Object.freeze({
  success: (row: Row) => { shared.sum += row.qty; shared.count++; },
  failure: (error: CallError) => { shared.failure = error; },
});

async function resultShared(stream: AsyncIterable<unknown>, pace: Pace, acc: Acc): Promise<void> {
  shared.sum = 0; shared.count = 0; shared.failure = undefined;
  for await (const item of stream as AsyncIterable<Result<Row>>) {
    item.match(SHARED);
    if (pace !== undefined) await pace();
  }
  acc.sum = shared.sum; acc.count = shared.count; acc.failure = shared.failure;
}

async function resultUnwrap(stream: AsyncIterable<unknown>, pace: Pace, acc: Acc): Promise<void> {
  let sum = 0, count = 0;
  try {
    for await (const item of stream as AsyncIterable<Result<Row>>) {
      const row = item.unwrap();
      sum += row.qty;
      count++;
      if (pace !== undefined) await pace();
    }
  } catch (error) {
    acc.failure = error;
  }
  acc.sum = sum;
  acc.count = count;
}

async function leanMatch(stream: AsyncIterable<unknown>, pace: Pace, acc: Acc): Promise<void> {
  let sum = 0, count = 0;
  for await (const item of stream as AsyncIterable<LeanResult<Row>>) {
    item.match({
      success: row => { sum += row.qty; count++; },
      failure: error => { acc.failure = error; },
    });
    if (pace !== undefined) await pace();
  }
  acc.sum = sum;
  acc.count = count;
}

const leanShared: Acc = { sum: 0, count: 0, failure: undefined };
const LEAN_SHARED = Object.freeze({
  success: (row: Row) => { leanShared.sum += row.qty; leanShared.count++; },
  failure: (error: CallError) => { leanShared.failure = error; },
});

async function leanSharedLoop(stream: AsyncIterable<unknown>, pace: Pace, acc: Acc): Promise<void> {
  leanShared.sum = 0; leanShared.count = 0; leanShared.failure = undefined;
  for await (const item of stream as AsyncIterable<LeanResult<Row>>) {
    item.match(LEAN_SHARED);
    if (pace !== undefined) await pace();
  }
  acc.sum = leanShared.sum; acc.count = leanShared.count; acc.failure = leanShared.failure;
}

async function leanUnwrap(stream: AsyncIterable<unknown>, pace: Pace, acc: Acc): Promise<void> {
  let sum = 0, count = 0;
  try {
    for await (const item of stream as AsyncIterable<LeanResult<Row>>) {
      const row = item.unwrap();
      sum += row.qty;
      count++;
      if (pace !== undefined) await pace();
    }
  } catch (error) {
    acc.failure = error;
  }
  acc.sum = sum;
  acc.count = count;
}

async function eagerMatch(stream: AsyncIterable<unknown>, pace: Pace, acc: Acc): Promise<void> {
  let sum = 0, count = 0;
  for await (const item of stream as AsyncIterable<Result<Row>>) {
    item.match({
      success: row => { sum += row.qty; count++; },
      failure: error => { acc.failure = error; },
    });
    if (pace !== undefined) await pace();
  }
  acc.sum = sum;
  acc.count = count;
}

const NO_ROWS: readonly Row[] = Object.freeze([]);

async function batchMatch(stream: AsyncIterable<unknown>, pace: Pace, acc: Acc): Promise<void> {
  let sum = 0, count = 0;
  for await (const batch of stream as AsyncIterable<Result<readonly Row[]>>) {
    const rows = batch.match({
      success: rows => rows,
      failure: error => { acc.failure = error; return NO_ROWS; },
    });
    for (let i = 0; i < rows.length; i++) {
      sum += rows[i]!.qty;
      count++;
      if (pace !== undefined) await pace();
    }
  }
  acc.sum = sum;
  acc.count = count;
}

async function tailLoop(stream: AsyncIterable<unknown>, pace: Pace, acc: Acc): Promise<void> {
  const s = stream as TailStream<Row>;
  let sum = 0, count = 0;
  for await (const row of s) {
    sum += row.qty;
    count++;
    if (pace !== undefined) await pace();
  }
  (await s.result).match({
    success: () => undefined,
    failure: error => { acc.failure = error; },
  });
  acc.sum = sum;
  acc.count = count;
}

async function genPlainLoop(stream: AsyncIterable<unknown>, pace: Pace, acc: Acc): Promise<void> {
  let sum = 0, count = 0;
  try {
    for await (const row of stream as AsyncIterable<Row>) {
      sum += row.qty;
      count++;
      if (pace !== undefined) await pace();
    }
  } catch (error) {
    acc.failure = error;
  }
  acc.sum = sum;
  acc.count = count;
}

async function genResultMatch(stream: AsyncIterable<unknown>, pace: Pace, acc: Acc): Promise<void> {
  let sum = 0, count = 0;
  for await (const item of stream as AsyncIterable<Result<Row>>) {
    item.match({
      success: row => { sum += row.qty; count++; },
      failure: error => { acc.failure = error; },
    });
    if (pace !== undefined) await pace();
  }
  acc.sum = sum;
  acc.count = count;
}

export interface Combo {
  readonly key: string;
  readonly kind: Kind;
  readonly label: string;
  readonly consume: Consume;
  /** true: the failure reaches the app as a thrown SparError; false: through a failure handler (CallError). */
  readonly throws: boolean;
}

export const combos: readonly Combo[] = [
  { key: "plain", kind: "plain", label: "plain items + throwing iterator (baseline)", consume: plainLoop, throws: true },
  { key: "resultMatch", kind: "result", label: "Result per item, match with a literal", consume: resultMatch, throws: false },
  { key: "resultShared", kind: "result", label: "Result per item, match with a shared frozen handler", consume: resultShared, throws: false },
  { key: "resultUnwrap", kind: "result", label: "Result per item, unwrap", consume: resultUnwrap, throws: true },
  { key: "leanMatch", kind: "lean", label: "lean Result per item, match with a literal", consume: leanMatch, throws: false },
  { key: "leanShared", kind: "lean", label: "lean Result per item, match with a shared handler", consume: leanSharedLoop, throws: false },
  { key: "leanUnwrap", kind: "lean", label: "lean Result per item, unwrap", consume: leanUnwrap, throws: true },
  { key: "eagerMatch", kind: "eager", label: "Result made at decode (buffer holds Results), match literal", consume: eagerMatch, throws: false },
  { key: "batch", kind: "batch", label: "exploratory: one Result per batch", consume: batchMatch, throws: false },
  { key: "tail", kind: "tail", label: "exploratory: plain items + stream.result", consume: tailLoop, throws: false },
  { key: "genPlain", kind: "genPlain", label: "prototype layering: async generator, plain + throw", consume: genPlainLoop, throws: true },
  { key: "genResult", kind: "genResult", label: "prototype layering: sdk stream(), Result per item", consume: genResultMatch, throws: false },
];
