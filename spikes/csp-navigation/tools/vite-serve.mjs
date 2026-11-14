// Usage: node tools/vite-serve.mjs <label>   Serves the frontend with the Spar plugin (dev) until stdin closes.
// Page results go to artifacts/csp-navigation/vite/<label>.jsonl. SPAR_ALLOW_DOCKER=1 lets the Linux container in.
import { mkdirSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const here = join(dirname(fileURLToPath(import.meta.url)), "..");
const outDir = join(here, "../../artifacts/csp-navigation/vite");
mkdirSync(outDir, { recursive: true });
process.env.SPAR_VITE_LOG = join(outDir, `${process.argv[2] ?? "serve"}.jsonl`);
writeFileSync(process.env.SPAR_VITE_LOG, "");
const { createServer } = await import("../frontend/node_modules/vite/dist/node/index.js");
const server = await createServer({ root: join(here, "frontend"), configFile: join(here, "frontend/vite.config.ts"), logLevel: "warn" });
await server.listen();
console.log("VITE_READY http://127.0.0.1:5273/");
process.stdin.resume();
process.stdin.on("end", () => void server.close().then(() => process.exit(0)));
