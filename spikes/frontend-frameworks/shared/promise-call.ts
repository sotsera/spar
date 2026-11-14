// Candidate SDK change, tested beside the unchanged prototype: the pending call as a real Promise subclass with the same
// match/unwrap. Promise methods (then, finally) return plain Promises through Symbol.species.
import type { Call, Result } from "@spar/app/sdk";

export class PromiseCall<T, E extends object = {}> extends Promise<Result<T, E>> {
  static override get [Symbol.species]() { return Promise; }

  /** Wraps a Call; the generated client would construct it directly. */
  static of<T, E extends object = {}>(call: PromiseLike<Result<T, E>>): PromiseCall<T, E> {
    return new PromiseCall<T, E>(resolve => { resolve(call); });
  }
}

export interface PromiseCall<T, E extends object = {}> {
  readonly match: Call<T, E>["match"];
  readonly unwrap: Call<T, E>["unwrap"];
}

Object.defineProperties(PromiseCall.prototype, {
  match: { value(this: Promise<Result<unknown>>, handlers: never) { return this.then(r => r.match(handlers)); } },
  unwrap: { value(this: Promise<Result<unknown>>) { return this.then(r => r.unwrap()); } },
});
