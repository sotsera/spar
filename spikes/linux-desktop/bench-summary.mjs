// Usage: node bench-summary.mjs <log prefix>...   e.g. node bench-summary.mjs b-gtk3 b-gtk3sb b-gtk4
// Reads ../../artifacts/linux-desktop/<prefix>-<n>.log and prints one line per bench case: median (min–max) across runs.
import { readdirSync, readFileSync } from "node:fs";

const dir = "../../artifacts/linux-desktop";
const med = values => {
  const sorted = [...values].sort((a, b) => a - b);
  const mid = sorted[Math.floor(sorted.length / 2)];
  return sorted.length > 1 ? `${mid} (${sorted[0]}–${sorted.at(-1)})` : `${mid}`;
};
const round = value => Math.round(value * 100) / 100;

for (const prefix of process.argv.slice(2)) {
  const files = readdirSync(dir).filter(f => new RegExp(`^${prefix}-[0-9]+[.]log$`).test(f));
  const cases = new Map();
  const memory = [];
  for (const file of files) {
    for (const line of readFileSync(`${dir}/${file}`, "utf8").split("\n")) {
      const bench = /^SPAR_BENCH (\w+) (\{.*\}) host=(\{.*\})$/.exec(line);
      if (bench) {
        const [, name, json, hostJson] = bench;
        const result = JSON.parse(json);
        const host = JSON.parse(hostJson);
        const key = name + " " + ["transport", "size", "rows", "batch", "delayMs", "how", "rate"].filter(k => k in result).map(k => `${k}=${result[k]}`).join(" ");
        if (!cases.has(key)) cases.set(key, []);
        cases.get(key).push({ ...result, host });
      }

      const mem = /^SPAR_MEMORY after-bench app rss=([\d.]+) pss=([\d.]+).*WebKitWebProces\(\d+\) rss=([\d.]+) pss=([\d.]+).*total rss=([\d.]+) pss=([\d.]+)/.exec(line);
      if (mem) memory.push(mem.slice(1).map(Number));
    }
  }

  console.log(`\n## ${prefix} (${files.length} runs)`);
  for (const [key, runs] of cases) {
    const pick = f => med(runs.map(f));
    const first = runs[0];
    let text;
    if (key.startsWith("unary")) text = `mean ${pick(r => round(r.host.wallMs / (r.n + 10)))} ms (host wall / calls), page median ${pick(r => r.medianMs)} p95 ${pick(r => r.p95Ms)} ms`;
    else if (key.startsWith("stream")) text = `first row ${pick(r => r.firstRowMs)} ms, total ${pick(r => r.totalMs)} ms, ${pick(r => r.rowsPerSecond)} rows/s, correct ${runs.every(r => r.correct)}, long tasks ${pick(r => r.longTasks)}, max frame gap ${pick(r => r.maxFrameGapMs)} ms, backend WS Δ ${pick(r => r.backend.peakWorkingSetDeltaMB)} MB, evaluations ${pick(r => r.host.evaluations)}`;
    else if (key.startsWith("backpressure")) text = `produced after 2 s ${pick(r => r.producedAfter2s)}, ahead ${pick(r => r.aheadOfConsumer)}, stalled ${runs.every(r => r.producerStalled)}, close ${pick(r => r.closeMs)} ms ${first.closeOutcome}`;
    else if (key.startsWith("cancel")) text = `outcomes ${[...new Set(runs.map(r => r.outcome))].join("/")}, settled ${pick(r => r.settledMs)} ms, still running ${pick(r => r.workRunningAtOutcome)}, counter moved ${pick(r => r.counterMovedWithin300msAfterOutcome)}`;
    else if (key.startsWith("push")) text = `${pick(r => r.events)} events, ${pick(r => r.eventsPerSecond)}/s, dropped ${pick(r => r.dropped)}, accounted ${runs.every(r => r.accounted)}, p99 ${pick(r => r.latencyMs.p99)} ms, max ${pick(r => r.latencyMs.max)} ms, long tasks ${pick(r => r.longTasks)}`;
    else text = JSON.stringify(first);
    console.log(`${key}: ${text}`);
  }

  if (memory.length) console.log(`memory after bench (MB): app rss ${med(memory.map(m => m[0]))} pss ${med(memory.map(m => m[1]))}; web process rss ${med(memory.map(m => m[2]))} pss ${med(memory.map(m => m[3]))}; total rss ${med(memory.map(m => m[4]))} pss ${med(memory.map(m => m[5]))}`);
}
