// Runs each form against a stand-in transport: handler choice, the interceptor, unknown cases, unwrap and streams.
// Small asserts instead of node:assert: the spike has no @types/node in the offline npm cache.
function strictEqual(actual: unknown, expected: unknown, what = ""): void {
  if (actual !== expected) throw new Error(`${what}: expected ${String(expected)}, got ${String(actual)}`);
}
function deepStrictEqual(actual: unknown, expected: unknown, what = ""): void {
  strictEqual(JSON.stringify(actual), JSON.stringify(expected), what);
}
function ok(value: boolean, what = ""): void {
  strictEqual(value, true, what);
}
async function rejects(promise: Promise<unknown>, test: (e: unknown) => boolean): Promise<void> {
  try {
    await promise;
  } catch (e) {
    ok(test(e), "rejection");
    return;
  }
  throw new Error("expected a rejection");
}
import { configureSpar, setTransport, SparError, type CallError, type Outcome } from "../forms/core.js";
import { copyJobs as ca } from "../forms/a-method/copyJobs.js";
import { copyJobs as cb } from "../forms/b-function/copyJobs.js";
import { match as matchB, unwrap as unwrapB } from "../forms/b-function/sdk.js";
import { copyJobs as cc } from "../forms/c-call/copyJobs.js";

const failure: CallError = { kind: "transport", code: "lost", message: "connection lost", execution: "unknown", call: "copyJobs.plan" };
const replies: Record<string, Outcome> = {
  ok: { ok: true, value: { steps: [{ table: "t", estimatedRows: 3 }] } },
  declared: { ok: false, call: "copyJobs.plan", code: "tableMissing", payload: { connection: "c", table: "t" } },
  undeclared: { ok: false, call: "copyJobs.plan", code: "brandNew", payload: {} },
  failure: { ok: false, call: "copyJobs.plan", error: failure },
};
let reply: Outcome = replies["ok"]!;
setTransport(async () => reply, async function* () { yield { ok: true, value: { cells: ["a"] } }; yield { ok: true, value: { cells: ["b"] } }; yield { ok: false, call: "copyJobs.preview", error: failure }; });
const intercepted: string[] = [];
configureSpar({ onFailure: e => intercepted.push(`${e.code}/${e.execution}`) });

const handlers = {
  success: (p: { readonly steps: readonly unknown[] }) => `ok ${p.steps.length}`,
  errors: { connectionNotFound: () => "nf", tableMissing: (e: { readonly table: string }) => `missing ${e.table}`, columnsMismatch: () => "cm" },
};
const recipe = { source: "a", target: "b", tables: [] };
const forms = {
  a: (h: typeof handlers | (typeof handlers & { failure: (e: CallError) => string })) => ca.plan(recipe).then(r => r.match(h)),
  b: (h: typeof handlers | (typeof handlers & { failure: (e: CallError) => string })) => cb.plan(recipe).then(r => matchB(r, h)),
  c: (h: typeof handlers | (typeof handlers & { failure: (e: CallError) => string })) => cc.plan(recipe).match(h),
};

for (const [name, run] of Object.entries(forms)) {
  intercepted.length = 0;
  reply = replies["ok"]!; strictEqual(await run(handlers), "ok 1", name);
  reply = replies["declared"]!; strictEqual(await run(handlers), "missing t", name);
  reply = replies["failure"]!; strictEqual(await run(handlers), undefined, name);
  strictEqual(await run({ ...handlers, failure: e => `failed ${e.code}` }), "failed lost", name);
  reply = replies["undeclared"]!; strictEqual(await run(handlers), undefined, name);
  strictEqual(await run({ ...handlers, failure: e => `${e.kind} ${e.code}` }), "protocol unknownErrorCase", name);
  deepStrictEqual(intercepted, ["lost/unknown", "unknownErrorCase/completed"], name);
  console.log(`form ${name}: unary ok`);
}

reply = replies["declared"]!;
const thrown = (e: unknown) => e instanceof SparError && e.error.code === "tableMissing" && "table" in e.error && e.error.table === "t";
await rejects(ca.plan(recipe).then(r => r.unwrap()), thrown);
await rejects(cb.plan(recipe).then(r => unwrapB(r)), thrown);
await rejects(cc.plan(recipe).unwrap(), thrown);
reply = replies["ok"]!;
deepStrictEqual(await cc.plan(recipe).unwrap(), replies["ok"]!.ok ? (replies["ok"] as { value: unknown }).value : null);
console.log("unwrap ok (A, B, C)");

for (const [name, items] of [["a", ca.preview(recipe)], ["b", cb.preview(recipe)], ["c", cc.preview(recipe)]] as const) {
  const seen: string[] = [];
  for await (const item of items) {
    const h = { success: (r: { readonly cells: readonly (string | null)[] }) => seen.push(r.cells.join()), errors: { tableMissing: () => seen.push("missing") }, failure: (e: CallError) => seen.push(`end ${e.code}`) };
    if (name === "b") matchB(item as Parameters<typeof matchB>[0], h as never); else (item as { match(h: unknown): unknown }).match(h);
  }

  deepStrictEqual(seen, ["a", "b", "end lost"], name);
}
console.log("streams ok (A, B, C)");

// Opacity at run time: a class Result has no readable fields; a branded B value is a plain object.
const a = await ca.count();
deepStrictEqual(Object.keys(a), []);
ok(Object.keys(await cb.count()).length > 0);
console.log("opacity: A/C hide the outcome in #private fields; B is a plain object with visible fields");
