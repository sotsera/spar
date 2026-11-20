// One worker script for the three kinds: dedicated, shared and service worker.
importScripts('/probe-core.js');
const isService = typeof ServiceWorkerGlobalScope !== 'undefined' && self instanceof ServiceWorkerGlobalScope;
const isShared = typeof SharedWorkerGlobalScope !== 'undefined' && self instanceof SharedWorkerGlobalScope;
if (isService) {
  self.addEventListener('install', () => self.skipWaiting());
  self.addEventListener('message', e => e.waitUntil(self.sparProbe().then(r => e.source.postMessage({ kind: 'service', ...r }))));
} else if (isShared) {
  self.onconnect = e => {
    const port = e.ports[0];
    port.onmessage = async () => port.postMessage({ kind: 'shared', ...(await self.sparProbe()) });
  };
} else {
  self.onmessage = async () => postMessage({ kind: 'dedicated', ...(await self.sparProbe()) });
}
