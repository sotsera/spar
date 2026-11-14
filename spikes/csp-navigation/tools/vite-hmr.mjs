// Usage: node tools/vite-hmr.mjs <label>   (switches through the environment: SPAR_TT=1, SPAR_HMR_CONNECT=1)
// Starts the Vite dev server with the Spar security plugin, opens the React app in Playwright's headless Chromium,
// then edits a component, a CSS file and introduces/fixes a syntax error, measuring each update and recording every
// CSP violation. The source files are restored at the end. Log: artifacts/csp-navigation/vite/<label>.log
import { appendFileSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const here = join(dirname(fileURLToPath(import.meta.url)), "..");
const frontend = join(here, "frontend");
const label = process.argv[2] ?? "dev";
const outDir = join(here, "../../artifacts/csp-navigation/vite");
mkdirSync(outDir, { recursive: true });
const log = join(outDir, `${label}.log`);
writeFileSync(log, "");
process.env.SPAR_VITE_LOG = join(outDir, `${label}.jsonl`);
writeFileSync(process.env.SPAR_VITE_LOG, "");
const write = line => {
  appendFileSync(log, line + "\n");
  console.log(line.slice(0, 300));
};

const { createServer } = await import("../frontend/node_modules/vite/dist/node/index.js");
const { chromium } = await import("../frontend/node_modules/playwright-core/index.mjs");
const server = await createServer({ root: frontend, configFile: join(frontend, "vite.config.ts"), logLevel: "warn" });
await server.listen();
const url = "http://127.0.0.1:5273/?stay=1";
const response = await fetch(url);
write("DEV_CSP " + response.headers.get("content-security-policy"));
write("DEV_HTML_NONCES " + JSON.stringify([...(await response.text()).matchAll(/nonce="([^"]+)"/g)].map(m => m[1])));

const appFile = join(frontend, "src/App.tsx");
const cssFile = join(frontend, "src/App.css");
const appSource = readFileSync(appFile, "utf8");
const cssSource = readFileSync(cssFile, "utf8");
const browser = await chromium.launch({ headless: true });
const page = await browser.newPage();
const violations = [];
page.on("console", m => {
  const text = m.text();
  if (/Content Security Policy|Trusted Type|TrustedHTML|TrustedScript/i.test(text)) violations.push(text.slice(0, 300));
  write(`CONSOLE ${m.type()} ${text.slice(0, 400)}`);
});
page.on("pageerror", e => write("PAGE_ERROR " + e.message));
const poll = async (check, timeout = 8000) => {
  const started = Date.now();
  while (Date.now() - started < timeout) {
    if (await page.evaluate(check).catch(() => false)) return Date.now() - started;
    await new Promise(r => setTimeout(r, 10));
  }
  return -1;
};

try {
  await page.goto(url);
  write("BOOT_MS " + (await poll(() => document.getElementById("lazy") !== null)));
  const bootId = await page.evaluate(() => window.__bootId);
  await page.click("#count");
  await page.click("#count");

  writeFileSync(appFile, appSource.replace("Spar CSP spike v1", "Spar CSP spike v2"));
  const componentMs = await poll(() => document.getElementById("title")?.textContent === "Spar CSP spike v2");
  const state = await page.evaluate(() => document.getElementById("count")?.textContent);
  write(`HMR_COMPONENT ms=${componentMs} stateAfter="${state}" sameBoot=${(await page.evaluate(() => window.__bootId)) === bootId}`);

  writeFileSync(cssFile, cssSource.replace("rgb(20, 30, 40)", "rgb(50, 60, 70)"));
  const cssMs = await poll(() => getComputedStyle(document.querySelector(".app")).color === "rgb(50, 60, 70)");
  write(`HMR_CSS ms=${cssMs} sameBoot=${(await page.evaluate(() => window.__bootId)) === bootId}`);

  writeFileSync(appFile, appSource.replace("Spar CSP spike v1", "Spar CSP spike v2").replace("export default function App() {", "export default function App() {\n  const broken = ;"));
  const overlayMs = await poll(() => document.querySelector("vite-error-overlay")?.shadowRoot?.querySelector(".window") != null);
  const overlay = await page.evaluate(() => {
    const root = document.querySelector("vite-error-overlay")?.shadowRoot;
    const box = root?.querySelector(".window");
    if (!box) return null;
    const style = getComputedStyle(box);
    return { styled: style.position !== "static" || style.backgroundColor !== "rgba(0, 0, 0, 0)", position: style.position, background: style.backgroundColor, message: root.querySelector(".message")?.textContent?.slice(0, 80) };
  });
  write(`OVERLAY ms=${overlayMs} ${JSON.stringify(overlay)}`);

  await new Promise(r => setTimeout(r, 1000));
  writeFileSync(appFile, appSource.replace("Spar CSP spike v1", "Spar CSP spike v3"));
  const fixedMs = await poll(() => document.getElementById("title")?.textContent === "Spar CSP spike v3" && !document.querySelector("vite-error-overlay"));
  const after = await page.evaluate(() => ({ title: document.getElementById("title")?.textContent, overlay: !!document.querySelector("vite-error-overlay") }));
  write(`OVERLAY_CLEARED ms=${fixedMs} ${JSON.stringify(after)} sameBoot=${(await page.evaluate(() => window.__bootId)) === bootId}`);
  const sdkViolations = await page.evaluate(() => document.title && window.__sparOrder);
  write("ORDER " + JSON.stringify(sdkViolations));
} finally {
  writeFileSync(appFile, appSource);
  writeFileSync(cssFile, cssSource);
  write("CSP_CONSOLE_VIOLATIONS " + violations.length);
  for (const v of violations) write("  " + v);
  await browser.close();
  await server.close();
}
