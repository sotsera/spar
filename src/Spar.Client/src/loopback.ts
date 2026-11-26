// The loopback transport (development and tests; plan section 2c, decision "Loopback transport: one ordered send
// path"). One streamed response per document carries every host message, one per line. The page's own messages go
// out in order, one request at a time, and whatever queues up meanwhile travels together in the next request: ids
// then reach the host in the order they were given, and a long call never holds one of the browser's six connections.
import type { BridgeChannel } from "./connection.js";
import type { RelayAddress } from "./tracing.js";
import { decode } from "./protocol.js";
import { awaitRestart, backendOpened, runHeader, type RestartPage } from "./restart.js";

const tokenKey = "spar.token";

/** Where the loopback host is and how to prove the page came from it. */
export interface LoopbackOptions {
  /** The host's origin; the page's own origin by default. */
  readonly origin?: string;
  /** The bearer token; by default taken from the URL fragment, or from session storage after a reload. */
  readonly token?: string;
  /** For tests: the `fetch` to use. */
  readonly fetch?: typeof fetch;
  /**
   * For tests: what waiting for a dev session's next run uses (`restart.ts`), or `false` never to wait. A page whose
   * channel names its backend's run (a dev session's `X-Spar-Run` header) waits for the next run and reloads.
   */
  readonly restart?: RestartPage | false;
}

/** What `takeToken` needs from the page; the browser's own objects by default. */
export interface TokenPage {
  readonly location: Pick<Location, "hash" | "pathname" | "search">;
  readonly history: Pick<History, "replaceState" | "state">;
  readonly sessionStorage: Pick<Storage, "getItem" | "setItem">;
}

/** The loopback channel for this document. */
export function loopbackChannel(options: LoopbackOptions = {}): BridgeChannel {
  const origin = options.origin ?? location.origin;
  const token = options.token ?? takeToken();
  return new LoopbackChannel(origin, token, options.fetch ?? globalThis.fetch.bind(globalThis), options.restart);
}

/**
 * Moves the token from the URL fragment (`#token=…`) into session storage and out of the address bar and history,
 * where it would otherwise stay visible (plan section 3c). A reload finds it in session storage.
 */
export function takeToken(page: TokenPage = globalThis as unknown as TokenPage): string {
  const fragment = new URLSearchParams(page.location.hash.slice(1));
  const fromUrl = fragment.get("token");
  if (fromUrl !== null) {
    page.sessionStorage.setItem(tokenKey, fromUrl);
    fragment.delete("token");

    const rest = fragment.toString();
    const address = page.location.pathname + page.location.search + (rest === "" ? "" : `#${rest}`);
    page.history.replaceState(page.history.state, "", address);
    return fromUrl;
  }

  const stored = page.sessionStorage.getItem(tokenKey);
  if (stored === null) {
    throw new Error("No loopback token: open the page from the address the host gave, which carries #token=.");
  }

  return stored;
}

class LoopbackChannel implements BridgeChannel {
  readonly #origin: string;
  readonly #token: string;
  readonly #fetch: typeof fetch;
  readonly #stop = new AbortController();
  readonly #queue: string[] = [];
  #received: ((message: string) => void) | undefined;
  #closedListener: (() => void) | undefined;
  #opening = false;
  #posting = false;
  #closed = false;
  readonly #restart: RestartPage | false | undefined;
  #doc: string | undefined;
  #run: string | undefined;
  #opened = false;

  constructor(origin: string, token: string, fetchFunction: typeof fetch, restart: RestartPage | false | undefined) {
    this.#origin = origin;
    this.#token = token;
    this.#fetch = fetchFunction;
    this.#restart = restart;
  }

  listen(received: (message: string) => void, closed: () => void): void {
    this.#received = received;
    this.#closedListener = closed;
  }

  /** @internal Where the page's telemetry reaches the host's relay: the host's origin, with the session's token. */
  telemetryRelay(): RelayAddress {
    return { origin: this.#origin, headers: { Authorization: `Bearer ${this.#token}` } };
  }

  send(message: string): void {
    if (this.#closed) {
      return;
    }

    // The first message is the hello, which opens the document's channel.
    if (!this.#opening) {
      this.#opening = true;
      void this.#open(message);
      return;
    }

    this.#queue.push(message);
    void this.#post();
  }

  async #open(hello: string): Promise<void> {
    let response: Response;
    try {
      response = await this.#fetch(`${this.#origin}/_spar/channel`, this.#request(hello, {}));
    } catch {
      this.#close();
      return;
    }

    if (!response.ok || response.body === null) {
      this.#close();
      return;
    }

    this.#run = response.headers.get(runHeader) ?? undefined;

    await this.#read(response.body);
    this.#close();
  }

  /** Reads the channel until it ends: one host message per line. */
  async #read(body: ReadableStream<Uint8Array>): Promise<void> {
    const reader = body.getReader();
    const text = new TextDecoder();
    let buffered = "";
    for (;;) {
      let chunk: ReadableStreamReadResult<Uint8Array>;
      try {
        chunk = await reader.read();
      } catch {
        return;
      }

      if (chunk.done) {
        return;
      }

      // In streaming mode a character split across two chunks is kept until its second half arrives.
      buffered += text.decode(chunk.value, { stream: true });
      let newline = buffered.indexOf("\n");
      while (newline >= 0) {
        this.#deliver(buffered.slice(0, newline));
        buffered = buffered.slice(newline + 1);
        newline = buffered.indexOf("\n");
      }
    }
  }

  #deliver(message: string): void {
    if (this.#closed) {
      return;
    }

    // The host's first message is open, which names the document every later request must carry.
    if (this.#doc === undefined) {
      const decoded = decode(message);
      if (decoded.ok && decoded.message.type === "open") {
        this.#doc = decoded.message.doc;
        this.#opened = true;
        if (this.#run !== undefined && this.#restart !== false) {
          // The page got its backend: an automatic reload that brought it here succeeded (restart.ts).
          backendOpened(this.#restart);
        }

        void this.#post();
      }
    }

    this.#received?.(message);
  }

  /** Sends what is queued, one request at a time, so the host receives messages in the order they were sent. */
  async #post(): Promise<void> {
    if (this.#posting || this.#doc === undefined) {
      return;
    }

    this.#posting = true;
    while (this.#queue.length > 0 && !this.#closed) {
      const batch = takeBatch(this.#queue).join("\n");
      const delivered = await this.#postBatch(batch, this.#doc);
      if (!delivered) {
        // The host may or may not have run what this request carried; the connection reports it as lost.
        this.#close();
      }
    }

    this.#posting = false;
  }

  async #postBatch(batch: string, doc: string): Promise<boolean> {
    try {
      const response = await this.#fetch(`${this.#origin}/_spar/send`, this.#request(batch, { "X-Spar-Doc": doc }));
      return response.ok;
    } catch {
      return false;
    }
  }

  #request(body: string, headers: Record<string, string>): RequestInit {
    return {
      method: "POST",
      headers: { Authorization: `Bearer ${this.#token}`, "Content-Type": "text/plain;charset=UTF-8", ...headers },
      body,
      cache: "no-store",
      signal: this.#stop.signal,
    };
  }

  #close(): void {
    if (this.#closed) {
      return;
    }

    this.#closed = true;
    this.#queue.length = 0;
    this.#stop.abort();
    this.#closedListener?.();

    // A dev session's backend went away: wait for its next run and reload. restart.ts decides: a document that got its
    // backend always waits; one that never did waits only when an automatic reload made it, and that reload counts
    // against the loop guard; a channel that names no run (the test host) never waits.
    if (this.#restart !== false) {
      const lost = { origin: this.#origin, token: this.#token, run: this.#run, opened: this.#opened };
      void (this.#restart === undefined ? awaitRestart(lost) : awaitRestart(lost, this.#restart));
    }
  }
}

/**
 * The most text one request carries. The host's server refuses a body far larger than the bridge's message limit,
 * which would end the channel; a message longer than this travels alone, and the host answers it by its id.
 */
const batchCharacters = 1024 * 1024;

/** Takes the first messages of the queue that fit in one request, and always at least one. */
export function takeBatch(queue: string[]): string[] {
  let length = queue[0]!.length;
  let count = 1;
  while (count < queue.length && length + 1 + queue[count]!.length <= batchCharacters) {
    length += 1 + queue[count]!.length;
    count++;
  }

  return queue.splice(0, count);
}
