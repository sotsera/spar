// Size probe: tracing only (provider, batch processor, OTLP/protobuf exporter, stack context, W3C propagator).
import { context, propagation, trace } from "@opentelemetry/api";
import { W3CTraceContextPropagator } from "@opentelemetry/core";
import { OTLPTraceExporter } from "@opentelemetry/exporter-trace-otlp-proto";
import { resourceFromAttributes } from "@opentelemetry/resources";
import { BatchSpanProcessor, StackContextManager, TracerProvider } from "@opentelemetry/sdk-trace";

export function start(): void {
  const provider = new TracerProvider({
    resource: resourceFromAttributes({ "service.name": "probe" }),
    spanProcessors: [new BatchSpanProcessor({ exporter: new OTLPTraceExporter({ url: "/_spar/otlp/v1/traces" }) })],
  });
  context.setGlobalContextManager(new StackContextManager().enable());
  propagation.setGlobalPropagator(new W3CTraceContextPropagator());
  trace.setGlobalTracerProvider(provider);
}
