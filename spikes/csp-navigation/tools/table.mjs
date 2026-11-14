// Usage: node tools/table.mjs <run label> [more labels...]
// Prints one markdown table per run label: the page results of that run on every engine that has a log for it
// (WebView2, Chromium loopback, WebKitGTK 4.1 and 6.0), plus counts of host-side events.
import { existsSync, readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "../../../artifacts/csp-navigation");
const engines = [["WebView2", "windows"], ["Chromium loopback", "loopback"], ["WebKitGTK 4.1", "linux/gtk3"], ["WebKitGTK 6.0", "linux/gtk4"]];
const short = value => String(value).replace(/\|/g, "/").replace(/(Evaluating|Refused|Compiling|Failed to).*$/, "$1…").slice(0, 60);

for (const label of process.argv.slice(2)) {
  const columns = [];
  for (const [name, folder] of engines) {
    const file = join(root, folder, `${label}.log`);
    if (!existsSync(file)) continue;
    const lines = readFileSync(file, "utf8").split(/\r?\n/);
    const result = lines.map(l => l.match(/\] PAGE (\{"kind":"result".*)$/)).find(Boolean);
    const counts = {};
    for (const line of lines) {
      const event = line.match(/\] (EXT_HIT|NAV_BLOCKED|NEW_WINDOW_BLOCKED|PERMISSION_DENIED|REPORT|FOREIGN_DOCUMENT_INTERCEPTED)\b/);
      if (event) counts[event[1]] = (counts[event[1]] ?? 0) + 1;
    }
    columns.push({ name, results: result ? JSON.parse(result[1]).results : {}, counts });
  }

  console.log(`\n### ${label}\n`);
  console.log(`| probe | ${columns.map(c => c.name).join(" | ")} |`);
  console.log(`|---|${columns.map(() => "---").join("|")}|`);
  const keys = [...new Set(columns.flatMap(c => Object.keys(c.results)))];
  for (const key of keys) {
    if (key === "sparAtModule" || key === "hostSettings") continue;
    console.log(`| ${key} | ${columns.map(c => short(c.results[key] ?? "-")).join(" | ")} |`);
  }
  const events = [...new Set(columns.flatMap(c => Object.keys(c.counts)))];
  for (const event of events) console.log(`| host: ${event} | ${columns.map(c => c.counts[event] ?? 0).join(" | ")} |`);
}
