// Deferred: runs once the document is parsed. Reports the globals the browser arguments change.
chrome.webview.postMessage(JSON.stringify({
  kind: 'ready',
  t: performance.now(),
  wasm: typeof WebAssembly,
  picker: typeof showOpenFilePicker,
  gc: typeof gc,
}));
