// The bridge probe's first page: connects, reports ui-ready, then runs the steps the test asked for
// (BRIDGE_PROBE_PLAN, comma-separated) and reports what it saw through the backend's log (probe.note).
import { connectSpar, hostChannel } from "@spar/app/sdk";
import { fingerprint } from "@spar/app/spar";
import { probe } from "@spar/app/probe";

const status = document.getElementById("status");
const connection = connectSpar(hostChannel(), fingerprint);

const asked = await probe.plan().unwrap();
const plan = asked.steps.split(",").filter(step => step !== "");
const reloaded = asked.load > 1;

// After a web process was lost the host reloads the page once (the backend counts the starts: the page cannot tell);
// this lets the test end the next process before ready.
if (reloaded && plan.includes("slow-ready-after-reload")) {
  await probe.note("reloaded, ready in 3 s").unwrap();
  await new Promise(resolve => setTimeout(resolve, 3_000));
}

connection.ready();
status.textContent = "Ready";
await probe.note(`first document ${connection.document}${reloaded ? " (reloaded)" : ""}`).unwrap();

const steps = {
  // What the page's own world can reach: neither the relay nor the script message handler.
  async facts() {
    const handler = typeof window.webkit?.messageHandlers?.spar;
    const relay = typeof globalThis.sparRelay;
    await probe.note(`main world: handler ${handler}, relay ${relay}`).unwrap();
  },

  // Sequential small calls: the page's timer has 1 ms steps on WebKitGTK, so the mean of many is reported.
  async echo() {
    for (let i = 0; i < 50; i++) {
      await probe.echo("warm").unwrap();
    }

    const count = 1_000;
    const started = performance.now();
    for (let i = 0; i < count; i++) {
      await probe.echo("ping").unwrap();
    }

    const mean = (performance.now() - started) / count;
    await probe.note(`echo mean ${mean.toFixed(3)} ms over ${count}`).unwrap();
  },

  // A frame of another origin, which must not reach the bridge: it reports to its own server, not here.
  async frame() {
    const address = await probe.frame().unwrap();
    const frame = document.createElement("iframe");
    const loaded = new Promise(resolve => frame.addEventListener("load", resolve, { once: true }));
    frame.src = address;
    frame.title = "Foreign frame";
    document.getElementById("frames").append(frame);
    await loaded;
    await probe.note("frame loaded").unwrap();

    // Give its script time to try everything before the test reads the log.
    await new Promise(resolve => setTimeout(resolve, 500));
    await probe.note("frame done").unwrap();
  },

  navigate() {
    location.href = "/other.html";
  },
};

for (const step of plan) {
  if (step in steps) {
    await steps[step]();
  }
}

await probe.note("plan done").unwrap();
