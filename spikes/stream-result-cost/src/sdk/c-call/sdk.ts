// Prototype of the @spar/sdk result model: the pending call is a Promise subclass with match/unwrap, and so is its awaited Result (decision 2026-11-14).
import { settle, streamTransport, transport, unwrapOutcome, type CallOptions, type ErrorCases, type ErrorHandlers, type ErrorResults, type Handlers, type Outcome } from "../core.js";

export { configureSpar, SparError, type CallError, type CallOptions, type SubscriptionEvent } from "../core.js";

/** The outcome of one call. Read it with `match` (exhaustive) or `unwrap` (throws). */
export class Result<T, E extends ErrorCases = {}> {
  readonly #outcome: Outcome;

  /** @internal Created by generated clients and by test helpers. */
  constructor(outcome: Outcome) {
    this.#outcome = outcome;
  }

  /** Runs the handler for this outcome and returns its value. Without `failure`, infrastructure failures go to the app-wide interceptor and `match` returns `undefined`. */
  match<S, H extends ErrorHandlers<E> = ErrorHandlers<E>, F = undefined>(handlers: Handlers<T, E, S, H, F>): S | ErrorResults<H> | F {
    return settle(this.#outcome, handlers as never) as S | ErrorResults<H> | F;
  }

  /** Tags the class so frameworks that proxy plain objects (Vue) leave results raw. */
  get [Symbol.toStringTag](): string {
    return "SparResult";
  }

  /** The success value, or throws `SparError`. */
  unwrap(): T {
    return unwrapOutcome(this.#outcome) as T;
  }
}

/**
 * A call in flight: a `Promise` of its `Result`, with `match` and `unwrap`. It never rejects except through `unwrap`.
 * Exported as a type only, so the inherited statics (`Call.resolve`, …) are never reachable.
 */
class Call<T, E extends ErrorCases = {}> extends Promise<Result<T, E>> {
  /** `then`, `catch` and `finally` return plain promises. */
  static override get [Symbol.species](): PromiseConstructor {
    return Promise;
  }

  /** Waits for the call, then runs the handler for its outcome. */
  match<S, H extends ErrorHandlers<E> = ErrorHandlers<E>, F = undefined>(handlers: Handlers<T, E, S, H, F>): Promise<S | ErrorResults<H> | F> {
    return this.then(result => result.match(handlers));
  }

  /** Waits for the call; resolves with the success value or rejects with `SparError`. */
  unwrap(): Promise<T> {
    return this.then(result => result.unwrap());
  }
}

export type { Call };

export function invoke<T, E extends ErrorCases = {}>(call: string, request: unknown, options?: CallOptions): Call<T, E> {
  return new Call<T, E>((resolve, reject) => {
    transport(call, request, options).then(outcome => { resolve(new Result<T, E>(outcome)); }, reject);
  });
}

export async function* stream<T, E extends ErrorCases = {}>(call: string, request: unknown, options?: CallOptions): AsyncIterable<Result<T, E>> {
  for await (const outcome of streamTransport(call, request, options)) yield new Result<T, E>(outcome);
}
