// Development-session experiment, app-owned dev server. `dotnet watch -lp Loopback` is the only command: the backend
// starts (or reuses) Vite, and watch builds skip the production bundle (DotNetWatchBuild). One headless page stays open.
// Measures start-up, React Fast Refresh, C# Hot Reload, a contract-changing restart, and how long Vite outlives the
// session. Compare with dev-loop.mjs (Vite as a sibling, full builds). Run from this folder: node dev-loop-owned.mjs
import { spawn, spawnSync } from "node:child_process";
import { readFileSync, writeFileSync, existsSync, rmSync } from "node:fs";
import { fileURLToPath } from "node:url";
const { chromium } = await import(new URL("../loopback/vite-dev/node_modules/playwright-core/index.mjs", import.meta.url).href);

const here = (p) => fileURLToPath(new URL(p, import.meta.url));
const appDir = here("./React.App");
const sessionFile = here("./React.App/obj/spar-dev-session.json");
const stateFile = here("./React.App/obj/spar-dev-server.json");
const header = here("./React.App/frontend/src/Header.tsx");
const service = here("./React.App/NotesService.cs");
const originals = { [header]: readFileSync(header, "utf8"), [service]: readFileSync(service, "utf8") };
const results = {};
const log = [];
const since = (start) => Math.round(performance.now() - start);
const alive = (pid) => { try { process.kill(pid, 0); return true; } catch { return false; } };

function start(command, args, cwd, name, env = {}) {
  // No intermediate shell: the pid is the tool itself, so stopping its tree reaches everything it started.
  const child = spawn(command, args, { cwd, stdio: ["pipe", "pipe", "pipe"], env: { ...process.env, ...env } });
  for (const stream of [child.stdout, child.stderr]) {
    stream.setEncoding("utf8");
    stream.on("data", (chunk) => chunk.split(/\r?\n/).filter(Boolean).forEach((line) => log.push({ at: performance.now(), name, line })));
  }
  return child;
}

async function waitForLog(name, pattern, after, timeout = 60000) {
  const deadline = performance.now() + timeout;
  while (performance.now() < deadline) {
    const hit = log.find((l) => l.name === name && l.at >= after && pattern.test(l.line));
    if (hit) return hit;
    await new Promise((r) => setTimeout(r, 25));
  }
  throw new Error(`timeout waiting for ${name} ${pattern}\n` + log.filter((l) => l.at >= after).map((l) => `${l.name}: ${l.line}`).join("\n"));
}

for (const file of [sessionFile, stateFile]) if (existsSync(file)) rmSync(file);
const watch = start("dotnet", ["watch", "-lp", "Loopback", "--non-interactive", ...(process.argv.includes("--full") ? [] : ["--property:SparDevServerBuild=true"])], appDir, "watch", { SPAR_DEV_SERVER_IDLE_MS: "15000" });
let browser;
let vitePid;
try {
  let t = performance.now();
  await waitForLog("watch", /SPAR_READY/, 0, 180000);
  results.watchStartToBackendReady = since(t);
  const devServer = await waitForLog("watch", /SPAR_DEV_SERVER url=/, 0, 60000);
  results.watchStartToDevServer = since(t);
  results.devServerLine = devServer.line;
  const devUrl = /url=(\S+)/.exec(devServer.line)[1];
  vitePid = JSON.parse(readFileSync(stateFile, "utf8")).pid;

  browser = await chromium.launch({ headless: true });
  const page = await browser.newPage();
  page.on("response", (r) => r.url().includes("/_spar/") && log.push({ at: performance.now(), name: "page", line: `bridge ${r.status()} ${r.url()}` }));
  page.on("framenavigated", (f) => f === page.mainFrame() && log.push({ at: performance.now(), name: "page", line: `navigated ${f.url()}` }));
  t = performance.now();
  await page.goto(devUrl); // no token in the URL: the dev server's proxy adds it
  await page.getByLabel("Note count").filter({ hasText: "0 notes" }).waitFor();
  results.devPageToReady = since(t);

  await page.getByRole("textbox", { name: "Note" }).fill("Buy milk");
  await page.getByRole("button", { name: "Save" }).click();
  await page.getByRole("status").filter({ hasText: "Saved version 1" }).waitFor();
  await page.getByRole("searchbox", { name: "Search" }).fill("milk");
  await page.getByRole("listitem").filter({ hasText: "Buy milk" }).waitFor();

  // 1. React Fast Refresh.
  t = performance.now();
  writeFileSync(header, originals[header].replace("<h1>Notes</h1>", "<h1>My notes</h1>"));
  await page.getByRole("heading", { name: "My notes" }).waitFor({ timeout: 15000 });
  results.fastRefreshEditToVisible = since(t);
  results.fastRefreshKeptSearchText = (await page.getByRole("searchbox", { name: "Search" }).inputValue()) === "milk";

  // 2. C# method body: Hot Reload.
  t = performance.now();
  writeFileSync(service, originals[service].replace("return Task.FromResult(_notes.Count);", "return Task.FromResult(_notes.Count + 100);"));
  const applied = await waitForLog("watch", /changes applied|Restart is needed/, t);
  results.hotReloadEditToApplied = since(t);
  results.hotReloadOutcome = applied.line.includes("applied") ? "applied in place" : "restart";
  await page.getByRole("searchbox", { name: "Search" }).fill("");
  await page.getByLabel("Note count").filter({ hasText: "101 notes" }).waitFor({ timeout: 15000 });

  // 3. Contract edit: watch restarts the backend; Vite must be reused and the open page must keep working.
  t = performance.now();
  writeFileSync(service, readFileSync(service, "utf8").replace(
    "    [SparCommand]\n    public Task<int> CountAsync()",
    "    [SparCommand]\n    public Task<string> PingAsync(NoteId id) => Task.FromResult(id.Id);\n\n    [SparCommand]\n    public Task<int> CountAsync()"));
  await waitForLog("watch", /SPAR_READY/, t, 120000);
  results.contractEditToBackendReady = since(t);
  const reused = await waitForLog("watch", /SPAR_DEV_SERVER/, t, 30000);
  results.devServerAfterRestart = reused.line;
  results.devServerSamePid = JSON.parse(readFileSync(stateFile, "utf8")).pid === vitePid;

  await page.getByRole("textbox", { name: "Note" }).fill("After restart");
  const clicked = performance.now();
  const [saveResponse] = await Promise.all([
    page.waitForResponse((r) => r.url().includes("/_spar/call/notes/save"), { timeout: 15000 }),
    page.getByRole("button", { name: "Save" }).click(),
  ]);
  await page.getByRole("listitem").filter({ hasText: "After restart" }).waitFor({ timeout: 15000 }).catch(() => {});
  results.openPageSaveAfterContractEdit = `HTTP ${saveResponse.status()}, status "${await page.getByRole("status").textContent()}", ` +
    `list shows it: ${await page.getByRole("listitem").filter({ hasText: "After restart" }).count() === 1} (${since(clicked)} ms)`;
  results.pageEventsAfterContractEdit = log.filter((l) => l.name === "page" && l.at >= t).map((l) => `${Math.round(l.at - t)}ms ${l.line}`);
  results.watchBuildLines = log.filter((l) => l.name === "watch" && /SparDevTypeCheck|vite build|Building|Build succeeded|dotnet watch/.test(l.line)).slice(-6).map((l) => l.line);
} finally {
  writeFileSync(header, originals[header]);
  writeFileSync(service, originals[service]);
  await browser?.close();
  const stopped = performance.now();
  if (process.platform === "win32") spawnSync("taskkill", ["/pid", String(watch.pid), "/T", "/F"], { stdio: "ignore" });
  else watch.kill();

  // The backend this session started must be gone too; record whether killing the watch tree took it down.
  const backendPid = existsSync(sessionFile) ? JSON.parse(readFileSync(sessionFile, "utf8")).pid : undefined;
  await new Promise((r) => setTimeout(r, 1000));
  results.backendAliveAfterWatchKilled = backendPid !== undefined && alive(backendPid);
  if (results.backendAliveAfterWatchKilled) process.kill(backendPid);
  if (vitePid) {
    while (alive(vitePid) && performance.now() - stopped < 40000) await new Promise((r) => setTimeout(r, 250));
    results.viteExitAfterWatchStopped = alive(vitePid) ? "still running after 40 s (killed)" : `${since(stopped)} ms`;
    if (alive(vitePid)) process.kill(vitePid);
  }
  console.log(JSON.stringify(results, null, 2));
}
