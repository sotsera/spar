// Probe page for the profile spike. The query string selects the operation:
//   op=read        report what this origin has stored (localStorage, cookie, IndexedDB, Cache Storage, HTTP cache,
//                  service workers, the geolocation permission state)
//   op=readwrite   read, then write token=<t> everywhere; bulk=<kB> adds that much IndexedDB, Cache Storage and
//                  localStorage data; unload=1 adds beforeunload/pagehide/visibilitychange writers; sw=1 registers a
//                  service worker first; stream=1 keeps writing IndexedDB records after the report
// Every operation ends with one POST to /_spar/result. Nothing here touches devices, notifications or the network.

const query = new URLSearchParams(location.search);
const op = query.get('op') ?? 'read';
const token = query.get('token') ?? '';
const bulkKb = Number(query.get('bulk') ?? '0');
const started = performance.now();

function openDb() {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open('spike', 1);
    request.onupgradeneeded = () => {
      const db = request.result;
      db.createObjectStore('kv');
      db.createObjectStore('bulk');
      db.createObjectStore('stream');
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

function done(tx) {
  return new Promise((resolve, reject) => {
    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(tx.error);
    tx.onabort = () => reject(tx.error);
  });
}

function requestValue(request) {
  return new Promise((resolve, reject) => {
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

function randomText(chars) {
  const bytes = new Uint8Array(chars / 2);
  for (let offset = 0; offset < bytes.length; offset += 65536) {
    crypto.getRandomValues(bytes.subarray(offset, offset + 65536));
  }
  return Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
}

async function read() {
  const state = {};
  state.ls = localStorage.getItem('token');
  state.lsUnload = Object.keys(localStorage).filter((k) => k.startsWith('unload-')).map((k) => `${k}=${localStorage.getItem(k)}`);
  state.lsBulk = (localStorage.getItem('bulk') ?? '').length;
  state.cookie = document.cookie;
  try {
    const db = await openDb();
    const tx = db.transaction(['kv', 'bulk', 'stream'], 'readonly');
    state.idb = (await requestValue(tx.objectStore('kv').get('token'))) ?? null;
    state.idbBulk = await requestValue(tx.objectStore('bulk').count());
    const cursor = await requestValue(tx.objectStore('stream').openCursor(null, 'prev'));
    state.streamMax = cursor ? cursor.key : null;
    state.streamToken = cursor ? cursor.value : null;
    db.close();
  } catch (error) {
    state.idbError = String(error);
  }
  try {
    const cache = await caches.open('spike');
    const hit = await cache.match('/cached/token');
    state.cacheStorage = hit ? await hit.text() : null;
    state.cacheStorageEntries = (await cache.keys()).length;
  } catch (error) {
    state.cacheError = String(error);
  }
  try {
    // The host stamps X-Serial with its own run id: an older run id means the HTTP disk cache answered.
    const response = await fetch('/cacheable.txt');
    state.httpSerial = response.headers.get('X-Serial');
  } catch (error) {
    state.httpError = String(error);
  }
  try {
    state.serviceWorkers = navigator.serviceWorker ? (await navigator.serviceWorker.getRegistrations()).length : 'unavailable';
  } catch (error) {
    state.serviceWorkers = String(error);
  }
  try {
    state.geolocation = (await navigator.permissions.query({ name: 'geolocation' })).state;
  } catch (error) {
    state.geolocation = String(error);
  }
  return state;
}

async function write() {
  if (query.get('sw') === '1') {
    await navigator.serviceWorker.register('/sw.js', { scope: '/' });
    await navigator.serviceWorker.ready;
  }
  if (query.get('unload') === '1') {
    for (const name of ['beforeunload', 'pagehide', 'visibilitychange', 'unload']) {
      addEventListener(name, () => localStorage.setItem(`unload-${name}`, token));
    }
  }
  localStorage.setItem('token', token);
  document.cookie = `token=${token}; Max-Age=86400; Path=/; Secure; SameSite=Strict`;
  const db = await openDb();
  const tx = db.transaction(['kv', 'bulk'], 'readwrite');
  tx.objectStore('kv').put(token, 'token');
  if (bulkKb > 0) {
    for (let i = 0; i < bulkKb; i++) {
      tx.objectStore('bulk').put(randomText(1024), `${token}-${i}`);
    }
  }
  await done(tx);
  let cacheWriteError;
  try {
    // Cache Storage accepts only http(s) requests, so this fails on WebKitGTK's custom scheme.
    const cache = await caches.open('spike');
    await cache.put('/cached/token', new Response(token));
    if (bulkKb > 0) {
      const blob = randomText(Math.min(bulkKb, 2048) * 1024 / 4);
      for (let i = 0; i < 4; i++) {
        await cache.put(`/cached/bulk-${i}`, new Response(blob));
      }
    }
  } catch (error) {
    cacheWriteError = String(error);
  }
  if (bulkKb > 0) {
    localStorage.setItem('bulk', randomText(Math.min(bulkKb, 1024) * 1024));
  }
  // Primes the HTTP disk cache (the host sends Cache-Control: max-age).
  const response = await fetch('/cacheable.txt');
  const serial = response.headers.get('X-Serial');
  db.close();
  return { wrote: token, httpSerial: serial, cacheWriteError };
}

async function stream() {
  // One record per transaction, back to back, until the page dies. The report says what was committed by then.
  const db = await openDb();
  let seq = 0;
  let committed = -1;
  let reported = false;
  return new Promise((resolve) => {
    const next = () => {
      const current = seq++;
      const tx = db.transaction('stream', 'readwrite');
      tx.objectStore('stream').put(token, current);
      tx.oncomplete = () => {
        committed = current;
        if (!reported && performance.now() - begin > 300) {
          reported = true;
          resolve({ streamCommitted: committed });
        }
        next();
      };
    };
    const begin = performance.now();
    next();
  });
}

async function report(result) {
  result.op = op;
  result.ms = Math.round(performance.now() - started);
  const body = JSON.stringify(result);
  // WebView2: the host answers POST /_spar/result. WebKitGTK: the script message handler 'spar'.
  if (window.webkit?.messageHandlers?.spar) {
    window.webkit.messageHandlers.spar.postMessage(body);
  } else {
    await fetch('/_spar/result', { method: 'POST', body });
  }
}

try {
  const result = {};
  result.before = await read();
  if (op === 'readwrite') {
    Object.assign(result, await write());
    if (query.get('stream') === '1') {
      Object.assign(result, await stream());
      result.lsAtReport = localStorage.getItem('token');
    }
  }
  await report(result);
} catch (error) {
  await report({ error: String(error) + ' @ ' + String(error && error.stack) });
}
