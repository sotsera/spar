import { hostHeaders } from "@spar/sdk";
import type { PageTelemetryConfig } from "./telemetry";

/** What startup measured, for the spike's drivers. */
export interface TelemetryStartup {
  readonly compiledIn: boolean;
  readonly enabled: boolean;
  readonly probeMs: number;
  readonly loadMs: number;
}

function isConfig(value: unknown): value is PageTelemetryConfig {
  if (typeof value !== "object" || value === null) return false;
  const candidate = value as Record<string, unknown>;
  return typeof candidate["serviceName"] === "string"
    && typeof candidate["scheduledDelayMs"] === "number"
    && typeof candidate["logs"] === "boolean";
}

/**
 * Asks the host whether page telemetry is on (POST /_spar/otlp/config: 404 when the app has no telemetry layer or no
 * endpoint is configured) and loads the OpenTelemetry chunk only when it is. Never rejects: telemetry must not stop the app.
 */
export async function startTelemetryIfEnabled(): Promise<TelemetryStartup> {
  if (!__SPAR_TELEMETRY__) return { compiledIn: false, enabled: false, probeMs: 0, loadMs: 0 };

  const started = performance.now();
  let config: unknown;
  try {
    const response = await fetch("/_spar/otlp/config", { method: "POST", headers: hostHeaders() });
    config = response.ok ? await response.json() : undefined;
  } catch {
    config = undefined;
  }

  const probed = performance.now();
  if (!isConfig(config)) return { compiledIn: true, enabled: false, probeMs: probed - started, loadMs: 0 };

  try {
    const { startTelemetry } = await import("./telemetry");
    startTelemetry(config);
    return { compiledIn: true, enabled: true, probeMs: probed - started, loadMs: performance.now() - probed };
  } catch (error) {
    console.warn("Page telemetry failed to start", error);
    return { compiledIn: true, enabled: false, probeMs: probed - started, loadMs: performance.now() - probed };
  }
}
