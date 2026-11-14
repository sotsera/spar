// Usage (from spikes/asset-packing): node scripts/host-summary.mjs <label>
// Summarises artifacts/asset-packing/<label>/host.log (Windows or Linux host runs): medians per build and decode mode.
import { readFileSync } from "node:fs";

const lines = readFileSync(`../../artifacts/asset-packing/${process.argv[2]}/host.log`, "utf8").split(/\r?\n/).filter(l => l.startsWith("RUN "));
const median = v => { const s = v.filter(x => !Number.isNaN(x)).sort((a, b) => a - b); return s.length ? s[Math.floor(s.length / 2)] : NaN; };
const range = v => { const s = v.filter(x => !Number.isNaN(x)); return s.length ? `${Math.min(...s)}–${Math.max(...s)}` : ""; };
const field = (line, key) => Number(line.match(new RegExp(` ${key.replace(/[>-]/g, m => "\\" + m)}=([0-9.]+)`))?.[1]);
const groups = new Map();
for (const line of lines) {
  const key = `${line.split(" ")[1]} ${line.match(/ decode=(\w+)/)[1]}`;
  groups.set(key, [...(groups.get(key) ?? []), line]);
}
const linux = lines.some(l => l.includes("app-pss-kb"));
console.log(`| Build | Decode | Runs | Ready | Navigate → ready ms (range) | ${linux ? "Load started → ready ms" : "Navigation completed ms"} | Asset handler ms | Requests | Decoded cache B | ${linux ? "App PSS KB" : "Host private KB"} |`);
console.log("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|");
for (const [key, runs] of groups) {
  const [combo, mode] = key.split(" ");
  const ready = runs.filter(l => !l.includes("TIMEOUT") && !Number.isNaN(field(l, "ui-ready")));
  const nav = ready.map(l => field(l, "navigate->ready"));
  const second = linux ? ready.map(l => field(l, "load-started->ready")) : ready.map(l => field(l, "navigation-completed"));
  console.log(`| ${combo} | ${mode} | ${runs.length} | ${ready.length} | ${median(nav).toFixed(1)} (${range(nav)}) | ${median(second).toFixed(1)} | ${median(runs.map(l => field(l, "asset-handler-ms"))).toFixed(2)} | ${median(runs.map(l => field(l, "requests")))} | ${median(runs.map(l => field(l, "cached")))} | ${median(runs.map(l => field(l, linux ? "app-pss-kb" : "host-private-kb")))} |`);
}
