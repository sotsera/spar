// The SDK's tracing hook: a call's span from the telemetry chunk's tracer, its traceparent on the call message, and its
// end on the call's outcome; without a tracer, nothing changes.
import { afterEach, beforeEach, describe, expect, test } from "vitest";
import { Connection, unknownErrorCase, type Decoder } from "../src/connection.js";
import { setTracer, traced, type CallSpan, type Tracer } from "../src/tracing.js";
import { openStream, openSubscription, type StreamDecoder } from "../src/streams.js";
import { FakeHost } from "./fakeHost.js";

const decoder: Decoder<string> = {
  value: json => json,
  error: code => (code === "notFound" ? {} : unknownErrorCase),
};

/** Records the spans it starts and how each ended. */
class RecordingTracer implements Tracer {
  readonly calls: Array<{ name: string; traceparent: string; ended?: [string, string?]; attributes: Record<string, number | string> }> = [];
  readonly groups: string[] = [];

  startCall(contract: string, command: string): CallSpan {
    const index = this.calls.length;
    const record: { name: string; traceparent: string; ended?: [string, string?]; attributes: Record<string, number | string> } = {
      name: `${contract}/${command}`,
      traceparent: `00-${"a".repeat(32)}-${(index + 1).toString(16).padStart(16, "0")}-01`,
      attributes: {},
    };
    this.calls.push(record);
    return {
      traceparent: record.traceparent,
      end: (outcome, code) => {
        record.ended = code === undefined ? [outcome] : [outcome, code];
      },
      set: (name, value) => {
        record.attributes[name] = value;
      },
    };
  }

  traced<T>(name: string, run: () => T): T {
    this.groups.push(name);
    return run();
  }
}

let host: FakeHost;
let connection: Connection;

beforeEach(() => {
  host = new FakeHost();
  connection = new Connection(host, "0123456789abcdef");
  host.open();
});

afterEach(() => setTracer(undefined));

describe("without telemetry", () => {
  test("a call carries no traceparent", () => {
    void connection.invoke("pages", "get", "{}", decoder);

    expect(host.last("call")?.traceparent).toBeUndefined();
  });

  test("traced only runs the function and returns what it returns", () => {
    expect(traced("save page", () => 42)).toBe(42);
  });
});

describe("with telemetry", () => {
  test("a call sends its span's traceparent and ends the span with its value", async () => {
    const tracer = new RecordingTracer();
    setTracer(tracer);

    const call = connection.invoke("pages", "get", "{}", decoder);
    const sent = host.last("call")!;
    expect(sent.traceparent).toBe(tracer.calls[0]!.traceparent);

    host.deliver({ type: "result", id: sent.id!, ok: true, payload: "\"page\"" });
    await call;
    expect(tracer.calls[0]!.ended).toEqual(["success"]);
  });

  test("a declared error ends the span as an error outcome, without a failure's code", async () => {
    const tracer = new RecordingTracer();
    setTracer(tracer);

    const call = connection.invoke("pages", "get", "{}", decoder);
    host.deliver({ type: "result", id: host.last("call")!.id!, ok: false, payload: "{\"code\":\"notFound\"}" });
    await call;

    expect(tracer.calls[0]!.ended).toEqual(["error"]);
  });

  test("a failure ends the span with its kind and code", async () => {
    const tracer = new RecordingTracer();
    setTracer(tracer);

    const call = connection.invoke("pages", "get", "{}", decoder);
    host.close();
    await call;

    expect(tracer.calls[0]!.ended).toEqual(["transport", "lost"]);
  });

  test("a call refused before it starts has no span", async () => {
    const tracer = new RecordingTracer();
    setTracer(tracer);

    await connection.invoke("pages", "get", "{}", decoder, { signal: AbortSignal.abort() });

    expect(tracer.calls).toHaveLength(0);
  });

  test("traced hands the function to the tracer", () => {
    const tracer = new RecordingTracer();
    setTracer(tracer);

    expect(traced("save page", () => "done")).toBe("done");
    expect(tracer.groups).toEqual(["save page"]);
  });
});

describe("streams", () => {
  const numbers: StreamDecoder<number> = { item: value => Number(value), error: () => unknownErrorCase };

  test("a stream has one span, its traceparent on the stream message, and its item count", async () => {
    const tracer = new RecordingTracer();
    setTracer(tracer);

    const reading = (async () => {
      let count = 0;
      for await (const _ of openStream(connection, { contract: "rows", command: "read" }, "", numbers, undefined)) {
        count++;
      }

      return count;
    })();
    const sent = host.last("stream")!;
    host.deliver({ type: "items", id: sent.id!, payload: "[1,2,3]" });
    host.deliver({ type: "end", id: sent.id!, ok: true, payload: "" });

    expect(await reading).toBe(3);
    expect(tracer.calls).toHaveLength(1);
    expect(sent.traceparent).toBe(tracer.calls[0]!.traceparent);
    expect(tracer.calls[0]!.ended).toEqual(["success"]);
    expect(tracer.calls[0]!.attributes).toEqual({ "spar.stream.kind": "stream", "spar.stream.items": 3 });
  });

  test("a subscription's span counts what it dropped and ends with its failure", async () => {
    const tracer = new RecordingTracer();
    setTracer(tracer);

    const reading = (async () => {
      for await (const _ of openSubscription(connection, { contract: "feed", command: "ticks" }, "", numbers, undefined, { latestOnly: false, resumes: false })) {
        // Read to the end.
      }
    })();
    const sent = host.last("stream")!;
    host.deliver({ type: "items", id: sent.id!, dropped: 4, payload: "[1]" });
    host.deliver({ type: "failure", id: sent.id!, kind: "failed", code: "failed", execution: "unknown", itemsSent: 1, payload: "" });
    await reading;

    expect(tracer.calls[0]!.ended).toEqual(["failed", "failed"]);
    expect(tracer.calls[0]!.attributes).toEqual({
      "spar.stream.kind": "subscription",
      "spar.stream.items": 1,
      "spar.stream.dropped": 4,
      "spar.stream.resumes": 0,
    });
  });
});
