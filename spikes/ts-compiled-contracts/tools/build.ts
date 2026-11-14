// Lays out the three variants of the generated client the way Spar would, under obj/variants/<variant>/:
//   obj/spar/client/  the linked @spar/app package (compiled SDK + generated contract modules)
//   frontend/         the consumer app, "@spar/app": "file:../obj/spar/client"
// Variants: ts (contract modules as .ts), js (.js + .d.ts), js-csmap (.js + .d.ts + .d.ts.map pointing to contracts/CopyJobs.cs).
// Usage: node tools/build.ts. Writes a file only when its content changes.
import { execFileSync, execSync } from "node:child_process";
import { existsSync, mkdirSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";
import { tsgo } from "./locate.ts";

const spikeRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const obj = join(spikeRoot, "obj");
export const variants = ["ts", "js", "js-csmap"] as const;

function write(path: string, content: string): void {
  mkdirSync(dirname(path), { recursive: true });
  if (existsSync(path) && readFileSync(path, "utf8") === content) return;
  writeFileSync(path, content);
}

function copyTree(from: string, to: string): void {
  for (const entry of readdirSync(from)) {
    const source = join(from, entry);
    if (entry === "node_modules" || entry === "dist") continue;
    if (statSync(source).isDirectory()) copyTree(source, join(to, entry));
    else write(join(to, entry), readFileSync(source, "utf8"));
  }
}

// ------------------------------------------------------------ SDK: compiled once, copied into each variant

const sdkOut = join(obj, "sdk");
copyTree(join(spikeRoot, "sdk"), join(sdkOut, "src"));
execFileSync(tsgo().command, [
  join(sdkOut, "src", "core.ts"), join(sdkOut, "src", "index.ts"),
  "--outDir", sdkOut, "--rootDir", join(sdkOut, "src"),
  "--declaration", "--declarationMap", "--sourceMap",
  "--target", "ES2022", "--module", "ESNext", "--moduleResolution", "bundler", "--lib", "ES2022,DOM",
  "--strict", "--exactOptionalPropertyTypes", "--noUncheckedIndexedAccess", "--verbatimModuleSyntax", "--isolatedModules", "--types", "",
], { stdio: "inherit" });

// ------------------------------------------------------------ declaration map to C#

const base64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
function vlq(value: number): string {
  let v = value < 0 ? (-value << 1) | 1 : value << 1;
  let out = "";
  do {
    let digit = v & 31;
    v >>>= 5;
    if (v > 0) digit |= 32;
    out += base64[digit];
  } while (v > 0);
  return out;
}

interface Spot { readonly line: number; readonly col: number; readonly length: number; }

/** The identifier `name` on the `occurrence`-th line containing `lineText`, searched from where `lineText` starts. */
function spot(text: string, lineText: string, name: string, occurrence = 1): Spot {
  const lines = text.split("\n");
  let seen = 0;
  for (let i = 0; i < lines.length; i++) {
    const at = lines[i]!.indexOf(lineText);
    if (at < 0 || ++seen < occurrence) continue;
    const col = lines[i]!.indexOf(name, at);
    if (col < 0) throw new Error(`${name} not in line ${i + 1}`);
    return { line: i, col, length: name.length };
  }
  throw new Error(`no line with ${lineText} (#${occurrence})`);
}

/** Maps each pair's start and end (d.ts identifier to C# identifier), as tsc does for its own declaration maps. */
function declarationMap(dtsFile: string, dts: string, csFile: string, cs: string, pairs: readonly (readonly [Spot, Spot])[]): string {
  const byLine = new Map<number, [number, number, number][]>();
  for (const [g, s] of pairs) {
    const list = byLine.get(g.line) ?? [];
    list.push([g.col, s.line, s.col], [g.col + g.length, s.line, s.col + s.length]);
    byLine.set(g.line, list);
  }

  const lineCount = dts.split("\n").length;
  let prevSourceLine = 0, prevSourceCol = 0;
  const lines: string[] = [];
  for (let line = 0; line < lineCount; line++) {
    const segments = (byLine.get(line) ?? []).sort((a, b) => a[0] - b[0]);
    let prevCol = 0;
    lines.push(segments.map(([col, sl, sc]) => {
      const segment = vlq(col - prevCol) + vlq(0) + vlq(sl - prevSourceLine) + vlq(sc - prevSourceCol);
      prevCol = col; prevSourceLine = sl; prevSourceCol = sc;
      return segment;
    }).join(","));
  }

  return JSON.stringify({ version: 3, file: dtsFile.split(/[\\/]/).pop(), sourceRoot: "", sources: [relative(dirname(dtsFile), csFile).replaceAll("\\", "/")], names: [], mappings: lines.join(";") });
}

// ------------------------------------------------------------ variants

function packageJson(variant: string): string {
  const contract = (name: string) => variant === "ts" ? `./${name}.ts` : { types: `./${name}.d.ts`, default: `./${name}.js` };
  return JSON.stringify({
    name: "@spar/app",
    private: true,
    type: "module",
    sideEffects: false,
    exports: {
      ".": contract("index"),
      "./sdk": { types: "./sdk/index.d.ts", default: "./sdk/index.js" },
      "./copyJobs": contract("copyJobs"),
      "./auditLog": contract("auditLog"),
    },
  }, null, 2) + "\n";
}

for (const variant of variants) {
  const root = join(obj, "variants", variant);
  const client = join(root, "obj", "spar", "client");
  copyTree(sdkOut, join(client, "sdk"));
  copyTree(join(spikeRoot, "generated", variant === "ts" ? "ts" : "js"), client);
  write(join(client, "package.json"), packageJson(variant));

  if (variant === "js-csmap") {
    const dtsFile = join(client, "copyJobs.d.ts");
    const csFile = join(spikeRoot, "contracts", "CopyJobs.cs");
    const dts = readFileSync(join(spikeRoot, "generated", "js", "copyJobs.d.ts"), "utf8");
    const cs = readFileSync(csFile, "utf8");
    const pairs: (readonly [Spot, Spot])[] = [
      [spot(dts, "interface CopyRecipe", "CopyRecipe"), spot(cs, "record CopyRecipe(", "CopyRecipe")],
      [spot(dts, "readonly source: string;", "source"), spot(cs, "string Source,", "Source")],
      [spot(dts, "readonly target: string;", "target"), spot(cs, "string Target,", "Target")],
      [spot(dts, "readonly tables:", "tables"), spot(cs, "Tables);", "Tables")],
      [spot(dts, "interface PlanError", "PlanError"), spot(cs, "closed record PlanError", "PlanError")],
      [spot(dts, "readonly connectionNotFound:", "connectionNotFound"), spot(cs, "record ConnectionNotFound(", "ConnectionNotFound")],
      [spot(dts, "readonly connectionNotFound:", "connectionId"), spot(cs, "record ConnectionNotFound(", "ConnectionId")],
      [spot(dts, "readonly tableMissing:", "tableMissing"), spot(cs, "record TableMissing(", "TableMissing")],
      [spot(dts, "readonly tableMissing:", "connection:"), spot(cs, "record TableMissing(", "Connection")],
      [spot(dts, "readonly tableMissing:", "table: string }"), spot(cs, "record TableMissing(", "Table)")],
      [spot(dts, "readonly columnsMismatch:", "columnsMismatch"), spot(cs, "record ColumnsMismatch(", "ColumnsMismatch")],
      [spot(dts, "const copyJobs", "copyJobs"), spot(cs, "class CopyJobsService", "CopyJobsService")],
      [spot(dts, "readonly plan:", "plan"), spot(cs, "PlanAsync(", "PlanAsync")],
    ];
    // The identifier spans: `connection:`/`table: string }` were searched with context; trim them to the name.
    const trimmed = pairs.map(([g, s]) => [{ ...g, length: /^\w+/.exec(dts.split("\n")[g.line]!.slice(g.col))![0].length }, { ...s, length: /^\w+/.exec(cs.split("\n")[s.line]!.slice(s.col))![0].length }] as const);
    write(dtsFile, dts.trimEnd() + "\n//# sourceMappingURL=copyJobs.d.ts.map\n");
    write(join(client, "copyJobs.d.ts.map"), declarationMap(dtsFile, dts, csFile, cs, trimmed));
  }

  const frontend = join(root, "frontend");
  copyTree(join(spikeRoot, "frontend"), frontend);
  if (!existsSync(join(frontend, "node_modules", "@spar", "app"))) {
    rmSync(join(frontend, "package-lock.json"), { force: true });
    execSync("npm install --offline --no-audit --no-fund", { cwd: frontend, stdio: "inherit" });
  }

  console.log(`${variant}: ${relative(spikeRoot, client)} -> ${relative(spikeRoot, frontend)}`);
}
