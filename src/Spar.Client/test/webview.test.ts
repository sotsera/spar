// The WebView2 transport against a fake port, and how the host's channel is chosen.
import { afterEach, describe, expect, test } from "vitest";
import { loopbackChannel } from "../src/loopback.js";
import { hostChannel, webViewChannel, webViewPort, type WebViewPort } from "../src/webview.js";

/** A port that records what the page posts and lets the test raise message events. */
class FakePort implements WebViewPort {
  readonly posted: string[] = [];
  readonly #listeners: Array<(event: { readonly data: unknown }) => void> = [];

  postMessage(message: string): void {
    this.posted.push(message);
  }

  addEventListener(type: "message", listener: (event: { readonly data: unknown }) => void): void {
    expect(type).toBe("message");
    this.#listeners.push(listener);
  }

  raise(data: unknown): void {
    for (const listener of this.#listeners) {
      listener({ data });
    }
  }
}

const page = globalThis as { chrome?: { webview?: WebViewPort } };

afterEach(() => {
  delete page.chrome;
});

describe("the WebView2 channel", () => {
  test("each message is posted as one string, in order", () => {
    const port = new FakePort();
    const channel = webViewChannel({ port });

    channel.send("{\"type\":\"hello\"}\t");
    channel.send("{\"type\":\"call\",\"id\":1}\t{}");

    expect(port.posted).toEqual(["{\"type\":\"hello\"}\t", "{\"type\":\"call\",\"id\":1}\t{}"]);
  });

  test("host messages arrive as the data of message events; anything but a string is ignored", () => {
    const port = new FakePort();
    const received: string[] = [];
    let closed = 0;
    webViewChannel({ port }).listen(message => received.push(message), () => closed++);

    port.raise("{\"type\":\"open\"}\t");
    port.raise({ type: "open" });
    port.raise(42);
    port.raise("second");

    expect(received).toEqual(["{\"type\":\"open\"}\t", "second"]);
    expect(closed).toBe(0);
  });

  test("outside WebView2 it refuses to start, naming what is missing", () => {
    expect(webViewPort()).toBeUndefined();
    expect(() => webViewChannel()).toThrow(/window\.chrome\.webview/);
  });
});

describe("the host's channel", () => {
  test("inside WebView2 it is the page's own chrome.webview", () => {
    const port = new FakePort();
    page.chrome = { webview: port };

    hostChannel().send("hello");

    expect(webViewPort()).toBe(port);
    expect(port.posted).toEqual(["hello"]);
  });

  test("elsewhere it is the loopback channel", () => {
    const options = { origin: "http://127.0.0.1:5000", token: "t0k" };
    const channel = hostChannel(options);

    expect(channel.constructor).toBe(loopbackChannel(options).constructor);
  });
});
