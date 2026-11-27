// Streams and subscriptions on the page (plan section 2c; decision "M3a's decide-first questions"). A stream is read
// through a hand-written iterator over the transport's buffer, never a layered async generator, and each item is
// decoded and wrapped in its `Result` when the iterator hands it out. The page grants credits in items: 256 at the start,
// topped up once half are used. A terminal failure is the last item, in-band. Leaving the loop early cancels the stream
// and waits for the backend's confirmation; an automatic subscription subscribes again after a lost connection.
import {
  Result,
  SparError,
  type CallError,
  type CallErrorKind,
  type CallName,
  type ErrorCases,
  type EventCases,
  type Execution,
  type Outcome,
} from "./results.js";
import { unknownErrorCase } from "./connection.js";
import type { CallSpan } from "./tracing.js";

/** How a generated module reads one stream's items and the error that ends it. */
export interface StreamDecoder<T> {
  /** Reads one item's parsed JSON; builds its object field by field. */
  readonly item: (value: unknown) => T;
  /** Reads a declared error's parsed payload, or returns the SDK's `unknownErrorCase`. */
  readonly error: (code: string, payload: Readonly<Record<string, unknown>>) => unknown;
}

/** The credits a stream starts with, and the window it keeps (decision "M3a's decide-first questions", item 2). */
export const streamCredits = 256;

/** How long a cancelled stream waits for the backend to confirm it stopped before its outcome is unknown. */
const confirmationTimeoutMs = 5_000;

/** Automatic resume (plan, "Subscription resumption"): the first wait, the longest one, and when it gives up. */
const resumeFirstMs = 250;
const resumeLongestMs = 5_000;
const resumeLimitMs = 30_000;

/** What a stream's run hears from its document's connection, for its own id. */
export interface StreamReceiver {
  items(payload: string, dropped: number | undefined): void;
  end(ok: boolean, payload: string): void;
  failure(error: CallError): void;
  /** The channel went away; `sent` tells whether the host had the stream message. */
  lost(sent: boolean): void;
}

/** What a stream's run can tell the connection about its own id. */
export interface StreamHandle {
  credits(count: number): void;
  /** Asks the backend to stop; false when the stream never left the page, so it certainly did not run. */
  cancel(): boolean;
}

/** What a stream needs from its document's connection. */
export interface StreamHost {
  /** Opens a stream (when the channel is open); its messages go to `receiver`. Undefined when the channel is gone. */
  open(name: CallName, request: string, credits: number, receiver: StreamReceiver, span: CallSpan | undefined): StreamHandle | undefined;
  /**
   * Waits until the channel can open streams again after it was lost, or `deadline` (a `Date.now()` value) passes.
   * False when the channel will not come back: a document whose channel closed for good never reconnects.
   */
  reconnected(deadline: number, signal: AbortSignal | undefined): Promise<boolean>;
  /** A span for the stream, when the page's telemetry is on. */
  span(name: CallName): CallSpan | undefined;
}

/** How a subscription behaves: whether it keeps only its newest value and whether it subscribes again by itself. */
export interface SubscriptionKind {
  readonly latestOnly: boolean;
  readonly resumes: boolean;
}

/** The iterator a run reports to; it ignores a run that is no longer its current one. */
interface RunOwner {
  received(run: StreamRun, payload: string, dropped: number | undefined): void;
  ended(run: StreamRun, outcome: Outcome | undefined, lost: boolean): void;
}

type Entry =
  | { readonly kind: "value"; readonly raw: unknown }
  | { readonly kind: "outcome"; readonly outcome: Outcome }
  | { readonly kind: "done" };

/** @internal A one-shot stream: its items, then the declared error or failure that ended it, if one did. */
export function openStream<T, E extends ErrorCases>(
  host: StreamHost,
  name: CallName,
  request: string,
  decoder: StreamDecoder<T>,
  signal: AbortSignal | undefined,
): AsyncIterable<Result<T, E>> {
  return once(() => new StreamIterator<T, E, {}>(host, name, request, decoder, signal, undefined));
}

/** @internal A subscription: its values, its status events, and the error or failure that ended it, if one did. */
export function openSubscription<T, E extends ErrorCases, V extends EventCases>(
  host: StreamHost,
  name: CallName,
  request: string,
  decoder: StreamDecoder<T>,
  signal: AbortSignal | undefined,
  kind: SubscriptionKind,
): AsyncIterable<Result<T, E, V>> {
  return once(() => new StreamIterator<T, E, V>(host, name, request, decoder, signal, kind));
}

/** An iterable read once: the stream starts when the loop asks for its iterator, so one never read sends nothing. */
function once<R>(start: () => AsyncIterator<R>): AsyncIterable<R> {
  let started = false;
  return {
    [Symbol.asyncIterator](): AsyncIterator<R> {
      if (started) {
        throw new TypeError("A Spar stream can be read once: call the command again for a new one.");
      }

      started = true;
      return start();
    },
  };
}

/**
 * The page's side of one stream or subscription: the buffer the runs fill, the iterator that empties it, and, for an
 * automatic subscription, the runs after a lost connection.
 */
class StreamIterator<T, E extends ErrorCases, V extends EventCases> implements AsyncIterator<Result<T, E, V>>, RunOwner {
  readonly #host: StreamHost;
  readonly #name: CallName;
  readonly #request: string;
  readonly #decoder: StreamDecoder<T>;
  readonly #signal: AbortSignal | undefined;
  readonly #kind: SubscriptionKind | undefined;
  readonly #buffer: Entry[] = [];
  #waiter: (() => void) | undefined;
  #run: StreamRun | undefined;
  #finished = false;
  #leaving: { resolve: () => void; reject: (error: unknown) => void } | undefined;
  #onAbort: (() => void) | undefined;

  /** One span for the whole stream or subscription, its resumed runs included, with its counts as attributes. */
  #span: CallSpan | undefined;
  #received = 0;
  #dropped = 0;
  #resumes = 0;

  constructor(
    host: StreamHost,
    name: CallName,
    request: string,
    decoder: StreamDecoder<T>,
    signal: AbortSignal | undefined,
    kind: SubscriptionKind | undefined,
  ) {
    this.#host = host;
    this.#name = name;
    this.#request = request;
    this.#decoder = decoder;
    this.#signal = signal;
    this.#kind = kind;
    if (signal?.aborted === true) {
      // It never left the page, so it certainly did not run.
      this.#terminal(failureOf(name, "cancelled", "cancelled-before-start", "notStarted"));
      return;
    }

    if (signal !== undefined) {
      this.#onAbort = () => {
        // The loop asked to stop: what is still unread stays unread, and only the confirmed end follows.
        this.#buffer.length = 0;
        this.#cancel();
      };
      signal.addEventListener("abort", this.#onAbort, { once: true });
    }

    this.#span = host.span(name);
    this.#start();
  }

  async next(): Promise<IteratorResult<Result<T, E, V>>> {
    while (this.#buffer.length === 0) {
      await new Promise<void>(resolve => (this.#waiter = resolve));
    }

    const entry = this.#buffer.shift()!;
    switch (entry.kind) {
      case "done":
        this.#buffer.unshift(entry);
        return { done: true, value: undefined };
      case "outcome":
        return { done: false, value: new Result<T, E, V>(entry.outcome) };
      case "value":
        return { done: false, value: this.#handOut(entry.raw) };
    }
  }

  /** Leaving the loop early: the stream is cancelled, and this waits for the backend to confirm it stopped. */
  return(): Promise<IteratorResult<Result<T, E, V>>> {
    if (this.#finished) {
      return Promise.resolve({ done: true, value: undefined });
    }

    return new Promise((resolve, reject) => {
      this.#leaving = { resolve: () => resolve({ done: true, value: undefined }), reject };
      this.#cancel();
    });
  }

  /** A run's items: decoded only when handed out; a latest-only subscription keeps just its newest unread value. */
  received(run: StreamRun, payload: string, dropped: number | undefined): void {
    if (run !== this.#run || this.#finished || this.#signal?.aborted === true) {
      return;
    }

    let values: unknown;
    try {
      values = JSON.parse(payload);
    } catch {
      values = undefined;
    }

    if (!Array.isArray(values)) {
      this.#malformed(run);
      return;
    }

    if (dropped !== undefined && this.#kind !== undefined && !this.#kind.latestOnly) {
      this.#dropped += dropped;
      this.#push({ kind: "outcome", outcome: { ok: true, event: "gap", payload: { dropped } } });
    }

    if (this.#kind?.latestOnly === true) {
      this.#dropUnreadValues(run);
    }

    this.#received += values.length;
    for (const raw of values) {
      this.#push({ kind: "value", raw });
    }
  }

  /** A run ended: its declared error or failure is the last item, unless an automatic subscription lost its channel. */
  ended(run: StreamRun, outcome: Outcome | undefined, lost: boolean): void {
    if (run !== this.#run || this.#finished) {
      return;
    }

    if (lost && this.#kind?.resumes === true && this.#leaving === undefined && this.#signal?.aborted !== true) {
      void this.#resume();
      return;
    }

    this.#terminal(outcome);
  }

  #start(): void {
    const run = new StreamRun(this, this.#name, this.#decoder);
    this.#run = run;
    run.open(this.#host, this.#request, this.#span);
  }

  /**
   * After a lost connection: `reconnecting` for each try, with backoff, then a new run once the channel is back, with
   * `reset` and `resumed` before its first value. After the limit, the failure `transport` / `lost`.
   */
  async #resume(): Promise<void> {
    this.#run = undefined;
    const deadline = Date.now() + resumeLimitMs;
    for (let attempt = 1; ; attempt++) {
      this.#push({ kind: "outcome", outcome: { ok: true, event: "reconnecting", payload: { attempt } } });
      const wait = Math.min(resumeFirstMs * 2 ** (attempt - 1), resumeLongestMs, Math.max(0, deadline - Date.now()));
      await delay(wait);
      if (this.#finished) {
        return;
      }

      const back = await this.#host.reconnected(Math.min(deadline, Date.now() + resumeLongestMs), this.#signal);
      if (this.#finished) {
        return;
      }

      if (back) {
        this.#resumes++;
        this.#push({ kind: "outcome", outcome: { ok: true, event: "reset", payload: undefined } });
        this.#push({ kind: "outcome", outcome: { ok: true, event: "resumed", payload: undefined } });
        this.#start();
        return;
      }

      if (Date.now() >= deadline || this.#signal?.aborted === true) {
        this.#terminal(failureOf(this.#name, "transport", "lost", "unknown"));
        return;
      }
    }
  }

  #handOut(raw: unknown): Result<T, E, V> {
    this.#run?.consumed(1);
    try {
      const value = this.#decoder.item(raw);
      return new Result<T, E, V>({ ok: true, value });
    } catch {
      // An item this client cannot read: the stream stops, this failure is its last item, and nothing after it is read.
      const malformed = failureOf(this.#name, "protocol", "malformed", "unknown");
      this.#run?.cancel();
      this.#buffer.length = 0;
      this.#terminal(undefined, malformed);
      return new Result<T, E, V>(malformed);
    }
  }

  #dropUnreadValues(run: StreamRun): void {
    let dropped = 0;
    for (let i = this.#buffer.length - 1; i >= 0; i--) {
      if (this.#buffer[i]!.kind === "value") {
        this.#buffer.splice(i, 1);
        dropped++;
      }
    }

    // The backend counts what it sent, read or not: the dropped values' credits go back.
    run.consumed(dropped);
  }

  #malformed(run: StreamRun | undefined): void {
    run?.cancel();
    this.#terminal(failureOf(this.#name, "protocol", "malformed", "unknown"));
  }

  /** The page aborted, or left the loop: cancel the current run, or end at once when there is none to wait for. */
  #cancel(): void {
    if (this.#finished) {
      return;
    }

    const run = this.#run;
    if (run === undefined || !run.cancel()) {
      this.#terminal(failureOf(this.#name, "cancelled", "cancelled-before-start", "notStarted"));
      return;
    }

    setTimeout(() => {
      if (this.#run === run) {
        this.#terminal(failureOf(this.#name, "transport", "unconfirmed", "unknown"));
      }
    }, confirmationTimeoutMs);
  }

  /**
   * The last item, if the outcome is one (a declared error or a failure), then the end of the iteration and of the
   * span, which `spanOutcome` describes when the last item was already handed out.
   */
  #terminal(outcome: Outcome | undefined, spanOutcome: Outcome | undefined = outcome): void {
    if (this.#finished) {
      return;
    }

    this.#finished = true;
    this.#run = undefined;
    this.#endSpan(spanOutcome);
    if (this.#onAbort !== undefined) {
      this.#signal?.removeEventListener("abort", this.#onAbort);
    }

    const leaving = this.#leaving;
    if (leaving !== undefined) {
      // A loop that was left early does not read the end; it only learns whether the backend confirmed it.
      this.#buffer.length = 0;
      this.#push({ kind: "done" });
      if (outcome !== undefined && !outcome.ok && outcome.code === undefined && outcome.error.code === "unconfirmed") {
        leaving.reject(new SparError(outcome.error));
      } else {
        leaving.resolve();
      }

      return;
    }

    if (outcome !== undefined) {
      this.#push({ kind: "outcome", outcome });
    }

    this.#push({ kind: "done" });
  }

  /** The stream's one span: its counts, then its outcome, named as on the backend's span. */
  #endSpan(outcome: Outcome | undefined): void {
    const span = this.#span;
    if (span === undefined) {
      return;
    }

    span.set("spar.stream.kind", this.#kind === undefined ? "stream" : "subscription");
    span.set("spar.stream.items", this.#received);
    if (this.#kind !== undefined) {
      span.set("spar.stream.dropped", this.#dropped);
      span.set("spar.stream.resumes", this.#resumes);
    }

    if (outcome === undefined || outcome.ok) {
      span.end("success");
    } else if (outcome.code !== undefined) {
      span.end("error");
    } else {
      span.end(outcome.error.kind, outcome.error.code);
    }
  }

  #push(entry: Entry): void {
    this.#buffer.push(entry);
    const waiter = this.#waiter;
    this.#waiter = undefined;
    waiter?.();
  }
}

/** One stream id on the wire: its credits, its cancel, and its end. A resumed subscription has several, one after another. */
class StreamRun implements StreamReceiver {
  readonly #owner: RunOwner;
  readonly #name: CallName;
  readonly #decoder: StreamDecoder<unknown>;
  #handle: StreamHandle | undefined;
  #consumed = 0;

  constructor(owner: RunOwner, name: CallName, decoder: StreamDecoder<unknown>) {
    this.#owner = owner;
    this.#name = name;
    this.#decoder = decoder;
  }

  /** Sends the stream message, with the stream's span as the parent of the backend's. */
  open(host: StreamHost, request: string, span: CallSpan | undefined): void {
    this.#handle = host.open(this.#name, request, streamCredits, this, span);
    if (this.#handle === undefined) {
      this.lost(false);
    }
  }

  /** The page handed out `count` items: once half the window is used, the backend gets those credits back. */
  consumed(count: number): void {
    this.#consumed += count;
    if (this.#consumed >= streamCredits / 2) {
      this.#handle?.credits(this.#consumed);
      this.#consumed = 0;
    }
  }

  cancel(): boolean {
    return this.#handle?.cancel() ?? false;
  }

  items(payload: string, dropped: number | undefined): void {
    this.#owner.received(this, payload, dropped);
  }

  end(ok: boolean, payload: string): void {
    this.#owner.ended(this, ok ? undefined : this.#errorOutcome(payload), false);
  }

  failure(error: CallError): void {
    this.#owner.ended(this, { ok: false, error }, false);
  }

  lost(sent: boolean): void {
    const error = sent
      ? failureOf(this.#name, "transport", "lost", "unknown")
      : failureOf(this.#name, "unavailable", "unavailable", "notStarted");
    this.#owner.ended(this, error, true);
  }

  #errorOutcome(payload: string): Outcome {
    let parsed: unknown;
    try {
      parsed = JSON.parse(payload);
    } catch {
      return failureOf(this.#name, "protocol", "malformed", "unknown");
    }

    if (typeof parsed !== "object" || parsed === null || typeof (parsed as { code?: unknown }).code !== "string") {
      return failureOf(this.#name, "protocol", "malformed", "unknown");
    }

    const fields = parsed as Readonly<Record<string, unknown>>;
    const code = fields["code"] as string;
    const decoded = this.#decoder.error(code, fields);
    if (decoded === unknownErrorCase) {
      return failureOf(this.#name, "protocol", "unknown-error-case", "unknown");
    }

    return { ok: false, code, payload: decoded, call: this.#name };
  }
}

function failureOf(name: CallName, kind: CallErrorKind, code: string, execution: Execution): Outcome & { readonly ok: false; readonly error: CallError } {
  return { ok: false, error: { kind, code, execution, contract: name.contract, command: name.command } };
}

function delay(ms: number): Promise<void> {
  return new Promise(resolve => setTimeout(resolve, ms));
}
