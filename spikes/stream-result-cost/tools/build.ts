// Bundles the benchmark (not minified, ES2022+ output, so classes and #private fields stay native) for Node and for
// the page. Output: obj/bundle/bench-node.mjs, bench-page.js and bench.html.
import { copyFileSync, mkdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { rolldown } from "rolldown";

const spike = join(dirname(fileURLToPath(import.meta.url)), "..");
const out = join(spike, "obj", "bundle");
mkdirSync(out, { recursive: true });

const node = await rolldown({ input: join(spike, "src", "node-main.ts"), platform: "node", logLevel: "warn" });
await node.write({ file: join(out, "bench-node.mjs"), format: "esm" });
await node.close();

const page = await rolldown({ input: join(spike, "src", "page-main.ts"), platform: "browser", logLevel: "warn" });
await page.write({ file: join(out, "bench-page.js"), format: "iife" });
await page.close();

copyFileSync(join(spike, "page", "bench.html"), join(out, "bench.html"));
console.log(`built ${out}`);
