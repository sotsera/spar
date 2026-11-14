// Maps ESLint findings (the JSON report given as the argument) to the "case NN" markers in the lint case files.
import { readFileSync } from "node:fs";
const report = JSON.parse(readFileSync(process.argv[2] ?? "../../artifacts/frontend-frameworks/react-eslint.json", "utf8"));
for (const file of report) {
  const name = file.filePath.split(/[\\/]/).pop();
  const lines = readFileSync(file.filePath, "utf8").split("\n");
  console.log(`== ${name}`);
  const byCase = new Map();
  for (const m of file.messages) {
    const line = lines[m.line - 1] ?? "";
    const tag = /case (\d+)/.exec(line)?.[1] ?? `line ${m.line}`;
    if (!byCase.has(tag)) byCase.set(tag, []);
    byCase.get(tag).push(`${m.ruleId ?? "parse"}${m.message.includes("rejection handler") ? " (no rejection handler)" : ""}`);
  }
  if (name.startsWith("lint-cases")) {
    for (const line of lines) {
      const c = /case (\d+): (.*?)( \*\/\})?$/.exec(line);
      if (c) console.log(`case ${c[1]} ${c[2].padEnd(70)} ${(byCase.get(c[1]) ?? ["-"]).join(", ")}`);
    }
  }
  for (const [tag, rules] of byCase) if (!/^\d+$/.test(tag)) console.log(`${tag}: ${rules.join(", ")}`);
}
