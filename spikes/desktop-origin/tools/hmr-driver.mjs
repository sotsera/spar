// Edits the Vite app's sources while the desktop host shows it, and measures how long each hot update takes to
// reach the page (question 6). The page reports to /_spar/hmr, which the host logs; this script reads that log.
//
//   node tools/hmr-driver.mjs <host log> <stop file> [rounds]
//
// Edits, in order: <rounds> Header.tsx text changes (Fast Refresh), <rounds> spike.css colour changes, then a syntax
// error (error overlay expected) and its fix. Sources are restored at the end, and the stop file ends the host.
import fs from "node:fs";

const [hostLog, stopFile, roundsText] = process.argv.slice(2);
const rounds = Number(roundsText ?? 5);
const app = new URL("../vite-app/src/", import.meta.url);
const header = new URL("Header.tsx", app);
const css = new URL("spike.css", app);
const originalHeader = fs.readFileSync(header, "utf8");
const originalCss = fs.readFileSync(css, "utf8");
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

function events() {
  const text = fs.existsSync(hostLog) ? fs.readFileSync(hostLog, "utf8") : "";
  return text.split(/\r?\n/).filter((line) => line.includes("/_spar/hmr?")).map((line) => {
    const query = /\/_spar\/hmr\?(\S+)/.exec(line)[1];
    return Object.fromEntries(new URLSearchParams(query));
  });
}

async function waitFor(predicate, ms) {
  const deadline = Date.now() + ms;
  while (Date.now() < deadline) {
    const found = events().find(predicate);
    if (found) return found;
    await sleep(20);
  }
  return undefined;
}

const results = { fastRefresh: [], css: [], overlay: {} };
const readyDeadline = Date.now() + 60000;
while (Date.now() < readyDeadline && !(fs.existsSync(hostLog) && fs.readFileSync(hostLog, "utf8").includes("TIMING"))) await sleep(100);
const first = await waitFor((e) => e.event === "dom" && e.title === "Notes 0", 10000);
results.wsConnect = (await waitFor((e) => e.event === "ws-connect", 3000)) ? "connected" : "no ws-connect event";
await sleep(1000);

try {
  for (let i = 1; i <= rounds; i++) {
    const start = Date.now();
    fs.writeFileSync(header, originalHeader.replace("Notes 0", `Notes ${i}`));
    const dom = await waitFor((e) => e.event === "dom" && e.title === `Notes ${i}`, 5000);
    const before = events().find((e) => e.event === "before" && Number(e.epoch) >= start);
    results.fastRefresh.push(dom
      ? { round: i, editToDom: Number(dom.epoch) - start, editToBefore: before ? Number(before.epoch) - start : null, statePreserved: dom.mounted === first?.mounted }
      : { round: i, editToDom: null, note: "no update within 5 s" });
    await sleep(600);
  }

  for (let i = 1; i <= rounds; i++) {
    const start = Date.now();
    fs.writeFileSync(css, `h1 { color: rgb(10, 20, ${100 + i}); }\n`);
    const applied = await waitFor((e) => e.event === "css" && e.color === `rgb(10, 20, ${100 + i})`, 5000);
    results.css.push({ round: i, editToApplied: applied ? Number(applied.epoch) - start : null });
    await sleep(600);
  }

  let start = Date.now();
  fs.writeFileSync(header, originalHeader.replace("<h1>Notes 0</h1>", "<h1>Notes broken</h2>"));
  const shown = await waitFor((e) => e.event === "overlay" && e.present === "true" && Number(e.epoch) >= start, 5000);
  results.overlay.shown = shown ? Number(shown.epoch) - start : null;
  await sleep(800);
  start = Date.now();
  fs.writeFileSync(header, originalHeader.replace("Notes 0", "Notes fixed"));
  const fixed = await waitFor((e) => e.event === "dom" && e.title === "Notes fixed", 5000);
  const hidden = await waitFor((e) => e.event === "overlay" && e.present === "false" && Number(e.epoch) >= start, 3000);
  results.overlay.fixedDom = fixed ? Number(fixed.epoch) - start : null;
  results.overlay.hidden = hidden ? Number(hidden.epoch) - start : null;
} finally {
  fs.writeFileSync(header, originalHeader);
  fs.writeFileSync(css, originalCss);
  await sleep(1000);
  fs.writeFileSync(stopFile, "stop");
}

console.log(JSON.stringify(results));
