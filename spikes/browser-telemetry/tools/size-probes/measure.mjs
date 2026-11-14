// Builds each size probe (and the app's real telemetry module) as its own minified chunk with the app's Vite, then
// prints raw, gzip -9 and Brotli sizes. Usage, from the app's frontend folder:
//   node ../../../tools/size-probes/measure.mjs
import { pathToFileURL } from "node:url";
import { readdirSync, readFileSync } from "node:fs";
import { join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { brotliCompressSync, gzipSync } from "node:zlib";

const probes = fileURLToPath(new URL(".", import.meta.url));
const frontend = process.cwd();
const outDir = resolve(frontend, "../obj/size-probes");
const importer = join(frontend, "src", "main.tsx");
const { build } = await import(pathToFileURL(join(frontend, "node_modules", "vite", "dist", "node", "index.js")).href);

const entries = {
  "traces": join(probes, "traces.ts"),
  "traces-logs": join(probes, "traces-logs.ts"),
  "traces-instrumentations": join(probes, "traces-instrumentations.ts"),
  "app-telemetry-module": join(frontend, "src", "telemetry.ts"),
};

// One build per probe, each a single self-contained file, so each size is the whole cost of that combination.
for (const [name, entry] of Object.entries(entries)) {
  await build({
    root: frontend,
    logLevel: "warn",
    configFile: false,
    define: { __SPAR_TELEMETRY__: "true", __SPAR_BENCH__: "false" },
    plugins: [{
      // Bare imports in the probes resolve as if the app imported them.
      name: "resolve-from-app",
      async resolveId(source, from) {
        if (from?.replaceAll("\\", "/").startsWith(probes.replaceAll("\\", "/")) && !source.startsWith(".") && !source.startsWith("/")) {
          return this.resolve(source, importer, { skipSelf: true });
        }
        return null;
      },
    }],
    build: {
      outDir: join(outDir, name),
      emptyOutDir: true,
      lib: { entry, formats: ["es"], fileName: name },
      rolldownOptions: { output: { codeSplitting: false } },
      minify: true,
    },
  });

  const file = join(outDir, name, `${name}.js`);
  const bytes = readFileSync(file);
  console.log(`SIZE ${name} raw=${bytes.length} gzip=${gzipSync(bytes, { level: 9 }).length} br=${brotliCompressSync(bytes).length}`);
}