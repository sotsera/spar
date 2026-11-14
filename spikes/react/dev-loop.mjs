// Development-session experiment: dotnet watch (Loopback profile, dev-session file) + Vite as a sibling dev server
// + one headless page kept open throughout. Measures React Fast Refresh, C# Hot Reload and a contract-changing
// restart, and checks the page keeps one URL and token. Run from this folder: node dev-loop.mjs
import { spawn } from "node:child_process";
import { readFileSync, writeFileSync, existsSync, rmSync } from "node:fs";
import { fileURLToPath } from "node:url";
const { chromium } = await import(new URL("../loopback/vite-dev/node_modules/playwright-core/index.mjs", import.meta.url).href);

const here = (p) => fileURLToPath(new URL(p, import.meta.url));
const appDir = here("./React.App");
const sessionFile = here("./React.App/obj/spar-dev-session.json");
const header = here("./React.App/frontend/src/Header.tsx");
const service = here("./React.App/NotesService.cs");
const originals = { [header]: readFileSync(header, "utf8"), [service]: readFileSync(service, "utf8") };
const results = {};
const log = [];
const since = (start) => Math.round(performance.now() - start);

function start(command, args, cwd, name) {
  const child = spawn(command, args, { cwd, shell: process.platform === "win32", stdio: ["pipe", "pipe", "pipe"] });
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

if (existsSync(sessionFile)) rmSync(sessionFile);
const watch = start("dotnet", ["watch", "-lp", "Loopback", "--non-interactive"], appDir, "watch");
let vite;
let browser;
try {
  let t = performance.now();
  const firstReady = await waitForLog("watch", /SPAR_READY/, 0, 180000);
  results.watchStartToReady = since(t);
  const session = JSON.parse(readFileSync(sessionFile, "utf8"));

  vite = start("npx", ["vite", "--port", "5173", "--strictPort"], here("./React.App/frontend"), "vite");
  await waitForLog("vite", /Local:|ready in/, 0, 60000);
  const pageUrl = `http://127.0.0.1:5173/#token=${session.token}`;

  browser = await chromium.launch({ headless: true });
  const page = await browser.newPage();
  page.on("console", (m) => log.push({ at: performance.now(), name: "page", line: `console ${m.type()}: ${m.text()}` }));
  page.on("response", (r) => r.url().includes("/_spar/") && log.push({ at: performance.now(), name: "page", line: `bridge ${r.status()} ${r.url()}` }));
  page.on("framenavigated", (f) => f === page.mainFrame() && log.push({ at: performance.now(), name: "page", line: `navigated ${f.url()}` }));
  t = performance.now();
  await page.goto(pageUrl);
  await page.getByLabel("Note count").filter({ hasText: "0 notes" }).waitFor();
  results.devPageToReady = since(t);

  await page.getByRole("textbox", { name: "Note" }).fill("Buy milk");
  await page.getByRole("button", { name: "Save" }).click();
  await page.getByRole("status").filter({ hasText: "Saved version 1" }).waitFor();
  await page.getByRole("searchbox", { name: "Search" }).fill("milk");
  await page.getByRole("listitem").filter({ hasText: "Buy milk" }).waitFor();

  // 1. React Fast Refresh: component edit, page state (the search box) must survive.
  t = performance.now();
  writeFileSync(header, originals[header].replace("<h1>Notes</h1>", "<h1>My notes</h1>"));
  await page.getByRole("heading", { name: "My notes" }).waitFor({ timeout: 15000 });
  results.fastRefreshEditToVisible = since(t);
  results.fastRefreshKeptSearchText = (await page.getByRole("searchbox", { name: "Search" }).inputValue()) === "milk";

  // 2. C# method body: Hot Reload into the running backend, no restart, backend state kept.
  t = performance.now();
  writeFileSync(service, originals[service].replace("return Task.FromResult(_notes.Count);", "return Task.FromResult(_notes.Count + 100);"));
  const applied = await waitForLog("watch", /changes applied|Restart is needed/, t);
  results.hotReloadEditToApplied = since(t);
  results.hotReloadOutcome = applied.line.includes("applied") ? "applied in place" : "restart";
  await page.getByRole("searchbox", { name: "Search" }).fill("");
  await page.getByLabel("Note count").filter({ hasText: "101 notes" }).waitFor({ timeout: 15000 });
  results.hotReloadVisibleInPage = true;

  // 3. Contract edit: watch restarts the backend; the dev URL and token must stay the same.
  t = performance.now();
  writeFileSync(service, readFileSync(service, "utf8").replace(
    "    [SparCommand]\n    public Task<int> CountAsync()",
    "    [SparCommand]\n    public Task<string> PingAsync(NoteId id) => Task.FromResult(id.Id);\n\n    [SparCommand]\n    public Task<int> CountAsync()"));
  const restarted = await waitForLog("watch", /SPAR_READY/, t, 120000);
  results.contractEditToBackendReady = since(t);
  const after = JSON.parse(readFileSync(sessionFile, "utf8"));
  results.restartKeptPortAndToken = after.port === session.port && after.token === session.token;
  results.restartSessionMarker = /session=\w+/.exec(restarted.line)?.[0];
  results.fingerprintChanged = after.build !== session.build;

  // What the open page sees: does Vite reload it with the new client, or does the SDK report the mismatch?
  const statusBefore = await page.getByRole("status").textContent();
  await page.getByRole("textbox", { name: "Note" }).fill("After restart");
  await page.getByRole("button", { name: "Save" }).click();
  const clicked = performance.now();
  await page.waitForTimeout(8000);
  results.pageEventsAfterContractEdit = log.filter((l) => l.name === "page" && l.at >= t).map((l) => `${Math.round(l.at - clicked)}ms ${l.line}`);
  results.openPageAfterContractEdit = await page.getByRole("status").textContent();
  results.openPageStatusBefore = statusBefore;

  t = performance.now();
  await page.reload();
  await page.getByLabel("Note count").waitFor();
  await page.getByRole("textbox", { name: "Note" }).fill("After reload");
  await page.getByRole("button", { name: "Save" }).click();
  await page.getByRole("status").filter({ hasText: "Saved version" }).waitFor({ timeout: 15000 });
  results.reloadSameUrlWorks = since(t);
  results.urlStable = page.url().startsWith("http://127.0.0.1:5173/");
} finally {
  writeFileSync(header, originals[header]);
  writeFileSync(service, originals[service]);
  await browser?.close();
  for (const child of [vite, watch]) {
    if (!child) continue;
    if (process.platform === "win32") spawn("taskkill", ["/pid", String(child.pid), "/T", "/F"], { stdio: "ignore" });
    else child.kill();
  }
  console.log(JSON.stringify(results, null, 2));
}
