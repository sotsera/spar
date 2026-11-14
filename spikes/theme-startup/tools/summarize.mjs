// Usage: node summarize.mjs <timeline.json | label>... [--frames] [--theme dark|light]
// Reads artifacts/theme-startup/<label>/timeline.json (written by both hosts) and prints the frame timeline as runs of
// identical classes, then the numbers: first visible frame, first app frame, white and wrong-colour frames, splash frames.
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const artifacts = path.resolve(here, "../../../artifacts/theme-startup");
const args = process.argv.slice(2);
const showFrames = args.includes("--frames");
const quiet = args.includes("--quiet");
const source = args.includes("--source") ? args[args.indexOf("--source") + 1] : "print";
const forcedTheme = args.includes("--theme") ? args[args.indexOf("--theme") + 1] : null;
const labels = args.filter((a, i) => !a.startsWith("--") && args[i - 1] !== "--theme" && args[i - 1] !== "--source");

const rgb = (hex) => [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16));
const near = (a, b, t = 10) => a.every((v, i) => Math.abs(v - b[i]) <= t);

// Windows frames carry the 5-bit bin centre (#1c1c1c for #1f1f1f, #f4f4f4 for #f3f3f3): tolerance 6. X11 frames carry the
// exact colour: tolerance 1, so GTK's own backgrounds (Yaru #f6f5f4) stay apart from the theme colour.
let tolerance = 6;
const tone = (c) => (near(c, [0x1f, 0x1f, 0x1f], tolerance) ? "dark" : near(c, [0xf3, 0xf3, 0xf3], tolerance) ? "light" : "#" + c.map((v) => v.toString(16).padStart(2, "0")).join(""));

export function classify(region) {
  const c = rgb(region.dom);
  if (region.app > 0.02) return "APP-" + tone(c);
  if (region.splash > 0.003) return "SPLASH-" + tone(c);
  if (near(c, [0xf3, 0xf3, 0xf3], tolerance)) return "light-bg";
  if (near(c, [0x1f, 0x1f, 0x1f], tolerance)) return "dark-bg";
  if (c.every((v) => v >= 0xf8)) return "WHITE";
  if (c.every((v) => v <= 0x0c)) return "BLACK";
  return region.dom;
}

const titleClass = (t) => {
  const [r, g, b] = rgb(t.dom);
  return 0.299 * r + 0.587 * g + 0.114 * b < 110 ? "dark" : "light";
};

export function summarize(file) {
  const t = JSON.parse(fs.readFileSync(file, "utf8"));
  tolerance = t.framesFile ? 1 : 6;
  const marks = t.marks;
  const start = marks.find((m) => m.name.startsWith("start "))?.name ?? "";
  const theme = forcedTheme ?? /effective=(\w+)/.exec(start)?.[1] ?? "dark";
  const markAt = (prefix) => marks.find((m) => m.name.startsWith(prefix))?.at;
  const created = markAt("window-created") ?? 0;
  const visibleAt = markAt("visible:") ?? markAt("shown-cloaked");
  const readyAt = markAt("ui-ready");

  // Frames the user could see: shown and not cloaked. WGC and X11 frames carry no flag: from the visible mark on.
  let all = source === "wgc" ? t.framesWgc : t.frames;
  if (t.framesFile) {
    const lines = fs.readFileSync(path.join(path.dirname(file), t.framesFile), "utf8").split(String.fromCharCode(10)).map((l) => l.trim()).filter(Boolean);
    all = lines.map((l) => JSON.parse(l)).map((f) => ({ ...f, at: f.mono - t.monotonicStartMs, ok: 1, visible: 1, cloaked: 0 }));
  }
  const fromMark = source === "wgc" || t.framesFile;
  // PrintWindow frames whose print started before the first WM_ERASEBKGND race the first paint (they show an empty
  // redirection surface that DWM never composes; Windows.Graphics.Capture never shows them): left out.
  const firstErase = source === "print" && !t.framesFile ? markAt("first WM_ERASEBKGND") : undefined;
  if (firstErase !== undefined) all = all.filter((f) => f.at >= firstErase);
  let frames = all.filter((f) => f.ok && f.visible && !f.cloaked && (!fromMark || visibleAt === undefined || f.at >= visibleAt - 0.5));
  // X11 (Xvfb, no window manager, root painted black with -br): until the window is mapped the region shows the root.
  let mappedAt;
  if (t.framesFile) {
    const firstMapped = frames.findIndex((f) => classify(f.client) !== "BLACK");
    mappedAt = frames[firstMapped]?.at;
    if (firstMapped > 0) frames = frames.slice(firstMapped);
    // After "closing" the window goes away (the root shows through): not part of the start-up.
    const closing = markAt("closing");
    if (closing !== undefined) frames = frames.filter((f) => f.at < closing);
  }
  const runs = [];
  for (const f of frames) {
    const cls = classify(f.client);
    const title = f.title ? titleClass(f.title) : "-";
    const key = `${cls}|${title}`;
    const last = runs.at(-1);
    if (last && last.key === key) {
      last.end = f.at;
      last.count++;
    } else {
      runs.push({ key, cls, title, start: f.at, end: f.at, count: 1, png: f.png, dom: f.client.dom });
    }
  }

  const expectedBg = theme === "dark" ? "dark-bg" : "light-bg";
  const wrong = (cls) => cls !== expectedBg && cls !== "APP-" + theme && cls !== "SPLASH-" + theme;
  const firstVisible = frames[0]?.at;
  const firstApp = frames.find((f) => classify(f.client).startsWith("APP"))?.at;
  const appIdx = frames.findIndex((f) => classify(f.client).startsWith("APP"));
  const beforeApp = appIdx < 0 ? frames : frames.slice(0, appIdx);
  const white = beforeApp.filter((f) => classify(f.client) === "WHITE");
  const wrongFrames = frames.filter((f) => wrong(classify(f.client)));
  const titleWrong = frames.filter((f) => f.title && titleClass(f.title) !== theme);
  const splashFrames = frames.filter((f) => classify(f.client).startsWith("SPLASH"));
  const splashRuns = runs.filter((r) => r.cls.startsWith("SPLASH")).length;
  const gaps = frames.slice(1).map((f, i) => f.at - frames[i].at);
  const meanGap = gaps.length ? gaps.reduce((a, b) => a + b, 0) / gaps.length : 0;
  const maxGap = gaps.length ? Math.max(...gaps) : 0;
  const durationOf = (list) => {
    // Time covered by these frames: each frame lasts until the next captured frame.
    let total = 0;
    for (const f of list) {
      const i = frames.indexOf(f);
      total += (frames[i + 1]?.at ?? f.at) - f.at;
    }
    return total;
  };

  const r1 = (x) => (x === undefined ? "-" : Math.round(x - created));
  const result = {
    label: t.label + (source === "wgc" ? " [wgc]" : ""),
    theme,
    frames: frames.length,
    meanGapMs: +meanGap.toFixed(1),
    maxGapMs: +maxGap.toFixed(1),
    visibleMs: r1(visibleAt),
    mappedMs: r1(mappedAt),
    firstFrameMs: r1(firstVisible),
    firstAppMs: r1(firstApp),
    uiReadyMs: r1(readyAt),
    whiteFrames: white.length,
    whiteMs: Math.round(durationOf(white)),
    wrongFrames: wrongFrames.length,
    wrongMs: Math.round(durationOf(wrongFrames)),
    wrongKinds: [...new Set(wrongFrames.map((f) => classify(f.client)))].join(" "),
    titleWrongFrames: titleWrong.length,
    splashFrames: splashFrames.length,
    splashRuns,
    splashMs: Math.round(durationOf(splashFrames)),
  };

  if (!quiet) {
    console.log(`\n== ${t.label} (${theme}) ${t.args}`);
    for (const m of marks) {
      if (/^(first WM_|page:(mark|script-start|mounted|splash)|ui-ready|visible|shown|controller|environment|navigat|serve \/(\w|-)+\.html|bundle|theme-|simulate|page-set|WM_|put_|FAILED|host-failure)/.test(m.name) || m.name.startsWith("page:"))
        console.log(`   mark ${String(Math.round(m.at - created)).padStart(6)} ms  ${m.name.slice(0, 150)}`);
    }
    for (const r of runs) {
      console.log(`   run  ${String(Math.round(r.start - created)).padStart(6)}..${String(Math.round(r.end - created)).padEnd(6)} ms  ${String(r.count).padStart(4)} fr  client=${r.cls.padEnd(8)} title=${r.title.padEnd(5)} ${r.png ?? ""}`);
    }
    if (showFrames) for (const f of frames) console.log(`   f ${Math.round(f.at - created)} ${JSON.stringify(f.client)} ${JSON.stringify(f.title)}`);
  }

  return result;
}

const results = [];
for (const label of labels) {
  const file = label.endsWith(".json") ? label : path.join(artifacts, label, "timeline.json");
  if (!fs.existsSync(file)) { console.log(`(missing ${label})`); continue; }
  results.push(summarize(file));
}

console.log("\n" + ["label", "theme", "frames", "meanGap", "visible", "firstApp", "uiReady", "white(fr/ms)", "wrong(fr/ms kinds)", "titleWrong", "splash(fr/runs/ms)"].join(" | "));
for (const r of results) {
  console.log([r.label, r.theme, r.frames, r.meanGapMs + "/" + r.maxGapMs, r.mappedMs === "-" ? r.visibleMs : r.visibleMs + ">" + r.mappedMs, r.firstAppMs, r.uiReadyMs, `${r.whiteFrames}/${r.whiteMs}`, `${r.wrongFrames}/${r.wrongMs} ${r.wrongKinds}`, r.titleWrongFrames, `${r.splashFrames}/${r.splashRuns}/${r.splashMs}`].join(" | "));
}

// --group: one line per configuration (label without -cold / -warmN): the cold run, then the warm runs' median and
// the worst case of the frame counts across all runs.
if (args.includes("--group")) {
  const median = (xs) => {
    const v = xs.filter((x) => typeof x === "number").sort((a, b) => a - b);
    return v.length ? v[Math.floor((v.length - 1) / 2)] : "-";
  };
  const groups = new Map();
  for (const r of results) {
    const key = r.label.replace(/-(cold|warm\d+)( \[wgc\])?$/, "$2");
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(r);
  }
  console.log("\n" + ["config", "theme", "runs", "firstApp cold/warm-median", "uiReady cold/warm-median", "white fr (max)", "wrong fr (max) kinds", "titleWrong (max)", "splash fr min-max / runs max"].join(" | "));
  for (const [key, rs] of groups) {
    const cold = rs.find((r) => /-cold/.test(r.label));
    const warm = rs.filter((r) => /-warm/.test(r.label));
    const max = (f) => Math.max(...rs.map(f));
    const kinds = [...new Set(rs.flatMap((r) => r.wrongKinds.split(" ").filter(Boolean)))].join(" ");
    console.log([key, rs[0].theme, rs.length, `${cold?.firstAppMs ?? "-"}/${median(warm.map((r) => r.firstAppMs))}`, `${cold?.uiReadyMs ?? "-"}/${median(warm.map((r) => r.uiReadyMs))}`,
      max((r) => r.whiteFrames), `${max((r) => r.wrongFrames)} ${kinds}`, max((r) => r.titleWrongFrames),
      `${Math.min(...rs.map((r) => r.splashFrames))}-${max((r) => r.splashFrames)} / ${max((r) => r.splashRuns)}`].join(" | "));
  }
}
