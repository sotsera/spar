// Stand-in for the bridge: a simulated host that sends batches under credits, and the page-side feed that receives
// them. Every variant uses this same code; only the iterator on top (variants.ts) differs.
//
// - The host sends at most `window` batches ahead; the page grants credits back in halves as the app consumes
//   (plan 2c: window 8, batch 1,000). Each batch is one MessageChannel message, so it arrives as its own task, as a
//   bridge message does. The message carries a batch index; the page decodes the matching pre-built JSON text
//   (JSON.parse plus field-by-field construction), so the page pays the decode a real batch costs, and nothing
//   clones a string per message.
// - A terminal message follows the last batch: completed, or a failure after the items sent so far.
// - Instrumentation: `drain` mode times each batch task from the message to the moment the iterator finds the buffer
//   empty (decode plus the app's loop over the batch, when the app keeps up); `handler` mode times only the message
//   handler (for apps that await between items, whose loop runs in later tasks).
import type { CallError, Outcome } from "./sdk/core.js";
import { batchText, decodeBatch } from "./rows.js";

export const EMPTY: unique symbol = Symbol("empty");
const END = -1;
const FAIL = -2;
const POOL = 8;

export interface Clock {
  now(): number;
}

export const failureError: CallError = {
  kind: "internal",
  code: "response-serialization",
  message: "stream stopped after the items sent",
  execution: "completed",
  call: "rows.read",
};

export const failureOutcome: Outcome = { ok: false, call: "rows.read", error: failureError };

interface Pool {
  readonly size: number;
  readonly texts: readonly string[];
  readonly qty: readonly number[];
}

const pools = new Map<number, Pool>();

/** Pre-built batch texts for one batch size, made before any measurement. */
export function pool(size: number): Pool {
  let p = pools.get(size);
  if (p === undefined) {
    const texts: string[] = [];
    const qty: number[] = [];
    for (let k = 0; k < POOL; k++) {
      texts.push(batchText(k * size, size));
      let sum = 0;
      for (let i = k * size; i < (k + 1) * size; i++) sum += i % 17;
      qty.push(sum);
    }
    p = { size, texts, qty };
    pools.set(size, p);
  }
  return p;
}

export function expectedSum(size: number, batches: number): number {
  const p = pool(size);
  let sum = 0;
  for (let b = 0; b < batches; b++) sum += p.qty[b % POOL]!;
  return sum;
}

let active: Feed | undefined;
let channel: MessageChannel | undefined;

function port(): MessagePort {
  if (channel === undefined) {
    channel = new MessageChannel();
    channel.port1.onmessage = (event: MessageEvent<number>) => active!.onMessage(event.data);
  }
  return channel.port2;
}

/** Node: lets the process exit. */
export function closeChannel(): void {
  channel?.port1.close();
  channel?.port2.close();
  channel = undefined;
}

export interface FeedOptions {
  readonly batches: number;
  readonly batchSize: number;
  readonly window: number;
  /** Send the failure terminal instead of completion after the last batch. */
  readonly fail: boolean;
  readonly measure: "drain" | "handler";
  /** Wrap each row into an item when the batch is decoded (eager) rather than when the iterator hands it out. */
  readonly wrap?: ((item: unknown) => unknown) | undefined;
  /** Subscription: no credits; the timer task calls deliver() and finish(). */
  readonly push?: boolean | undefined;
  /** Called after each delivered batch (slow-consumer sampling). */
  readonly onDeliver?: ((feed: Feed) => void) | undefined;
  /** Hand out whole batches (the batch variant). */
  readonly whole: boolean;
}

export class Feed {
  readonly #clock: Clock;
  readonly #options: FeedOptions;
  readonly #pool: Pool;
  readonly #half: number;
  // host side
  #credits: number;
  #sent = 0;
  #terminalSent = false;
  // page side
  readonly #queue: unknown[][] = [];
  #current: unknown[] = [];
  #index = 0;
  #waiter: (() => void) | undefined;
  #consumed = 0;
  #granted = 0;
  /** 0 open, 1 completed, 2 failed. */
  state = 0;
  received = 0;
  // instrumentation
  #taskStart = -1;
  longest = 0;
  busy = 0;
  maxBuffered = 0;
  tasks = 0;

  constructor(clock: Clock, options: FeedOptions) {
    this.#clock = clock;
    this.#options = options;
    this.#pool = pool(options.batchSize);
    this.#credits = options.window;
    this.#half = Math.max(1, options.window >> 1);
  }

  /** Opens the stream: the host starts sending under its initial credits. */
  start(): void {
    active = this;
    this.#pump();
  }

  /** True when the host has used every credit (or sent everything) and every message it sent has arrived. */
  get hostIdle(): boolean {
    return (this.#credits === 0 || this.#terminalSent) && this.received === this.#sent + (this.#terminalSent ? 1 : 0);
  }

  get buffered(): number {
    let n = this.#current.length - this.#index;
    for (const batch of this.#queue) n += batch.length;
    return n;
  }

  // ---------------------------------------------------------------- host side

  #grant(n: number): void {
    this.#credits += n;
    this.#pump();
  }

  #pump(): void {
    const p = port();
    while (this.#credits > 0 && this.#sent < this.#options.batches) {
      this.#credits--;
      p.postMessage(this.#sent++);
    }
    if (this.#sent === this.#options.batches && !this.#terminalSent) {
      this.#terminalSent = true;
      p.postMessage(this.#options.fail ? FAIL : END);
    }
  }

  // ---------------------------------------------------------------- page side

  onMessage(message: number): void {
    this.received++;
    if (message >= 0) this.deliver(this.#pool.texts[message % POOL]!);
    else {
      this.state = message === END ? 1 : 2;
      this.#wake();
    }
  }

  /** One batch message: decode, queue, wake the iterator. Subscriptions call it directly from their timer task. */
  deliver(text: string): void {
    const start = this.#clock.now();
    if (this.#taskStart < 0) this.#taskStart = start;
    const rows = decodeBatch(text);
    let items: unknown[] = rows;
    const wrap = this.#options.wrap;
    if (wrap !== undefined) {
      const wrapped: unknown[] = [];
      for (let i = 0; i < items.length; i++) wrapped.push(wrap(items[i]));
      items = wrapped;
    }
    this.#queue.push(items);
    const buffered = this.buffered;
    if (buffered > this.maxBuffered) this.maxBuffered = buffered;
    this.#options.onDeliver?.(this);
    this.#wake();
    if (this.#options.measure === "handler") this.#record(start);
  }

  #wake(): void {
    const waiter = this.#waiter;
    if (waiter !== undefined) {
      this.#waiter = undefined;
      waiter();
    }
  }

  #record(start: number): void {
    const d = this.#clock.now() - start;
    this.busy += d;
    this.tasks++;
    if (d > this.longest) this.longest = d;
    this.#taskStart = -1;
  }

  /** The iterator found nothing to hand out: the app has caught up with what has arrived. */
  drained(): void {
    if (this.#options.measure === "drain" && this.#taskStart >= 0) this.#record(this.#taskStart);
  }

  /** Resolves when a batch or the terminal message arrives. */
  wait(): Promise<void> {
    return new Promise(resolve => { this.#waiter = resolve; });
  }

  /** The next item, or EMPTY. Clears the slot so a consumed item is not kept alive by the buffer. */
  take(): unknown {
    if (this.#index >= this.#current.length) {
      const next = this.#queue.shift();
      if (next === undefined) return EMPTY;
      this.#current = next;
      this.#index = 0;
    }
    const current = this.#current;
    const i = this.#index++;
    const item = current[i];
    current[i] = undefined;
    if (this.#index === current.length) this.#batchDone();
    return item;
  }

  /** The next whole batch, or EMPTY. */
  takeBatch(): unknown[] | typeof EMPTY {
    const next = this.#queue.shift();
    if (next === undefined) return EMPTY;
    this.#batchDone();
    return next;
  }

  /** Ends a subscription (it has no host messages of its own here). */
  finish(failed: boolean): void {
    this.state = failed ? 2 : 1;
    this.#wake();
  }

  #batchDone(): void {
    if (this.#options.push === true) return; // subscriptions: no credits (the host drops oldest instead)
    this.#consumed++;
    const ungranted = this.#consumed - this.#granted;
    if (ungranted >= this.#half) {
      this.#granted += ungranted;
      this.#grant(ungranted);
    }
  }
}
