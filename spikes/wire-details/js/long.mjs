// Item 1: long/ulong beyond ±(2^53 − 1) in JavaScript. Precision, parse cost for 100k rows, send-side behaviour.
// Usage (from spikes/wire-details): node js/long.mjs
import { readFileSync } from "node:fs";

const text = readFileSync(new URL("../results/long.json", import.meta.url), "utf8");
const sources = [];
const parsed = JSON.parse(text, function (key, value, context) {
  if (typeof value === "number" && context?.source !== undefined) sources.push({ source: context.source, value });
  return value;
});

console.log("## Precision: the C# writer's numbers through JSON.parse");
for (const { source, value } of sources) {
  const back = BigInt(source) === BigInt(value) ? "exact" : `LOST (reads as ${BigInt(value)})`;
  console.log(`- ${source}: ${back}; Number.isSafeInteger ${Number.isSafeInteger(value)}; JSON.stringify -> ${JSON.stringify(value)}`);
}
console.log(`- as strings: ${JSON.stringify(parsed.longAsString)} (all exact, by construction)`);

console.log("\n## Send side");
const big = 9007199254740993n;
const trySend = (label, fn) => {
  try {
    console.log(`- ${label}: ${fn()}`);
  } catch (e) {
    console.log(`- ${label}: THROWS ${e.constructor.name}: ${e.message}`);
  }
};
trySend("JSON.stringify({ id: 9007199254740993n })", () => JSON.stringify({ id: big }));
trySend("replacer bigint -> string", () => JSON.stringify({ id: big }, (k, v) => (typeof v === "bigint" ? v.toString() : v)));
trySend("replacer bigint -> JSON.rawJSON", () =>
  JSON.stringify({ id: big }, (k, v) => (typeof v === "bigint" ? JSON.rawJSON(v.toString()) : v)));
trySend("JSON.stringify(2 ** 53 + 1) (a number literal the page typed)", () => JSON.stringify(2 ** 53 + 1));
trySend("JSON.stringify(1e21)", () => JSON.stringify(1e21));
trySend("JSON.stringify(1.5) for a long field", () => JSON.stringify(1.5));
trySend("Number(2n ** 63n - 1n)", () => String(Number(2n ** 63n - 1n)));
trySend("BigInt.asIntN(64, 2n ** 63n) (wraps)", () => String(BigInt.asIntN(64, 2n ** 63n)));

console.log("\n## Parse cost, 100,000 rows {id, qty, name} (min of 9 runs; Node " + process.version + ")");
const count = 100_000;
const safeIds = Array.from({ length: count }, (_, i) => 1_000_000_000n + BigInt(i) * 7n);
let seed = 53n;
const fullIds = Array.from({ length: count }, () => {
  seed = (seed * 6364136223846793005n + 1442695040888963407n) & 0xffffffffffffffffn;
  return BigInt.asIntN(64, seed);
});
const isSafe = (id) => id >= -9007199254740991n && id <= 9007199254740991n;
const rows = (ids, encode) => "[" + ids.map((id, i) => `{"id":${encode(id)},"qty":${i % 100},"name":"row ${i}"}`).join(",") + "]";
const asNumber = (id) => id.toString();
const asString = (id) => `"${id}"`;
const whenNeeded = (id) => (isSafe(id) ? id.toString() : `"${id}"`);

const payloads = {
  "safe ids as numbers": rows(safeIds, asNumber),
  "safe ids as strings": rows(safeIds, asString),
  "full-range ids as numbers (lossy)": rows(fullIds, asNumber),
  "full-range ids as strings": rows(fullIds, asString),
  "full-range ids, string only when unsafe": rows(fullIds, whenNeeded),
};
const unsafeShare = fullIds.filter((id) => !isSafe(id)).length;
console.log(`- full-range set: ${unsafeShare} of ${count} ids are outside the safe range`);

function time(fn) {
  fn();
  let best = Infinity;
  for (let i = 0; i < 9; i++) {
    const start = performance.now();
    fn();
    best = Math.min(best, performance.now() - start);
  }
  return best.toFixed(1);
}

const results = [];
function measure(label, payloadName, fn, check) {
  const payload = payloads[payloadName];
  const ms = time(() => fn(payload));
  const out = fn(payload);
  const note = check ? check(out) : "";
  results.push({ label, payloadName, ms, bytes: payload.length, note });
}

const exactCount = (ids) => (out) => {
  let exact = 0;
  for (let i = 0; i < out.length; i++) if (BigInt(out[i].id) === ids[i]) exact++;
  return `${exact}/${out.length} exact`;
};

measure("A number: JSON.parse", "safe ids as numbers", (t) => JSON.parse(t), exactCount(safeIds));
measure("A number: JSON.parse", "full-range ids as numbers (lossy)", (t) => JSON.parse(t), exactCount(fullIds));
measure("B string: JSON.parse (TS string)", "safe ids as strings", (t) => JSON.parse(t), exactCount(safeIds));
measure("B string: JSON.parse (TS string)", "full-range ids as strings", (t) => JSON.parse(t), exactCount(fullIds));
measure("B string -> bigint: JSON.parse + generated post-pass BigInt()", "full-range ids as strings", (t) => {
  const out = JSON.parse(t);
  for (const row of out) row.id = BigInt(row.id);
  return out;
}, exactCount(fullIds));
measure("B string -> bigint: same, safe ids", "safe ids as strings", (t) => {
  const out = JSON.parse(t);
  for (const row of out) row.id = BigInt(row.id);
  return out;
}, exactCount(safeIds));
measure("C string when unsafe: JSON.parse (TS number | string)", "full-range ids, string only when unsafe",
  (t) => JSON.parse(t), exactCount(fullIds));
measure("D bigint: reviver with context.source", "full-range ids as numbers (lossy)",
  (t) => JSON.parse(t, (k, v, ctx) => (k === "id" ? BigInt(ctx.source) : v)), exactCount(fullIds));
measure("D bigint: reviver with context.source, safe ids", "safe ids as numbers",
  (t) => JSON.parse(t, (k, v, ctx) => (k === "id" ? BigInt(ctx.source) : v)), exactCount(safeIds));
measure("(reviver overhead alone: identity reviver)", "safe ids as numbers", (t) => JSON.parse(t, (k, v) => v));
measure("D' bigint: regex quotes \"id\" numbers, then JSON.parse + BigInt", "full-range ids as numbers (lossy)", (t) => {
  const out = JSON.parse(t.replace(/"id":(-?\d+)/g, '"id":"$1"'));
  for (const row of out) row.id = BigInt(row.id);
  return out;
}, exactCount(fullIds));

for (const r of results) console.log(`- ${r.label} [${r.payloadName}, ${(r.bytes / 1e6).toFixed(2)} MB]: ${r.ms} ms ${r.note}`);

console.log("\n## Send cost, 100,000 rows (min of 9 runs)");
const safeRows = safeIds.map((id, i) => ({ id: Number(id), qty: i % 100, name: `row ${i}` }));
const bigRows = fullIds.map((id, i) => ({ id, qty: i % 100, name: `row ${i}` }));
const stringRows = fullIds.map((id, i) => ({ id: id.toString(), qty: i % 100, name: `row ${i}` }));
console.log(`- number, JSON.stringify: ${time(() => JSON.stringify(safeRows))} ms`);
console.log(`- number + Number.isSafeInteger check per id: ${time(() => {
  for (const r of safeRows) if (!Number.isSafeInteger(r.id)) throw new RangeError("unsafe");
  return JSON.stringify(safeRows);
})} ms`);
console.log(`- string (TS string), JSON.stringify: ${time(() => JSON.stringify(stringRows))} ms`);
console.log(`- bigint via generated pre-pass to string: ${time(() =>
  JSON.stringify(bigRows.map((r) => ({ id: r.id.toString(), qty: r.qty, name: r.name }))))} ms`);
console.log(`- bigint via replacer to string: ${time(() =>
  JSON.stringify(bigRows, (k, v) => (typeof v === "bigint" ? v.toString() : v)))} ms`);
console.log(`- bigint via replacer to JSON.rawJSON (number on the wire): ${time(() =>
  JSON.stringify(bigRows, (k, v) => (typeof v === "bigint" ? JSON.rawJSON(v.toString()) : v)))} ms`);
