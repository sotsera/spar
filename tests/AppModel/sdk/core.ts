// Shared by every form: the public error types, the handler types and the runtime that picks a handler.
export type Execution = "notStarted" | "stopped" | "completed" | "unknown";

/** What went wrong, for choosing what to tell the user (decision "`CallError` kinds", 2026-11-14). `code` gives the precise cause. */
export type CallErrorKind = "cancelled" | "unavailable" | "rejected" | "failed" | "transport" | "response" | "protocol";

/** The call an outcome belongs to. The SDK fills it from its own record of the call id; nothing extra travels. */
export interface CallName {
  readonly contract: string;
  readonly command: string;
}

/**
 * An infrastructure failure, the same for every command. Declared errors are never a CallError.
 * The decided fields (decisions "`CallError` kinds" and "Bridge messages: encoding C header fields"): kind, code,
 * execution, and the call it belongs to. There is no message.
 */
export interface CallError extends CallName {
  readonly kind: CallErrorKind;
  readonly code: string;
  readonly execution: Execution;
}

/** Declared errors of a command: case name to payload, `undefined` for a case without one. `{}` when the command declares none. */
export type ErrorCases = object;

/** The declared errors as a discriminated union on `code`, for code that needs one value (logging, `unwrap`). */
export type ApplicationError<E extends ErrorCases> = {
  readonly [K in keyof E]: E[K] extends undefined ? { readonly code: K } : { readonly code: K } & E[K];
}[keyof E];

export interface CallOptions {
  readonly signal?: AbortSignal;
}

/**
 * The `events` handlers of a subscription (decision 2026-11-14). `reset` is required: ignoring it leaves stale state.
 * The others are informational and optional.
 */
export interface SubscriptionEvents {
  /** Drop what you have: the values start again from the current state. */
  readonly reset: () => unknown;
  /** Values were dropped because the page fell behind. */
  readonly gap?: (event: { readonly dropped: number }) => unknown;
  readonly reconnecting?: (event: { readonly attempt: number }) => unknown;
  readonly resumed?: () => unknown;
}

/** A coalesced subscription keeps only the newest value, so it never reports a gap. */
export interface CoalescedEvents {
  readonly reset: () => unknown;
  readonly reconnecting?: (event: { readonly attempt: number }) => unknown;
  readonly resumed?: () => unknown;
}

/** The `events` handlers an item accepts: one of the interfaces above, or `{}` for calls and plain streams. */
export type EventCases = object;

/** Keys of an `events` object that the subscription does not emit (typos). */
type NoExtraEvents<G, V extends EventCases> = NoInfer<[Exclude<keyof G, keyof V>] extends [never] ? unknown : { readonly [K in Exclude<keyof G, keyof V>]: never }>;

/** What the event handlers add to `match`'s result, plus undefined for events left unhandled. */
export type EventResults<G, V extends EventCases> =
  [keyof V] extends [never] ? never : Returns<Exclude<G[keyof G], undefined>> | ([keyof V] extends [keyof G] ? never : undefined);

/** One handler per declared error case. */
/** A case without a payload (entry `undefined`, decision "M1a names") takes a handler with no parameter. */
export type ErrorHandlers<E extends ErrorCases> = {
  readonly [K in keyof E]: E[K] extends undefined ? () => unknown : (error: E[K]) => unknown;
};

/** Keys that are not declared cases (typos). Kept out of inference so the declared cases drive completions and messages. */
type NoExtraCases<H, E extends ErrorCases> = NoInfer<[Exclude<keyof H, keyof E>] extends [never] ? unknown : { readonly [K in Exclude<keyof H, keyof E>]: never }>;

/** Failure handlers by kind. Kinds left out go to the app-wide interceptor, and `match` returns undefined for them. */
export type FailureByKind = { readonly [K in CallErrorKind]?: (error: CallError & { readonly kind: K }) => unknown };

/**
 * What `match` adds to its result for failures. `F` is the return of a `failure` function, `K` the type of a
 * `failure` object. Without `failure`, or for kinds the object leaves out, failures go to the interceptor: undefined.
 */
export type FailureResults<F, K> =
  [K] extends [never] ? ([F] extends [never] ? undefined : F)
  : K extends (...args: never[]) => unknown ? F
  : Returns<Exclude<K[keyof K], undefined>> | ([CallErrorKind] extends [keyof K] ? never : undefined);

/** `errors` is required, with every case, when the command declares errors, and not allowed otherwise. */
export type Handlers<T, E extends ErrorCases, S, H, F, K, V extends EventCases = {}, G = unknown> =
  & { readonly success: (value: T) => S; readonly failure?: ((error: CallError) => F) | (K & FailureByKind & NoExtraKinds<K>) }
  & ([keyof E] extends [never] ? unknown : { readonly errors: ErrorHandlers<E> & H & NoExtraCases<H, E> })
  & ([keyof V] extends [never] ? unknown : { readonly events: V & G & NoExtraEvents<G, V> });

/** Keys of a failure object that are not kinds (typos). */
type NoExtraKinds<K> = NoInfer<[Exclude<keyof K, CallErrorKind>] extends [never] ? unknown : { readonly [P in Exclude<keyof K, CallErrorKind>]: never }>;

/** What a function returns, distributed over a union of functions. */
type Returns<F> = F extends (...args: never[]) => infer R ? R : never;

/** The union of what the error handlers return. */
export type ErrorResults<H> = Returns<H[keyof H]>;

/** Wire outcome, internal to the SDK. The transport creates one per call or stream item. */
export type Outcome =
  | { readonly ok: true; readonly value: unknown }
  | { readonly ok: true; readonly event: string; readonly payload: unknown }
  | { readonly ok: false; readonly code: string; readonly payload: unknown; readonly call: CallName }
  | { readonly ok: false; readonly code?: undefined; readonly error: CallError };

let onFailure: (error: CallError) => void = error => console.error(`${error.contract}.${error.command}`, error.kind, error.code, error.execution);
let observe: ((error: CallError) => void) | undefined;

/** The page's app-wide settings, as in src/Spar.Client/src/results.ts. Each call sets all of them, those left out to their defaults. */
export interface SparConfiguration {
  readonly onFailure?: (error: CallError) => void;
  readonly observe?: (error: CallError) => void;
  /** Debug builds only: whether Spar's development overlay shows a toast per backend update (`true` by default). */
  readonly backendUpdateToast?: boolean;
}

/** Stand-in for the toast setting. */
export let backendUpdateToast = true;

/**
 * App-wide settings. `onFailure` (the interceptor) receives the failures no call handled: a `match` without a
 * handler for that failure. `unwrap` never reaches it: it throws instead. `observe` receives every failure, handled
 * or not, for logs and telemetry. Neither can retry or change a call's result. `backendUpdateToast: false` turns the
 * development overlay's toast off.
 */
export function configureSpar(options: SparConfiguration): void {
  onFailure = options.onFailure ?? (error => console.error(`${error.contract}.${error.command}`, error.kind, error.code, error.execution));
  observe = options.observe;
  backendUpdateToast = options.backendUpdateToast !== false;
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
  readonly events?: Readonly<Record<string, AnyHandler | undefined>>;
  readonly failure?: AnyHandler | Readonly<Partial<Record<CallErrorKind, AnyHandler>>>;
}

/**
 * Calls the handler for this outcome. A case the client does not know (a newer backend) is the failure
 * `protocol` / `unknown-error-case`, whose execution is `unknown` (decision "`CallError` kinds").
 */
export function settle(outcome: Outcome, handlers: AnyHandlers): unknown {
  if (outcome.ok) return "event" in outcome ? handlers.events?.[outcome.event]?.(outcome.payload) : handlers.success(outcome.value);

  let error: CallError;
  if (outcome.code === undefined) error = outcome.error;
  else {
    const handler = handlers.errors?.[outcome.code];
    if (handler) return handler(outcome.payload);
    error = { kind: "protocol", code: "unknown-error-case", execution: "unknown", contract: outcome.call.contract, command: outcome.call.command };
  }

  observe?.(error);
  const failure = handlers.failure;
  const handler = typeof failure === "function" ? failure : failure?.[error.kind];
  if (handler) return handler(error);
  onFailure(error);
  return undefined;
}

export function unwrapOutcome(outcome: Outcome): unknown {
  if (outcome.ok) return "event" in outcome ? undefined : outcome.value;
  if (outcome.code === undefined) {
    observe?.(outcome.error);
    throw new SparError(outcome.error);
  }
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
