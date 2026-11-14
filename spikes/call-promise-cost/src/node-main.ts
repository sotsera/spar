// Node (V8) runner. Usage: node --expose-gc obj/bundle/bench-node.mjs [--quick | --order] [--out <file>]
import { runAll, runOrderChecks, type Host } from "./bench.js";

// Minimal declarations: the spike type-checks without @types/node.
declare const process: {
  argv: string[];
  versions: Record<string, string>;
  arch: string;
  platform: string;
  memoryUsage(): { heapUsed: number };
  on(event: "unhandledRejection", listener: () => void): void;
};
declare function setImmediate(callback: () => void): void;
declare const gc: (() => void) | undefined;

const args = process.argv.slice(2);
const quick = args.includes("--quick");
const outIndex = args.indexOf("--out");
const out = outIndex >= 0 ? args[outIndex + 1] : undefined;

const host: Host = {
  engine: "node",
  details: { node: process.versions.node, v8: process.versions.v8, arch: process.arch, platform: process.platform, exposeGc: typeof gc === "function" },
  now: () => performance.now(),
  nextTask: () => new Promise(resolve => setImmediate(resolve)),
  settleEvents: () => new Promise(resolve => setTimeout(resolve, 20)),
  heapUsed: () => process.memoryUsage().heapUsed,
  gc: typeof gc === "function" ? gc : undefined,
  onUnhandledRejection: listener => process.on("unhandledRejection", listener),
  log: line => console.error(`[node] ${line}`),
};

const report = args.includes("--order") ? await runOrderChecks(host) : await runAll(host, quick
  ? { sampleMs: 10, warmupRounds: 1, rounds: 3, burstSize: 10_000, burstRounds: 2, retainedCount: 20_000 }
  : { sampleMs: 60, warmupRounds: 3, rounds: 7, burstSize: 10_000, burstRounds: 15, retainedCount: 20_000 });
const json = JSON.stringify(report, null, 1);
if (out) {
  const { writeFileSync } = await import("node:fs" as string) as { writeFileSync(path: string, data: string): void };
  writeFileSync(out, json);
  console.error(`[node] wrote ${out}`);
} else {
  console.log(json);
}
