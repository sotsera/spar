// The page's telemetry chunk (section 6b; decision "M2d's decide-first questions", item 3): OpenTelemetry's tracer, a
// span per bridge call whose `traceparent` the backend's span continues, the document's load, and spans the app opens
// with `traced`. Its spans go to the host's relay at `/_spar/otlp/v1/traces`, which forwards them to the app's OTLP
// endpoint with headers the page never sees. Prebundled with its OpenTelemetry packages when the SDK is built, and
// loaded lazily, only when the app turned telemetry on and the host gave an endpoint; it never loads otherwise.
import { context, SpanKind, SpanStatusCode, trace, type Span } from "@opentelemetry/api";
import { OTLPTraceExporter } from "@opentelemetry/exporter-trace-otlp-proto";
import { registerInstrumentations } from "@opentelemetry/instrumentation";
import { DocumentLoadInstrumentation } from "@opentelemetry/instrumentation-document-load";
import { resourceFromAttributes } from "@opentelemetry/resources";
import { BatchSpanProcessor, StackContextManager, TracerProvider } from "@opentelemetry/sdk-trace";
import type { CallErrorKind } from "../results.js";
import type { CallSpan, RelayAddress, TelemetrySettings, Tracer } from "../tracing.js";

export interface Telemetry {
  readonly tracer: Tracer;
  /** Exports the spans still buffered; the SDK gives it at most a second while the window closes. */
  flush(): Promise<void>;
}

export function startTelemetry(settings: TelemetrySettings, relay: RelayAddress): Telemetry {
  // Synchronous context only: the page keeps none across an `await` (no `zone.js`), which is what `traced` documents.
  context.setGlobalContextManager(new StackContextManager().enable());

  const exporter = new OTLPTraceExporter({
    url: `${relay.origin}/_spar/otlp/v1/traces`,
    headers: { ...relay.headers },
  });
  const provider = new TracerProvider({
    resource: resourceFromAttributes({ "service.name": settings.serviceName }),
    spanProcessors: [new BatchSpanProcessor({ exporter, scheduledDelayMillis: settings.delayMs })],
  });

  registerInstrumentations({ tracerProvider: provider, instrumentations: [new DocumentLoadInstrumentation()] });

  return {
    tracer: new PageTracer(provider.getTracer("spar")),
    flush: () => provider.forceFlush(),
  };
}

/** Each call's span, and `traced`'s, as a child of the span active when it starts. */
class PageTracer implements Tracer {
  readonly #tracer: ReturnType<TracerProvider["getTracer"]>;

  constructor(tracer: ReturnType<TracerProvider["getTracer"]>) {
    this.#tracer = tracer;
  }

  startCall(contract: string, command: string): CallSpan {
    const span = this.#tracer.startSpan(
      `${contract}/${command}`,
      { kind: SpanKind.CLIENT, attributes: { "rpc.system": "spar", "rpc.service": contract, "rpc.method": command } },
      context.active(),
    );
    return {
      traceparent: traceparentOf(span),
      end: (outcome, code) => endCall(span, outcome, code),
      set: (name, value) => span.setAttribute(name, value),
    };
  }

  traced<T>(name: string, run: () => T): T {
    const span = this.#tracer.startSpan(name, { kind: SpanKind.INTERNAL }, context.active());
    let result: T;
    try {
      result = context.with(trace.setSpan(context.active(), span), run);
    } catch (error) {
      failSpan(span, error);
      span.end();
      throw error;
    }

    if (!isThenable(result)) {
      span.end();
      return result;
    }

    result.then(
      () => span.end(),
      (error: unknown) => {
        failSpan(span, error);
        span.end();
      },
    );
    return result;
  }
}

/** The W3C header for the span: version 00, the ids, and whether it was sampled. */
function traceparentOf(span: Span): string {
  const ids = span.spanContext();
  const sampled = (ids.traceFlags & 1) === 1 ? "01" : "00";
  return `00-${ids.traceId}-${ids.spanId}-${sampled}`;
}

/** Tagged like the backend's span: a declared error is an outcome, only a failure has a type and an error status. */
function endCall(span: Span, outcome: "success" | "error" | CallErrorKind, code: string | undefined): void {
  span.setAttribute("spar.outcome", outcome);
  if (outcome === "success" || outcome === "error") {
    span.end();
    return;
  }

  if (code !== undefined) {
    span.setAttribute("error.type", code);
  }

  span.setStatus({ code: SpanStatusCode.ERROR, ...(code === undefined ? {} : { message: code }) });
  span.end();
}

function failSpan(span: Span, error: unknown): void {
  const name = error instanceof Error ? error.name : typeof error;
  span.setAttribute("error.type", name);
  span.setStatus({ code: SpanStatusCode.ERROR });
}

function isThenable(value: unknown): value is PromiseLike<unknown> {
  return (typeof value === "object" || typeof value === "function")
    && value !== null
    && typeof (value as { then?: unknown }).then === "function";
}
