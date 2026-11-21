// The M1d step 1 run on Linux (docs/evidence/m1d-dev-server.md), the Linux side of DevRun.cs: starts the Debug
// Notebook's executable on the display in DISPLAY (wsl.sh's private Xvfb, never WSLg's) with HOME and every XDG folder
// under --root, its output in a file, no standard input or (--stdin-pipe) a pipe this run holds, then runs the same
// steps as DevRun.cs against the app's log, copying every new line of it into the run's log with the run's clock:
//   until:<text> sleep:<ms> marker:<n> violation:<n> worker css:<n> kill-vite close-stdin close
// and one more, key:<keys>, which sends a key to the window with xdotool through XTEST on that private display. `close`
// sends SIGTERM (the Linux host's session end). Every edited file is restored at the end.
//   node linux-run.mjs --exe <path> --root <dir> --log <file> --frontend <dir> --obj <dir> [--env K=V]... --steps "a;b"
import { spawn, spawnSync } from "node:child_process";
import { appendFileSync, closeSync, existsSync, mkdirSync, openSync, readFileSync, readSync, rmSync, statSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";

const options = { env: [] };
for (let i = 2; i < process.argv.length; i += 2) {
  const [name, value] = [process.argv[i].slice(2), process.argv[i + 1]];
  if (name === "env") options.env.push(value);
  else options[name] = value;
}

const started = performance.now();
const clock = () => (performance.now() - started).toFixed(1).padStart(10);
mkdirSync(dirname(options.log), { recursive: true });
writeFileSync(options.log, "");
const note = (text) => appendFileSync(options.log, `${clock()} | run: ${text}\n`);
const originals = new Map();

const root = options.root;
rmSync(root, { recursive: true, force: true });
const folders = { HOME: "home", XDG_DATA_HOME: "data", XDG_CONFIG_HOME: "config", XDG_CACHE_HOME: "cache", XDG_STATE_HOME: "state", TMPDIR: "tmp" };
const environment = { ...process.env };
for (const [variable, folder] of Object.entries(folders)) {
  environment[variable] = join(root, folder);
  mkdirSync(environment[variable], { recursive: true });
}
for (const pair of options.env) {
  const at = pair.indexOf("=");
  environment[pair.slice(0, at)] = pair.slice(at + 1);
}

const output = join(root, "output.txt");
const outputFd = openSync(output, "w");
note(`start ${options.exe} env [${options.env.join(" ")}]`);
const app = spawn(options.exe, [], {
  env: environment,
  stdio: [options["stdin-pipe"] === "true" ? "pipe" : "ignore", outputFd, outputFd],
});
closeSync(outputFd);
let exited = null;
app.on("exit", (code, signal) => { exited = { code, signal }; });

const appLog = join(root, "data", "com.example.Notebook.Development", "logs", "app.log");
const tails = new Map();
const lines = [];
function tail(path, source, keep) {
  if (!existsSync(path)) return;
  const from = tails.get(path) ?? 0;
  const size = statSync(path).size;
  if (size <= from) return;
  const fd = openSync(path, "r");
  const buffer = Buffer.alloc(size - from);
  readSync(fd, buffer, 0, buffer.length, from);
  closeSync(fd);
  const text = buffer.toString("utf8");
  const end = text.lastIndexOf("\n");
  if (end < 0) return;
  for (const line of text.slice(0, end).split("\n")) {
    if (keep) lines.push(line);
    appendFileSync(options.log, `${clock()} | ${source}: ${line}\n`);
  }
  tails.set(path, from + Buffer.byteLength(text.slice(0, end + 1)));
}
const pump = () => { tail(appLog, "app", true); tail(output, "out", false); };
const pause = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

function original(path) {
  if (!originals.has(path)) originals.set(path, readFileSync(path, "utf8"));
  return originals.get(path);
}

const sidebar = join(options.frontend, "src", "components", "Sidebar.tsx");
const anchor = "  const { summaries, selected, query, onQuery, onSelect, onNew } = props;\n";
function marker(n) {
  const text = original(sidebar)
    .replace('import { forwardRef, useMemo } from "react";', 'import { forwardRef, useEffect, useMemo, useRef } from "react";')
    .replace(anchor, anchor + "  const sparMount = useRef(Math.random().toString(36).slice(2));\n" +
      `  useEffect(() => { new Image().src = \`/__spar-hmr/${n}/\${sparMount.current}/\${Date.now()}\`; });\n`);
  writeFileSync(sidebar, text);
  note(`marker ${n} written at ${Date.now()}`);
}
function violation(n) {
  const text = original(sidebar)
    .replace('import { forwardRef, useMemo } from "react";', 'import { forwardRef, useEffect, useMemo } from "react";')
    .replace(anchor, anchor + `  useEffect(() => { new Image().src = "https://spar-violation.invalid/${n}"; });\n`);
  writeFileSync(sidebar, text);
  note(`violation ${n} written at ${Date.now()}`);
}
const added = [];
function worker() {
  const script = join(options.frontend, "src", "spar-worker-check.js");
  writeFileSync(script, 'let outcome;\ntry { outcome = String((0, eval)("1 + 1")); } catch (error) { outcome = error.name; }\npostMessage(outcome);\n');
  added.push(script);
  const text = original(sidebar)
    .replace('import { forwardRef, useMemo } from "react";', 'import { forwardRef, useEffect, useMemo } from "react";')
    .replace(anchor, anchor +
      "  useEffect(() => {\n" +
      '    const policy = trustedTypes.createPolicy("spar-worker-check", { createScriptURL: (url: string) => url });\n' +
      '    const worker = new Worker(policy.createScriptURL("/src/spar-worker-check.js") as unknown as string, { type: "module" });\n' +
      "    worker.onmessage = event => { new Image().src = `/__spar-worker/${String(event.data)}`; worker.terminate(); };\n" +
      "  }, []);\n");
  writeFileSync(sidebar, text);
  note(`worker check written at ${Date.now()}`);
}
function css(n) {
  const path = join(options.frontend, "src", "styles.css");
  const colour = `#2f5b${(0xd0 + (n % 16)).toString(16)}`;
  writeFileSync(path, original(path).replace("--color-accent: #2f5bd3;", `--color-accent: ${colour};`));
  note(`css ${n} (${colour}) written at ${Date.now()}`);
}

let result = 0;
try {
  for (const step of options.steps.split(";").map((s) => s.trim()).filter(Boolean)) {
    const at = step.indexOf(":");
    const [name, value] = at < 0 ? [step, ""] : [step.slice(0, at), step.slice(at + 1)];
    if (name === "until") {
      const seen = lines.length;
      const deadline = performance.now() + 60_000;
      let found = false;
      while (!found && performance.now() < deadline && exited === null) {
        pump();
        found = lines.slice(seen).some((line) => line.includes(value));
        if (!found) await pause(10);
      }
      pump();
      found ||= lines.slice(seen).some((line) => line.includes(value));
      note(found ? `found '${value}'` : `'${value}' NOT found`);
      if (!found) result = 1;
    } else if (name === "sleep") {
      const until = performance.now() + Number(value);
      while (performance.now() < until) { pump(); await pause(10); }
    } else if (name === "marker") marker(Number(value));
    else if (name === "violation") violation(Number(value));
    else if (name === "worker") worker();
    else if (name === "css") css(Number(value));
    else if (name === "kill-vite") {
      const pid = JSON.parse(readFileSync(join(options.obj, "spar-dev-server.json"), "utf8")).pid;
      // Vite's tree: the bundler's workers below it, by their parent ids.
      const tree = [pid];
      for (let i = 0; i < tree.length; i++) {
        const children = spawnSync("pgrep", ["-P", String(tree[i])], { encoding: "utf8" }).stdout.split("\n").filter(Boolean).map(Number);
        tree.push(...children);
      }
      for (const id of tree) { try { process.kill(id, "SIGKILL"); } catch { /* gone */ } }
      note(`Vite (pid ${pid}) ended with its tree (${tree.length} processes) at ${Date.now()}`);
    } else if (name === "key") {
      const sent = spawnSync("xdotool", ["key", value], { env: environment, encoding: "utf8" });
      note(`key ${value} sent with xdotool (exit ${sent.status}${sent.stderr ? `: ${sent.stderr.trim()}` : ""})`);
    } else if (name === "close-stdin") {
      app.stdin?.end();
      note(`standard input's writing end closed at ${Date.now()}`);
    } else if (name === "close") {
      note("SIGTERM");
      app.kill("SIGTERM");
      const deadline = performance.now() + 30_000;
      while (exited === null && performance.now() < deadline) { pump(); await pause(10); }
      await pause(300);
      pump();
      note(exited === null ? "did not exit within 30 s" : `exit ${exited.code ?? exited.signal}`);
      if (exited === null) result = 1;
    }
  }
} finally {
  for (const [path, text] of originals) {
    writeFileSync(path, text);
    note(`restored ${path.split("/").pop()}`);
  }
  for (const path of added) {
    rmSync(path, { force: true });
    note(`removed ${path.split("/").pop()}`);
  }
  if (exited === null) {
    note("the app was still running: ended");
    app.kill("SIGKILL");
  }
}
process.exit(result);
