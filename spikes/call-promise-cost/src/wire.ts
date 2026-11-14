// Stand-in transport. A call registers its resolver here; a "message from the host" later settles the oldest pending
// call. `wrap` says whether the transport builds the Result itself (native baseline and the Promise subclass) or hands
// the raw Outcome to the caller (form C, whose invoke adds `.then(outcome => new Result(outcome))`).
import { Result } from "./forms/c-call/sdk.js";
import type { Outcome } from "./forms/core.js";

type Resolve = (value: never) => void;

// A queue that keeps its capacity: emptying it resets the indices, so in steady state the stand-in transport
// allocates nothing of its own and adds the same (zero) cost to every variant.
let resolvers: (Resolve | undefined)[] = [];
let wraps: boolean[] = [];
let head = 0;
let tail = 0;

export function enqueue(resolve: Resolve, wrap: boolean): void {
  resolvers[tail] = resolve;
  wraps[tail] = wrap;
  tail++;
}

export function pending(): number {
  return tail - head;
}

/** Settles the oldest pending call with `outcome`, as the bridge does when a result frame arrives. */
export function deliver(outcome: Outcome): void {
  const resolve = resolvers[head]!;
  const wrap = wraps[head]!;
  resolvers[head] = undefined;
  head++;
  if (head === tail) head = tail = 0;
  resolve((wrap ? new Result(outcome) : outcome) as never);
}

/** Drops every pending call without settling it (heap measurements). */
export function drop(): void {
  resolvers = [];
  wraps = [];
  head = tail = 0;
}

/** Grows the queue to `count` slots up front, so that a heap measurement does not count the queue's own storage. */
export function reserve(count: number): void {
  if (tail !== 0) throw new Error("reserve needs an empty queue");
  for (let i = resolvers.length; i < count; i++) {
    resolvers[i] = undefined;
    wraps[i] = false;
  }
}

export const okOutcome = (value: number): Outcome => ({ ok: true, value });

export const cancelledOutcome = (call: string): Outcome => ({
  ok: false,
  call,
  error: { kind: "cancelled", code: "cancelled", message: "The call was cancelled.", execution: "unknown", call },
});
