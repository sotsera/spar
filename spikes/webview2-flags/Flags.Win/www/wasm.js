// Runs the probe in the page, a dedicated worker, a shared worker, a service worker and a same-origin frame.
const within = (promise, ms, label) =>
  Promise.race([promise, new Promise(resolve => setTimeout(() => resolve({ error: label + ': timeout' }), ms))])
    .catch(e => ({ error: label + ': ' + (e && e.name) + ': ' + (e && e.message) }));

function dedicated() {
  return new Promise(resolve => {
    const w = new Worker('/w/probe.js');
    w.onmessage = e => { resolve(e.data); w.terminate(); };
    w.onerror = e => resolve({ error: 'worker error: ' + e.message });
    w.postMessage('probe');
  });
}

function shared() {
  return new Promise(resolve => {
    const s = new SharedWorker('/w/probe.js', { name: 'probe-' + Date.now() });
    s.onerror = e => resolve({ error: 'shared worker error: ' + e.message });
    s.port.onmessage = e => resolve(e.data);
    s.port.start();
    s.port.postMessage('probe');
  });
}

async function service() {
  for (const old of await navigator.serviceWorker.getRegistrations()) await old.unregister();
  const reg = await navigator.serviceWorker.register('/w/probe.js', { scope: '/w/' });
  const sw = reg.installing || reg.waiting || reg.active;
  if (sw.state !== 'activated') {
    await new Promise(resolve => sw.addEventListener('statechange', () => { if (sw.state === 'activated') resolve(); }));
  }
  const answer = new Promise(resolve => { navigator.serviceWorker.onmessage = e => resolve(e.data); });
  sw.postMessage('probe');
  const result = await answer;
  await reg.unregister();
  return result;
}

function frame() {
  return new Promise(resolve => {
    window.addEventListener('message', e => {
      if (e.origin === location.origin && e.data && e.data.from === 'frame') resolve(e.data.result);
    });
    const f = document.createElement('iframe');
    f.src = '/frame.html';
    document.body.appendChild(f);
  });
}

(async () => {
  const out = { kind: 'wasm', picker: typeof showOpenFilePicker };
  out.page = await within(self.sparProbe(), 5000, 'page');
  out.dedicated = await within(dedicated(), 5000, 'dedicated');
  out.shared = await within(shared(), 5000, 'shared');
  out.service = await within(service(), 8000, 'service');
  out.frame = await within(frame(), 5000, 'frame');
  chrome.webview.postMessage(JSON.stringify(out));
})();
