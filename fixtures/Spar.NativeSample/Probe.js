// Integration assertions execute inside the real WebView using the generated public client.
async function runProbe() {
  const identity = await connect();
  const status = document.querySelector('#status');
  const assert = (condition, detail) => { if (!condition) throw new Error(detail); };
  const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
  const greet = () => backend.greet({ name: 'Spar' });
  const waitActive = async count => {
    for (let attempt = 0; attempt < 100; attempt++) {
      if ((await greet()).activeDelays === count) return;
      await pause(20);
    }
    throw new Error(`Active delay count did not reach ${count}`);
  };
  const expectError = async (promise, code) => {
    try { await promise; } catch (error) { assert(error.code === code, `${error.code} != ${code}`); return; }
    throw new Error(`Expected ${code}`);
  };
  let rawId = 10000;
  const raw = (operation, payload, changes = {}) => new Promise((resolve, reject) => {
    const id = changes.id ?? ++rawId;
    const timer = setTimeout(() => { window.chrome.webview.removeEventListener('message', receive); reject(new Error('Raw call timeout')); }, 5000);
    function receive(event) {
      if (event.data?.id !== id || event.data?.client !== identity.client) return;
      clearTimeout(timer);
      window.chrome.webview.removeEventListener('message', receive);
      resolve(event.data);
    }
    window.chrome.webview.addEventListener('message', receive);
    window.chrome.webview.postMessage({ version: 1, kind: 'call', id, ...identity, operation,
      payload, timeoutMs: 5000, ...changes });
  });
  const initial = await greet();
  assert(initial.greeting === 'Hello, Spar' && initial.deniedEntries === 0, 'Greeting or permission entry failed');
  if (closeProbe) {
    const delay = backend.delay({ milliseconds: 1000, ignoreCancellation: true });
    delay.catch(() => {});
    await waitActive(1);
    await backend.report({ stage: 'close', detail: 'work-outstanding' });
    return;
  }
  if (initial.phase === 0) {
    assert((await backend.counter({ delta: 1 })).value === 1, 'First mutation failed');
    assert((await backend.counter({ delta: 1 })).value === 2, 'Second mutation failed');
    assert((await backend.delay({ milliseconds: 10, ignoreCancellation: false })).outcome === 'finished', 'Delay failed');
    const abort = new AbortController();
    const cancelled = backend.delay({ milliseconds: 5000, ignoreCancellation: false }, { signal: abort.signal });
    // Attach rejection handling before abort, then prove the handler actually exits.
    const cancellationCheck = expectError(cancelled, 'cancelled');
    await waitActive(1);
    abort.abort();
    await cancellationCheck;
    await waitActive(0);
    await expectError(backend.delay({ milliseconds: 1000, ignoreCancellation: true }, { timeoutMs: 100 }), 'timeout');
    assert((await greet()).activeDelays === 1, 'Timeout incorrectly implied work ended');
    await waitActive(0);
    await expectError(backend.denied({ value: 'blocked' }), 'permission_denied');
    assert((await greet()).deniedEntries === 0, 'Denied handler entered');
    await backend.report({ stage: 'typed-pass', detail: identity.contract });
    assert((await raw('missing', {})).code === 'unknown_operation', 'Unknown operation admitted');
    assert((await raw('counter', { delta: 99 }, { contract: '0'.repeat(64) })).code === 'contract_mismatch', 'Stale contract admitted');
    assert((await raw('counter', { delta: 99 }, { document: '0'.repeat(32) })).code === 'document_rejected', 'Wrong document admitted');
    assert((await raw('counter', { delta: 'wrong' })).code === 'invalid_payload', 'Malformed DTO admitted');
    assert((await raw('counter', { delta: 99 }, { id: -1 })).code === 'invalid_request_id', 'Invalid ID admitted');
    // Malformed envelope and oversized input have no trustworthy correlation ID; inspect native diagnostics.
    window.chrome.webview.postMessage('not an envelope');
    window.chrome.webview.postMessage({ oversized: 'x'.repeat(66000) });
    const delayed = raw('delay', { milliseconds: 1000, ignoreCancellation: true });
    delayed.catch(() => {});
    // This report is processed after the delay's admission, before its actual completion.
    await pause(50);
    const reload = raw('report', { stage: 'reload', detail: identity.document });
    reload.catch(() => {});
    return;
  }
  assert(initial.counter === 2 && initial.activeDelays === 0, 'Reload overlapped work or replayed a mutation');
  if (initial.phase === 1) {
    await backend.report({ stage: 'retirement-pass', detail: identity.document });
    // Native top-level navigation policy must revoke admission, deny this origin, then the sample explicitly reloads.
    location.href = 'https://untrusted.invalid/';
    return;
  }
  assert(initial.phase === 2, 'Unexpected probe phase');
  await backend.report({ stage: 'bridge-pass', detail: identity.document });
  document.querySelector('#greeting').textContent = initial.greeting;
  document.querySelector('#backend').textContent = `Backend counter: ${initial.counter}`;
  status.textContent = 'Typed bridge passed · counter 2 · denied entries 0 · work drained before reload';
  document.documentElement.style.background = '#44dd88';
  document.documentElement.style.color = '#101827';
  for (const paragraph of document.querySelectorAll('p')) paragraph.style.color = '#101827';
  await backend.report({ stage: 'client-pass', detail: identity.document });
}
runProbe().catch(error => {
  document.querySelector('#status').textContent = `BRIDGE FAILED: ${error.stack || error}`;
  backend.report({ stage: 'bridge-failed', detail: String(error.message).slice(0, 512) }).catch(() => {});
});
