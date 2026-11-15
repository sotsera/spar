// Calls and results (plan section 2b; decisions "Opaque results in TypeScript", "`CallError` kinds" and
// "Bridge messages: encoding C header fields"). Results are opaque: the value is reached only through match or unwrap.

/** Whether the command ran. `notStarted` only when the host is certain. */
export type Execution = "notStarted" | "stopped" | "completed" | "unknown";

/** What went wrong, for choosing what to tell the user; `code` gives the precise cause. */
export type CallErrorKind = "cancelled" | "unavailable" | "rejected" | "failed" | "transport" | "response" | "protocol";

/** The call an outcome belongs to. The SDK fills it from its own record of the call id; nothing extra travels. */
export interface CallName {
  readonly contract: string;
  readonly command: string;
}

/** An infrastructure failure, the same for every command. Declared errors are never a CallError. There is no message. */
export interface CallError extends CallName {
  readonly kind: CallErrorKind;
  readonly code: string;
  readonly execution: Execution;
}

/** Declared errors of a command: case name to payload, `undefined` for a case without one. `{}` when there are none. */
export type ErrorCases = object;

/** The declared errors as a union on `code`, for code that needs one value (logging, `unwrap`). */
export type ApplicationError<E extends ErrorCases> = {
  readonly [K in keyof E]: E[K] extends undefined ? { readonly code: K } : { readonly code: K } & E[K];
}[keyof E];

export interface CallOptions {
  /** Aborting ends the wait; the call reports `cancelled` only when the backend confirms it. */
  readonly signal?: AbortSignal;
}

/** One handler per declared case; a case without a payload takes a handler with no parameter. */
export type ErrorHandlers<E extends ErrorCases> = {
  readonly [K in keyof E]: E[K] extends undefined ? () => unknown : (error: E[K]) => unknown;
};

/** Failure handlers by kind. Kinds left out go to the app-wide `onFailure`, and `match` returns undefined for them. */
export type FailureByKind = { readonly [K in CallErrorKind]?: (error: CallError & { readonly kind: K }) => unknown };

/** What a function returns, distributed over a union of functions. */
type Returns<F> = F extends (...args: never[]) => infer R ? R : never;

/** Keys that are not declared cases (typos), kept out of inference so the declared cases drive completions. */
type NoExtraCases<H, E extends ErrorCases> = NoInfer<
  [Exclude<keyof H, keyof E>] extends [never] ? unknown : { readonly [K in Exclude<keyof H, keyof E>]: never }
>;

/** Keys of a failure object that are not kinds (typos). */
type NoExtraKinds<K> = NoInfer<
  [Exclude<keyof K, CallErrorKind>] extends [never] ? unknown : { readonly [P in Exclude<keyof K, CallErrorKind>]: never }
>;

/** The union of what the error handlers return. */
export type ErrorResults<H> = Returns<H[keyof H]>;

/**
 * What `match` adds to its result for failures: `F` from a `failure` function, or the handlers of a `failure`
 * object. Without `failure`, or for kinds the object leaves out, the failure goes to `onFailure`: undefined.
 */
export type FailureResults<F, K> =
  [K] extends [never] ? ([F] extends [never] ? undefined : F)
  : K extends (...args: never[]) => unknown ? F
  : Returns<Exclude<K[keyof K], undefined>> | ([CallErrorKind] extends [keyof K] ? never : undefined);

/**
 * The handlers of `match`. `errors` is required, with every case, when the command declares errors, and not allowed
 * otherwise. Example: `call.match({ success: page => show(page), errors: { notFound: () => back() } })`.
 */
export type Handlers<T, E extends ErrorCases, S, H, F, K> =
  & { readonly success: (value: T) => S; readonly failure?: ((error: CallError) => F) | (K & FailureByKind & NoExtraKinds<K>) }
  & ([keyof E] extends [never] ? unknown : { readonly errors: ErrorHandlers<E> & H & NoExtraCases<H, E> });

/** The wire outcome of one call, internal to the SDK: a value, a declared error, or a failure. */
export type Outcome =
  | { readonly ok: true; readonly value: unknown }
  | { readonly ok: false; readonly code: string; readonly payload: unknown; readonly call: CallName }
  | { readonly ok: false; readonly code?: undefined; readonly error: CallError };

type FailureHook = (error: CallError) => void;

const defaultOnFailure: FailureHook = error =>
  console.error(`Spar: ${error.contract}.${error.command} failed: ${error.kind} / ${error.code} (${error.execution})`);

let onFailure: FailureHook = defaultOnFailure;
let observe: FailureHook | undefined;

/**
 * App-wide failure hooks. `onFailure` receives the failures no `match` handled; `unwrap` never reaches it, it throws.
 * `observe` sees every failure, handled or not, for logs and telemetry. Neither can retry or change a result.
 */
export function configureSpar(options: { readonly onFailure?: FailureHook; readonly observe?: FailureHook }): void {
  onFailure = options.onFailure ?? defaultOnFailure;
  observe = options.observe;
}

/** Thrown by `unwrap` for a declared error or an infrastructure failure. */
export class SparError<E extends ErrorCases = ErrorCases> extends Error {
  readonly error: ApplicationError<E> | CallError;

  constructor(error: ApplicationError<E> | CallError) {
    super(String(error.code));
    this.name = "SparError";
    this.error = error;
  }
}

type AnyHandler = (value?: unknown) => unknown;

interface AnyHandlers {
  readonly success: AnyHandler;
  readonly errors?: Readonly<Record<string, AnyHandler>>;
  readonly failure?: AnyHandler | Readonly<Partial<Record<CallErrorKind, AnyHandler>>>;
}

/** The failure for an error case this client does not know (a newer backend). */
function unknownCase(outcome: { readonly call: CallName }): CallError {
  return { kind: "protocol", code: "unknown-error-case", execution: "unknown", contract: outcome.call.contract, command: outcome.call.command };
}

function settle(outcome: Outcome, handlers: AnyHandlers): unknown {
  if (outcome.ok) {
    return handlers.success(outcome.value);
  }

  let error: CallError;
  if (outcome.code === undefined) {
    error = outcome.error;
  } else {
    const handler = handlers.errors?.[outcome.code];
    if (handler !== undefined) {
      return handler(outcome.payload);
    }

    error = unknownCase(outcome);
  }

  observe?.(error);
  const failure = handlers.failure;
  const handler = typeof failure === "function" ? failure : failure?.[error.kind];
  if (handler !== undefined) {
    return handler(error);
  }

  onFailure(error);
  return undefined;
}

function unwrapOutcome(outcome: Outcome): unknown {
  if (outcome.ok) {
    return outcome.value;
  }

  if (outcome.code === undefined) {
    observe?.(outcome.error);
    throw new SparError(outcome.error);
  }

  // The payload was built field by field by the generated decoder, never taken from JSON.parse as it is, so copying
  // its own properties cannot carry a parsed __proto__ key.
  const payload = (outcome.payload ?? {}) as object;
  throw new SparError({ code: outcome.code, ...payload } as never);
}

/** The outcome of one call. Read it with `match` (exhaustive over the declared errors) or `unwrap` (throws). */
export class Result<T, E extends ErrorCases = {}> {
  readonly #outcome: Outcome;

  /** @internal Created by the SDK and by test helpers. */
  constructor(outcome: Outcome) {
    this.#outcome = outcome;
  }

  /** Tags the class so frameworks that proxy plain objects (Vue) leave results raw. */
  get [Symbol.toStringTag](): string {
    return "SparResult";
  }

  /** Runs the handler for this outcome and returns its value. */
  match<S, H extends ErrorHandlers<E> = ErrorHandlers<E>, F = never, K = never>(
    handlers: Handlers<T, E, S, H, F, K>,
  ): S | ErrorResults<H> | FailureResults<F, K> {
    return settle(this.#outcome, handlers as unknown as AnyHandlers) as S | ErrorResults<H> | FailureResults<F, K>;
  }

  /** The success value, or throws `SparError`. */
  unwrap(): T {
    return unwrapOutcome(this.#outcome) as T;
  }
}

/**
 * A call in flight: a `Promise` of its `Result`, with `match` and `unwrap`. It never rejects except through `unwrap`.
 * Exported as a type only, so its inherited statics (`Call.resolve`, …) are never reachable.
 */
class Call<T, E extends ErrorCases = {}> extends Promise<Result<T, E>> {
  /** `then`, `catch` and `finally` return plain promises. */
  static override get [Symbol.species](): PromiseConstructor {
    return Promise;
  }

  override get [Symbol.toStringTag](): string {
    return "SparCall";
  }

  /** Waits for the call, then runs the handler for its outcome. */
  match<S, H extends ErrorHandlers<E> = ErrorHandlers<E>, F = never, K = never>(
    handlers: Handlers<T, E, S, H, F, K>,
  ): Promise<S | ErrorResults<H> | FailureResults<F, K>> {
    return this.then(result => result.match(handlers));
  }

  /** Waits for the call; resolves with the success value or rejects with `SparError`. */
  unwrap(): Promise<T> {
    return this.then(result => result.unwrap());
  }
}

export type { Call };

/** @internal Wraps an outcome that will arrive into a `Call`. */
export function callOf<T, E extends ErrorCases>(outcome: Promise<Outcome>): Call<T, E> {
  return new Call<T, E>(resolve => {
    void outcome.then(value => resolve(new Result<T, E>(value)));
  });
}
