// Prototype of the @spar/sdk result model (not the real SDK).
export type Execution = "notStarted" | "completed" | "unknown";

/** Infrastructure failure: transport, protocol, cancellation, internal. Same for every command. */
export interface CallError {
  readonly kind: "transport" | "protocol" | "cancelled" | "internal";
  readonly code: string;
  readonly message: string;
  readonly execution: Execution;
  readonly call: string;
}

/** A declared application error: the command ran and reported this outcome. */
export type ApplicationError<Code extends string, Payload> = Payload & {
  readonly kind: "application";
  readonly code: Code;
  readonly execution: "completed";
};

type AnyApplicationError = { readonly kind: "application"; readonly code: string };

type Outcome<T, E> =
  | { readonly ok: true; readonly value: T }
  | { readonly ok: false; readonly error: E | CallError };

declare const result: unique symbol;

/** Opaque outcome of a call (prototype shape made opaque with a brand). */
export interface Result<T, E extends AnyApplicationError = never> {
  readonly [result]: { readonly value: T; readonly errors: E };
}

type ErrorHandlers<E extends AnyApplicationError> = {
  readonly [K in E["code"]]: (error: Extract<E, { readonly code: K }>) => unknown;
};

type Handlers<T, E extends AnyApplicationError> =
  & { readonly success: (value: T) => unknown; readonly failure?: (error: CallError) => unknown }
  & ([E] extends [never] ? { readonly errors?: never } : { readonly errors: ErrorHandlers<E> });

/** Rejects error keys that the command does not declare (typos), on top of the required ones. */
type NoUnknownErrors<H, E extends AnyApplicationError> = H extends { readonly errors: infer X }
  ? { readonly errors: { readonly [K in keyof X]: K extends E["code"] ? X[K] : never } }
  : unknown;

type Returns<F> = F extends (...args: never[]) => infer R ? R : never;

/** The union of what the handlers return; `undefined` when `failure` is left to the interceptor. */
type MatchResult<H> =
  | Returns<H extends { readonly success: infer S } ? S : never>
  | (H extends { readonly errors: infer X } ? { [K in keyof X]: Returns<X[K]> }[keyof X] : never)
  | (H extends { readonly failure: infer F } ? Returns<F> : undefined);

let onFailure: (error: CallError) => void = error => console.error(error.call, error.code, error.execution);

/** App-wide interceptor for infrastructure failures that a call site does not handle itself. */
export function configureSpar(options: { readonly onFailure: (error: CallError) => void }): void {
  onFailure = options.onFailure;
}

/** Exhaustive over success and every declared error; infrastructure failures go to `failure` or the interceptor. */
export function match<T, E extends AnyApplicationError, const H extends Handlers<T, E>>(
  outcome: Result<T, E>, handlers: H & NoUnknownErrors<H, E>): MatchResult<H> {
  const result = outcome as unknown as Outcome<T, E>;
  type Any = (value: unknown) => unknown;
  if (result.ok) return (handlers.success as Any)(result.value) as MatchResult<H>;
  const error = result.error;
  if (error.kind === "application") {
    const table = (handlers as { readonly errors?: Record<string, Any> }).errors!;
    return table[error.code]!(error) as MatchResult<H>;
  }

  const callError = error as CallError;
  if (handlers.failure) return (handlers.failure as Any)(callError) as MatchResult<H>;
  onFailure(callError);
  return undefined as MatchResult<H>;
}

export class SparCallError extends Error {
  constructor(readonly error: { readonly kind: string; readonly code: string }) { super(error.code); }
}

/** Opt-in exception style, for libraries that expect rejected promises. */
export function unwrap<T, E extends AnyApplicationError>(outcome: Result<T, E>): T {
  const result = outcome as unknown as Outcome<T, E>;
  if (result.ok) return result.value;
  throw new SparCallError(result.error);
}
