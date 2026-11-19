// Shared by the page and the dedicated worker: each probe settles to a short outcome string, and the CSP violation
// events seen while it ran are attached to it. No probe relies on a timer except the settle wait and the timeout.
"use strict";

var sparViolations = [];
self.addEventListener("securitypolicyviolation", function (e) {
  sparViolations.push({
    blockedURI: e.blockedURI,
    violatedDirective: e.violatedDirective,
    effectiveDirective: e.effectiveDirective,
    disposition: e.disposition,
    statusCode: e.statusCode,
    sourceFile: e.sourceFile ? e.sourceFile.replace(/^.*\//, "") : e.sourceFile,
    lineNumber: e.lineNumber,
  });
});

var sparTT = self.trustedTypes ? self.trustedTypes.createPolicy("probe", { createScriptURL: function (s) { return s; } }) : null;
function sparScriptUrl(s) { return sparTT ? sparTT.createScriptURL(s) : s; }

function sparDelay(ms) { return new Promise(function (r) { setTimeout(r, ms); }); }

function sparTimeout(promise, ms) {
  return Promise.race([promise, sparDelay(ms).then(function () { return "timeout " + ms + " ms"; })]);
}

function sparErr(e) { return "threw " + (e && e.name) + ": " + (e && e.message); }

function sparFetch(url) {
  return fetch(url, { cache: "no-store" }).then(
    function (r) { return r.text().then(function (t) { return "ok " + r.status + " " + t.slice(0, 40).trim(); }); },
    function (e) { return "rejected " + e.name + ": " + e.message; });
}

function sparXhr(url) {
  return new Promise(function (resolve) {
    var x = new XMLHttpRequest();
    x.onload = function () { resolve("load " + x.status + " " + String(x.responseText).slice(0, 40).trim()); };
    x.onerror = function () { resolve("error event, status " + x.status); };
    try {
      x.open("GET", url);
      x.send();
    } catch (e) {
      resolve(sparErr(e));
    }
  });
}

function sparSocket(url) {
  return new Promise(function (resolve) {
    var ws;
    try {
      ws = new WebSocket(url);
    } catch (e) {
      resolve(sparErr(e));
      return;
    }
    ws.onopen = function () { resolve("open"); ws.close(); };
    ws.onerror = function () { resolve("error event, readyState " + ws.readyState); };
    ws.onclose = function (e) { resolve("close " + e.code); };
  });
}

function sparEventSource(url) {
  return new Promise(function (resolve) {
    var es;
    try {
      es = new EventSource(url);
    } catch (e) {
      resolve(sparErr(e));
      return;
    }
    es.onmessage = function (m) { resolve("message " + m.data); es.close(); };
    es.onerror = function () { resolve("error event, readyState " + es.readyState); es.close(); };
  });
}

function sparBeacon(url) {
  try {
    return Promise.resolve("returned " + navigator.sendBeacon(url, "beacon-body"));
  } catch (e) {
    return Promise.resolve(sparErr(e));
  }
}

async function sparRun(list) {
  var results = [];
  for (var i = 0; i < list.length; i++) {
    var name = list[i][0], fn = list[i][1];
    var before = sparViolations.length;
    var outcome;
    try {
      outcome = await sparTimeout(fn(), 4000);
    } catch (e) {
      outcome = sparErr(e);
    }
    await sparDelay(250);
    results.push({ name: name, outcome: outcome, violations: sparViolations.slice(before) });
  }
  return results;
}
