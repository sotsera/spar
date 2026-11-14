// Fills the origin's storage with incompressible data, then reports what it wrote and navigator.storage.estimate().
// Query: ls (KB of localStorage), idb (MB of IndexedDB, 1 MB ArrayBuffers), cache (MB of Cache Storage),
// http (number of cacheable fetches from the server's /blob) and size (KB per fetch).
"use strict";
const query = new URLSearchParams(location.search);
const number = (name) => Number(query.get(name) || 0);

function sparPost(text) {
  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.postMessage(text);
  } else if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.spar) {
    window.webkit.messageHandlers.spar.postMessage(text);
  }
}

function randomBytes(count) {
  const bytes = new Uint8Array(count);
  for (let offset = 0; offset < count; offset += 65536) {
    crypto.getRandomValues(bytes.subarray(offset, Math.min(count, offset + 65536)));
  }
  return bytes;
}

function randomText(characters) {
  // Base64 of random bytes: incompressible enough for LevelDB's and SQLite's storage.
  const bytes = randomBytes(Math.ceil(characters * 3 / 4));
  let text = "";
  for (let offset = 0; offset < bytes.length; offset += 8192) {
    text += btoa(String.fromCharCode.apply(null, bytes.subarray(offset, offset + 8192)));
  }
  return text.slice(0, characters);
}

function request(r) {
  return new Promise((resolve, reject) => { r.onsuccess = () => resolve(r.result); r.onerror = () => reject(r.error); });
}

async function fill() {
  const report = { kind: "filled", origin: location.origin, steps: {} };
  const started = performance.now();
  const lsKb = number("ls");
  if (lsKb > 0) {
    // 64 keys of equal size; localStorage counts UTF-16 characters against a quota of about 5 M.
    const per = Math.floor(lsKb * 1024 / 64);
    for (let i = 0; i < 64; i++) localStorage.setItem("fill" + i, randomText(per));
    report.steps.localStorageChars = per * 64;
  }
  const idbMb = number("idb");
  if (idbMb > 0) {
    const db = await new Promise((resolve, reject) => {
      const open = indexedDB.open("fill", 1);
      open.onupgradeneeded = () => open.result.createObjectStore("blobs");
      open.onsuccess = () => resolve(open.result);
      open.onerror = () => reject(open.error);
    });
    for (let i = 0; i < idbMb; i++) {
      const tx = db.transaction("blobs", "readwrite");
      tx.objectStore("blobs").put(randomBytes(1048576).buffer, "b" + i);
      await new Promise((resolve, reject) => { tx.oncomplete = resolve; tx.onerror = () => reject(tx.error); });
    }
    report.steps.indexedDbBytes = idbMb * 1048576;
    db.close();
  }
  const cacheMb = number("cache");
  if (cacheMb > 0 && self.caches) {
    try {
      const cache = await caches.open("fill");
      for (let i = 0; i < cacheMb; i++) {
        await cache.put(new Request("/cache-storage/" + i), new Response(randomBytes(1048576), { headers: { "Content-Type": "application/octet-stream" } }));
      }
      report.steps.cacheStorageBytes = cacheMb * 1048576;
    } catch (error) {
      report.steps.cacheStorageError = String(error);
    }
  }
  const http = number("http");
  const sizeKb = number("size") || 100;
  if (http > 0) {
    let bytes = 0;
    const seed = Math.random().toString(36).slice(2);
    for (let i = 0; i < http; i++) {
      const response = await fetch(`/blob?i=${i}&seed=${seed}&size=${sizeKb}`);
      bytes += (await response.arrayBuffer()).byteLength;
    }
    report.steps.httpBytes = bytes;
  }
  report.fillMs = Math.round(performance.now() - started);
  if (navigator.storage && navigator.storage.estimate) {
    const estimate = await navigator.storage.estimate();
    report.estimate = { usage: estimate.usage, quota: estimate.quota, usageDetails: estimate.usageDetails || null };
  }
  document.getElementById("state").textContent = "Done";
  return report;
}

requestAnimationFrame(() => requestAnimationFrame(() => {
  fill().then((report) => sparPost(JSON.stringify(report)), (error) => sparPost(JSON.stringify({ kind: "error", error: String(error) })));
}));
