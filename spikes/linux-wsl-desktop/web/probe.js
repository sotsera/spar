// Probe for the WSLg desktop spike: reports page-side facts to the host through the injected channel
// (chrome.webview shape, presented by the host's document-start script).
//   ?raf=MS     measure requestAnimationFrame intervals for MS ms after ready (a CSS animation keeps compositing busy)
//   ?focus=1    report focus at ready, +250 ms and +1000 ms (always on)
const host = globalThis.chrome?.webview;
const query = new URLSearchParams(location.search);
const post = (message) => host?.postMessage(JSON.stringify(message));
const now = () => Math.round(performance.now() * 10) / 10;
const out = document.getElementById("out");
const mark = (n, extra) => { post({ t: "mark", n, p: now(), ...extra }); out.textContent += `${now()} ${n} ${extra ? JSON.stringify(extra) : ""}\n`; };
const report = (n, extra) => { post({ t: "report", n, p: now(), ...extra }); out.textContent += `${now()} ${n} ${JSON.stringify(extra)}\n`; };
const frame = () => new Promise((resolve) => requestAnimationFrame(resolve));
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

function facts() {
  return {
    dpr: devicePixelRatio,
    inner: `${innerWidth}x${innerHeight}`,
    outer: `${outerWidth}x${outerHeight}`,
    screen: `${screen.width}x${screen.height}`,
    hasFocus: document.hasFocus(),
    visibility: document.visibilityState,
    active: document.activeElement?.id || document.activeElement?.tagName,
    scheme: matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light",
    button: (() => { const r = document.getElementById("button").getBoundingClientRect(); return [Math.round(r.x + r.width / 2), Math.round(r.y + r.height / 2)]; })(),
  };
}

for (const type of ["keydown", "focusin", "pointerdown", "click"]) {
  addEventListener(type, (e) => mark("input-" + type, { trusted: e.isTrusted, key: e.key, target: e.target?.id || e.target?.tagName, x: e.clientX, y: e.clientY }), true);
}
addEventListener("focus", () => mark("window-focus", { hasFocus: document.hasFocus() }));
addEventListener("blur", () => mark("window-blur", { hasFocus: document.hasFocus() }));
matchMedia("(prefers-color-scheme: dark)").addEventListener("change", (e) => mark("scheme-change", { dark: e.matches }));
document.addEventListener("visibilitychange", () => mark("visibility", { state: document.visibilityState }));
addEventListener("resize", async () => {
  const at = performance.now();
  await frame();
  mark("resize", { inner: `${innerWidth}x${innerHeight}`, dpr: devicePixelRatio, frameAfterMs: Math.round(performance.now() - at) });
});

async function rafJitter(ms) {
  const stamps = [];
  const end = performance.now() + ms;
  while (performance.now() < end) stamps.push(await frame());
  const gaps = stamps.slice(1).map((t, i) => t - stamps[i]).sort((a, b) => a - b);
  const pick = (q) => Math.round(gaps[Math.min(gaps.length - 1, Math.floor(q * gaps.length))] * 100) / 100;
  const mean = gaps.reduce((a, b) => a + b, 0) / gaps.length;
  const sd = Math.sqrt(gaps.reduce((a, b) => a + (b - mean) ** 2, 0) / gaps.length);
  return {
    frames: stamps.length, ms, fps: Math.round((stamps.length - 1) / ((stamps.at(-1) - stamps[0]) / 1000) * 10) / 10,
    mean: Math.round(mean * 100) / 100, sd: Math.round(sd * 100) / 100, p50: pick(0.5), p95: pick(0.95), p99: pick(0.99), max: pick(1),
    over20: gaps.filter((g) => g > 20).length, over33: gaps.filter((g) => g > 33.4).length,
  };
}

async function main() {
  if (document.readyState === "loading") await new Promise((r) => document.addEventListener("DOMContentLoaded", r, { once: true }));
  const domAt = now();
  const first = await frame();
  const firstAt = now();
  await frame();
  const secondAt = now();
  const paints = Object.fromEntries(performance.getEntriesByType("paint").map((e) => [e.name, Math.round(e.startTime)]));
  post({ t: "ready", p: now(), domAt, firstRafAt: firstAt, firstRafStamp: Math.round(first), secondRafAt: secondAt, ...facts(), paints, ua: navigator.userAgent });
  out.textContent += `ready ${JSON.stringify(facts())}\n`;
  await sleep(250);
  report("focus-250", facts());
  await sleep(750);
  report("focus-1000", facts());
  const raf = Number(query.get("raf") ?? 0);
  if (raf > 0) report("raf", await rafJitter(raf));
}

main();
