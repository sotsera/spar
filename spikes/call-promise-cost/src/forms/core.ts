// Shared by every form: the public error types, the handler types and the runtime that picks a handler.
export type Execution = "notStarted" | "completed" | "unknown";

/** An infrastructure failure: transport, protocol, cancellation or an internal error. The same for every command. */
export interface CallError {
  readonly kind: "transport" | "protocol" | "cancelled" | "internal";
  readonly code: string;
  readonly message: string;
  readonly execution: Execution;
  readonly call: string;
}

/** Declared errors of a command: case name to payload. `{}` when the command declares none. */
export type ErrorCases = object;

/** The declared errors as a discriminated union on `code`, for code that needs one value (logging, `unwrap`). */
export type ApplicationError<E extends ErrorCases> = { readonly [K in keyof E]: { readonly code: K } & E[K] }[keyof E];

export interface CallOptions {
  readonly signal?: AbortSignal;
}

/** What a subscription delivers besides its terminal failure. */
export type SubscriptionEvent<T> =
  | { readonly type: "event"; readonly value: T }
  | { readonly type: "gap"; readonly dropped: number }
  | { readonly type: "reconnecting"; readonly attempt: number }
  | { readonly type: "reset" }
  | { readonly type: "resumed" };

/** One handler per declared error case. */
export type ErrorHandlers<E extends ErrorCases> = { readonly [K in keyof E]: (error: E[K]) => unknown };

/** Keys that are not declared cases (typos). Kept out of inference so the declared cases drive completions and messages. */
type NoExtraCases<H, E extends ErrorCases> = NoInfer<[Exclude<keyof H, keyof E>] extends [never] ? unknown : { readonly [K in Exclude<keyof H, keyof E>]: never }>;

/** `errors` is required, with every case, when the command declares errors, and not allowed otherwise. */
export type Handlers<T, E extends ErrorCases, S, H, F> =
  & { readonly success: (value: T) => S; readonly failure?: (error: CallError) => F }
  & ([keyof E] extends [never] ? unknown : { readonly errors: ErrorHandlers<E> & H & NoExtraCases<H, E> });

/** What a function returns, distributed over a union of functions. */
type Returns<F> = F extends (...args: never[]) => infer R ? R : never;

/** The union of what the error handlers return. */
export type ErrorResults<H> = Returns<H[keyof H]>;

/** Wire outcome, internal to the SDK. The transport creates one per call or stream item. */
export type Outcome =
  | { readonly ok: true; readonly value: unknown }
  | { readonly ok: false; readonly call: string; readonly code: string; readonly payload: unknown }
  | { readonly ok: false; readonly call: string; readonly code?: undefined; readonly error: CallError };

let onFailure: (error: CallError) => void = error => console.error(error.call, error.code, error.execution);

/** App-wide interceptor for infrastructure failures that a call site does not handle itself. It observes; it cannot recover. */
export function configureSpar(options: { readonly onFailure: (error: CallError) => void }): void {
  onFailure = options.onFailure;
}

/** Thrown by `unwrap` for a declared error or an infrastructure failure. */
export class SparError<E extends ErrorCases = ErrorCases> extends Error {
  readonly error: ApplicationError<E> | CallError;

  constructor(error: ApplicationError<E> | CallError) {
    super(String(error.code));
    this.error = error;
  }
}

type AnyHandler = (value: unknown) => unknown;

interface AnyHandlers {
  readonly success: AnyHandler;
  readonly errors?: Readonly<Record<string, AnyHandler>>;
  readonly failure?: AnyHandler;
}

/** Calls the handler for this outcome. A case the client does not know (a newer backend) is a protocol failure. */
export function settle(outcome: Outcome, handlers: AnyHandlers): unknown {
  if (outcome.ok) return handlers.success(outcome.value);

  let error: CallError;
  if (outcome.code === undefined) error = outcome.error;
  else {
    const handler = handlers.errors?.[outcome.code];
    if (handler) return handler(outcome.payload);
    error = { kind: "protocol", code: "unknownErrorCase", message: `${outcome.call} returned undeclared error ${outcome.code}`, execution: "completed", call: outcome.call };
  }

  if (handlers.failure) return handlers.failure(error);
  onFailure(error);
  return undefined;
}

export function unwrapOutcome(outcome: Outcome): unknown {
  if (outcome.ok) return outcome.value;
  if (outcome.code === undefined) throw new SparError(outcome.error);
  throw new SparError({ code: outcome.code, ...(outcome.payload as object) } as never);
}

// Stand-in transport: tests and benchmarks set it.
export type Transport = (call: string, request: unknown, options: CallOptions | undefined) => Promise<Outcome>;
export type StreamTransport = (call: string, request: unknown, options: CallOptions | undefined) => AsyncIterable<Outcome>;
export let transport: Transport = () => Promise.reject(new Error("no transport"));
export let streamTransport: StreamTransport = () => { throw new Error("no transport"); };

export function setTransport(unary: Transport, streams: StreamTransport): void {
  transport = unary;
  streamTransport = streams;
}
