// Medians and ranges of the load-time series.
//   node tools/load-summary.mjs <load log>
// Lines: "round=<n> name=<name> TIMING ... host-navigate->ready=<ms> ... page-dcl=<ms>&page-ready=<ms>&...".
// Round 0 (the profile's first run) is reported apart. A line without TIMING is counted as a failure.
import fs from "node:fs";

const lines = fs.readFileSync(process.argv[2], "utf8").trim().split(/\r?\n/);
const series = new Map();
const value = (line, key) => Number(new RegExp(`${key}=([0-9.]+)`).exec(line)?.[1] ?? NaN);
for (const line of lines) {
  const round = Number(/round=(\d+)/.exec(line)?.[1] ?? /run=(\d+)/.exec(line)?.[1]);
  const name = /name=(\S+)/.exec(line)?.[1] ?? "(all)";
  const entry = series.get(name) ?? { warm: [], first: [], failures: 0 };
  series.set(name, entry);
  if (!line.includes("TIMING")) {
    entry.failures++;
    continue;
  }
  const sample = { ready: value(line, "host-navigate->ready"), dcl: value(line, "page-dcl"), pageReady: value(line, "page-ready") };
  (round === 0 ? entry.first : entry.warm).push(sample);
}

const median = (xs) => {
  const s = [...xs].sort((a, b) => a - b);
  const m = s.length >> 1;
  return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2;
};
const stat = (xs) => `${median(xs).toFixed(1)} (${Math.min(...xs).toFixed(1)}–${Math.max(...xs).toFixed(1)})`;

console.log("| Name | Runs | Page: navigation start → DOMContentLoaded ms | Page: navigation start → ui-ready ms | Host: Navigate() → ready request ms | First run (cold profile): host ms | Failures |");
console.log("|---|---:|---:|---:|---:|---:|---:|");
for (const [name, entry] of series) {
  const w = entry.warm;
  console.log(`| ${name} | ${w.length} | ${stat(w.map((s) => s.dcl))} | ${stat(w.map((s) => s.pageReady))} | ${stat(w.map((s) => s.ready))} | ${entry.first.map((s) => s.ready.toFixed(1)).join(", ") || "—"} | ${entry.failures} |`);
}
