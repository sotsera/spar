// The Invoke spike's page: every event that a click through UI Automation could produce is timestamped here
// (performance.timeOrigin + performance.now(), Unix milliseconds) and sent to the backend, which adds its own clock.
// Once a second it also reports how many animation frames and 100 ms timer ticks ran, and whether the page is visible.
import { connectSpar, hostChannel } from "@spar/app/sdk";
import { fingerprint } from "@spar/app/spar";
import { probe } from "@spar/app/probe";

const connection = connectSpar(hostChannel(), fingerprint);
const now = () => (performance.timeOrigin + performance.now()).toFixed(1);
const mark = text => { void probe.mark(`${now()} ${text}`); };
const state = () => `vis=${document.visibilityState} focus=${document.hasFocus()} active=${document.activeElement?.id || document.activeElement?.tagName}`;

for (const type of ["pointerdown", "mousedown", "pointerup", "mouseup", "focusin", "click", "keydown"]) {
  addEventListener(type, event => mark(`${type} ${event.target?.id || event.target?.tagName} trusted=${event.isTrusted} ${state()}`), true);
}

addEventListener("focus", () => mark(`window-focus ${state()}`));
addEventListener("blur", () => mark(`window-blur ${state()}`));
document.addEventListener("visibilitychange", () => mark(`visibility ${state()}`));

let count = 0;
const status = document.getElementById("status");
const dialog = document.getElementById("dialog");
document.getElementById("count").addEventListener("click", () => { status.textContent = String(++count); });
document.getElementById("open").addEventListener("click", () => dialog.showModal());
document.getElementById("close").addEventListener("click", () => dialog.close());

let frames = 0;
const frame = () => { frames++; requestAnimationFrame(frame); };
requestAnimationFrame(frame);
let ticks = 0;
setInterval(() => { ticks++; }, 100);
let last = performance.now();
setInterval(() => {
  const at = performance.now();
  mark(`stats elapsed=${(at - last).toFixed(0)} frames=${frames} ticks=${ticks} ${state()}`);
  frames = 0;
  ticks = 0;
  last = at;
}, 1000);

mark(`loaded ${state()}`);
connection.ready();
