// A module worker served by the host: its own response carries the policy, so eval and WebAssembly are blocked here
// as they are in the page (plan section 3b, "The CSP goes on every host-served response, worker scripts included").
const results = {};
try {
  results.eval = String(eval("1 + 1"));
} catch (error) {
  results.eval = `blocked (${error.name})`;
}

try {
  results.function = String(new Function("return 2")());
} catch (error) {
  results.function = `blocked (${error.name})`;
}

// The smallest valid module: the magic number and version.
const module = new Uint8Array([0, 97, 115, 109, 1, 0, 0, 0]);
WebAssembly.instantiate(module).then(
  () => {
    results.webassembly = "instantiated";
    postMessage(results);
  },
  error => {
    results.webassembly = `blocked (${error.name})`;
    postMessage(results);
  });
