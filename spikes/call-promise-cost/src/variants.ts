// The pending-call shapes under test. Each `invoke` registers with the stand-in transport (wire.ts) exactly as a
// generated client would register with the bridge, and honours an AbortSignal the way form C's contract describes:
// an aborted call resolves to a `cancelled` failure; it never rejects.
import { Result, invoke as invokeFormC } from "./forms/c-call/sdk.js";
import { setTransport, type CallOptions, type ErrorCases, type ErrorHandlers, type ErrorResults, type Handlers, type Outcome } from "./forms/core.js";
import { PromiseCall } from "./promise-call-of.js";
import { cancelledOutcome, enqueue } from "./wire.js";

function onAbort(signal: AbortSignal | undefined, call: string, settle: (outcome: Outcome) => void): void {
  if (!signal) return;
  if (signal.aborted) settle(cancelledOutcome(call));
  else signal.addEventListener("abort", () => settle(cancelledOutcome(call)), { once: true });
}

// 1. Baseline: a plain native Promise<Result>, resolved by the transport.
export function invokeNative<T, E extends ErrorCases = {}>(call: string, _request: unknown, options?: CallOptions): Promise<Result<T, E>> {
  return new Promise<Result<T, E>>(resolve => {
    enqueue(resolve, true);
    onAbort(options?.signal, call, outcome => resolve(new Result<T, E>(outcome)));
  });
}

// 2. Form C, unchanged: the transport returns Promise<Outcome>; sdk.ts wraps it in the thenable Call.
setTransport(
  (call, _request, options) => new Promise<Outcome>(resolve => {
    enqueue(resolve, false);
    onAbort(options?.signal, call, resolve);
  }),
  () => { throw new Error("streams are not part of this spike"); });
export { invokeFormC };

// 3. The proposal: Call is a real Promise subclass, constructed by the client with an executor that stores resolve.
export class Call<T, E extends ErrorCases = {}> extends Promise<Result<T, E>> {
  static override get [Symbol.species]() { return Promise; }

  /** Waits for the call, then runs the handler for its outcome. */
  match<S, H extends ErrorHandlers<E> = ErrorHandlers<E>, F = undefined>(handlers: Handlers<T, E, S, H, F>): Promise<S | ErrorResults<H> | F> {
    return this.then(result => result.match(handlers));
  }

  /** Waits for the call; resolves with the success value or rejects with `SparError`. */
  unwrap(): Promise<T> {
    return this.then(result => result.unwrap());
  }
}

export function invokeSubclass<T, E extends ErrorCases = {}>(call: string, _request: unknown, options?: CallOptions): Call<T, E> {
  return new Call<T, E>(resolve => {
    enqueue(resolve, true);
    onAbort(options?.signal, call, outcome => resolve(new Result<T, E>(outcome)));
  });
}

// 4. The frontend-frameworks prototype: a Promise subclass that wraps an existing form C call.
export function invokeOf<T, E extends ErrorCases = {}>(call: string, request: unknown, options?: CallOptions): PromiseCall<T, E> {
  return PromiseCall.of(invokeFormC<T, E>(call, request, options));
}

// 5. As 3, without the Symbol.species override: then/catch/finally construct another CallNoSpecies.
export class CallNoSpecies<T, E extends ErrorCases = {}> extends Promise<Result<T, E>> {
  match<S, H extends ErrorHandlers<E> = ErrorHandlers<E>, F = undefined>(handlers: Handlers<T, E, S, H, F>): Promise<S | ErrorResults<H> | F> {
    return this.then(result => result.match(handlers));
  }

  unwrap(): Promise<T> {
    return this.then(result => result.unwrap());
  }
}

export function invokeNoSpecies<T, E extends ErrorCases = {}>(call: string, _request: unknown, options?: CallOptions): CallNoSpecies<T, E> {
  return new CallNoSpecies<T, E>(resolve => {
    enqueue(resolve, true);
    onAbort(options?.signal, call, outcome => resolve(new Result<T, E>(outcome)));
  });
}

// 6. Exploratory: as 3, but instances report `constructor === Promise` (prototype property), and no species getter.
// PromiseResolve (used by await, Promise.resolve, Promise.all) then accepts the call as a native promise, and
// SpeciesConstructor finds Promise directly. `instanceof CallPromiseCtor` still holds.
export class CallPromiseCtor<T, E extends ErrorCases = {}> extends Promise<Result<T, E>> {
  match<S, H extends ErrorHandlers<E> = ErrorHandlers<E>, F = undefined>(handlers: Handlers<T, E, S, H, F>): Promise<S | ErrorResults<H> | F> {
    return this.then(result => result.match(handlers));
  }

  unwrap(): Promise<T> {
    return this.then(result => result.unwrap());
  }
}

Object.defineProperty(CallPromiseCtor.prototype, "constructor", { value: Promise, writable: true, configurable: true, enumerable: false });

export function invokePromiseCtor<T, E extends ErrorCases = {}>(call: string, _request: unknown, options?: CallOptions): CallPromiseCtor<T, E> {
  return new CallPromiseCtor<T, E>(resolve => {
    enqueue(resolve, true);
    onAbort(options?.signal, call, outcome => resolve(new Result<T, E>(outcome)));
  });
}

export interface Variant {
  readonly key: "native" | "formC" | "subclass" | "of" | "noSpecies" | "ctor";
  readonly label: string;
  readonly invoke: (call: string, request: unknown, options?: CallOptions) => PromiseLike<Result<number, never>>;
  /** false for the native baseline: match and unwrap go through `.then(r => r.match(h))` instead. */
  readonly methods: boolean;
}

export const variants: readonly Variant[] = [
  { key: "native", label: "native Promise<Result> (baseline)", invoke: invokeNative, methods: false },
  { key: "formC", label: "form C thenable Call (today)", invoke: invokeFormC, methods: true },
  { key: "subclass", label: "Promise subclass, built by the client (proposal)", invoke: invokeSubclass, methods: true },
  { key: "of", label: "Promise subclass wrapping a form C call (PromiseCall.of)", invoke: invokeOf, methods: true },
  { key: "noSpecies", label: "Promise subclass without Symbol.species", invoke: invokeNoSpecies, methods: true },
  { key: "ctor", label: "Promise subclass whose instances have constructor === Promise", invoke: invokePromiseCtor, methods: true },
];
