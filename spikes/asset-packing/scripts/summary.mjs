// Usage (from spikes/asset-packing): node scripts/summary.mjs <label>
// Summarises artifacts/asset-packing/<label>/*.log from run-bench.sh: medians across runs.
import { readFileSync, readdirSync } from "node:fs";

const dir = `../../artifacts/asset-packing/${process.argv[2]}`;
const median = values => { const s = [...values].sort((a, b) => a - b); return s.length ? s[Math.floor(s.length / 2)] : NaN; };
const range = values => values.length ? `${Math.min(...values)}–${Math.max(...values)}` : "";
const field = (line, key) => Number(line.match(new RegExp(`(?:^| )${key}=([0-9.]+)`))?.[1]);
const rows = [];
let baseSize = 0, baseStartup = 0, basePeak = 0;
for (const file of readdirSync(dir).filter(f => f.endsWith(".log")).sort((a, b) => a.startsWith("none") ? -1 : b.startsWith("none") ? 1 : a.localeCompare(b))) {
  const lines = readFileSync(`${dir}/${file}`, "utf8").split(/\r?\n/);
  const combo = file.replace(".log", "");
  const pick = (prefix, key) => lines.filter(l => l.includes(prefix)).map(l => field(l, key)).filter(v => !Number.isNaN(v));
  const size = Number(lines.find(l => l.startsWith("SIZE")).split(" ")[2]);
  const eager = lines.filter(l => l.includes("mode=eager"));
  const lazy = lines.filter(l => l.includes("mode=lazy"));
  const startup = lines.filter(l => l.startsWith("STARTUP")).map(l => Number(l.match(/ (\d+)us/)[1]) / 1000);
  const r = {
    combo, size,
    original: field(eager[0] ?? "", "original"), stored: field(eager[0] ?? "", "stored"),
    open: median(eager.map(l => field(l, "open-ms"))),
    eagerMs: median(eager.map(l => field(l, "decode-all-ms"))), eagerRange: range(eager.map(l => field(l, "decode-all-ms"))),
    lazyMs: median(lazy.map(l => field(l, "first-requests-ms"))),
    hitMs: median(lazy.map(l => field(l, "cached-requests-ms"))),
    warmTotal: pick("total-median-ms", "total-median-ms")[0],
    peak: median(eager.map(l => field(l, "peak-ws-kb"))), priv: median(eager.map(l => field(l, "private-kb"))),
    lazyPeak: median(lazy.map(l => field(l, "peak-ws-kb"))), alloc: median(eager.map(l => field(l, "allocated-kb"))),
    startup: median(startup), startupRange: range(startup.map(v => Math.round(v * 10) / 10)),
  };
  if (combo.startsWith("none")) { baseSize = size; baseStartup = r.startup; basePeak = r.peak; }
  rows.push(r);
  for (const a of lines.filter(l => l.startsWith("ASSET"))) console.log(`${combo} ${a}`);
}
console.log("\n| Build | Exe bytes | Δ vs none | Original | Stored | Open ms | Eager decode ms (range) | Lazy first ms | Cache hit ms | Warm decode ms | Peak WS KB (Δ) | Private KB | Allocated KB | Startup ms (Δ) |");
console.log("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
for (const r of rows) {
  console.log(`| ${r.combo} | ${r.size.toLocaleString("en")} | ${(r.size - baseSize).toLocaleString("en")} | ${r.original?.toLocaleString("en") ?? ""} | ${r.stored?.toLocaleString("en") ?? ""} | ${r.open?.toFixed(2)} | ${r.eagerMs?.toFixed(2)} (${r.eagerRange}) | ${r.lazyMs?.toFixed(2)} | ${r.hitMs?.toFixed(3)} | ${r.warmTotal?.toFixed(2) ?? ""} | ${r.peak} (${r.peak - basePeak}) | ${r.priv} | ${r.alloc} | ${r.startup.toFixed(1)} (${(r.startup - baseStartup).toFixed(1)}) [${r.startupRange}] |`);
}
