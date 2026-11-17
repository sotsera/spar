// The probe page: asks the backend which probe to run, runs it, and reports each result through the probe contract,
// which writes it to the app's standard error. Every probe stays inside the page: none needs real input, and nothing
// it does may open a window or reach the network.
import { connectSpar, hostChannel } from "@spar/app/sdk";
import { fingerprint } from "@spar/app/spar";
import { probe } from "@spar/app/probe";

const connection = connectSpar(hostChannel(), fingerprint);
connection.ready();

const report = text => probe.report(text);
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));

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
};

const mode = await probe.mode().unwrap();
const [name, argument] = mode.split(":");
const run = probes[name];
if (run === undefined) {
  await report(`unknown mode ${mode}`);
} else {
  await run(Number(argument ?? "0"));
  await report(`done ${mode}`);
}
