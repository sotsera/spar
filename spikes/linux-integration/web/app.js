// Probe page: a title bar drawn by the page. Buttons call host commands; the host reports the window state.
const $ = id => document.getElementById(id);
const post = message => window.spar.post(message);
const lines = [];
const show = text => { lines.unshift(text); lines.length = Math.min(lines.length, 18); $("log").textContent = lines.join("\n"); };

let ticks = 0, hiddenTicks = 0, hiddenAt = null;
setInterval(() => { ticks++; if (document.visibilityState === "hidden") hiddenTicks++; $("ticks").textContent = String(ticks); }, 100);

$("min").addEventListener("click", () => post({ t: "cmd", c: "minimize" }));
$("max").addEventListener("click", () => post({ t: "cmd", c: "toggle-maximize" }));
$("close").addEventListener("click", () => post({ t: "cmd", c: "close" }));

for (const type of ["mousedown", "mouseup", "click", "dblclick", "contextmenu"]) {
  addEventListener(type, event => {
    const target = event.target instanceof Element ? (event.target.id || event.target.tagName.toLowerCase()) : "?";
    post({ t: "input", type, button: event.button, detail: event.detail, trusted: event.isTrusted, target, x: event.clientX, y: event.clientY });
  });
}
$("search").addEventListener("focus", () => post({ t: "input", type: "focus", target: "search" }));

document.addEventListener("visibilitychange", () => {
  const state = document.visibilityState;
  $("vis").textContent = state;
  post({ t: "visibility", state, ticks, hiddenTicks });
  if (state === "hidden") { hiddenAt = performance.now(); return; }
  const visibleAt = performance.now();
  requestAnimationFrame(() => requestAnimationFrame(() => post({
    t: "shown-frame", ticks, hiddenTicks, hiddenMs: hiddenAt === null ? null : Math.round(visibleAt - hiddenAt),
    visibleToFrameMs: Math.round(performance.now() - visibleAt), kept: $("kept").value,
  })));
});

// Clipboard through the page API (the host decides the permission).
$("copy").addEventListener("click", async () => {
  try { await navigator.clipboard.writeText("spar-clip-page-" + ticks); post({ t: "clip", op: "writeText", ok: true }); }
  catch (error) { post({ t: "clip", op: "writeText", ok: false, error: String(error) }); }
});
$("paste").addEventListener("click", async () => {
  try { const text = await navigator.clipboard.readText(); post({ t: "clip", op: "readText", ok: true, own: text.startsWith("spar-clip-"), text: text.startsWith("spar-clip-") ? text : `(${text.length} chars)` }); }
  catch (error) { post({ t: "clip", op: "readText", ok: false, error: String(error) }); }
});

window.spar.on(message => {
  if (message.t === "state") {
    $("state").textContent = message.state;
    $("max").textContent = message.maximized ? "❐" : "□";
    $("max").setAttribute("aria-label", message.maximized ? "Restore" : "Maximize");
    document.documentElement.dataset.maximized = String(message.maximized);
    post({ t: "state-seen", state: message.state, maximized: message.maximized });
  } else if (message.t === "eval") {
    // Spike-only remote control from the host's control pipe (the page has no eval: the CSP forbids it).
    const [op, ...rest] = message.code.split(" ");
    if (op === "set-kept") $("kept").value = rest.join(" ");
    if (op === "report") post({ t: "report", ticks, hiddenTicks, kept: $("kept").value, vis: document.visibilityState, hasFocus: document.hasFocus() });
    if (op === "css-support") post({ t: "css-support",
      appRegion: CSS.supports("app-region", "drag"), webkitAppRegion: CSS.supports("-webkit-app-region", "drag"),
      computedAppRegion: getComputedStyle($("titlebar")).getPropertyValue("app-region") || "(empty)",
      computedWebkitAppRegion: getComputedStyle($("titlebar")).getPropertyValue("-webkit-app-region") || "(empty)",
      customProperty: getComputedStyle($("drag-spot")).getPropertyValue("--spar-region").trim(),
      regionCostMs: window.spar.regionCost.length ? Math.max(...window.spar.regionCost).toFixed(3) : null });
    if (op === "click") $(rest[0]).click();
    show("eval " + message.code);
  }
  show(JSON.stringify(message));
});

const rect = id => { const r = $(id).getBoundingClientRect(); return [Math.round(r.left + r.width / 2), Math.round(r.top + r.height / 2), Math.round(r.width), Math.round(r.height)]; };
requestAnimationFrame(() => requestAnimationFrame(() => post({
  t: "ready", dpr: devicePixelRatio, w: innerWidth, h: innerHeight, vis: document.visibilityState, config: window.spar.config,
  rects: { titlebar: rect("titlebar"), title: rect("app-title"), spot: rect("drag-spot"), search: rect("search"), min: rect("min"), max: rect("max"), close: rect("close"), copy: rect("copy"), paste: rect("paste") },
})));
