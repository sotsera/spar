// Desktop-origin spike instrumentation (question 6), development only. Reports HMR milestones to /_spar/hmr with
// wall-clock times, so the edit driver (another process on the same machine) can compute latencies:
// - before/after: Vite's update events;
// - dom: the <h1> text changed (Fast Refresh applied); mounted shows whether the component kept its state;
// - css: the <h1> colour changed (CSS hot update applied);
// - overlay: Vite's error overlay appeared or went away.
function report(event: string, detail: Record<string, string | number | boolean>) {
  const query = new URLSearchParams({ event, epoch: String(Date.now()), ...Object.fromEntries(Object.entries(detail).map(([k, v]) => [k, String(v)])) });
  void fetch(`/_spar/hmr?${query}`).catch(() => undefined);
}

if (import.meta.hot) {
  import.meta.hot.on("vite:beforeUpdate", (payload) => report("before", { paths: payload.updates.map((u) => u.path).join(",") }));
  import.meta.hot.on("vite:afterUpdate", (payload) => report("after", { paths: payload.updates.map((u) => u.path).join(",") }));
  import.meta.hot.on("vite:error", (payload) => report("vite-error", { message: payload.err.message.slice(0, 80) }));
  import.meta.hot.on("vite:ws:connect", () => report("ws-connect", { url: location.origin }));
  import.meta.hot.on("vite:ws:disconnect", () => report("ws-disconnect", { url: location.origin }));
}

let lastTitle = "";
let lastColor = "";
let overlay = false;
function check() {
  const h1 = document.querySelector("h1");
  if (h1) {
    const title = h1.textContent ?? "";
    if (title !== lastTitle) {
      lastTitle = title;
      report("dom", { title, mounted: h1.closest("header")?.getAttribute("data-mounted") ?? "" });
    }

    const color = getComputedStyle(h1).color;
    if (color !== lastColor) {
      lastColor = color;
      report("css", { color });
    }
  }

  const present = document.querySelector("vite-error-overlay") !== null;
  if (present !== overlay) {
    overlay = present;
    report("overlay", { present });
  }
}

new MutationObserver(check).observe(document, { subtree: true, childList: true, characterData: true, attributes: true });
addEventListener("load", check);
