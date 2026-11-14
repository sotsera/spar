// Usage: node tools/show.mjs <log> — prints the page results, violations and the host-side events of one run.
import { readFileSync } from "node:fs";

const lines = readFileSync(process.argv[2], "utf8").split(/\r?\n/);
for (const line of lines) {
  const page = line.search(/] PAGE {/) < 0 ? -1 : line.indexOf("PAGE {");
  if (page >= 0) {
    const message = JSON.parse(line.slice(page + 5));
    if (message.kind === "result") {
      for (const [k, v] of Object.entries(message.results)) console.log(`  ${k.padEnd(26)} ${v}`);
      for (const v of message.late ?? []) console.log(`  late-violation ${v.directive} ${v.blocked} ${v.sample ?? ""}`);
      continue;
    }
    if (message.kind === "violation") {
      console.log(`  page-violation ${message.via} ${message.directive} ${message.blocked}`);
      continue;
    }
  }
  const native = line.indexOf("NATIVE ");
  if (native >= 0 && line.includes('"kind":"violation"')) {
    const v = JSON.parse(line.slice(line.indexOf("{", native)));
    console.log(`  injected-violation ${v.directive} ${v.blocked} line=${v.line} ${v.sample ?? ""}`);
    continue;
  }
  if (/ (EXT_HIT|EXT_BODY|NAV_BLOCKED|NAV_ALLOWED|NEW_WINDOW|PERMISSION|REPORT|FORM_SELF|OPEN_EXTERNAL|TAMPERED|TIMEOUT|HOST_|CLICKED|REDIRECT_SERVED|POLICY|DECIDE|CREATE|LOAD_)/.test(line)) {
    console.log(line.replace(/ ua=\S+/, "").slice(0, 220));
  } else if (line.includes("PAGE {")) {
    console.log(line.slice(0, 220));
  }
}
