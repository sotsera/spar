// Usage: node tools/chromium.mjs <label> <profile> <page> [dist]
// Runs spikes/csp-navigation's host in loopback mode on this spike's folders (the frontend build first, then
// pages/, then csp-navigation's pages/ for report.js), opens the page in Playwright's headless Chromium, and writes
// the host's and the browser's lines to artifacts/csp-style-injection/chromium/<label>.log.
import { spawn } from "node:child_process";
import { appendFileSync, mkdirSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "../../csp-navigation/frontend/node_modules/playwright-core/index.mjs";

const here = join(dirname(fileURLToPath(import.meta.url)), "..");
const [label, profile, page, dist = "dist"] = process.argv.slice(2);
const outDir = join(here, "../../artifacts/csp-style-injection/chromium");
mkdirSync(outDir, { recursive: true });
const log = join(outDir, `${label}.log`);
writeFileSync(log, "");
const write = line => appendFileSync(log, line + "\n");

const exe = join(here, "../csp-navigation/Csp.Windows/bin/Release/net11.0/win-x64/publish/Csp.Windows.exe");
const roots = ["--root", join(here, "frontend/obj", dist), "--root", join(here, "pages"), "--root", join(here, "../csp-navigation/pages")];
const host = spawn(exe, ["--loopback", "--label", label, "--profile", profile, ...roots], { cwd: here });
let ready;
const started = new Promise(resolve => (ready = resolve));
host.stdout.setEncoding("utf8");
host.stdout.on("data", chunk => {
  for (const line of chunk.split(/\r?\n/).filter(Boolean)) {
    write(line);
    if (line.startsWith("LOOPBACK_READY")) ready();
  }
});
await started;

const browser = await chromium.launch({ headless: true });
const tab = await (await browser.newContext()).newPage();
let finish;
const finished = new Promise(resolve => (finish = resolve));
tab.on("pageerror", error => write("BROWSER_PAGE_ERROR " + error.message));
tab.on("console", message => {
  const text = message.text();
  write("BROWSER_CONSOLE " + message.type() + " " + text.slice(0, 2000));
  if (text.startsWith("SPAR_PAGE ") && JSON.parse(text.slice(10)).kind === "done") finish();
});
await tab.goto(`http://127.0.0.1:5190${page}`);
await Promise.race([finished, new Promise(resolve => setTimeout(resolve, 15000))]);
write("BROWSER_VERSION " + browser.version());
await browser.close();
host.kill();
console.log(`log=artifacts/csp-style-injection/chromium/${label}.log`);
