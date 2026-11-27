// The page side of the probe: the left area takes drops, the right one has no handler. Every drop on the left posts
// the files with postMessageWithAdditionalObjects at once (the DataTransfer is empty after the event), then the
// directory handles, then a JSON report of what the page itself could see.
const handled = document.getElementById("handled");

let dragEvents = [];

for (const type of ["dragenter", "dragover", "dragleave"]) {
  handled.addEventListener(type, (e) => {
    dragEvents.push(type);
    if (type !== "dragleave") {
      e.preventDefault();
      e.dataTransfer.dropEffect = "copy";
    }
  });
}

handled.addEventListener("drop", async (e) => {
  e.preventDefault();
  const started = performance.now();
  const transfer = e.dataTransfer;

  const items = Array.from(transfer.items);
  const entries = items.map((i) => (i.webkitGetAsEntry ? i.webkitGetAsEntry() : null));
  const handlePromises = items.map((i) =>
    i.getAsFileSystemHandle ? i.getAsFileSystemHandle().catch((err) => ({ error: String(err) })) : Promise.resolve(null));

  let postError = null;
  try {
    chrome.webview.postMessageWithAdditionalObjects("files", transfer.files);
  } catch (err) {
    postError = String(err);
  }
  const postedAfter = performance.now() - started;

  const report = {
    kind: "drop",
    x: e.clientX,
    y: e.clientY,
    area: document.elementFromPoint(e.clientX, e.clientY)?.id ?? null,
    dragEvents,
    types: Array.from(transfer.types),
    uriList: transfer.getData("text/uri-list"),
    text: transfer.getData("text/plain"),
    fileCount: transfer.files.length,
    files: Array.from(transfer.files).slice(0, 20).map((f) => ({
      name: f.name, size: f.size, type: f.type, relativePath: f.webkitRelativePath, path: f.path ?? null,
    })),
    items: items.slice(0, 20).map((i) => ({ kind: i.kind, type: i.type })),
    entries: entries.slice(0, 20).map((en) => (en ? { name: en.name, isDirectory: en.isDirectory, fullPath: en.fullPath } : null)),
    postError,
    postedAfter,
  };
  dragEvents = [];

  const handles = await Promise.all(handlePromises);
  report.handles = handles.slice(0, 20).map((h) => (h && h.kind ? { kind: h.kind, name: h.name } : h));
  const real = handles.filter((h) => h && h.kind);
  if (real.length > 0) {
    try {
      chrome.webview.postMessageWithAdditionalObjects("handles", real);
    } catch (err) {
      report.handlePostError = String(err);
    }
  }

  const folder = real.find((h) => h.kind === "directory");
  if (folder) {
    try {
      let count = 0;
      for await (const _ of folder.values()) {
        count += 1;
      }
      report.folderChildren = count;
    } catch (err) {
      report.folderError = String(err);
    }
  }

  report.handlesAfter = performance.now() - started;
  chrome.webview.postMessage(JSON.stringify(report));
});

// A page can make File objects of its own: what reaches the host when it posts one with a path-like name?
chrome.webview.addEventListener("message", (e) => {
  if (e.data !== "forge") {
    return;
  }

  const report = { kind: "forge" };
  try {
    const forged = new File(["not the real one"], "C:\\Windows\\win.ini");
    chrome.webview.postMessageWithAdditionalObjects("forged", [forged, new Blob(["blob"])]);
  } catch (err) {
    report.error = String(err);
  }
  chrome.webview.postMessage(JSON.stringify(report));
});

chrome.webview.postMessage(JSON.stringify({ kind: "ready" }));
