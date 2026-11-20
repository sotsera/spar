// A module worker, dedicated or shared, that reports whether WebAssembly runs in it. The host serves it with the
// page's policy, which refuses WebAssembly unless the app calls AllowWebAssembly(). Self-contained: Vite copies a file
// reached through new URL(…) as it is, so it imports nothing.
const module = new Uint8Array([0, 97, 115, 109, 1, 0, 0, 0]);

const check = async () => {
  if (typeof WebAssembly === "undefined") {
    return "absent";
  }

  try {
    await WebAssembly.instantiate(module);
    return "instantiated";
  } catch (error) {
    return `refused (${error.name})`;
  }
};

if (typeof SharedWorkerGlobalScope !== "undefined" && self instanceof SharedWorkerGlobalScope) {
  self.onconnect = event => check().then(result => event.ports[0].postMessage(result));
} else {
  check().then(result => self.postMessage(result));
}
