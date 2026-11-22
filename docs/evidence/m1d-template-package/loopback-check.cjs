// dotnet run -lp Loopback in <app dir>: waits for the printed address, opens it in Playwright's headless Chromium, greets
// a name through the backend, then ends the app's tree and the Vite server it started (by its record).
//   node loopback-check.cjs <app dir> <playwright package dir> <log file>
const { spawn, spawnSync } = require("node:child_process");
const fs = require("node:fs");
const path = require("node:path");
const [app, playwrightDir, log] = process.argv.slice(2);
const { chromium } = require(playwrightDir);
const windows = process.platform === "win32";
const started = Date.now();
const note = text => { const line = `${String(Date.now() - started).padStart(6)} ms ${text}`; console.log(line); fs.appendFileSync(log, line + "\n"); };
fs.writeFileSync(log, "");
const child = spawn("dotnet", ["run", "-lp", "Loopback"], { cwd: app, env: { ...process.env, SPAR_LOOPBACK_BROWSER: "none" }, detached: !windows });
let output = "";
const address = new Promise((resolve, reject) => {
  const read = chunk => { output += chunk; const m = output.match(/http:\/\/127\.0\.0\.1:\d+\/#token=[A-Za-z0-9_-]+/); if (m) resolve(m[0]); };
  child.stdout.on("data", read); child.stderr.on("data", read);
  child.on("exit", code => reject(new Error(`exited ${code}:\n${output}`)));
  setTimeout(() => reject(new Error(`no address in 240 s:\n${output}`)), 240_000);
});
function endTree(pid) {
  if (windows) spawnSync("taskkill", ["/T", "/F", "/PID", String(pid)]);
  else try { process.kill(-pid, "SIGTERM"); } catch {}
}
(async () => {
  let result = 1;
  try {
    const url = await address;
    note(`address printed: ${url.replace(/token=.*/, "token=…")}`);
    const browser = await chromium.launch({ headless: true });
    const page = await browser.newPage();
    await page.goto(url);
    await page.getByLabel("Name").fill("Ada");
    await page.getByRole("button", { name: "Greet" }).click();
    await page.getByRole("status").filter({ hasText: "Hello, Ada!" }).waitFor({ timeout: 30_000 });
    note("PASS the page greeted Ada through the backend");
    await browser.close();
    result = 0;
  } catch (e) { note(`FAIL ${e.message}`); }
  endTree(child.pid);
  const record = path.join(app, "obj", "spar-dev-server.json");
  if (fs.existsSync(record)) { const pid = JSON.parse(fs.readFileSync(record, "utf8")).pid; endTree(pid); note(`ended the development server ${pid}`); }
  fs.appendFileSync(log, "--- app output ---\n" + output.replace(/token=[A-Za-z0-9_-]+/g, "token=…"));
  process.exit(result);
})();
