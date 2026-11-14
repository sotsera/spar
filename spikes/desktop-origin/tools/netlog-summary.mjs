// Prints the net log events for URL requests whose URL contains <needle>: type names, phases and short params.
//   node tools/netlog-summary.mjs <netlog.json> <needle>
import fs from "node:fs";
const [file, needle] = process.argv.slice(2);
let text = fs.readFileSync(file, "utf8").trim();
if (!text.endsWith("}")) text = text.replace(/,\s*$/, "") + "]}"; // a log cut at browser exit
const log = JSON.parse(text);
const types = Object.fromEntries(Object.entries(log.constants.logEventTypes).map(([k, v]) => [v, k]));
const sources = new Set();
for (const e of log.events) if (JSON.stringify(e.params ?? {}).includes(needle)) sources.add(e.source.id);
for (const e of log.events) {
  if (!sources.has(e.source.id)) continue;
  const name = types[e.type];
  if (/^(HTTP_STREAM_JOB_CONTROLLER|SOCKET_POOL|NETWORK_DELEGATE|URL_REQUEST_DELEGATE|DELEGATE_INFO|COOKIE)/.test(name)) continue;
  console.log(e.source.id, e.time, name, e.phase, JSON.stringify(e.params ?? "").slice(0, 300));
}
