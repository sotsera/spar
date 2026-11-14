// Usage: node wsl/summarize.mjs <runs-dir> <label-prefix>
// Groups runs by label without the trailing round number and prints medians (min–max) in ms from window creation:
// mapped, view created, load started, ui-ready; and the page's second animation frame (ms after navigation start).
import { readdirSync, readFileSync, existsSync } from "node:fs";
import { join } from "node:path";

const [dir, prefix = ""] = process.argv.slice(2);
const groups = new Map();
for (const name of readdirSync(dir).sort()) {
  if (!name.startsWith(prefix) || /-prime$/.test(name)) continue;
  const file = join(dir, name, "timeline.json");
  if (!existsSync(file)) continue;
  const t = JSON.parse(readFileSync(file, "utf8"));
  const at = (re) => t.marks.find((m) => re.test(m.name))?.at;
  const base = t.windowCreatedMs;
  const rel = (v) => (v === undefined || v === null ? null : v - base);
  const row = {
    mapped: rel(at(/^window-mapped/)),
    view: rel(at(/^view-created/)),
    load: rel(at(/^load-started/)),
    ready: t.readyMs === null ? null : t.readyMs - base,
    raf2: t.ready?.secondRafAt ?? null,
    hasFocus: t.ready?.hasFocus,
  };
  const key = name.replace(/-\d+$/, "");
  if (!groups.has(key)) groups.set(key, []);
  groups.get(key).push(row);
}
const median = (xs) => { const v = xs.filter((x) => x !== null && x !== undefined).sort((a, b) => a - b); if (!v.length) return "—"; const m = v.length % 2 ? v[(v.length - 1) / 2] : (v[v.length / 2 - 1] + v[v.length / 2]) / 2; return `${Math.round(m)} (${Math.round(v[0])}–${Math.round(v.at(-1))})`; };
console.log("| Group | n | mapped | view created | load started | **ui-ready** | page 2nd rAF | hasFocus |");
console.log("|---|---:|---:|---:|---:|---:|---:|---|");
for (const [key, rows] of groups) {
  const f = (k) => median(rows.map((r) => r[k]));
  const focus = [...new Set(rows.map((r) => String(r.hasFocus)))].join("/");
  console.log(`| ${key} | ${rows.length} | ${f("mapped")} | ${f("view")} | ${f("load")} | **${f("ready")}** | ${f("raf2")} | ${focus} |`);
}
