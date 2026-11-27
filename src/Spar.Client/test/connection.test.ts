// The page side of the bridge protocol, against a scripted host.
import { afterEach, beforeEach, describe, expect, test, vi } from "vitest";
import { Connection, unknownErrorCase, type Decoder } from "../src/connection.js";
import type { CallError, Outcome } from "../src/results.js";
import { FakeHost } from "./fakeHost.js";

interface Page {
  readonly id: string;
  readonly title: string;
}

/** What a generated module would pass: field-by-field decoders for the `pages.get` command. */
const pageDecoder: Decoder<Page> = {
  value: json => {
    const parsed = JSON.parse(json) as Record<string, unknown>;
    return { id: String(parsed["id"]), title: String(parsed["title"]) };
  },
  error: (code, payload) => (code === "notFound" ? { id: String(payload["id"]) } : unknownErrorCase),
};

let host: FakeHost;
let connection: Connection;

beforeEach(() => {
  host = new FakeHost();
  connection = new Connection(host, "0123456789abcdef");
});

afterEach(() => {
  vi.useRealTimers();
});

function errorOf(outcome: Outcome): CallError {
  if (outcome.ok || outcome.code !== undefined) {
    throw new Error(`Expected a failure, got ${JSON.stringify(outcome)}`);
  }

  return outcome.error;
}

describe("opening", () => {
  test("the page says hello with its fingerprint and the types it understands", () => {
    expect(host.sent[0]).toEqual({
      type: "hello",
      protocol: 1,
      types: ["open", "result", "items", "end", "failure", "event", "closing", "close-request", "close-ended"],
      contracts: "0123456789abcdef",
      payload: "",
    });
  });

  test("calls wait for open, then go out in order with increasing ids", async () => {
    const first = connection.invoke("pages", "get", "{\"id\":\"a\"}", pageDecoder);
    const second = connection.invoke("pages", "get", "{\"id\":\"b\"}", pageDecoder);
    expect(host.sent.filter(m => m.type === "call")).toHaveLength(0);

    host.open();

    const calls = host.sent.filter(m => m.type === "call");
    expect(calls.map(c => [c.id, c.payload])).toEqual([[1, "{\"id\":\"a\"}"], [2, "{\"id\":\"b\"}"]]);
    expect(connection.document).toBe("doc-1");
    host.deliver({ type: "result", id: 2, ok: true, payload: "{\"id\":\"b\",\"title\":\"B\"}" });
    host.deliver({ type: "result", id: 1, ok: true, payload: "{\"id\":\"a\",\"title\":\"A\"}" });
    expect(await first).toEqual({ ok: true, value: { id: "a", title: "A" } });
    expect(await second).toEqual({ ok: true, value: { id: "b", title: "B" } });
  });

  test("the host's fingerprint is known from open", () => {
    host.open("ffffffffffffffff");

    expect(connection.hostContracts).toBe("ffffffffffffffff");
  });
});

describe("outcomes", () => {
  beforeEach(() => host.open());

  test("a declared error is decoded field by field", async () => {
    const outcome = connection.invoke("pages", "get", "{}", pageDecoder);

    host.deliver({ type: "result", id: 1, ok: false, payload: "{\"code\":\"notFound\",\"id\":\"x\",\"__proto__\":{\"polluted\":1}}" });

    expect(await outcome).toEqual({ ok: false, code: "notFound", payload: { id: "x" }, call: { contract: "pages", command: "get" } });
    expect(({} as Record<string, unknown>)["polluted"]).toBeUndefined();
  });

  test("an error code this client does not know is protocol / unknown-error-case", async () => {
    const outcome = connection.invoke("pages", "get", "{}", pageDecoder);

    host.deliver({ type: "result", id: 1, ok: false, payload: "{\"code\":\"archived\"}" });

    expect(errorOf(await outcome)).toEqual({ kind: "protocol", code: "unknown-error-case", execution: "unknown", contract: "pages", command: "get" });
  });

  test("a failure becomes a CallError that names its call", async () => {
    const outcome = connection.invoke("pages", "rename", "{}", pageDecoder);

    host.deliver({ type: "failure", id: 1, kind: "rejected", code: "unknown-command", execution: "notStarted", payload: "" });

    expect(errorOf(await outcome)).toEqual({ kind: "rejected", code: "unknown-command", execution: "notStarted", contract: "pages", command: "rename" });
  });

  test("a failed command carries its correlation id, and the detail a Development host sends", async () => {
    const correlation = "4bf92f3577b34da6a3ce929d0e0e4736";
    const outcome = connection.invoke("pages", "save", "{}", pageDecoder);

    host.deliver({ type: "failure", id: 1, kind: "failed", code: "failed", execution: "unknown", payload: `{"correlation":"${correlation}","detail":"IOException: disk full"}` });

    expect(errorOf(await outcome)).toEqual({
      kind: "failed", code: "failed", execution: "unknown", contract: "pages", command: "save", correlation, detail: "IOException: disk full",
    });
  });

  test.each([
    ["not JSON", "{oops"],
    ["an array", "[\"4bf92f3577b34da6a3ce929d0e0e4736\"]"],
    ["a correlation that is not a trace id", "{\"correlation\":\"4BF92F35\"}"],
    ["values that are not strings", "{\"correlation\":7,\"detail\":{}}"],
    ["inherited keys only", "{\"__proto__\":{\"correlation\":\"4bf92f3577b34da6a3ce929d0e0e4736\"}}"],
  ])("a failure payload with %s adds nothing", async (_, payload) => {
    const outcome = connection.invoke("pages", "save", "{}", pageDecoder);

    host.deliver({ type: "failure", id: 1, kind: "failed", code: "failed", execution: "unknown", payload });

    expect(errorOf(await outcome)).toEqual({ kind: "failed", code: "failed", execution: "unknown", contract: "pages", command: "save" });
  });

  test("a value the decoder cannot read is protocol / malformed", async () => {
    const outcome = connection.invoke("pages", "get", "{}", pageDecoder);

    host.deliver({ type: "result", id: 1, ok: true, payload: "not json" });

    expect(errorOf(await outcome).code).toBe("malformed");
  });

  test("a second terminal message for a call is ignored", async () => {
    const outcome = connection.invoke("pages", "get", "{}", pageDecoder);

    host.deliver({ type: "result", id: 1, ok: true, payload: "{\"id\":\"a\",\"title\":\"A\"}" });
    host.deliver({ type: "failure", id: 1, kind: "failed", code: "failed", execution: "unknown", payload: "" });

    expect((await outcome).ok).toBe(true);
  });

  test("an unexpected message type for a pending call fails it, so it cannot hang", async () => {
    const outcome = connection.invoke("pages", "get", "{}", pageDecoder);

    host.deliverText("{\"type\":\"progress\",\"id\":1}\t");

    expect(errorOf(await outcome)).toMatchObject({ kind: "protocol", code: "malformed", execution: "unknown" });
  });
});

describe("cancellation", () => {
  beforeEach(() => host.open());

  test("an abort before the call leaves is certain: cancelled-before-start, nothing sent", async () => {
    const controller = new AbortController();
    controller.abort();

    const outcome = await connection.invoke("pages", "get", "{}", pageDecoder, { signal: controller.signal });

    expect(errorOf(outcome)).toMatchObject({ kind: "cancelled", code: "cancelled-before-start", execution: "notStarted" });
    expect(host.sent.filter(m => m.type === "call")).toHaveLength(0);
  });

  test("an abort sends cancel and reports what the backend confirms", async () => {
    const controller = new AbortController();
    const outcome = connection.invoke("pages", "get", "{}", pageDecoder, { signal: controller.signal });

    controller.abort();
    expect(host.last("cancel")).toEqual({ type: "cancel", id: 1, payload: "" });
    host.deliver({ type: "failure", id: 1, kind: "cancelled", code: "cancelled", execution: "stopped", payload: "" });

    expect(errorOf(await outcome)).toMatchObject({ kind: "cancelled", execution: "stopped" });
  });

  test("a result that wins the race against the cancel is the result", async () => {
    const controller = new AbortController();
    const outcome = connection.invoke("pages", "get", "{}", pageDecoder, { signal: controller.signal });

    controller.abort();
    host.deliver({ type: "result", id: 1, ok: true, payload: "{\"id\":\"a\",\"title\":\"A\"}" });

    expect((await outcome).ok).toBe(true);
  });

  test("an abort the backend never confirms ends as an unknown outcome", async () => {
    vi.useFakeTimers();
    const controller = new AbortController();
    const outcome = connection.invoke("pages", "get", "{}", pageDecoder, { signal: controller.signal });

    controller.abort();
    await vi.advanceTimersByTimeAsync(5_000);

    expect(errorOf(await outcome)).toMatchObject({ kind: "transport", code: "unconfirmed", execution: "unknown" });
  });
});

describe("losing the host", () => {
  test("a call the host received may have run; one it never received did not", async () => {
    host.open();
    const sent = connection.invoke("pages", "get", "{}", pageDecoder);

    host.close();
    const after = connection.invoke("pages", "get", "{}", pageDecoder);

    expect(errorOf(await sent)).toMatchObject({ kind: "transport", code: "lost", execution: "unknown" });
    expect(errorOf(await after)).toMatchObject({ kind: "unavailable", code: "unavailable", execution: "notStarted" });
  });

  test("a call still waiting for open did not run", async () => {
    const waiting = connection.invoke("pages", "get", "{}", pageDecoder);

    host.close();

    expect(errorOf(await waiting)).toMatchObject({ kind: "unavailable", execution: "notStarted" });
  });
});

describe("host messages", () => {
  test("events reach their listeners, and ready and close work reach the host", async () => {
    host.open();
    const events: string[] = [];
    connection.onEvent("backend-updated", payload => events.push(payload));
    let flushed = false;
    connection.onClosing(async () => {
      flushed = true;
    });

    connection.ready();
    host.deliver({ type: "event", name: "backend-updated", payload: "{\"revision\":3}" });
    host.deliver({ type: "closing", payload: "" });
    await vi.waitFor(() => expect(host.last("closing-done")).toBeDefined());

    expect(events).toEqual(["{\"revision\":3}"]);
    expect(flushed).toBe(true);
    expect(host.last("ready")).toEqual({ type: "ready", payload: "" });
  });
});

describe("heartbeat", () => {
  // The desktop host's injected script answers pings from the document's creation (plan section 3, "Hung pages"), so
  // a page that hangs before it connects is seen too; the SDK leaves them alone.
  test("a ping is not the SDK's to answer, and touches no pending call", async () => {
    host.open();
    const outcome = connection.invoke("pages", "get", "{}", pageDecoder);

    host.deliver({ type: "ping", seq: 7, payload: "" });
    host.deliver({ type: "result", id: 1, ok: true, payload: "{\"id\":\"a\",\"title\":\"A\"}" });

    expect(host.last("pong")).toBeUndefined();
    expect(await outcome).toEqual({ ok: true, value: { id: "a", title: "A" } });
  });
});
