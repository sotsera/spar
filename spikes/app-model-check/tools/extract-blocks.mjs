// Copies every C#, TypeScript and TSX block of docs/app-model.md, verbatim, into the file the document names for it.
// A block's file is the last backticked path with a .cs, .ts or .tsx extension in the paragraph just before the block.
// Run from anywhere: paths are resolved from this script's location. Writes a file only when its content changes.
// `--check` writes nothing and exits 1 when a copy differs from the document.
import { readFileSync, writeFileSync, mkdirSync, existsSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";

const checkRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const documentPath = join(checkRoot, "..", "..", "docs", "app-model.md");
const checkOnly = process.argv.includes("--check");

const lines = readFileSync(documentPath, "utf8").split(/\r?\n/);
const languages = new Set(["csharp", "ts", "tsx"]);
const blocks = [];

for (let i = 0; i < lines.length; i++) {
  const open = /^```(\w+)\s*$/.exec(lines[i] ?? "");
  if (!open) continue;

  const end = lines.indexOf("```", i + 1);
  if (end < 0) throw new Error(`Unclosed block at line ${i + 1}`);

  const language = open[1] ?? "";
  if (languages.has(language)) {
    blocks.push({ language, start: i + 1, body: lines.slice(i + 1, end), file: findFile(i) });
  }
  i = end;
}

function findFile(fenceIndex) {
  let j = fenceIndex - 1;
  while (j >= 0 && (lines[j] ?? "").trim() === "") j--;
  const paragraph = [];
  while (j >= 0 && (lines[j] ?? "").trim() !== "") paragraph.unshift(lines[j--]);
  const paths = [...paragraph.join(" ").matchAll(/`([\w\-./]+\.(?:cs|tsx|ts))`/g)].map(m => m[1]);
  const file = paths.at(-1);
  if (!file) throw new Error(`No file named for the block at line ${fenceIndex + 1}`);
  return file;
}

let differences = 0;
for (const block of blocks) {
  const target = join(checkRoot, block.file);
  const content = block.body.join("\n") + "\n";
  const current = existsSync(target) ? readFileSync(target, "utf8") : undefined;
  const state = current === content ? "unchanged" : current === undefined ? "new" : "changed";
  console.log(`${block.language.padEnd(6)} doc line ${String(block.start).padStart(3)}  ${state.padEnd(9)} ${relative(checkRoot, target)}`);
  if (state === "unchanged") continue;

  differences++;
  if (checkOnly) continue;
  mkdirSync(dirname(target), { recursive: true });
  writeFileSync(target, content);
}

console.log(`${blocks.length} blocks, ${differences} ${checkOnly ? "differ" : "written"}`);
if (checkOnly && differences > 0) process.exit(1);
