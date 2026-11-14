// Navigation-lock probes. Each step announces itself, acts, waits, and reports that this document is still alive.
// The host logs what it cancelled; a step after which no "alive" arrives means the page navigated away.
import { send, wait, frames, external } from "/report.js";

const page = name => `${external}/page.html?p=${name}`;
const target = document.getElementById("target");
const steps = [
  ["assign", () => { location.href = page("assign"); }],
  ["replace", () => { location.replace(page("replace")); }],
  ["unknownScheme", () => { location.href = "spar-unknown-scheme:probe"; }],
  ["metaRefresh", () => {
    const meta = document.createElement("meta");
    meta.httpEquiv = "refresh";
    meta.content = "0;url=" + page("metaRefresh");
    document.head.append(meta);
  }],
  ["redirect", () => { location.href = "/_test/redirect?to=" + encodeURIComponent(page("redirect")); }],
  ["redirectImg", () => {
    const img = new Image();
    img.src = "/_test/redirect?to=" + encodeURIComponent(`${external}/img.svg?p=redirectImg`);
  }],
  ["formTop", () => {
    const form = document.createElement("form");
    form.method = "post";
    form.action = `${external}/form?p=formTop`;
    document.body.append(form);
    form.submit();
  }],
  ["iframe", () => {
    const frame = document.createElement("iframe");
    frame.src = page("iframe");
    document.body.append(frame);
  }],
  ["iframeNavigate", () => {
    document.getElementById("sink").contentWindow.location.href = page("iframeNavigate");
  }],
  ["iframeFormTarget", () => {
    const form = document.createElement("form");
    form.method = "post";
    form.action = `${external}/form?p=iframeFormTarget`;
    form.target = "sink";
    document.body.append(form);
    form.submit();
  }],
  ["windowOpen", async () => {
    const opened = window.open(page("windowOpen"));
    await send({ kind: "note", step: "windowOpen", returned: opened === null ? "null" : typeof opened });
  }],
  ["windowOpenSelfOrigin", async () => {
    const opened = window.open("/blank.html?p=windowOpenSelfOrigin");
    await send({ kind: "note", step: "windowOpenSelfOrigin", returned: opened === null ? "null" : typeof opened });
  }],
  ["syntheticBlankClick", () => {
    const a = document.createElement("a");
    a.href = page("syntheticBlankClick");
    a.target = "_blank";
    document.body.append(a);
    a.click();
  }],
  ["trustedBlankClick", async () => {
    target.href = page("trustedBlankClick");
    target.target = "_blank";
    await frames(2);
    await send({ kind: "click", button: "left", x: 300, y: 120, modifiers: 0 });
  }],
  ["trustedMiddleClick", async () => {
    target.href = page("trustedMiddleClick");
    target.removeAttribute("target");
    await frames(2);
    await send({ kind: "click", button: "middle", x: 300, y: 120, modifiers: 0 });
  }],
  ["trustedCtrlClick", async () => {
    target.href = page("trustedCtrlClick");
    await frames(2);
    await send({ kind: "click", button: "left", x: 300, y: 120, modifiers: 2 });
  }],
  ["trustedShiftClick", async () => {
    target.href = page("trustedShiftClick");
    await frames(2);
    await send({ kind: "click", button: "left", x: 300, y: 120, modifiers: 8 });
  }],
  ["trustedPlainClick", async () => {
    target.href = page("trustedPlainClick");
    await frames(2);
    await send({ kind: "click", button: "left", x: 300, y: 120, modifiers: 0 });
  }],
  ["openExternal", async () => {
    await fetch("/_spar/log", { method: "POST", body: JSON.stringify({ kind: "open-external", url: `${external}/opened?p=openExternal` }) });
  }],
];

// ?skip=a,b leaves steps out (WebKitGTK does not follow a redirect from a scheme handler: it shows the 302 body as the
// document, which would end the run).
const skip = (new URL(location.href).searchParams.get("skip") ?? "").split(",");
await frames(2);
for (const [name, act] of steps.filter(([n]) => !skip.includes(n))) {
  await send({ kind: "step", name });
  try {
    await act();
  } catch (error) {
    await send({ kind: "step-error", name, error: String(error) });
  }
  await wait(name.startsWith("trusted") ? 2000 : 900);
  await send({ kind: "alive", name, href: location.href });
}

// Same-origin navigation must still work under the lock.
await send({ kind: "step", name: "sameOrigin" });
location.href = "/nav-done.html";
