// Subresources after the document: a fetch, a missing path (404), a second image; then tells the host.
(async () => {
  const data = await fetch('/data.json').then(r => r.status);
  const missing = await fetch('/missing.json').then(r => r.status);
  await new Promise(resolve => {
    const i = new Image();
    i.onload = i.onerror = resolve;
    i.src = '/pixel.svg?again';
  });
  chrome.webview.postMessage(JSON.stringify({ kind: 'ready', data, missing }));
})();
