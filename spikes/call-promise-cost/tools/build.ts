// Bundles the benchmark (not minified, ES2022+ output, so classes and #private fields stay native) for Node and for
// the page. Output: obj/bundle/bench-node.mjs, native-alone.mjs, bench-page.js (+ the page HTML files beside it).
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

const alone = await rolldown({ input: join(spike, "src", "native-alone.ts"), platform: "node", logLevel: "warn" });
await alone.write({ file: join(out, "native-alone.mjs"), format: "esm" });
await alone.close();

const page = await rolldown({ input: join(spike, "src", "page-main.ts"), platform: "browser", logLevel: "warn" });
await page.write({ file: join(out, "bench-page.js"), format: "iife" });
await page.close();

for (const file of ["bench.html", "memory-probe.html"]) copyFileSync(join(spike, "page", file), join(out, file));
console.log(`built ${out}`);
