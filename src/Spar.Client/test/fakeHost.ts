// A scripted host for tests: records what the page sends and lets the test answer, as a host would.
import type { BridgeChannel } from "../src/connection.js";
import { decode, encode, type BridgeMessage } from "../src/protocol.js";

export class FakeHost implements BridgeChannel {
  readonly sent: BridgeMessage[] = [];
  #received: ((message: string) => void) | undefined;
  #closed: (() => void) | undefined;

  send(message: string): void {
    const decoded = decode(message);
    if (!decoded.ok) {
      throw new Error(`The page sent a malformed message: ${decoded.reason}`);
    }

    this.sent.push(decoded.message);
  }

  listen(received: (message: string) => void, closed: () => void): void {
    this.#received = received;
    this.#closed = closed;
  }

  /** The last message of a type the page sent. */
  last(type: BridgeMessage["type"]): BridgeMessage | undefined {
    return [...this.sent].reverse().find(message => message.type === type);
  }

  deliver(message: BridgeMessage): void {
    this.#received!(encode(message));
  }

  deliverText(text: string): void {
    this.#received!(text);
  }

  open(contracts = "0123456789abcdef"): void {
    this.deliver({ type: "open", protocol: 1, doc: "doc-1", contracts, payload: "" });
  }

  close(): void {
    this.#closed!();
  }
}
