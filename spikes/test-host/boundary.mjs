// Production boundary check: the published app must not contain the test host's hook or Spar's test fakes.
// Usage: node boundary.mjs <SampleApp executable> [<executable built with -p:SparTestHostSupported=true>]
// NativeAOT keeps type names as UTF-8 metadata and string literals as UTF-16, so both encodings are searched.
import { readFileSync } from "node:fs";

const mustBeAbsent = ["SparTestSession", "FakeClipboard", "MemorySparLog", "SparExternalGuard"];
const mustBePresent = ["SystemClipboard", "No contracts were registered"];

function contains(binary, text) {
  return [Buffer.from(text, "utf8"), Buffer.from(text, "utf16le")].some(needle => binary.indexOf(needle) >= 0);
}

let failures = 0;
const [production, switchOn] = process.argv.slice(2);
const binary = readFileSync(production);
for (const text of mustBeAbsent) {
  const found = contains(binary, text);
  failures += found ? 1 : 0;
  console.log(`${found ? "FAIL" : "ok  "} production lacks ${text}`);
}

for (const text of mustBePresent) {
  const found = contains(binary, text);
  failures += found ? 0 : 1;
  console.log(`${found ? "ok  " : "FAIL"} production has ${text} (control)`);
}

if (switchOn) {
  const on = readFileSync(switchOn);
  console.log(`${contains(on, "SparTestSession") ? "ok  " : "FAIL"} switch-on build has SparTestSession (control)`);
}

console.log(`size production=${binary.length}`);
process.exit(failures === 0 ? 0 : 1);
