// Bridge encoding bench, page side. A classic script so that it also runs from file:// in WebKitGTK.
// Modes (query string): mode=micro (codec costs, all encodings), mode=full&enc=A|B|C|Bj (end to end over the
// host's channel), mode=probe&enc=… (transport probes, WebView2 only).
"use strict";
(function () {
  const query = new URLSearchParams(location.search);
  const MODE = query.get("mode") || "micro";
  const ENC = query.get("enc") || "A";
  const FP = "3d1faabdfca06185";
  const TRACE = { tp: "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01", ts: "congo=t61rcWkgMzE,rojo=00f067aa0ba902b7" };
  const webview = globalThis.chrome && globalThis.chrome.webview;
  const webkitHandler = globalThis.webkit && globalThis.webkit.messageHandlers && globalThis.webkit.messageHandlers.bench;
  const HOST = webview ? "webview2" : webkitHandler ? "webkitgtk" : "loopback";
  const status = text => { const el = document.getElementById("status"); if (el) el.textContent = text; };

  // ---------------------------------------------------------------- encodings
  // Every decoder returns a message: { type, id, … }. Results expose ok/value/error/failure, item batches
  // expose dropped and items, errors expose code and detail. Malformed input gives { type: "malformed", id, error },
  // with id -1 when it could not be read.

  const LETTERS = { o: "open", r: "result", i: "items", e: "end", c: "cancelled", n: "not-executed", u: "outcome-unknown", z: "closing" };

  function canonicalId(text) {
    return /^(0|[1-9][0-9]{0,15})$/.test(text) && +text <= Number.MAX_SAFE_INTEGER ? +text : -1;
  }

  function traceField(trace) {
    if (!trace) return "-";
    return trace.ts ? trace.tp + ";" + encodeURIComponent(trace.ts) : trace.tp;
  }

  const A = {
    name: "A",
    decode(s) {
      if (typeof s !== "string" || s.length === 0) return { type: "malformed", id: -1, error: "empty" };
      const letter = s.charAt(0);
      if (s.length > 1 && s.charCodeAt(1) !== 32) return { type: "malformed", id: -1, error: "kind is not one letter" };
      const type = LETTERS[letter];
      let at = 2;
      const take = () => {
        const end = s.indexOf(" ", at);
        const value = end < 0 ? s.slice(at) : s.slice(at, end);
        at = end < 0 ? s.length : end + 1;
        return value;
      };
      if (type === undefined) return { type: "unrecognized", kind: letter, id: canonicalId(take()) };
      if (letter === "z") return { type };
      if (letter === "o") return { type, doc: take() };
      const id = canonicalId(take());
      if (id < 0) return { type: "malformed", id: -1, error: "bad id" };
      const field = letter === "i" || letter === "n" || letter === "u" ? take() : "";
      const json = s.slice(at);
      let body;
      if (json.length > 0) {
        try { body = JSON.parse(json); } catch (error) { return { type: "malformed", id, error: String(error) }; }
      }
      switch (letter) {
        case "r":
          if (body === undefined) return { type: "malformed", id, error: "missing result" };
          return { type, id, ok: body.ok, value: body.value, error: body.error, failure: body.failure };
        case "i":
          if (!Array.isArray(body)) return { type: "malformed", id, error: "missing items" };
          return { type, id, dropped: +field, items: body };
        case "n":
        case "u":
          return { type, id, code: field, detail: body && body.detail };
        default:
          return json.length > 0 ? { type: "malformed", id, error: "unexpected payload" } : { type, id };
      }
    },
    hello: doc => `h ${doc} 1`,
    call: (id, contract, command, trace, arg) =>
      `q ${id} ${contract} ${command} ${FP} ${traceField(trace)}` + (arg === undefined ? "" : " " + JSON.stringify(arg)),
    stream: (id, contract, command, trace, batch, credits, arg) =>
      `s ${id} ${contract} ${command} ${FP} ${traceField(trace)} ${batch} ${credits}` + (arg === undefined ? "" : " " + JSON.stringify(arg)),
    credits: (id, n) => `k ${id} ${n}`,
    cancel: id => `x ${id}`,
  };

  const B_TYPES = new Set(["open", "result", "items", "end", "cancelled", "not-executed", "outcome-unknown", "closing"]);

  function envelope(m) {
    if (m === null || typeof m !== "object" || typeof m.type !== "string") return { type: "malformed", id: -1, error: "not an envelope" };
    if (!B_TYPES.has(m.type)) return { type: "unrecognized", kind: m.type, id: typeof m.id === "number" ? m.id : -1 };
    if (m.type === "items" && !Array.isArray(m.items)) return { type: "malformed", id: m.id, error: "missing items" };
    return m;
  }

  const B = {
    name: "B",
    decode(s) {
      let m;
      try { m = JSON.parse(s); } catch (error) { return { type: "malformed", id: -1, error: String(error) }; }
      return envelope(m);
    },
    hello: doc => JSON.stringify({ type: "hello", doc, protocol: 1 }),
    call: (id, contract, command, trace, arg) => JSON.stringify({
      type: "call", id, contract, command, fingerprint: FP,
      traceparent: trace ? trace.tp : undefined, tracestate: trace ? trace.ts : undefined, arg,
    }),
    stream: (id, contract, command, trace, batch, credits, arg) => JSON.stringify({
      type: "stream", id, contract, command, fingerprint: FP,
      traceparent: trace ? trace.tp : undefined, tracestate: trace ? trace.ts : undefined, batch, credits, arg,
    }),
    credits: (id, n) => JSON.stringify({ type: "credits", id, credits: n }),
    cancel: id => JSON.stringify({ type: "cancel", id }),
  };

  // B over WebView2's JSON messages: the page posts objects and receives objects (PostWebMessageAsJson).
  const Bj = {
    name: "Bj",
    decode: m => typeof m === "string" ? B.decode(m) : envelope(m),
    hello: doc => ({ type: "hello", doc, protocol: 1 }),
    call: (id, contract, command, trace, arg) => ({
      type: "call", id, contract, command, fingerprint: FP,
      traceparent: trace ? trace.tp : undefined, tracestate: trace ? trace.ts : undefined, arg,
    }),
    stream: (id, contract, command, trace, batch, credits, arg) => ({
      type: "stream", id, contract, command, fingerprint: FP,
      traceparent: trace ? trace.tp : undefined, tracestate: trace ? trace.ts : undefined, batch, credits, arg,
    }),
    credits: (id, n) => ({ type: "credits", id, credits: n }),
    cancel: id => ({ type: "cancel", id }),
  };

  const C = {
    name: "C",
    decode(s) {
      if (typeof s !== "string") return { type: "malformed", id: -1, error: "not a string" };
      const tab = s.indexOf("\t");
      let h;
      try { h = JSON.parse(tab < 0 ? s : s.slice(0, tab)); } catch (error) { return { type: "malformed", id: -1, error: String(error) }; }
      const m = envelopeHeader(h);
      if (m.type === "malformed" || m.type === "unrecognized" || tab < 0) {
        return m.type === "result" || m.type === "items" ? { type: "malformed", id: m.id, error: "missing payload" } : m;
      }
      let p;
      try { p = JSON.parse(s.slice(tab + 1)); } catch (error) { return { type: "malformed", id: h.id, error: String(error) }; }
      switch (m.type) {
        case "result":
          return { type: "result", id: h.id, ok: p.ok, value: p.value, error: p.error, failure: p.failure };
        case "items":
          if (!Array.isArray(p)) return { type: "malformed", id: h.id, error: "missing items" };
          return { type: "items", id: h.id, dropped: h.dropped, items: p };
        default:
          return { type: "malformed", id: h.id, error: "unexpected payload" };
      }
    },
    hello: doc => JSON.stringify({ type: "hello", doc, protocol: 1 }),
    call: (id, contract, command, trace, arg) => JSON.stringify({
      type: "call", id, contract, command, fingerprint: FP,
      traceparent: trace ? trace.tp : undefined, tracestate: trace ? trace.ts : undefined,
    }) + (arg === undefined ? "" : "\t" + JSON.stringify(arg)),
    stream: (id, contract, command, trace, batch, credits, arg) => JSON.stringify({
      type: "stream", id, contract, command, fingerprint: FP,
      traceparent: trace ? trace.tp : undefined, tracestate: trace ? trace.ts : undefined, batch, credits,
    }) + (arg === undefined ? "" : "\t" + JSON.stringify(arg)),
    credits: (id, n) => JSON.stringify({ type: "credits", id, credits: n }),
    cancel: id => JSON.stringify({ type: "cancel", id }),
  };

  function envelopeHeader(h) {
    if (h === null || typeof h !== "object" || typeof h.type !== "string") return { type: "malformed", id: -1, error: "not a header" };
    if (!B_TYPES.has(h.type)) return { type: "unrecognized", kind: h.type, id: typeof h.id === "number" ? h.id : -1 };
    return h;
  }

  const CODECS = { A, B, C, Bj };

  // ---------------------------------------------------------------- helpers
  const now = () => performance.now();
  const sorted = values => Float64Array.from(values).sort();
  const pct = (s, p) => s.length === 0 ? null : s[Math.min(s.length - 1, Math.floor(p * s.length))];
  const stats = values => {
    const s = sorted(values);
    return { n: s.length, p50: pct(s, 0.5), p95: pct(s, 0.95), p99: pct(s, 0.99), max: s.length ? s[s.length - 1] : null };
  };
  const round = (v, d = 4) => v === null || v === undefined ? v : Math.round(v * 10 ** d) / 10 ** d;
  const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

  async function report(path, body) {
    const text = JSON.stringify(body);
    if (HOST === "webkitgtk") {
      webkitHandler.postMessage(text);
      return {};
    }
    const response = await fetch(path, { method: "POST", body: text });
    return response.headers.get("content-type")?.includes("json") ? response.json() : {};
  }

  // ---------------------------------------------------------------- micro benchmarks (all encodings, no transport)
  let sink = 0;

  function measure(fn, minMs) {
    // Calibrate a loop count that takes at least minMs, then time it.
    let iterations = 1;
    for (;;) {
      const start = now();
      for (let i = 0; i < iterations; i++) fn();
      const elapsed = now() - start;
      if (elapsed >= minMs) return (elapsed * 1e6) / iterations;
      iterations = elapsed <= 0 ? iterations * 10 : Math.ceil(iterations * Math.min(10, (minMs * 1.2) / elapsed));
    }
  }

  function touch(m) {
    sink += m.id | 0;
    if (m.items) sink += m.items.length;
    if (m.value) sink += 1;
  }

  async function micro() {
    const corpus = globalThis.CORPUS;
    const samples = 7;
    const minMs = 25;
    const decode = {};
    const encode = {};
    status("micro: decode");
    for (const name of Object.keys(corpus.A)) {
      decode[name] = { A: [], B: [], C: [], payloadOnly: [] };
      const payloadOnly = corpus.payloads[name];
      for (let round = 0; round < samples; round++) {
        for (const codec of [A, B, C]) {
          const frame = corpus[codec.name][name];
          decode[name][codec.name].push(measure(() => touch(codec.decode(frame)), minMs));
        }
        if (payloadOnly) decode[name].payloadOnly.push(measure(() => { sink += JSON.parse(payloadOnly) ? 1 : 0; }, minMs));
      }
      await sleep(0);
    }

    status("micro: encode");
    const note = { title: "Buy milk", body: "Two litres, semi-skimmed", tags: ["home", "shop"] };
    const encoders = {
      "call (note, no trace)": c => c.call(12345, "bench", "save", undefined, note),
      "call (note, traceparent + tracestate)": c => c.call(12345, "bench", "save", TRACE, note),
      "call (no argument)": c => c.call(12345, "bench", "count", undefined, undefined),
      "stream call": c => c.stream(12345, "bench", "rows", undefined, 1000, 8, { count: 100000 }),
      "credits": c => c.credits(12345, 4),
      "cancel": c => c.cancel(12345),
    };
    for (const [name, fn] of Object.entries(encoders)) {
      encode[name] = { A: [], B: [], C: [] };
      for (let round = 0; round < samples; round++) {
        for (const codec of [A, B, C]) {
          encode[name][codec.name].push(measure(() => { sink += fn(codec).length; }, minMs));
        }
      }
      await sleep(0);
    }

    const median = values => sorted(values)[values.length >> 1];
    const summarize = table => Object.fromEntries(Object.entries(table).map(([name, byCodec]) =>
      [name, Object.fromEntries(Object.entries(byCodec).filter(([, v]) => v.length).map(([codec, v]) =>
        [codec, { medianNs: round(median(v), 1), minNs: round(Math.min(...v), 1), maxNs: round(Math.max(...v), 1) }]))]));
    return { decodeNs: summarize(decode), encodeNs: summarize(encode), sink, robustness: robustness() };
  }

  // ---------------------------------------------------------------- robustness of the page decoders
  function robustness() {
    const cases = globalThis.CORPUS.robustness;
    const out = {};
    for (const [codecName, list] of Object.entries(cases)) {
      out[codecName] = {};
      for (const [name, frame] of Object.entries(list)) {
        let outcome;
        try {
          const m = CODECS[codecName].decode(frame);
          outcome = { type: m.type, id: m.id === undefined ? null : m.id };
          if (m.error) outcome.error = String(m.error).slice(0, 120);
          if (m.type === "result" && m.value !== undefined) outcome.value = JSON.stringify(m.value).slice(0, 80);
          if (m.type === "items") outcome.items = m.items.length;
        } catch (error) {
          outcome = { threw: String(error).slice(0, 120) };
        }
        out[codecName][name] = outcome;
      }
    }
    // Page-side encoders with awkward input.
    const lone = "a\ud800b";
    const encoders = {};
    for (const codec of [A, B, C]) {
      const result = {};
      try { result.loneSurrogateArg = JSON.stringify(codec.call(1, "bench", "echo", undefined, { text: lone })).slice(0, 120); } catch (e) { result.loneSurrogateArg = "threw " + e; }
      try { result.loneSurrogateTracestate = codec.call(1, "bench", "echo", { tp: TRACE.tp, ts: "k=" + lone }, undefined).slice(0, 160); } catch (e) { result.loneSurrogateTracestate = "threw " + e; }
      try { result.spaceInTracestate = codec.call(1, "bench", "echo", { tp: TRACE.tp, ts: "k=a b" }, undefined); } catch (e) { result.spaceInTracestate = "threw " + e; }
      try { result.newlineAndU2028Arg = codec.call(1, "bench", "echo", undefined, { text: "a\nb\u2028c\u2029d" }); } catch (e) { result.newlineAndU2028Arg = "threw " + e; }
      try { result.bigint = codec.call(1, "bench", "echo", undefined, { n: 1n }); } catch (e) { result.bigint = "threw " + e; }
      encoders[codec.name] = result;
    }
    return { decoders: out, encoders };
  }

  // ---------------------------------------------------------------- transport + client (full mode)
  const codec = CODECS[ENC];
  const handlers = new Map();
  let opened;
  const busy = { ms: 0, frames: 0, chars: 0 };
  let nextId = 0;
  const documentId = Math.random().toString(36).slice(2, 10);

  function onFrame(data) {
    const start = now();
    const m = codec.decode(data);
    busy.frames++;
    if (typeof data === "string") busy.chars += data.length;
    if (m.type === "open") {
      opened?.(m);
    } else {
      const handler = handlers.get(m.id);
      if (handler) handler(m);
    }
    busy.ms += now() - start;
  }

  let send;
  async function connect() {
    const ready = new Promise(resolve => { opened = resolve; });
    if (webview) {
      webview.addEventListener("message", event => {
        if (typeof event.data === "string" && event.data.charCodeAt(0) === 35) return; // '#': bench control
        onFrame(event.data);
      });
      send = frame => webview.postMessage(frame);
    } else {
      const response = await fetch(`/_spar/channel?enc=${ENC}&doc=${documentId}`, { method: "POST" });
      void readLines(response.body);
      let outbox = [];
      let flushing = false;
      send = frame => {
        outbox.push(frame);
        if (flushing) return;
        flushing = true;
        queueMicrotask(() => {
          const body = outbox.join("\n");
          outbox = [];
          flushing = false;
          void fetch(`/_spar/send?doc=${documentId}`, { method: "POST", body });
        });
      };
    }
    send(codec.hello(documentId));
    await ready;
  }

  async function readLines(body) {
    const reader = body.getReader();
    const decoder = new TextDecoder();
    let buffered = "";
    for (;;) {
      const { value, done } = await reader.read();
      if (done) return;
      buffered += decoder.decode(value, { stream: true });
      let start = 0;
      let newline = buffered.indexOf("\n");
      while (newline >= 0) {
        onFrame(buffered.slice(start, newline));
        start = newline + 1;
        newline = buffered.indexOf("\n", start);
      }
      buffered = buffered.slice(start);
    }
  }

  function unary(command, arg, trace) {
    const id = ++nextId;
    return new Promise(resolve => {
      handlers.set(id, m => { handlers.delete(id); resolve(m); });
      send(codec.call(id, "bench", command, trace, arg));
    });
  }

  function stream(command, arg, options, onItems) {
    const id = ++nextId;
    const window = options.window;
    const half = Math.max(1, window >> 1);
    let owed = 0;
    const call = { id };
    call.done = new Promise(resolve => {
      handlers.set(id, m => {
        if (m.type === "items") {
          onItems(m.items, m.dropped);
          if (++owed >= half) { send(codec.credits(id, owed)); owed = 0; }
          return;
        }
        handlers.delete(id);
        resolve(m);
      });
    });
    send(codec.stream(id, "bench", command, options.trace, options.batch, window, arg));
    return call;
  }

  const marks = {};
  async function mark(name) {
    const metrics = await report(`/bench/mark?name=${encodeURIComponent(name)}`, {});
    marks[name] = metrics;
    return metrics;
  }

  async function phase(name, fn) {
    status(name);
    await sleep(20);
    const before = await mark(name + ":start");
    const busyBefore = { ...busy };
    const wall = now();
    const result = await fn();
    const elapsed = now() - wall;
    const after = await mark(name + ":end");
    result.wallMs = round(elapsed, 2);
    result.handlerMs = round(busy.ms - busyBefore.ms, 2);
    result.handlerFrames = busy.frames - busyBefore.frames;
    result.chars = busy.chars - busyBefore.chars;
    if (before.TaskDuration !== undefined && after.TaskDuration !== undefined) {
      result.taskMs = round((after.TaskDuration - before.TaskDuration) * 1000, 2);
      result.scriptMs = round((after.ScriptDuration - before.ScriptDuration) * 1000, 2);
    }
    return result;
  }

  async function full() {
    await connect();
    const note = { title: "Buy milk", body: "Two litres, semi-skimmed", tags: ["home", "shop"] };
    const results = { host: HOST, enc: ENC, userAgent: navigator.userAgent };

    // Warm-up: JIT and IPC paths.
    for (let i = 0; i < 300; i++) await unary("echo", note);
    for (let i = 0; i < 2; i++) await stream("rows", { count: 20000 }, { batch: 1000, window: 8 }, () => undefined).done;

    async function unaryRun(command, trace, n) {
      const latencies = [];
      let ok = 0;
      const begin = now();
      for (let i = 0; i < n; i++) {
        const start = now();
        const m = await unary(command, note, trace);
        latencies.push(now() - start);
        if (m.type === "result" && m.ok === true) ok++;
      }
      return { ...stats(latencies), meanUs: round((now() - begin) / n * 1000, 1), ok };
    }

    results.unaryEcho = await phase("unary-echo", () => unaryRun("echo", undefined, 2000));
    results.unarySaveTraced = await phase("unary-save-traced", () => unaryRun("save", TRACE, 2000));

    async function rows(batch, runs) {
      const list = [];
      for (let r = 0; r < runs; r++) {
        list.push(await phase(`rows-${batch}-${r}`, async () => {
          let count = 0;
          let qty = 0;
          let first = null;
          const start = now();
          const call = stream("rows", { count: 100000 }, { batch, window: 8 }, items => {
            if (first === null) first = now() - start;
            for (let i = 0; i < items.length; i++) qty += items[i].qty;
            count += items.length;
          });
          const end = await call.done;
          const total = now() - start;
          return { rows: count, end: end.type, firstMs: round(first, 2), totalMs: round(total, 2), rowsPerSec: Math.round(count / total * 1000), qty };
        }));
      }
      return list;
    }
    results.rows1000 = await rows(1000, 3);
    results.rows100 = await rows(100, 3);

    async function push(name, batch) {
      return phase(name, async () => {
        const latencies = [];
        let dropped = 0;
        let gaps = 0;
        let lastSeq = -1;
        let outOfOrder = 0;
        let frames = 0;
        const origin = performance.timeOrigin;
        const call = stream("watch", { rate: 20000, ms: 3000, capacity: 1000 }, { batch, window: 8 }, (items, d) => {
          frames++;
          const t = origin + now();
          if (d > 0) { dropped += d; gaps++; }
          for (let i = 0; i < items.length; i++) {
            const ev = items[i];
            latencies.push(t - ev.at);
            const seq = +ev.seq;
            if (seq <= lastSeq) outOfOrder++;
            lastSeq = seq;
          }
        });
        const end = await call.done;
        return { end: end.type, delivered: latencies.length, dropped, gaps, outOfOrder, frames, latencyMs: stats(latencies) };
      });
    }
    results.push = await push("push-20000", 1000);
    results.pushSmallFrames = await push("push-20000-batch20", 20);
    results.cancel = await phase("cancel", async () => {
      const latencies = [];
      const outcomes = {};
      const begin = now();
      for (let i = 0; i < 200; i++) {
        const id = nextId + 1;
        const pending = unary("wait", undefined, undefined);
        await sleep(0);
        const start = now();
        send(codec.cancel(id));
        const m = await pending;
        latencies.push(now() - start);
        outcomes[m.type] = (outcomes[m.type] || 0) + 1;
      }
      return { ...stats(latencies), meanUs: round((now() - begin) / 200 * 1000, 1), outcomes };
    });

    const failure = await unary("fail", undefined, undefined);
    results.serializationFailure = { type: failure.type, ok: failure.ok, failure: failure.failure };
    const unknown = await unary("nope", undefined, undefined);
    results.unknownCommand = { type: unknown.type, code: unknown.code };
    results.marks = marks;
    return results;
  }

  // ---------------------------------------------------------------- transport probes (WebView2)
  async function probe() {
    const replies = [];
    webview.addEventListener("message", event => replies.push(event.data));
    const describe = value => typeof value === "string"
      ? { kind: "string", length: value.length, text: Array.from(value.slice(0, 240), ch => ch.charCodeAt(0) >= 32 && ch.charCodeAt(0) < 127 ? ch : "<" + ch.charCodeAt(0).toString(16) + ">").join("") }
      : { kind: typeof value, json: (() => { try { return JSON.stringify(value); } catch (e) { return "unserializable " + e; } })(), protoX: value && value.x, ownProto: value ? Object.prototype.hasOwnProperty.call(value, "__proto__") : false };
    const ask = async message => {
      const before = replies.length;
      try { webview.postMessage(message); } catch (error) { return ["page threw " + error]; }
      for (let i = 0; i < 400 && !replies.slice(before).includes("#done"); i++) await sleep(5);
      return replies.slice(before).filter(r => r !== "#done").map(describe);
    };
    const out = {};
    // Page to host: what the host receives (it answers with a '#seen …' description).
    out.pageLoneSurrogateString = await ask("#seen a\ud800b");
    out.pageSeparatorsString = await ask("#seen a" + String.fromCharCode(0x2028) + "b\nc\u0000d");
    out.pageObjectWithLoneSurrogate = await ask({ probe: "seen", text: "a\ud800b" });
    out.pageObjectWithBigint = await ask({ probe: "seen", n: 1n });
    out.pageObjectUndefinedAndNaN = await ask({ probe: "seen", u: undefined, n: NaN, i: Infinity });
    out.pageObjectWithDate = await ask({ probe: "seen", d: new Date(0) });
    // Host to page: the host posts strings and JSON and the page describes what arrived.
    out.hostToPage = await ask("#send-all");
    return out;
  }

  // ---------------------------------------------------------------- entry
  (async () => {
    try {
      let results;
      if (MODE === "micro") results = { host: HOST, mode: MODE, userAgent: navigator.userAgent, ...(await micro()) };
      else if (MODE === "probe") results = { host: HOST, mode: MODE, ...(await probe()) };
      else results = { mode: MODE, ...(await full()) };
      status("done");
      await report("/bench/result", results);
    } catch (error) {
      status("error " + error);
      await report("/bench/result", { error: String(error), stack: error && error.stack });
    }
  })();
})();
