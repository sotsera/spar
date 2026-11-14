import { context, logs, propagation, SeverityNumber, SpanKind, SpanStatusCode, trace } from "@opentelemetry/api";
import { W3CTraceContextPropagator } from "@opentelemetry/core";
import { OTLPLogExporter } from "@opentelemetry/exporter-logs-otlp-proto";
import { OTLPTraceExporter } from "@opentelemetry/exporter-trace-otlp-proto";
import { registerInstrumentations } from "@opentelemetry/instrumentation";
import { DocumentLoadInstrumentation } from "@opentelemetry/instrumentation-document-load";
import { UserInteractionInstrumentation } from "@opentelemetry/instrumentation-user-interaction";
import { resourceFromAttributes } from "@opentelemetry/resources";
import { BatchLogRecordProcessor, LoggerProvider } from "@opentelemetry/sdk-logs";
import { BatchSpanProcessor, StackContextManager, TracerProvider } from "@opentelemetry/sdk-trace";
import { configure, hostHeaders, onClosing, type CallOutcome, type CallTracer, type TraceContext } from "@spar/sdk";

/** What the host's telemetry layer tells the page (POST /_spar/otlp/config). No endpoint, no credentials. */
export interface PageTelemetryConfig {
  readonly serviceName: string;
  readonly scheduledDelayMs: number;
  readonly logs: boolean;
}

/** Spike hooks for the drivers: flush on demand, counters, and the last trace a call joined. */
export interface PageTelemetryHooks {
  /** Exports what is buffered; false when the export failed (it never rejects). */
  flush(): Promise<boolean>;
  setTracing(on: boolean): void;
  readonly stats: { callSpans: number; lastTraceId: string; lastCall: string };
}

declare global {
  interface Window {
    sparTelemetry?: PageTelemetryHooks;
  }
}

/**
 * Starts the page's OpenTelemetry SDK: traces (document load, clicks and submits, one client span per Spar call) and
 * logs, exported over OTLP/protobuf to the page's own origin, where the host relays them. The SDK gets a CallTracer,
 * so every call carries W3C trace context and the backend's span joins the page's trace.
 */
export function startTelemetry(config: PageTelemetryConfig): void {
  const resource = resourceFromAttributes({ "service.name": config.serviceName, "telemetry.sdk.language": "webjs" });

  // Same-origin, relative paths: the host relays to the collector, so the page needs no CORS and holds no keys.
  // hostHeaders() is the loopback token (empty on desktop hosts, which check the origin instead).
  const headers = hostHeaders();
  const traceExporter = new OTLPTraceExporter({ url: "/_spar/otlp/v1/traces", headers });
  const tracerProvider = new TracerProvider({
    resource,
    spanProcessors: [new BatchSpanProcessor({ exporter: traceExporter, scheduledDelayMillis: config.scheduledDelayMs })],
  });

  context.setGlobalContextManager(new StackContextManager().enable());
  propagation.setGlobalPropagator(new W3CTraceContextPropagator());
  trace.setGlobalTracerProvider(tracerProvider);

  const loggerProvider = config.logs
    ? new LoggerProvider({
      resource,
      processors: [new BatchLogRecordProcessor({ exporter: new OTLPLogExporter({ url: "/_spar/otlp/v1/logs", headers }), scheduledDelayMillis: config.scheduledDelayMs })],
    })
    : undefined;
  if (loggerProvider) logs.setGlobalLoggerProvider(loggerProvider);
  const logger = logs.getLogger("spar-page");

  // Clicks and form submissions become spans; a Spar call made synchronously in the handler becomes their child.
  // Without zone.js the interaction span ends when the handler returns; the call span outlives it, which is valid.
  registerInstrumentations({
    instrumentations: [
      new DocumentLoadInstrumentation(),
      new UserInteractionInstrumentation({ eventNames: ["click", "submit"] }),
    ],
  });

  const stats = { callSpans: 0, lastTraceId: "", lastCall: "" };
  const tracer = trace.getTracer("spar-sdk");
  const callTracer: CallTracer = {
    start(contract, command) {
      const span = tracer.startSpan(`${contract}/${command}`, {
        kind: SpanKind.CLIENT,
        attributes: { "rpc.system": "spar", "rpc.service": contract, "rpc.method": command },
      });
      const carrier: Record<string, string> = {};
      propagation.inject(trace.setSpan(context.active(), span), carrier);
      const traceparent = carrier["traceparent"];
      if (traceparent === undefined) {
        span.end();
        return undefined;
      }

      stats.callSpans++;
      stats.lastTraceId = span.spanContext().traceId;
      stats.lastCall = `${contract}/${command}`;
      const tracestate = carrier["tracestate"];
      const sent: TraceContext = tracestate === undefined ? { traceparent } : { traceparent, tracestate };
      return {
        context: sent,
        end(outcome: CallOutcome, error?: Error) {
          span.setAttribute("spar.outcome", outcome);
          if (outcome !== "ok") {
            span.setStatus({ code: SpanStatusCode.ERROR, message: error?.message ?? outcome });
            context.with(trace.setSpan(context.active(), span), () => logger.emit({
              severityNumber: SeverityNumber.WARN,
              severityText: "WARN",
              body: `${contract}.${command} ${outcome}: ${error?.message ?? ""}`,
            }));
          }

          span.end();
        },
      };
    },
  };
  configure({ tracer: callTracer });

  addEventListener("error", event => logger.emit({ severityNumber: SeverityNumber.ERROR, severityText: "ERROR", body: `Uncaught: ${event.message}` }));
  addEventListener("unhandledrejection", event => logger.emit({ severityNumber: SeverityNumber.ERROR, severityText: "ERROR", body: `Unhandled rejection: ${String(event.reason)}` }));
  logger.emit({ severityNumber: SeverityNumber.INFO, severityText: "INFO", body: "page telemetry started" });

  // Telemetry never fails the app: an export the collector refused is reported as false, not as a rejection.
  const flush = async (): Promise<boolean> => {
    try {
      await Promise.all([tracerProvider.forceFlush(), loggerProvider?.forceFlush()]);
      return true;
    } catch {
      return false;
    }
  };

  // A desktop window can close before the next scheduled export: the host asks first, and waits briefly.
  onClosing(flush);

  window.sparTelemetry = {
    flush,
    setTracing(on: boolean) {
      configure({ tracer: on ? callTracer : undefined });
    },
    stats,
  };
}
