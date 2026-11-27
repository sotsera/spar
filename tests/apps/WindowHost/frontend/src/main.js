// The window tests' page (tests/Spar.WindowTests): controls for the page engine's rules (roles and names, labels,
// fields of every kind, an element that loads late, a dialog that animates in, a button covered for a moment, one far
// down a scroll container) and the calls of the required host tests.
import "./styles.css";
import { connectSpar, files, hostChannel } from "@spar/app/sdk";
import { fingerprint } from "@spar/app/spar";
import { host } from "@spar/app/host";
import { windows } from "@spar/app/windows";

const connection = connectSpar(hostChannel(), fingerprint);
const status = document.getElementById("status");
const show = text => { status.textContent = text; };
const on = (id, handler) => document.getElementById(id).addEventListener("click", handler);

const settle = (label, call) => call.match({
  success: value => show(`${label}: ${value}`),
  failure: error => show(`${label}: ${error.kind} ${error.code}`),
});

on("hold", () => { show("Long call: running"); settle("Long call", host.hold("hold")); });
on("slow", () => { show("Slow call: running"); settle("Slow call", host.slow(800)); });
on("echo", () => settle("Echo", host.echo("hello")));

// The order form: Place order is enabled once there is a name.
const customer = document.getElementById("customer");
const place = document.getElementById("place");
customer.addEventListener("input", () => { place.disabled = customer.value.trim() === ""; });
on("place", () => {
  const region = document.getElementById("region");
  const gift = document.getElementById("gift").checked ? ", gift wrapped" : "";
  const day = document.getElementById("day").value || "any day";
  const quantity = document.getElementById("quantity").value;
  document.getElementById("summary").textContent = `${customer.value} ordered ${quantity} for ${region.value}${gift}, ${day}`;
});

// Orders arrive 600 ms after the click.
on("load", () => {
  setTimeout(() => {
    const list = document.createElement("ul");
    list.setAttribute("aria-label", "Orders");
    for (const name of ["Ada", "Grace", "Linus"]) {
      const item = document.createElement("li");
      item.textContent = `Order for ${name}`;
      list.append(item);
    }

    document.getElementById("orders").replaceChildren(list);
  }, 600);
});

// The dialog slides in over 150 ms; a click during the animation must wait for it to stop.
const dialog = document.getElementById("dialog");
on("open", () => {
  dialog.classList.add("entering");
  dialog.hidden = false;
  requestAnimationFrame(() => requestAnimationFrame(() => dialog.classList.remove("entering")));
});
on("confirm", () => {
  dialog.hidden = true;
  show("Confirmed");
});
on("cancel", () => {
  dialog.hidden = true;
  show("Cancelled");
});

// The covered button is under a "Loading…" layer for its first 400 ms.
setTimeout(() => document.getElementById("overlay").remove(), 400);
on("covered", () => show("Covered button clicked"));

on("later", () => setTimeout(() => {
  const button = document.createElement("button");
  button.type = "button";
  button.textContent = "Late button";
  button.addEventListener("click", () => show("Late button clicked"));
  document.getElementById("late").replaceChildren(button);
}, 300));

// The jumpy button moves away on its first press, before the release: the click a person would make lands elsewhere.
// Every click the page sees is recorded, by its target's id, in window.hostClicks.
const jumpy = document.getElementById("jumpy");
const clicks = [];
window.hostClicks = clicks;
addEventListener("click", event => clicks.push(event.target instanceof Element ? event.target.id || event.target.tagName : "?"), true);
jumpy.addEventListener("pointerdown", () => jumpy.classList.add("jumped"), { once: true });
on("jumpy", () => show("Jumpy button clicked"));
on("far", () => show("Far away clicked"));
on("log-error", () => console.error("Something went wrong on purpose"));
on("throw", () => setTimeout(() => {
  throw new Error("Thrown on purpose");
}));
on("alert", () => {
  alert("Saved!");
  show("Alert closed");
});

// Typing "/" anywhere but in a field focuses the customer name, as an app's shortcut would.
addEventListener("keydown", event => {
  const target = event.target;
  const typing = target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement || target?.isContentEditable;
  if (event.key === "/" && !typing) {
    event.preventDefault();
    customer.focus();
  }
});

// The close guard the test picks: every request is recorded for the tests, in window.hostCloses.
const guard = document.getElementById("guard");
const discardDialog = document.getElementById("discard-dialog");
const closes = [];
window.hostCloses = closes;
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
windows.onCloseRequested(async request => {
  const record = { reason: request.reason, automated: request.automated, aborted: false, mode: guard.value };
  request.signal.addEventListener("abort", () => { record.aborted = true; });
  closes.push(record);
  await host.note(`close ${request.reason}${request.automated ? " automated" : ""} ${guard.value}`);
  show(`Close requested: ${request.reason}${request.automated ? ", automated" : ""}`);
  switch (guard.value) {
    case "keep":
      return "keep";
    case "ask":
      return { ask: "Close without saving?" };
    case "late-keep":
      await delay(1000);
      return "keep";
    case "slow":
      await delay(5000);
      return "keep";
    case "silent":
      return new Promise(() => undefined);
    case "dialog":
      discardDialog.hidden = false;
      return new Promise(resolve => {
        const answer = value => () => { discardDialog.hidden = true; resolve(value); };
        document.getElementById("discard").onclick = answer("close");
        document.getElementById("stay").onclick = answer("keep");
      });
    case "throw":
      throw new Error("The guard broke on purpose");
    default:
      return "close";
  }
});

// Blocks the page's thread for 12 s, a moment after the click, so whatever started it returns first. The page tells the
// backend "hang started" and hangs in the same task: the call's message leaves synchronously, before the loop, so no
// host message (a close request) can run between the note and the hang. A test waits for the note instead of guessing
// a delay (HostPage.HangAsync, which clicks from script: a trusted click waits for the renderer's acknowledgement,
// which a hang that starts first holds for the 12 s). Hanging in a later task, through a MessageChannel, let a close
// request delivered after the note run the guard first (Linux NativeAOT lane on CI).
on("hang", () => setTimeout(() => {
  host.note("hang started");
  const end = Date.now() + 12_000;
  while (Date.now() < end) {
    // Busy on purpose: a hung page.
  }
}, 50));

// What the key tests read: every keyboard and input event of the Keys field, in order.
const keys = document.getElementById("keys");
const keyEvents = [];
window.hostKeys = keyEvents;
for (const type of ["keydown", "keypress", "beforeinput", "input", "keyup"]) {
  keys.addEventListener(type, event => keyEvents.push({
    type,
    key: event.key ?? null,
    code: event.code ?? null,
    data: event.data ?? null,
    value: keys.value,
  }));
}

// The bridge's small unary round trip, timed by the page (plan, "Provisional budgets"): 20 warm-up calls, then 200
// timed one after another; the times go to a hidden element as JSON for the budget test to summarise.
on("time-echo", async () => {
  const times = [];
  for (let call = 0; call < 220; call++) {
    const started = performance.now();
    await host.echo("ping");
    if (call >= 20) {
      times.push(performance.now() - started);
    }
  }

  document.getElementById("echo-times").textContent = JSON.stringify(times);
});

// WebAssembly in the page and in its workers: Spar's policy refuses every module unless the app allows WebAssembly.
// The worker's address goes through a Trusted Types policy, since the Worker constructors are script sinks.
const wasmModule = new Uint8Array([0, 97, 115, 109, 1, 0, 0, 0]);
const checkWasm = async () => {
  if (typeof WebAssembly === "undefined") {
    return "absent";
  }

  try {
    await WebAssembly.instantiate(wasmModule);
    return "instantiated";
  } catch (error) {
    return `refused (${error.name})`;
  }
};

on("wasm", async () => {
  const urls = trustedTypes.createPolicy("wasm-worker", { createScriptURL: url => url });
  const address = urls.createScriptURL(new URL("./wasm-worker.js", import.meta.url).href);
  const dedicated = await new Promise(resolve => {
    const worker = new Worker(address, { type: "module" });
    worker.onmessage = event => resolve(event.data);
    worker.onerror = event => resolve(`error (${event.message})`);
  });
  const shared = await new Promise(resolve => {
    const worker = new SharedWorker(address, { type: "module" });
    worker.port.onmessage = event => resolve(event.data);
    worker.onerror = event => resolve(`error (${event.message})`);
  });
  document.getElementById("webassembly").textContent =
    `page ${typeof WebAssembly} ${await checkWasm()}; worker ${dedicated}; shared worker ${shared}`;
});

connection.ready();
show("Ready");

// The desktop bridge's stream budgets, timed by the page and run from a test: `window.hostStreams.rows(count)` and
// `window.hostStreams.burst(perSecond)` leave their outcome as JSON in `window.hostStreamResult`.
window.hostStreamResult = "";
window.hostStreams = {
  rows: count => {
    window.hostStreamResult = "";
    void (async () => {
      const started = performance.now();
      let next = 0;
      let ordered = true;
      for await (const item of host.rows(count)) {
        ordered &&= item.match({ success: row => row.n === next, failure: () => false });
        next++;
      }

      window.hostStreamResult = JSON.stringify({ received: next, ordered, ms: performance.now() - started });
    })();
  },
  burst: perSecond => {
    window.hostStreamResult = "";
    const controller = new AbortController();
    void (async () => {
      const started = performance.now();
      let received = 0;
      let gaps = 0;
      let ms = 0;
      for await (const item of host.burst(perSecond, { signal: controller.signal })) {
        item.match({
          success: () => {
            received++;
            if (received === perSecond) {
              ms = performance.now() - started;
              controller.abort();
            }
          },
          events: { reset: () => {}, gap: () => gaps++ },
          failure: () => {},
        });
      }

      window.hostStreamResult = JSON.stringify({ received, gaps, ms });
    })();
  },
};

// The desktop host's blob route, run from a test: `window.hostBlobs.read(size, runs)` makes and reads a handle of `size`
// bytes `runs` times, timed by the page from the address to the last byte; `window.hostBlobs.images(count)` shows that
// many resources in `<img>` elements. Each leaves its outcome as JSON in `window.hostBlobResult`.
window.hostBlobResult = "";
window.hostBlobs = {
  read: (size, runs) => {
    window.hostBlobResult = "";
    void (async () => {
      const times = [];
      let intact = true;
      let afterRelease = 0;
      for (let run = 0; run < runs; run++) {
        const blob = (await host.make(size)).unwrap();
        const started = performance.now();
        const bytes = await blob.bytes();
        times.push(performance.now() - started);
        intact &&= bytes.length === size && bytes[size - 1] === (size - 1) % 251;
        await blob.release();
        if (run === runs - 1) {
          afterRelease = (await fetch(blob.url)).status;
        }
      }

      window.hostBlobResult = JSON.stringify({ times, intact, afterRelease });
    })();
  },
  images: count => {
    window.hostBlobResult = "";
    void (async () => {
      const loads = [];
      for (let n = 0; n < count; n++) {
        const image = new Image();
        loads.push(new Promise(resolve => {
          image.onload = () => resolve(image.naturalWidth === 1);
          image.onerror = () => resolve(false);
        }));
        image.src = host.pixel.url(n);
        document.body.append(image);
      }

      const loaded = (await Promise.all(loads)).filter(Boolean).length;
      window.hostBlobResult = JSON.stringify({ loaded });
    })();
  },
};

// Files: a drop area that takes Markdown and text, and an open dialog. Each dropped file's size is read in C# from its
// grant, so the test sees that the grant reached the backend and resolved to the file; the opened file is read here.
const dropped = document.getElementById("dropped");
files.dropTarget(document.getElementById("drop-area"), { accept: [".md", ".txt"], limit: 5 }, result => void result.match({
  success: async drop => {
    const sizes = await Promise.all(drop.files.map(async file => `${file.name} ${(await host.size(file)).unwrap()}`));
    dropped.textContent = sizes.length === 0 ? "Nothing dropped" : `Dropped ${sizes.join(", ")}`;
  },
  errors: {
    notAccepted: e => { dropped.textContent = `Not taken: ${e.name}`; },
    tooMany: e => { dropped.textContent = `At most ${e.limit}`; },
  },
  failure: error => { dropped.textContent = `Drop failed: ${error.kind} ${error.code}`; },
}));
on("open-file", () => void files.open({ title: "Open notes", filters: [{ name: "Markdown", extensions: ["md"] }] }).match({
  success: async file => { dropped.textContent = `Opened ${file.name}: ${(await file.readText()).unwrap()}`; },
  errors: {
    dismissed: () => { dropped.textContent = "Dismissed"; },
    accessDenied: e => { dropped.textContent = `Denied ${e.path}`; },
    unavailable: e => { dropped.textContent = `Unavailable ${e.reason}`; },
    busy: () => { dropped.textContent = "Busy"; },
  },
}));

// Windows of the app's kinds (decision "M3d's decide-first questions", item 3): each window shows its route and the
// params its opener gave; a note carries a number, so each one is told apart.
const windowInfo = document.getElementById("window-info");
const windowStatus = document.getElementById("window-status");
let notes = 0;
windows.params().match({
  success: params => { windowInfo.textContent = `This window: ${location.pathname} ${JSON.stringify(params)}`; },
  failure: error => { windowInfo.textContent = `This window: ${error.kind} ${error.code}`; },
});
const opened = (label, call) => call.match({
  success: () => { windowStatus.textContent = `${label}: opened`; },
  errors: { notOpened: error => { windowStatus.textContent = `${label}: not opened, ${error.reason}`; } },
  failure: error => { windowStatus.textContent = `${label}: ${error.kind} ${error.code}`; },
});
on("open-note", () => { notes += 1; opened(`Note ${notes}`, windows.open("note", { text: "hello", n: notes })); });
on("open-panel", () => opened("Panel", windows.open("panel")));

// A modal (decision "M3d's decide-first questions", item 4): the opener waits for its answer, which the modal's own
// page gives with closeWith; closed any other way, it is dismissed. Each opener counts its modals' answers.
let answers = 0;
on("show-confirm", () => windows.showModal("confirm", { question: "Sure?" }).match({
  success: value => { answers += 1; windowStatus.textContent = `Confirm ${answers}: ${JSON.stringify(value)}`; },
  errors: {
    dismissed: () => { answers += 1; windowStatus.textContent = `Confirm ${answers}: dismissed`; },
    notOpened: error => { windowStatus.textContent = `Confirm: not opened, ${error.reason}`; },
  },
  failure: error => { windowStatus.textContent = `Confirm: ${error.kind} ${error.code}`; },
}));
on("answer-yes", () => windows.closeWith({ answer: "yes" }));
