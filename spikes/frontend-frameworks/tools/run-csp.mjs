// Serves each built app with the strict CSP of plan 3b (as a response header, with the other host headers) and runs
// it in headless Chromium. Collects the in-page test results, CSP violations (injected listener + report-uri),
// console errors and page errors. Usage, from the spike folder:
//   node tools/run-csp.mjs <name>=<dir relative to the spike folder> ...
// Writes results/<name>.json (one object per policy) and prints a summary.
import { createServer } from "node:http";
import { readFile, writeFile, mkdir } from "node:fs/promises";
import { extname, join, normalize, resolve } from "node:path";
import { chromium } from "playwright";

const base = [
  "default-src 'none'", "script-src 'self'", "style-src 'self'", "img-src 'self'", "font-src 'self'",
  "connect-src https://app.localhost", "worker-src 'self'", "manifest-src 'self'",
  "base-uri 'none'", "form-action 'none'", "frame-ancestors 'none'", "object-src 'none'",
  "report-uri /_spar/csp-report",
].join("; ");
const policies = { base, trustedTypes: `${base}; require-trusted-types-for 'script'` };

const types = {
  ".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8", ".mjs": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8", ".svg": "image/svg+xml", ".json": "application/json", ".ico": "image/x-icon",
  ".png": "image/png", ".woff2": "font/woff2", ".txt": "text/plain; charset=utf-8", ".map": "application/json",
};

function serve(root, policy, reports) {
  const server = createServer(async (req, res) => {
    const url = new URL(req.url ?? "/", "http://x");
    if (req.method === "POST" && url.pathname === "/_spar/csp-report") {
      let body = "";
      for await (const chunk of req) body += chunk;
      reports.push(JSON.parse(body));
      res.writeHead(204).end();
      return;
    }
    const path = url.pathname === "/" ? "/index.html" : decodeURIComponent(url.pathname);
    const file = normalize(join(root, path));
    if (!file.startsWith(root)) { res.writeHead(403).end(); return; }
    try {
      const body = await readFile(file);
      const headers = {
        "Content-Type": types[extname(file)] ?? "application/octet-stream",
        "Cache-Control": "no-store",
        "X-Content-Type-Options": "nosniff",
        "Referrer-Policy": "no-referrer",
        "Cross-Origin-Opener-Policy": "same-origin",
        "Cross-Origin-Resource-Policy": "same-origin",
      };
      if (extname(file) === ".html") headers["Content-Security-Policy"] = policy;
      res.writeHead(200, headers).end(body);
    } catch {
      res.writeHead(404, { "Content-Type": "text/plain" }).end("not found");
    }
  });
  return new Promise(ok => server.listen(0, "127.0.0.1", () => ok(server)));
}

// Stands in for the host-injected script: it runs before any page script (plan 3b, "Where to listen").
const listener = () => {
  window.__violations = [];
  document.addEventListener("securitypolicyviolation", e => {
    window.__violations.push({ directive: e.effectiveDirective, blocked: e.blockedURI, sample: e.sample, source: e.sourceFile, line: e.lineNumber });
  });
};

// <name>=<dir>[#<page>]: the page defaults to index.html.
const targets = process.argv.slice(2).map(arg => {
  const [name, rest] = arg.split("=");
  const [dir, page = ""] = rest.split("#");
  return { name, dir: resolve(dir), page };
});
const browser = await chromium.launch({ headless: true });
await mkdir("results", { recursive: true });
try {
  for (const { name, dir, page: entry } of targets) {
    const out = {};
    for (const [policyName, policy] of Object.entries(policies)) {
      const reports = [];
      const server = await serve(dir, policy, reports);
      const context = await browser.newContext();
      const page = await context.newPage();
      const consoleMessages = [], pageErrors = [];
      page.on("console", m => { if (m.type() === "error" || m.type() === "warning") consoleMessages.push(`${m.type()}: ${m.text()}`.slice(0, 400)); });
      page.on("pageerror", e => pageErrors.push(String(e).slice(0, 400)));
      await page.addInitScript(listener);
      const started = Date.now();
      await page.goto(`http://127.0.0.1:${server.address().port}/${entry}`);
      let done = false;
      while (!done && Date.now() - started < Number(process.env.RUN_TIMEOUT_MS ?? 90_000)) {
        done = await page.evaluate(() => window.__done === true).catch(() => false);
        if (!done) await new Promise(r => setTimeout(r, 100));
      }
      const results = await page.evaluate(() => window.__results ?? []).catch(() => []);
      const violations = await page.evaluate(() => window.__violations ?? []).catch(() => []);
      await new Promise(r => setTimeout(r, 300)); // let report-uri posts arrive
      await context.close();
      server.close();
      out[policyName] = {
        done, ms: Date.now() - started,
        passed: results.filter(r => r.ok).length, failed: results.filter(r => !r.ok).length,
        results, violations,
        reports: reports.map(r => { const b = r["csp-report"] ?? r; return { directive: b["effective-directive"] ?? b["violated-directive"], blocked: b["blocked-uri"], sample: b["script-sample"] }; }),
        consoleMessages, pageErrors,
      };
      console.log(`${name} [${policyName}] done=${done} pass=${out[policyName].passed} fail=${out[policyName].failed} violations=${violations.length} reports=${reports.length} pageErrors=${pageErrors.length}`);
      for (const r of results.filter(r => !r.ok)) console.log(`  FAIL ${r.name}: ${String(r.detail).split("\n")[0]}`);
      for (const v of violations) console.log(`  CSP ${v.directive} blocked=${v.blocked} sample=${(v.sample ?? "").slice(0, 80)} at ${v.source}:${v.line}`);
      for (const e of pageErrors) console.log(`  PAGEERROR ${e}`);
    }
    await writeFile(`results/${name}.json`, JSON.stringify(out, (_, v) => typeof v === "bigint" ? `${v}n` : v, 2));
  }
} finally {
  await browser.close();
}
