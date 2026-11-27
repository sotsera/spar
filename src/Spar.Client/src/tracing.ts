// The SDK's tracing hook (section 6b; decision "M2d's decide-first questions", item 3). The core SDK carries only this:
// a place where the optional telemetry chunk, once loaded, starts a span for each call and gives its `traceparent`,
// and `traced`, which groups an interaction's calls under one span. Without the chunk both do nothing at all.
import type { CallErrorKind } from "./results.js";

/**
 * A call's span while it runs: its W3C `traceparent`, sent with the call, and its end. The outcome is named as on the
 * backend's span: `success`, `error` for a declared error, or the failure's `CallError` kind.
 */
export interface CallSpan {
  readonly traceparent: string;
  end(outcome: "success" | "error" | CallErrorKind, code?: string): void;
  /** Sets one attribute before the span ends, such as a stream's item count. */
  set(name: string, value: number | string): void;
}

/** What the telemetry chunk provides. */
export interface Tracer {
  startCall(contract: string, command: string): CallSpan | undefined;
  traced<T>(name: string, run: () => T): T;
}

/** What the host tells the page: the service name its spans report under and the exporter's batch delay. */
export interface TelemetrySettings {
  readonly serviceName: string;
  readonly delayMs: number;
}

/** Where the host's relay is: its origin and the headers its requests need (the loopback token). */
export interface RelayAddress {
  readonly origin: string;
  readonly headers: Readonly<Record<string, string>>;
}

let tracer: Tracer | undefined;

/** @internal The telemetry chunk's tracer, set once it started; undefined again only in tests. */
export function setTracer(value: Tracer | undefined): void {
  tracer = value;
}

/** @internal A span for one call, when telemetry is on. */
export function traceCall(contract: string, command: string): CallSpan | undefined {
  return tracer?.startCall(contract, command);
}

/**
 * Groups the calls `run` starts under one span named `name`, when the app's telemetry is on; otherwise it only runs
 * `run`. The calls started before `run`'s first `await` are the span's children (the page keeps no context across an
 * `await`). The span ends when `run` returns, or when the promise it returns settles.
 *
 * ```ts
 * await traced("save page", () => pages.save(draft));
 * ```
 */
export function traced<T>(name: string, run: () => T): T {
  return tracer === undefined ? run() : tracer.traced(name, run);
}
