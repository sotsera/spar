// @vitest-environment happy-dom
// The page's telemetry chunk as it ships (obj/dist/telemetry/index.js, which `npm test` builds first: from source,
// Node's resolution would pick OpenTelemetry's Node builds) and its start: the settings a host gives, the relay the spans
// go to, and the parents the page's synchronous context gives a call. `fetch` is replaced, so nothing leaves the test.
import { afterEach, beforeEach, describe, expect, test, vi } from "vitest";
import { Connection } from "../src/connection.js";
import { startPageTelemetry, type TelemetryChannel } from "../src/telemetryBoot.js";
import { setTracer, traceCall } from "../src/tracing.js";
import { FakeHost } from "./fakeHost.js";

type Chunk = typeof import("../src/telemetry/index.js");

// A variable, so the type-check, which runs before the build, does not look for the file.
const { builtChunk } = vi.hoisted(() => ({ builtChunk: "../obj/dist/telemetry/index.js" }));

vi.mock("../src/telemetry/index.js", async () => (await import(/* @vite-ignore */ builtChunk)) as Chunk);

const { startTelemetry } = (await import(/* @vite-ignore */ builtChunk)) as Chunk;

interface Sent {
  readonly url: string;
  readonly init: RequestInit | undefined;
}

let sent: Sent[];

beforeEach(() => {
  sent = [];
  vi.stubGlobal("fetch", (url: string | URL, init?: RequestInit) => {
    sent.push({ url: String(url), init });
    return Promise.resolve(new Response(null, { status: 200 }));
  });
});

afterEach(() => {
  setTracer(undefined);
  vi.unstubAllGlobals();
  delete (globalThis as { __sparTelemetry?: unknown }).__sparTelemetry;
});

function traceIdOf(traceparent: string): string {
  return traceparent.split("-")[1]!;
}

describe("the chunk", () => {
  const settings = { serviceName: "com.example.App-page", delayMs: 5_000 };
  const relay = { origin: "http://127.0.0.1:5000", headers: { Authorization: "Bearer token" } };

  test("calls started inside traced share its trace; calls outside it start their own", () => {
    const { tracer } = startTelemetry(settings, relay);

    const [first, second] = tracer.traced("save page", () => [tracer.startCall("pages", "save")!, tracer.startCall("pages", "list")!]);
    const outside = tracer.startCall("pages", "get")!;

    expect(traceIdOf(first!.traceparent)).toBe(traceIdOf(second!.traceparent));
    expect(traceIdOf(outside.traceparent)).not.toBe(traceIdOf(first!.traceparent));
    expect(first!.traceparent).toMatch(/^00-[0-9a-f]{32}-[0-9a-f]{16}-01$/);
  });

  test("a call started after traced's first await is not its child", async () => {
    const { tracer } = startTelemetry(settings, relay);
    let before = "";
    let after = "";

    await tracer.traced("open page", async () => {
      before = tracer.startCall("pages", "get")!.traceparent;
      await Promise.resolve();
      after = tracer.startCall("pages", "list")!.traceparent;
    });

    expect(traceIdOf(after)).not.toBe(traceIdOf(before));
  });

  test("a flush posts protobuf to the relay with the relay's headers", async () => {
    const telemetry = startTelemetry(settings, relay);
    telemetry.tracer.startCall("pages", "get")!.end("success");

    await telemetry.flush();

    const traces = sent.filter(s => s.url === "http://127.0.0.1:5000/_spar/otlp/v1/traces");
    expect(traces.length).toBeGreaterThan(0);
    const headers = new Headers(traces[0]!.init!.headers);
    expect(headers.get("Authorization")).toBe("Bearer token");
    expect(headers.get("Content-Type")).toBe("application/x-protobuf");
  });
});

describe("the start", () => {
  test("a desktop host's injected settings start the chunk, which then traces calls", async () => {
    (globalThis as { __sparTelemetry?: unknown }).__sparTelemetry = { serviceName: "com.example.App-page", delayMs: 5_000 };
    const host = new FakeHost();

    await startPageTelemetry(new Connection(host, "0123456789abcdef"), host);

    expect(traceCall("pages", "get")?.traceparent).toMatch(/^00-/);
    expect(sent).toHaveLength(0);
  });

  test("without injected settings a desktop host's page loads nothing", async () => {
    const host = new FakeHost();

    await startPageTelemetry(new Connection(host, "0123456789abcdef"), host);

    expect(traceCall("pages", "get")).toBeUndefined();
  });

  test("a loopback channel asks its host for the settings, with its token", async () => {
    vi.stubGlobal("fetch", (url: string | URL, init?: RequestInit) => {
      sent.push({ url: String(url), init });
      return Promise.resolve(Response.json({ serviceName: "com.example.App-page", delayMs: 5_000 }));
    });
    const host = new FakeHost();
    const channel: TelemetryChannel = Object.assign(host, {
      telemetryRelay: () => ({ origin: "http://127.0.0.1:5000", headers: { Authorization: "Bearer token" } }),
    });

    await startPageTelemetry(new Connection(channel, "0123456789abcdef"), channel);

    expect(sent[0]!.url).toBe("http://127.0.0.1:5000/_spar/otlp/config");
    expect(new Headers(sent[0]!.init!.headers).get("Authorization")).toBe("Bearer token");
    expect(traceCall("pages", "get")).toBeDefined();
  });

  test.each([
    ["a 204 (telemetry off)", () => Promise.resolve(new Response(null, { status: 204 }))],
    ["a 404", () => Promise.resolve(new Response(null, { status: 404 }))],
    ["settings without a service name", () => Promise.resolve(Response.json({ delayMs: 5_000 }))],
    ["a delay that is not positive", () => Promise.resolve(Response.json({ serviceName: "x", delayMs: 0 }))],
    ["a network error", () => Promise.reject(new TypeError("failed"))],
  ])("a loopback host answering %s leaves telemetry off", async (_name, answer) => {
    vi.stubGlobal("fetch", answer);
    const host = new FakeHost();
    const channel: TelemetryChannel = Object.assign(host, {
      telemetryRelay: () => ({ origin: "http://127.0.0.1:5000", headers: {} }),
    });

    await startPageTelemetry(new Connection(channel, "0123456789abcdef"), channel);

    expect(traceCall("pages", "get")).toBeUndefined();
  });
});
