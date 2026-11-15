// Type-checks bad/ with the given TypeScript package (typescript or typescript6) and compares the diagnostics with
// the [TSxxxx] codes in bad/bad.ts: each numbered comment names the codes its next line must produce, exactly.
// An unexpected diagnostic, a missing one, or a clean compile fails the check. Paths are relative to this script.
import { spawnSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const checkRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const compilerPackage = process.argv[2] ?? "typescript";
const tsc = join(checkRoot, "node_modules", compilerPackage, "bin", "tsc");

const sourceLines = readFileSync(join(checkRoot, "bad", "bad.ts"), "utf8").split(/\r?\n/);
const expected = new Map();
sourceLines.forEach((text, index) => {
  const tag = /^\s*\/\/ (\d+)\..*\[((?:TS\d+)(?:, TS\d+)*)\]\s*$/.exec(text);
  if (!tag) return;
  expected.set(index + 2, { mistake: tag[1], codes: (tag[2] ?? "").split(", ").sort() });
});

const version = spawnSync(process.execPath, [tsc, "--version"], { encoding: "utf8" }).stdout.trim();
const run = spawnSync(process.execPath, [tsc, "-p", "tsconfig.bad.json", "--pretty", "false"], { cwd: checkRoot, encoding: "utf8" });

const actual = new Map();
for (const match of run.stdout.matchAll(/^(.+?)\((\d+),\d+\): error (TS\d+):/gm)) {
  const line = match[1]?.replaceAll("\\", "/") === "bad/bad.ts" ? Number(match[2]) : -1;
  const codes = actual.get(line) ?? [];
  codes.push(match[3]);
  actual.set(line, codes);
}

let failed = run.status === 0;
if (failed) console.log("tsc reported no errors");

for (const [line, { mistake, codes }] of expected) {
  const got = (actual.get(line) ?? []).sort();
  const ok = got.join() === codes.join();
  failed ||= !ok;
  console.log(`${ok ? "ok  " : "FAIL"} mistake ${mistake} (line ${line}): expected ${codes.join(", ")}, got ${got.join(", ") || "none"}`);
}

for (const [line, codes] of actual) {
  if (expected.has(line)) continue;
  failed = true;
  console.log(`FAIL unexpected ${codes.join(", ")} at ${line < 0 ? "a file outside bad/bad.ts" : `line ${line}`}`);
}

console.log(`${compilerPackage} ${version}: ${failed ? "FAILED" : `all ${expected.size} mistakes rejected as expected`}`);
if (failed) {
  console.log(run.stdout);
  process.exit(1);
}
