// Summarises the self-test logs: UI ready and bench means per mode and transport.
import { readFileSync } from "node:fs";
const lines = readFileSync(process.argv[2], "utf8").split(/\r?\n/);
let mode = "";
const ui = {}, bench = {}, flush = {}, exports = {};
for (const line of lines) {
  const header = line.match(/^=== (.+) run \d+/);
  if (header) { mode = header[1]; continue; }
  const ready = line.match(/\]\s*ui-ready|SPAR_MARK ui-ready \(fetch \/_spar\/ready\) ([\d.]+)/);
  const readyWin = line.match(/\[\s*([\d.]+) ms\] ui-ready/);
  if (readyWin) (ui[mode] ??= []).push(Number(readyWin[1]));
  else if (ready?.[1]) (ui[mode] ??= []).push(Number(ready[1]));
  const b = line.match(/bench round=\d+ transport=(\w+) tracing=([\w-]+) "?(\{.*\})"?$/);
  if (b) {
    const result = JSON.parse(b[3].replaceAll('\\"', '"'));
    const key = `${mode} | ${b[1]} | tracing=${b[2]}`;
    (bench[key] ??= []).push(result.meanMs);
  }
  const f = line.match(/flush (?:ok=\w+ )?ms=([\d.]+)/);
  if (f) (flush[mode] ??= []).push(Number(f[1]));
  if (/SPAR_OTLP \/_spar\/otlp\/v1/.test(line)) exports[mode] = (exports[mode] ?? 0) + 1;
}
const range = xs => `${Math.min(...xs).toFixed(1)}-${Math.max(...xs).toFixed(1)}`;
const mean = xs => (xs.reduce((a, b) => a + b, 0) / xs.length).toFixed(3);
for (const [k, v] of Object.entries(ui)) console.log(`ui-ready ${k}: ${range(v)} ms (n=${v.length})`);
for (const [k, v] of Object.entries(bench)) console.log(`call mean ${k}: avg ${mean(v)} ms, range ${Math.min(...v)}-${Math.max(...v)} (n=${v.length} x 300 calls)`);
for (const [k, v] of Object.entries(flush)) console.log(`flush ${k}: ${range(v)} ms`);
for (const [k, v] of Object.entries(exports)) console.log(`relayed export requests ${k}: ${v}`);
