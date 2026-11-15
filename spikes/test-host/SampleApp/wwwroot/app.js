"use strict";

// Spike page: one bridge client over whichever transport the host offers (WebView2, WebKitGTK, or loopback fetch).
(() => {
  const pending = new Map();
  let nextId = 1;

  function receive(reply) {
    const tab = reply.indexOf("\t");
    const header = JSON.parse(reply.slice(0, tab));
    const payload = JSON.parse(reply.slice(tab + 1));
    const resolve = pending.get(header.id);
    if (resolve) {
      pending.delete(header.id);
      resolve({ status: header.status, payload });
    }
  }

  function openTransport() {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.addEventListener("message", event => receive(event.data));
      return { name: "webview2", send: message => window.chrome.webview.postMessage(message) };
    }

    if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.spar) {
      return { name: "webkitgtk", send: message => window.webkit.messageHandlers.spar.postMessage(message).then(receive) };
    }

    const token = new URLSearchParams(location.search).get("token");
    return {
      name: "loopback",
      send: message => fetch("/_spar/call", { method: "POST", headers: { "X-Spar-Token": token }, body: message })
        .then(response => response.text())
        .then(receive),
    };
  }

  const transport = openTransport();

  function call(contract, command, payload) {
    const id = nextId++;
    return new Promise(resolve => {
      pending.set(id, resolve);
      transport.send(JSON.stringify({ id, contract, command }) + "\t" + JSON.stringify(payload ?? {}));
    });
  }

  function render(notes) {
    const list = document.getElementById("notes");
    list.replaceChildren(...notes.map(note => {
      const item = document.createElement("li");
      item.textContent = note.text;
      item.dataset.id = String(note.id);
      return item;
    }));
  }

  async function refresh() {
    const reply = await call("notes", "list");
    render(reply.payload);
  }

  document.getElementById("add").addEventListener("submit", async event => {
    event.preventDefault();
    const input = document.getElementById("text");
    const reply = await call("notes", "add", { text: input.value });
    document.getElementById("status").textContent = reply.status === "ok" ? "" : reply.payload.code;
    if (reply.status === "ok") {
      input.value = "";
      await refresh();
    }
  });

  window.spar = { call, transport: transport.name };
  window.sparReady = (async () => {
    await refresh();
    const orders = await call("orders", "count");
    document.getElementById("orders").textContent = orders.status === "ok" ? String(orders.payload) : orders.payload.code;
    document.body.dataset.ready = "1";
    return transport.name;
  })();
})();
