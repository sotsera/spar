// The WebAssembly step of the bridge probe (M1c step 5, docs/evidence/m1c-wasm.md): whether the WebAssembly global
// exists and a module runs, in the page and in a dedicated, a shared and a service worker. The host sets JSC_useWasm
// before WebKit starts: false by default, so the global is absent everywhere; true with csp.AllowWebAssembly(), which
// also adds 'wasm-unsafe-eval' to the policy. The result is one note in the backend's log.
import { probe } from "@spar/app/probe";

// A module exporting add(i32, i32) -> i32 (41 bytes); the same bytes as in wasm-worker.js.
const adder = new Uint8Array([
  0, 97, 115, 109, 1, 0, 0, 0, 1, 7, 1, 96, 2, 127, 127, 1, 127, 3, 2, 1, 0, 7, 7, 1, 3, 97, 100, 100, 0, 0, 10, 9, 1,
  7, 0, 32, 0, 32, 1, 106, 11,
]);

const check = async () => {
  if (typeof WebAssembly === "undefined") {
    return "undefined";
  }

  try {
    const { instance } = await WebAssembly.instantiate(adder);
    return `object, add(2, 3) = ${instance.exports.add(2, 3)}`;
  } catch (error) {
    return `object, refused (${error.name})`;
  }
};

// The worker's address passes through a Trusted Types policy: Worker, SharedWorker and register are script sinks.
let policy;
const workerUrl = () => {
  policy ??= trustedTypes.createPolicy("probe-wasm-worker", { createScriptURL: url => url });
  return policy.createScriptURL(new URL("./wasm-worker.js", import.meta.url).href);
};

const within = (answer, milliseconds = 5_000) =>
  Promise.race([answer, new Promise(resolve => setTimeout(() => resolve("no answer"), milliseconds))]);

const dedicated = () => within(new Promise(resolve => {
  const worker = new Worker(workerUrl(), { type: "module" });
  worker.onmessage = event => {
    resolve(event.data);
    worker.terminate();
  };
  worker.onerror = event => resolve(`error (${event.message || "no message"})`);
}));

const shared = () => {
  if (typeof SharedWorker === "undefined") {
    return Promise.resolve("no SharedWorker");
  }

  return within(new Promise(resolve => {
    try {
      const worker = new SharedWorker(workerUrl(), { type: "module", name: "probe-wasm" });
      worker.port.onmessage = event => resolve(event.data);
      worker.onerror = event => resolve(`error (${event.message || "no message"})`);
      worker.port.start();
    } catch (error) {
      resolve(`threw ${error.name}`);
    }
  }));
};

// A service worker, registered as a classic script, asked once over a message channel, then unregistered.
const service = async () => {
  if (!("serviceWorker" in navigator)) {
    return "no navigator.serviceWorker";
  }

  let registration;
  try {
    registration = await within(navigator.serviceWorker.register(workerUrl()));
  } catch (error) {
    return `register refused (${error.name}: ${error.message})`;
  }

  if (typeof registration === "string") {
    return `register: ${registration}`;
  }

  try {
    const worker = registration.installing ?? registration.waiting ?? registration.active;
    const active = await within(new Promise(resolve => {
      if (worker.state === "activated") {
        resolve("activated");
        return;
      }

      worker.addEventListener("statechange", () => {
        if (worker.state === "activated" || worker.state === "redundant") {
          resolve(worker.state);
        }
      });
    }));
    if (active !== "activated") {
      return `not activated (${active})`;
    }

    const channel = new MessageChannel();
    const answer = new Promise(resolve => { channel.port1.onmessage = event => resolve(event.data); });
    worker.postMessage("check", [channel.port2]);
    return await within(answer);
  } finally {
    await registration.unregister().catch(() => false);
  }
};

export const wasmSteps = reloaded => ({
  async wasm() {
    const results = {
      page: await check(),
      worker: await dedicated(),
      shared: await shared(),
      service: await service(),
    };
    await probe.note(`wasm${reloaded ? " (reloaded)" : ""}: ${JSON.stringify(results)}`).unwrap();
  },
});
