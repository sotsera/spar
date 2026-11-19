// The WebView2 transport (plan section 2c): the page posts each message as one string through
// `window.chrome.webview.postMessage`, and the host's messages, posted with `PostWebMessageAsString`, arrive as the
// `data` of its `message` events. Strings only, both ways, never structured messages. Delivery is in order, the
// host checks that each message comes from the app's own origin and top frame, and it assigns the document.
import type { BridgeChannel } from "./connection.js";
import { loopbackChannel, type LoopbackOptions } from "./loopback.js";

/** The part of WebView2's `window.chrome.webview` the transport uses. */
export interface WebViewPort {
  postMessage(message: string): void;
  addEventListener(type: "message", listener: (event: { readonly data: unknown }) => void): void;
}

/** What `webViewChannel` needs; the page's own `window.chrome.webview` by default. */
export interface WebViewOptions {
  /** For tests: the port to use instead of `window.chrome.webview`. */
  readonly port?: WebViewPort;
}

/** WebView2's port when the page runs inside a WebView2 host, otherwise undefined. */
export function webViewPort(): WebViewPort | undefined {
  const chrome = (globalThis as { readonly chrome?: { readonly webview?: WebViewPort } }).chrome;
  return chrome?.webview;
}

/** The WebView2 channel for this document. Throws when the page does not run inside WebView2. */
export function webViewChannel(options: WebViewOptions = {}): BridgeChannel {
  const port = options.port ?? webViewPort();
  if (port === undefined) {
    throw new Error("Spar: this page does not run inside WebView2 (window.chrome.webview is missing).");
  }

  return new WebViewChannel(port);
}

/**
 * The host's channel for this document: WebView2's when the page runs in the Windows host, otherwise the loopback one
 * (browser tests, and the page opened from the loopback host's address).
 */
export function hostChannel(options: LoopbackOptions = {}): BridgeChannel {
  return webViewPort() === undefined ? loopbackChannel(options) : webViewChannel();
}

class WebViewChannel implements BridgeChannel {
  readonly #port: WebViewPort;

  constructor(port: WebViewPort) {
    this.#port = port;
  }

  send(message: string): void {
    this.#port.postMessage(message);
  }

  /**
   * The channel never closes on its own: the host ends a document's bridge when the page navigates away or the window
   * closes, and the document goes with it.
   */
  listen(received: (message: string) => void): void {
    this.#port.addEventListener("message", event => {
      // Only the host posts to this port, and only strings; anything else is not a bridge message.
      if (typeof event.data === "string") {
        received(event.data);
      }
    });
  }
}
