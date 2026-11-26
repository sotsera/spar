// Starts the page's telemetry when the app turned it on (decision "M2d's decide-first questions", item 3). The bundle
// carries this only when `Program.cs` calls `spar.Telemetry(…)`: the Vite plugin defines `import.meta.env.SPAR_TELEMETRY`
// from the build, and `vite build` drops the code below, the lazy chunk with it, when it is false. At run time the
// chunk loads only when the host has an endpoint: a desktop host says so in its injected script, a loopback host
// answers `/_spar/otlp/config`. Calls made before the chunk is in have no span.
import type { BridgeChannel, Connection } from "./connection.js";
import { setTracer, type RelayAddress, type TelemetrySettings } from "./tracing.js";

/** How long the closing window waits for the spans still buffered. */
const flushLimitMs = 1_000;

/** The relay's address on a loopback channel, which carries the session's token; desktop hosts need none. */
export interface TelemetryChannel extends BridgeChannel {
  telemetryRelay?(): RelayAddress;
}

export async function startPageTelemetry(connection: Connection, channel: TelemetryChannel): Promise<void> {
  const relay = channel.telemetryRelay?.();
  const settings = relay === undefined ? injectedSettings() : await fetchedSettings(relay);
  if (settings === undefined) {
    return;
  }

  const { startTelemetry } = await import("./telemetry/index.js");
  const telemetry = startTelemetry(settings, relay ?? { origin: pageOrigin(), headers: {} });
  setTracer(telemetry.tracer);
  connection.onClosing(() => withinLimit(telemetry.flush()));
}

/** `globalThis.__sparTelemetry`, which the desktop host's injected script sets only when it has an endpoint. */
function injectedSettings(): TelemetrySettings | undefined {
  const value = (globalThis as { __sparTelemetry?: unknown }).__sparTelemetry;
  return settingsOf(value);
}

async function fetchedSettings(relay: RelayAddress): Promise<TelemetrySettings | undefined> {
  try {
    const response = await fetch(`${relay.origin}/_spar/otlp/config`, { method: "POST", headers: { ...relay.headers } });
    // 204: telemetry is off (no endpoint, or Program.cs never called spar.Telemetry()).
    if (!response.ok || response.status === 204) {
      return undefined;
    }

    return settingsOf(await response.json());
  } catch {
    return undefined;
  }
}

/** Read field by field: the value comes from outside the bundle. */
function settingsOf(value: unknown): TelemetrySettings | undefined {
  if (typeof value !== "object" || value === null) {
    return undefined;
  }

  const serviceName: unknown = (value as { serviceName?: unknown }).serviceName;
  const delayMs: unknown = (value as { delayMs?: unknown }).delayMs;
  if (typeof serviceName !== "string" || typeof delayMs !== "number" || !Number.isFinite(delayMs) || delayMs <= 0) {
    return undefined;
  }

  return { serviceName, delayMs };
}

/** The page's origin from its address: `location.origin` is `"null"` for some custom schemes. */
function pageOrigin(): string {
  const address = new URL(location.href);
  return `${address.protocol}//${address.host}`;
}

function withinLimit(work: Promise<void>): Promise<void> {
  return new Promise(resolve => {
    const timer = setTimeout(resolve, flushLimitMs);
    work.then(
      () => {
        clearTimeout(timer);
        resolve();
      },
      () => {
        clearTimeout(timer);
        resolve();
      },
    );
  });
}
