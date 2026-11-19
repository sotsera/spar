// Copies the Windows copy's logs and trees from artifacts/window-automation-linux/windows into results/windows, with
// the repository path and the user profile replaced by placeholders. Usage (repository root): node <this file>
import { readdirSync, readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { join } from "node:path";

const source = "artifacts/window-automation-linux/windows";
const target = "spikes/window-automation-linux/results/windows";
mkdirSync(target, { recursive: true });
const repo = process.cwd();
const spellings = [repo, repo.replaceAll("\\", "/"), "/" + repo[0].toLowerCase() + repo.slice(2).replaceAll("\\", "/")];
for (const file of readdirSync(source)) {
  let text = readFileSync(join(source, file), "utf8");
  for (const spelling of spellings) text = text.split(spelling).join("<repo>");
  text = text.replace(/C:\\Users\\[^\\\s]+/gi, "<profile>").replace(/[A-Z]+~1/g, "<user>");
  writeFileSync(join(target, file), text);
}
console.log(`copied ${readdirSync(source).length} files to ${target}`);
