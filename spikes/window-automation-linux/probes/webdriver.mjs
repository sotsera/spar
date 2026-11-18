// WebDriver (classic, and the BiDi request) against WebKitWebDriver, with Spar's harness as the browser.
// Usage: node webdriver.mjs http://127.0.0.1:<port> <harness> <page folder>
const [base, harness, page] = process.argv.slice(2);
const wd = async (method, path, body) => {
  const started = performance.now();
  const response = await fetch(base + path, { method, headers: { "Content-Type": "application/json" }, body: body ? JSON.stringify(body) : undefined });
  const text = await response.text();
  return { ms: (performance.now() - started).toFixed(1), status: response.status, json: JSON.parse(text || "{}"), text };
};
const show = (what, r) => console.log(`WEBDRIVER ${what} (${r.ms} ms, ${r.status}): ${r.text.replace(/"data:image\/png;base64,[^"]+"|"value":"[A-Za-z0-9+/=]{200,}"/g, '"<png>"').slice(0, 420)}`);
show("status", await wd("GET", "/status"));
const browser = { binary: harness, args: ["webdriver-host", "--page", page, "--seconds", "60"] };
// First with WebDriver BiDi requested (webSocketUrl), then classic only.
let created = await wd("POST", "/session", { capabilities: { alwaysMatch: { webSocketUrl: true, "webkitgtk:browserOptions": browser } } });
show("new session with webSocketUrl (BiDi)", created);
if (!created.json.value?.sessionId) {
  created = await wd("POST", "/session", { capabilities: { alwaysMatch: { "webkitgtk:browserOptions": browser } } });
  show("new session, classic", created);
}
const id = created.json.value?.sessionId;
if (!id) process.exit(0);
const s = `/session/${id}`;
console.log(`WEBDRIVER webSocketUrl capability returned: ${JSON.stringify(created.json.value.capabilities?.webSocketUrl ?? null)}`);
show("navigate spar://app/", await wd("POST", `${s}/url`, { url: "spar://app/" }));
show("title", await wd("GET", `${s}/title`));
await wd("POST", `${s}/execute/sync`, { script: "window.__t = []; ['pointerdown','click','keydown','input'].forEach(t => addEventListener(t, e => window.__t.push(t + ':' + e.isTrusted), true)); return true;", args: [] });
const find = async (using, value) => { const r = await wd("POST", `${s}/element`, { using, value }); show(`find ${value}`, r); return r.json.value && Object.values(r.json.value)[0]; };
const input = await find("css selector", "form label input");
show("element send keys", await wd("POST", `${s}/element/${input}/value`, { text: "Ada" }));
const actions = await find("xpath", "//button[normalize-space()='Actions']");
show("element click", await wd("POST", `${s}/element/${actions}/click`, {}));
show("actions API (pointer move+down+up)", await wd("POST", `${s}/actions`, { actions: [{ type: "pointer", id: "mouse", parameters: { pointerType: "mouse" }, actions: [{ type: "pointerMove", x: 50, y: 20, origin: "viewport" }, { type: "pointerDown", button: 0 }, { type: "pointerUp", button: 0 }] }] }));
show("events the page saw", await wd("POST", `${s}/execute/sync`, { script: "return window.__t.join(' ') + ' | input value=' + document.querySelector('form label input').value;", args: [] }));
show("computed role of the menu button", await wd("GET", `${s}/element/${actions}/computedrole`));
show("computed label of the menu button", await wd("GET", `${s}/element/${actions}/computedlabel`));
show("screenshot", await wd("GET", `${s}/screenshot`));
const bidi = created.json.value.capabilities?.webSocketUrl;
if (typeof bidi === "string") {
  const ws = new WebSocket(bidi);
  const answer = await new Promise(resolve => { ws.onopen = () => ws.send(JSON.stringify({ id: 1, method: "session.status", params: {} })); ws.onmessage = e => resolve(e.data); ws.onerror = () => resolve("WebSocket error"); setTimeout(() => resolve("no answer in 3 s"), 3000); });
  console.log("WEBDRIVER BiDi session.status: " + String(answer).slice(0, 300));
  ws.close();
}
show("delete session", await wd("DELETE", s));
