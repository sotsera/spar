// The M1d step 3 run under dotnet watch (docs/evidence/m1d-hot-reload.md), on Windows and in WSL: `dotnet watch
// --non-interactive -lp Loopback` on the Debug Notebook, its data folders in a disposable root (LOCALAPPDATA on Windows;
// HOME and every XDG folder on Linux, always absolute paths), its page in Playwright's headless Chromium (the copy the
// repository's browser tests use; never the developer's browser, SPAR_LOOPBACK_BROWSER=none). One page, "Welcome", is
// written into the data folder before the page opens. Then, in order, each edit named by --edits, written into the
// working tree and restored after it (every file is restored at the end, whatever happened):
//   body     a method body (MarkdownPageStore.ListAsync's summary title gains " (edited)"): Hot Reload applies it, the
//            page gets backend-updated, the toast names MarkdownPageStore, and the Notebook's own onBackendUpdate
//            handler fetches the list again, which the patched code now titles "Welcome (edited)"; then restored (a
//            second update)
//   toastoff the same body edit with configureSpar({ backendUpdateToast: false }) written into the page's spar.ts
//            first: the list is fetched again, and no toast shows within 3 s
//   dto      a DTO property (PageSummary gains `string? Excerpt = null`, and the page tests' summaries `excerpt: null`,
//            as a developer changes both: the build's type check of the page needs it)  restart, client has `excerpt`
//   error    an error case's payload (PageError.TitleTaken's Title made nullable)  restart, client has `title: string | null`
//   command  a command (PagesService gains CountAsync)                              restart, client has `count:`
//   limit    a command's limit ([SparCommand(TimeoutSeconds = 30)] on SaveAsync)   restart
// A restart is dotnet watch's "Restart is needed" followed by a new run of the app (its address line), the same port and
// token, the page reloading by itself once (step 2's mechanism) and working (the list); each restored edit restarts again.
// Times: from the write to dotnet watch's line, to the event (the toast, or the list's request), and to the new run.
//   node watch-run.mjs --project <app dir> --root <dir> --log <file> --playwright <.playwright/package dir>
//        --edits body,toastoff,dto,error,command,limit [--verbose true]
import { spawn, spawnSync } from "node:child_process";
import { appendFileSync, existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { pathToFileURL } from "node:url";

const options = {};
for (let i = 2; i < process.argv.length; i += 2) options[process.argv[i].slice(2)] = process.argv[i + 1];

const windows = process.platform === "win32";
const started = performance.now();
const clock = () => (performance.now() - started).toFixed(1).padStart(9);
mkdirSync(dirname(options.log), { recursive: true });
writeFileSync(options.log, "");
const note = text => appendFileSync(options.log, `${clock()} run: ${text}\n`);
const failures = [];
const check = (ok, text) => {
  note(`${ok ? "PASS" : "FAIL"} ${text}`);
  if (!ok) failures.push(text);
};
const sleep = ms => new Promise(r => setTimeout(r, ms));
const project = resolve(options.project);

// Absolute: Spar takes LOCALAPPDATA (and the XDG folders) only when it is a full path, and otherwise the user's own.
const root = resolve(options.root);
rmSync(root, { recursive: true, force: true });
const environment = { ...process.env, DOTNET_NOLOGO: "1", DOTNET_WATCH_SUPPRESS_EMOJIS: "1", MSBUILDDISABLENODEREUSE: "1" };
for (const name of Object.keys(environment)) if (name.startsWith("SPAR_")) delete environment[name];
environment.SPAR_LOOPBACK_BROWSER = "none";
const folders = windows
  ? { LOCALAPPDATA: "local" }
  : { HOME: "home", XDG_DATA_HOME: "data", XDG_CONFIG_HOME: "config", XDG_CACHE_HOME: "cache", XDG_STATE_HOME: "state" };
for (const [variable, folder] of Object.entries(folders)) {
  environment[variable] = join(root, folder);
  mkdirSync(join(root, folder), { recursive: true });
}
rmSync(join(project, "obj", "spar-loopback.json"), { force: true });

// The edited files, kept to be restored.
const originals = new Map();
function edit(relative, from, to) {
  const path = join(project, relative);
  const text = originals.get(path) ?? readFileSync(path, "utf8");
  originals.set(path, text);
  const edited = text.replace(from, to);
  if (edited === text) throw new Error(`The anchor of the edit is not in ${relative}: ${from}`);
  writeFileSync(path, edited);
  note(`edit written: ${relative}`);
  return performance.now();
}

function editWith(relative, change) {
  const path = join(project, relative);
  const text = originals.get(path) ?? readFileSync(path, "utf8");
  originals.set(path, text);
  const edited = change(text);
  if (edited === text) throw new Error(`The edit changed nothing in ${relative}`);
  writeFileSync(path, edited);
  note(`edit written: ${relative}`);
}

function restore(relative) {
  const path = join(project, relative);
  writeFileSync(path, originals.get(path));
  note(`restored: ${relative}`);
  return performance.now();
}

const output = [];
const args = ["watch", "--non-interactive", ...(options.verbose === "true" ? ["--verbose"] : []), "--project", project, "-lp", "Loopback"];
const watch = spawn("dotnet", args, { env: environment, stdio: ["ignore", "pipe", "pipe"], detached: !windows });
for (const stream of [watch.stdout, watch.stderr]) {
  let buffered = "";
  stream.setEncoding("utf8");
  stream.on("data", chunk => {
    buffered += chunk;
    let newline;
    while ((newline = buffered.indexOf("\n")) >= 0) {
      const line = buffered.slice(0, newline).trimEnd();
      buffered = buffered.slice(newline + 1);
      output.push({ at: performance.now(), line });
      appendFileSync(options.log, `${clock()} watch: ${line}\n`);
    }
  });
}

async function waitFor(pattern, after = 0, ms = 120_000) {
  const deadline = performance.now() + ms;
  while (performance.now() < deadline) {
    const found = output.find(entry => entry.at >= after && pattern.test(entry.line));
    if (found) return { ...found, match: found.line.match(pattern) };
    await sleep(10);
  }

  throw new Error(`no ${pattern} within ${ms} ms`);
}

const addressLine = /Spar: the page is at (http:\/\/127\.0\.0\.1:(\d+)\/#token=([A-Za-z0-9_-]+)) \(([^)]*)\)/;
const applied = /C# and Razor changes applied in (\d+)ms|changes applied/;
const restartNeeded = /Restart is needed|rude edit|Restarting/i;
const updateLine = /Hot Reload applied revision (\d+) \(([^)]*)\); sent to (\d+) document/;
const client = join(project, "obj", "spar", "client");
const ms = (from, to) => `${(to - from).toFixed(0)} ms`;

const { chromium } = await import(pathToFileURL(join(options.playwright, "index.mjs")).href);
const browser = await chromium.launch({ headless: true });
const page = await (await browser.newContext()).newPage();
let loads = 0;
const listCalls = [];
const violations = [];
page.on("load", () => note(`page load ${++loads}`));
page.on("console", message => {
  note(`page console ${message.type()}: ${message.text()}`);
  if (/Content Security Policy|Trusted Type/i.test(message.text())) violations.push(message.text());
});
page.on("request", request => {
  if (request.url().endsWith("/_spar/send") && (request.postData() ?? "").includes("\"list\"")) listCalls.push(performance.now());
});
const toast = page.locator("[data-spar-dev-overlay] .toast");

/** The page's toast whose text contains `text`, and when it was seen (polled every 10 ms). */
async function toastWith(text, after, timeout = 15_000) {
  const deadline = performance.now() + timeout;
  while (performance.now() < deadline) {
    const texts = await toast.allTextContents();
    const found = texts.find(t => t.includes(text));
    if (found !== undefined) return { at: performance.now(), text: found };
    await sleep(10);
  }

  return undefined;
}

async function listShows(title, timeout = 15_000) {
  await page.getByText(title, { exact: true }).first().waitFor({ timeout });
}

async function firstListCall(after, timeout = 15_000) {
  const deadline = performance.now() + timeout;
  while (performance.now() < deadline) {
    const at = listCalls.find(t => t >= after);
    if (at !== undefined) return at;
    await sleep(10);
  }

  return undefined;
}

let address;

/** A Hot Reload edit: applied, the event, the toast (unless off) and the list fetched again with the patched code. */
async function hotReload(name, write, expectTitle, toastName, toastOn = true) {
  const at = write();
  const done = await waitFor(new RegExp(`${applied.source}|${restartNeeded.source}`, "i"), at, 60_000);
  check(applied.test(done.line), `${name}: dotnet watch applied it by Hot Reload (${done.line.trim()}; ${ms(at, done.at)} after the write)`);
  const update = await waitFor(updateLine, at, 30_000);
  check(Number(update.match[3]) === 1, `${name}: the backend sent revision ${update.match[1]} (${update.match[2]}) to 1 document, ${ms(at, update.at)} after the write`);
  const call = await firstListCall(at);
  check(call !== undefined, `${name}: the page's onBackendUpdate handler fetched the list again${call ? `, ${ms(at, call)} after the write` : ""}`);
  await listShows(expectTitle);
  check(true, `${name}: the list now shows "${expectTitle}", from the patched code`);
  if (toastOn) {
    const seen = await toastWith(`${toastName} (revision ${update.match[1]})`, at);
    check(seen !== undefined, `${name}: the toast shows${seen ? ` "${seen.text.replace("×", "").trim()}", ${ms(at, seen.at)} after the write` : ""}`);
    if (seen !== undefined && name === "body") {
      await page.screenshot({ path: join(dirname(options.log), `toast-${process.platform}.png`) });
    }
  } else {
    await sleep(3_000);
    const texts = await toast.allTextContents();
    check(!texts.some(t => t.includes(`(revision ${update.match[1]})`)), `${name}: no toast for revision ${update.match[1]} within 3 s (${texts.length} toasts in the page)`);
  }
}

/** A contract edit: dotnet watch restarts, the client is regenerated, the page reloads by itself and works. */
async function restart(name, write, clientHas) {
  const loadsBefore = loads;
  const at = write();
  const done = await waitFor(new RegExp(`${applied.source}|${restartNeeded.source}`, "i"), at, 60_000);
  check(restartNeeded.test(done.line), `${name}: dotnet watch restarts instead of patching (${done.line.trim()}; ${ms(at, done.at)} after the write)`);
  const next = await waitFor(addressLine, at, 180_000);
  check(next.match[2] === address.match[2] && next.match[3] === address.match[3], `${name}: a new run listens on the same address, ${ms(at, next.at)} after the write`);
  if (clientHas !== undefined) {
    const declarations = readFileSync(join(client, "pages.d.ts"), "utf8");
    check(clientHas.test(declarations), `${name}: the regenerated client's pages.d.ts matches ${clientHas}`);
  }

  const deadline = performance.now() + 30_000;
  while (loads === loadsBefore && performance.now() < deadline) await sleep(10);
  check(loads === loadsBefore + 1, `${name}: the page reloaded by itself once${loads > loadsBefore ? `, ${ms(at, performance.now())} after the write` : ""}`);
  await listShows("Welcome");
  check(true, `${name}: the reloaded page lists the notebook's page`);

  // The next edit waits until the reloaded document has kept its backend 5 s: three reloads in a row into documents
  // that lost their backend sooner stop the page's automatic reloads (step 2's loop guard), which quick successive
  // restarts trigger (docs/evidence/m1d-hot-reload.md).
  await sleep(5_500);
}

const edits = {
  body: async () => {
    await hotReload("body", () => edit("Pages/MarkdownPageStore.cs", "new PageSummary(page.Id, page.Title, page.UpdatedAt)", "new PageSummary(page.Id, page.Title + \" (edited)\", page.UpdatedAt)"), "Welcome (edited)", "MarkdownPageStore");
    await hotReload("body restored", () => restore("Pages/MarkdownPageStore.cs"), "Welcome", "MarkdownPageStore");
  },
  toastoff: async () => {
    const loadsBefore = loads;
    edit("frontend/src/spar.ts", "  configureSpar({\n", "  configureSpar({\n    backendUpdateToast: false,\n");
    const deadline = performance.now() + 15_000;
    while (loads === loadsBefore && performance.now() < deadline) await sleep(10);
    await listShows("Welcome");
    note(`the page took the setting (${loads - loadsBefore} reload by Vite)`);
    await hotReload("toast off", () => edit("Pages/MarkdownPageStore.cs", "new PageSummary(page.Id, page.Title, page.UpdatedAt)", "new PageSummary(page.Id, page.Title + \" (edited)\", page.UpdatedAt)"), "Welcome (edited)", "MarkdownPageStore", false);
    await hotReload("toast off, restored", () => restore("Pages/MarkdownPageStore.cs"), "Welcome", "MarkdownPageStore", false);
    restore("frontend/src/spar.ts");
    await sleep(2_000);
  },
  dto: async () => {
    const fixtures = text => text.replaceAll(/(const (?:older|newer): PageSummary = \{[^}]*?)( \};)/g, "$1, excerpt: null$2");
    await restart("dto", () => {
      editWith("frontend/src/notebook.test.tsx", fixtures);
      return edit("Pages/PageTypes.cs", "PageSummary(string Id, string Title, DateTimeOffset UpdatedAt)", "PageSummary(string Id, string Title, DateTimeOffset UpdatedAt, string? Excerpt = null)");
    }, /readonly excerpt: string \| null/);
    await restart("dto restored", () => {
      restore("frontend/src/notebook.test.tsx");
      return restore("Pages/PageTypes.cs");
    }, /^(?![\s\S]*excerpt)/);
  },
  error: async () => {
    await restart("error", () => edit("Pages/PageTypes.cs", "TitleTaken(string Title)", "TitleTaken(string? Title)"), /readonly title: string \| null/);
    await restart("error restored", () => restore("Pages/PageTypes.cs"), /^(?![\s\S]*readonly title: string \| null)/);
  },
  command: async () => {
    await restart("command", () => edit("Pages/PagesService.cs", "    private static string DescribeInvalid", "    [SparCommand]\n    public Task<int> CountAsync(CancellationToken cancellationToken) => Task.FromResult(0);\n\n    private static string DescribeInvalid"), /readonly count: \(/);
    await restart("command restored", () => restore("Pages/PagesService.cs"), /^(?![\s\S]*readonly count: \()/);
  },
  // Step 2's untried edit, recorded either way: a private static method made an instance one.
  instance: async () => {
    const at = edit("Pages/MarkdownPageStore.cs", "private static string Format(Page page)", "private string Format(Page page)");
    const done = await waitFor(new RegExp(`${applied.source}|${restartNeeded.source}|No managed code changes`, "i"), at, 60_000);
    note(`instance: dotnet watch answered "${done.line.trim()}", ${ms(at, done.at)} after the write`);
    const back = restore("Pages/MarkdownPageStore.cs");
    await waitFor(new RegExp(`${applied.source}|${restartNeeded.source}|No managed code changes`, "i"), back, 60_000);
    if (restartNeeded.test(done.line)) await waitFor(addressLine, back, 180_000);
    await sleep(6_000);
  },
  limit: async () => {
    await restart("limit", () => edit("Pages/PagesService.cs", "    [SparCommand]\n    public async Task<Result<Page, PageError>> SaveAsync", "    [SparCommand(TimeoutSeconds = 30)]\n    public async Task<Result<Page, PageError>> SaveAsync"), undefined);
    await restart("limit restored", () => restore("Pages/PagesService.cs"), undefined);
  },
};

try {
  address = await waitFor(addressLine, 0, 300_000);
  const host = await waitFor(/Host started on loopback.*folder (.+)$/);
  const pagesFolder = join(host.match[1].trim(), "data", "pages");
  mkdirSync(pagesFolder, { recursive: true });
  writeFileSync(join(pagesFolder, "welcome.md"), "---\ntitle: Welcome\nrevision: 1\nupdated: 2026-11-22T09:00:00.0000000+00:00\n---\n# Welcome\n");
  await page.goto(address.match[1]);
  await listShows("Welcome", 60_000);
  check(loads === 1, `the page lists the notebook's page (${address.match[1].replace(/#token=.*/, "#token=…")})`);

  for (const name of options.edits.split(",")) {
    note(`--- ${name}`);
    await edits[name]();
    await sleep(1_500);
  }

  check(violations.length === 0, `no CSP or Trusted Types message in the page's console (${violations.length})`);
} catch (error) {
  failures.push(String(error));
  note(`FAIL ${error.stack ?? error}`);
} finally {
  for (const [path, text] of originals) writeFileSync(path, text);
  note(`${originals.size} edited files restored`);
  await browser.close();
  if (windows) spawnSync("taskkill", ["/PID", String(watch.pid), "/T", "/F"]);
  else {
    try {
      process.kill(-watch.pid, "SIGTERM");
    } catch {}
  }

  await sleep(1_000);
  const record = join(project, "obj", "spar-dev-server.json");
  if (existsSync(record)) {
    try {
      const vite = JSON.parse(readFileSync(record, "utf8"));
      if (windows) spawnSync("taskkill", ["/PID", String(vite.pid), "/T", "/F"]);
      else process.kill(vite.pid, "SIGTERM");
      note(`vite ${vite.pid} ended`);
    } catch {
      note("the development server had already ended");
    }
  }

  note(`${failures.length} failures`);
}

process.exit(failures.length === 0 ? 0 : 1);
