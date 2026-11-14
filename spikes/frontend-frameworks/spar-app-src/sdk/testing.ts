// Not part of the SDK prototype: a scripted fake transport for this spike, installed through core.ts's setTransport.
// Unary calls take their next step from a per-command queue (the last step repeats). Streams play a list of items.
// Every start, settle, abort and early return is logged, so pages can count backend calls (retries) and aborts.
import { setTransport, type CallError, type CallOptions, type Outcome } from "./core.js";
import { Call, Result } from "./c-call/sdk.js";

export type Step =
  | { readonly kind: "success"; readonly value?: unknown; readonly delayMs?: number }
  | { readonly kind: "error"; readonly code: string; readonly payload?: object; readonly delayMs?: number }
  | { readonly kind: "failure"; readonly error: Omit<CallError, "call">; readonly delayMs?: number }
  | { readonly kind: "hang" };

export interface LogEntry {
  readonly id: number;
  readonly call: string;
  readonly event: "start" | "settled" | "aborted" | "returned" | "item";
  readonly at: number;
  readonly detail?: unknown;
}

const unary = new Map<string, Step[]>();
const streams = new Map<string, Step[]>();
export const log: LogEntry[] = [];
let nextId = 1;

function record(id: number, call: string, event: LogEntry["event"], detail?: unknown): void {
  log.push({ id, call, event, at: performance.now(), detail });
}

/** Replaces the queue of steps for a unary command. */
export function script(call: string, ...steps: Step[]): void {
  unary.set(call, [...steps]);
}

/** Replaces the items of a stream or subscription. */
export function scriptStream(call: string, ...items: Step[]): void {
  streams.set(call, [...items]);
}

export function reset(): void {
  unary.clear();
  streams.clear();
  log.length = 0;
}

export function count(call: string, event: LogEntry["event"] = "start"): number {
  return log.filter(e => e.call === call && e.event === event).length;
}

function cancelled(call: string, execution: CallError["execution"]): Outcome {
  return { ok: false, call, error: { kind: "cancelled", code: "aborted", message: `${call} was cancelled`, execution, call } };
}

function toOutcome(call: string, step: Exclude<Step, { kind: "hang" }>): Outcome {
  switch (step.kind) {
    case "success": return { ok: true, value: step.value };
    case "error": return { ok: false, call, code: step.code, payload: step.payload ?? {} };
    case "failure": return { ok: false, call, error: { ...step.error, call } };
  }
}

const sleep = (ms: number, signal: AbortSignal | undefined): Promise<boolean> =>
  new Promise(resolve => {
    if (signal?.aborted) { resolve(false); return; }
    const timer = setTimeout(() => { signal?.removeEventListener("abort", onAbort); resolve(true); }, ms);
    const onAbort = (): void => { clearTimeout(timer); resolve(false); };
    signal?.addEventListener("abort", onAbort, { once: true });
  });

function runUnary(call: string, _request: unknown, options: CallOptions | undefined): Promise<Outcome> {
  const id = nextId++;
  record(id, call, "start");
  const signal = options?.signal;
  if (signal?.aborted) {
    record(id, call, "aborted", "before start");
    return Promise.resolve(cancelled(call, "notStarted"));
  }
  const queue = unary.get(call) ?? [{ kind: "failure", error: { kind: "internal", code: "unscripted", message: `no script for ${call}`, execution: "notStarted" } }];
  const step = queue.length > 1 ? queue.shift()! : queue[0]!;
  return (async () => {
    const delay = step.kind === "hang" ? 1e9 : step.delayMs ?? 0;
    const finished = await sleep(delay, signal);
    if (!finished || step.kind === "hang") {
      record(id, call, "aborted");
      return cancelled(call, "unknown");
    }
    record(id, call, "settled", step.kind);
    return toOutcome(call, step);
  })();
}

async function* runStream(call: string, _request: unknown, options: CallOptions | undefined): AsyncIterable<Outcome> {
  const id = nextId++;
  record(id, call, "start");
  const signal = options?.signal;
  let completed = false;
  try {
    for (const step of streams.get(call) ?? []) {
      const delay = step.kind === "hang" ? 1e9 : step.delayMs ?? 0;
      if (!(await sleep(delay, signal)) || step.kind === "hang") {
        record(id, call, "aborted");
        // In-band terminal failure, as every stream item is a Result (docs/app-model.md 6.2).
        yield cancelled(call, "completed");
        completed = true;
        return;
      }
      record(id, call, "item", step.kind);
      yield toOutcome(call, step);
    }
    completed = true;
    record(id, call, "settled");
  } finally {
    if (!completed) record(id, call, "returned");
  }
}

export function installFakeTransport(): void {
  setTransport(runUnary, runStream);
}

// Test doubles for pages that need a Result or Call without a transport.
export function successResult<T>(value: T): Result<T> {
  return new Result<T>({ ok: true, value });
}

export function errorResult<T, E extends object>(code: keyof E & string, payload: object): Result<T, E> {
  return new Result<T, E>({ ok: false, call: "test", code, payload });
}

export function resolvedCall<T>(value: T): Call<T> {
  return new Call(Promise.resolve(successResult(value)));
}

export type { Outcome };
