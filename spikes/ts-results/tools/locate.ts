// Finds the two language servers: TypeScript 7's native one in this spike's node_modules, and the TypeScript bundled with VS Code.
import { execFileSync } from "node:child_process";
import { existsSync, readdirSync, readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { dirname, join } from "node:path";

const require = createRequire(import.meta.url);

export interface Server {
  readonly name: string;
  readonly version: string;
  readonly command: string;
  readonly args: readonly string[];
  readonly env?: Readonly<Record<string, string>>;
}

/** `tsc --lsp --stdio` from the platform package that the `typescript` 7 package installs. */
export function tsgo(): Server {
  const platformPackage = `@typescript/typescript-${process.platform}-${process.arch}/package.json`;
  const exe = join(dirname(require.resolve(platformPackage)), "lib", process.platform === "win32" ? "tsc.exe" : "tsc");
  const version = execFileSync(exe, ["--version"], { encoding: "utf8" }).trim().replace(/^Version /, "");
  return { name: "tsgo-lsp", version, command: exe, args: ["--lsp", "--stdio"] };
}

/** The VS Code install: `VSCODE_HOME`, else the folder of `code` on PATH, else the default per-user install. */
function vscodeHome(): string {
  const fromEnv = process.env["VSCODE_HOME"];
  if (fromEnv) return fromEnv;

  try {
    const lookup = process.platform === "win32" ? "where.exe" : "which";
    const codeCmd = execFileSync(lookup, ["code"], { encoding: "utf8" }).split(/\r?\n/).find(line => line.trim()) ?? "";
    if (codeCmd) return dirname(dirname(codeCmd.trim()));
  } catch {
    // Not on PATH: fall through.
  }

  return join(process.env["LOCALAPPDATA"] ?? "", "Programs", "Microsoft VS Code");
}

/** VS Code's bundled tsserver, run by VS Code's own Electron in Node mode, the way VS Code runs it by default. */
export function vscodeTsserver(): Server & { readonly vscodeVersion: string; readonly typescriptDir: string } {
  const home = vscodeHome();
  const candidates = [home, ...readdirSync(home, { withFileTypes: true }).filter(d => d.isDirectory()).map(d => join(home, d.name))];
  const appDir = candidates.map(c => join(c, "resources", "app")).find(a => existsSync(join(a, "extensions", "node_modules", "typescript", "lib", "tsserver.js")));
  if (!appDir) throw new Error(`No bundled TypeScript under ${home}`);

  const typescriptDir = join(appDir, "extensions", "node_modules", "typescript");
  const version = (JSON.parse(readFileSync(join(typescriptDir, "package.json"), "utf8")) as { version: string }).version;
  const vscodeVersion = (JSON.parse(readFileSync(join(appDir, "package.json"), "utf8")) as { version: string }).version;
  const electron = join(home, process.platform === "win32" ? "Code.exe" : "code");
  const tsserver = join(typescriptDir, "lib", "tsserver.js");
  const args = [tsserver, "--useInferredProjectPerProjectRoot", "--disableAutomaticTypingAcquisition", "--locale", "en", "--noGetErrOnBackgroundUpdate"];
  return existsSync(electron)
    ? { name: "vscode-tsserver", version, vscodeVersion, typescriptDir, command: electron, args, env: { ELECTRON_RUN_AS_NODE: "1" } }
    : { name: "vscode-tsserver", version, vscodeVersion, typescriptDir, command: process.execPath, args };
}
