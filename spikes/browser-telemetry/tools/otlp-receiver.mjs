// Minimal OTLP/HTTP receiver for the spike: accepts protobuf (and JSON) traces and logs, decodes the fields the spike
// checks (ids, parents, names, service names, severities), and appends one JSON line per request to a log file.
// Usage: node otlp-receiver.mjs <port> <out.jsonl> [--host 127.0.0.1]
// Also: node otlp-receiver.mjs --report <out.jsonl>   prints each trace as a tree, with its services.
import { createServer } from "node:http";
import { appendFileSync, readFileSync } from "node:fs";

// ---- Protobuf wire format: just enough to walk OTLP messages ----

function* fields(bytes) {
  let at = 0;
  const varint = () => {
    let result = 0n;
    let shift = 0n;
    while (true) {
      const byte = bytes[at++];
      result |= BigInt(byte & 0x7f) << shift;
      if ((byte & 0x80) === 0) return result;
      shift += 7n;
    }
  };

  while (at < bytes.length) {
    const key = Number(varint());
    const field = key >>> 3;
    const wire = key & 7;
    if (wire === 0) yield { field, wire, value: varint() };
    else if (wire === 1) {
      yield { field, wire, value: Buffer.from(bytes.subarray(at, at + 8)).readBigUInt64LE() };
      at += 8;
    } else if (wire === 2) {
      const length = Number(varint());
      yield { field, wire, value: bytes.subarray(at, at + length) };
      at += length;
    } else if (wire === 5) {
      at += 4;
    } else throw new Error(`unsupported wire type ${wire}`);
  }
}

const text = bytes => Buffer.from(bytes).toString("utf8");
const hex = bytes => Buffer.from(bytes).toString("hex");

function anyValue(bytes) {
  for (const f of fields(bytes)) {
    if (f.field === 1) return text(f.value);
    if (f.field === 2) return f.value !== 0n;
    if (f.field === 3) return Number(f.value);
    if (f.field === 4) return "(double)";
    return "(complex)";
  }
  return null;
}

function attributes(list) {
  const result = {};
  for (const bytes of list) {
    let key = "";
    let value = null;
    for (const f of fields(bytes)) {
      if (f.field === 1) key = text(f.value);
      if (f.field === 2) value = anyValue(f.value);
    }
    result[key] = value;
  }
  return result;
}

function resource(bytes) {
  return attributes([...fields(bytes)].filter(f => f.field === 1).map(f => f.value));
}

function scope(bytes) {
  for (const f of fields(bytes)) if (f.field === 1) return text(f.value);
  return "";
}

function span(bytes) {
  const s = { traceId: "", spanId: "", parentSpanId: "", name: "", kind: 0, startNs: "0", endNs: "0", status: 0, attributes: {} };
  const attrs = [];
  for (const f of fields(bytes)) {
    switch (f.field) {
      case 1: s.traceId = hex(f.value); break;
      case 2: s.spanId = hex(f.value); break;
      case 3: s.traceState = text(f.value); break;
      case 4: s.parentSpanId = hex(f.value); break;
      case 5: s.name = text(f.value); break;
      case 6: s.kind = Number(f.value); break;
      case 7: s.startNs = f.value.toString(); break;
      case 8: s.endNs = f.value.toString(); break;
      case 9: attrs.push(f.value); break;
      case 15: for (const g of fields(f.value)) if (g.field === 3) s.status = Number(g.value); break;
    }
  }

  s.attributes = attributes(attrs);
  return s;
}

function logRecord(bytes) {
  const r = { timeNs: "0", severity: 0, severityText: "", body: null, traceId: "", spanId: "" };
  for (const f of fields(bytes)) {
    switch (f.field) {
      case 1: r.timeNs = f.value.toString(); break;
      case 2: r.severity = Number(f.value); break;
      case 3: r.severityText = text(f.value); break;
      case 5: r.body = anyValue(f.value); break;
      case 9: r.traceId = hex(f.value); break;
      case 10: r.spanId = hex(f.value); break;
    }
  }
  return r;
}

function decodeTraces(bytes) {
  const out = [];
  for (const rs of fields(bytes)) {
    if (rs.field !== 1) continue;
    let res = {};
    for (const f of fields(rs.value)) {
      if (f.field === 1) res = resource(f.value);
      if (f.field === 2) {
        let scopeName = "";
        for (const g of fields(f.value)) {
          if (g.field === 1) scopeName = scope(g.value);
          if (g.field === 2) out.push({ service: res["service.name"], scope: scopeName, ...span(g.value) });
        }
      }
    }
  }
  return out;
}

function decodeLogs(bytes) {
  const out = [];
  for (const rl of fields(bytes)) {
    if (rl.field !== 1) continue;
    let res = {};
    for (const f of fields(rl.value)) {
      if (f.field === 1) res = resource(f.value);
      if (f.field === 2) {
        let scopeName = "";
        for (const g of fields(f.value)) {
          if (g.field === 1) scopeName = scope(g.value);
          if (g.field === 2) out.push({ service: res["service.name"], scope: scopeName, ...logRecord(g.value) });
        }
      }
    }
  }
  return out;
}

// ---- Report ----

function report(file) {
  const lines = readFileSync(file, "utf8").split("\n").filter(Boolean).map(l => JSON.parse(l));
  const spans = lines.flatMap(l => l.spans ?? []);
  const logs = lines.flatMap(l => l.logs ?? []);
  const byTrace = new Map();
  for (const s of spans) {
    if (!byTrace.has(s.traceId)) byTrace.set(s.traceId, []);
    byTrace.get(s.traceId).push(s);
  }

  console.log(`requests=${lines.length} spans=${spans.length} logs=${logs.length} traces=${byTrace.size}`);

  // Every page client span (a Spar call) should have exactly one backend server span as its child, in the same trace.
  const clientSpans = spans.filter(s => s.kind === 3 && s.attributes["rpc.system"] === "spar");
  const serverByParent = new Map(spans.filter(s => s.kind === 2).map(s => [s.traceId + s.parentSpanId, s]));
  const joined = clientSpans.filter(s => serverByParent.has(s.traceId + s.spanId)).length;
  const roots = spans.filter(s => s.kind === 2 && !s.parentSpanId).length;
  console.log(`page-call-spans=${clientSpans.length} joined-with-backend=${joined} backend-root-spans=${roots} relay-spans=${spans.filter(s => (s.attributes["url.full"] ?? "").includes("/v1/")).length}`);
  for (const l of lines) {
    console.log(`  ${l.at} ${l.path} ${l.contentType} ${l.bytes}B spans=${l.spans?.length ?? 0} logs=${l.logs?.length ?? 0} headers=${JSON.stringify(l.headers)}`);
  }

  for (const [traceId, list] of byTrace) {
    const services = [...new Set(list.map(s => s.service))];
    if (services.length < 2 && !process.argv.includes("--all")) continue;
    console.log(`\ntrace ${traceId} services=${services.join(",")} spans=${list.length}`);
    const ids = new Set(list.map(s => s.spanId));
    const children = new Map();
    for (const s of list) {
      const parent = ids.has(s.parentSpanId) ? s.parentSpanId : "";
      if (!children.has(parent)) children.set(parent, []);
      children.get(parent).push(s);
    }

    const t0 = BigInt(list.reduce((min, s) => (BigInt(s.startNs) < BigInt(min) ? s.startNs : min), list[0].startNs));
    const print = (parent, depth) => {
      for (const s of (children.get(parent) ?? []).sort((a, b) => (BigInt(a.startNs) < BigInt(b.startNs) ? -1 : 1))) {
        const start = Number(BigInt(s.startNs) - t0) / 1e6;
        const duration = Number(BigInt(s.endNs) - BigInt(s.startNs)) / 1e6;
        const orphan = depth === 0 && s.parentSpanId ? ` (parent ${s.parentSpanId} not received)` : "";
        console.log(`${"  ".repeat(depth + 1)}${s.service} | ${s.name} kind=${s.kind} span=${s.spanId} +${start.toFixed(2)}ms ${duration.toFixed(2)}ms${orphan}`);
        print(s.spanId, depth + 1);
      }
    };
    print("", 0);
    for (const l of logs.filter(l => l.traceId === traceId)) {
      console.log(`    log ${l.service} ${l.severityText} span=${l.spanId} ${JSON.stringify(l.body)}`);
    }
  }
}

if (process.argv[2] === "--report") {
  report(process.argv[3]);
} else {
  const port = Number(process.argv[2]);
  const out = process.argv[3];
  const hostIndex = process.argv.indexOf("--host");
  const host = hostIndex > 0 ? process.argv[hostIndex + 1] : "127.0.0.1";
  const server = createServer((req, res) => {
    const chunks = [];
    req.on("data", c => chunks.push(c));
    req.on("end", () => {
      const body = Buffer.concat(chunks);
      const contentType = req.headers["content-type"] ?? "";
      const entry = {
        at: new Date().toISOString(),
        path: req.url,
        contentType,
        bytes: body.length,
        headers: Object.fromEntries(Object.entries(req.headers).filter(([k]) => !["host", "content-length", "connection", "content-type"].includes(k))),
      };
      try {
        const json = contentType.includes("json");
        if (req.url.endsWith("/v1/traces")) entry.spans = json ? JSON.parse(body).resourceSpans?.length ?? 0 : decodeTraces(body);
        if (req.url.endsWith("/v1/logs")) entry.logs = json ? JSON.parse(body).resourceLogs?.length ?? 0 : decodeLogs(body);
      } catch (error) {
        entry.decodeError = String(error);
      }

      appendFileSync(out, JSON.stringify(entry) + "\n");
      console.log(`OTLP ${entry.path} ${entry.bytes}B spans=${entry.spans?.length ?? "-"} logs=${entry.logs?.length ?? "-"}`);
      res.writeHead(200, { "Content-Type": "application/x-protobuf" });
      res.end();
    });
  });
  server.listen(port, host, () => console.log(`OTLP_RECEIVER listening http://${host}:${port} -> ${out}`));
}
