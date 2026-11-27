// Calls and results (plan section 2b; decisions "Opaque results in TypeScript", "`CallError` kinds" and
// "Bridge messages: encoding C header fields"). Results are opaque: the value is reached only through match or unwrap.
import { setBackendUpdateToast } from "./backendUpdate.js";

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
  /**
   * When the command threw, or its result could not be written: the id of the host's log entry for it, the call's
   * W3C trace id. Sent in every build, so a user can quote it.
   */
  readonly correlation?: string;
  /** What went wrong, sent only by a Development host. For developers; never show it to users. */
  readonly detail?: string;
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

/**
 * A subscription's options. `resume` may only be more cautious than the contract: an automatic subscription accepts
 * `"manual"` too, a manual one only `"manual"` (plan, "Subscription resumption").
 */
export interface SubscribeOptions<R extends "automatic" | "manual" = "automatic"> extends CallOptions {
  readonly resume?: R | "manual";
}

/**
 * The `events` handlers of a subscription (plan section 2c, "The `events` slot"). `reset` is required: ignoring it
 * leaves stale state. The others are informational.
 */
export interface SubscriptionEvents {
  /** Drop what you have: the values start again from the current state. */
  readonly reset: () => unknown;
  /** The page fell behind and the backend dropped `dropped` values before the next one. */
  readonly gap?: (event: { readonly dropped: number }) => unknown;
  /** The connection was lost; the subscription subscribes again, this being its `attempt`-th try. */
  readonly reconnecting?: (event: { readonly attempt: number }) => unknown;
  /** The subscription runs again after a lost connection. */
  readonly resumed?: () => unknown;
}

/** A subscription that keeps only its newest value never reports a gap. */
export interface LatestOnlyEvents {
  readonly reset: () => unknown;
  readonly reconnecting?: (event: { readonly attempt: number }) => unknown;
  readonly resumed?: () => unknown;
}

/** The `events` handlers an item accepts: one of the interfaces above, or `{}` for calls and one-shot streams. */
export type EventCases = object;

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

/** Keys of an `events` object that the subscription does not emit (typos). */
type NoExtraEvents<G, V extends EventCases> = NoInfer<
  [Exclude<keyof G, keyof V>] extends [never] ? unknown : { readonly [K in Exclude<keyof G, keyof V>]: never }
>;

/** What the `events` handlers add to `match`'s result, and undefined for the events left unhandled. */
export type EventResults<G, V extends EventCases> =
  [keyof V] extends [never] ? never : Returns<Exclude<G[keyof G], undefined>> | ([keyof V] extends [keyof G] ? never : undefined);

/**
 * The handlers of `match`. `errors` is required, with every case, when the command declares errors, and not allowed
 * otherwise; `events` likewise for a subscription's status events, with `reset` required. Example:
 * `call.match({ success: page => show(page), errors: { notFound: () => back() } })`.
 */
export type Handlers<T, E extends ErrorCases, S, H, F, K, V extends EventCases = {}, G = unknown> =
  & { readonly success: (value: T) => S; readonly failure?: ((error: CallError) => F) | (K & FailureByKind & NoExtraKinds<K>) }
  & ([keyof E] extends [never] ? unknown : { readonly errors: ErrorHandlers<E> & H & NoExtraCases<H, E> })
  & ([keyof V] extends [never] ? unknown : { readonly events: V & G & NoExtraEvents<G, V> });

/** The wire outcome of one call or item, internal to the SDK: a value, a status event, a declared error, or a failure. */
export type Outcome =
  | { readonly ok: true; readonly value: unknown }
  | { readonly ok: true; readonly event: string; readonly payload: unknown }
  | { readonly ok: false; readonly code: string; readonly payload: unknown; readonly call: CallName }
  | { readonly ok: false; readonly code?: undefined; readonly error: CallError };

type FailureHook = (error: CallError) => void;

const defaultOnFailure: FailureHook = error =>
  console.error(`Spar: ${error.contract}.${error.command} failed: ${error.kind} / ${error.code} (${error.execution})`);

let onFailure: FailureHook = defaultOnFailure;
let observe: FailureHook | undefined;
let resumeDefault: "automatic" | "manual" = "automatic";

/** The page's app-wide settings. Each call of `configureSpar` sets all of them, those left out to their defaults. */
export interface SparConfiguration {
  /** Receives the failures no `match` handled; by default they are written with `console.error`. */
  readonly onFailure?: FailureHook;
  /** Sees every failure, handled or not, for logs and telemetry. */
  readonly observe?: FailureHook;
  /**
   * Debug builds only: whether Spar's development overlay shows a toast for each C# edit Hot Reload applied to the
   * backend (`true` by default). Turn it off to show your own from `onBackendUpdate`. A production build has no toast.
   */
  readonly backendUpdateToast?: boolean;
  /**
   * Whether subscriptions subscribe again by themselves after a lost connection: `"manual"` turns that off for every
   * subscription of the page. It never turns it on for one whose contract says `Manual`.
   */
  readonly resume?: "automatic" | "manual";
}

/**
 * App-wide settings. `onFailure` receives the failures no `match` handled; `unwrap` never reaches it, it throws.
 * `observe` sees every failure, handled or not, for logs and telemetry. Neither can retry or change a result.
 * `backendUpdateToast: false` turns off the development overlay's toast for backend updates. `resume: "manual"` stops
 * subscriptions from subscribing again by themselves.
 */
export function configureSpar(options: SparConfiguration): void {
  onFailure = options.onFailure ?? defaultOnFailure;
  observe = options.observe;
  resumeDefault = options.resume ?? "automatic";
  if (import.meta.env?.DEV === true) {
    setBackendUpdateToast(options.backendUpdateToast !== false);
  }
}

/** @internal Back to the default settings, for page tests: each test starts from what an app's start-up code sets. */
export function resetConfiguration(): void {
  onFailure = defaultOnFailure;
  observe = undefined;
  resumeDefault = "automatic";
  if (import.meta.env?.DEV === true) {
    setBackendUpdateToast(true);
  }
}

/**
 * @internal Whether a subscription resumes by itself: only when its contract, the app's configuration and its own
 * options all allow it (plan, "Subscription resumption").
 */
export function resumes(contractAutomatic: boolean, options?: SubscribeOptions<"automatic" | "manual">): boolean {
  return contractAutomatic && resumeDefault === "automatic" && options?.resume !== "manual";
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
  readonly events?: Readonly<Record<string, AnyHandler | undefined>>;
  readonly failure?: AnyHandler | Readonly<Partial<Record<CallErrorKind, AnyHandler>>>;
}

/** The failure for an error case this client does not know (a newer backend). */
function unknownCase(outcome: { readonly call: CallName }): CallError {
  return { kind: "protocol", code: "unknown-error-case", execution: "unknown", contract: outcome.call.contract, command: outcome.call.command };
}

function settle(outcome: Outcome, handlers: AnyHandlers): unknown {
  if (outcome.ok) {
    // A status event never reaches the failure hooks; an optional one left unhandled is simply skipped.
    return "event" in outcome ? handlers.events?.[outcome.event]?.(outcome.payload) : handlers.success(outcome.value);
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
    return "event" in outcome ? undefined : outcome.value;
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

/**
 * The outcome of one call, stream item or subscription item. Read it with `match` (exhaustive over the declared errors
 * and, for a subscription, its status events) or `unwrap` (throws). `V` lists the events a subscription item can carry,
 * `{}` for calls and one-shot streams.
 */
export class Result<T, E extends ErrorCases = {}, V extends EventCases = {}> {
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
  match<S, H extends ErrorHandlers<E> = ErrorHandlers<E>, F = never, K = never, G = unknown>(
    handlers: Handlers<T, E, S, H, F, K, V, G>,
  ): S | ErrorResults<H> | FailureResults<F, K> | EventResults<G, V> {
    return settle(this.#outcome, handlers as unknown as AnyHandlers) as S | ErrorResults<H> | FailureResults<F, K> | EventResults<G, V>;
  }

  /** The success value, or throws `SparError`; a subscription's status event unwraps to `undefined`. */
  unwrap(): [keyof V] extends [never] ? T : T | undefined {
    return unwrapOutcome(this.#outcome) as [keyof V] extends [never] ? T : T | undefined;
  }
}

/**
 * A subscription (plan section 2c): an `AsyncIterable` of `Result` items, the app's values, its status events (`reset`
 * must be handled), and the declared error or failure that ended it, as its last item.
 */
export type Subscription<T, E extends ErrorCases = {}, V extends EventCases = SubscriptionEvents> = AsyncIterable<Result<T, E, V>>;

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
