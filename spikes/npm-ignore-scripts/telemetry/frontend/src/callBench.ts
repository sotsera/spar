import { notes } from "@spar/notes";
import { configure } from "@spar/sdk";

declare global {
  interface Window {
    sparCallBench?: (options: { transport: "fetch" | "message"; n: number }) => Promise<{ medianMs: number; p95Ms: number; meanMs: number }>;
  }
}

/** Spike only (bench builds): sequential notes.count calls through the SDK, timed in the page. */
export function installCallBench(): void {
  window.sparCallBench = async ({ transport, n }) => {
    configure({ unary: transport });
    for (let i = 0; i < 10; i++) await notes.count();
    const times: number[] = [];
    for (let i = 0; i < n; i++) {
      const started = performance.now();
      await notes.count();
      times.push(performance.now() - started);
    }

    configure({ unary: "fetch" });
    times.sort((a, b) => a - b);
    const round = (value: number) => Math.round(value * 1000) / 1000;
    return {
      medianMs: round(times[Math.floor(n / 2)]!),
      p95Ms: round(times[Math.min(n - 1, Math.floor(n * 0.95))]!),
      meanMs: round(times.reduce((sum, t) => sum + t, 0) / n),
    };
  };
}
