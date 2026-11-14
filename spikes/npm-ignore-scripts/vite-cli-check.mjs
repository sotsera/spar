// Starts the Vite CLI (node_modules/.bin target) for <frontend> on <port>, fetches the page and a module, then stops
// the process it started and waits for it to exit.
// Usage: node vite-cli-check.mjs <frontend-folder> <port>
import { spawn } from "node:child_process";
import { join, resolve } from "node:path";

const frontend = resolve(process.argv[2]);
const port = Number(process.argv[3] ?? 5293);
const ansi = /\x1b\[[0-9;]*m/g; // Vite prints the port in bold: strip colour codes before matching
const started = performance.now();
const child = spawn(process.execPath, [join(frontend, "node_modules", "vite", "bin", "vite.js"), "--port", String(port),
  "--strictPort", "--host", "127.0.0.1"], { cwd: frontend, stdio: ["ignore", "pipe", "pipe"] });
let output = "";
const ready = new Promise((resolveReady, reject) => {
  const onData = (chunk) => {
    output += chunk;
    if (output.replace(ansi, "").includes(`:${port}/`)) resolveReady();
  };
  child.stdout.on("data", onData);
  child.stderr.on("data", onData);
  child.on("exit", (code) => reject(new Error(`vite exited early with ${code}: ${output}`)));
  setTimeout(() => reject(new Error("timeout waiting for vite")), 30000).unref();
});
const result = { frontend, pid: child.pid };
try {
  await ready;
  result.readyMs = Math.round(performance.now() - started);
  for (const path of ["/", "/src/main.tsx"]) {
    const response = await fetch(`http://127.0.0.1:${port}${path}`);
    result[path] = { status: response.status, bytes: (await response.text()).length };
  }
} finally {
  const exited = new Promise((r) => child.once("exit", (code, signal) => r({ code, signal })));
  child.kill();
  result.exit = await exited;
}
console.log(JSON.stringify(result));
