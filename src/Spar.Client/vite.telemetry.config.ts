// Prebundles the page's telemetry chunk (src/telemetry) with the OpenTelemetry packages it uses into
// obj/dist/telemetry/index.js, after tsc compiled the rest of the SDK: an app's @spar/app package then carries it whole,
// with no npm dependency of its own, and the app's `vite build` turns it into a lazy chunk (decision "M2d's decide-first
// questions", item 3). The bundled packages' licences go beside it in licenses.md, in the sections of Vite's own
// `.vite/license.md`, which Spar's Vite plugin extends with them when an app's bundle has the chunk.
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { defineConfig, type Plugin } from "vite";

interface BundledPackage {
  readonly name: string;
  readonly version: string;
  readonly license: string;
  readonly folder: string;
}

export default defineConfig({
  configFile: false,
  logLevel: "warn",
  cacheDir: "obj/vite-telemetry",
  plugins: [licenses()],
  build: {
    lib: { entry: "src/telemetry/index.ts", formats: ["es"], fileName: () => "index.js" },
    outDir: "obj/dist/telemetry",
    emptyOutDir: false,
    minify: false,
    sourcemap: true,
    target: "es2022",
    reportCompressedSize: false,
  },
});

function licenses(): Plugin {
  return {
    name: "spar-telemetry-licenses",
    generateBundle(_options, bundle) {
      const packages = new Map<string, BundledPackage>();
      for (const output of Object.values(bundle)) {
        if (output.type === "chunk") {
          addPackages(packages, output.moduleIds);
        }
      }

      const sorted = [...packages.values()].sort((a, b) => a.name.localeCompare(b.name));
      this.emitFile({ type: "asset", fileName: "licenses.md", source: sorted.map(section).join("") });
    },
  };
}

/** The packages under node_modules whose modules are in the chunk. */
function addPackages(packages: Map<string, BundledPackage>, moduleIds: readonly string[]): void {
  for (const id of moduleIds) {
    const folder = packageFolder(id);
    if (folder === undefined || packages.has(folder)) {
      continue;
    }

    const manifest = JSON.parse(readFileSync(join(folder, "package.json"), "utf8")) as Record<string, unknown>;
    packages.set(folder, {
      name: String(manifest["name"]),
      version: String(manifest["version"]),
      license: String(manifest["license"] ?? "UNKNOWN"),
      folder,
    });
  }
}

function packageFolder(id: string): string | undefined {
  const path = id.replaceAll("\\", "/");
  const marker = path.lastIndexOf("/node_modules/");
  if (marker < 0) {
    return undefined;
  }

  const rest = path.slice(marker + "/node_modules/".length).split("/");
  const name = rest[0]?.startsWith("@") === true ? `${rest[0]}/${rest[1]}` : rest[0];
  return name === undefined ? undefined : join(path.slice(0, marker), "node_modules", name);
}

/** One package as Vite's license.md writes it: a heading with name, version and licence, then the licence text. */
function section(p: BundledPackage): string {
  return `## ${p.name} - ${p.version} (${p.license})\n\n${readLicense(p.folder)}\n\n`;
}

function readLicense(folder: string): string {
  for (const name of ["LICENSE", "LICENSE.md", "LICENSE.txt"]) {
    try {
      return readFileSync(join(folder, name), "utf8").trim();
    } catch {
      // The next spelling.
    }
  }

  throw new Error(`No licence file in ${folder}`);
}
