// Exercises Vite (installed in <frontend>/node_modules) without install scripts having run:
//  1. a production build of ./probe (TypeScript + CSS with nesting, so Oxc/Rolldown and Lightning CSS both run);
//  2. the dev server for <frontend> on <port>: start, fetch the page, a transformed .tsx module and the client, stop.
// Usage: node vite-check.mjs <frontend-folder> <port>
import { createRequire } from "node:module";
import { readFileSync, readdirSync } from "node:fs";
import { join, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const frontend = resolve(process.argv[2]);
const port = Number(process.argv[3] ?? 5291);
const here = fileURLToPath(new URL(".", import.meta.url));
const require = createRequire(join(frontend, "package.json"));
const vite = await import(pathToFileURL(require.resolve("vite")).href);
const result = { frontend, vite: vite.version };

// 1. Probe build: CSS goes through Lightning CSS (transform and minify), TypeScript through Oxc.
const probeRoot = join(here, "probe");
const outDir = join(frontend, "..", "probe-dist");
let t = performance.now();
await vite.build({ root: probeRoot, logLevel: "silent", configFile: false, build: { outDir, emptyOutDir: true, target: "chrome100" }, css: { transformer: "lightningcss" } });
result.probeBuildMs = Math.round(performance.now() - t);
const assets = readdirSync(join(outDir, "assets"));
result.probeCss = readFileSync(join(outDir, "assets", assets.find((f) => f.endsWith(".css"))), "utf8").trim();
result.probeJs = readFileSync(join(outDir, "assets", assets.find((f) => f.endsWith(".js"))), "utf8").trim().slice(0, 200);

// 2. Dev server with the app's own vite.config.ts.
t = performance.now();
const server = await vite.createServer({ root: frontend, logLevel: "silent", server: { port, strictPort: true, host: "127.0.0.1" } });
await server.listen();
result.devStartMs = Math.round(performance.now() - t);
try {
  const base = `http://127.0.0.1:${port}`;
  const fetched = {};
  for (const path of ["/", "/src/main.tsx", "/@vite/client"]) {
    const response = await fetch(base + path);
    const body = await response.text();
    fetched[path] = { status: response.status, bytes: body.length, head: body.slice(0, 120).replace(/\s+/g, " ") };
  }
  result.dev = fetched;
} finally {
  await server.close();
  result.devClosed = true;
}

console.log(JSON.stringify(result, null, 2));
