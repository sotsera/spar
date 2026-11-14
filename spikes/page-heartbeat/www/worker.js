// A dedicated worker that hangs; the page's main thread stays free.
postMessage(`started:${performance.now().toFixed(1)}`);
for (;;) { /* hang in the worker only */ }
