// Dedicated worker: one answered and one unanswered fetch, reported back to the page.
async function probe(url) {
  try {
    const response = await fetch(url, { cache: "no-store" });
    return `status ${response.status}`;
  } catch (error) {
    return `error ${error.name}: ${error.message}`;
  }
}

self.onmessage = async () => {
  self.postMessage({ answered: await probe("/probe/data.json?from=worker"), unanswered: await probe("/unanswered/worker-fetch") });
};
