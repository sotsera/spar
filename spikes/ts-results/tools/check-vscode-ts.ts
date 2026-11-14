// Type-checks every form with the TypeScript bundled in VS Code (its compiler API), for parity with `tsc` 7.
// Usage: node tools/check-vscode-ts.ts
import { createRequire } from "node:module";
import { existsSync, readdirSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";
import { vscodeTsserver } from "./locate.ts";

interface Diagnostic { file?: { fileName: string; getLineAndCharacterOfPosition(p: number): { line: number; character: number } }; start?: number; code: number; messageText: unknown; }
interface Ts {
  version: string;
  readConfigFile(path: string, read: (p: string) => string | undefined): { config: unknown };
  parseJsonConfigFileContent(config: unknown, host: unknown, base: string): { fileNames: string[]; options: unknown };
  createProgram(files: string[], options: unknown): unknown;
  getPreEmitDiagnostics(program: unknown): readonly Diagnostic[];
  flattenDiagnosticMessageText(m: unknown, nl: string): string;
  sys: { readFile(p: string): string | undefined };
}

const spikeRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const server = vscodeTsserver();
const ts = createRequire(import.meta.url)(join(server.typescriptDir, "lib", "typescript.js")) as Ts;
console.log(`# TypeScript ${ts.version} from VS Code ${server.vscodeVersion}`);

for (const form of readdirSync(join(spikeRoot, "forms"), { withFileTypes: true }).filter(d => d.isDirectory()).map(d => d.name)) {
  for (const config of ["tsconfig.app.json", "tsconfig.bad.json"]) {
    const path = join(spikeRoot, "forms", form, config);
    if (!existsSync(path)) continue;

    const parsed = ts.parseJsonConfigFileContent(ts.readConfigFile(path, ts.sys.readFile).config, ts.sys, dirname(path));
    const diagnostics = ts.getPreEmitDiagnostics(ts.createProgram(parsed.fileNames, parsed.options));
    console.log(`\n## ${form}/${config}: ${diagnostics.length} diagnostics`);
    for (const d of diagnostics) {
      const where = d.file && d.start !== undefined ? (() => { const p = d.file.getLineAndCharacterOfPosition(d.start); return `${relative(join(spikeRoot, "forms", form), d.file.fileName)}(${p.line + 1},${p.character + 1})`; })() : "";
      console.log(`${where}: error TS${d.code}: ${ts.flattenDiagnosticMessageText(d.messageText, "\n  ")}`);
    }
  }
}
