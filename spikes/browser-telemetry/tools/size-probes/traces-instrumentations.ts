// Size probe: tracing plus the document-load and user-interaction instrumentations (no logs).
import { registerInstrumentations } from "@opentelemetry/instrumentation";
import { DocumentLoadInstrumentation } from "@opentelemetry/instrumentation-document-load";
import { UserInteractionInstrumentation } from "@opentelemetry/instrumentation-user-interaction";
import { start as startTraces } from "./traces";

export function start(): void {
  startTraces();
  registerInstrumentations({ instrumentations: [new DocumentLoadInstrumentation(), new UserInteractionInstrumentation()] });
}
