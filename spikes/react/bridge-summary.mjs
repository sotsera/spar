// Summarises ../../artifacts/react/bridge-bench.jsonl (written by the desktop self-test and BridgeTests) as Markdown tables.
// Usage: node bridge-summary.mjs [file]
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const file = process.argv[2] ?? fileURLToPath(new URL("../../artifacts/react/bridge-bench.jsonl", import.meta.url));
const lines = readFileSync(file, "utf8").split("\n").filter(Boolean).map(l => JSON.parse(l));

const groupBy = (items, key) => items.reduce((all, item) => {
  const k = key(item);
  (all[k] ??= []).push(item);
  return all;
}, {});
const range = values => {
  const sorted = [...values].sort((a, b) => a - b);
  const median = sorted[Math.floor(sorted.length / 2)];
  return sorted.length === 1 ? `${median}` : `${median} (${sorted[0]}–${sorted.at(-1)})`;
};

console.log("### Streams (100,000 rows)\n");
console.log("| Host | Transport | Batch | n | First row ms | Total ms | Rows/s | Page heap peak Δ MB | Long tasks (ms) | Max frame gap ms | Page main-thread task ms | Backend peak WS Δ MB | Backend CPU ms |");
console.log("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
const streams = lines.filter(l => l.kind === "stream" && l.result.rows === 100000 && !l.result.delayMs);
for (const [key, runs] of Object.entries(groupBy(streams, l => `${l.host}|${l.result.transport}|${l.result.batch}`))) {
  const [host, transport, batch] = key.split("|");
  const r = runs.map(x => x.result);
  console.log(`| ${host} | ${transport} | ${batch} | ${runs.length} | ${range(r.map(x => x.firstRowMs))} | ${range(r.map(x => Math.round(x.totalMs)))} | ${range(r.map(x => x.rowsPerSecond))} | ${range(r.map(x => x.heapPeakDeltaMB))} | ${range(r.map(x => x.longTasks))} (${range(r.map(x => Math.round(x.longTaskMs)))}) | ${range(r.map(x => x.maxFrameGapMs))} | ${range(runs.map(x => x.page?.taskMs ?? 0))} | ${range(r.map(x => x.backend.peakWorkingSetDeltaMB))} | ${range(r.map(x => Math.round(x.backend.cpuMs)))} |`);
}

const print = (title, kind, columns) => {
  const rows = lines.filter(l => l.kind === kind);
  if (rows.length === 0) return;
  console.log(`\n### ${title}\n`);
  console.log(`| Host | ${columns.map(c => c[0]).join(" | ")} |`);
  console.log(`|---|${columns.map(() => "---").join("|")}|`);
  for (const l of rows) console.log(`| ${l.host} | ${columns.map(c => String(c[1](l.result, l))).join(" | ")} |`);
};

print("Stream that keeps every row", "stream-keep", [["Transport", r => r.transport], ["Batch", r => r.batch], ["Total ms", r => r.totalMs], ["Page heap peak Δ MB", r => r.heapPeakDeltaMB], ["Heap after (CDP) MB", (r, l) => l.page?.heapUsedMB], ["Long tasks", r => r.longTasks]]);
print("Back-pressure (consumer stops after 5,000 rows)", "backpressure", [["Transport", r => r.transport], ["Batch", r => r.batch], ["Produced after 1 s", r => r.producedAfter1s], ["after 2 s", r => r.producedAfter2s], ["Ahead of consumer", r => r.aheadOfConsumer], ["Stalled", r => r.producerStalled], ["Close (cancel) ms", r => r.closeMs]]);

console.log("\n### Cancellation\n");
console.log("| Host | Kind | Transport | How | Honors token | n | Outcome | Page abort → settled ms | Counter moved after outcome | Work running at outcome |");
console.log("|---|---|---|---|---|---:|---|---:|---:|---:|");
const cancels = lines.filter(l => l.kind.startsWith("cancel-"));
for (const [key, runs] of Object.entries(groupBy(cancels, l => `${l.host}|${l.kind}|${l.result.transport}|${l.result.how ?? "abort"}|${l.result.honor}|${l.result.tickMs ?? 1}`))) {
  const [host, kind, transport, how, honor, tick] = key.split("|");
  const r = runs.map(x => x.result);
  const outcomes = [...new Set(r.map(x => x.outcome))].join(", ");
  console.log(`| ${host} | ${kind}${tick !== "1" ? ` (tick ${tick} ms)` : ""} | ${transport} | ${how} | ${honor} | ${runs.length} | ${outcomes} | ${range(r.map(x => x.settledMs))} | ${range(r.map(x => x.counterMovedWithin300msAfterOutcome))} | ${range(r.map(x => x.workRunningAtOutcome))} |`);
}

console.log("\n### Push\n");
console.log("| Host | Transport | Rate/s | Events | Gaps (dropped) | Accounted | p50 ms | p95 ms | p99 ms | max ms | Long tasks | Page task ms / wall ms | Backend CPU ms | Backend peak WS Δ MB |");
console.log("|---|---|---:|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|");
for (const l of lines.filter(l => l.kind === "push" || l.kind === "push-slow")) {
  const r = l.result;
  console.log(`| ${l.host} | ${r.transport}${r.slowConsumerMs ? " (slow consumer)" : ""} | ${r.rate} | ${r.events} | ${r.gaps} (${r.dropped}) | ${r.accounted} | ${r.latencyMs.p50} | ${r.latencyMs.p95} | ${r.latencyMs.p99} | ${r.latencyMs.max} | ${r.longTasks} | ${l.page ? `${l.page.taskMs} / ${l.page.wallMs}` : "—"} | ${Math.round(r.backend.cpuMs)} | ${r.backend.peakWorkingSetDeltaMB} |`);
}

print("Unary calls through the SDK", "unary", [["Transport", r => r.transport], ["Size", r => r.size], ["n", r => r.n], ["Median ms", r => r.medianMs], ["p95 ms", r => r.p95Ms]]);
print("Six open subscriptions, then a unary call", "connection-limit", [["Transport", r => r.transport], ["Unary", r => r.unary], ["Unary ms", r => r.unaryMs], ["Subscription outcomes", r => JSON.stringify(r.subscriptionOutcomes)]]);
for (const l of lines.filter(l => l.kind === "reload" || l.kind === "resubscribe")) console.log(`\n${l.kind} (${l.host}): ${JSON.stringify(l.result)}${l.killToResumedMs ? ` killToBackendReadyMs=${l.killToBackendReadyMs} killToResumedMs=${l.killToResumedMs}` : ""}`);
