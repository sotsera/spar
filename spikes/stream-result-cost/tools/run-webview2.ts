// Runs the page benchmark in WebView2, using the already published NativeAOT host of spikes/desktop-origin
// (bin/dev: the build that has --extra-filter). That host is offscreen and never activated; its profile goes to
// artifacts/webview2-profiles/desktop-origin-stream-result-cost (the host only accepts names starting desktop-origin).
// This script serves obj/bundle/ on 127.0.0.1 (random port); the page POSTs its report to /results, then requests
// /_spar/done, which the host intercepts (through --extra-filter) and closes.
// The page is cross-origin isolated (COOP/COEP headers), which gives performance.now() its fine resolution.
// Usage: node tools/run-webview2.ts [--runs 3] [--quick] [--query mode=alloc --name alloc-webview2 --semi 64]
import { spawn } from "node:child_process";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { createServer } from "node:http";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const spike = join(dirname(fileURLToPath(import.meta.url)), "..");
const bundle = join(spike, "obj", "bundle");
const raw = join(spike, "results", "raw");
const hostExe = join(spike, "..", "desktop-origin", "Origin.Win", "bin", "dev", "Release", "net11.0", "win-x64", "publish", "Origin.Win.exe");
const args = process.argv.slice(2);
const runs = Number(args[args.indexOf("--runs") + 1] ?? 3) || 3;
const quick = args.includes("--quick");
const option = (name: string) => args.includes(name) ? args[args.indexOf(name) + 1] : undefined;
const page = option("--page") ?? "bench.html";
const extraQuery = option("--query");
const label = option("--name") ?? `webview2${quick ? "-quick" : ""}`;
const semi = option("--semi");
mkdirSync(raw, { recursive: true });

let received: ((body: string) => void) | undefined;
const server = createServer((request, response) => {
  const url = new URL(request.url ?? "/", "http://127.0.0.1");
  if (request.method === "POST" && url.pathname === "/results") {
    const chunks: Buffer[] = [];
    request.on("data", chunk => chunks.push(chunk));
    request.on("end", () => { response.writeHead(204).end(); received?.(Buffer.concat(chunks).toString("utf8")); });
    return;
  }

  const files: Record<string, string> = { "/bench.html": "text/html; charset=utf-8", "/bench-page.js": "text/javascript; charset=utf-8" };
  const type = files[url.pathname];
  if (request.method === "GET" && type) {
    response.writeHead(200, {
      "Content-Type": type,
      "Cache-Control": "no-store",
      "Cross-Origin-Opener-Policy": "same-origin",
      "Cross-Origin-Embedder-Policy": "require-corp",
      "Cross-Origin-Resource-Policy": "same-origin",
    }).end(readFileSync(join(bundle, url.pathname.slice(1))));
    return;
  }

  response.writeHead(404).end();
});
await new Promise<void>(resolve => server.listen(0, "127.0.0.1", resolve));
const port = (server.address() as { port: number }).port;
const origin = `http://127.0.0.1:${port}`;

for (let run = 1; run <= runs; run++) {
  const result = new Promise<string>(resolve => { received = resolve; });
  const hostArgs = [
    "--url", `${origin}/${page}?engine=webview2${quick ? "&quick=1" : ""}${extraQuery ? `&${extraQuery}` : ""}`,
    "--extra-filter", `${origin}/_spar/*`,
    "--profile", "desktop-origin-stream-result-cost",
    "--browser-args", `--enable-precise-memory-info ${semi ? `--js-flags="--expose-gc --min-semi-space-size=${semi}"` : "--js-flags=--expose-gc"}`,
    "--timeout", "900",
  ];
  const started = Date.now();
  const child = spawn(hostExe, hostArgs, { stdio: ["ignore", "pipe", "pipe"] });
  let log = "";
  child.stdout.on("data", chunk => { log += chunk; });
  child.stderr.on("data", chunk => { log += chunk; });
  const exit = new Promise<number | null>(resolve => child.on("exit", resolve));
  const body = await Promise.race([result, exit.then(() => undefined)]);
  const code = await exit;
  const name = `${label}-${run}`;
  // Keep only the host's summary lines (runtime version, exit); request lines would just repeat the two /_spar/ calls.
  const hostLines = log.split(/\r?\n/).filter(l => /^(RUNTIME|HOST|TIMEOUT|HOST-FAILURE|FILTER|NAVIGATE)/.test(l)).map(l => l.replace(/epoch=\d+ /, ""));
  writeFileSync(join(raw, `${name}.host.txt`), `exit=${code} seconds=${((Date.now() - started) / 1000).toFixed(1)}\n${hostLines.join("\n")}\n`);
  if (body === undefined) {
    console.error(`run ${run}: host exited (${code}) without a result`);
    process.exitCode = 1;
    continue;
  }

  const report = JSON.parse(body);
  const runtime = hostLines.find(l => l.startsWith("RUNTIME"));
  if (runtime && report.details) report.details.webview2Runtime = runtime.slice("RUNTIME ".length);
  writeFileSync(join(raw, `${name}.json`), JSON.stringify(report, null, 1));
  console.log(`run ${run}: exit ${code}, ${((Date.now() - started) / 1000).toFixed(1)} s, ${report.error ? `ERROR ${report.error}` : "ok"} -> results/raw/${name}.json`);
}

server.close();
