// The engine of @spar/app/testing (plan section 5, level 3): a fake host for page tests. It speaks the real bridge
// protocol to the page's own connection, so every scripted value crosses the generated encoders and decoders exactly as
// a real host's would: a bigint, bytes, an enum member, a null. The generated testing module passes it the app's
// commands with their wire functions; app code imports only `fakeSpar` from there.
import { fakeBlob, fakeBlobs, type SparBlob } from "./blobs.js";
import type { BridgeChannel } from "./connection.js";
import { decode, encode, protocolVersion, type BridgeMessage } from "./protocol.js";
import { resetConfiguration, type Call, type CallError, type ErrorCases, type Result } from "./results.js";
import { setColorScheme } from "./colorScheme.js";
import type { FileAccess, PickedFile } from "./files.js";
import { contentsOf, dropOn, fakeFile, fileCommands, fileStore } from "./testingFiles.js";
import { connectSpar } from "./sdk.js";
import type { CloseRequest, GuardsOutcome } from "./windows.js";

/** @internal One command as the generated testing module describes it: its names and its wire functions. */
export interface CommandCodec {
  readonly contract: string;
  readonly command: string;
  /** Reads the request from its JSON; absent for a command without a request. */
  readonly request?: ((json: string) => unknown) | undefined;
  /** Writes the result's JSON; absent for a command without a value. */
  readonly value?: ((value: unknown) => string) | undefined;
  /** Whether the command is a stream or a subscription, whose `value` writes one item. */
  readonly stream?: "stream" | "subscription" | undefined;
  /** Writes a declared error's JSON, its code included; absent when the command declares none. */
  readonly error?: ((code: string, payload: unknown) => string) | undefined;
}

/** A generated command function, such as `pages.get`; streams and subscriptions are scripted with `stream`. */
export type Command = (...args: never[]) => PromiseLike<unknown>;

/** The request a command takes, or `undefined` for one without a request. */
export type RequestOf<F> = F extends (...args: infer A) => unknown ? (A extends [infer R, ...unknown[]] ? R : undefined) : never;

/** The value a command's call succeeds with. */
export type ValueOf<F> = F extends (...args: never[]) => Call<infer T, infer _E> ? T : never;

/** The declared errors of a command's call: case name to payload. */
export type ErrorsOf<F> = F extends (...args: never[]) => Call<infer _T, infer E> ? E : never;

/** The arguments `succeed` takes: none for a command without a value. */
export type ValueArgs<T> = [T] extends [void] ? [] : [value: T];

/** The arguments after the case name: none for a case without a payload. */
export type PayloadArgs<P> = [P] extends [undefined] ? [] : [payload: P];

/** An infrastructure failure to script: the fields a host sends. The SDK adds the call's names itself. */
export type ScriptedFailure = Pick<CallError, "kind" | "code" | "execution">;

/** A generated stream or subscription function, such as `pages.changes`. */
export type StreamCommand = (...args: never[]) => AsyncIterable<unknown>;

/** The values a stream or subscription carries. */
export type ItemOf<F> = F extends (...args: never[]) => AsyncIterable<Result<infer T, infer _E, infer _V>> ? T : never;

/** The declared errors that can end a stream or subscription. */
export type StreamErrorsOf<F> = F extends (...args: never[]) => AsyncIterable<Result<infer _T, infer E, infer _V>> ? E : never;

/**
 * What a page test does with the streams and subscriptions the page opens of one command. Values pushed before the page
 * opens one wait for it; afterwards they go to every one that is open. A stream nobody scripted stays open with nothing,
 * as a subscription with no change does.
 */
export interface StreamScript<R, T, E extends ErrorCases> {
  /** Sends values, in order, as one batch. */
  push(...values: T[]): void;
  /** Tells a subscription the backend dropped `dropped` values for a page that fell behind: the page sees a gap. */
  gap(dropped: number): void;
  /** Ends the open ones: a stream finished, or a subscription whose backend stopped it. */
  end(): void;
  /** Ends the open ones with a declared error, their last item. */
  fail<K extends keyof E & string>(code: K, ...payload: PayloadArgs<E[K]>): void;
  /** Ends the open ones with an infrastructure failure, as a host would report it. */
  failWith(error: ScriptedFailure): void;
  /** The requests of the ones the page opened, in order. */
  readonly opened: readonly R[];
}

declare const replyBrand: unique symbol;

/** What a scripted answer does with one call; made only by the `reply` an `answer` handler receives. */
export interface Reply {
  readonly [replyBrand]: true;
}

/** The replies an `answer` handler can give, typed by its command. */
export interface Replies<T, E extends ErrorCases> {
  succeed(...value: ValueArgs<T>): Reply;
  fail<K extends keyof E & string>(code: K, ...payload: PayloadArgs<E[K]>): Reply;
  failWith(error: ScriptedFailure): Reply;
  hang(): Reply;
}

/**
 * How the fake backend answers one command, from now on, for every call until scripted again. Each method replaces the
 * last one's behaviour.
 */
export interface Script<R, T, E extends ErrorCases> {
  /** Every call succeeds with this value. */
  succeed(...value: ValueArgs<T>): void;
  /** Every call ends with this declared error. */
  fail<K extends keyof E & string>(code: K, ...payload: PayloadArgs<E[K]>): void;
  /** Every call ends with this infrastructure failure, as a host would report it. */
  failWith(error: ScriptedFailure): void;
  /** Calls never answer. Aborting one cancels it, and the backend confirms: `cancelled` / `stopped`. */
  hang(): void;
  /** Each call is answered by `handler`, from its request; it may wait, to test what the page shows meanwhile. */
  answer(handler: (request: R, reply: Replies<T, E>) => Reply | Promise<Reply>): void;
}

/**
 * A fake backend for page tests, connected in place of the host: the generated contract modules call it as they would
 * call a real one. A command nobody scripted is refused (`rejected` / `unknown-command`), with a console error naming it.
 */
export interface FakeSpar {
  /**
   * Scripts one command, such as `spar.on(pages.get)`. The SDK's own `setColorScheme` is a command too: it succeeds
   * until a test scripts it otherwise, such as `spar.on(setColorScheme).fail("notSaved")`.
   */
  on<F extends Command>(command: F): Script<RequestOf<F>, ValueOf<F>, ErrorsOf<F>>;
  /** The requests the page sent to one command, in order, read back from the wire: `spar.calls(setColorScheme)` gives the schemes. */
  calls<F extends Command>(command: F): readonly RequestOf<F>[];
  /**
   * Scripts the streams or subscriptions the page opens of one command, such as `spar.stream(pages.changes)`: push
   * values, report a gap, end them. Cancelling one, as leaving its loop does, is confirmed: `cancelled` / `stopped`.
   */
  stream<F extends StreamCommand>(command: F): StreamScript<RequestOf<F>, ItemOf<F>, StreamErrorsOf<F>>;
  /**
   * Asks the page's close guards (`windows.onCloseRequested`) as the host does when its window is asked to close, and
   * resolves with their answer: `"close"`, `"keep"`, `{ ask }`, or `"failed"` when a guard threw. A user's close that
   * is not automated unless the request says otherwise; aborting `signal` ends the request without the guards, as a
   * session-end deadline does, and rejects with the signal's reason.
   */
  requestClose(request?: FakeCloseRequest): Promise<GuardsOutcome>;
  /**
   * Sends `backend-updated` as a Debug host does after Hot Reload applied a C# edit: the page's `onBackendUpdate`
   * handlers run, then the development overlay's toast shows unless `configureSpar({ backendUpdateToast: false })`.
   * Revisions count from 1 for each fake; `types` are the updated types' full names. Delivered asynchronously, as a
   * host message is: await a microtask (or the page's reaction) before asserting.
   */
  backendUpdate(update?: FakeBackendUpdate): void;
  /**
   * A blob for a scripted reply, such as `spar.on(reports.export).succeed(spar.blob(bytes, "text/csv", "report.csv"))`:
   * the page reads its bytes from the fake, and its `url` is the address a real handle would have, which nothing serves
   * in a page test.
   */
  blob(bytes: Uint8Array, contentType: string, name?: string): SparBlob;
  /**
   * A file for a scripted dialog or a drop, such as `spar.on(files.open).succeed(spar.file("notes.md", "# Notes"))`:
   * the fake keeps its contents, so the page's `readText()` reads them and its `writeText()` replaces them.
   */
  file(name: string, contents?: string | Uint8Array, access?: FileAccess): PickedFile;
  /** What the fake holds for a file now: the test's contents, or what the page wrote since. */
  contents(file: PickedFile): Uint8Array | undefined;
  /**
   * Drops files on an element as a person would, from the OS: the page's drop area (`files.dropTarget`) gets the drag
   * events, and its claim of the drop is answered with these files.
   */
  drop(area: Element, dropped: readonly PickedFile[]): void;
}

/** A backend update a page test sends: the updated types' full names, none by default. */
export interface FakeBackendUpdate {
  readonly types?: readonly string[];
}

/** A close request a page test sends: by default a user's, not automated. */
export interface FakeCloseRequest {
  readonly reason?: CloseRequest["reason"];
  readonly automated?: boolean;
  readonly signal?: AbortSignal;
}

type Answer =
  | { readonly kind: "value"; readonly value: unknown }
  | { readonly kind: "error"; readonly code: string; readonly payload: unknown }
  | { readonly kind: "failure"; readonly error: ScriptedFailure }
  | { readonly kind: "hang" };

type Behaviour = (request: unknown) => Answer | Promise<Answer>;

const replies: Replies<unknown, Record<string, unknown>> = {
  succeed: (...value: unknown[]) => reply({ kind: "value", value: value[0] }),
  fail: (code: string, ...payload: unknown[]) => reply({ kind: "error", code, payload: payload[0] }),
  failWith: error => reply({ kind: "failure", error }),
  hang: () => reply({ kind: "hang" }),
};

/**
 * @internal Called by the generated `fakeSpar`: resets the failure hooks and connects the page's SDK to a new fake host.
 * `commands` maps each generated command function to its names and wire functions.
 */
export function createFakeSpar(appCommands: ReadonlyMap<unknown, CommandCodec>, fingerprint: string): FakeSpar {
  resetConfiguration();
  fakeBlobs.clear();
  const commands = new Map(appCommands);
  commands.set(setColorScheme, colorSchemeCodec);
  for (const [dialog, codec] of fileCommands()) {
    commands.set(dialog, codec);
  }

  const { store, codecs: fileCodecs } = fileStore();
  for (const [codec] of fileCodecs) {
    commands.set(codec, codec);
  }

  const host = new FakeHost(commands, fingerprint);
  host.script(colorSchemeCodec, () => ({ kind: "value", value: undefined }));
  for (const [codec, answer] of fileCodecs) {
    host.script(codec, request => answer(request) as Answer);
  }

  connectSpar(host, fingerprint);
  return {
    on: command => scriptFor(host, codecOf(commands, command)),
    calls: command => host.calls(codecOf(commands, command)) as never,
    stream: command => streamScriptFor(host, codecOf(commands, command)) as never,
    requestClose: request => host.requestClose(request ?? {}),
    backendUpdate: update => host.backendUpdate(update ?? {}),
    blob: (bytes, contentType, name) => fakeBlob(bytes, contentType, name ?? null),
    file: (name, contents, access) => fakeFile(store, name, contents ?? "", access ?? "read"),
    contents: file => contentsOf(store, file),
    drop: (area, dropped) => dropOn(store, area, dropped),
  };
}

/** The host's own `spar.setColorScheme`, as a host answers it: the scheme's name, no value, `notSaved` without a payload. */
const colorSchemeCodec: CommandCodec = {
  contract: "spar",
  command: "setColorScheme",
  request: json => JSON.parse(json) as unknown,
  error: code => JSON.stringify({ code }),
};

function scriptFor<R, T, E extends ErrorCases>(host: FakeHost, codec: CommandCodec): Script<R, T, E> {
  const always = (answer: Answer): void => host.script(codec, () => answer);
  return {
    succeed: (...value: unknown[]) => always({ kind: "value", value: value[0] }),
    fail: (code: string, ...payload: unknown[]) => always({ kind: "error", code, payload: payload[0] }),
    failWith: error => always({ kind: "failure", error }),
    hang: () => always({ kind: "hang" }),
    answer: handler => host.script(codec, async request => answerOf(await handler(request as R, replies as never))),
  } as Script<R, T, E>;
}

function streamScriptFor(host: FakeHost, codec: CommandCodec): StreamScript<unknown, unknown, Record<string, unknown>> {
  if (codec.stream === undefined) {
    throw new TypeError(`fakeSpar: ${codec.contract}.${codec.command} is a call; script it with spar.on.`);
  }

  return {
    push: (...values: unknown[]) => host.streamItems(codec, values),
    gap: dropped => host.streamGap(codec, dropped),
    end: () => host.streamEnd(codec, { kind: "value", value: undefined }),
    fail: (code: string, ...payload: unknown[]) => host.streamEnd(codec, { kind: "error", code, payload: payload[0] }),
    failWith: error => host.streamEnd(codec, { kind: "failure", error }),
    get opened() {
      return host.calls(codec);
    },
  };
}

function codecOf(commands: ReadonlyMap<unknown, CommandCodec>, command: unknown): CommandCodec {
  const codec = commands.get(command);
  if (codec === undefined) {
    throw new TypeError("fakeSpar: this is not a command of the app's contracts. Pass one such as pages.get.");
  }

  return codec;
}

// A Reply is the Answer itself; the brand only keeps tests from building one by hand.
function reply(answer: Answer): Reply {
  return answer as unknown as Reply;
}

function answerOf(value: Reply): Answer {
  return value as unknown as Answer;
}

/** The host side of the fake: answers `hello` with `open`, runs the scripts, and confirms cancellations. */
class FakeHost implements BridgeChannel {
  readonly #byName = new Map<string, CommandCodec>();
  readonly #scripts = new Map<CommandCodec, Behaviour>();
  readonly #calls = new Map<CommandCodec, unknown[]>();
  readonly #inFlight = new Map<number, { settled: boolean }>();
  readonly #openStreams = new Map<number, { readonly codec: CommandCodec; sent: number }>();
  readonly #waitingItems = new Map<CommandCodec, unknown[][]>();
  readonly #closeAnswers = new Map<number, (outcome: GuardsOutcome) => void>();
  #closeSeq = 0;
  #revision = 0;
  readonly #fingerprint: string;
  #received: ((message: string) => void) | undefined;

  constructor(commands: ReadonlyMap<unknown, CommandCodec>, fingerprint: string) {
    for (const codec of commands.values()) {
      this.#byName.set(`${codec.contract}.${codec.command}`, codec);
    }

    this.#fingerprint = fingerprint;
  }

  listen(received: (message: string) => void): void {
    this.#received = received;
  }

  send(text: string): void {
    const decoded = decode(text);
    if (!decoded.ok) {
      throw new Error(`fakeSpar: the page sent a malformed message: ${decoded.reason}`);
    }

    const message = decoded.message;
    switch (message.type) {
      case "hello":
        this.#deliver({ type: "open", protocol: protocolVersion, doc: "fake-document", contracts: this.#fingerprint, payload: "" });
        break;
      case "call":
        this.#call(message);
        break;
      case "stream":
        this.#stream(message);
        break;
      case "cancel":
        this.#cancel(message.id!);
        break;
      case "close-answer":
        this.#closeAnswers.get(message.seq!)?.(closeOutcomeOf(message.payload));
        break;
      default:
        // ready and closing-done need no answer.
        break;
    }
  }

  requestClose(request: FakeCloseRequest): Promise<GuardsOutcome> {
    const seq = ++this.#closeSeq;
    const payload = JSON.stringify({ reason: request.reason ?? "user", automated: request.automated ?? false });
    return new Promise<GuardsOutcome>((resolve, reject) => {
      const signal = request.signal;
      const onAbort = (): void => {
        this.#closeAnswers.delete(seq);
        this.#deliver({ type: "close-ended", seq, payload: "" });
        reject(signal!.reason);
      };
      this.#closeAnswers.set(seq, outcome => {
        this.#closeAnswers.delete(seq);
        signal?.removeEventListener("abort", onAbort);
        resolve(outcome);
      });
      signal?.addEventListener("abort", onAbort, { once: true });
      this.#deliver({ type: "close-request", seq, payload });
    });
  }

  script(codec: CommandCodec, behaviour: Behaviour): void {
    this.#scripts.set(codec, behaviour);
  }

  calls(codec: CommandCodec): readonly unknown[] {
    return [...(this.#calls.get(codec) ?? [])];
  }

  #call(message: BridgeMessage): void {
    const id = message.id!;
    const name = `${message.contract}.${message.command}`;
    const codec = this.#byName.get(name);
    const behaviour = codec === undefined ? undefined : this.#scripts.get(codec);
    if (codec === undefined || behaviour === undefined) {
      console.error(`fakeSpar: the page called ${name}, which no test scripted.`);
      this.#fail(id, { kind: "rejected", code: "unknown-command", execution: "notStarted" });
      return;
    }

    const request = codec.request === undefined ? undefined : codec.request(message.payload);
    this.#record(codec, request);

    const flight = { settled: false };
    this.#inFlight.set(id, flight);
    void Promise.resolve()
      .then(() => behaviour(request))
      .then(answer => this.#answer(id, flight, codec, answer), (error: unknown) => {
        console.error(`fakeSpar: the script for ${name} threw:`, error);
        this.#settle(id, flight, () => this.#fail(id, { kind: "failed", code: "failed", execution: "unknown" }));
      });
  }

  #record(codec: CommandCodec, request: unknown): void {
    const list = this.#calls.get(codec) ?? [];
    list.push(request);
    this.#calls.set(codec, list);
  }

  #answer(id: number, flight: { settled: boolean }, codec: CommandCodec, answer: Answer): void {
    if (answer.kind === "hang") {
      return;
    }

    this.#settle(id, flight, () => {
      try {
        this.#deliver(this.#message(id, codec, answer));
      } catch (error) {
        // What a real host does when it cannot write a result (plan section 2c): the command ran, its result is lost.
        console.error(`fakeSpar: the scripted answer for ${codec.contract}.${codec.command} could not be written:`, error);
        this.#fail(id, { kind: "response", code: "serialization", execution: "completed" });
      }
    });
  }

  #message(id: number, codec: CommandCodec, answer: Exclude<Answer, { kind: "hang" }>): BridgeMessage {
    switch (answer.kind) {
      case "value":
        return { type: "result", id, ok: true, payload: codec.value === undefined ? "" : codec.value(answer.value) };
      case "error":
        return { type: "result", id, ok: false, payload: codec.error!(answer.code, answer.payload) };
      case "failure":
        return { type: "failure", id, ...answer.error, payload: "" };
    }
  }

  /** Values for the open streams of a command, or, before the page opens one, for the next one it opens. */
  streamItems(codec: CommandCodec, values: readonly unknown[]): void {
    const open = this.#openOf(codec);
    if (open.length === 0) {
      const waiting = this.#waitingItems.get(codec) ?? [];
      waiting.push([...values]);
      this.#waitingItems.set(codec, waiting);
      return;
    }

    for (const [id, stream] of open) {
      this.#deliverItems(id, stream, values);
    }
  }

  streamGap(codec: CommandCodec, dropped: number): void {
    for (const [id] of this.#openOf(codec)) {
      this.#deliver({ type: "items", id, dropped, payload: "[]" });
    }
  }

  /** Ends the open streams of a command: normally, with a declared error, or with a failure. */
  streamEnd(codec: CommandCodec, answer: Exclude<Answer, { kind: "hang" }>): void {
    for (const [id, stream] of this.#openOf(codec)) {
      this.#openStreams.delete(id);
      switch (answer.kind) {
        case "value":
          this.#deliver({ type: "end", id, ok: true, payload: "" });
          break;
        case "error":
          this.#deliver({ type: "end", id, ok: false, payload: codec.error!(answer.code, answer.payload) });
          break;
        case "failure":
          this.#deliver({ type: "failure", id, ...answer.error, itemsSent: stream.sent, payload: "" });
          break;
      }
    }
  }

  /** The page opened a stream: its request is recorded, and values pushed before it opened go to it. */
  #stream(message: BridgeMessage): void {
    const id = message.id!;
    const codec = this.#byName.get(`${message.contract}.${message.command}`);
    if (codec === undefined) {
      console.error(`fakeSpar: the page opened ${message.contract}.${message.command}, which the app does not have.`);
      this.#deliver({ type: "failure", id, kind: "rejected", code: "unknown-command", execution: "notStarted", itemsSent: 0, payload: "" });
      return;
    }

    this.#record(codec, codec.request === undefined ? undefined : codec.request(message.payload));
    const stream = { codec, sent: 0 };
    this.#openStreams.set(id, stream);
    for (const values of this.#waitingItems.get(codec)?.splice(0) ?? []) {
      this.#deliverItems(id, stream, values);
    }
  }

  #deliverItems(id: number, stream: { sent: number }, values: readonly unknown[]): void {
    const codec = this.#openStreams.get(id)!.codec;
    stream.sent += values.length;
    this.#deliver({ type: "items", id, payload: `[${values.map(value => codec.value!(value)).join(",")}]` });
  }

  #openOf(codec: CommandCodec): Array<[number, { readonly codec: CommandCodec; sent: number }]> {
    return [...this.#openStreams].filter(([, stream]) => stream.codec === codec);
  }

  /** The page gave up waiting: a call still running is stopped, and the backend confirms it. */
  #cancel(id: number): void {
    const stream = this.#openStreams.get(id);
    if (stream !== undefined) {
      this.#openStreams.delete(id);
      this.#deliver({ type: "failure", id, kind: "cancelled", code: "cancelled", execution: "stopped", itemsSent: stream.sent, payload: "" });
      return;
    }

    const flight = this.#inFlight.get(id);
    if (flight !== undefined) {
      this.#settle(id, flight, () => this.#fail(id, { kind: "cancelled", code: "cancelled", execution: "stopped" }));
    }
  }

  #settle(id: number, flight: { settled: boolean }, send: () => void): void {
    if (flight.settled) {
      return;
    }

    flight.settled = true;
    this.#inFlight.delete(id);
    send();
  }

  #fail(id: number, error: ScriptedFailure): void {
    this.#deliver({ type: "failure", id, ...error, payload: "" });
  }

  /** Answers arrive later, never inside the page's own send, as from a real host. */
  backendUpdate(update: FakeBackendUpdate): void {
    this.#revision++;
    const payload = JSON.stringify({ revision: this.#revision, time: new Date().toISOString(), types: [...(update.types ?? [])] });
    this.#deliver({ type: "event", name: "backend-updated", payload });
  }

  #deliver(message: BridgeMessage): void {
    const text = encode(message);
    queueMicrotask(() => this.#received?.(text));
  }
}

/** The page's `close-answer` payload, as the host reads it. */
function closeOutcomeOf(payload: string): GuardsOutcome {
  const parsed = JSON.parse(payload) as { readonly answer?: unknown; readonly question?: unknown };
  switch (parsed.answer) {
    case "close":
    case "keep":
    case "failed":
      return parsed.answer;
    case "ask":
      return { ask: String(parsed.question) };
    default:
      throw new Error(`fakeSpar: the page answered a close request with ${payload}.`);
  }
}
