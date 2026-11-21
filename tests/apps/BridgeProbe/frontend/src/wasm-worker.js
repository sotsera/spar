// A worker, dedicated, shared or service, that reports whether WebAssembly exists in it and whether a module runs
// (M1c step 5). The host serves it with the page's policy. Self-contained: Vite copies a file reached through
// new URL(…) as it is, so it imports nothing, and it runs as a classic or a module script.
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

if (typeof ServiceWorkerGlobalScope !== "undefined" && self instanceof ServiceWorkerGlobalScope) {
  self.addEventListener("message", event => check().then(result => event.ports[0].postMessage(result)));
} else if (typeof SharedWorkerGlobalScope !== "undefined" && self instanceof SharedWorkerGlobalScope) {
  self.onconnect = event => check().then(result => event.ports[0].postMessage(result));
} else {
  check().then(result => self.postMessage(result));
}
