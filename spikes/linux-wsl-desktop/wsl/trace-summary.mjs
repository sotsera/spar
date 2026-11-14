// Usage: node wsl/trace-summary.mjs <trace.jsonl>
// Summarises a procsample trace: host marks, process spawns, the web process's main-thread states over time, the
// files it mapped (grouped by 50 ms), and where its threads were blocked between its spawn and the host's load-started.
import { readFileSync } from "node:fs";

const records = readFileSync(process.argv[2], "utf8").trim().split("\n").map((l) => JSON.parse(l));
const out = (s) => console.log(s);
const marks = records.filter((r) => r.out && /^\[/.test(r.out) && /(window-created|view-created|load-started|ui-ready|closing)/.test(r.out));
out("host marks (sampler clock):");
for (const m of marks) out(`  ${m.t.toFixed(0).padStart(6)}  ${m.out.slice(0, 110)}`);
const spawns = records.filter((r) => r.spawn);
out("spawns:");
for (const s of spawns) out(`  ${s.t.toFixed(0).padStart(6)}  ${s.spawn} ${s.comm}  ${s.cmdline.slice(0, 120)}`);
const web = spawns.find((s) => /WebKitWebProces/.test(s.comm));
const loadStarted = records.find((r) => r.out && /load-started/.test(r.out))?.t ?? Infinity;
if (web) {
  out(`web process ${web.spawn}: main-thread transitions until load-started (${loadStarted.toFixed(0)}):`);
  for (const r of records.filter((r) => r.pid === web.spawn && r.tid === web.spawn && r.t <= loadStarted + 20)) {
    out(`  ${r.t.toFixed(0).padStart(6)}  ${r.state} wchan=${r.wchan} syscall=${r.syscall} cpu=${r.cpu}`);
  }
  out("web process mapped files (by 50 ms bucket):");
  const buckets = new Map();
  for (const r of records.filter((r) => r.pid === web.spawn && r.mapped)) {
    const b = Math.floor(r.t / 50) * 50;
    buckets.set(b, (buckets.get(b) ?? []).concat(r.mapped));
  }
  for (const [b, files] of buckets) out(`  ${String(b).padStart(6)}  ${files.length} files: ${files.map((f) => f.split("/").pop()).slice(0, 14).join(", ")}${files.length > 14 ? ", …" : ""}`);
  const threads = new Map();
  for (const r of records.filter((r) => r.pid === web.spawn && r.thread && r.t <= loadStarted)) threads.set(r.tid, r.thread);
  out(`web process threads seen before load-started: ${[...new Set(threads.values())].join(", ")}`);
}
const others = records.filter((r) => r.thread && r.t > (web?.t ?? 0) && r.t < loadStarted && r.state === "R" && r.pid !== web?.spawn);
out(`running samples in other processes during the gap: ${others.length} (${[...new Set(others.map((r) => r.comm + ":" + r.thread))].join(", ")})`);
