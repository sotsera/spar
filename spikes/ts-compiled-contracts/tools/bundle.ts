// Bundles the consumer with Vite (production build, rolldown) for each variant and checks tree shaking.
// Entries: main.ts (copyJobs subpath), main-barrel.ts (copyJobs through the root barrel), main-audit.ts (control: imports auditLog).
// A second pass removes "sideEffects": false from the package to see whether it matters here.
// Usage: node tools/bundle.ts. Run tools/build.ts first. Writes obj/bundle.md.
import { readdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { gzipSync } from "node:zlib";
import { build, version } from "vite";

const spikeRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const marker = "auditLog.list__UNUSED_CONTRACT_MARKER";
const out = [`# Vite ${version} bundles`, "", "| variant | sideEffects | entry | auditLog in bundle | copyJobs.plan in bundle | SDK (\`unknownErrorCase\`) | bytes | gzip |", "|---|---|---|---|---|---|---|---|"];

for (const variant of ["ts", "js", "js-csmap"]) {
  const frontend = join(spikeRoot, "obj", "variants", variant, "frontend");
  const manifest = join(spikeRoot, "obj", "variants", variant, "obj", "spar", "client", "package.json");
  const original = readFileSync(manifest, "utf8");
  try {
    for (const sideEffects of [false, "(absent)"]) {
      if (sideEffects !== false) {
        const json = JSON.parse(original) as Record<string, unknown>;
        delete json["sideEffects"];
        writeFileSync(manifest, JSON.stringify(json, null, 2) + "\n");
      }

      for (const entry of ["main", "main-barrel", "main-audit"]) {
        const outDir = join(frontend, "dist", `${entry}-${sideEffects === false ? "se-false" : "se-absent"}`);
        await build({
          root: frontend, configFile: false, logLevel: "silent",
          build: { outDir, emptyOutDir: true, minify: true, modulePreload: false, rolldownOptions: { input: join(frontend, "src", `${entry}.ts`) } },
        });
        const js = readdirSync(join(outDir, "assets")).filter(f => f.endsWith(".js")).map(f => readFileSync(join(outDir, "assets", f), "utf8")).join("\n");
        out.push(`| ${variant} | ${sideEffects} | ${entry} | ${js.includes(marker) ? "yes" : "no"} | ${js.includes("copyJobs.plan") ? "yes" : "no"} | ${js.includes("unknownErrorCase") ? "yes" : "no"} | ${js.length} | ${gzipSync(js, { level: 9 }).length} |`);
      }
    }
  } finally {
    writeFileSync(manifest, original);
  }
}

const text = out.join("\n") + "\n";
writeFileSync(join(spikeRoot, "obj", "bundle.md"), text);
console.log(text);
