// What the Spar SDK does first: read the host's injected settings synchronously, and listen for CSP violations.
interface HostSettings {
  readonly injectedAt: number;
  readonly telemetry?: { readonly serviceName: string; readonly delayMs: number };
}

declare global {
  interface Window {
    __spar?: HostSettings;
    __sparOrder?: string[];
    __sparEarlyViolations?: unknown[];
  }
}

const order = (window.__sparOrder ??= []);
order.push("sdk-boot:spar=" + typeof window.__spar);

/** Read synchronously at boot: the desktop hosts inject it before any page script runs. */
export const hostSettings: HostSettings | null = window.__spar ?? null;

export interface Violation {
  readonly directive: string;
  readonly blocked: string;
  readonly sample: string;
}

export const violations: Violation[] = [];

export async function send(message: object): Promise<void> {
  const text = JSON.stringify(message);
  console.log("SPAR_PAGE " + text);
  try {
    await fetch("/_spar/log", { method: "POST", body: text });
  } catch (error) {
    console.log("SPAR_LOG_FAILED " + String(error));
  }
}

document.addEventListener("securitypolicyviolation", e => {
  const violation = { directive: e.effectiveDirective, blocked: e.blockedURI, sample: e.sample };
  violations.push(violation);
  void send({ kind: "violation", via: "sdk", ...violation });
});
