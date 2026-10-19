// Generated transport v1. No frontend framework, HTTP endpoint, evaluation, or automatic mutation replay.
const fingerprint = '__FINGERPRINT__';
const client = Array.from(crypto.getRandomValues(new Uint8Array(16)), n => n.toString(16).padStart(2, '0')).join('');
const pending = new Map();
let documentId = null;
let requestId = 0;
let connectionPromise;
let retired = false;
const channel = window.chrome?.webview;

function envelope(kind, id, operation = null, payload = null, timeoutMs = null) {
  return { version: 1, kind, id, contract: fingerprint, client, document: documentId, operation, payload, timeoutMs };
}

function failure(code) {
  const error = new Error(code);
  error.code = code;
  return error;
}

function finish(id, result, error) {
  const entry = pending.get(id);
  if (!entry) return;
  pending.delete(id);
  clearTimeout(entry.timer);
  entry.signal?.removeEventListener('abort', entry.abort);
  if (error) entry.reject(failure(error));
  else entry.resolve(result);
}

channel?.addEventListener('message', event => {
  const reply = event.data;
  if (!reply || reply.version !== 1 || reply.client !== client) return;
  if (reply.kind === 'hello' && reply.id === 0 && pending.has(0)) {
    if (reply.status !== 'ok') finish(0, null, reply.code || 'handshake_failed');
    else if (reply.contract !== fingerprint) finish(0, null, 'contract_mismatch');
    else if (typeof reply.document === 'string' && /^[0-9a-f]{32}$/.test(reply.document)) {
      documentId = reply.document;
      finish(0, { version: 1, contract: fingerprint, client, document: documentId });
    } else finish(0, null, reply.code || 'handshake_failed');
  } else if (reply.kind === 'result' && reply.contract === fingerprint && reply.document === documentId) {
    finish(reply.id, reply.result, reply.status === 'ok' ? null : reply.code || 'operation_failed');
  }
});

export function connect(clientCredential = null) {
  if (retired) return Promise.reject(failure('document_retired'));
  if (connectionPromise) return connectionPromise;
  connectionPromise = new Promise((resolve, reject) => {
    if (!channel) { reject(failure('bridge_unavailable')); return; }
    pending.set(0, { resolve, reject, timer: setTimeout(() => finish(0, null, 'handshake_timeout'), 10000) });
    try { channel.postMessage(envelope('hello', 0, null, clientCredential)); }
    catch (error) { finish(0, null, 'transport_failed'); }
  });
  return connectionPromise;
}

async function call(operation, payload, options = {}) {
  await connect();
  if (options.signal?.aborted) throw failure('cancelled');
  if (pending.size >= 8) throw failure('busy');
  const timeout = options.timeoutMs ?? 30000;
  if (!Number.isInteger(timeout) || timeout < 1 || timeout > 30000) throw failure('invalid_timeout');
  if (requestId === 2147483647) throw failure('request_ids_exhausted');
  const id = ++requestId;
  const message = envelope('call', id, operation, payload, timeout);
  const serialized = JSON.stringify(message);
  if (new TextEncoder().encode(serialized).length > 65536) throw failure('input_too_large');
  return new Promise((resolve, reject) => {
    const abort = () => {
      try { channel.postMessage(envelope('cancel', id)); } catch (error) { /* No retry or completion claim. */ }
      finish(id, null, 'cancelled');
    };
    const timer = setTimeout(() => {
      try { channel.postMessage(envelope('cancel', id)); } catch (error) { /* Native work may still be running. */ }
      finish(id, null, 'transport_timeout');
    }, timeout + 2000);
    pending.set(id, { resolve, reject, timer, signal: options.signal, abort });
    options.signal?.addEventListener('abort', abort, { once: true });
    try { channel.postMessage(message); }
    catch (error) { finish(id, null, 'transport_failed'); }
  });
}

window.addEventListener('pagehide', () => {
  retired = true;
  for (const id of Array.from(pending.keys())) finish(id, null, 'document_retired');
});
