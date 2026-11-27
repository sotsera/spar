import { afterEach, describe, expect, test, vi } from "vitest";
import { unknownErrorCase } from "../src/connection.js";
import { configureSpar, invoke, subscribe, type Call, type CallError, type CallOptions, type Result, type SubscribeOptions, type SubscriptionEvents } from "../src/sdk.js";
import { createFakeSpar, type CommandCodec } from "../src/testing.js";
import * as wire from "../src/wire.js";

// A contract module written by hand the way the client step writes one, with the table the generated testing module
// reads: a note with a bigint, errors with and without a payload, a command without a request, one without a value.
interface Note {
  readonly id: string;
  readonly words: bigint;
}

interface NoteError {
  readonly notFound: { readonly id: string };
  readonly locked: undefined;
}

function readNote(value: unknown): Note {
  const object = wire.readObject(value);
  return { id: wire.readString(wire.field(object, "id")), words: wire.readInt64(wire.field(object, "words")) };
}

function writeNote(value: Note): string {
  return wire.writeObject([wire.member("id", wire.writeString(value.id)), wire.member("words", wire.writeInt64(value.words))]);
}

function readNoteRef(value: unknown): { readonly id: string } {
  const object = wire.readObject(value);
  return { id: wire.readString(wire.field(object, "id")) };
}

function readNoteError(code: string, payload: Readonly<Record<string, unknown>>): unknown {
  switch (code) {
    case "notFound":
      return { id: wire.readString(wire.field(payload, "id")) };
    case "locked":
      return undefined;
    default:
      return unknownErrorCase;
  }
}

function writeNoteError(code: string, value: unknown): string {
  switch (code) {
    case "notFound":
      return wire.writeObject([wire.member("code", wire.writeString(code)), wire.member("id", wire.writeString((value as { id: string }).id))]);
    case "locked":
      return wire.writeObject([wire.member("code", wire.writeString(code))]);
    default:
      return wire.refusedMember(code, "NoteError");
  }
}

const notes = {
  get: (request: { readonly id: string }, options?: CallOptions): Call<Note, NoteError> =>
    invoke("notes", "get", () => wire.writeObject([wire.member("id", wire.writeString(request.id))]), { value: json => readNote(JSON.parse(json)), error: readNoteError }, options),
  count: (options?: CallOptions): Call<number> =>
    invoke("notes", "count", () => "", { value: json => wire.readDouble(JSON.parse(json)), error: () => unknownErrorCase }, options),
  changes: (options?: SubscribeOptions<"automatic">): AsyncIterable<Result<Note, NoteError, SubscriptionEvents>> =>
    subscribe("notes", "changes", () => "", { item: readNote, error: readNoteError }, false, true, options),
  remove: (request: { readonly id: string }, options?: CallOptions): Call<void, NoteError> =>
    invoke("notes", "remove", () => wire.writeObject([wire.member("id", wire.writeString(request.id))]), { value: () => undefined, error: readNoteError }, options),
};

const commands = new Map<unknown, CommandCodec>([
  [notes.get, { contract: "notes", command: "get", request: json => readNoteRef(JSON.parse(json)), value: value => writeNote(value as Note), error: writeNoteError }],
  [notes.count, { contract: "notes", command: "count", value: value => wire.writeDouble(value as number) }],
  [notes.remove, { contract: "notes", command: "remove", request: json => readNoteRef(JSON.parse(json)), error: writeNoteError }],
  [notes.changes, { contract: "notes", command: "changes", stream: "subscription", value: value => writeNote(value as Note), error: writeNoteError }],
]);

const fakeSpar = () => createFakeSpar(commands, "0123456789abcdef");

async function failureOf(call: Call<unknown, object>): Promise<CallError> {
  return call.match({ success: () => { throw new Error("expected a failure"); }, failure: error => error }) as Promise<CallError>;
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe("fakeSpar", () => {
  test("a scripted value crosses the wire, bigint included", async () => {
    const spar = fakeSpar();
    spar.on(notes.get).succeed({ id: "a", words: 9007199254740993n });

    const note = await notes.get({ id: "a" }).unwrap();

    expect(note).toEqual({ id: "a", words: 9007199254740993n });
  });

  test("the requests are read back from the wire, in order", async () => {
    const spar = fakeSpar();
    spar.on(notes.get).succeed({ id: "x", words: 1n });

    await notes.get({ id: "a" });
    await notes.get({ id: "b" });

    expect(spar.calls(notes.get)).toEqual([{ id: "a" }, { id: "b" }]);
    expect(spar.calls(notes.count)).toEqual([]);
  });

  test("a declared error reaches its handler with its payload, and a case without one", async () => {
    const spar = fakeSpar();
    spar.on(notes.get).fail("notFound", { id: "gone" });
    spar.on(notes.remove).fail("locked");

    const found = await notes.get({ id: "gone" }).match({ success: () => "found", errors: { notFound: e => `missing ${e.id}`, locked: () => "locked" } });
    const removed = await notes.remove({ id: "a" }).match({ success: () => "removed", errors: { notFound: () => "missing", locked: () => "locked" } });

    expect(found).toBe("missing gone");
    expect(removed).toBe("locked");
  });

  test("a command without a request or a value", async () => {
    const spar = fakeSpar();
    spar.on(notes.count).succeed(3);
    spar.on(notes.remove).succeed();

    expect(await notes.count().unwrap()).toBe(3);
    expect(await notes.remove({ id: "a" }).unwrap()).toBeUndefined();
    expect(spar.calls(notes.count)).toEqual([undefined]);
  });

  test("a scripted failure is a CallError naming the call", async () => {
    const spar = fakeSpar();
    spar.on(notes.get).failWith({ kind: "transport", code: "lost", execution: "unknown" });

    const error = await failureOf(notes.get({ id: "a" }));

    expect(error).toEqual({ kind: "transport", code: "lost", execution: "unknown", contract: "notes", command: "get" });
  });

  test("a hanging call that the page aborts is cancelled, and the backend confirms it stopped", async () => {
    const spar = fakeSpar();
    spar.on(notes.get).hang();
    const abort = new AbortController();

    const call = notes.get({ id: "a" }, { signal: abort.signal });
    await vi.waitFor(() => expect(spar.calls(notes.get)).toHaveLength(1));
    abort.abort();

    expect(await failureOf(call)).toMatchObject({ kind: "cancelled", code: "cancelled", execution: "stopped" });
  });

  test("answer decides from the request and may wait", async () => {
    const spar = fakeSpar();
    let release: () => void = () => {};
    const gate = new Promise<void>(resolve => (release = resolve));
    spar.on(notes.get).answer(async (request, reply) => {
      await gate;
      return request.id === "a" ? reply.succeed({ id: "a", words: 2n }) : reply.fail("notFound", { id: request.id });
    });

    const first = notes.get({ id: "a" }).unwrap();
    const second = notes.get({ id: "b" }).match({ success: () => "found", errors: { notFound: e => e.id, locked: () => "locked" } });
    release();

    expect(await first).toEqual({ id: "a", words: 2n });
    expect(await second).toBe("b");
  });

  test("scripting again replaces the earlier behaviour", async () => {
    const spar = fakeSpar();
    spar.on(notes.count).succeed(1);
    spar.on(notes.count).succeed(2);

    expect(await notes.count().unwrap()).toBe(2);
  });

  test("a command no test scripted is refused, and the console says which", async () => {
    const errors = vi.spyOn(console, "error").mockImplementation(() => {});
    fakeSpar();

    const error = await failureOf(notes.count());

    expect(error).toMatchObject({ kind: "rejected", code: "unknown-command", execution: "notStarted" });
    expect(errors).toHaveBeenCalledWith("fakeSpar: the page called notes.count, which no test scripted.");
  });

  test("a value the writer refuses fails as a real host's would: response / serialization", async () => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    const spar = fakeSpar();
    spar.on(notes.count).succeed(Number.NaN);

    expect(await failureOf(notes.count())).toMatchObject({ kind: "response", code: "serialization", execution: "completed" });
  });

  test("each fake starts from the default failure hooks", async () => {
    const hooked: CallError[] = [];
    configureSpar({ onFailure: error => hooked.push(error) });
    const errors = vi.spyOn(console, "error").mockImplementation(() => {});
    const spar = fakeSpar();
    spar.on(notes.count).failWith({ kind: "failed", code: "failed", execution: "unknown" });

    await notes.count().match({ success: () => {} });

    expect(hooked).toEqual([]);
    expect(errors).toHaveBeenCalledWith("Spar: notes.count failed: failed / failed (unknown)");
  });

  test("something that is not a command is refused at once", () => {
    const spar = fakeSpar();

    expect(() => spar.on(() => Promise.resolve())).toThrow(TypeError);
  });
});

// Never run: each marked line must fail to compile, on TypeScript 6 and 7 (npm run check).
export function scriptsAreTyped(): void {
  const spar = fakeSpar();
  // @ts-expect-error A note has no `title`.
  spar.on(notes.get).succeed({ id: "a", title: "A" });
  // @ts-expect-error `words` is a bigint on the wire.
  spar.on(notes.get).succeed({ id: "a", words: 1 });
  // @ts-expect-error `archived` is not a case of NoteError.
  spar.on(notes.get).fail("archived");
  // @ts-expect-error `notFound` carries an id.
  spar.on(notes.get).fail("notFound");
  // @ts-expect-error `locked` carries nothing.
  spar.on(notes.remove).fail("locked", { id: "a" });
  // @ts-expect-error `count` declares no errors.
  spar.on(notes.count).fail("notFound", { id: "a" });
  // @ts-expect-error `remove` has no value.
  spar.on(notes.remove).succeed({ id: "a", words: 1n });
  // @ts-expect-error `cancel` is not a CallErrorKind; `cancelled` is.
  spar.on(notes.get).failWith({ kind: "cancel", code: "cancelled", execution: "stopped" });
  spar.on(notes.get).answer(request => {
    // @ts-expect-error A request is a NoteRef: it has no `words`.
    void request.words;
    return Promise.reject(new Error("never run"));
  });
  const ids: readonly string[] = spar.calls(notes.get).map(request => request.id);
  void ids;
}

describe("fakeSpar streams", () => {
  /** What a loop over the subscription sees, as text. */
  const seen = (item: Result<Note, NoteError, SubscriptionEvents>): string =>
    item.match({
      success: note => `${note.id} ${note.words}`,
      errors: { notFound: e => `notFound ${e.id}`, locked: () => "locked" },
      events: { reset: () => "reset", gap: e => `gap ${e.dropped}` },
      failure: e => `${e.kind} ${e.code}`,
    }) ?? "?";

  test("values pushed before the page subscribes wait for it, and a gap and the end follow", async () => {
    const spar = fakeSpar();
    const script = spar.stream(notes.changes);
    script.push({ id: "a", words: 1n }, { id: "b", words: 2n });

    const iterator = notes.changes()[Symbol.asyncIterator]();
    const first = [seen((await iterator.next()).value!), seen((await iterator.next()).value!)];
    script.gap(5);
    script.push({ id: "c", words: 3n });
    const second = [seen((await iterator.next()).value!), seen((await iterator.next()).value!)];
    script.fail("locked");
    const last = seen((await iterator.next()).value!);

    expect([...first, ...second, last]).toEqual(["a 1", "b 2", "gap 5", "c 3", "locked"]);
    expect((await iterator.next()).done).toBe(true);
    expect(script.opened).toEqual([undefined]);
  });

  test("leaving the loop is confirmed by the fake backend", async () => {
    const spar = fakeSpar();
    const script = spar.stream(notes.changes);
    const iterator = notes.changes()[Symbol.asyncIterator]();
    script.push({ id: "a", words: 1n });
    await iterator.next();

    await expect(iterator.return!()).resolves.toEqual({ done: true, value: undefined });
  });

  test("a call is not a stream", () => {
    const spar = fakeSpar();

    expect(() => spar.stream(notes.get as never)).toThrow(TypeError);
  });
});
