// The security-default steps of the bridge probe (M1c step 6, docs/evidence/m1c-security-defaults.md): what a debugging
// object of JavaScriptCore's would show the page, downloads, the file picker, the context menu, the browser's keys and
// history navigation, each with the opt-in the test sets through BRIDGE_PROBE_BROWSER. The test drives the picker, the
// menu and the keys with real input on its own X server; every result is a note in the backend's log.
import { probe } from "@spar/app/probe";

const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
const note = text => probe.note(text).unwrap();

// One element over the whole window, so the test's click in the middle of the window lands on it.
const cover = element => {
  element.style.cssText = "position:fixed;inset:0;width:100vw;height:100vh;margin:0;opacity:0.5;z-index:10";
  document.body.append(element);
  return element;
};

const download = async (label, href, name) => {
  const link = document.createElement("a");
  link.href = href;
  link.download = name;
  link.textContent = label;
  document.body.append(link);
  link.click();
  await pause(800);
  link.remove();
  return `${label} clicked`;
};

export const defaultsSteps = () => ({
  // JavaScriptCore's debugging object, which JSC_useDollarVM would install, as the page sees it.
  async jsc() {
    const vm = typeof globalThis.$vm;
    const members = vm === "object" ? Object.getOwnPropertyNames(globalThis.$vm).length : 0;
    await note(`jsc: $vm ${vm}, ${members} members, describe ${typeof globalThis.describe}`);
  },

  // A download of the app's own file, of a blob and of a data: address, each by a link with "download".
  async download() {
    const results = [
      await download("own file", "/other.html", "other.html"),
      await download("blob", URL.createObjectURL(new Blob(["probe"], { type: "text/plain" })), "probe.txt"),
      await download("data", "data:text/plain,probe", "data.txt"),
    ];
    await note(`download: ${results.join(", ")}`);
  },

  // A file input over the whole window; the test clicks it. Its events say what the picker answered.
  async picker() {
    const input = cover(document.createElement("input"));
    input.type = "file";
    const answered = new Promise(resolve => {
      input.addEventListener("change", () => resolve(`change, ${input.files.length} files`), { once: true });
      input.addEventListener("cancel", () => resolve("cancel"), { once: true });
    });
    await note("picker ready");
    const answer = await Promise.race([answered, pause(8_000).then(() => "no answer")]);
    await note(`picker: ${answer}`);
    input.remove();
  },

  // An editable field, or plain text, over the whole window, for the test's right click.
  async menu(kind) {
    const element = kind === "editable" ? document.createElement("textarea") : document.createElement("div");
    element.textContent = "Context menu target";
    cover(element);
    if (kind === "editable") {
      element.focus();
    }

    await note(`menu ready (${kind})`);
  },

  // The page sees every key the test presses; the zoom and the address show whether the browser acted on one.
  async keys() {
    const seen = [];
    const zoom = () => `${window.devicePixelRatio}/${window.innerWidth}`;
    const before = zoom();
    const done = new Promise(resolve => {
      window.addEventListener("keydown", event => {
        if (event.key === "Escape") {
          resolve();
          return;
        }

        const modifiers = `${event.ctrlKey ? "Ctrl+" : ""}${event.shiftKey ? "Shift+" : ""}${event.altKey ? "Alt+" : ""}`;
        seen.push(`${modifiers}${event.key}`);
      });
    });
    await note("keys ready");
    await Promise.race([done, pause(30_000)]);
    await pause(500);
    await note(`keys: seen [${seen.join(" ")}], zoom ${before} -> ${zoom()}, at ${location.pathname}`);
  },

  // A move within the document, then to another document, whose page goes back (other.js).
  async history() {
    history.pushState({ step: 1 }, "", "#moved");
    const popped = new Promise(resolve => window.addEventListener("popstate", () => resolve(location.hash || "none"), { once: true }));
    history.back();
    const hash = await Promise.race([popped, pause(3_000).then(() => "no popstate")]);
    await note(`history: within the document back to ${hash}`);
    location.href = "/other.html";
  },
});
