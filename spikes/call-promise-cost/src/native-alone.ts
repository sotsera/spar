// Control for the Node timing: the native baseline in a process where no Promise subclass (and no Symbol.species or
// prototype.constructor override) was ever defined. If the subclasses had disabled an engine fast path for every
// promise, the baseline in the main run would be slower than here.
// Usage: node obj/bundle/native-alone.mjs
import { Result } from "./forms/c-call/sdk.js";
import { deliver, enqueue, okOutcome } from "./wire.js";

declare function setImmediate(callback: () => void): void;

const NAME = "bench.echo";
const OK = okOutcome(1);
const H = { success: (value: number) => value };

function invokeNative<T>(_call: string, _request: unknown): Promise<Result<T>> {
  return new Promise<Result<T>>(resolve => { enqueue(resolve, true); });
}

async function nativeAwait(n: number) { let s = 0; for (let i = 0; i < n; i++) { const c = invokeNative<number>(NAME, undefined); deliver(OK); s += (await c).match(H) ?? 0; } return s; }
async function nativeMatch(n: number) { let s = 0; for (let i = 0; i < n; i++) { const c = invokeNative<number>(NAME, undefined); deliver(OK); s += await c.then(r => r.match(H)) ?? 0; } return s; }
async function nativeUnwrap(n: number) { let s = 0; for (let i = 0; i < n; i++) { const c = invokeNative<number>(NAME, undefined); deliver(OK); s += await c.then(r => r.unwrap()); } return s; }

const runs = { await: nativeAwait, match: nativeMatch, unwrap: nativeUnwrap };
const nextTask = () => new Promise<void>(resolve => setImmediate(resolve));
let sink = 0;
let n = 1024;
for (;;) {
  const t0 = performance.now();
  sink += await nativeAwait(n);
  if (performance.now() - t0 >= 60 || n >= 1 << 21) break;
  n *= 2;
}

const samples: Record<string, number[]> = { await: [], match: [], unwrap: [] };
const keys = Object.keys(runs) as (keyof typeof runs)[];
for (let round = 0; round < 10; round++) {
  for (let k = 0; k < keys.length; k++) {
    const key = keys[(k + round) % keys.length]!;
    await nextTask();
    const t0 = performance.now();
    sink += await runs[key](n);
    if (round >= 3) samples[key]!.push((performance.now() - t0) * 1e6 / n);
  }
}

console.log(JSON.stringify({ engine: "node", mode: "native-alone", callsPerSample: n, samples, sink: sink > 0 ? "+" : "-" }));
