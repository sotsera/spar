// Compares the engine's role/name tree with another tree of the same page state: WebKitGTK's AT-SPI tree
// (tree-atspi-<state>.json, from atspi.py) or Chromium's (tree-chromium-<state>.json, from the Windows copy).
// Usage: node compare-trees.mjs <folder> <atspi|chromium> <state>...
// Nodes are paired in document order by tag and an equivalent role, then their names are compared.
import { readFileSync } from "node:fs";
import { join } from "node:path";

const [folder, kind, ...states] = process.argv.slice(2);

// ARIA role (engine) -> the platform roles it may appear as.
const atspi = {
  document: ["document web"], link: ["link"], heading: ["heading"], button: ["push button", "combo box", "toggle button"],
  checkbox: ["check box"], textbox: ["entry"], searchbox: ["entry"], combobox: ["combo box"], option: ["menu item", "list item"],
  list: ["list", "panel"], listitem: ["list item"], navigation: ["landmark"], main: ["landmark"], region: ["landmark"],
  form: ["form"], status: ["status bar"], dialog: ["dialog"], menu: ["menu"], menuitem: ["menu item"], separator: ["separator"],
  paragraph: ["paragraph"],
};
const same = (role, other) => kind === "atspi" ? (atspi[role] ?? [role]).includes(other) : role === other || (role === "document" && other === "RootWebArea");

let total = { engine: 0, paired: 0, sameName: 0 };
for (const state of states) {
  const engine = JSON.parse(readFileSync(join(folder, `tree-engine-${state}.json`), "utf8"));
  const other = JSON.parse(readFileSync(join(folder, `tree-${kind}-${state}.json`), "utf8")).filter(n => n.role);
  const used = new Set();
  const rows = [];
  for (const e of engine) {
    const candidate = (o, i) => !used.has(i) && (o.tag ? o.tag === e.tag : true) && same(e.role, o.role) && (kind !== "chromium" || o.index === e.index);
    let index = other.findIndex((o, i) => candidate(o, i) && (o.name ?? "") === e.name);
    if (index < 0) index = other.findIndex(candidate);
    if (index < 0) { rows.push(`  only engine: ${e.role} "${e.name}" <${e.tag}>`); continue; }
    used.add(index);
    total.paired++;
    const o = other[index];
    if ((o.name ?? "") === e.name) total.sameName++;
    else rows.push(`  name differs: ${e.role} <${e.tag}> engine "${e.name}" | ${kind} ${o.role} "${o.name}"`);
  }
  total.engine += engine.length;
  other.forEach((o, i) => { if (!used.has(i) && !["section", "label", "generic", "StaticText", "none", "InlineTextBox", "LineBreak", "LabelText"].includes(o.role)) rows.push(`  only ${kind}: ${o.role} "${o.name}" <${o.tag ?? ""}>`); });
  console.log(`STATE ${state}: engine nodes=${engine.length} ${kind} nodes=${other.length} paired=${used.size}`);
  for (const row of rows) console.log(row);
}
console.log(`TOTAL engine nodes=${total.engine} paired=${total.paired} same name=${total.sameName}`);
