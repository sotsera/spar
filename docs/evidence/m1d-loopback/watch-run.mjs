// The M1d step 2 run under dotnet watch (docs/evidence/m1d-loopback.md), on Windows: `dotnet watch --non-interactive
// -lp Loopback` on the Debug Notebook with its data in a disposable root (LOCALAPPDATA), its page in Playwright's headless
// Chromium, SPAR_LOOPBACK_BROWSER pointing at loopback-run.mjs's recorder. Then an edit meant to make dotnet watch
// restart the app (a static method made an instance one), after which the run expects the same port and token, one
// reload by itself, and nothing new opened. As recorded, dotnet watch never restarted: it applied a new parameter of a
// private method by Hot Reload, and answered a project-file edit and this edit with "No managed code changes to
// apply" (docs/evidence/m1d-loopback.md). The edited file is restored, and every process it started ended.
//   node watch-run.mjs --project <app dir> --root <dir> --log <file> --playwright <.playwright/package dir>
import { spawn, spawnSync } from "node:child_process";
import { appendFileSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { pathToFileURL } from "node:url";

const options = {};
for (let i = 2; i < process.argv.length; i += 2) options[process.argv[i].slice(2)] = process.argv[i + 1];

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

const root = resolve(options.root);
rmSync(root, { recursive: true, force: true });
mkdirSync(join(root, "local"), { recursive: true });
const recorded = join(root, "opened.txt");
writeFileSync(recorded, "");
rmSync(join(options.project, "obj", "spar-loopback.json"), { force: true });
const edited = join(options.project, "Pages", "MarkdownPageStore.cs");
const original = readFileSync(edited, "utf8");
const recorder = join(dirname(process.argv[1]), "loopback-run.mjs");

const environment = { ...process.env, LOCALAPPDATA: join(root, "local"), DOTNET_NOLOGO: "1" };
for (const name of Object.keys(environment)) if (name.startsWith("SPAR_")) delete environment[name];
environment.SPAR_LOOPBACK_BROWSER = `"${process.execPath}" "${recorder}" --record "${recorded}" {url}`;

const output = [];
const watch = spawn("dotnet", ["watch", "--non-interactive", "--project", options.project, "-lp", "Loopback"], { env: environment, stdio: ["ignore", "pipe", "pipe"] });
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
    await sleep(25);
  }

  throw new Error(`no ${pattern} within ${ms} ms`);
}

const addressLine = /Spar: the page is at (http:\/\/127\.0\.0\.1:(\d+)\/#token=([A-Za-z0-9_-]+)) \(([^)]*)\)/;
const { chromium } = await import(pathToFileURL(join(options.playwright, "index.mjs")).href);
const browser = await chromium.launch({ headless: true });
const page = await (await browser.newContext()).newPage();
let loads = 0;
page.on("load", () => note(`page load ${++loads}`));
page.on("console", message => note(`page console ${message.type()}: ${message.text()}`));

try {
  const first = await waitFor(addressLine);
  await sleep(1_000);
  await page.goto(first.match[1]);
  await page.getByRole("heading", { name: "Start your notebook" }).waitFor({ timeout: 60_000 });
  check(loads === 1, "the page works on the first run");

  const editAt = performance.now();
  writeFileSync(edited, original.replace("</SparAppId>", "</SparAppId>\n    <SparWatchRunMarker>1</SparWatchRunMarker>"));
  note("rude edit written: Format is no longer static");
  const second = await waitFor(addressLine, editAt);
  check(second.match[2] === first.match[2] && second.match[3] === first.match[3], `the restarted app kept the port and token (${(second.at - editAt).toFixed(0)} ms after the edit)`);
  const deadline = performance.now() + 30_000;
  while (loads < 2 && performance.now() < deadline) await sleep(25);
  check(loads === 2, `the page reloaded by itself once (${loads - 1})`);
  await page.getByRole("heading", { name: "Start your notebook" }).waitFor({ timeout: 30_000 });
  await sleep(3_000);
  check(loads === 2, "no further reload");
  check(readFileSync(recorded, "utf8").trim().split("\n").length === 1, "the browser was asked to open once");
  check(output.some(entry => entry.at >= editAt && /Development server .* reused/.test(entry.line)), "the restarted app reused the development server");
} catch (error) {
  failures.push(String(error));
  note(`FAIL ${error.stack ?? error}`);
} finally {
  writeFileSync(edited, original);
  await browser.close();
  spawnSync("taskkill", ["/PID", String(watch.pid), "/T", "/F"]);
  try {
    const vite = JSON.parse(readFileSync(join(options.project, "obj", "spar-dev-server.json"), "utf8"));
    spawnSync("taskkill", ["/PID", String(vite.pid), "/T", "/F"]);
    note(`vite ${vite.pid} ended`);
  } catch {
    note("no development server record");
  }

  note(`${failures.length} failures`);
}

process.exit(failures.length === 0 ? 0 : 1);
