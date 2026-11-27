// Streams and subscriptions on the page, against a scripted host (plan section 2c; decision "M3a's decide-first
// questions"): credits in items, the in-band end, the gap, the newest value only, and cancellation.
import { afterEach, beforeEach, describe, expect, test, vi } from "vitest";
import { Connection, unknownErrorCase } from "../src/connection.js";
import { configureSpar, resetConfiguration, type CallError, type Result } from "../src/results.js";
import { openStream, openSubscription, streamCredits, type StreamDecoder } from "../src/streams.js";
import { FakeHost } from "./fakeHost.js";

interface Row {
  readonly n: number;
}

const rows: StreamDecoder<Row> = {
  item: value => ({ n: Number((value as Record<string, unknown>)["n"]) }),
  error: (code, payload) => (code === "tooMany" ? { limit: Number(payload["limit"]) } : unknownErrorCase),
};

const name = { contract: "rows", command: "read" };

let host: FakeHost;
let connection: Connection;

beforeEach(() => {
  host = new FakeHost();
  connection = new Connection(host, "0123456789abcdef");
  host.open();
});

afterEach(() => {
  vi.useRealTimers();
  resetConfiguration();
});

/** One `items` message with rows `from` to `to`, exclusive. */
function items(id: number, from: number, to: number, dropped?: number, target: FakeHost = host): void {
  const payload = JSON.stringify(Array.from({ length: to - from }, (_, i) => ({ n: from + i })));
  target.deliver(dropped === undefined ? { type: "items", id, payload } : { type: "items", id, dropped, payload });
}

type Seen = number | string;

/** What a loop sees: the rows' numbers, the events by name, the error's code or the failure's kind and code. */
function seen(item: Result<Row, { tooMany: { limit: number } }, object>): Seen {
  return item.match({
    success: (row: Row) => row.n,
    errors: { tooMany: (e: { limit: number }) => `tooMany ${e.limit}` },
    events: { reset: () => "reset", gap: (e: { dropped: number }) => `gap ${e.dropped}`, reconnecting: () => "reconnecting", resumed: () => "resumed" },
    failure: (e: CallError) => `${e.kind} ${e.code}`,
  } as never) as Seen;
}

async function readAll<T>(iterable: AsyncIterable<T>, map: (item: T) => Seen): Promise<Seen[]> {
  const all: Seen[] = [];
  for await (const item of iterable) {
    all.push(map(item));
  }

  return all;
}

describe("streams", () => {
  test("nothing is sent until the loop starts, then a stream message with the first credits", () => {
    const iterable = openStream(connection, name, "3", rows, undefined);
    expect(host.last("stream")).toBeUndefined();

    iterable[Symbol.asyncIterator]();

    expect(host.last("stream")).toMatchObject({ id: 1, contract: "rows", command: "read", credits: streamCredits, payload: "3" });
  });

  test("items arrive in order, and the end finishes the loop", async () => {
    const reading = readAll(openStream(connection, name, "", rows, undefined), seen);
    items(1, 0, 3);
    items(1, 3, 5);
    host.deliver({ type: "end", id: 1, ok: true, payload: "" });

    expect(await reading).toEqual([0, 1, 2, 3, 4]);
  });

  test("a declared error is the last item", async () => {
    const reading = readAll(openStream(connection, name, "", rows, undefined), seen);
    items(1, 0, 2);
    host.deliver({ type: "end", id: 1, ok: false, payload: "{\"code\":\"tooMany\",\"limit\":2}" });

    expect(await reading).toEqual([0, 1, "tooMany 2"]);
  });

  test("a failure is the last item, after the items before it", async () => {
    const reading = readAll(openStream(connection, name, "", rows, undefined), seen);
    items(1, 0, 1);
    host.deliver({ type: "failure", id: 1, kind: "failed", code: "failed", execution: "unknown", itemsSent: 1, payload: "" });

    expect(await reading).toEqual([0, "failed failed"]);
  });

  test("credits go back when half the window has been handed out", async () => {
    const iterator = openStream(connection, name, "", rows, undefined)[Symbol.asyncIterator]();
    items(1, 0, 200);
    for (let i = 0; i < streamCredits / 2 - 1; i++) {
      await iterator.next();
    }

    expect(host.last("credits")).toBeUndefined();
    await iterator.next();
    expect(host.last("credits")).toMatchObject({ id: 1, credits: streamCredits / 2 });
  });

  test("a row the client cannot read fails the stream, which is cancelled", async () => {
    const strict: StreamDecoder<Row> = { item: () => { throw new TypeError("not a row"); }, error: () => unknownErrorCase };
    const reading = readAll(openStream(connection, name, "", strict, undefined), seen);
    items(1, 0, 2);

    expect(await reading).toEqual(["protocol malformed"]);
    expect(host.last("cancel")).toMatchObject({ id: 1 });
  });

  test("leaving the loop cancels the stream and waits for the backend's confirmation", async () => {
    const iterator = openStream(connection, name, "", rows, undefined)[Symbol.asyncIterator]();
    items(1, 0, 3);
    await iterator.next();

    const leaving = iterator.return!();
    expect(host.last("cancel")).toMatchObject({ id: 1 });
    host.deliver({ type: "failure", id: 1, kind: "cancelled", code: "cancelled", execution: "stopped", itemsSent: 3, payload: "" });

    await expect(leaving).resolves.toEqual({ done: true, value: undefined });
  });

  test("leaving the loop throws when the backend never confirms", async () => {
    vi.useFakeTimers();
    const iterator = openStream(connection, name, "", rows, undefined)[Symbol.asyncIterator]();
    items(1, 0, 1);
    await iterator.next();

    const leaving = iterator.return!();
    const caught = leaving.catch((error: unknown) => error);
    await vi.advanceTimersByTimeAsync(5_000);

    expect(await caught).toMatchObject({ error: { kind: "transport", code: "unconfirmed", execution: "unknown" } });
  });

  test("an abort before the stream left the page is cancelled-before-start", async () => {
    const aborted = AbortSignal.abort();

    expect(await readAll(openStream(connection, name, "", rows, aborted), seen)).toEqual(["cancelled cancelled-before-start"]);
    expect(host.last("stream")).toBeUndefined();
  });

  test("a lost channel ends a stream as lost; it never resumes", async () => {
    const reading = readAll(openStream(connection, name, "", rows, undefined), seen);
    items(1, 0, 1);
    host.close();

    expect(await reading).toEqual([0, "transport lost"]);
  });

  test("a stream is read once", () => {
    const iterable = openStream(connection, name, "", rows, undefined);
    iterable[Symbol.asyncIterator]();

    expect(() => iterable[Symbol.asyncIterator]()).toThrow(TypeError);
  });
});

describe("subscriptions", () => {
  const buffered = { latestOnly: false, resumes: true };
  const latest = { latestOnly: true, resumes: true };

  test("the values dropped for a page that fell behind are a gap before the next ones", async () => {
    const iterator = openSubscription(connection, name, "", rows, undefined, buffered)[Symbol.asyncIterator]();
    items(1, 0, 2);
    items(1, 10, 12, 8);

    const values: Seen[] = [];
    for (let i = 0; i < 5; i++) {
      values.push(seen((await iterator.next()).value as never));
    }

    expect(values).toEqual([0, 1, "gap 8", 10, 11]);
  });

  test("a latest-only subscription keeps only its newest unread value, and its credits go back", async () => {
    const iterator = openSubscription(connection, name, "", rows, undefined, latest)[Symbol.asyncIterator]();
    for (let n = 0; n < streamCredits / 2; n++) {
      items(1, n, n + 1);
    }

    expect(seen((await iterator.next()).value as never)).toEqual(streamCredits / 2 - 1);
    expect(host.last("credits")).toMatchObject({ id: 1, credits: streamCredits / 2 });
  });

  test("a subscription whose channel will not come back ends as lost after reconnecting", async () => {
    vi.useFakeTimers();
    const subscription = openSubscription(connection, name, "", rows, undefined, buffered);
    const reading = readAll(subscription, seen);
    items(1, 0, 1);
    host.close();
    await vi.advanceTimersByTimeAsync(31_000);

    const all = await reading;
    expect(all[0]).toEqual(0);
    expect(all[1]).toEqual("reconnecting");
    expect(all.at(-1)).toEqual("transport lost");
  });

  test("the app's configuration turns automatic resume off", async () => {
    configureSpar({ resume: "manual" });
    const { resumes } = await import("../src/results.js");

    expect(resumes(true)).toBe(false);
    resetConfiguration();
    expect(resumes(true)).toBe(true);
    expect(resumes(true, { resume: "manual" })).toBe(false);
    expect(resumes(false)).toBe(false);
  });
});

describe("aborting", () => {
  test("values still unread when the signal aborts are dropped, and the confirmed end is the last item", async () => {
    const controller = new AbortController();
    const iterator = openStream(connection, name, "", rows, controller.signal)[Symbol.asyncIterator]();
    items(1, 0, 3);
    expect(seen((await iterator.next()).value as never)).toEqual(0);

    controller.abort();
    items(1, 3, 5);
    host.deliver({ type: "failure", id: 1, kind: "cancelled", code: "cancelled", execution: "stopped", itemsSent: 5, payload: "" });

    expect(host.last("cancel")).toMatchObject({ id: 1 });
    expect(seen((await iterator.next()).value as never)).toEqual("cancelled cancelled");
    expect((await iterator.next()).done).toBe(true);
  });
});

/** A host whose channel can open again for the same page, as loopback's can. */
class ReconnectingHost extends FakeHost {
  reconnects = 0;

  reconnect(): boolean {
    this.reconnects++;
    return true;
  }
}

describe("resuming", () => {
  test("an automatic subscription reports reconnecting, then reset and resumed on a new channel", async () => {
    vi.useFakeTimers();
    const reconnecting = new ReconnectingHost();
    const page = new Connection(reconnecting, "0123456789abcdef");
    reconnecting.open();
    const iterator = openSubscription(page, name, "7", rows, undefined, { latestOnly: false, resumes: true })[Symbol.asyncIterator]();
    items(1, 0, 1, undefined, reconnecting);
    const seenItems: Seen[] = [seen((await iterator.next()).value as never)];

    reconnecting.close();
    seenItems.push(seen((await iterator.next()).value as never));
    await vi.advanceTimersByTimeAsync(250);
    expect(reconnecting.sent.filter(m => m.type === "hello")).toHaveLength(2);
    reconnecting.open();
    for (let i = 0; i < 2; i++) {
      seenItems.push(seen((await iterator.next()).value as never));
    }

    expect(reconnecting.last("stream")).toMatchObject({ id: 2, payload: "7" });
    items(2, 5, 6, undefined, reconnecting);
    seenItems.push(seen((await iterator.next()).value as never));

    expect(seenItems).toEqual([0, "reconnecting", "reset", "resumed", 5]);
    expect(reconnecting.reconnects).toBe(1);
  });

  test("a one-shot stream and a manual subscription end lost, and the page does not reconnect for them", async () => {
    const reconnecting = new ReconnectingHost();
    const page = new Connection(reconnecting, "0123456789abcdef");
    reconnecting.open();
    const oneShot = readAll(openStream(page, name, "", rows, undefined), seen);
    const manual = readAll(openSubscription(page, { contract: "rows", command: "drain" }, "", rows, undefined, { latestOnly: false, resumes: false }), seen);

    reconnecting.close();

    expect(await oneShot).toEqual(["transport lost"]);
    expect(await manual).toEqual(["transport lost"]);
    expect(reconnecting.reconnects).toBe(0);
  });

  test("a call made while the channel is lost is unavailable, never queued for a later channel", async () => {
    const reconnecting = new ReconnectingHost();
    const page = new Connection(reconnecting, "0123456789abcdef");
    reconnecting.open();
    reconnecting.close();

    const outcome = await page.invoke("pages", "get", "{}", { value: json => json, error: () => unknownErrorCase });

    expect(outcome).toMatchObject({ ok: false, error: { kind: "unavailable", code: "unavailable", execution: "notStarted" } });
  });
});
