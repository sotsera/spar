// Drives both language servers over the three variants (ts, js, js-csmap) of the linked @spar/app package.
// Usage: node tools/probe.ts [tsgo|vscode|all] [variant...]. Run tools/build.ts first. Writes obj/editor/<server>.md.
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { startLsp, startTsserver, type Edit, type EditorClient, type Location, type Position } from "./clients.ts";
import { tsgo, vscodeTsserver } from "./locate.ts";

const spikeRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const [serverArg, ...variantArgs] = process.argv.slice(2);
const variants = variantArgs.length ? variantArgs : ["ts", "js", "js-csmap"];
const servers = serverArg && serverArg !== "all" ? [serverArg] : ["tsgo", "vscode"];

interface Marker { readonly name: string; readonly kind: "|" | "^"; readonly at: Position; }

function markers(text: string): Marker[] {
  const found: Marker[] = [];
  text.split("\n").forEach((line, index) => {
    for (const m of line.matchAll(/\/\*([|^])([a-z-]+)\*\//g)) {
      found.push({ name: m[2]!, kind: m[1] as "|" | "^", at: { line: index + 1, col: m.index + 1 - (m[1] === "^" ? 1 : 0) } });
    }
  });

  return found;
}

function applyEdits(text: string, edits: readonly Edit[]): string {
  const lines = text.split("\n");
  const offset = (p: Position) => lines.slice(0, p.line - 1).reduce((n, l) => n + l.length + 1, 0) + p.col - 1;
  let result = text;
  for (const e of [...edits].sort((a, b) => offset(b.start) - offset(a.start))) result = result.slice(0, offset(e.start)) + e.newText + result.slice(offset(e.end));
  return result;
}

const show = (p: Position) => `${p.line}:${p.col}`;
const same = (a: string, b: string) => resolve(a).toLowerCase() === resolve(b).toLowerCase();

async function probeVariant(client: EditorClient, variant: string, out: string[]): Promise<void> {
  const root = join(spikeRoot, "obj", "variants", variant);
  const frontend = join(root, "frontend");
  const rel = (file: string) => {
    const r = relative(root, file).replaceAll("\\", "/");
    return r.startsWith("..") ? `<spike>/${relative(spikeRoot, file).replaceAll("\\", "/")}` : r;
  };
  const lineAt = (l: Location) => existsSync(l.file) ? readFileSync(l.file, "utf8").split("\n")[l.start.line - 1]?.trim().slice(0, 90) ?? "" : "(missing file)";
  const where = (list: readonly Location[]) => list.length ? list.map(l => `${rel(l.file)}:${show(l.start)}`).join(", ") : "(none)";
  const byFile = (list: readonly Location[]) => {
    const counts = new Map<string, number>();
    for (const l of list) counts.set(rel(l.file), (counts.get(rel(l.file)) ?? 0) + 1);
    const outside = list.filter(l => !rel(l.file).startsWith("frontend/")).map(l => {
      const line = existsSync(l.file) ? readFileSync(l.file, "utf8").split("\n")[l.start.line - 1] ?? "" : "";
      const text = l.start.line === l.end.line ? line.slice(l.start.col - 1, l.end.col - 1) : "(multi-line)";
      return `${rel(l.file)}:${show(l.start)}-${show(l.end)} \`${text}\``;
    });
    return ([...counts].map(([f, n]) => `${f} ×${n}`).join(", ") || "(none)") + (outside.length ? ` [outside the app: ${outside.join("; ")}]` : "");
  };

  const probe = join(frontend, "src", "probe.ts");
  const bad = join(frontend, "src", "bad.ts");
  const generated = join(root, "obj", "spar", "client", variant === "ts" ? "copyJobs.ts" : "copyJobs.d.ts");
  for (const f of [probe, bad]) await client.open(f);

  out.push(`\n## ${variant}\n`, "### bad.ts: diagnostics and quick fixes\n");
  const badText = readFileSync(bad, "utf8");
  const diagnostics = (await client.diagnostics(bad)).sort((a, b) => a.start.line - b.start.line || a.start.col - b.start.col);
  for (const d of diagnostics) {
    out.push(`- ${show(d.start)} TS${d.code}: ${d.message.split("\n")[0]}`);
    const fixes = await client.fixes(bad, d).catch((e: unknown) => [{ title: `(error: ${(e as Error).message})`, edits: [] }]);
    for (const fix of fixes) {
      out.push(`  - fix: ${fix.title}`);
      const before = badText.split("\n");
      const after = applyEdits(badText, fix.edits.filter(e => same(e.file, bad))).split("\n");
      let top = 0;
      while (top < before.length && before[top] === after[top]) top++;
      let tail = 0;
      while (tail < before.length - top && before[before.length - 1 - tail] === after[after.length - 1 - tail]) tail++;
      const changed = after.slice(top, after.length - tail);
      if (changed.length) out.push("    ```ts", ...changed.map(l => `    ${l}`), "    ```");
      for (const e of fix.edits.filter(e => !same(e.file, bad))) out.push(`    **edits ${rel(e.file)}:${show(e.start)}**: \`${e.newText.trim().replaceAll("\n", "⏎").slice(0, 120)}\``);
    }
  }

  out.push("\n### probe.ts\n");
  for (const m of markers(readFileSync(probe, "utf8"))) {
    if (m.kind === "|") {
      const items = await client.completions(probe, m.at);
      const list = items.map(i => `${i.label}${i.insertText ? ` => \`${i.insertText.replaceAll("\n", "⏎")}\`` : ""} (${i.kind})`);
      out.push(`- completions @${m.name} [${items.length}]:`, ...list.slice(0, 12).map(l => `  - ${l}`), ...(list.length > 12 ? [`  - … ${list.length - 12} more`] : []));
      if (items.some(i => i.label === "tableMissing")) {
        const details = await client.details(probe, m.at, "tableMissing");
        out.push("  - details of tableMissing:", "    ```", ...details.split("\n").map(l => `    ${l}`), "    ```");
      }
      continue;
    }

    out.push(`- **@${m.name}**`);
    const hover = await client.hover(probe, m.at);
    out.push("  - hover:", "    ```", ...hover.split("\n").map(l => `    ${l}`), "    ```");
    const defs = await client.definition(probe, m.at);
    out.push(`  - definition: ${defs.length ? defs.map(d => `${rel(d.file)}:${show(d.start)} \`${lineAt(d)}\``).join("; ") : "(none)"}`);
    const source = await client.sourceDefinition(probe, m.at);
    if (typeof source === "string") out.push(`  - source definition: ${source}`);
    else out.push(`  - source definition: ${source.length ? source.map(d => `${rel(d.file)}:${show(d.start)} \`${lineAt(d)}\``).join("; ") : "(none)"}`);
    const refs = await client.references(probe, m.at).catch((e: unknown) => (e as Error).message);
    out.push(`  - references: ${typeof refs === "string" ? refs : `${refs.length}: ${byFile(refs)}`}`);
    if (["case-key", "payload-field", "dto-prop-literal", "dto-prop-access"].includes(m.name)) {
      const rename = await client.rename(probe, m.at, "renamed").catch((e: unknown) => ({ canRename: false, message: (e as Error).message, locations: [] }));
      out.push(`  - rename: canRename=${rename.canRename}${rename.message ? ` (${rename.message})` : ""}; ${rename.locations.length} edits: ${byFile(rename.locations)}`);
    }
  }

  // From the generated declaration itself (a user who opened the generated file).
  await client.open(generated);
  const lines = readFileSync(generated, "utf8").split("\n");
  const declLine = lines.findIndex(l => l.includes("readonly tableMissing:"));
  const at = { line: declLine + 1, col: lines[declLine]!.indexOf("tableMissing") + 2 };
  const rename = await client.rename(generated, at, "renamed").catch((e: unknown) => ({ canRename: false, message: (e as Error).message, locations: [] }));
  const refs = await client.references(generated, at).catch(() => []);
  out.push(`- **from the generated declaration** ${rel(generated)}:${show(at)}: rename canRename=${rename.canRename}${rename.message ? ` (${rename.message})` : ""}, ${rename.locations.length} edits: ${byFile(rename.locations)}; references ${refs.length}: ${byFile(refs)}`);
  out.push(`  - definition list detail: ${where(await client.definition(generated, at))}`);
}

for (const name of servers) {
  const server = name === "tsgo" ? tsgo() : vscodeTsserver();
  const out: string[] = [];
  for (const variant of variants) {
    const frontend = join(spikeRoot, "obj", "variants", variant, "frontend");
    const client = name === "tsgo" ? await startLsp(server, frontend) : await startTsserver(server, frontend);
    try {
      if (!out.length) out.push(`# ${client.description}`);
      await probeVariant(client, variant, out);
    } finally {
      await client.close().catch((e: unknown) => out.push(`
(close: ${(e as Error).message}; process killed)`));
    }
  }

  const text = out.join("\n") + "\n";
  mkdirSync(join(spikeRoot, "obj", "editor"), { recursive: true });
  const target = join(spikeRoot, "obj", "editor", `${name}${variantArgs.length ? "-" + variantArgs.join("-") : ""}.md`);
  if (!existsSync(target) || readFileSync(target, "utf8") !== text) writeFileSync(target, text);
  console.log(text);
}
