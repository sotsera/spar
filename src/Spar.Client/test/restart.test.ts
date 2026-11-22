// A loopback dev session's restarts: the page waits for the backend's next run, reloads once, and never loops.
import { describe, expect, test } from "vitest";
import { Connection } from "../src/connection.js";
import { loopbackChannel } from "../src/loopback.js";
import { awaitRestart, delayFor, type LostBackend, type RestartPage } from "../src/restart.js";

const open = "{\"type\":\"open\",\"protocol\":1,\"doc\":\"d1\",\"contracts\":\"0123456789abcdef\"}\t";

/** One scripted answer of `POST /_spar/session`: a status and body, or a refused connection. */
type Probe = { readonly status: number; readonly body?: string } | "down";

/** A page whose clock moves only when it waits, and whose probes answer from a script. */
class FakePage {
  readonly storage = new Map<string, string>();
  readonly probes: Array<{ readonly url: string; readonly authorization: string }> = [];
  readonly waits: number[] = [];
  readonly lines: string[] = [];
  reloads = 0;
  time = 1_000_000;
  readonly answers: Probe[];

  constructor(answers: Probe[]) {
    this.answers = answers;
  }

  readonly page: RestartPage = {
    reload: () => this.reloads++,
    sessionStorage: {
      getItem: key => this.storage.get(key) ?? null,
      setItem: (key, value) => this.storage.set(key, value),
    },
    now: () => this.time,
    wait: async ms => {
      this.waits.push(ms);
      this.time += ms;
    },
    fetch: (async (input: string | URL | Request, init?: RequestInit) => {
      const headers = init?.headers as Record<string, string>;
      this.probes.push({ url: String(input), authorization: headers["Authorization"]! });
      const answer = this.answers.shift() ?? "down";
      if (answer === "down") {
        throw new TypeError("Failed to fetch");
      }

      return new Response(answer.body ?? null, { status: answer.status });
    }) as typeof fetch,
    log: {
      info: (line: string) => this.lines.push(`info ${line}`),
      warn: (line: string) => this.lines.push(`warn ${line}`),
      error: (line: string) => this.lines.push(`error ${line}`),
    },
  };
}

function lost(page: FakePage, livedMs = 60_000): LostBackend {
  return { origin: "http://127.0.0.1:5000", token: "t0k", run: "run-1", openedAt: page.time - livedMs };
}

describe("waiting for the next run", () => {
  test("the page looks with its token until a new run answers, then reloads once", async () => {
    const fake = new FakePage(["down", { status: 503 }, "down", { status: 200, body: "run-2" }]);

    const outcome = await awaitRestart(lost(fake), fake.page);

    expect(outcome).toBe("reloaded");
    expect(fake.reloads).toBe(1);
    expect(fake.probes).toHaveLength(4);
    expect(fake.probes.every(p => p.url === "http://127.0.0.1:5000/_spar/session" && p.authorization === "Bearer t0k")).toBe(true);
    expect(fake.waits).toEqual([250, 500, 1000, 1000]);
  });

  test("the same run still answering means the channel ended for another reason: no reload", async () => {
    const fake = new FakePage([{ status: 200, body: "run-1" }]);

    expect(await awaitRestart(lost(fake), fake.page)).toBe("same-run");
    expect(fake.reloads).toBe(0);
  });

  test("a refused token is a new dev session: the page stops and says so", async () => {
    const fake = new FakePage(["down", { status: 401 }]);

    expect(await awaitRestart(lost(fake), fake.page)).toBe("refused");
    expect(fake.reloads).toBe(0);
    expect(fake.lines.at(-1)).toContain("open the address it printed");
  });

  test("something that is not the app's backend is not waited for", async () => {
    const fake = new FakePage([{ status: 404 }]);

    expect(await awaitRestart(lost(fake), fake.page)).toBe("foreign");
    expect(fake.reloads).toBe(0);
  });

  test("a backend that keeps stopping soon after the page connects stops the reloads after three in a row", async () => {
    const fake = new FakePage([]);
    const outcomes: string[] = [];
    for (let round = 0; round < 4; round++) {
      // Each reloaded document loses its backend 1 s after it connected, and a new run answers at once.
      fake.answers.push({ status: 200, body: `run-${round + 2}` });
      outcomes.push(await awaitRestart(lost(fake, 1_000), fake.page));
    }

    expect(fake.reloads).toBe(3);
    expect(outcomes).toEqual(["reloaded", "reloaded", "reloaded", "too-many"]);
  });

  test("a document that stayed connected breaks a run of short reloads", async () => {
    const fake = new FakePage([{ status: 200, body: "run-2" }]);
    fake.storage.set("spar.restart.reloads", "3");

    expect(await awaitRestart(lost(fake, 5_000), fake.page)).toBe("reloaded");
    expect(fake.storage.get("spar.restart.reloads")).toBe("1");
  });

  test("after a minute of waiting it looks every 5 s", () => {
    expect([0, 1, 2, 3, 9].map(attempt => delayFor(attempt, 0))).toEqual([250, 500, 1000, 1000, 1000]);
    expect(delayFor(40, 60_000)).toBe(5_000);
  });
});

describe("the loopback channel of a dev session", () => {
  /** A fetch whose channel names a run and ends when the test says, and which answers sends and probes. */
  function scripted(run: string | null) {
    let controller: ReadableStreamDefaultController<Uint8Array> | undefined;
    const sends: string[] = [];
    const fetchFunction = (async (input: string | URL | Request, init?: RequestInit): Promise<Response> => {
      const url = String(input);
      if (url.endsWith("/_spar/channel")) {
        const stream = new ReadableStream<Uint8Array>({ start: c => (controller = c) });
        return new Response(stream, { status: 200, headers: run === null ? {} : { "X-Spar-Run": run } });
      }

      sends.push(String(init?.body));
      return new Response(null, { status: 204 });
    }) as typeof fetch;

    return {
      fetch: fetchFunction,
      sends,
      write: (line: string) => controller!.enqueue(new TextEncoder().encode(`${line}\n`)),
      end: () => controller!.close(),
    };
  }

  const settle = (): Promise<void> => new Promise(resolve => setTimeout(resolve, 0));

  test("a call in flight when the backend stops ends lost, nothing is sent again, and the page reloads once", async () => {
    const host = scripted("run-1");
    const fake = new FakePage(["down", { status: 200, body: "run-2" }]);
    const channel = loopbackChannel({ origin: "http://127.0.0.1:5000", token: "t0k", fetch: host.fetch, restart: fake.page });
    const connection = new Connection(channel, "0123456789abcdef");
    await settle();
    host.write(open);
    await settle();

    const outcome = connection.invoke("pages", "list", "", { value: json => json, error: () => undefined });
    await settle();
    host.end();
    await settle();
    await settle();

    expect(await outcome).toEqual({
      ok: false,
      error: { kind: "transport", code: "lost", execution: "unknown", contract: "pages", command: "list" },
    });
    expect(fake.reloads).toBe(1);
    expect(host.sends).toHaveLength(1);
  });

  test("a channel that names no run (the test host) never waits", async () => {
    const host = scripted(null);
    const fake = new FakePage([{ status: 200, body: "run-2" }]);
    const channel = loopbackChannel({ origin: "http://127.0.0.1:5000", token: "t0k", fetch: host.fetch, restart: fake.page });
    new Connection(channel, "0123456789abcdef");
    await settle();
    host.write(open);
    await settle();
    host.end();
    await settle();

    expect(fake.probes).toHaveLength(0);
    expect(fake.reloads).toBe(0);
  });

  test("a document that never received open never reloads", async () => {
    const host = scripted("run-1");
    const fake = new FakePage([{ status: 200, body: "run-2" }]);
    const channel = loopbackChannel({ origin: "http://127.0.0.1:5000", token: "t0k", fetch: host.fetch, restart: fake.page });
    new Connection(channel, "0123456789abcdef");
    await settle();
    host.end();
    await settle();

    expect(fake.probes).toHaveLength(0);
  });
});
