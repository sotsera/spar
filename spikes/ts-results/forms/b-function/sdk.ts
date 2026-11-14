// Prototype of the @spar/sdk result model, form B: an opaque branded value read with free functions.
import { settle, streamTransport, transport, unwrapOutcome, type CallOptions, type ErrorCases, type ErrorHandlers, type ErrorResults, type Handlers, type Outcome } from "../core.js";

export { configureSpar, SparError, type CallError, type CallOptions, type SubscriptionEvent } from "../core.js";

declare const result: unique symbol;

/** The outcome of one call. Opaque: read it with `match` (exhaustive) or `unwrap` (throws). */
export interface Result<T, E extends ErrorCases = {}> {
  readonly [result]: { readonly value: T; readonly errors: E };
}

/** Runs the handler for this outcome and returns its value. Without `failure`, infrastructure failures go to the app-wide interceptor and `match` returns `undefined`. */
export function match<T, E extends ErrorCases, S, H extends ErrorHandlers<E> = ErrorHandlers<E>, F = undefined>(
  outcome: Result<T, E>, handlers: Handlers<T, E, S, H, F>): S | ErrorResults<H> | F {
  return settle(outcome as unknown as Outcome, handlers as never) as S | ErrorResults<H> | F;
}

/** The success value, or throws `SparError`. */
export function unwrap<T>(outcome: Result<T, ErrorCases>): T {
  return unwrapOutcome(outcome as unknown as Outcome) as T;
}

export async function invoke<T, E extends ErrorCases = {}>(call: string, request: unknown, options?: CallOptions): Promise<Result<T, E>> {
  return await transport(call, request, options) as unknown as Result<T, E>;
}

export function stream<T, E extends ErrorCases = {}>(call: string, request: unknown, options?: CallOptions): AsyncIterable<Result<T, E>> {
  return streamTransport(call, request, options) as unknown as AsyncIterable<Result<T, E>>;
}
