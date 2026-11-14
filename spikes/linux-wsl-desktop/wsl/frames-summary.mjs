// Usage: node wsl/frames-summary.mjs <run-dir> [<windows-capture-dir>]
// Collapses captured frames into runs of the same class. X11 frames (x11-frames.jsonl, CLOCK_MONOTONIC) are placed on
// the host's clock (ms after window creation); Windows frames (wincap.jsonl) are in ms after the window appeared.
import { readFileSync, existsSync } from "node:fs";
import { join } from "node:path";

const [runDir, winDir] = process.argv.slice(2);
const runs = (frames, at) => {
  const out = [];
  for (const f of frames) {
    const last = out.at(-1);
    const cls = f.class.startsWith("other") ? `${f.class.split(":")[0]}(${f.dom})` : f.class;
    if (last && last.cls === cls) { last.n++; last.end = at(f); } else out.push({ cls, n: 1, start: at(f), end: at(f) });
  }
  return out.map((r) => `${r.cls}×${r.n} [${r.start.toFixed(0)}–${r.end.toFixed(0)}]`).join(" → ");
};
const timeline = JSON.parse(readFileSync(join(runDir, "timeline.json"), "utf8"));
const created = timeline.monotonicStartMs + timeline.windowCreatedMs;
const x11 = join(runDir, "x11-frames.jsonl");
if (existsSync(x11)) {
  const lines = readFileSync(x11, "utf8").trim().split("\n").map((l) => JSON.parse(l));
  const frames = lines.filter((l) => l.class);
  const firstViewable = frames[0] ? frames[0].mono - created : null;
  console.log(`x11: first frame at ${firstViewable?.toFixed(0)} ms after window creation; ${frames.length} frames; ready at ${(timeline.readyMs - timeline.windowCreatedMs).toFixed(0)} ms`);
  console.log("  " + runs(frames, (f) => f.mono - created));
}
if (winDir && existsSync(join(winDir, "wincap.jsonl"))) {
  const frames = readFileSync(join(winDir, "wincap.jsonl"), "utf8").trim().split("\n").map((l) => JSON.parse(l)).filter((l) => l.class);
  console.log(`windows: ${frames.length} frames (ms after the window appeared on Windows)`);
  console.log("  " + runs(frames, (f) => f.at));
}
