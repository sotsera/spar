// Another local process, with no credentials, against a window-test process's remote debugging port: lists the
// targets, then attaches to the test's page and runs script in it. Usage: node exposure.mjs <port>
const port = process.argv[2];
const base = `http://127.0.0.1:${port}`;

const version = await (await fetch(`${base}/json/version`)).json();
console.log(`EXPOSE /json/version Browser=${version.Browser} Protocol=${version["Protocol-Version"]} browser-ws=${version.webSocketDebuggerUrl ? "yes" : "no"}`);
const list = await (await fetch(`${base}/json/list`)).json();
for (const t of list) console.log(`EXPOSE /json/list type=${t.type} url=${t.url.slice(0, 60)} ws=${t.webSocketDebuggerUrl ? "yes" : "no"}`);

function call(ws, method, params = {}) {
  return new Promise((resolve, reject) => {
    const id = Math.floor(Math.random() * 1e9);
    const onMessage = (event) => {
      const message = JSON.parse(event.data);
      if (message.id !== id) return;
      ws.removeEventListener("message", onMessage);
      message.error ? reject(new Error(message.error.message)) : resolve(message.result);
    };
    ws.addEventListener("message", onMessage);
    ws.send(JSON.stringify({ id, method, params }));
  });
}

const open = (url) => new Promise((resolve, reject) => {
  const ws = new WebSocket(url);
  ws.addEventListener("open", () => resolve(ws));
  ws.addEventListener("error", () => reject(new Error("websocket failed")));
});

const page = list.find((t) => t.type === "page" && t.url.startsWith("https://app.localhost"));
if (page) {
  const ws = await open(page.webSocketDebuggerUrl);
  const result = await call(ws, "Runtime.evaluate", {
    expression: "document.title + ' | status: ' + document.querySelector('[role=status]')?.textContent + ' | url: ' + location.pathname",
    returnByValue: true,
  });
  console.log(`EXPOSE page attach + Runtime.evaluate: ${result.result.value}`);
  const shot = await call(ws, "Page.captureScreenshot", { format: "png" });
  console.log(`EXPOSE page screenshot: ${shot.data.length} base64 chars`);
  ws.close();
}

const browser = await open(version.webSocketDebuggerUrl);
const targets = await call(browser, "Target.getTargets");
console.log(`EXPOSE browser attach + Target.getTargets: ${targets.targetInfos.length} targets (${targets.targetInfos.map((t) => t.type).join(", ")})`);
browser.close();
