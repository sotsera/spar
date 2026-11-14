// The stream shapes under test. Each sits on the same Feed (wire.ts); only what the app's `for await` receives differs.
//
// plain      plain items; a terminal failure rejects next() with SparError (the throwing iterator, baseline).
// result     every item is the prototype's Result (sdk/c-call/sdk.ts, unchanged): `new Result({ ok: true, value })`,
//            made when the iterator hands the item out. The failure is one more Result.
// lean       the same public shape (#private fields, match, unwrap, Symbol.toStringTag) with a cheaper success path:
//            the value is stored directly, so a success item costs one object instead of Result + outcome.
// eager      prototype Result made when the batch is decoded, so the buffer holds Results (affects retention).
// batch      exploratory: one Result per transport batch, whose value is the batch's rows.
// tail       exploratory: plain items, the loop always ends normally, and `stream.result` (a Promise<Result>) carries
//            the terminal outcome.
// genPlain   the SDK prototype's layering: a transport async iterable of outcomes, wrapped by an async generator that
// genResult  yields plain items and throws (genPlain), or the prototype's own `stream()` yielding Results (genResult).
import { Result, SparError, stream as sdkStream } from "./sdk/c-call/sdk.js";
import { settle, setTransport, unwrapOutcome, type ErrorCases, type ErrorHandlers, type ErrorResults, type Handlers, type Outcome } from "./sdk/core.js";
import { EMPTY, Feed, failureError, failureOutcome, type Clock, type FeedOptions } from "./wire.js";

const DONE: IteratorReturnResult<undefined> = Object.freeze({ value: undefined, done: true as const });

/** Same public shape as the prototype's Result; the success value is held directly. */
export class LeanResult<T, E extends ErrorCases = {}> {
  readonly #value: T | undefined;
  readonly #failure: Outcome | undefined;

  private constructor(value: T | undefined, failure: Outcome | undefined) {
    this.#value = value;
    this.#failure = failure;
  }

  /** @internal */
  static ok<T, E extends ErrorCases = {}>(value: T): LeanResult<T, E> {
    return new LeanResult<T, E>(value, undefined);
  }

  /** @internal */
  static from<T, E extends ErrorCases = {}>(outcome: Outcome): LeanResult<T, E> {
    return outcome.ok ? new LeanResult<T, E>(outcome.value as T, undefined) : new LeanResult<T, E>(undefined, outcome);
  }

  match<S, H extends ErrorHandlers<E> = ErrorHandlers<E>, F = undefined>(handlers: Handlers<T, E, S, H, F>): S | ErrorResults<H> | F {
    if (this.#failure === undefined) return handlers.success(this.#value as T);
    return settle(this.#failure, handlers as never) as S | ErrorResults<H> | F;
  }

  get [Symbol.toStringTag](): string {
    return "SparResult";
  }

  unwrap(): T {
    if (this.#failure === undefined) return this.#value as T;
    return unwrapOutcome(this.#failure) as T;
  }
}

abstract class Base<T> implements AsyncIterableIterator<T> {
  protected readonly feed: Feed;
  protected finished = false;

  constructor(feed: Feed) {
    this.feed = feed;
  }

  [Symbol.asyncIterator](): this {
    return this;
  }

  abstract next(): Promise<IteratorResult<T>>;

  return(): Promise<IteratorResult<T>> {
    this.finished = true;
    return Promise.resolve(DONE);
  }
}

class PlainIterator<T> extends Base<T> {
  next(): Promise<IteratorResult<T>> {
    const item = this.feed.take();
    if (item !== EMPTY) return Promise.resolve({ value: item as T, done: false });
    return this.#slow();
  }

  #slow(): Promise<IteratorResult<T>> {
    const feed = this.feed;
    feed.drained();
    if (this.finished) return Promise.resolve(DONE);
    if (feed.state === 0) return feed.wait().then(() => this.next());
    this.finished = true;
    if (feed.state === 2) return Promise.reject(new SparError(failureError));
    return Promise.resolve(DONE);
  }
}

class ResultIterator<T> extends Base<Result<T>> {
  next(): Promise<IteratorResult<Result<T>>> {
    const item = this.feed.take();
    if (item !== EMPTY) return Promise.resolve({ value: new Result<T>({ ok: true, value: item }), done: false });
    return this.#slow();
  }

  #slow(): Promise<IteratorResult<Result<T>>> {
    const feed = this.feed;
    feed.drained();
    if (this.finished) return Promise.resolve(DONE);
    if (feed.state === 0) return feed.wait().then(() => this.next());
    this.finished = true;
    if (feed.state === 2) return Promise.resolve({ value: new Result<T>(failureOutcome), done: false });
    return Promise.resolve(DONE);
  }
}

class LeanIterator<T> extends Base<LeanResult<T>> {
  next(): Promise<IteratorResult<LeanResult<T>>> {
    const item = this.feed.take();
    if (item !== EMPTY) return Promise.resolve({ value: LeanResult.ok<T>(item as T), done: false });
    return this.#slow();
  }

  #slow(): Promise<IteratorResult<LeanResult<T>>> {
    const feed = this.feed;
    feed.drained();
    if (this.finished) return Promise.resolve(DONE);
    if (feed.state === 0) return feed.wait().then(() => this.next());
    this.finished = true;
    if (feed.state === 2) return Promise.resolve({ value: LeanResult.from<T>(failureOutcome), done: false });
    return Promise.resolve(DONE);
  }
}

/** Items in the buffer are already Results. */
class EagerIterator<T> extends Base<Result<T>> {
  next(): Promise<IteratorResult<Result<T>>> {
    const item = this.feed.take();
    if (item !== EMPTY) return Promise.resolve({ value: item as Result<T>, done: false });
    return this.#slow();
  }

  #slow(): Promise<IteratorResult<Result<T>>> {
    const feed = this.feed;
    feed.drained();
    if (this.finished) return Promise.resolve(DONE);
    if (feed.state === 0) return feed.wait().then(() => this.next());
    this.finished = true;
    if (feed.state === 2) return Promise.resolve({ value: new Result<T>(failureOutcome), done: false });
    return Promise.resolve(DONE);
  }
}

class BatchIterator<T> extends Base<Result<readonly T[]>> {
  next(): Promise<IteratorResult<Result<readonly T[]>>> {
    const batch = this.feed.takeBatch();
    if (batch !== EMPTY) return Promise.resolve({ value: new Result<readonly T[]>({ ok: true, value: batch }), done: false });
    return this.#slow();
  }

  #slow(): Promise<IteratorResult<Result<readonly T[]>>> {
    const feed = this.feed;
    feed.drained();
    if (this.finished) return Promise.resolve(DONE);
    if (feed.state === 0) return feed.wait().then(() => this.next());
    this.finished = true;
    if (feed.state === 2) return Promise.resolve({ value: new Result<readonly T[]>(failureOutcome), done: false });
    return Promise.resolve(DONE);
  }
}

/** Plain items; the loop always ends normally; `result` settles with the terminal outcome. */
export class TailStream<T> extends Base<T> {
  readonly result: Promise<Result<void>>;
  readonly #settle: (result: Result<void>) => void;

  constructor(feed: Feed) {
    super(feed);
    let settleResult!: (result: Result<void>) => void;
    this.result = new Promise(resolve => { settleResult = resolve; });
    this.#settle = settleResult;
  }

  next(): Promise<IteratorResult<T>> {
    const item = this.feed.take();
    if (item !== EMPTY) return Promise.resolve({ value: item as T, done: false });
    return this.#slow();
  }

  #slow(): Promise<IteratorResult<T>> {
    const feed = this.feed;
    feed.drained();
    if (this.finished) return Promise.resolve(DONE);
    if (feed.state === 0) return feed.wait().then(() => this.next());
    this.finished = true;
    this.#settle(new Result<void>(feed.state === 2 ? failureOutcome : { ok: true, value: undefined }));
    return Promise.resolve(DONE);
  }
}

/** The transport layer of the SDK prototype: one outcome object per item, then the failure outcome. */
class OutcomeIterator extends Base<Outcome> {
  next(): Promise<IteratorResult<Outcome>> {
    const item = this.feed.take();
    if (item !== EMPTY) return Promise.resolve({ value: { ok: true, value: item }, done: false });
    return this.#slow();
  }

  #slow(): Promise<IteratorResult<Outcome>> {
    const feed = this.feed;
    feed.drained();
    if (this.finished) return Promise.resolve(DONE);
    if (feed.state === 0) return feed.wait().then(() => this.next());
    this.finished = true;
    if (feed.state === 2) return Promise.resolve({ value: failureOutcome, done: false });
    return Promise.resolve(DONE);
  }
}

async function* plainOverOutcomes<T>(source: AsyncIterable<Outcome>): AsyncIterable<T> {
  for await (const outcome of source) {
    if (outcome.ok) yield outcome.value as T;
    else throw new SparError(outcome.code === undefined ? outcome.error : failureError);
  }
}

let transportFeed: Feed | undefined;
setTransport(() => Promise.reject(new Error("unary not used")), () => new OutcomeIterator(transportFeed!));

export type Kind = "plain" | "result" | "lean" | "eager" | "batch" | "tail" | "genPlain" | "genResult";

export function open(kind: Kind, clock: Clock, options: Omit<FeedOptions, "wrap" | "whole">): { readonly feed: Feed; readonly stream: AsyncIterable<unknown> } {
  const feed = new Feed(clock, { ...options, whole: kind === "batch", wrap: kind === "eager" ? (item: unknown) => new Result({ ok: true, value: item }) : undefined });
  switch (kind) {
    case "plain": return { feed, stream: new PlainIterator(feed) };
    case "result": return { feed, stream: new ResultIterator(feed) };
    case "lean": return { feed, stream: new LeanIterator(feed) };
    case "eager": return { feed, stream: new EagerIterator(feed) };
    case "batch": return { feed, stream: new BatchIterator(feed) };
    case "tail": return { feed, stream: new TailStream(feed) };
    case "genPlain": return { feed, stream: plainOverOutcomes(new OutcomeIterator(feed)) };
    case "genResult": {
      transportFeed = feed;
      const s = sdkStream("rows.read", undefined);
      return { feed, stream: s };
    }
  }
}
