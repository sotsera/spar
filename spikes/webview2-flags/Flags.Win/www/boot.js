// First script of the page: tells the host it ran.
chrome.webview.postMessage(JSON.stringify({ kind: 'boot', t: performance.now() }));
