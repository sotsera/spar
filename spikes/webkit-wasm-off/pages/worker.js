// Dedicated worker: runs the shared suite in the worker realm, and instantiates a Module the page sends.
"use strict";
importScripts("suite.js");

const violations = [];
self.addEventListener("securitypolicyviolation", (e) => {
  violations.push({ directive: e.effectiveDirective, blocked: e.blockedURI, sample: e.sample });
});

self.onmessage = async (event) => {
  const out = {};
  if (event.data && event.data.module) {
    await check(out, "instantiatePostedModule", async () => (await WebAssembly.instantiate(event.data.module)).exports.add(2, 3));
    await check(out, "instanceSyncPostedModule", () => new WebAssembly.Instance(event.data.module).exports.add(2, 3));
  } else {
    out.suite = await wasmSuite(self.WebAssembly, () => fetch("add.wasm"));
    out.evalChecks = await evalChecks();
  }
  out.violations = violations.slice();
  postMessage(out);
};
