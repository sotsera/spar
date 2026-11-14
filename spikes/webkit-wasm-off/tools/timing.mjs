// Medians of the timing runs (results/runs/timing-*.json) into results/timing.json.
// Usage, from the spike folder: node tools/timing.mjs
import { readdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";

const dir = join("results", "runs");
const groups = {};
for (const f of readdirSync(dir).filter((name) => name.startsWith("timing-"))) {
  const d = JSON.parse(readFileSync(join(dir, f), "utf8"));
  const group = f.replace(/-\d+\.json$/, "");
  (groups[group] ??= []).push({
    firstScript: d.hostMs.progress.find((p) => p[0] === "start")?.[1],
    domContentLoaded: d.report.timing.domContentLoadedMs,
    jsWork: Math.min(...d.report.jsWork.map((w) => w.ms)),
    wasm: d.report.initial.typeofWebAssembly,
  });
}
const median = (values) => [...values].sort((a, b) => a - b)[Math.floor(values.length / 2)];
const out = Object.entries(groups).map(([group, rows]) => ({
  group,
  runs: rows.length,
  typeofWebAssembly: [...new Set(rows.map((r) => r.wasm))].join(),
  hostStartToFirstPageScriptMs: { median: median(rows.map((r) => r.firstScript)), min: Math.min(...rows.map((r) => r.firstScript)), max: Math.max(...rows.map((r) => r.firstScript)) },
  domContentLoadedMedianMs: median(rows.map((r) => r.domContentLoaded)),
  jsWorkBestOf3MedianMs: median(rows.map((r) => r.jsWork)),
}));
writeFileSync(join("results", "timing.json"), JSON.stringify(out, null, 1) + "\n");
console.log(JSON.stringify(out, null, 1));
