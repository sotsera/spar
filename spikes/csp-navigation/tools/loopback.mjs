// Usage: node tools/loopback.mjs <label> <profile> <page or absolute URL> [--dist <folder under frontend>] [host args...]
// Starts the host in loopback mode (app on 127.0.0.1:5190, external on :5180), opens the page in Playwright's
// headless Chromium, performs the trusted clicks the page asks for, and writes host + browser lines to
// artifacts/csp-navigation/loopback/<label>.log. Paths are relative to this script.
import { spawn } from "node:child_process";
import { appendFileSync, mkdirSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "../frontend/node_modules/playwright-core/index.mjs";

const here = join(dirname(fileURLToPath(import.meta.url)), "..");
const [label, profile, page, ...rest] = process.argv.slice(2);
let dist = "dist";
const distIndex = rest.indexOf("--dist");
if (distIndex >= 0) dist = rest.splice(distIndex, 2)[1];
const outDir = join(here, "../../artifacts/csp-navigation/loopback");
mkdirSync(outDir, { recursive: true });
const log = join(outDir, `${label}.log`);
writeFileSync(log, "");
const write = line => appendFileSync(log, line + "\n");

const exe = join(here, "Csp.Windows/bin/Release/net11.0/win-x64/publish/Csp.Windows.exe");
const host = spawn(exe, ["--loopback", "--label", label, "--profile", profile, "--root", `frontend/${dist}`, "--root", "pages", ...rest], { cwd: here });
let ready;
const started = new Promise(resolve => (ready = resolve));
host.stdout.setEncoding("utf8");
host.stdout.on("data", chunk => {
  for (const line of chunk.split(/\r?\n/).filter(Boolean)) {
    write(line);
    if (line.startsWith("LOOPBACK_READY")) ready();
  }
});
host.stderr.on("data", chunk => write("HOST_STDERR " + chunk));
await started;

const browser = await chromium.launch({ headless: true });
const context = await browser.newContext();
const tab = await context.newPage();
let finish;
const finished = new Promise(resolve => (finish = resolve));
context.on("page", popup => {
  write("BROWSER_POPUP " + popup.url());
  // The cross-origin page checks its popup handle itself (COOP); other runs close popups at once.
  if (!label.startsWith("xpage")) void popup.close();
});
tab.on("framenavigated", frame => write(`BROWSER_NAVIGATED ${frame === tab.mainFrame() ? "top" : "frame"} ${frame.url()}`));
tab.on("pageerror", error => write("BROWSER_PAGE_ERROR " + error.message));
tab.on("console", async message => {
  const text = message.text();
  write("BROWSER_CONSOLE " + message.type() + " " + text.slice(0, 600));
  if (text === "SPAR_DONE") finish();
  if (!text.startsWith("SPAR_PAGE ")) return;
  const body = JSON.parse(text.slice(10));
  if (body.kind === "done") finish();
  if (body.kind === "click") {
    const modifiers = [];
    if (body.modifiers & 2) modifiers.push("Control");
    if (body.modifiers & 8) modifiers.push("Shift");
    await tab.mouse.click(body.x, body.y, { button: body.button, modifiers });
    write(`BROWSER_CLICKED ${body.button} ${modifiers.join("+")}`);
  }
});
const url = page.startsWith("http") ? page : `http://127.0.0.1:5190${page}`;
write("BROWSER_OPEN " + url + " " + browser.version());
await tab.goto(url);
await Promise.race([finished, new Promise(resolve => setTimeout(resolve, 60000).unref())]);
await new Promise(resolve => setTimeout(resolve, Number(process.env.SPAR_LINGER ?? 300)));
await browser.close();
host.stdin.end();
await new Promise(resolve => host.on("exit", resolve));
console.log(`done ${label} -> artifacts/csp-navigation/loopback/${label}.log`);
