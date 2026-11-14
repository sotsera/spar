// CSP, header and injection probes. Each probe records what the page observed; the host and the external server log
// what actually reached them, so a blocked request shows up as a missing EXT_HIT.
import { send, wait, external } from "/report.js";

const order = (window.__sparOrder = window.__sparOrder || []);
order.push("module:spar=" + typeof window.__spar);
const sparAtModule = window.__spar ? JSON.stringify(window.__spar) : null;
const late = [];
document.addEventListener("securitypolicyviolation", e =>
  late.push({ directive: e.effectiveDirective, blocked: e.blockedURI, sample: e.sample, line: e.lineNumber }));

const results = {};
const probe = async (name, run) => {
  try {
    results[name] = await Promise.race([run(), wait(4000).then(() => "timeout")]);
  } catch (error) {
    results[name] = "error:" + (error?.name ?? "") + ":" + String(error?.message ?? error).slice(0, 120);
  }
};
const color = id => getComputedStyle(document.getElementById(id)).color;
const image = src => new Promise(resolve => {
  const img = new Image();
  img.onload = () => resolve("loaded");
  img.onerror = () => resolve("error");
  img.src = src;
});
const script = src => new Promise(resolve => {
  const s = document.createElement("script");
  s.onload = () => resolve("loaded");
  s.onerror = () => resolve("error");
  s.src = src;
  document.head.append(s);
});

await probe("inlineScript", async () => (order.includes("head-inline") ? "ran" : "blocked"));
await probe("inlineHandler", async () => {
  await wait(100);
  return window.__handlerRan ? "ran" : "blocked";
});
await probe("eval", async () => {
  (0, eval)("1 + 1");
  return "ran";
});
await probe("newFunction", async () => {
  new Function("return 1")();
  return "ran";
});
await probe("setTimeoutString", async () => {
  setTimeout("window.__sts = 1", 0);
  await wait(100);
  return window.__sts === 1 ? "ran" : "blocked";
});
await probe("javascriptUrl", async () => {
  const a = document.createElement("a");
  a.href = "javascript:window.__jsUrl = 1";
  document.body.append(a);
  a.click();
  await wait(200);
  return window.__jsUrl === 1 ? "ran" : "blocked";
});
await probe("wasm", async () => {
  await WebAssembly.compile(new Uint8Array([0, 97, 115, 109, 1, 0, 0, 0]));
  return "ran";
});
await probe("selfCss", async () => (color("self-css") === "rgb(10, 11, 12)" ? "applied" : "blocked"));
await probe("inlineStyleElement", async () => (color("inline-style") === "rgb(1, 2, 3)" ? "applied" : "blocked"));
await probe("styleAttribute", async () => (color("attr-style") === "rgb(4, 5, 6)" ? "applied" : "blocked"));
await probe("cssomStyle", async () => {
  document.getElementById("cssom").style.color = "rgb(7, 8, 9)";
  return color("cssom") === "rgb(7, 8, 9)" ? "applied" : "blocked";
});
await probe("selfImg", () => image("/pixel.svg"));
await probe("dataImg", () => image("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/%3E"));
await probe("extImg", () => image(external + "/img.svg?p=extImg"));
await probe("extCorpSame", () => image(external + "/corp-same.svg?p=extCorpSame"));
await probe("extCorpNone", () => image(external + "/corp-none.svg?p=extCorpNone"));
await probe("extFont", async () => {
  await new FontFace("ext", `url(${external}/font.woff2?p=extFont)`).load();
  return "loaded";
});
await probe("selfFetch", async () => "loaded:" + (await fetch("/data.json")).status);
await probe("extFetch", async () => "loaded:" + (await fetch(external + "/data?p=extFetch")).status);
await probe("extScript", () => script(external + "/script.js?p=extScript"));
await probe("wrongTypeScript", async () => {
  const outcome = await script("/_test/wrong-type.js");
  return outcome + (window.__wrongType ? ":ran" : ":not-run");
});
await probe("base", async () => {
  const base = document.createElement("base");
  base.href = external + "/based/";
  document.head.prepend(base);
  const applied = document.baseURI.startsWith(external);
  base.remove();
  return applied ? "applied" : "blocked";
});
await probe("object", async () => {
  const object = document.createElement("object");
  object.data = external + "/obj.bin?p=object";
  object.type = "application/octet-stream";
  document.body.append(object);
  await wait(500);
  object.remove();
  return "created";
});
await probe("extFrame", () => new Promise(resolve => {
  const frame = document.createElement("iframe");
  frame.onload = () => resolve("load-event");
  frame.src = external + "/page.html?p=extFrame";
  document.body.append(frame);
  setTimeout(() => resolve("no-load-event"), 1500);
}));
await probe("selfFrame", () => new Promise(resolve => {
  const frame = document.createElement("iframe");
  frame.onload = () => {
    let title = "";
    try {
      title = frame.contentDocument?.title ?? "";
    } catch {
      title = "cross-origin";
    }
    resolve("load-event:" + title);
  };
  frame.src = "/frame.html";
  document.body.append(frame);
  setTimeout(() => resolve("no-load-event"), 1500);
}));
await probe("formExternal", async () => {
  const form = document.createElement("form");
  form.method = "post";
  form.action = external + "/form?p=formExternal";
  form.target = "sink";
  const field = document.createElement("input");
  field.name = "f";
  field.value = "1";
  form.append(field);
  document.body.append(form);
  form.submit();
  await wait(700);
  return "submitted";
});
await probe("formSelf", async () => {
  const form = document.createElement("form");
  form.method = "post";
  form.action = "/_spar/form";
  form.target = "sink";
  document.body.append(form);
  form.submit();
  await wait(700);
  return "submitted";
});
await probe("worker", () => new Promise(resolve => {
  const worker = new Worker("/worker.js");
  worker.onmessage = e => resolve(String(e.data));
  worker.onerror = () => resolve("error");
}));
await probe("blobWorker", () => new Promise(resolve => {
  const blob = new Blob(["postMessage('blob-worker-ran')"], { type: "text/javascript" });
  const worker = new Worker(URL.createObjectURL(blob));
  worker.onmessage = e => resolve(String(e.data));
  worker.onerror = () => resolve("error");
}));
await probe("refererImg", () => image(external + "/referer.svg?p=refererImg"));
await probe("refererFetch", async () => "loaded:" + (await fetch(external + "/data?p=refererFetch")).status);

// Headers and contexts.
results.isSecureContext = String(window.isSecureContext);
results.crossOriginIsolated = String(window.crossOriginIsolated);
results.sharedArrayBuffer = typeof SharedArrayBuffer;
const policy = document.permissionsPolicy ?? document.featurePolicy;
results.permissionsPolicyApi = policy ? (document.permissionsPolicy ? "permissionsPolicy" : "featurePolicy") : "none";
if (policy) {
  results.policyAllows = ["camera", "microphone", "geolocation", "usb", "payment", "display-capture"]
    .map(f => `${f}=${policy.allowsFeature(f)}`).join(" ");
}
for (const name of ["camera", "microphone", "geolocation"]) {
  await probe("permissionQuery_" + name, async () => (await navigator.permissions.query({ name })).state);
}
// Device requests only where nothing can reach a person: when Permissions-Policy already denies the feature, or when
// the run opts in (?media=1 / ?geo=1: the Linux container, or a host whose permission event is known to answer first).
// A camera request without the policy on Windows showed a prompt on the desktop (see RESULTS.md), so it is not run.
const query = new URL(location.href).searchParams;
const deniedByPolicy = feature => policy !== undefined && policy !== null && policy.allowsFeature(feature) === false;
await probe("getUserMedia", async () => {
  // Camera requests run only where no person can be prompted (?media=1: the Linux container). On Windows a request
  // reached a desktop prompt even with no PermissionRequested event (see RESULTS.md), so it never runs there.
  if (query.get("media") !== "1") return "skipped:media-not-enabled";
  if (!navigator.mediaDevices) return "no-mediaDevices";
  const started = performance.now();
  try {
    const stream = await navigator.mediaDevices.getUserMedia({ video: true });
    stream.getTracks().forEach(t => t.stop());
    return "granted";
  } catch (error) {
    return `error:${error.name} after ${Math.round(performance.now() - started)}ms`;
  }
});
await probe("geolocation", () => new Promise(resolve => {
  if (!deniedByPolicy("geolocation") && query.get("geo") !== "1") {
    resolve("skipped:not-denied-by-policy");
    return;
  }
  if (!navigator.geolocation) {
    resolve("no-geolocation");
    return;
  }
  const started = performance.now();
  navigator.geolocation.getCurrentPosition(
    () => resolve("granted"),
    e => resolve(`error:code${e.code} after ${Math.round(performance.now() - started)}ms`),
    { timeout: 3000 });
}));

// Trusted Types.
results.trustedTypes = typeof window.trustedTypes;
await probe("innerHtmlSink", async () => {
  const div = document.createElement("div");
  div.innerHTML = "<b>x</b>";
  return "ran";
});
await probe("trustedTypesPolicy", async () => {
  if (!window.trustedTypes) return "no-api";
  const p = trustedTypes.createPolicy("spar-probe", { createHTML: s => s });
  const div = document.createElement("div");
  div.innerHTML = p.createHTML("<b>x</b>");
  return "ran";
});

await wait(1000);
results.order = order.join(" > ");
results.sparAtModule = sparAtModule;
results.earlyViolations = (window.__sparEarlyViolations ?? []).length;
results.lateViolations = late.length;
await send({ kind: "result", page: "probe", results, late });
await send({ kind: "done" });
