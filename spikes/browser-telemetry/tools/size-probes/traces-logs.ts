// Size probe: tracing plus logs (logger provider, batch processor, OTLP/protobuf log exporter).
import { logs } from "@opentelemetry/api";
import { OTLPLogExporter } from "@opentelemetry/exporter-logs-otlp-proto";
import { resourceFromAttributes } from "@opentelemetry/resources";
import { BatchLogRecordProcessor, LoggerProvider } from "@opentelemetry/sdk-logs";
import { start as startTraces } from "./traces";

export function start(): void {
  startTraces();
  logs.setGlobalLoggerProvider(new LoggerProvider({
    resource: resourceFromAttributes({ "service.name": "probe" }),
    processors: [new BatchLogRecordProcessor({ exporter: new OTLPLogExporter({ url: "/_spar/otlp/v1/logs" }) })],
  }));
}
