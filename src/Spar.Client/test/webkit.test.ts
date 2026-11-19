// The WebKitGTK transport against a fake top window, the Linux host's relay (src/Spar.Linux/BridgeRelay.js) against a
// fake script message handler, and the two together the way the host runs them: the page's world and the relay's world
// sharing only the window's events.
import { readFileSync } from "node:fs";
import { afterEach, describe, expect, test } from "vitest";
import { Connection } from "../src/connection.js";
import { loopbackChannel } from "../src/loopback.js";
import { receiveEvent, sendEvent, isWebKitHost, webKitChannel } from "../src/webkit.js";
import { hostChannel } from "../src/webview.js";

const relaySource = readFileSync(new URL("../../Spar.Linux/BridgeRelay.js", import.meta.url), "utf8");

interface Relay {
  deliver(messages: readonly string[]): void;
}

/** A fake script message handler: what the relay posts to the host. */
class FakeHandler {
  readonly posted: unknown[] = [];

  postMessage(message: unknown): void {
    this.posted.push(message);
  }
}

/**
 * Runs the relay's source as WebKit does at document start, in its own scope, with the given window and location, and
 * returns what it left in that scope (`sparRelay`). No code is built from page data: the source is the host's file.
 */
function runRelay(window: EventTarget & { webkit?: unknown }, location: { protocol: string; host: string }): Relay | undefined {
  const run = new Function("window", "location", "CustomEvent", `${relaySource}\nreturn sparRelay;`) as (
    window: unknown,
    location: unknown,
    customEvent: typeof CustomEvent,
  ) => Relay | undefined;
  return run(window, location, CustomEvent);
}

/** The top window both worlds share, with the handler only the relay's world sees. */
function topWindow(handler: FakeHandler | undefined): EventTarget & { webkit?: unknown } {
  const window = new EventTarget() as EventTarget & { webkit?: unknown };
  if (handler !== undefined) {
    window.webkit = { messageHandlers: { spar: handler } };
  }

  return window;
}

const app = { protocol: "spar:", host: "app" };
const page = globalThis as { location?: unknown };

afterEach(() => {
  delete page.location;
});

describe("the WebKitGTK channel", () => {
  test("each message is dispatched as spar:send with the string as its detail, in order", () => {
    const target = new EventTarget();
    const sent: unknown[] = [];
    target.addEventListener(sendEvent, event => sent.push((event as CustomEvent).detail));

    const channel = webKitChannel({ target });
    channel.send("{\"type\":\"hello\"}\t");
    channel.send("{\"type\":\"call\",\"id\":1}\t{}");

    expect(sent).toEqual(["{\"type\":\"hello\"}\t", "{\"type\":\"call\",\"id\":1}\t{}"]);
  });

  test("host messages arrive as spar:receive; anything but a string is ignored", () => {
    const target = new EventTarget();
    const received: string[] = [];
    let closed = 0;
    webKitChannel({ target }).listen(message => received.push(message), () => closed++);

    target.dispatchEvent(new CustomEvent(receiveEvent, { detail: "{\"type\":\"open\"}\t" }));
    target.dispatchEvent(new CustomEvent(receiveEvent, { detail: { type: "open" } }));
    target.dispatchEvent(new CustomEvent(receiveEvent, { detail: 42 }));
    target.dispatchEvent(new CustomEvent(receiveEvent, { detail: "second" }));

    expect(received).toEqual(["{\"type\":\"open\"}\t", "second"]);
    expect(closed).toBe(0);
  });

  test("outside the Linux host it refuses to start, naming what is missing", () => {
    expect(isWebKitHost()).toBe(false);
    expect(() => webKitChannel()).toThrow(/spar:\/\/app/);
  });
});

describe("the host's channel on Linux", () => {
  test("on spar://app it is the relay's channel", () => {
    page.location = { protocol: "spar:", host: "app" };

    expect(isWebKitHost()).toBe(true);
    expect(hostChannel().constructor).toBe(webKitChannel({ target: new EventTarget() }).constructor);
  });

  test("on any other protocol it is the loopback channel", () => {
    page.location = { protocol: "http:", host: "127.0.0.1:5000" };
    const options = { origin: "http://127.0.0.1:5000", token: "t0k" };

    expect(hostChannel(options).constructor).toBe(loopbackChannel(options).constructor);
  });
});

describe("the relay", () => {
  test("posts each spar:send string to the handler, in order, and nothing else", () => {
    const handler = new FakeHandler();
    const window = topWindow(handler);
    runRelay(window, app);

    window.dispatchEvent(new CustomEvent(sendEvent, { detail: "one" }));
    window.dispatchEvent(new CustomEvent(sendEvent, { detail: { not: "a string" } }));
    window.dispatchEvent(new CustomEvent(sendEvent, { detail: "two" }));
    window.dispatchEvent(new CustomEvent("message", { detail: "a postMessage is not a bridge message" }));

    expect(handler.posted).toEqual(["one", "two"]);
  });

  test("delivers the host's messages as spar:receive events, in order", () => {
    const window = topWindow(new FakeHandler());
    const relay = runRelay(window, app);
    const received: unknown[] = [];
    window.addEventListener(receiveEvent, event => received.push((event as CustomEvent).detail));

    relay?.deliver(["{\"type\":\"open\"}\t", "{\"type\":\"result\",\"id\":1,\"ok\":true}\t\"hi\""]);

    expect(received).toEqual(["{\"type\":\"open\"}\t", "{\"type\":\"result\",\"id\":1,\"ok\":true}\t\"hi\""]);
  });

  test("does nothing outside the app's origin, or where its world has no handler", () => {
    for (const [location, handler] of [
      [{ protocol: "https:", host: "example.com" }, new FakeHandler()],
      [{ protocol: "spar:", host: "elsewhere" }, new FakeHandler()],
      [app, undefined],
    ] as const) {
      const window = topWindow(handler);
      const relay = runRelay(window, location);
      window.dispatchEvent(new CustomEvent(sendEvent, { detail: "call" }));

      expect(relay).toBeUndefined();
      expect(handler?.posted ?? []).toEqual([]);
    }
  });

  test("cannot be changed by the page: what it returns is frozen", () => {
    const relay = runRelay(topWindow(new FakeHandler()), app);

    expect(Object.isFrozen(relay)).toBe(true);
  });

  test("carries a connection's hello, open and a call's result between the page and the host", async () => {
    const handler = new FakeHandler();
    const window = topWindow(handler);
    const relay = runRelay(window, app);
    const connection = new Connection(webKitChannel({ target: window }), "0123456789abcdef");

    expect(handler.posted).toHaveLength(1);
    expect(String(handler.posted[0])).toMatch(/^\{"type":"hello"/);

    relay?.deliver(["{\"type\":\"open\",\"protocol\":1,\"doc\":\"d1\",\"contracts\":\"0123456789abcdef\"}\t"]);
    const outcome = connection.invoke("host", "echo", "\"hi\"", { value: json => JSON.parse(json) as unknown, error: () => null });
    expect(String(handler.posted[1])).toMatch(/^\{"type":"call","id":1,"contract":"host","command":"echo"\}\t"hi"$/);

    relay?.deliver(["{\"type\":\"result\",\"id\":1,\"ok\":true}\t\"hi\""]);
    expect(await outcome).toEqual({ ok: true, value: "hi" });
    expect(connection.document).toBe("d1");
  });
});
