// Development session with the real desktop host: `dotnet watch -lp Desktop` on a Debug build that embeds no bundle,
// so the WebView2 page comes from the app-owned Vite server and the bridge is answered in-process.
// Measures Fast Refresh inside WebView2, a contract-changing restart, and whether Vite is reused.
// The window is offscreen; the app's self-test drives the page. Run from this folder: node dev-loop-desktop.mjs
import { spawn, spawnSync } from "node:child_process";
import { readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const here = (p) => fileURLToPath(new URL(p, import.meta.url));
const header = here("./React.App/frontend/src/Header.tsx");
const service = here("./React.App/NotesService.cs");
const originals = { [header]: readFileSync(header, "utf8"), [service]: readFileSync(service, "utf8") };
const results = {};
const log = [];
const since = (start) => Math.round(performance.now() - start);

async function waitForLog(pattern, after, timeout = 120000) {
  const deadline = performance.now() + timeout;
  while (performance.now() < deadline) {
    const hit = log.find((l) => l.at >= after && pattern.test(l.line));
    if (hit) return hit;
    await new Promise((r) => setTimeout(r, 20));
  }
  throw new Error(`timeout waiting for ${pattern}\n` + log.filter((l) => l.at >= after).map((l) => l.line).join("\n"));
}

// Arguments after "--" go to the app: offscreen window, self-test without the benchmark, then wait for the heading.
const watch = spawn("dotnet", ["watch", "-lp", "Desktop", "--non-interactive", "--", "--offscreen", "--selftest", "--no-bench", "--wait-heading", "My notes"],
  { cwd: here("./React.App"), stdio: ["pipe", "pipe", "pipe"] });
for (const stream of [watch.stdout, watch.stderr]) {
  stream.setEncoding("utf8");
  stream.on("data", (chunk) => chunk.split(/\r?\n/).filter(Boolean).forEach((line) => log.push({ at: performance.now(), line })));
}

try {
  let t = performance.now();
  results.firstDevServer = (await waitForLog(/SPAR_DEV_SERVER/, 0)).line;
  await waitForLog(/SPAR_UI_READY/, 0);
  results.watchStartToUiReady = since(t);
  results.journey = (await waitForLog(/SELFTEST journey/, 0)).line;

  // 1. Fast Refresh inside WebView2.
  await waitForLog(/SELFTEST waiting-heading/, 0);
  const edited = Date.now();
  writeFileSync(header, originals[header].replace("<h1>Notes</h1>", "<h1>My notes</h1>"));
  const seen = await waitForLog(/SELFTEST heading-seen/, 0, 30000);
  results.fastRefreshInWebView2Ms = Number(/epoch=(\d+)/.exec(seen.line)[1]) - edited;
  writeFileSync(header, originals[header]);
  await waitForLog(/browser-exited/, 0);

  // 2. Contract edit: dotnet watch rebuilds and relaunches the app; Vite should be reused.
  t = performance.now();
  writeFileSync(service, originals[service].replace(
    "    [SparCommand]\n    public Task<int> CountAsync()",
    "    [SparCommand]\n    public Task<string> PingAsync(NoteId id) => Task.FromResult(id.Id);\n\n    [SparCommand]\n    public Task<int> CountAsync()"));
  results.secondDevServer = (await waitForLog(/SPAR_DEV_SERVER/, t)).line;
  await waitForLog(/SPAR_UI_READY/, t);
  results.contractEditToUiReady = since(t);
  results.secondJourney = (await waitForLog(/SELFTEST journey/, t)).line;
  results.buildLines = log.filter((l) => l.at >= t && /vite build|SparDevTypeCheck|Build succeeded|error/.test(l.line)).map((l) => l.line).slice(0, 4);
} finally {
  writeFileSync(header, originals[header]);
  writeFileSync(service, originals[service]);
  spawnSync("taskkill", ["/pid", String(watch.pid), "/T", "/F"], { stdio: "ignore" });
  console.log(JSON.stringify(results, null, 2));
}
