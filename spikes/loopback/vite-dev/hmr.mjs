// Frontend hot reload against a live loopback backend: edit main.js on disk, measure until the page shows the
// change, and check the backend kept its state. Run: node hmr.mjs
import { spawn } from "node:child_process";
import { readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { createServer } from "vite";
import { chromium } from "playwright-core";

const here = (path) => fileURLToPath(new URL(path, import.meta.url));
const appExe = here("../Loop.App/bin/Debug/net11.0/win-x64/Loop.App.exe");
const mainJs = here("../Loop.App/frontend/main.js");
const original = readFileSync(mainJs, "utf8");
const timings = {};
const mark = (name, start) => (timings[name] = Math.round(performance.now() - start));

const backend = await new Promise((resolve, reject) => {
  const child = spawn(appExe, ["--loopback", "--exit-with-stdin"], { stdio: ["pipe", "pipe", "inherit"] });
  child.stdout.setEncoding("utf8");
  child.stdout.on("data", (chunk) => {
    const match = /SPAR_READY url=(\S+)\/#token=(\S+) build=(\S+)/.exec(chunk);
    if (match) resolve({ child, base: match[1], token: match[2], build: match[3] });
  });
  child.on("exit", (code) => reject(new Error(`backend exited ${code}`)));
});

process.env.SPAR_BACKEND = backend.base;
const server = await createServer({ configFile: here("vite.config.mjs") });
await server.listen();
const viteUrl = server.resolvedUrls.local[0];
const browser = await chromium.launch({ headless: true });
const page = await browser.newPage();
const status = page.getByRole("status");
page.on("console", (m) => console.log("page console:", m.type(), m.text()));
page.on("response", (r) => r.url().includes("/_spar/") && console.log("bridge response:", r.status(), r.url()));

try {
  let start = performance.now();
  await page.goto(`${viteUrl}#token=${backend.token}`);
  await status.filter({ hasText: /^Ready$/ }).waitFor({ timeout: 15000 }).catch(async () => {
    throw new Error(`page never became ready; status: "${await status.textContent()}"`);
  });
  mark("viteFirstLoadToReady", start);

  await page.getByRole("textbox", { name: "Note" }).fill("kept across frontend edits");
  await page.getByRole("button", { name: "Save" }).click();
  await status.filter({ hasText: "Saved version 1" }).waitFor();

  start = performance.now();
  writeFileSync(mainJs, original.replace('status.textContent = "Ready";', 'status.textContent = "Ready to write";'));
  await status.filter({ hasText: "Ready to write" }).waitFor({ timeout: 15000 });
  mark("editToVisible", start);

  const items = await page.getByRole("list", { name: "Saved notes" }).textContent();
  const kept = items.includes("kept across frontend edits");
  console.log(JSON.stringify({ vite: viteUrl, backend: backend.base, build: backend.build, timings, backendStateKept: kept }));
} finally {
  writeFileSync(mainJs, original);
  await browser.close();
  await server.close();
  backend.child.stdin.end();
}
