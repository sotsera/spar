// Shared worker: the same two fetches as the dedicated worker.
async function probe(url) {
  try {
    const response = await fetch(url, { cache: "no-store" });
    return `status ${response.status}`;
  } catch (error) {
    return `error ${error.name}: ${error.message}`;
  }
}

self.onconnect = (event) => {
  const port = event.ports[0];
  port.onmessage = async () => {
    port.postMessage({ answered: await probe("/probe/data.json?from=shared-worker"), unanswered: await probe("/unanswered/shared-worker-fetch") });
  };
};
