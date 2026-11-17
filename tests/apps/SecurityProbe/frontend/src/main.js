// The probe page: asks the backend which probe to run, runs it, and reports each result through the probe contract,
// which writes it to the app's standard error. Nothing a probe does may open a window or reach the network. The probes
// of the browser defaults that need input get it from the Development-only SPAR_DEV_INPUT trigger, the in-process
// DevTools Protocol, never from the desktop's real mouse and keyboard.
import { connectSpar, hostChannel } from "@spar/app/sdk";
import { fingerprint } from "@spar/app/spar";
import { probe } from "@spar/app/probe";

const connection = connectSpar(hostChannel(), fingerprint);
connection.ready();

const report = text => probe.report(text);
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));

// Places an element at a fixed CSS pixel, through the CSSOM: the policy blocks style attributes, not style properties.
const place = (element, left, top) => {
  Object.assign(element.style, { position: "absolute", left: `${left}px`, top: `${top}px`, width: "200px", height: "40px" });
  document.body.append(element);
  return element;
};

// The first document opens a second one, which runs `back`; the probe reports where each document ends up.
const backToFirst = async (back, interact = false) => {
  const step = sessionStorage.getItem("history") ?? "first";
  if (step === "first") {
    if (interact) {
      // A click first: the browser's back button skips entries of documents the user never interacted with.
      await new Promise(resolve => {
        document.addEventListener("click", resolve, { once: true });
        setTimeout(resolve, 3_000);
      });
    }

    sessionStorage.setItem("history", "second");
    await report("history: opening a second document");
    location.href = "/index.html?second";
    return "stay";
  }

  if (step === "second") {
    sessionStorage.setItem("history", "back");
    await report(`history: on the second document, ${history.length} entries; going back`);
    await back();
    await pause(2_000);
    await report(`history: still on the second document (${location.search})`);
    return undefined;
  }

  await report("history: back on the first document");
  return undefined;
};

const probes = {
  // Nothing: a baseline, and the policy's own checks of the page as built.
  idle: async () => {
    await report(`ready; nonce meta ${document.querySelector("meta[property=csp-nonce]")?.nonce ? "present" : "absent"}`);
  },

  // N violations of one group: a style attribute set from script, which style-src-attr blocks.
  flood: async count => {
    const element = document.getElementById("status");
    const started = performance.now();
    for (let i = 0; i < count; i++) {
      element.setAttribute("style", "color: red");
    }

    await report(`flood: raised ${count} in ${(performance.now() - started).toFixed(1)} ms`);
  },

  // N violations in N groups: images from N addresses, which img-src blocks before any request.
  distinct: async count => {
    const started = performance.now();
    for (let i = 0; i < count; i++) {
      new Image().src = `https://blocked.invalid/${i}.png`;
    }

    await report(`distinct: raised ${count} in ${(performance.now() - started).toFixed(1)} ms`);
  },

  // A module worker the host serves: does its own response carry the policy? Under Trusted Types a worker's address
  // must come from a policy (the Worker constructor is a script sink), so the address Vite emits is passed through one.
  worker: async () => {
    const policy = trustedTypes.createPolicy("probe-worker", { createScriptURL: url => url });
    const address = new URL("./worker.js", import.meta.url).href;
    const worker = new Worker(policy.createScriptURL(address), { type: "module" });
    const result = await new Promise(resolve => {
      worker.onmessage = event => resolve(event.data);
      worker.onerror = event => resolve({ error: event.message });
    });
    await report(`worker: ${JSON.stringify(result)}`);
  },

  // The style nonce: a <style> with it applies, one without is blocked.
  nonce: async () => {
    const nonce = document.querySelector("meta[property=csp-nonce]")?.nonce ?? "";
    const allowed = document.createElement("style");
    allowed.nonce = nonce;
    allowed.textContent = "#status { margin-left: 7px; }";
    document.head.append(allowed);
    const blocked = document.createElement("style");
    blocked.textContent = "#status { margin-right: 9px; }";
    document.head.append(blocked);
    const style = getComputedStyle(document.getElementById("status"));
    await report(`nonce: length ${nonce.length}, with nonce ${style.marginLeft}, without ${style.marginRight}`);
  },

  // The navigation lock, by script only (a user's click cannot be made here): each must leave the page where it is.
  navigate: async () => {
    window.open("https://example.com/opened-by-script");
    const link = document.createElement("a");
    link.href = "https://example.com/blank-by-script";
    link.target = "_blank";
    document.body.append(link);
    link.click();
    location.href = "https://example.com/top-level";
    await pause(500);
    location.href = "mailto:someone@example.com";
    await pause(500);
    await report(`navigate: still on ${location.href}`);
  },

  // Permissions: notifications and geolocation are not declared, MIDI is.
  permissions: async () => {
    const notifications = await Notification.requestPermission();
    const geolocation = await new Promise(resolve => navigator.geolocation.getCurrentPosition(
      () => resolve("granted"),
      error => resolve(`error ${error.code}`),
      { timeout: 5_000 }));
    const midi = await navigator.requestMIDIAccess({ sysex: true }).then(() => "granted", error => `refused (${error.name})`);
    // The camera and microphone are never asked for here: on a desktop with a person, a real device may prompt.
    await report(`permissions: notifications ${notifications}, geolocation ${geolocation}, midi ${midi}`);
  },

  // A page that hangs in its start-up code: the host's script must still be the one that stops answering.
  "hang-early": async () => {
    await report("hang-early: going to hang.html");
    location.href = "/hang.html";
  },

  // The browser defaults a page can observe without input (plan section 3c): the folders app code sees, the File
  // System Access pickers, host objects, and a download the page builds itself.
  defaults: async () => {
    await report(`folders: ${await probe.folders().unwrap()}`);
    const pickers = ["showOpenFilePicker", "showSaveFilePicker", "showDirectoryPicker"].map(name => `${name} ${typeof window[name]}`);
    await report(`pickers: ${pickers.join(", ")}`);
    await report(`hostObjects: ${typeof window.chrome?.webview?.hostObjects}`);
    const link = document.createElement("a");
    link.href = URL.createObjectURL(new Blob(["Spar M1b step 5 probe download\n"], { type: "text/plain" }));
    link.download = "spar-step5-probe-download.txt";
    document.body.append(link);
    link.click();
    await pause(2_000);
    await report("download: clicked");
  },

  // History navigation to another document: the first document opens a second one, which goes back by script, the
  // same navigation kind as the mouse's back button. Each document runs this probe again from sessionStorage.
  history: async () => backToFirst(async () => {
    history.back();
  }),

  // The same, with the mouse's back button through the Development-only input trigger (SPAR_DEV_INPUT click:x,y, then
// back:x,y), after a click on the first document.
  "mouse-back": async () => backToFirst(async () => {
    await pause(4_000);
  }, true),

  // A move within one document (pushState), then the mouse's back button: not a navigation, so it is not cancelled.
  "mouse-back-same": async () => {
    history.pushState({ step: 1 }, "", "#one");
    history.pushState({ step: 2 }, "", "#two");
    await report(`mouse-back-same: on ${location.hash}`);
    await pause(4_000);
    await report(`mouse-back-same: after the back button on ${location.hash}`);
  },

  // <input type="file"> clicked by trusted input (SPAR_DEV_INPUT click:120,40): no picker, no change, no files.
  picker: async () => {
    const input = place(document.createElement("input"), 20, 20);
    input.type = "file";
    const seen = { click: 0, change: 0, cancel: 0 };
    for (const name of Object.keys(seen)) {
      input.addEventListener(name, () => seen[name]++);
    }

    await report("picker: ready at 20,20");
    await pause(6_000);
    await report(`picker: click ${seen.click}, change ${seen.change}, cancel ${seen.cancel}, files ${input.files?.length ?? 0}`);
  },

  // Context menus by trusted input (SPAR_DEV_INPUT right:x,y): the host logs what it kept. A text field at 20,100.
  menu: async () => {
    const field = place(document.createElement("input"), 20, 100);
    field.value = "editable";
    let menus = 0;
    document.addEventListener("contextmenu", () => menus++);
    await report("menu: field at 20,100");
    await pause(5_000);
    await report(`menu: contextmenu events ${menus}`);
  },

  // Browser keys by trusted input (SPAR_DEV_INPUT key:…): reloads are counted across documents, print by beforeprint.
  keys: async () => {
    const loads = Number(sessionStorage.getItem("keys") ?? "0") + 1;
    sessionStorage.setItem("keys", String(loads));
    const seen = [];
    document.addEventListener("keydown", event => seen.push(`${event.ctrlKey ? "ctrl+" : ""}${event.altKey ? "alt+" : ""}${event.key}`));
    let printed = 0;
    window.addEventListener("beforeprint", () => printed++);
    await report(`keys: load ${loads}`);
    await pause(loads === 1 ? 12_000 : 8_000);
    await report(`keys: load ${loads} saw [${seen.join(" ")}], print opened ${printed}`);
  },
};

const mode = await probe.mode().unwrap();
const [name, argument] = mode.split(":");
const run = probes[name];
if (run === undefined) {
  await report(`unknown mode ${mode}`);
} else {
  if (await run(Number(argument ?? "0")) !== "stay") {
    await report(`done ${mode}`);
  }
}
