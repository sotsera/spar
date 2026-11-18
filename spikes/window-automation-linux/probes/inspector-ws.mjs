// What WebKitGTK's HTTP inspector server speaks. Usage: node inspector-ws.mjs http://127.0.0.1:<port>
// Reads the target list page, opens the first page target's WebSocket, and sends WebKit Inspector Protocol messages
// (directly and wrapped in Target.sendMessageToTarget) plus Chrome DevTools Protocol methods the library depends on.
const base = process.argv[2];
const html = await (await fetch(base + "/")).text().catch(e => "fetch failed: " + e.message);
const sockets = [...new Set([...html.matchAll(/socket\/\d+\/\d+\/[A-Za-z]+/g)].map(m => m[0]))];
console.log(`INSPECTOR targets page: ${html.length} bytes; sockets: ${sockets.join(", ") || "none"}`);
if (!sockets.length) { console.log("INSPECTOR page excerpt: " + html.replace(/\s+/g, " ").slice(0, 300)); process.exit(0); }
const ws = new WebSocket(base.replace("http", "ws") + "/" + sockets[0]);
const log = [];
let pageTarget = null, id = 0;
const send = (method, params = {}) => { const m = { id: ++id, method, params }; ws.send(JSON.stringify(m)); return m.id; };
const sendToTarget = (method, params = {}) => send("Target.sendMessageToTarget", { targetId: pageTarget, message: JSON.stringify({ id: 1000 + id, method, params }) });
ws.onmessage = e => {
  const m = JSON.parse(e.data);
  if (m.method === "Target.targetCreated" && !pageTarget) pageTarget = m.params.targetInfo.targetId;
  const inner = m.method === "Target.dispatchMessageFromTarget" ? " inner=" + m.params.message.slice(0, 160) : "";
  log.push(JSON.stringify(m).slice(0, 220) + inner);
};
await new Promise((resolve, reject) => { ws.onopen = resolve; ws.onerror = () => reject(new Error("WebSocket error")); }).catch(e => { console.log("INSPECTOR " + e.message); process.exit(0); });
await new Promise(r => setTimeout(r, 800));
send("Runtime.evaluate", { expression: "document.title" });
send("Target.setAutoAttach", { autoAttach: true, waitForDebuggerOnStart: false });
if (pageTarget) {
  sendToTarget("Runtime.evaluate", { expression: "document.title + ' ' + location.href" });
  sendToTarget("Input.dispatchMouseEvent", { type: "mousePressed", x: 10, y: 10, button: "left" });
  sendToTarget("Accessibility.queryAXTree", { role: "button" });
  sendToTarget("Page.captureScreenshot", {});
  sendToTarget("Page.snapshotRect", { x: 0, y: 0, width: 100, height: 100, coordinateSystem: "Viewport" });
}
await new Promise(r => setTimeout(r, 1500));
ws.close();
console.log(`INSPECTOR page target=${pageTarget}`);
for (const line of log) console.log("INSPECTOR <- " + line);
