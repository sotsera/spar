// Reads the probe logs of scenarios.sh (artifacts/webview2-profiles/m1b-hangs/logs/*.log) and prints the numbers that
// docs/evidence/m1b-hangs.md reports. Times are the app's own host clock ("+12345.6 ms") unless marked "probe".
// Usage: node summarize.mjs <logs folder>
import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";

const folder = process.argv[2];
const host = line => Number(/\+(\d+(?:\.\d+)?) ms:/.exec(line)?.[1] ?? NaN);
const probe = line => Number(line.slice(0, 10).trim().replace(",", "."));

for (const file of readdirSync(folder).filter(f => f.endsWith(".log")).sort()) {
  const lines = readFileSync(join(folder, file), "utf8").split(/\r?\n/);
  const at = text => lines.find(l => l.includes(text));
  const all = text => lines.filter(l => l.includes(text));
  const out = [];
  const hang = at("the page hangs from now") ?? at("the page is busy for");
  const alarm = at("The page is not responding");
  if (hang && alarm) out.push(`alarm ${((host(alarm) - host(hang)) / 1000).toFixed(1)} s after the hang began`);
  if (alarm) out.push(`reported ${/\(([\d.]+) s without/.exec(alarm)?.[1]} s without an answer`);
  const shown = all("hung-page dialog shown");
  if (shown.length > 1 && hang) out.push(`dialogs at ${shown.map(l => ((host(l) - host(hang)) / 1000).toFixed(1)).join(", ")} s after the hang`);
  const waits = all("hung-page dialog: Wait");
  if (waits.length > 0) {
    const gaps = waits.map((w, i) => shown[i + 1] ? ((host(shown[i + 1]) - host(w)) / 1000).toFixed(1) : null).filter(x => x);
    out.push(`after each Wait, asked again in ${gaps.join(", ")} s`);
  }
  const reload = at("Reload page, after");
  const ended = at("renderer processes ended");
  const ready = lines.filter(l => l.includes("reported ui-ready")).at(-1);
  if (reload && ready && host(ready) > host(reload)) out.push(`Reload: renderers ended ${(host(ended) - host(reload)).toFixed(1)} ms, ui-ready ${(host(ready) - host(reload)).toFixed(1)} ms after the choice (${/ended: (\d+)/.exec(ended)?.[1]} ended)`);
  const again = at("answered again after");
  if (again) out.push(`recovered: ${/after ([\d.]+ s)/.exec(again)?.[1]}`);
  const unresp = all("WebView2 reports");
  if (unresp.length) out.push(`RenderProcessUnresponsive x${unresp.length}${shown[0] ? `, ${((host(unresp[0]) - host(shown[0])) / 1000).toFixed(1)} s after the first dialog` : ""}`);
  const silent = at("did not answer the close request");
  const close = at("close requested");
  if (silent && close) out.push(`close: silent page found after ${(host(silent) - host(close)).toFixed(0)} ms`);
  const prompts = all("shutdown prompt shown");
  const closing = at("] closing at");
  if (prompts.length && closing) out.push(`shutdown prompts at ${prompts.map(l => ((host(l) - host(closing)) / 1000).toFixed(1)).join(", ")} s after the teardown began`);
  const stalls = all("UI thread stalled");
  if (stalls.length) out.push(`stalls: ${stalls.map(l => l.replace(/^.*?: /, "")).join(" | ")}`);
  const beat = at("Heartbeat:");
  if (beat) out.push(beat.replace(/^.*Heartbeat: /, "heartbeat: "));
  const hooks = all("hook: main");
  if (hooks.length) out.push(`hook: ${hooks.map(l => l.replace(/^.*hook: main /, "")).join(" | ")}`);
  const exit = lines.filter(l => / > exit \d+$/.test(l)).at(-1);
  const left = at("browser processes left");
  const result = at("RESULT");
  out.push(`${exit?.replace(/^.*> /, "") ?? "no exit"}; ${left?.replace(/^.*> /, "") ?? ""}; ${result?.replace(/^.*> /, "")}`);
  console.log(`${file}\n  ${out.join("\n  ")}`);
}
