// In-page test harness shared by the three apps. Results go to window.__results; tools/run-csp.mjs reads them.
import { configureSpar, type CallError } from "@spar/app/sdk";
import { installFakeTransport, log, reset } from "@spar/app/testing";

export interface TestResult {
  readonly name: string;
  readonly ok: boolean;
  readonly detail: unknown;
}

declare global {
  interface Window {
    __results: TestResult[];
    __done: boolean;
    __log: typeof log;
  }
}

window.__results = [];
window.__done = false;
window.__log = log;

/** Infrastructure failures that reached the app-wide interceptor, in order. */
export const intercepted: CallError[] = [];

installFakeTransport();
configureSpar({ onFailure: e => { intercepted.push(e); } });

export class Check extends Error {}

export function expect(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Check(message);
}

export async function test(name: string, body: () => Promise<unknown>): Promise<void> {
  reset();
  intercepted.length = 0;
  try {
    const detail = await body();
    window.__results.push({ name, ok: true, detail: detail ?? null });
  } catch (e) {
    const detail = e instanceof Check ? e.message : e instanceof Error ? `${e.name}: ${e.message}\n${e.stack ?? ""}` : String(e);
    window.__results.push({ name, ok: false, detail });
  }
}

export const delay = (ms: number): Promise<void> => new Promise(resolve => setTimeout(resolve, ms));

/** Polls until `probe` returns true; throws with the last observed value. */
export async function waitFor(what: string, probe: () => boolean, timeoutMs = 3000, observe?: () => unknown): Promise<void> {
  const until = performance.now() + timeoutMs;
  while (performance.now() < until) {
    if (probe()) return;
    await delay(10);
  }
  throw new Check(`timed out waiting for ${what}; observed: ${JSON.stringify(observe?.() ?? null)}`);
}

export function host(): HTMLElement {
  const el = document.createElement("div");
  document.body.append(el);
  return el;
}

export function finish(): void {
  window.__done = true;
  const pass = window.__results.filter(r => r.ok).length;
  const p = document.createElement("p");
  p.id = "summary";
  p.textContent = `${pass}/${window.__results.length} passed`;
  document.body.append(p);
}

export const recipe = { source: "src", target: "dst", tables: ["dbo.Orders"] } as const;
export const plan = { planId: "p1", steps: [{ table: "dbo.Orders", estimatedRows: 120000n }] };

export { log };
