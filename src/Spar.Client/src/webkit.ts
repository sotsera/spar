// The WebKitGTK transport (plan section 2c and section 3, "Origin policy"): the Linux host's page talks to the bridge
// relay that the host injects into the top document's isolated script world. Neither world can reach the other's
// objects, so they meet on the top window as DOM events, with one message string as each event's `detail`: the page
// dispatches `spar:send`, and the relay dispatches `spar:receive` with the host's messages. Strings only, both ways,
// in order. The relay runs on the app's origin only, so the page is on it exactly when its protocol is `spar:`.
import type { BridgeChannel } from "./connection.js";

/** The event the page dispatches with each message for the host. */
export const sendEvent = "spar:send";

/** The event the relay dispatches with each message from the host. */
export const receiveEvent = "spar:receive";

/** The part of the top window the transport uses. */
export interface RelayTarget {
  dispatchEvent(event: Event): boolean;
  addEventListener(type: string, listener: (event: Event) => void): void;
}

/** What `webKitChannel` needs; the page's own window by default. */
export interface WebKitOptions {
  /** For tests: the target to use instead of the page's window. */
  readonly target?: RelayTarget;
}

/** Whether the page runs in the Linux host: its documents are served on `spar://app`. */
export function isWebKitHost(): boolean {
  const location = (globalThis as { readonly location?: { readonly protocol?: string } }).location;
  return location?.protocol === "spar:";
}

/** The WebKitGTK channel for this document. Throws when the page does not run in the Linux host. */
export function webKitChannel(options: WebKitOptions = {}): BridgeChannel {
  const target = options.target ?? (isWebKitHost() ? (globalThis as unknown as RelayTarget) : undefined);
  if (target === undefined) {
    throw new Error("Spar: this page does not run in the Linux host (its address is not on spar://app).");
  }

  return new WebKitChannel(target);
}

class WebKitChannel implements BridgeChannel {
  readonly #target: RelayTarget;

  constructor(target: RelayTarget) {
    this.#target = target;
  }

  send(message: string): void {
    this.#target.dispatchEvent(new CustomEvent(sendEvent, { detail: message }));
  }

  /**
   * The channel never closes on its own: the host ends a document's bridge when the page navigates away or the window
   * closes, and the document goes with it.
   */
  listen(received: (message: string) => void): void {
    this.#target.addEventListener(receiveEvent, event => {
      // Only strings are bridge messages; anything else was not the relay's.
      const detail = (event as CustomEvent<unknown>).detail;
      if (typeof detail === "string") {
        received(detail);
      }
    });
  }
}
