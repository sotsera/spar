// Runs js/engine-probe.html in Microsoft Edge (Chromium, the engine WebView2 uses) through the Playwright driver that
// the Microsoft.Playwright NuGet package already carries (no download), and prints the feature results.
// Usage (from spikes/wire-details): node js/engine-probe.mjs
import { existsSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const packages = process.env.NUGET_PACKAGES ?? join(process.env.USERPROFILE ?? "", ".nuget", "packages");
const driver = join(packages, "microsoft.playwright", "1.64.1", ".playwright", "package", "index.js");
if (!existsSync(driver)) {
  console.log(`Playwright driver not found at ${driver}; skipped`);
  process.exit(0);
}
const { chromium } = (await import(pathToFileURL(driver).href)).default;
const page = pathToFileURL(fileURLToPath(new URL("./engine-probe.html", import.meta.url))).href;
const browser = await chromium.launch({ channel: "msedge", headless: true });
try {
  const tab = await browser.newPage();
  await tab.goto(page);
  console.log(await tab.locator("#out").textContent());
  console.log(`browser version ${browser.version()}`);
} finally {
  await browser.close();
}
