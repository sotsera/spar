// The SDK from spikes/ts-results form C (copied), compiled here to .js, .d.ts and maps as @spar/app/sdk.
import { settle, streamTransport, transport, unwrapOutcome, type CallOptions, type ErrorCases, type ErrorHandlers, type ErrorResults, type Handlers, type Outcome } from "./core.js";

export { configureSpar, SparError, type CallError, type CallOptions, type SubscriptionEvent } from "./core.js";

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

/** A call in flight. Await it for its `Result`, or match or unwrap it directly. It never rejects except through `unwrap`. */
export class Call<T, E extends ErrorCases = {}> implements PromiseLike<Result<T, E>> {
  readonly #result: Promise<Result<T, E>>;

  /** @internal */
  constructor(result: Promise<Result<T, E>>) {
    this.#result = result;
  }

  then<A = Result<T, E>, B = never>(
    onfulfilled?: ((value: Result<T, E>) => A | PromiseLike<A>) | null,
    onrejected?: ((reason: unknown) => B | PromiseLike<B>) | null): Promise<A | B> {
    return this.#result.then(onfulfilled, onrejected);
  }

  /** Waits for the call, then runs the handler for its outcome. */
  async match<S, H extends ErrorHandlers<E> = ErrorHandlers<E>, F = undefined>(handlers: Handlers<T, E, S, H, F>): Promise<S | ErrorResults<H> | F> {
    return (await this.#result).match(handlers);
  }

  /** Waits for the call; resolves with the success value or rejects with `SparError`. */
  async unwrap(): Promise<T> {
    return (await this.#result).unwrap();
  }
}

export function invoke<T, E extends ErrorCases = {}>(call: string, request: unknown, options?: CallOptions): Call<T, E> {
  return new Call(transport(call, request, options).then(outcome => new Result<T, E>(outcome)));
}

export async function* stream<T, E extends ErrorCases = {}>(call: string, request: unknown, options?: CallOptions): AsyncIterable<Result<T, E>> {
  for await (const outcome of streamTransport(call, request, options)) yield new Result<T, E>(outcome);
}
