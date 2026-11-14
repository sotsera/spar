// Prototype of the @spar/sdk result model, form A: a Result object with match/unwrap methods.
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

  /** The success value, or throws `SparError`. */
  unwrap(): T {
    return unwrapOutcome(this.#outcome) as T;
  }
}

export async function invoke<T, E extends ErrorCases = {}>(call: string, request: unknown, options?: CallOptions): Promise<Result<T, E>> {
  return new Result<T, E>(await transport(call, request, options));
}

export async function* stream<T, E extends ErrorCases = {}>(call: string, request: unknown, options?: CallOptions): AsyncIterable<Result<T, E>> {
  for await (const outcome of streamTransport(call, request, options)) yield new Result<T, E>(outcome);
}
