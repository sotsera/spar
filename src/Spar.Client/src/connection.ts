// One document's side of the bridge (plan section 2c): hello and open, increasing ids, one terminal per call,
// aborts confirmed by the backend, and a lost channel turned into the decided failures. Never retries a call.
import { decode, encode, protocolVersion, type BridgeMessage } from "./protocol.js";
import type { CallError, CallErrorKind, CallName, CallOptions, Execution, Outcome } from "./results.js";
import { finishSplash, hasSplash } from "./splash.js";
import type { StreamHandle, StreamHost, StreamReceiver } from "./streams.js";
import { traceCall, type CallSpan } from "./tracing.js";
import { runCloseGuards, type CloseRequest, type GuardsOutcome } from "./windows.js";

/** The host's channel for one document: strings both ways, never structured messages. */
export interface BridgeChannel {
  send(message: string): void;
  /** Registers the listeners. `closed` runs when the channel can deliver nothing more (the host went away). */
  listen(received: (message: string) => void, closed: () => void): void;
}

/** Returned by a decoder for a declared error code this client does not know (a newer backend). */
export const unknownErrorCase: unique symbol = Symbol("spar.unknownErrorCase");

/** How a generated module reads one command's payloads; both build their objects field by field. */
export interface Decoder<T> {
  /** Reads the value's JSON; the text is empty for a command without a value. */
  readonly value: (json: string) => T;
  /** Reads a declared error's parsed payload, or returns `unknownErrorCase`. */
  readonly error: (code: string, payload: Readonly<Record<string, unknown>>) => unknown;
}

/** How long an aborted call waits for the backend to confirm it stopped before its outcome is unknown. */
const confirmationTimeoutMs = 5_000;

/**
 * The host-to-page types this SDK understands, announced in hello. The heartbeat's `ping` is not among them: the desktop
 * host's injected script answers it from the document's creation, before this SDK connects (plan section 3, "Hung
 * pages"), and this connection ignores it. `close-request` and `close-ended` run the page's close guards (decision
 * "Two-stage close"); a host sends them only to a page that lists them.
 */
const understoodTypes = ["open", "result", "items", "end", "failure", "event", "closing", "close-request", "close-ended"] as const;

interface Pending {
  readonly name: CallName;
  readonly decoder: Decoder<unknown>;
  readonly resolve: (outcome: Outcome) => void;
  sent: boolean;
  cleanup: () => void;
}

/** An open stream: where its messages go, and whether the host has its stream message. */
interface OpenStream {
  readonly name: CallName;
  readonly receiver: StreamReceiver;
  sent: boolean;
}

export class Connection implements StreamHost {
  readonly #channel: BridgeChannel;
  readonly #pending = new Map<number, Pending>();
  readonly #streams = new Map<number, OpenStream>();
  readonly #waitingForOpen: Array<() => void> = [];
  readonly #eventListeners = new Map<string, Set<(payload: string) => void>>();
  readonly #closingWork: Array<() => Promise<unknown>> = [];
  readonly #closeRequests = new Map<number, AbortController>();
  #state: "opening" | "open" | "closed" = "opening";
  #readyCalled = false;
  #nextId = 1;
  #document: string | undefined;
  #hostContracts: string | undefined;

  constructor(channel: BridgeChannel, contracts: string) {
    this.#channel = channel;
    channel.listen(message => this.#receive(message), () => this.#closed());
    this.#send({ type: "hello", protocol: protocolVersion, types: understoodTypes, contracts, payload: "" });
  }

  /** The document id the host assigned in open; undefined until then. */
  get document(): string | undefined {
    return this.#document;
  }

  /** The host's contract fingerprint from open: when it differs from the page's, every call is rejected. */
  get hostContracts(): string | undefined {
    return this.#hostContracts;
  }

  /** Sends one call and resolves with its outcome; the promise never rejects. */
  invoke(contract: string, command: string, request: string, decoder: Decoder<unknown>, options?: CallOptions): Promise<Outcome> {
    const name: CallName = { contract, command };
    const signal = options?.signal;
    if (signal?.aborted === true) {
      // It never left the page, so it certainly did not run.
      return Promise.resolve(failure(name, "cancelled", "cancelled-before-start", "notStarted"));
    }

    if (this.#state === "closed") {
      return Promise.resolve(failure(name, "unavailable", "unavailable", "notStarted"));
    }

    return new Promise<Outcome>(settle => {
      const id = this.#nextId++;
      // Started here, synchronously, so a `traced` span around the caller is its parent.
      const span = traceCall(contract, command);
      const resolve = span === undefined ? settle : (outcome: Outcome): void => {
        span.end(...spanOutcome(outcome));
        settle(outcome);
      };
      const pending: Pending = { name, decoder, resolve, sent: false, cleanup: () => {} };
      this.#pending.set(id, pending);
      if (signal !== undefined) {
        const onAbort = (): void => this.#abort(id);
        signal.addEventListener("abort", onAbort, { once: true });
        pending.cleanup = () => signal.removeEventListener("abort", onAbort);
      }

      const send = (): void => {
        if (!this.#pending.has(id)) {
          return;
        }

        pending.sent = true;
        const call: BridgeMessage = span === undefined
          ? { type: "call", id, contract, command, payload: request }
          : { type: "call", id, contract, command, traceparent: span.traceparent, payload: request };
        this.#send(call);
      };
      if (this.#state === "open") {
        send();
      } else {
        this.#waitingForOpen.push(send);
      }
    });
  }

  /**
   * @internal Opens a stream or a subscription (streams.ts): its `items`, `end` and `failure` go to `receiver`. Sent
   * once the channel is open; undefined when the channel is gone.
   */
  open(name: CallName, request: string, credits: number, receiver: StreamReceiver, span: CallSpan | undefined): StreamHandle | undefined {
    if (this.#state === "closed") {
      return undefined;
    }

    const id = this.#nextId++;
    const stream: OpenStream = { name, receiver, sent: false };
    this.#streams.set(id, stream);
    const send = (): void => {
      if (!this.#streams.has(id)) {
        return;
      }

      stream.sent = true;
      const { contract, command } = name;
      this.#send(span === undefined
        ? { type: "stream", id, contract, command, credits, payload: request }
        : { type: "stream", id, contract, command, credits, traceparent: span.traceparent, payload: request });
    };
    if (this.#state === "open") {
      send();
    } else {
      this.#waitingForOpen.push(send);
    }

    return {
      credits: count => {
        if (stream.sent && this.#streams.has(id)) {
          this.#send({ type: "credits", id, credits: count, payload: "" });
        }
      },
      cancel: () => {
        if (!stream.sent) {
          this.#streams.delete(id);
          return false;
        }

        this.#send({ type: "cancel", id, payload: "" });
        return true;
      },
    };
  }

  /** @internal Whether streams can open again after a lost channel; this document's channel never comes back. */
  reconnected(_deadline: number, _signal: AbortSignal | undefined): Promise<boolean> {
    return Promise.resolve(this.#state === "open");
  }

  /** @internal A stream's span, when the page's telemetry is on. */
  span(name: CallName): CallSpan | undefined {
    return traceCall(name.contract, name.command);
  }

  /**
   * Tells the host the page is ready: call it once the app's first view has rendered, for example in the root
   * component's first effect. The host's `ui-ready`, which tests and tools wait for, follows. When the page has a
   * splash (`#spar-splash` in `index.html`), it is removed first, after its minimum visible time if its mark has
   * appeared, and the host hears `ready` once the removal has had a frame. Later calls in the same document do nothing.
   */
  ready(): void {
    if (this.#readyCalled) {
      return;
    }

    this.#readyCalled = true;
    if (!hasSplash()) {
      this.#send({ type: "ready", payload: "" });
      return;
    }

    void finishSplash().then(() => this.#send({ type: "ready", payload: "" }));
  }

  /** Listens for a host event, such as `backend-updated`; returns a function that stops listening. */
  onEvent(name: string, listener: (payload: string) => void): () => void {
    const listeners = this.#eventListeners.get(name) ?? new Set();
    listeners.add(listener);
    this.#eventListeners.set(name, listeners);
    return () => listeners.delete(listener);
  }

  /** Work the page does before its window closes, such as a telemetry flush; the host waits about a second. */
  onClosing(work: () => Promise<unknown>): void {
    this.#closingWork.push(work);
  }

  #send(message: BridgeMessage): void {
    if (this.#state !== "closed") {
      this.#channel.send(encode(message));
    }
  }

  #receive(text: string): void {
    const decoded = decode(text);
    if (!decoded.ok) {
      if (decoded.id !== undefined) {
        this.#fail(decoded.id, "protocol", "malformed", "unknown");
      }

      return;
    }

    const message = decoded.message;
    switch (message.type) {
      case "open":
        this.#open(message);
        break;
      case "result":
        this.#result(message);
        break;
      case "items":
        this.#items(message);
        break;
      case "end":
        this.#end(message);
        break;
      case "failure":
        this.#failure(message);
        break;
      case "event":
        this.#eventListeners.get(message.name!)?.forEach(listener => listener(message.payload));
        break;
      case "closing":
        void this.#runClosingWork();
        break;
      case "close-request":
        void this.#runCloseGuards(message.seq!, message.payload);
        break;
      case "close-ended":
        this.#closeRequests.get(message.seq!)?.abort();
        break;
      default:
        // An unexpected type for a pending call fails that call, so no call can hang.
        if (message.id !== undefined) {
          this.#fail(message.id, "protocol", "malformed", "unknown");
        }
    }
  }

  #open(message: BridgeMessage): void {
    this.#document = message.doc;
    this.#hostContracts = message.contracts;
    this.#state = "open";
    for (const send of this.#waitingForOpen.splice(0)) {
      send();
    }
  }

  #result(message: BridgeMessage): void {
    const pending = this.#take(message.id!);
    if (pending === undefined) {
      return;
    }

    pending.resolve(message.ok === true ? valueOutcome(pending, message.payload) : errorOutcome(pending, message.payload));
  }

  #failure(message: BridgeMessage): void {
    const stream = this.#takeStream(message.id!);
    if (stream !== undefined) {
      const { contract, command } = stream.name;
      const kind = message.kind as CallErrorKind;
      const execution = message.execution as Execution;
      stream.receiver.failure({ kind, code: message.code!, execution, contract, command, ...failureNotes(message.payload) });
      return;
    }

    const pending = this.#take(message.id!);
    pending?.resolve(failure(pending.name, message.kind as CallErrorKind, message.code!, message.execution as Execution, failureNotes(message.payload)));
  }

  #items(message: BridgeMessage): void {
    this.#streams.get(message.id!)?.receiver.items(message.payload, message.dropped);
  }

  #end(message: BridgeMessage): void {
    this.#takeStream(message.id!)?.receiver.end(message.ok === true, message.payload);
  }

  #takeStream(id: number): OpenStream | undefined {
    const stream = this.#streams.get(id);
    this.#streams.delete(id);
    return stream;
  }

  /** The page gave up waiting: ask the backend to stop, and report what it confirms, or an unknown outcome. */
  #abort(id: number): void {
    const pending = this.#pending.get(id);
    if (pending === undefined) {
      return;
    }

    if (!pending.sent) {
      this.#take(id);
      pending.resolve(failure(pending.name, "cancelled", "cancelled-before-start", "notStarted"));
      return;
    }

    this.#send({ type: "cancel", id, payload: "" });
    setTimeout(() => this.#fail(id, "transport", "unconfirmed", "unknown"), confirmationTimeoutMs);
  }

  #fail(id: number, kind: CallErrorKind, code: string, execution: Execution): void {
    const stream = this.#takeStream(id);
    if (stream !== undefined) {
      // A malformed message for a stream fails it, and the backend is asked to stop it.
      this.#send({ type: "cancel", id, payload: "" });
      stream.receiver.failure({ kind, code, execution, contract: stream.name.contract, command: stream.name.command });
      return;
    }

    const pending = this.#take(id);
    pending?.resolve(failure(pending.name, kind, code, execution));
  }

  #take(id: number): Pending | undefined {
    const pending = this.#pending.get(id);
    if (pending === undefined) {
      return undefined;
    }

    this.#pending.delete(id);
    pending.cleanup();
    return pending;
  }

  /** The host went away: a call it received may have run (`lost`); one it never received did not (`unavailable`). */
  #closed(): void {
    this.#state = "closed";
    for (const request of this.#closeRequests.values()) {
      request.abort();
    }

    this.#waitingForOpen.length = 0;
    for (const id of [...this.#pending.keys()]) {
      const pending = this.#take(id)!;
      pending.resolve(pending.sent
        ? failure(pending.name, "transport", "lost", "unknown")
        : failure(pending.name, "unavailable", "unavailable", "notStarted"));
    }

    for (const id of [...this.#streams.keys()]) {
      const stream = this.#takeStream(id)!;
      stream.receiver.lost(stream.sent);
    }
  }

  /** One close request: the guards in order, then their answer, unless the request ended without them. */
  async #runCloseGuards(seq: number, payload: string): Promise<void> {
    const { reason, automated } = closeRequestOf(payload);
    const controller = new AbortController();
    this.#closeRequests.set(seq, controller);
    let outcome: GuardsOutcome;
    try {
      outcome = await runCloseGuards(reason, automated, controller.signal);
    } finally {
      this.#closeRequests.delete(seq);
    }

    if (!controller.signal.aborted) {
      this.#send({ type: "close-answer", seq, payload: closeAnswerPayload(outcome) });
    }
  }

  async #runClosingWork(): Promise<void> {
    await Promise.allSettled(this.#closingWork.map(work => work()));
    this.#send({ type: "closing-done", payload: "" });
  }
}

/** The outcome as the call's span names it, and the failure's code. */
function spanOutcome(outcome: Outcome): ["success" | "error" | CallErrorKind, string?] {
  if (outcome.ok) {
    return ["success"];
  }

  return outcome.code === undefined ? [outcome.error.kind, outcome.error.code] : ["error"];
}

function failure(name: CallName, kind: CallErrorKind, code: string, execution: Execution, notes: FailureNotes = {}): Outcome {
  const error: CallError = { kind, code, execution, contract: name.contract, command: name.command, ...notes };
  return { ok: false, error };
}

interface FailureNotes {
  correlation?: string;
  detail?: string;
}

const traceId = /^[0-9a-f]{32}$/;

/**
 * The correlation id and the Development detail from a failure's payload, each only when it has the expected shape.
 * The payload is the host's, but read as untrusted: own properties only, built into a new object one by one.
 */
function failureNotes(payload: string): FailureNotes {
  if (payload === "") {
    return {};
  }

  let parsed: unknown;
  try {
    parsed = JSON.parse(payload);
  } catch {
    return {};
  }

  if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) {
    return {};
  }

  const notes: FailureNotes = {};
  const correlation = ownString(parsed, "correlation");
  if (correlation !== undefined && traceId.test(correlation)) {
    notes.correlation = correlation;
  }

  const detail = ownString(parsed, "detail");
  if (detail !== undefined) {
    notes.detail = detail;
  }

  return notes;
}

function ownString(object: object, key: string): string | undefined {
  if (!Object.prototype.hasOwnProperty.call(object, key)) {
    return undefined;
  }

  const value: unknown = (object as Record<string, unknown>)[key];
  return typeof value === "string" ? value : undefined;
}

function valueOutcome(pending: Pending, payload: string): Outcome {
  try {
    return { ok: true, value: pending.decoder.value(payload) };
  } catch {
    return failure(pending.name, "protocol", "malformed", "unknown");
  }
}

function errorOutcome(pending: Pending, payload: string): Outcome {
  let parsed: unknown;
  try {
    parsed = JSON.parse(payload);
  } catch {
    return failure(pending.name, "protocol", "malformed", "unknown");
  }

  if (typeof parsed !== "object" || parsed === null || typeof (parsed as { code?: unknown }).code !== "string") {
    return failure(pending.name, "protocol", "malformed", "unknown");
  }

  const fields = parsed as Readonly<Record<string, unknown>>;
  const code = fields["code"] as string;
  const decoded = pending.decoder.error(code, fields);
  if (decoded === unknownErrorCase) {
    return failure(pending.name, "protocol", "unknown-error-case", "unknown");
  }

  return { ok: false, code, payload: decoded, call: pending.name };
}

/** A close request's payload, `{"reason":"user","automated":false}`, read as untrusted: anything unexpected is a user's close. */
function closeRequestOf(payload: string): Pick<CloseRequest, "reason" | "automated"> {
  let parsed: unknown;
  try {
    parsed = JSON.parse(payload);
  } catch {
    parsed = undefined;
  }

  const fields = typeof parsed === "object" && parsed !== null ? parsed : {};
  const reason = ownString(fields, "reason");
  const automated = Object.prototype.hasOwnProperty.call(fields, "automated") && (fields as { automated?: unknown }).automated === true;
  return { reason: reason === "app" || reason === "sessionEnd" ? reason : "user", automated };
}

/** The guards' answer on the wire: `{"answer":"close"}`, `{"answer":"ask","question":"…"}` and so on. */
function closeAnswerPayload(outcome: GuardsOutcome): string {
  return typeof outcome === "string"
    ? JSON.stringify({ answer: outcome })
    : JSON.stringify({ answer: "ask", question: outcome.ask });
}
