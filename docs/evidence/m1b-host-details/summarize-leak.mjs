// Summarizes the leak probe's logs (docs/evidence/m1b-host-details.md): for each run, the handle counts of the probe
// and of each WebView2 process kind before the cycles, after cycle 1, after the last cycle, and the change from cycle 1
// to the last. Usage: node summarize-leak.mjs <log>...
import { readFileSync } from "node:fs";
import { basename } from "node:path";

const parse = line => {
  const [, label, probe, , processes] = line.split(" | ");
  const counts = { probe: Number(probe.replace("probe ", "")) };
  for (const part of (processes ?? "").split(", ").filter(Boolean)) {
    const [name, handles] = part.split(" ");
    counts[name.split("#")[0]] = Number(handles);
  }
  return { label: label.trim(), counts };
};

for (const file of process.argv.slice(2)) {
  const rows = readFileSync(file, "utf8").split(/\r?\n/).filter(line => /\| (before cycles|cycle \d+:)/.test(line)).map(parse);
  const before = rows[0];
  const first = rows[1];
  const last = rows.at(-1);
  const kinds = ["probe", "browser", "network", "gpu", "renderer", "storage"];
  const cell = kind => `${before.counts[kind]}/${first.counts[kind]}/${last.counts[kind]} (${last.counts[kind] - first.counts[kind] >= 0 ? "+" : ""}${last.counts[kind] - first.counts[kind]})`;
  console.log(`${basename(file, ".log")}: ${rows.length - 1} cycles; before/cycle 1/last (change): ${kinds.map(k => `${k} ${cell(k)}`).join(", ")}`);
}
