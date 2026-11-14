import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import { startTelemetryIfEnabled, type TelemetryStartup } from "./telemetryBoot";
import { installCallBench } from "./callBench";

declare global {
  interface Window {
    sparTelemetryStartup?: TelemetryStartup;
  }
}

function mount(startup: TelemetryStartup) {
  window.sparTelemetryStartup = startup;
  if (__SPAR_BENCH__) installCallBench();
  createRoot(document.getElementById("root")!).render(
    <StrictMode>
      <App />
    </StrictMode>,
  );
}

// Telemetry starts before React registers its event listeners, so the interaction instrumentation sees them.
void startTelemetryIfEnabled().then(mount);
