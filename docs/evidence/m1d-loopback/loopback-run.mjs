// The M1d step 2 run (docs/evidence/m1d-loopback.md), on Windows and in WSL: the Debug Notebook on loopback, started
// as `dotnet run --no-build -lp Loopback` with its data folders in a disposable root (LOCALAPPDATA on Windows; HOME and
// every XDG folder on Linux), and its page in Playwright's headless Chromium (the copy the repository's browser tests
// use), never the developer's browser: SPAR_LOOPBACK_BROWSER points at this script's own recorder, which writes each
// address Spar asks to open into a file. In order:
//   fresh     a new dev session (no obj/spar-loopback.json): one address recorded; the page works through the bridge
//             (the empty notebook, then a page created); 0 CSP violations in the page and the app's log
//   restart   the backend ended (its own process, hard, as a crash ends it; on Linux SIGTERM with --signal), then run
//             again: the same port and token, the page reloads by itself once, nothing new recorded, the page created
//             before is there
//   refusals  a wrong token, no token, another Host and another Origin are refused (401, 401, 403, 403)
//   override  SPAR_LOOPBACK_PORT and SPAR_LOOPBACK_TOKEN: used as given, nothing recorded, the record unchanged
//   new-day   the record last held 5 hours ago: a new token on the same port, one more address recorded, and the old
//             page told its token is refused, without reloading
// It ends every process it started (the app's tree, and the Vite server the first run started, by its record).
//   node loopback-run.mjs --project <app dir> --root <dir> --log <file> --playwright <.playwright/package dir> [--signal TERM]
//   node loopback-run.mjs --record <file> <url>        (the recorder Spar runs)
import { spawn, spawnSync } from "node:child_process";
import { appendFileSync, existsSync, mkdirSync, readdirSync, readFileSync, rmSync, statSync, utimesSync, writeFileSync } from "node:fs";
import { createServer } from "node:net";
import { dirname, join, resolve } from "node:path";
import { pathToFileURL } from "node:url";

if (process.argv[2] === "--record") {
  appendFileSync(process.argv[3], `${process.argv[4]}\n`);
  process.exit(0);
}

const options = {};
for (let i = 2; i < process.argv.length; i += 2) {
  options[process.argv[i].slice(2)] = process.argv[i + 1];
}

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

// Absolute: Spar takes LOCALAPPDATA only when it is a full path, and otherwise the user's own folder.
const root = resolve(options.root);
rmSync(root, { recursive: true, force: true });
mkdirSync(root, { recursive: true });
const record = join(options.project, "obj", "spar-loopback.json");
const recorded = join(root, "opened.txt");
rmSync(record, { force: true });
writeFileSync(recorded, "");

const environment = { ...process.env, SPAR_HOST: "", DOTNET_NOLOGO: "1" };
for (const name of Object.keys(environment)) {
  if (name.startsWith("SPAR_")) delete environment[name];
}
if (windows) {
  environment.LOCALAPPDATA = join(root, "local");
} else {
  for (const [variable, folder] of Object.entries({ HOME: "home", XDG_DATA_HOME: "data", XDG_CONFIG_HOME: "config", XDG_CACHE_HOME: "cache", XDG_STATE_HOME: "state" })) {
    environment[variable] = join(root, folder);
  }
}
for (const folder of windows ? ["local"] : ["home", "data", "config", "cache", "state"]) {
  mkdirSync(join(root, folder), { recursive: true });
}
environment.SPAR_LOOPBACK_BROWSER = `"${process.execPath}" "${process.argv[1]}" --record "${recorded}" {url}`;

/** One `dotnet run --no-build -lp Loopback`: its output in the run's log; resolves with its address line. */
function startApp(name, extra = {}) {
  const output = [];
  const child = spawn("dotnet", ["run", "--no-build", "--project", options.project, "-lp", "Loopback"], {
    env: { ...environment, ...extra },
    stdio: ["ignore", "pipe", "pipe"],
    detached: !windows,
  });
  const app = { name, child, output, exited: null, at: performance.now() };
  child.on("exit", (code, signal) => {
    app.exited = { code, signal };
    note(`${name}: exited ${code ?? signal}`);
  });
  for (const stream of [child.stdout, child.stderr]) {
    let buffered = "";
    stream.setEncoding("utf8");
    stream.on("data", chunk => {
      buffered += chunk;
      let newline;
      while ((newline = buffered.indexOf("\n")) >= 0) {
        const line = buffered.slice(0, newline).trimEnd();
        buffered = buffered.slice(newline + 1);
        output.push({ at: performance.now(), line });
        appendFileSync(options.log, `${clock()} ${name}: ${line}\n`);
      }
    });
  }

  return app;
}

async function waitFor(app, pattern, ms = 60_000) {
  const deadline = performance.now() + ms;
  while (performance.now() < deadline) {
    const found = app.output.find(entry => pattern.test(entry.line));
    if (found) return { ...found, match: found.line.match(pattern) };
    if (app.exited) throw new Error(`${app.name} exited before ${pattern}`);
    await sleep(25);
  }

  throw new Error(`${app.name}: no ${pattern} within ${ms} ms`);
}

const addressLine = /Spar: the page is at (http:\/\/127\.0\.0\.1:(\d+)\/#token=([A-Za-z0-9_-]+)) \(([^)]*)\)/;

/**
 * Ends the app's own process, which dotnet run then follows: a hard end (TerminateProcess, SIGKILL), as a crash or a
 * killed backend ends it, or a signal (Linux). Never its whole tree: the Vite server the app started is a descendant
 * of it, and a backend restart keeps it (step 1).
 */
async function stopApp(app, signal) {
  const pid = appPid(app.child.pid);
  if (pid === undefined) {
    note(`${app.name}: no app process under dotnet run ${app.child.pid}`);
  } else if (windows) {
    spawnSync("taskkill", ["/PID", String(pid), "/F"]);
  } else {
    process.kill(pid, signal ?? "SIGKILL");
  }

  for (let i = 0; i < 400 && !app.exited; i++) await sleep(25);
  if (!app.exited) {
    if (windows) spawnSync("taskkill", ["/PID", String(app.child.pid), "/F"]);
    else process.kill(app.child.pid, "SIGKILL");
  }

  note(`${app.name}: app process ${pid} stopped (${windows ? "taskkill /F" : signal ?? "SIGKILL"})`);
}

/** The app's process: the child of dotnet run named after the app. */
function appPid(dotnetPid) {
  const query = windows
    ? spawnSync("powershell", ["-NoProfile", "-Command", `(Get-CimInstance Win32_Process -Filter "ParentProcessId=${dotnetPid}" | Where-Object Name -eq "Notebook.exe").ProcessId`], { encoding: "utf8" })
    : spawnSync("pgrep", ["-P", String(dotnetPid), "-x", "Notebook"], { encoding: "utf8" });
  const pid = Number.parseInt(query.stdout.trim(), 10);
  return Number.isNaN(pid) ? undefined : pid;
}

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const openedLines = () => readFileSync(recorded, "utf8").split("\n").filter(line => line.length > 0);
const freePort = () => new Promise(resolve => {
  const server = createServer();
  server.listen(0, "127.0.0.1", () => {
    const { port } = server.address();
    server.close(() => resolve(port));
  });
});

async function request(url, init) {
  try {
    const response = await fetch(url, init);
    return response.status;
  } catch (error) {
    return `error ${error.cause?.code ?? error.message}`;
  }
}

const { chromium } = await import(pathToFileURL(join(options.playwright, "index.mjs")).href);
const browser = await chromium.launch({ headless: true });
const context = await browser.newContext();
const page = await context.newPage();
const consoleLines = [];
let loads = 0;
page.on("console", message => {
  consoleLines.push(`${message.type()}: ${message.text()}`);
  note(`page console ${message.type()}: ${message.text()}`);
});
page.on("pageerror", error => note(`page error: ${error.message}`));
page.on("load", () => {
  loads++;
  note(`page load ${loads}: ${page.url().replace(/#token=.*/, "#token=<token>")}`);
});
await context.addInitScript(() => {
  document.addEventListener("securitypolicyviolation", event => {
    console.error(`csp violation: ${event.violatedDirective} ${event.blockedURI}`);
  });
});
const violations = () => consoleLines.filter(line => line.includes("csp violation") || line.includes("Content Security Policy"));

let viteRecord;
const apps = [];
try {
  // fresh
  const first = startApp("run1");
  apps.push(first);
  const line1 = (await waitFor(first, addressLine)).match;
  check(line1[4] === "a new dev session", `run1 is a new dev session (${line1[4]})`);
  await waitFor(first, /Development server (ready|.*reused)/);
  await sleep(1_000);
  check(openedLines().length === 1 && openedLines()[0] === line1[1], `run1 asked once to open its address (${openedLines().length})`);
  check(existsSync(record), "the record exists in obj/");

  await page.goto(openedLines()[0]);
  await page.getByRole("heading", { name: "Start your notebook" }).waitFor({ timeout: 30_000 });
  await page.getByRole("button", { name: "New page" }).first().click();
  const dialog = page.getByRole("dialog", { name: "New page" });
  await dialog.getByLabel("Title").fill("Groceries");
  await dialog.getByRole("button", { name: "Create page" }).click();
  await page.getByRole("heading", { name: "Groceries", level: 2 }).waitFor({ timeout: 10_000 });
  check(page.url().endsWith("/") && !page.url().includes("token"), "the token left the address bar");
  check(first.output.some(entry => /ui-ready/.test(entry.line)), "run1's page reported ui-ready");
  viteRecord = JSON.parse(readFileSync(join(options.project, "obj", "spar-dev-server.json"), "utf8"));

  // restart
  const loadsBefore = loads;
  await stopApp(first, options.signal ? `SIG${options.signal}` : undefined);
  await sleep(1_500);
  const second = startApp("run2");
  apps.push(second);
  const line2 = (await waitFor(second, addressLine)).match;
  const listening = (await waitFor(second, /loopback listening/)).at;
  check(line2[2] === line1[2] && line2[3] === line1[3], `run2 kept the port and the token (${line2[2]})`);
  check(/reloads by itself/.test(line2[4]), `run2 continues the dev session (${line2[4]})`);
  const reloadDeadline = performance.now() + 30_000;
  while (loads === loadsBefore && performance.now() < reloadDeadline) await sleep(25);
  const reloadedAt = performance.now();
  check(loads === loadsBefore + 1, `the page reloaded by itself once (${loads - loadsBefore} loads, ${(reloadedAt - listening).toFixed(0)} ms after run2 listened)`);
  await page.getByRole("button", { name: /^Groceries/ }).waitFor({ timeout: 30_000 });
  check(true, "after the reload the page lists the page created before (the bridge of run2)");
  await sleep(3_000);
  check(loads === loadsBefore + 1, `no further reload in 3 s (${loads - loadsBefore})`);
  check(openedLines().length === 1, `nothing more was opened (${openedLines().length})`);

  // refusals
  const origin = `http://127.0.0.1:${line2[2]}`;
  const hello = "{\"type\":\"hello\",\"protocol\":1,\"types\":[\"open\"],\"contracts\":\"0123456789abcdef\"}\t";
  check(await request(`${origin}/_spar/channel`, { method: "POST", headers: { Authorization: "Bearer AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" }, body: hello }) === 401, "a wrong token is refused (401)");
  check(await request(`${origin}/_spar/session`, { method: "POST" }) === 401, "no token is refused (401)");
  check(await request(`${origin}/_spar/session`, { method: "POST", headers: { Authorization: `Bearer ${line2[3]}`, Origin: "http://evil.example" } }) === 403, "another origin is refused (403)");
  const host = spawnSync(windows ? "curl.exe" : "curl", ["-s", "-o", "/dev/null", "-w", "%{http_code}", "-H", `Host: evil.example:${line2[2]}`, `${origin}/`], { encoding: "utf8" });
  check(host.stdout.trim() === "403", `another Host is refused (${host.stdout.trim()})`);
  check(await request(`${origin}/_spar/dev-server/retry`, { method: "POST", headers: { Origin: "http://evil.example" } }) === 403, "Retry from another origin is refused (403)");
  await stopApp(second);

  // override
  const recordBefore = readFileSync(record, "utf8");
  const port = await freePort();
  const token = "b3ZlcnJpZGRlbi10b2tlbi1mb3ItdGVzdHM";
  const third = startApp("run3", { SPAR_LOOPBACK_PORT: String(port), SPAR_LOOPBACK_TOKEN: token });
  apps.push(third);
  const line3 = (await waitFor(third, addressLine)).match;
  check(line3[2] === String(port) && line3[3] === token, `run3 used the environment's port and token (${line3[4]})`);
  check(await request(`http://127.0.0.1:${port}/_spar/session`, { method: "POST", headers: { Authorization: `Bearer ${token}` } }) === 200, "the environment's token is accepted");
  await sleep(1_000);
  check(openedLines().length === 1, "run3 opened nothing");
  await stopApp(third);
  check(readFileSync(record, "utf8") === recordBefore, "run3 left the record as it was");
  const bad = startApp("run3b", { SPAR_LOOPBACK_TOKEN: "short" });
  apps.push(bad);
  for (let i = 0; i < 400 && !bad.exited; i++) await sleep(50);
  check(bad.output.some(entry => /SPAR_LOOPBACK_TOKEN is not base64url/.test(entry.line)), `a malformed SPAR_LOOPBACK_TOKEN stops the start (exit ${bad.exited?.code})`);

  // new-day
  const fiveHoursAgo = new Date(Date.now() - 5 * 3600_000);
  utimesSync(record, fiveHoursAgo, fiveHoursAgo);
  const consoleBefore = consoleLines.length;
  const loadsBeforeNewDay = loads;
  const fourth = startApp("run4");
  apps.push(fourth);
  const line4 = (await waitFor(fourth, addressLine)).match;
  check(line4[2] === line1[2] && line4[3] !== line1[3], `run4 kept the port with a new token (${line4[4]})`);
  await sleep(4_000);
  check(openedLines().length === 2 && openedLines()[1] === line4[1], `run4 asked to open its address (${openedLines().length})`);
  check(loads === loadsBeforeNewDay, "the old page did not reload into the new session");
  check(consoleLines.slice(consoleBefore).some(line => line.includes("new dev session with a new token")), "the old page says its token is refused");
  await stopApp(fourth);

  const logFile = readdirSync(root, { recursive: true }).map(name => join(root, String(name))).find(path => path.endsWith("app.log"));
  check(logFile !== undefined, `app.log is in the run's root (${logFile?.slice(root.length)})`);
  const appLog = logFile ? readFileSync(logFile, "utf8") : "";
  check(violations().length === 0, `0 CSP violations in the page (${violations().length})`);
  check(!/CspViolation|Content Security Policy violation|csp violation/i.test(appLog), "0 CSP violations in app.log");
  note(`app.log: ${appLog.split("\n").length} lines, ${logFile ? statSync(logFile).size : 0} B`);
} catch (error) {
  failures.push(String(error));
  note(`FAIL ${error.stack ?? error}`);
} finally {
  await browser.close();
  for (const app of apps) {
    if (!app.exited) await stopApp(app);
  }

  if (viteRecord?.pid) {
    if (windows) spawnSync("taskkill", ["/PID", String(viteRecord.pid), "/T", "/F"]);
    else spawnSync("kill", [String(viteRecord.pid)]);
    note(`vite (pid ${viteRecord.pid}) ended`);
  }

  note(`${failures.length} failures`);
}

process.exit(failures.length === 0 ? 0 : 1);
