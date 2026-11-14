// Summarizes artifacts/bridge-encoding/{desktop,loopback,webkitgtk}/*.json into results/*.md.
// From the repository root: node spikes/bridge-encoding/summarize.mjs
import { existsSync, readdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";

const spike = import.meta.dirname;
const artifacts = join(spike, "..", "..", "artifacts", "bridge-encoding");
const results = join(spike, "results");

function load(host) {
  const dir = join(artifacts, host);
  if (!existsSync(dir)) return [];
  return readdirSync(dir).filter(f => f.endsWith(".json") && !/-0\.json$/.test(f)).map(f => {
    const [mode, enc, run] = f.replace(/\.json$/, "").split("-");
    const parsed = JSON.parse(readFileSync(join(dir, f), "utf8"));
    return { file: f, mode, enc, run, data: parsed.page ? parsed : { page: parsed } };
  });
}

const median = values => {
  const s = values.filter(v => typeof v === "number" && Number.isFinite(v)).sort((a, b) => a - b);
  if (s.length === 0) return null;
  return s.length % 2 ? s[s.length >> 1] : (s[s.length / 2 - 1] + s[s.length / 2]) / 2;
};
const range = values => {
  const s = values.filter(v => typeof v === "number" && Number.isFinite(v));
  return s.length ? [Math.min(...s), Math.max(...s)] : [null, null];
};
const fmt = (v, d = 1) => v === null || v === undefined ? "—" : Number(v).toFixed(d);
const cell = (values, d = 1) => {
  const [lo, hi] = range(values);
  return lo === null ? "—" : `${fmt(median(values), d)} (${fmt(lo, d)}–${fmt(hi, d)})`;
};

// ---------------------------------------------------------------- end to end
function e2e(host, title, encodings) {
  const runs = load(host).filter(r => r.mode === "full");
  if (runs.length === 0) return "";
  const lines = [`# End to end: ${title}`, "",
    "Median over runs, range in brackets. Unary: 2,000 sequential calls each; `meanUs` = loop time / calls (finer than " +
    "`performance.now()`, which is coarsened to 0.1 ms). Streams: 100,000 rows of about 228 bytes, credits window 8, " +
    "3 streams per run. Push: 20,000 events/s for 3 s from a 1,000-item drop-oldest buffer. Cancel: 200 calls that " +
    "run until cancelled; time from sending the cancel to the confirmation. `task ms` is CDP `TaskDuration` (page " +
    "main thread busy) over the phase; `handler ms` is the time spent inside the page's message handler (decode, " +
    "dispatch and the consumer's own loop).", ""];
  const by = enc => runs.filter(r => r.enc === enc).map(r => r.data);
  const userAgent = runs[0].data.page.userAgent;
  lines.push(`Runs per encoding: ${encodings.map(e => `${e} ${by(e).length}`).join(", ")}. Browser: \`${userAgent}\`.`, "");
  const rows = [
    ["unary echo, mean µs", d => d.page.unaryEcho.meanUs],
    ["unary echo, p50 ms", d => d.page.unaryEcho.p50, 2],
    ["unary echo, p99 ms", d => d.page.unaryEcho.p99, 2],
    ["unary echo, task ms / 2,000 calls", d => d.page.unaryEcho.taskMs],
    ["unary save + traceparent + tracestate, mean µs", d => d.page.unarySaveTraced.meanUs],
    ["unary save traced, p99 ms", d => d.page.unarySaveTraced.p99, 2],
    ["100k rows batch 1000, total ms", d => d.page.rows1000.map(x => x.totalMs)],
    ["100k rows batch 1000, rows/s (k)", d => d.page.rows1000.map(x => x.rowsPerSec / 1000), 0],
    ["100k rows batch 1000, task ms", d => d.page.rows1000.map(x => x.taskMs)],
    ["100k rows batch 1000, handler ms", d => d.page.rows1000.map(x => x.handlerMs)],
    ["100k rows batch 100, total ms", d => d.page.rows100.map(x => x.totalMs)],
    ["100k rows batch 100, task ms", d => d.page.rows100.map(x => x.taskMs)],
    ["push 20k/s (≤1000 per frame), delivered", d => d.page.push.delivered, 0],
    ["push 20k/s, frames", d => d.page.push.frames, 0],
    ["push 20k/s, latency p50 ms", d => d.page.push.latencyMs.p50, 2],
    ["push 20k/s, latency p99 ms", d => d.page.push.latencyMs.p99, 2],
    ["push 20k/s, task ms / 3 s", d => d.page.push.taskMs],
    ["push 20k/s (≤20 per frame), frames", d => d.page.pushSmallFrames.frames, 0],
    ["push 20k/s (≤20 per frame), latency p99 ms", d => d.page.pushSmallFrames.latencyMs.p99, 2],
    ["push 20k/s (≤20 per frame), task ms / 3 s", d => d.page.pushSmallFrames.taskMs],
    ["push 20k/s (≤20 per frame), handler ms", d => d.page.pushSmallFrames.handlerMs],
    ["push, gaps / dropped (both phases)", d => d.page.push.dropped + d.page.pushSmallFrames.dropped, 0],
    ["cancel → confirmed, p50 ms", d => d.page.cancel.p50, 2],
    ["cancel → confirmed, p99 ms", d => d.page.cancel.p99, 2],
    ["host process CPU ms (whole run)", d => d.hostSide.cpuMs, 0],
  ];
  lines.push(`| Measure | ${encodings.join(" | ")} |`, `|---|${encodings.map(() => "---:").join("|")}|`);
  for (const [name, pick, digits = 1] of rows) {
    lines.push(`| ${name} | ${encodings.map(enc => cell(by(enc).flatMap(d => { try { const v = pick(d); return Array.isArray(v) ? v : [v]; } catch { return []; } }), digits)).join(" | ")} |`);
  }
  const checks = encodings.map(enc => {
    const all = by(enc);
    const ok = all.every(d => d.page.unaryEcho.ok === 2000 && d.page.unarySaveTraced.ok === 2000
      && d.page.rows1000.every(x => x.rows === 100000 && x.end === "end") && d.page.rows100.every(x => x.rows === 100000)
      && d.page.cancel.outcomes.cancelled === 200 && d.page.serializationFailure.failure?.kind === "response-serialization"
      && d.page.unknownCommand.code === "unknown-command" && d.page.push.outOfOrder === 0 && d.hostSide.malformed === 0);
    return `${enc}: ${ok ? "all checks passed" : "**a check failed**"} in ${all.length} runs`;
  });
  lines.push("", "Functional checks per run (2,000 ok results per unary phase, every row delivered, 200 confirmed cancels, " +
    "`response-serialization` failure and `unknown-command` classified, no out-of-order event, no malformed frame on the host): " +
    checks.join("; ") + ".", "");
  return lines.join("\n");
}

// ---------------------------------------------------------------- micro
function micro() {
  const hosts = [["desktop", "WebView2"], ["loopback", "Chromium headless shell"], ["webkitgtk", "WebKitGTK (WSL)"]];
  const lines = ["# Page-side codec cost (JavaScript, ns per message)", "",
    "Median over runs of each run's median of 7 samples (every sample at least 25 ms of repeated calls, encodings " +
    "interleaved). Decoding: A and C slice a header and `JSON.parse` the payload; B parses the whole envelope with one " +
    "`JSON.parse`. `payload only` is `JSON.parse` of the payload alone, the floor every encoding pays. Encoding includes " +
    "`JSON.stringify` of the argument.", ""];
  for (const [host, title] of hosts) {
    const runs = load(host).filter(r => r.mode === "micro" && !r.data.page.error);
    if (runs.length === 0) { lines.push(`## ${title}`, "", "Not run.", ""); continue; }
    lines.push(`## ${title}`, "", `${runs.length} runs. \`${runs[0].data.page.userAgent}\``, "");
    lines.push("| Decode (host → page) | A | B | C | payload only | B / A | C / A |", "|---|---:|---:|---:|---:|---:|---:|");
    for (const name of Object.keys(runs[0].data.page.decodeNs)) {
      const v = codec => median(runs.map(r => r.data.page.decodeNs[name][codec]?.medianNs));
      const [a, b, c, p] = ["A", "B", "C", "payloadOnly"].map(v);
      lines.push(`| ${name} | ${fmt(a, 0)} | ${fmt(b, 0)} | ${fmt(c, 0)} | ${fmt(p, 0)} | ${fmt(b / a, 2)} | ${fmt(c / a, 2)} |`);
    }
    lines.push("", "| Encode (page → host) | A | B | C | B / A | C / A |", "|---|---:|---:|---:|---:|---:|");
    for (const name of Object.keys(runs[0].data.page.encodeNs)) {
      const v = codec => median(runs.map(r => r.data.page.encodeNs[name][codec]?.medianNs));
      const [a, b, c] = ["A", "B", "C"].map(v);
      lines.push(`| ${name} | ${fmt(a, 0)} | ${fmt(b, 0)} | ${fmt(c, 0)} | ${fmt(b / a, 2)} | ${fmt(c / a, 2)} |`);
    }
    lines.push("");
  }
  return lines.join("\n");
}

// ---------------------------------------------------------------- page robustness
function robustness() {
  const lines = ["# Page decoder robustness (host → page frames)", "",
    "Outcome of each page decoder on malformed or newer frames: the message type it produced and the call id it could " +
    "attribute the frame to (−1: none). Identical on every engine unless a column says otherwise.", ""];
  const hosts = ["desktop", "loopback", "webkitgtk"];
  const firsts = hosts.map(h => load(h).find(r => r.mode === "micro" && r.data.page.robustness)).filter(Boolean);
  if (firsts.length === 0) return "";
  const show = o => o.threw ? `**threw** ${o.threw}` : `${o.type}, id ${o.id}` + (o.value !== undefined ? `, value ${o.value}` : "") + (o.items !== undefined ? `, ${o.items} items` : "") + (o.error ? ` (${o.error.replace(/\|/g, "\\|")})` : "");
  const base = firsts[0].data.page.robustness;
  for (const codec of Object.keys(base.decoders)) {
    lines.push(`## ${codec}`, "", "| Case | Outcome | Differs on |", "|---|---|---|");
    for (const [name, outcome] of Object.entries(base.decoders[codec])) {
      const differs = firsts.slice(1).filter(f => JSON.stringify(f.data.page.robustness.decoders[codec][name]) !== JSON.stringify(outcome))
        .map(f => `${f.file.includes("webkit") ? "" : ""}${f.data.page.host}: ${show(f.data.page.robustness.decoders[codec][name])}`);
      lines.push(`| ${name} | ${show(outcome)} | ${differs.join("; ") || ""} |`);
    }
    lines.push("");
  }
  lines.push("## Page encoders with awkward input", "", "| Encoding | Case | Output |", "|---|---|---|");
  for (const [codec, cases] of Object.entries(base.encoders)) {
    for (const [name, out] of Object.entries(cases)) lines.push(`| ${codec} | ${name} | \`${String(out).replace(/\|/g, "\\|").replace(/\n/g, "\\n")}\` |`);
  }
  lines.push("", `Engines compared: ${firsts.map(f => f.data.page.host).join(", ")}.`, "");
  return lines.join("\n");
}

// ---------------------------------------------------------------- WebView2 probes
function probe() {
  const run = load("desktop").find(r => r.mode === "probe");
  if (!run) return "";
  const p = run.data.page;
  const lines = ["# WebView2 transport probes", "", "What crosses `postMessage` / `PostWebMessageAsString` / `PostWebMessageAsJson` unchanged.", "",
    "## Page → host", "", "| Page posts | Host sees |", "|---|---|"];
  for (const key of Object.keys(p).filter(k => k.startsWith("page"))) {
    lines.push(`| ${key} | ${(p[key] || []).map(x => typeof x === "string" ? x : x.text).join("<br>").replace(/\|/g, "\\|")} |`);
  }
  lines.push("", "## Host → page", "", "| Host posts | Page receives |", "|---|---|");
  let current = null;
  for (const item of p.hostToPage) {
    if (item.kind === "string" && item.text.startsWith("#case ")) { current = item.text.slice(6); continue; }
    const shown = item.kind === "string" ? `string (${item.length} units): \`${item.text}\`` : `${item.kind}: \`${item.json}\`` + (item.ownProto ? " (own `__proto__` key)" : "");
    lines.push(`| ${current} | ${shown.replace(/\|/g, "\\|")} |`);
  }
  lines.push("");
  return lines.join("\n");
}

const write = (name, text) => { if (text) { writeFileSync(join(results, name), text); console.log("wrote results/" + name); } };
write("e2e-webview2.md", e2e("desktop", "WebView2 (NativeAOT host, postMessage)", ["A", "B", "C", "Bj"]));
write("e2e-loopback.md", e2e("loopback", "Chromium over the loopback channel (NativeAOT Kestrel host)", ["A", "B", "C"]));
write("micro-js.md", micro());
write("robustness-js.md", robustness());
write("probe-webview2.md", probe());
