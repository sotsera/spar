// The loopback transport against a scripted fetch: the channel, the ordered sends, and losing the host.
import { describe, expect, test } from "vitest";
import { loopbackChannel, takeToken, type TokenPage } from "../src/loopback.js";

const open = "{\"type\":\"open\",\"protocol\":1,\"doc\":\"d1\",\"contracts\":\"0123456789abcdef\"}\t";

interface Sent {
  readonly url: string;
  readonly headers: Record<string, string>;
  readonly body: string;
  readonly answer: (status: number) => void;
}

/** A fetch that holds every send until the test answers it, and gives the channel a stream the test writes to. */
class ScriptedFetch {
  readonly sends: Sent[] = [];
  channelHeaders: Record<string, string> = {};
  channelStatus = 200;
  #stream: ReadableStreamDefaultController<Uint8Array> | undefined;

  readonly fetch = (async (input: string | URL | Request, init?: RequestInit): Promise<Response> => {
    const url = String(input);
    const headers = init?.headers as Record<string, string>;
    const body = String(init?.body);
    if (url.endsWith("/_spar/channel")) {
      this.channelHeaders = headers;
      const stream = new ReadableStream<Uint8Array>({ start: controller => (this.#stream = controller) });
      return new Response(stream, { status: this.channelStatus });
    }

    return new Promise<Response>(resolve => {
      this.sends.push({ url, headers, body, answer: status => resolve(new Response(null, { status })) });
    });
  }) as typeof fetch;

  write(line: string): void {
    this.#stream!.enqueue(new TextEncoder().encode(`${line}\n`));
  }

  end(): void {
    this.#stream!.close();
  }
}

/** Lets pending promise callbacks and stream reads run. */
const settle = (): Promise<void> => new Promise(resolve => setTimeout(resolve, 0));

function connect(scripted: ScriptedFetch) {
  const received: string[] = [];
  let closed = 0;
  const channel = loopbackChannel({ origin: "http://127.0.0.1:5000", token: "t0k", fetch: scripted.fetch });
  channel.listen(message => received.push(message), () => closed++);
  return { channel, received, closed: () => closed };
}

describe("the channel", () => {
  test("hello opens it with the token, and host messages arrive one per line", async () => {
    const scripted = new ScriptedFetch();
    const { channel, received } = connect(scripted);

    channel.send("{\"type\":\"hello\"}\t");
    await settle();
    scripted.write(open);
    scripted.write("{\"type\":\"event\",\"name\":\"backend-updated\"}\t{\"a\":\"\u2028\"}");
    await settle();

    expect(scripted.channelHeaders["Authorization"]).toBe("Bearer t0k");
    expect(received).toEqual([open, "{\"type\":\"event\",\"name\":\"backend-updated\"}\t{\"a\":\"\u2028\"}"]);
  });

  test("its end closes the channel once", async () => {
    const scripted = new ScriptedFetch();
    const { channel, closed } = connect(scripted);

    channel.send("{\"type\":\"hello\"}\t");
    await settle();
    scripted.end();
    await settle();
    channel.send("{\"type\":\"ready\"}\t");
    await settle();

    expect(closed()).toBe(1);
    expect(scripted.sends).toHaveLength(0);
  });

  test("a refused channel closes at once", async () => {
    const scripted = new ScriptedFetch();
    scripted.channelStatus = 401;
    const { channel, closed } = connect(scripted);

    channel.send("{\"type\":\"hello\"}\t");
    await settle();

    expect(closed()).toBe(1);
  });
});

describe("sending", () => {
  async function opened() {
    const scripted = new ScriptedFetch();
    const connection = connect(scripted);
    connection.channel.send("{\"type\":\"hello\"}\t");
    await settle();
    return { scripted, ...connection };
  }

  test("messages sent before open wait for it, then go in one request naming the document", async () => {
    const { scripted, channel } = await opened();

    channel.send("call 1");
    channel.send("call 2");
    await settle();
    expect(scripted.sends).toHaveLength(0);
    scripted.write(open);
    await settle();

    expect(scripted.sends.map(s => [s.headers["X-Spar-Doc"], s.body])).toEqual([["d1", "call 1\ncall 2"]]);
  });

  test("one request at a time: what queues meanwhile goes next, together and in order", async () => {
    const { scripted, channel } = await opened();
    scripted.write(open);
    await settle();

    channel.send("call 1");
    await settle();
    channel.send("call 2");
    channel.send("cancel 1");
    await settle();
    expect(scripted.sends.map(s => s.body)).toEqual(["call 1"]);

    scripted.sends[0]!.answer(204);
    await settle();

    expect(scripted.sends.map(s => s.body)).toEqual(["call 1", "call 2\ncancel 1"]);
  });

  test("a refused send closes the channel, and nothing more is sent", async () => {
    const { scripted, channel, closed } = await opened();
    scripted.write(open);
    await settle();

    channel.send("call 1");
    await settle();
    scripted.sends[0]!.answer(404);
    await settle();
    channel.send("call 2");
    await settle();

    expect(closed()).toBe(1);
    expect(scripted.sends).toHaveLength(1);
  });
});

describe("the token", () => {
  function page(hash: string, stored: string | null = null) {
    const storage = new Map<string, string>();
    if (stored !== null) {
      storage.set("spar.token", stored);
    }

    const addresses: string[] = [];
    const fake: TokenPage = {
      location: { hash, pathname: "/notes", search: "?view=all" },
      history: { state: null, replaceState: (_state, _unused, url) => addresses.push(String(url)) },
      sessionStorage: { getItem: key => storage.get(key) ?? null, setItem: (key, value) => storage.set(key, value) },
    };
    return { fake, storage, addresses };
  }

  test("it moves from the fragment to session storage and leaves the address", () => {
    const { fake, storage, addresses } = page("#token=abc&tab=2");

    expect(takeToken(fake)).toBe("abc");
    expect(storage.get("spar.token")).toBe("abc");
    expect(addresses).toEqual(["/notes?view=all#tab=2"]);
  });

  test("after a reload it comes from session storage", () => {
    const { fake, addresses } = page("", "abc");

    expect(takeToken(fake)).toBe("abc");
    expect(addresses).toEqual([]);
  });

  test("without one the page is told where to get it", () => {
    expect(() => takeToken(page("").fake)).toThrow(/#token=/);
  });
});
