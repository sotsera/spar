// Bundles each form's entry with rolldown (minified) and reports raw and gzip sizes; also bundles the perf script.
// Usage: node bench/build.ts [perf|behaviour] (no argument: the size entries)
import { gzipSync } from "node:zlib";
import { readFileSync, readdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { rolldown } from "rolldown";

const here = dirname(fileURLToPath(import.meta.url));
const out = join(here, "..", "obj", "bundle");

const nodeEntry = process.argv[2];
if (nodeEntry) {
  const bundle = await rolldown({ input: join(here, `${nodeEntry}.ts`), platform: "node", logLevel: "silent" });
  await bundle.write({ file: join(out, `${nodeEntry}.mjs`), format: "esm" });
  await bundle.close();
  console.log(join(out, `${nodeEntry}.mjs`));
} else {
  for (const entry of readdirSync(here).filter(f => f.startsWith("entry-")).sort()) {
    const bundle = await rolldown({ input: join(here, entry), platform: "browser", logLevel: "silent" });
    const file = join(out, entry.replace(/\.ts$/, ".js"));
    await bundle.write({ file, format: "esm", minify: true });
    await bundle.close();
    const code = readFileSync(file);
    console.log(`${entry.padEnd(22)} ${String(code.length).padStart(6)} B  gzip ${String(gzipSync(code, { level: 9 }).length).padStart(5)} B`);
  }
}
