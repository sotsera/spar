// Service worker: claims clients, answers a message with its own fetches, and passes /probe/via-sw/* requests
// to the network with fetch(event.request), so the page can see whether a service worker fetch reaches the host.
self.addEventListener("install", () => self.skipWaiting());
self.addEventListener("activate", (event) => event.waitUntil(self.clients.claim()));

async function probe(url) {
  try {
    const response = await fetch(url, { cache: "no-store" });
    return `status ${response.status}`;
  } catch (error) {
    return `error ${error.name}: ${error.message}`;
  }
}

self.addEventListener("message", async (event) => {
  event.source.postMessage({ answered: await probe("/probe/data.json?from=service-worker"), unanswered: await probe("/unanswered/service-worker-fetch") });
});

self.addEventListener("fetch", (event) => {
  const url = new URL(event.request.url);
  if (url.pathname.startsWith("/probe/via-sw/")) {
    event.respondWith(fetch(`/probe/data.json?from=sw-fetch-event&for=${encodeURIComponent(url.pathname)}`, { cache: "no-store" }));
  }
});
