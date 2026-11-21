// The page-security steps of the bridge probe (M1c step 4, docs/evidence/m1c-page-security.md): the navigation lock by
// script, the response headers as the page sees them, violations for both report channels, the permission gate, and a
// link the test clicks with real input under its own X server. Each result is a note in the backend's log. Nothing here
// may reach the network but the test's own loopback site, open a window, or touch a camera or microphone.
import { probe } from "@spar/app/probe";

const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
const note = text => probe.note(text).unwrap();

// The worker's address passes through a Trusted Types policy: the Worker constructor is a script sink (pitfalls).
let workerPolicy;
const workerUrl = () => {
  workerPolicy ??= trustedTypes.createPolicy("probe-worker", { createScriptURL: url => url });
  return workerPolicy.createScriptURL(new URL("./worker.js", import.meta.url).href);
};

// The headers of one response as the page reads them: the ones plan section 3b requires, by name.
const headerNames = [
  "content-type",
  "content-security-policy",
  "x-content-type-options",
  "cache-control",
  "referrer-policy",
  "cross-origin-opener-policy",
  "cross-origin-resource-policy",
  "permissions-policy",
];
const headersOf = async address => {
  const response = await fetch(address, { cache: "no-store" });
  const seen = { status: response.status };
  for (const name of headerNames) {
    seen[name] = response.headers.get(name);
  }

  return seen;
};

const attempt = async (label, run) => {
  try {
    const result = run();
    await pause(400);
    return `${label}: ${result === undefined ? "done" : String(result)}`;
  } catch (error) {
    return `${label}: threw ${error.name}`;
  }
};

const link = (address, target) => {
  const element = document.createElement("a");
  element.href = address;
  if (target !== undefined) {
    element.target = target;
  }

  element.textContent = address;
  document.body.append(element);
  return element;
};

export const securitySteps = connection => ({
  // Every way a script can try to leave the page; each must leave it where it is, on the same document.
  async "nav-script"() {
    const document0 = connection.document;
    const results = [];
    results.push(await attempt("location https", () => { location.href = "https://example.invalid/top"; }));
    results.push(await attempt("location spar-other", () => { location.href = "spar://other/"; }));
    results.push(await attempt("location data", () => { location.href = "data:text/html,<p>data</p>"; }));
    results.push(await attempt("location blob", () => {
      location.href = URL.createObjectURL(new Blob(["<p>blob</p>"], { type: "text/html" }));
    }));
    results.push(await attempt("location file", () => { location.href = "file:///etc/hostname"; }));
    results.push(await attempt("link click", () => link("https://example.invalid/link").click()));
    results.push(await attempt("blank link click", () => link("https://example.invalid/blank", "_blank").click()));
    results.push(await attempt("window.open", () => window.open("https://example.invalid/open") === null ? "null" : "a window"));
    results.push(await attempt("form post", () => {
      const form = document.createElement("form");
      form.method = "post";
      form.action = "https://example.invalid/form";
      document.body.append(form);
      form.submit();
    }));
    results.push(await attempt("meta refresh", () => {
      const meta = document.createElement("meta");
      meta.httpEquiv = "refresh";
      meta.content = "0; url=https://example.invalid/refresh";
      document.head.append(meta);
    }));
    await pause(600);
    const stayed = location.href.startsWith("spar://app/") && connection.document === document0;
    await note(`nav: ${results.join("; ")}`);
    await note(`nav: still on ${location.href}, same document ${stayed}`);
  },

  // A top-level navigation to a frame's first document (about:blank, about:srcdoc), which WebKit lets the window
  // load: the window must keep the page, on the same document.
  async "nav-about"(which) {
    const document0 = connection.document;
    location.href = `about:${which}`;
    await pause(1_500);
    await note(`nav-about: still on ${location.href}, same document ${connection.document === document0}`);
  },

  // A top-level navigation to the frame address the app declared (csp.FrameSrc): the lock lets it out for frames
  // and stops the window's document at its response.
  async "nav-declared"() {
    const address = await probe.frame().unwrap();
    const document0 = connection.document;
    location.href = address;
    await pause(1_500);
    await note(`nav-declared: still on ${location.href}, same document ${connection.document === document0}`);
  },

  // Frames: about:blank and srcdoc, which the lock refuses on Linux (a frame keeps its first, blank document), and a
  // declared address that redirects elsewhere.
  async frames() {
    const blank = document.createElement("iframe");
    blank.src = "about:blank";
    const srcdoc = document.createElement("iframe");
    // srcdoc is a Trusted Types sink for HTML, like innerHTML.
    srcdoc.srcdoc = trustedTypes.createPolicy("probe-srcdoc", { createHTML: html => html }).createHTML("<p id=inside>srcdoc</p>");
    const loaded = frame => new Promise(resolve => {
      frame.addEventListener("load", resolve, { once: true });
      setTimeout(resolve, 2_000);
    });
    const both = Promise.all([loaded(blank), loaded(srcdoc)]);
    document.body.append(blank, srcdoc);
    await both;
    const inside = srcdoc.contentDocument?.getElementById("inside")?.textContent ?? "none";
    await note(`frames: blank ${blank.contentDocument?.location?.href ?? "unreachable"}, srcdoc ${inside}`);

    const address = await probe.frame().unwrap();
    if (address !== "") {
      const redirected = document.createElement("iframe");
      redirected.src = `${address}?redirect=1`;
      document.body.append(redirected);
      await pause(1_500);
      await note("frames: redirect tried");
    }
  },

  // The response headers of the page, a module, the worker script and a missing file, as the page reads them.
  async headers() {
    const module = document.querySelector("script[type=module]")?.src ?? "";
    const seen = {
      document: await headersOf("/"),
      module: await headersOf(module),
      worker: await headersOf(new URL("./worker.js", import.meta.url).href),
      missing: await headersOf("/no-such-file.js"),
    };
    await note(`headers: ${JSON.stringify(seen)}`);
  },

  // A module worker the host serves: its own response carries the policy. A violation in it raises nothing the page
  // or the host can see on WebKitGTK (spikes/linux-gpu-csp).
  async worker() {
    const worker = new Worker(workerUrl(), { type: "module" });
    const result = await new Promise(resolve => {
      worker.onmessage = event => resolve(event.data);
      worker.onerror = event => resolve({ error: event.message });
      setTimeout(() => resolve({ error: "no answer" }), 5_000);
    });
    worker.terminate();
    await pause(2_000);
    await note(`worker: ${JSON.stringify(result)}`);
  },

  // One violation: img-src blocks an address before any request. Both channels report it; the host logs it once.
  async violation() {
    new Image().src = "https://blocked.invalid/one.png";
    await pause(2_500);
    await note("violation: raised 1");
  },

  // N violations in N groups, then a pause for both channels to be read.
  async distinct(count) {
    const started = performance.now();
    for (let i = 0; i < Number(count); i++) {
      new Image().src = `https://blocked.invalid/${i}.png`;
    }

    const raised = performance.now() - started;
    await pause(3_000);
    await note(`distinct: raised ${count} in ${raised.toFixed(1)} ms`);
  },

  // Every permission a page can ask for without a device or a person: geolocation is declared, the others are not.
  // The camera and microphone are never asked for; listing them only asks whether the page may see their names.
  async permissions() {
    const results = {};
    results.geolocation = await new Promise(resolve => navigator.geolocation.getCurrentPosition(
      () => resolve("position"),
      error => resolve(`error ${error.code}`),
      { timeout: 4_000 }));
    results.notifications = await Notification.requestPermission();
    results.devices = await navigator.mediaDevices?.enumerateDevices().then(
      devices => `${devices.length} listed, labels ${devices.some(device => device.label !== "") ? "shown" : "hidden"}`,
      error => `refused (${error.name})`) ?? "no mediaDevices";
    results.clipboard = await navigator.clipboard?.readText().then(() => "read", error => `refused (${error.name})`) ?? "no clipboard";
    for (const name of ["geolocation", "notifications", "camera", "microphone", "clipboard-read"]) {
      results[`query ${name}`] = await navigator.permissions.query({ name }).then(state => state.state, error => `threw ${error.name}`);
    }

    await note(`permissions: ${JSON.stringify(results)}`);
  },

  // The permissions WebKit asks the host for only after a person's click: the test clicks this button over the whole
  // page with real input (XTEST), and each request is made from the click's handler. None of them is declared.
  async "permissions-click"() {
    const button = document.createElement("button");
    button.textContent = "Ask";
    Object.assign(button.style, { position: "fixed", left: "0", top: "0", width: "100vw", height: "100vh" });
    document.body.append(button);
    const asked = new Promise(resolve => button.addEventListener("click", () => {
      const results = {};
      const pending = [
        Notification.requestPermission().then(state => { results.notifications = state; }),
        navigator.clipboard.readText().then(() => { results.clipboard = "read"; }, error => { results.clipboard = `refused (${error.name})`; }),
        new Promise(done => {
          document.addEventListener("pointerlockchange", () => { results.pointerLock = "locked"; done(); }, { once: true });
          document.addEventListener("pointerlockerror", () => { results.pointerLock = "refused"; done(); }, { once: true });
          try {
            const request = button.requestPointerLock();
            request?.catch?.(() => {});
          } catch (error) {
            results.pointerLock = `threw ${error.name}`;
            done();
          }

          setTimeout(done, 3_000);
        }),
      ];
      Promise.all(pending).then(() => resolve(results));
    }, { once: true }));
    await note("permissions-click: ready");
    await note(`permissions-click: ${JSON.stringify(await asked)}`);
  },

  // A link over the whole page that opens in a new window; the test clicks it with real input (XTEST) and its stub
  // handler records the address. A script's click on the same link must not open it.
  async "external-link"() {
    const element = link("https://example.invalid/from-click", "_blank");
    Object.assign(element.style, { position: "fixed", left: "0", top: "0", width: "100vw", height: "100vh", display: "block" });
    element.click();
    await note("external: ready");
    await new Promise(resolve => {
      element.addEventListener("click", () => setTimeout(resolve, 1_000), { once: true });
      setTimeout(resolve, 15_000);
    });
    await note("external: clicked");
  },
});
