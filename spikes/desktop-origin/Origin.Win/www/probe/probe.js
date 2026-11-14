// Desktop-origin probe. The mode comes from ?mode=; every result goes to the host as POST /_spar/result, and
// /_spar/done ends the run. Each test records wall-clock (epoch) start and end so the loopback listener's log can be
// matched to it.
const params = new URLSearchParams(location.search);
const mode = params.get("mode") ?? "leaks";
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const timeout = (ms, value) => new Promise((resolve) => setTimeout(() => resolve(value), ms));
const errorText = (error) => `${error?.name ?? "Error"}: ${error?.message ?? String(error)}`;

async function report(test, outcome, extra = {}) {
  await fetch("/_spar/result", { method: "POST", body: JSON.stringify({ mode, origin: location.origin, test, outcome, ...extra }) });
}

async function run(test, action, settle = 2000) {
  const start = Date.now();
  let outcome;
  try {
    outcome = await Promise.race([action(), timeout(settle + 6000, "TIMEOUT (still pending)")]);
  } catch (error) {
    outcome = `threw ${errorText(error)}`;
  }

  await report(test, outcome, { start, end: Date.now() });
  await sleep(400);
}

async function fetchOutcome(url, init) {
  try {
    const response = await fetch(url, { cache: "no-store", ...init });
    return `status ${response.status} type ${response.type}`;
  } catch (error) {
    return `fetch failed ${errorText(error)}`;
  }
}

function imageOutcome(url) {
  return new Promise((resolve) => {
    const image = new Image();
    image.onload = () => resolve("load");
    image.onerror = () => resolve("error");
    image.src = url;
  });
}

function frameOutcome(url) {
  return new Promise((resolve) => {
    const frame = document.createElement("iframe");
    frame.onload = () => {
      let detail;
      try {
        detail = `same-origin document, url ${frame.contentWindow.location.href}, body "${frame.contentDocument.body?.textContent?.slice(0, 40)}"`;
      } catch (error) {
        detail = `not readable (${errorText(error)}): an error page`;
      }

      frame.remove();
      resolve(`load, ${detail}`);
    };
    frame.src = url;
    document.body.append(frame);
  });
}

async function beaconOutcome(url) {
  const queued = navigator.sendBeacon(url, "beacon");
  await sleep(1500);
  return `queued=${queued}`;
}

function eventSourceOutcome(url) {
  return new Promise((resolve) => {
    const events = [];
    const source = new EventSource(url);
    source.onopen = () => events.push("open");
    source.onmessage = (event) => events.push(`message ${event.data}`);
    source.onerror = () => events.push(`error readyState=${source.readyState}`);
    setTimeout(() => {
      source.close();
      resolve(events.join(", ") || "no events");
    }, 2000);
  });
}

function webSocketOutcome(url) {
  return new Promise((resolve) => {
    const events = [];
    let socket;
    try {
      socket = new WebSocket(url);
    } catch (error) {
      resolve(`constructor threw ${errorText(error)}`);
      return;
    }

    socket.onopen = () => events.push("open");
    socket.onerror = () => events.push("error");
    socket.onclose = (event) => {
      events.push(`close code=${event.code}`);
      resolve(events.join(", "));
    };
    setTimeout(() => {
      socket.close();
      resolve(`${events.join(", ")} (no close after 3 s, readyState=${socket.readyState})`);
    }, 3000);
  });
}

function dedicatedWorkerOutcome() {
  return new Promise((resolve) => {
    const worker = new Worker("/probe/worker.js");
    worker.onmessage = (event) => {
      worker.terminate();
      resolve(event.data);
    };
    worker.onerror = (event) => resolve(`worker error ${event.message}`);
    worker.postMessage("go");
  });
}

function sharedWorkerOutcome() {
  return new Promise((resolve) => {
    const worker = new SharedWorker("/probe/shared-worker.js");
    worker.port.onmessage = (event) => resolve(event.data);
    worker.onerror = (event) => resolve(`shared worker error ${event.message ?? "(no message)"}`);
    worker.port.start();
    worker.port.postMessage("go");
  });
}

async function serviceWorkerOutcome() {
  for (const old of await navigator.serviceWorker.getRegistrations()) {
    await old.unregister();
  }

  let registration;
  try {
    registration = await navigator.serviceWorker.register("/probe/sw.js", { scope: "/probe/" });
  } catch (error) {
    return { registration: `failed ${errorText(error)}` };
  }

  const worker = registration.installing ?? registration.waiting ?? registration.active;
  if (worker.state !== "activated") {
    await new Promise((resolve) => worker.addEventListener("statechange", () => worker.state === "activated" && resolve()));
  }

  if (!navigator.serviceWorker.controller) {
    await new Promise((resolve) => navigator.serviceWorker.addEventListener("controllerchange", resolve, { once: true }));
  }

  const fromWorker = await new Promise((resolve) => {
    navigator.serviceWorker.addEventListener("message", (event) => resolve(event.data), { once: true });
    navigator.serviceWorker.controller.postMessage("go");
  });
  const viaFetchEvent = await fetchOutcome("/probe/via-sw/item");
  await registration.unregister();
  return { registration: "ok", fromWorker, viaFetchEvent };
}

async function leaks() {
  const host = location.host;
  await run("missing path (host answers 404)", () => fetchOutcome("/missing/file.js"));
  await run("fetch, unanswered", () => fetchOutcome("/unanswered/fetch"));
  await run("img, unanswered", () => imageOutcome("/unanswered/img.png"));
  await run("iframe, unanswered", () => frameOutcome("/unanswered/frame.html"));
  await run("iframe, answered", () => frameOutcome("/probe/frame.html"));
  await run("sendBeacon, answered path", () => beaconOutcome("/beacon/answered"));
  await run("sendBeacon, unanswered", () => beaconOutcome("/unanswered/beacon"));
  await run("EventSource, answered", () => eventSourceOutcome("/sse"));
  await run("EventSource, unanswered", () => eventSourceOutcome("/unanswered/sse"));
  await run(`WebSocket wss://${host}/x`, () => webSocketOutcome(`wss://${host}/x`), 3000);
  await run(`WebSocket ws://${host}/x`, () => webSocketOutcome(`ws://${host}/x`), 3000);
  await run(`fetch http://${host}/unanswered/plain-http (outside the https filter)`, () => fetchOutcome(`http://${host}/unanswered/plain-http`));
  await run("fetch https://other.localhost/x (another .localhost name)", () => fetchOutcome(`https://other.localhost/x`));
  await run("dedicated worker fetches", dedicatedWorkerOutcome);
  await run("shared worker fetches", sharedWorkerOutcome);
  await run("service worker", serviceWorkerOutcome, 4000);
}

async function idbOpen() {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open("spar-origin", 1);
    request.onupgradeneeded = () => request.result.createObjectStore("kv");
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

async function idbPut(key, value) {
  const db = await idbOpen();
  await new Promise((resolve, reject) => {
    const tx = db.transaction("kv", "readwrite");
    tx.objectStore("kv").put(value, key);
    tx.oncomplete = resolve;
    tx.onerror = () => reject(tx.error);
  });
  db.close();
}

async function idbGet(key) {
  const databases = (await indexedDB.databases()).map((db) => db.name);
  if (!databases.includes("spar-origin")) {
    return `(no database; databases=${JSON.stringify(databases)})`;
  }

  const db = await idbOpen();
  const value = await new Promise((resolve, reject) => {
    const request = db.transaction("kv").objectStore("kv").get(key);
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
  db.close();
  return value ?? "(missing)";
}

async function permissionStates() {
  const states = {};
  for (const name of ["notifications", "window-management", "clipboard-read", "clipboard-write", "persistent-storage"]) {
    try {
      states[name] = (await navigator.permissions.query({ name })).state;
    } catch (error) {
      states[name] = `query threw ${errorText(error)}`;
    }
  }

  states["Notification.permission"] = typeof Notification === "undefined" ? "(no Notification)" : Notification.permission;
  return states;
}

async function storeWrite() {
  const stamp = params.get("stamp") ?? String(Date.now());
  localStorage.setItem("spar-origin", stamp);
  await idbPut("stamp", stamp);
  document.cookie = `__Host-spar=${stamp}; Secure; Path=/; SameSite=Strict; Max-Age=31536000`;
  document.cookie = `plain-spar=${stamp}; Path=/; Max-Age=31536000`;
  const before = await permissionStates();
  let notification;
  try {
    notification = await Notification.requestPermission();
  } catch (error) {
    notification = `threw ${errorText(error)}`;
  }

  let screenDetails;
  try {
    screenDetails = `screens=${(await window.getScreenDetails()).screens.length}`;
  } catch (error) {
    screenDetails = `threw ${errorText(error)}`;
  }

  await report("write", "written", { stamp, cookie: document.cookie, before, notification, screenDetails, after: await permissionStates() });
  // A request after writing shows whether the host receives the cookies.
  await fetch("/probe/data.json?after-write", { cache: "no-store" });
}

async function storeRead() {
  await report("read", "read", {
    localStorage: localStorage.getItem("spar-origin"),
    indexedDB: await idbGet("stamp"),
    cookie: document.cookie,
    permissions: await permissionStates(),
  });
  await fetch("/probe/data.json?after-read", { cache: "no-store" });
}

async function secure() {
  const result = { isSecureContext, crossOriginIsolated, userAgent: navigator.userAgent };
  try {
    const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode("spar"));
    result.subtleDigest = [...new Uint8Array(digest)].slice(0, 4).map((b) => b.toString(16).padStart(2, "0")).join("") + "...";
    const key = await crypto.subtle.generateKey({ name: "AES-GCM", length: 256 }, false, ["encrypt"]);
    result.subtleGenerateKey = key.type;
  } catch (error) {
    result.subtle = `threw ${errorText(error)}`;
  }

  result.randomUUID = typeof crypto.randomUUID === "function" ? "available" : "missing";
  result.clipboardApi = navigator.clipboard ? Object.getOwnPropertyNames(Object.getPrototypeOf(navigator.clipboard)).join(",") : "missing";
  result.permissions = await permissionStates();
  result.hasFocus = document.hasFocus();
  // readText goes through the host's permission event, which denies it; nothing is read from the user's clipboard.
  // writeText is not called: it would replace the user's clipboard.
  try {
    result.clipboardReadText = `resolved (length ${(await navigator.clipboard.readText()).length})`;
  } catch (error) {
    result.clipboardReadText = `rejected ${errorText(error)}`;
  }

  result.storagePersistedBefore = await navigator.storage.persisted();
  try {
    result.storagePersist = await navigator.storage.persist();
  } catch (error) {
    result.storagePersist = `threw ${errorText(error)}`;
  }

  result.storagePersistedAfter = await navigator.storage.persisted();
  const estimate = await navigator.storage.estimate();
  result.storageEstimate = { usage: estimate.usage, quotaGB: Math.round(estimate.quota / 1e9) };
  result.storageGetDirectory = typeof navigator.storage.getDirectory === "function" ? "available" : "missing";
  try {
    const root = await navigator.storage.getDirectory();
    result.opfs = `ok (${root.kind})`;
  } catch (error) {
    result.opfs = `threw ${errorText(error)}`;
  }

  result.serviceWorker = "serviceWorker" in navigator ? "available" : "missing";
  result.webLocks = navigator.locks ? "available" : "missing";
  await report("secure-context", "checked", result);
}

async function lna() {
  const port = params.get("port");
  const lan = params.get("lan");
  const states = {};
  for (const name of ["local-network-access", "local-network", "loopback-network"]) {
    try {
      states[name] = (await navigator.permissions.query({ name })).state;
    } catch (error) {
      states[name] = `query threw ${errorText(error)}`;
    }
  }

  await report("permission names", "queried", { states });
  await run(`fetch http://127.0.0.1:${port}/lna`, () => fetchOutcome(`http://127.0.0.1:${port}/lna?from=${location.host}`), 8000);
  await run(`fetch http://127.0.0.1:${port}/lna targetAddressSpace=loopback`,
    () => fetchOutcome(`http://127.0.0.1:${port}/lna?from=${location.host}&tas=loopback`, { targetAddressSpace: "loopback" }), 8000);
  await run(`fetch http://localhost:${port}/lna`, () => fetchOutcome(`http://localhost:${port}/lna?from=${location.host}&name=localhost`), 8000);
  await run(`fetch http://${lan}:${port}/lna`, () => fetchOutcome(`http://${lan}:${port}/lna?from=${location.host}`), 8000);
  await run(`fetch http://${lan}:${port}/lna targetAddressSpace=local`,
    () => fetchOutcome(`http://${lan}:${port}/lna?from=${location.host}&tas=local`, { targetAddressSpace: "local" }), 8000);
  await run(`fetch http://${lan}:${port}/lna no-cors`, () => fetchOutcome(`http://${lan}:${port}/lna?from=${location.host}&mode=no-cors`, { mode: "no-cors" }), 8000);
  await run(`img http://127.0.0.1:${port}/lna.png`, () => imageOutcome(`http://127.0.0.1:${port}/lna.png?from=${location.host}`), 8000);
  const after = {};
  for (const name of ["local-network-access", "local-network", "loopback-network"]) {
    try {
      after[name] = (await navigator.permissions.query({ name })).state;
    } catch (error) {
      after[name] = `query threw ${errorText(error)}`;
    }
  }

  await report("permission names after", "queried", { states: after });
}

const modes = { leaks, "store-write": storeWrite, "store-read": storeRead, secure, lna };
try {
  await modes[mode]();
} catch (error) {
  await report("probe failure", errorText(error));
}

await fetch("/_spar/done");
