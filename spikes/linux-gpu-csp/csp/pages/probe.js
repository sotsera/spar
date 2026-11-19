// The page's probes (spar://app/index.html). Listener ports come from <meta name="ports">.
"use strict";

(function () {
  var ports = {};
  document.querySelector('meta[name="ports"]').content.split("&").forEach(function (kv) {
    var p = kv.split("=");
    ports[p[0]] = p[1];
  });
  var lo = "127.0.0.1:";
  var list = [
    ["fetch relative data.json", function () { return sparFetch("data.json"); }],
    ["fetch absolute spar://app/data.json", function () { return sparFetch("spar://app/data.json"); }],
    ["fetch spar://other/data.json", function () { return sparFetch("spar://other/data.json"); }],
    ["xhr relative data.json", function () { return sparXhr("data.json"); }],
    ["xhr absolute spar://app/data.json", function () { return sparXhr("spar://app/data.json"); }],
    ["websocket ws://127.0.0.1", function () { return sparSocket("ws://" + lo + ports.ws + "/"); }],
    ["websocket wss://127.0.0.1", function () { return sparSocket("wss://" + lo + ports.wss + "/"); }],
    ["websocket ws://localhost", function () { return sparSocket("ws://localhost:" + ports.wsLocalhost + "/"); }],
    ["websocket ws://app", function () { return sparSocket("ws://app/"); }],
    ["websocket wss://app", function () { return sparSocket("wss://app/"); }],
    // These reach a listener only where "app" resolves to 127.0.0.1 (the containers run with --add-host app:127.0.0.1).
    ["websocket ws://app:port", function () { return sparSocket("ws://app:" + ports.wsApp + "/"); }],
    ["websocket wss://app:port", function () { return sparSocket("wss://app:" + ports.wssApp + "/"); }],
    ["fetch https://app:port", function () { return sparFetch("https://app:" + ports.httpsApp + "/"); }],
    ["fetch http://127.0.0.1", function () { return sparFetch("http://" + lo + ports.fetch + "/"); }],
    ["fetch http://localhost", function () { return sparFetch("http://localhost:" + ports.fetchLocalhost + "/"); }],
    ["xhr http://127.0.0.1", function () { return sparXhr("http://" + lo + ports.xhr + "/"); }],
    ["eventsource spar://app/events", function () { return sparEventSource("spar://app/events"); }],
    ["eventsource relative events", function () { return sparEventSource("events"); }],
    ["eventsource http://127.0.0.1", function () { return sparEventSource("http://" + lo + ports.es + "/"); }],
    ["sendBeacon spar://app/beacon", function () { return sparBeacon("spar://app/beacon"); }],
    ["sendBeacon relative beacon", function () { return sparBeacon("beacon"); }],
    ["sendBeacon http://127.0.0.1", function () { return sparBeacon("http://" + lo + ports.beacon + "/"); }],
  ];

  function runWorker() {
    return new Promise(function (resolve) {
      var w;
      try {
        w = new Worker(sparScriptUrl("worker.js"));
      } catch (e) {
        resolve({ error: sparErr(e) });
        return;
      }
      w.onmessage = function (m) { resolve(m.data); };
      w.onerror = function (e) { resolve({ error: "worker error event: " + (e.message || "") }); };
      w.postMessage(ports);
    });
  }

  async function main() {
    var info = {
      origin: self.origin, isSecureContext: self.isSecureContext, trustedTypes: !!self.trustedTypes,
      userAgent: navigator.userAgent,
    };
    var page = await sparRun(list);
    var worker = await sparTimeout(runWorker(), 20000);
    await sparDelay(500);
    window.webkit.messageHandlers.probe.postMessage(JSON.stringify({
      type: "done", info: info, page: page, worker: worker, allPageViolations: sparViolations,
    }));
  }

  main();
})();
