// Quick look at one raw report (development aid): node tools/peek.cjs <file>
const r = require(require("node:path").resolve(process.argv[2]));
if (r.error) { console.log(r.error, r.stack); process.exit(1); }
console.log(JSON.stringify(r.details));
if (r.checks) console.log("checks pass:", r.checks.every(c => c.pass), r.checks.filter(c => !c.pass));
if (r.decodeOnly) console.log("decodeOnly ns/row", r.decodeOnly.map(x => x.toFixed(0)).join(" "));
const g = {};
for (const s of r.samples ?? []) (g[s.scenario + " " + s.combo] ??= []).push(s);
for (const [k, v] of Object.entries(g)) {
  const s = v[v.length - 1];
  console.log(k.padEnd(28), s.nsPerItem.toFixed(0).padStart(8), "ns | longest", s.longestTaskMs.toFixed(2), "busy", s.busyMs.toFixed(1), "tasks", s.tasks, "maxBuf", s.maxBuffered, "gc", s.gcCount ?? "-", "lt", s.longTasks ?? "-", s.longTaskMaxMs ?? "", "peak", s.peakHeapGrowth ?? "-", "ms", s.ms.toFixed(0), "ticks", s.ticks ?? "", "cpu", s.cpuMs ?? "");
}
for (const x of r.retained ?? []) console.log("retained", x.combo, x.buffered, x.bytesPerItem?.toFixed(0) ?? "", x.rssPerItem?.toFixed(0) ?? "");
for (const x of r.alloc ?? []) console.log("alloc", x.combo, x.bytesPerItem.toFixed(0), "gc", x.gcInside);
