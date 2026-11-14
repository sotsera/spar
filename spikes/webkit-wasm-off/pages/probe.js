// Page probe: WebAssembly paths in the top frame, in workers and in other realms the page can create.
// Reports through the "probe" script message handler. No timers: waits end on an event or when the host
// calls window.__hostTick() (the host calls it every few seconds), so a never-presented view cannot stall it.
"use strict";

(() => {
  const host = window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.probe;
  const params = new URLSearchParams(location.search);
  const startedAt = performance.now();
  const violations = [];
  document.addEventListener("securitypolicyviolation", (e) => {
    violations.push({ directive: e.effectiveDirective, blocked: e.blockedURI, sample: e.sample, disposition: e.disposition });
  });
  // Captured before any page code runs, so later checks can tell what the user script did.
  const initial = {
    typeofWebAssembly: typeof self.WebAssembly,
    descriptor: (() => {
      const d = Object.getOwnPropertyDescriptor(globalThis, "WebAssembly");
      return d ? { writable: d.writable, enumerable: d.enumerable, configurable: d.configurable, valueType: typeof d.value } : null;
    })(),
    hostMarker: typeof window.__sparWasmOff,
    extension: typeof window.__sparExt === "string" ? window.__sparExt : null,
  };

  const ticks = new Set();
  window.__hostTick = () => {
    for (const resolve of ticks) resolve({ event: "host-tick" });
    ticks.clear();
    return "ticked";
  };
  const progress = (step) => {
    if (host) host.postMessage(JSON.stringify({ progress: step }));
  };
  // Resolves on the first of the named events, or on the next host tick after `ticksToWait` ticks.
  const waitEvent = (target, names, ticksToWait = 2) =>
    new Promise((resolve) => {
      let left = ticksToWait;
      const done = (value) => {
        for (const n of names) target.removeEventListener(n, handlers[n]);
        ticks.delete(onTick);
        resolve(value);
      };
      const handlers = {};
      for (const n of names) {
        handlers[n] = (e) => done({ event: n, data: e.data, message: e.message });
        target.addEventListener(n, handlers[n]);
      }
      const onTick = () => {
        left -= 1;
        if (left <= 0) done({ event: "timeout" });
        else ticks.add(onTick);
      };
      ticks.add(onTick);
    });

  const report = {};

  async function workerRun(url, message) {
    let worker;
    try {
      worker = new Worker(url);
    } catch (e) {
      return { created: false, error: describeError(e) };
    }
    const reply = waitEvent(worker, ["message", "error"]);
    worker.postMessage(message || {});
    const got = await reply;
    worker.terminate();
    return { created: true, event: got.event, data: got.data, message: got.message };
  }

  async function frameRealm(kind) {
    const f = document.createElement("iframe");
    let loaded = null;
    if (kind === "srcdoc") f.srcdoc = "<!doctype html><p>srcdoc</p>";
    if (kind === "same-origin") f.src = "frame.html";
    if (kind !== "about-blank") loaded = waitEvent(f, ["load", "error"]);
    document.body.append(f);
    const out = {};
    if (loaded) out.load = (await loaded).event;
    let w = null;
    try {
      w = f.contentWindow;
      out.url = w.location.href;
    } catch (e) {
      out.access = describeError(e);
    }
    if (w) {
      try {
        out.suite = await wasmSuite(w.WebAssembly, undefined);
      } catch (e) {
        out.suite = { error: describeError(e) };
      }
    }
    f.remove();
    return out;
  }

  async function run() {
    progress("start");
    report.initial = initial;
    report.location = location.href;
    report.userAgent = navigator.userAgent;
    report.evalChecks = await evalChecks();
    report.main = await wasmSuite(self.WebAssembly, () => fetch("add.wasm"));
    progress("main");

    await check(report, "esmImportWasm", async () => {
      const m = await import("./add.wasm");
      return typeof m.add === "function" ? m.add(2, 3) : `imported, exports ${Object.keys(m)}`;
    });
    progress("esm");

    report.workerWithCsp = await workerRun("worker.js?csp=1");
    report.workerWithoutCsp = await workerRun("worker.js?csp=0");
    progress("workers");

    // A Module compiled in the page (compile is unchecked by CSP in some builds) posted to workers.
    let module = null;
    try {
      module = await WebAssembly.compile(ADD_WASM);
    } catch (e) {
      report.postedModule = { compileInPage: describeError(e) };
    }
    if (module) {
      report.postedModule = {
        toWorkerWithCsp: await workerRun("worker.js?csp=1", { module }),
        toWorkerWithoutCsp: await workerRun("worker.js?csp=0", { module }),
      };
    }
    progress("posted");

    await check(report, "blobWorker", async () => {
      const url = URL.createObjectURL(new Blob(["postMessage(typeof WebAssembly)"], { type: "text/javascript" }));
      return workerRun(url);
    });

    report.frames = {
      aboutBlank: await frameRealm("about-blank"),
      srcdoc: await frameRealm("srcdoc"),
      sameOrigin: await frameRealm("same-origin"),
    };
    progress("frames");

    // Ways page code might try to get a removed global back.
    const recovery = {};
    await check(recovery, "inGlobalThis", () => "WebAssembly" in globalThis);
    await check(recovery, "reflectGet", () => typeof Reflect.get(globalThis, "WebAssembly"));
    await check(recovery, "windowPrototypeChain", () => {
      const found = [];
      for (let p = Object.getPrototypeOf(globalThis); p; p = Object.getPrototypeOf(p)) {
        if (Object.getOwnPropertyDescriptor(p, "WebAssembly")) found.push(p.constructor && p.constructor.name);
      }
      return found;
    });
    await check(recovery, "redefine", () => {
      Object.defineProperty(globalThis, "WebAssembly", { value: { probe: 1 }, configurable: true, writable: true });
      return typeof globalThis.WebAssembly.probe;
    });
    await check(recovery, "windowOpen", () => {
      const w = window.open("about:blank");
      return w ? typeof w.WebAssembly : "null";
    });
    report.recovery = recovery;

    report.jsWork = [jsWork(), jsWork(), jsWork()];
    const nav = performance.getEntriesByType("navigation")[0];
    report.timing = {
      probeStartMs: Math.round(startedAt),
      domContentLoadedMs: nav ? Math.round(nav.domContentLoadedEventEnd || nav.domInteractive) : null,
      probeTotalMs: Math.round(performance.now() - startedAt),
    };
    report.violations = violations.slice();
    report.query = Object.fromEntries(params);
  }

  document.addEventListener("DOMContentLoaded", () => run()
    .catch((e) => {
      report.fatal = describeError(e);
    })
    .finally(() => {
      if (host) host.postMessage(JSON.stringify({ report }));
    }));
})();
