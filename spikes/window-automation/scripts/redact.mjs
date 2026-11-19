// Copies the spike's logs from artifacts/window-automation/ to results/, with the repository path, the user profile
// path (long and 8.3 forms) and the user name redacted. Run from the repository root: node spikes/window-automation/scripts/redact.mjs
import fs from "node:fs";
import os from "node:os";
import path from "node:path";

const repo = process.cwd();
const from = path.join(repo, "artifacts", "window-automation");
const to = path.join(repo, "spikes", "window-automation", "results");
const escape = (s) => s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
const forms = (p) => [p, p.replaceAll("\\", "/"), p.replaceAll("\\", "\\\\")];
const rules = [
  ...forms(repo).map((p) => [new RegExp(escape(p), "gi"), "<repo>"]),
  ...forms(os.homedir()).map((p) => [new RegExp(escape(p), "gi"), "%USERPROFILE%"]),
  [/C:[\\/]+Users[\\/]+[^\\/\s"']+/gi, "%USERPROFILE%"],
  [new RegExp(escape(os.userInfo().username), "gi"), "<user>"],
];

fs.mkdirSync(to, { recursive: true });
for (const name of fs.readdirSync(from)) {
  const source = path.join(from, name);
  if (/\.(log|yml|txt)$/.test(name)) {
    let text = fs.readFileSync(source, "utf8");
    for (const [pattern, replacement] of rules) text = text.replace(pattern, replacement);
    fs.writeFileSync(path.join(to, name), text);
  } else if (/\.png$/.test(name)) {
    fs.copyFileSync(source, path.join(to, name));
  }
}
