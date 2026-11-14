// Lists every package under a node_modules folder (installed tree) and every package in a lockfile that declares
// install-time lifecycle scripts, or that npm would build implicitly (binding.gyp without an install script).
// Usage: node scan-scripts.mjs <frontend-folder>
import { existsSync, readFileSync, readdirSync } from "node:fs";
import { join, resolve } from "node:path";

const root = resolve(process.argv[2] ?? ".");
const lifecycle = ["preinstall", "install", "postinstall", "prepare"];

function* packages(dir) {
  if (!existsSync(dir)) return;
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.name.startsWith(".")) continue;
    const full = join(dir, entry.name);
    if (entry.name.startsWith("@")) {
      for (const scoped of readdirSync(full, { withFileTypes: true })) yield join(full, scoped.name);
    } else {
      yield full;
    }
  }
}

function scan(dir, depth = 0) {
  const found = [];
  for (const pkgDir of packages(dir)) {
    const manifest = join(pkgDir, "package.json");
    if (!existsSync(manifest)) continue;
    const pkg = JSON.parse(readFileSync(manifest, "utf8"));
    const scripts = Object.fromEntries(lifecycle.filter((s) => pkg.scripts?.[s]).map((s) => [s, pkg.scripts[s]]));
    const gyp = existsSync(join(pkgDir, "binding.gyp"));
    const nativeFiles = readdirSync(pkgDir).filter((f) => f.endsWith(".node") || f.endsWith(".exe"));
    found.push({ name: pkg.name, version: pkg.version, scripts, gyp, nativeFiles, bin: pkg.bin ?? null });
    if (depth < 4) found.push(...scan(join(pkgDir, "node_modules"), depth + 1));
  }
  return found;
}

const installed = scan(join(root, "node_modules"));
const withScripts = installed.filter((p) => Object.keys(p.scripts).length > 0 || p.gyp);
const native = installed.filter((p) => p.nativeFiles.length > 0);

const lock = JSON.parse(readFileSync(join(root, "package-lock.json"), "utf8"));
const lockScripts = Object.entries(lock.packages)
  .filter(([, v]) => v.hasInstallScript)
  .map(([k, v]) => ({ path: k, version: v.version, os: v.os ?? null, optional: v.optional ?? false }));

console.log(JSON.stringify({
  installedCount: installed.length,
  installedWithInstallScripts: withScripts,
  installedNativeBinaries: native.map((p) => ({ name: p.name, version: p.version, files: p.nativeFiles })),
  lockfileEntries: Object.keys(lock.packages).length - 1,
  lockfileHasInstallScript: lockScripts,
}, null, 2));
