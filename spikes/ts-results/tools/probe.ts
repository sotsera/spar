// Drives both language servers over every form: diagnostics and quick fixes in bad.ts, and the probes marked in probe.ts.
// Usage: node tools/probe.ts [tsgo|vscode] [form...]. Writes obj/editor/<server>.txt next to printing it.
import { mkdirSync, readdirSync, readFileSync, writeFileSync, existsSync } from "node:fs";
import { dirname, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { startLsp, startTsserver, type Edit, type EditorClient, type Position } from "./clients.ts";
import { tsgo, vscodeTsserver } from "./locate.ts";

const spikeRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const formsRoot = join(spikeRoot, "forms");
const [serverArg, ...formArgs] = process.argv.slice(2);
const forms = formArgs.length ? formArgs : readdirSync(formsRoot, { withFileTypes: true }).filter(d => d.isDirectory()).map(d => d.name);
const servers = serverArg && serverArg !== "all" ? [serverArg] : ["tsgo", "vscode"];

interface Marker { readonly name: string; readonly kind: "|" | "^"; readonly at: Position; }

function markers(text: string): Marker[] {
  const found: Marker[] = [];
  const lines = text.split("\n");
  lines.forEach((line, index) => {
    for (const m of line.matchAll(/\/\*([|^])([a-z-]+)\*\//g)) {
      const col = m.index + 1 - (m[1] === "^" ? 1 : 0);
      found.push({ name: m[2]!, kind: m[1] as "|" | "^", at: { line: index + 1, col } });
    }
  });

  return found;
}

function applyEdits(text: string, edits: readonly Edit[]): string {
  const lines = text.split("\n");
  const offset = (p: Position) => lines.slice(0, p.line - 1).reduce((n, l) => n + l.length + 1, 0) + p.col - 1;
  const sorted = [...edits].sort((a, b) => offset(b.start) - offset(a.start));
  let result = text;
  for (const e of sorted) result = result.slice(0, offset(e.start)) + e.newText + result.slice(offset(e.end));
  return result;
}

const show = (p: Position) => `${p.line}:${p.col}`;
const rel = (file: string) => relative(spikeRoot, file).replaceAll("\\", "/");

async function probeForm(client: EditorClient, form: string, out: string[]): Promise<void> {
  const dir = join(formsRoot, form);
  const probe = join(dir, "probe.ts");
  const bad = join(dir, "bad.ts");
  for (const f of ["sdk.ts", "copyJobs.ts", "bad.ts", "probe.ts"]) await client.open(join(dir, f));

  out.push(`\n## ${form}\n`, "### bad.ts diagnostics and quick fixes\n");
  const badText = readFileSync(bad, "utf8");
  const diagnostics = (await client.diagnostics(bad)).sort((a, b) => a.start.line - b.start.line || a.start.col - b.start.col);
  for (const d of diagnostics) {
    out.push(`- ${show(d.start)} TS${d.code}: ${d.message.replaceAll("\n", "\n    ")}`);
    const fixes = await client.fixes(bad, d).catch((e: unknown) => [{ title: `(error: ${(e as Error).message})`, edits: [] }]);
    for (const fix of fixes) {
      out.push(`  - fix: ${fix.title}`);
      const same = (f: string) => resolve(f).toLowerCase() === resolve(bad).toLowerCase();
      const before = badText.split("\n");
      const after = applyEdits(badText, fix.edits.filter(e => same(e.file))).split("\n");
      let top = 0;
      while (top < before.length && before[top] === after[top]) top++;
      let tail = 0;
      while (tail < before.length - top && before[before.length - 1 - tail] === after[after.length - 1 - tail]) tail++;
      const changed = after.slice(top, after.length - tail);
      if (changed.length) out.push("    ```ts", ...changed.map(l => `    ${l}`), "    ```");
      for (const e of fix.edits.filter(e => !same(e.file))) out.push(`    edits ${rel(e.file)}:${show(e.start)}: \`${e.newText.trim().replaceAll("\n", "⏎").slice(0, 120)}\``);
    }
  }

  // Rename started at the generated declaration of a case.
  const generated = join(dir, "copyJobs.ts");
  const generatedLines = readFileSync(generated, "utf8").split("\n");
  const declLine = generatedLines.findIndex(l => /readonly tableMissing:|"tableMissing"/.test(l));
  if (declLine >= 0) {
    const col = generatedLines[declLine]!.indexOf("tableMissing") + 2;
    const rename = await client.rename(generated, { line: declLine + 1, col }, "renamed").catch((e: unknown) => ({ canRename: false, message: (e as Error).message, locations: [] }));
    out.push("", `- rename from the generated declaration copyJobs.ts:${declLine + 1}:${col}: canRename=${rename.canRename}${rename.message ? ` (${rename.message})` : ""}; ${rename.locations.length} locations: ${rename.locations.map(l => `${rel(l.file)}:${show(l.start)}`).join(", ")}`);
  }

  out.push("\n### probe.ts\n");
  for (const m of markers(readFileSync(probe, "utf8"))) {
    if (m.kind === "|") {
      const items = await client.completions(probe, m.at);
      const list = items.map(i => `${i.label}${i.insertText ? ` => \`${i.insertText.replaceAll("\n", "⏎")}\`` : ""} (${i.kind}${i.detail ? `, ${i.detail}` : ""})`);
      out.push(`- completions @${m.name} [${items.length}]:`, ...list.slice(0, 25).map(l => `  - ${l}`), ...(list.length > 25 ? [`  - … ${list.length - 25} more`] : []));
      const detailFor = m.name.startsWith("errors") ? "tableMissing" : m.name.startsWith("keys") ? "success" : undefined;
      if (detailFor && items.some(i => i.label === detailFor)) {
        const details = await client.details(probe, m.at, detailFor);
        out.push(`  - details of ${detailFor}:`, "    ```", ...details.split("\n").map(l => `    ${l}`), "    ```");
      }
      continue;
    }

    const hover = await client.hover(probe, m.at);
    out.push(`- hover @${m.name}:`, "  ```", ...hover.split("\n").map(l => `  ${l}`), "  ```");
    const defs = await client.definition(probe, m.at);
    out.push(`- definition @${m.name}: ${defs.length ? defs.map(d => `${rel(d.file)}:${show(d.start)} \`${readFileSync(d.file, "utf8").split("\n")[d.start.line - 1]?.trim().slice(0, 100)}\``).join("; ") : "(none)"}`);
    if (m.name === "hover-case-key" || m.name === "definition-payload-field") {
      const rename = await client.rename(probe, m.at, "renamed").catch((e: unknown) => ({ canRename: false, message: (e as Error).message, locations: [] }));
      out.push(`- rename @${m.name}: canRename=${rename.canRename}${rename.message ? ` (${rename.message})` : ""}; ${rename.locations.length} locations: ${rename.locations.map(l => `${rel(l.file)}:${show(l.start)}`).join(", ")}`);
    }
  }
}

for (const name of servers) {
  const server = name === "tsgo" ? tsgo() : vscodeTsserver();
  const out: string[] = [];
  for (const form of forms) {
    const dir = join(formsRoot, form);
    if (!existsSync(join(dir, "probe.ts"))) continue;

    const client = name === "tsgo" ? await startLsp(server, dir) : await startTsserver(server, dir);
    try {
      if (!out.length) out.push(`# ${client.description}`);
      await probeForm(client, form, out);
    } finally {
      await client.close();
    }
  }

  const text = out.join("\n") + "\n";
  mkdirSync(join(spikeRoot, "obj", "editor"), { recursive: true });
  const target = join(spikeRoot, "obj", "editor", `${name}${formArgs.length ? "-" + formArgs.join("-") : ""}.md`);
  if (!existsSync(target) || readFileSync(target, "utf8") !== text) writeFileSync(target, text);
  console.log(text);
}
