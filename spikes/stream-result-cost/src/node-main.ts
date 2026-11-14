// Node runner. Usage: node --expose-gc obj/bundle/bench-node.mjs [--quick] [--alloc] [--only a,b] --out <file>
// --alloc: allocation per item only; run it with --min-semi-space-size=64 so no scavenge falls inside a measurement.
import { writeFileSync } from "node:fs";
import { runAll, runAllocation, type Host } from "./bench.js";
import { closeChannel } from "./wire.js";

const args = process.argv.slice(2);
const option = (name: string) => (args.includes(name) ? args[args.indexOf(name) + 1] : undefined);
const quick = args.includes("--quick");
const only = option("--only")?.split(",");
const out = option("--out");

const entries: { type: string; start: number; duration: number }[] = [];
new PerformanceObserver((list: PerformanceObserverEntryList) => {
  for (const e of list.getEntries()) entries.push({ type: e.entryType, start: e.startTime, duration: e.duration });
}).observe({ entryTypes: ["gc"] });

const channel = new MessageChannel();
const waiting: (() => void)[] = [];
channel.port1.onmessage = () => waiting.shift()!();
const g = globalThis as unknown as { gc?: () => void };

const host: Host = {
  engine: "node",
  details: { node: process.version, v8: process.versions.v8, arch: process.arch, execArgv: process.execArgv, gc: typeof g.gc === "function" },
  now: () => performance.now(),
  nextTask: () => new Promise(resolve => { waiting.push(resolve); channel.port2.postMessage(0); }),
  timer: ms => new Promise(resolve => setTimeout(resolve, ms)),
  heapUsed: () => process.memoryUsage().heapUsed,
  gc: g.gc,
  entries: () => entries,
  log: line => console.error(line),
};

const report = args.includes("--alloc")
  ? await runAllocation(host, quick ? 3 : 15, only)
  : await runAll(host, quick
    ? { rounds100k: 2, rounds1M: 1, roundsSlow: 1, pushSeconds: 1, pushRate: 20_000, retainedReps: 1, only }
    : { rounds100k: 7, rounds1M: 3, roundsSlow: 5, pushSeconds: 3, pushRate: 20_000, retainedReps: 3, only });
const text = JSON.stringify(report, null, 1);
if (out) writeFileSync(out, text);
else console.log(text);
channel.port1.close();
closeChannel();
