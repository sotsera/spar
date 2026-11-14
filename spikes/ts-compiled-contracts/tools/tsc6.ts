// `tsc -p <config> --noEmit` with VS Code's bundled TypeScript (6.0.3). It ships typescript.js but no tsc.js,
// so this runs its compiler API and prints diagnostics in tsc's non-pretty format.
// Usage: node tools/tsc6.ts -p <tsconfig>
import { createRequire } from "node:module";
import { dirname, join, relative } from "node:path";
import { vscodeTsserver } from "./locate.ts";

interface Diagnostic { file?: { fileName: string; getLineAndCharacterOfPosition(p: number): { line: number; character: number } }; start?: number; code: number; messageText: unknown; }
interface Ts {
  version: string;
  readConfigFile(path: string, read: (p: string) => string | undefined): { config: unknown };
  parseJsonConfigFileContent(config: unknown, host: unknown, base: string): { fileNames: string[]; options: unknown; errors: readonly Diagnostic[] };
  createProgram(files: string[], options: unknown): unknown;
  getPreEmitDiagnostics(program: unknown): readonly Diagnostic[];
  flattenDiagnosticMessageText(m: unknown, nl: string): string;
  sys: { readFile(p: string): string | undefined };
}

const ts = createRequire(import.meta.url)(join(vscodeTsserver().typescriptDir, "lib", "typescript.js")) as Ts;
const config = process.argv[process.argv.indexOf("-p") + 1]!;
const parsed = ts.parseJsonConfigFileContent(ts.readConfigFile(config, ts.sys.readFile).config, ts.sys, dirname(config));
const diagnostics = [...parsed.errors, ...ts.getPreEmitDiagnostics(ts.createProgram(parsed.fileNames, parsed.options))];
for (const d of diagnostics) {
  const where = d.file && d.start !== undefined ? (() => { const p = d.file.getLineAndCharacterOfPosition(d.start); return `${relative(process.cwd(), d.file.fileName).replaceAll("\\", "/")}(${p.line + 1},${p.character + 1}): `; })() : "";
  console.log(`${where}error TS${d.code}: ${ts.flattenDiagnosticMessageText(d.messageText, " ")}`);
}

process.exitCode = diagnostics.length ? 2 : 0;
