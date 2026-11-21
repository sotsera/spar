// Dedicated worker served from spar://app with the CSP header: the same connect-src probes from a worker.
"use strict";
importScripts(self.trustedTypes ? self.trustedTypes.createPolicy("w", { createScriptURL: function (s) { return s; } }).createScriptURL("probes.js") : "probes.js");

self.onmessage = async function (m) {
  var ports = m.data;
  var lo = "127.0.0.1:";
  var results = await sparRun([
    ["worker fetch relative data.json", function () { return sparFetch("data.json"); }],
    ["worker fetch absolute spar://app/data.json", function () { return sparFetch("spar://app/data.json"); }],
    ["worker xhr relative data.json", function () { return sparXhr("data.json"); }],
    ["worker fetch http://127.0.0.1", function () { return sparFetch("http://" + lo + ports.wfetch + "/"); }],
    ["worker xhr http://127.0.0.1", function () { return sparXhr("http://" + lo + ports.wxhr + "/"); }],
    ["worker websocket ws://127.0.0.1", function () { return sparSocket("ws://" + lo + ports.wws + "/"); }],
  ]);
  self.postMessage({ origin: self.origin, isSecureContext: self.isSecureContext, results: results, all: sparViolations });
};
