// Item 2: what JavaScript makes of the C# writer's JSON for the SQL tool's types.
// Usage (from spikes/wire-details): node js/types.mjs            (runs itself again under three TZ values for dates)
import { readFileSync } from "node:fs";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const text = readFileSync(new URL("../results/types.json", import.meta.url), "utf8");
const source = {};
const values = JSON.parse(text, function (key, value, context) {
  if (typeof value === "number" && context?.source !== undefined) source[key] = context.source;
  return value;
});

if (process.argv[2] === "dates") {
  // Child mode: date interpretation under the TZ this process was started with.
  const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;
  const show = (label, s) => {
    const d = new Date(s);
    console.log(`  - [${zone}] new Date(${JSON.stringify(s)}) -> toISOString ${d.toISOString()}, local date ${d.getFullYear()}-` +
      `${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")} ${String(d.getHours()).padStart(2, "0")}:` +
      `${String(d.getMinutes()).padStart(2, "0")} (${label})`);
  };
  show("DateTime Utc", values.dateTimeUtc);
  show("DateTime Local, written with the backend's offset", values.dateTimeLocal);
  show("DateTime Unspecified (SQL datetime2)", values.dateTimeUnspecified);
  show("DateTimeOffset", values.dateTimeOffset);
  show("DateOnly", values.dateOnly);
  process.exit(0);
}

console.log("## decimal through JSON.parse (written by C# as a JSON number)");
for (let i = 0; values[`decimal${i}`] !== undefined; i++) {
  const n = values[`decimal${i}`];
  const exact = values[`decimal${i}Text`];
  const shown = String(n);
  console.log(`- ${exact}: JS number prints ${shown} -> ${shown === exact ? "same text" : "DIFFERENT"}` +
    `; round trip JSON.stringify ${JSON.stringify(n)}`);
}
console.log(`- 0.1 + 0.2 in JS: ${0.1 + 0.2}`);

console.log("\n## Dates: JSON strings as written by C#");
for (const key of ["dateTimeUtc", "dateTimeLocal", "dateTimeUnspecified", "dateTimeOffset", "dateOnly", "timeOnly", "timeSpan"]) {
  console.log(`- ${key}: ${JSON.stringify(values[key])}`);
}
console.log(`- new Date(...).toISOString() of the Utc value: ${new Date(values.dateTimeUtc).toISOString()} (100 ns -> ms)`);
console.log(`- new Date("10:20:30.1234567") (TimeOnly): ${String(new Date(values.timeOnly))}`);
console.log(`- typeof Temporal: ${typeof Temporal}`);
const self = fileURLToPath(import.meta.url);
for (const tz of ["UTC", "America/Los_Angeles", "Asia/Tokyo"]) {
  const child = spawnSync(process.execPath, [self, "dates"], { env: { ...process.env, TZ: tz }, encoding: "utf8" });
  process.stdout.write(child.stdout + child.stderr);
}

console.log("\n## Guid, byte[], char, float, enum");
console.log(`- Guid: ${JSON.stringify(values.guid)}; crypto.randomUUID() is lower-case: ${/^[0-9a-f-]+$/.test(crypto.randomUUID())}`);
const b64 = values.rowversion;
const bytes = Uint8Array.from(atob(b64), (c) => c.charCodeAt(0));
console.log(`- rowversion ${JSON.stringify(b64)} -> atob bytes ${Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join("")}`);
console.log(`- Uint8Array.fromBase64 in Node ${process.version}: ${typeof Uint8Array.fromBase64}`);
console.log(`- float 0.1f written as ${source.float01}, JS reads ${values.float01}`);
console.log(`- enum written as ${JSON.stringify(values.enumName)}`);

console.log("\n## Non-finite numbers in JavaScript");
for (const [label, v] of [["NaN", NaN], ["Infinity", Infinity], ["-Infinity", -Infinity], ["-0", -0]]) {
  console.log(`- JSON.stringify({ x: ${label} }) -> ${JSON.stringify({ x: v })}; JSON.stringify([${label}]) -> ${JSON.stringify([v])}`);
}
for (const t of ["NaN", "\"NaN\"", "-0", "1e999"]) {
  try {
    const v = JSON.parse(t);
    console.log(`- JSON.parse(${t}) -> ${Object.is(v, -0) ? "-0" : String(v)} (${typeof v})`);
  } catch (e) {
    console.log(`- JSON.parse(${t}) -> THROWS ${e.constructor.name}`);
  }
}

console.log("\n## base64 decode/encode cost in JavaScript, 1 MiB (min of 9 runs)");
const mib = new Uint8Array(1 << 20).map((_, i) => (i * 31) & 0xff);
let binary = "";
for (let i = 0; i < mib.length; i += 0x8000) binary += String.fromCharCode(...mib.subarray(i, i + 0x8000));
const mibB64 = btoa(binary);
function time(fn) {
  fn();
  let best = Infinity;
  for (let i = 0; i < 9; i++) {
    const start = performance.now();
    fn();
    best = Math.min(best, performance.now() - start);
  }
  return best.toFixed(2);
}
const decodeAtob = (s) => {
  const raw = atob(s);
  const out = new Uint8Array(raw.length);
  for (let i = 0; i < raw.length; i++) out[i] = raw.charCodeAt(i);
  return out;
};
const encodeBtoa = (u8) => {
  let s = "";
  for (let i = 0; i < u8.length; i += 0x8000) s += String.fromCharCode(...u8.subarray(i, i + 0x8000));
  return btoa(s);
};
console.log(`- atob + loop, 1 MiB: ${time(() => decodeAtob(mibB64))} ms; correct ${decodeAtob(mibB64).every((b, i) => b === mib[i])}`);
console.log(`- btoa + fromCharCode chunks, 1 MiB: ${time(() => encodeBtoa(mib))} ms; equal ${encodeBtoa(mib) === mibB64}`);
console.log(`- Buffer.from(base64) (Node only), 1 MiB: ${time(() => Buffer.from(mibB64, "base64"))} ms`);
console.log(`- base64 is ${(mibB64.length / mib.length).toFixed(3)}x the bytes; JSON array of numbers would be ` +
  `${(JSON.stringify(Array.from(mib)).length / mib.length).toFixed(2)}x`);
