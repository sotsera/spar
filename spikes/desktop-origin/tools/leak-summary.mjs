// Matches the listener's connections to the probe's tests by time.
//
//   node tools/leak-summary.mjs <host log> <listener log> <profile name>
//
// A connection belongs to a test when the listener saw it between the test's start and the next test's start.
// Connections from other processes (other apps using the same origin) are counted apart.
import fs from "node:fs";

const [hostLog, listenerLog, profile] = process.argv.slice(2);
const host = fs.readFileSync(hostLog, "utf8").split(/\r?\n/);
const navigate = Number(/NAVIGATE epoch=(\d+)/.exec(host.join("\n"))?.[1]);
const results = host.filter((line) => line.startsWith("RESULT ")).map((line) => JSON.parse(line.slice(7))).filter((r) => r.start);
const connections = fs.readFileSync(listenerLog, "utf8").trim().split(/\r?\n/).map((line) => JSON.parse(line)).filter((e) => e.kind === "connection");
const mine = connections.filter((c) => c.owner?.includes(profile));
const others = connections.filter((c) => !c.owner?.includes(profile));
const label = (c) => `${c.listener} ${c.sni ?? c.request ?? c.data}`;

const windows = [{ test: "(page load, before the first test)", start: navigate, outcome: "" }, ...results];
for (let i = 0; i < windows.length; i++) {
  const start = windows[i].start;
  const end = windows[i + 1]?.start ?? Infinity;
  const hits = mine.filter((c) => c.seen >= start && c.seen < end);
  const grouped = {};
  for (const hit of hits) grouped[label(hit)] = (grouped[label(hit)] ?? 0) + 1;
  const outcome = typeof windows[i].outcome === "string" ? windows[i].outcome : JSON.stringify(windows[i].outcome);
  console.log(`${windows[i].test} | ${outcome} | leaked connections: ${hits.length} ${hits.length ? JSON.stringify(grouped) : ""}`);
}

console.log(`connections from this profile: ${mine.length}; from other processes: ${others.length}` +
  (others.length ? ` (${[...new Set(others.map((c) => c.owner))].join("; ")})` : ""));
