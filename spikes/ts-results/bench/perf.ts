// Runtime cost of each form: wrapping and matching outcomes, synchronously and through awaited calls.
import { setTransport, type Outcome } from "../forms/core.js";
import * as a from "../forms/a-method/sdk.js";
import * as b from "../forms/b-function/sdk.js";
import * as c from "../forms/c-call/sdk.js";

interface Errors { readonly notFound: { readonly id: string }; readonly stale: { readonly reason: string } }
const N = 2_000_000;
const calls = 200_000;
let sink = 0;
const outcomes: Outcome[] = Array.from({ length: 1024 }, (_, i): Outcome => i % 16 === 0
  ? { ok: false, call: "x.y", code: i % 32 === 0 ? "notFound" : "stale", payload: { id: "n", reason: "r" } }
  : { ok: true, value: i });

function time(label: string, run: () => void): void {
  for (let i = 0; i < 3; i++) run(); // warm-up
  const runs: number[] = [];
  for (let i = 0; i < 7; i++) {
    const start = performance.now();
    run();
    runs.push(performance.now() - start);
  }

  runs.sort((x, y) => x - y);
  console.log(`${label.padEnd(44)} median ${(runs[3]! * 1e6 / N).toFixed(1).padStart(6)} ns/result`);
}

async function timeAsync(label: string, run: () => Promise<void>): Promise<void> {
  for (let i = 0; i < 2; i++) await run();
  const runs: number[] = [];
  for (let i = 0; i < 5; i++) {
    const start = performance.now();
    await run();
    runs.push(performance.now() - start);
  }

  runs.sort((x, y) => x - y);
  console.log(`${label.padEnd(44)} median ${(runs[2]! * 1e6 / calls).toFixed(1).padStart(6)} ns/call`);
}

const handlers = { success: (v: number) => v, errors: { notFound: (e: { readonly id: string }) => e.id.length, stale: () => -1 } };

time("baseline: plain switch on the outcome", () => {
  for (let i = 0; i < N; i++) {
    const o = outcomes[i & 1023]!;
    sink += o.ok ? o.value as number : o.code === "notFound" ? 1 : -1;
  }
});
time("A: new Result(outcome).match(...)", () => {
  for (let i = 0; i < N; i++) sink += new a.Result<number, Errors>(outcomes[i & 1023]!).match(handlers) ?? 0;
});
time("B: match(outcome, ...)", () => {
  for (let i = 0; i < N; i++) sink += b.match(outcomes[i & 1023]! as unknown as b.Result<number, Errors>, handlers) ?? 0;
});
time("C (after await): same as A", () => {
  for (let i = 0; i < N; i++) sink += new c.Result<number, Errors>(outcomes[i & 1023]!).match(handlers) ?? 0;
});

let k = 0;
setTransport(() => Promise.resolve({ ok: true, value: k++ }), () => { throw new Error("unused"); });
await timeAsync("A: (await invoke()).match(...)", async () => {
  for (let i = 0; i < calls; i++) sink += (await a.invoke<number, Errors>("x.y", undefined)).match(handlers) ?? 0;
});
await timeAsync("B: match(await invoke(), ...)", async () => {
  for (let i = 0; i < calls; i++) sink += b.match(await b.invoke<number, Errors>("x.y", undefined), handlers) ?? 0;
});
await timeAsync("C: await invoke().match(...)", async () => {
  for (let i = 0; i < calls; i++) sink += await c.invoke<number, Errors>("x.y", undefined).match(handlers) ?? 0;
});
await timeAsync("C: (await invoke()).match(...)", async () => {
  for (let i = 0; i < calls; i++) sink += (await c.invoke<number, Errors>("x.y", undefined)).match(handlers) ?? 0;
});
console.log(`(sink ${sink > 0 ? "+" : "-"})`);
