// Item 3: lone surrogates in JavaScript. Reads the C# lossless prototype's output, checks the code units, and writes
// JSON.stringify's answer to results/surrogates-from-js.json for the C# side (WireProbe surrogates-back).
// Usage (from spikes/wire-details): node js/surrogates.mjs
import { readFileSync, writeFileSync } from "node:fs";

const units = (s) => Array.from({ length: s.length }, (_, i) => s.charCodeAt(i).toString(16).padStart(4, "0")).join(" ");

console.log("## JSON.stringify / JSON.parse and friends");
const lone = "a\ud800b";
console.log(`- JSON.stringify("a\\ud800b") -> ${JSON.stringify(lone)}`);
console.log(`- JSON.stringify("\\ud83d\\ude00") (valid pair) -> ${JSON.stringify("😀")} (raw, not escaped)`);
console.log(`- JSON.parse('"\\\\ud800"') -> units [${units(JSON.parse('"\\ud800"'))}]`);
console.log(`- JSON.parse('"\\\\uFFFD"') -> units [${units(JSON.parse('"\\uFFFD"'))}]`);
console.log(`- "a\\ud800b".isWellFormed() -> ${lone.isWellFormed()}; toWellFormed() units [${units(lone.toWellFormed())}]`);
console.log(`- new TextEncoder().encode("a\\ud800b") -> ${Buffer.from(new TextEncoder().encode(lone)).toString("hex")}`);
console.log(`- new TextDecoder().decode(ED A0 80) -> units [${units(new TextDecoder().decode(Uint8Array.of(0xed, 0xa0, 0x80)))}]`);
try {
  new TextDecoder("utf-8", { fatal: true }).decode(Uint8Array.of(0xed, 0xa0, 0x80));
} catch (e) {
  console.log(`- TextDecoder fatal on ED A0 80 -> THROWS ${e.constructor.name}`);
}
try {
  encodeURIComponent(lone);
} catch (e) {
  console.log(`- encodeURIComponent("a\\ud800b") -> THROWS ${e.constructor.name}: ${e.message}`);
}
const body = await new Response(JSON.stringify({ s: lone })).text();
console.log(`- fetch-style body of JSON.stringify({ s }) round-trips: ${JSON.parse(body).s === lone} (escape is ASCII)`);
const rawBody = await new Response(lone).text();
console.log(`- fetch-style body of the raw string -> units [${units(rawBody)}] (UTF-8 encoding replaced it)`);

console.log("\n## The C# lossless prototype's output");
const text = readFileSync(new URL("../results/surrogates.json", import.meta.url), "utf8");
const fromCs = JSON.parse(text);
const back = {};
for (const name of Object.keys(fromCs).filter((k) => !k.endsWith("Units"))) {
  const value = fromCs[name];
  const expected = fromCs[name + "Units"].map((u) => u.toString(16).padStart(4, "0")).join(" ");
  console.log(`- ${name}: JS units [${units(value)}] equal to C# original ${units(value) === expected}`);
  back[name] = value;
}
const json = JSON.stringify(back);
writeFileSync(new URL("../results/surrogates-from-js.json", import.meta.url), json);
console.log(`- wrote results/surrogates-from-js.json: ${json}`);
