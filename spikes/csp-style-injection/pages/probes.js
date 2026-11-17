// Each way a library can add CSS at run time, under the page's policy. A probe passes when its element takes the
// colour it set; every CSP violation is recorded with its directive. Results go to the host log and the console.
import { send, wait } from "/report.js";

const violations = [];
document.addEventListener("securitypolicyviolation", e =>
  violations.push({ directive: e.effectiveDirective, blocked: e.blockedURI, sample: e.sample }));

const meta = document.querySelector('meta[property="csp-nonce"]');
const nonce = meta?.nonce ?? "";
const coloured = id => getComputedStyle(document.getElementById(id)).color === "rgb(10, 20, 30)";
const rule = id => `#${id} { color: rgb(10, 20, 30); }`;

const results = {};
const probe = async (name, run) => {
  try {
    await run();
    await wait(50);
    results[name] = coloured(name) ? "applied" : "not applied";
  } catch (error) {
    results[name] = "error:" + error.name + ":" + String(error.message).slice(0, 100);
  }
};

const styleElement = (id, withNonce) => {
  const style = document.createElement("style");
  if (withNonce !== undefined) {
    style.nonce = withNonce;
  }

  style.textContent = rule(id);
  document.head.append(style);
};

await probe("element", () => styleElement("element"));
await probe("element-nonce", () => styleElement("element-nonce", nonce));
await probe("element-wrong-nonce", () => styleElement("element-wrong-nonce", "bm90LXRoZS1ub25jZQ=="));
await probe("replace-sync", () => {
  const sheet = new CSSStyleSheet();
  sheet.replaceSync(rule("replace-sync"));
  document.adoptedStyleSheets = [...document.adoptedStyleSheets, sheet];
});
await probe("replace-async", async () => {
  const sheet = new CSSStyleSheet();
  await sheet.replace(rule("replace-async"));
  document.adoptedStyleSheets = [...document.adoptedStyleSheets, sheet];
});
await probe("insert-rule", () => {
  const sheet = new CSSStyleSheet();
  sheet.insertRule(rule("insert-rule"));
  document.adoptedStyleSheets = [...document.adoptedStyleSheets, sheet];
});
await probe("linked-insert", () => {
  const linked = [...document.styleSheets].find(s => s.href?.endsWith("/probes.css"));
  linked.insertRule(rule("linked-insert"), linked.cssRules.length);
});
await probe("property", () => document.getElementById("property").style.setProperty("color", "rgb(10, 20, 30)"));
await probe("attribute", () => document.getElementById("attribute").setAttribute("style", "color: rgb(10, 20, 30)"));

// A constructed sheet in a shadow root, as components that render into one would use.
const shadow = document.getElementById("host").attachShadow({ mode: "open" });
const inner = document.createElement("span");
inner.id = "inner";
shadow.append(inner);
const shadowSheet = new CSSStyleSheet();
shadowSheet.replaceSync("#inner { color: rgb(10, 20, 30); }");
shadow.adoptedStyleSheets = [shadowSheet];
await wait(50);
results["shadow-replace-sync"] = getComputedStyle(inner).color === "rgb(10, 20, 30)" ? "applied" : "not applied";

await wait(300);
await send({
  kind: "probes",
  nonceOffered: meta !== null,
  nonceReadable: nonce.length > 0,
  nonceAttributeHidden: meta !== null && meta.getAttribute("nonce") === "",
  results,
  violations,
});
await send({ kind: "done" });
