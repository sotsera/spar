// Page runner for WebView2 (Chromium/V8) and WebKitGTK (JSC). Query: ?engine=<name>&quick=1, or
// ?engine=<name>&mode=order (same-variant settle order), or ?engine=<name>&mode=memory&variant=<key>&style=<none|await|match|unwrap|control>&count=<n> for one memory reading.
// Reports through one of the host channels:
// - WebKitGTK: window.webkit.messageHandlers.bench (result) and .rss (RSS requests, answered via window.__rssReply);
// - WebView2 via the desktop-origin host: POST /results to the static server, then GET /_spar/done (closes the host).
import { runAll, runMemory, runOrderChecks, type Host } from "./bench.js";

interface WebKitWindow {
  webkit?: { messageHandlers: Record<string, { postMessage(value: unknown): void }> };
  __rssReply?: (bytes: number) => void;
  gc?: () => void;
}

const w = window as unknown as WebKitWindow;
const query = new URLSearchParams(location.search);
const engine = query.get("engine") ?? "browser";
const quick = query.get("quick") === "1";
const status = document.getElementById("status")!;

const channel = new MessageChannel();
const waiting: (() => void)[] = [];
channel.port1.onmessage = () => waiting.shift()!();

// performance.memory returns a new snapshot object on every read, so read it each time.
const perf = performance as unknown as { readonly memory?: { readonly usedJSHeapSize: number } };
const memory = perf.memory !== undefined;
const handlers = w.webkit?.messageHandlers;

let rssWaiting: ((bytes: number) => void) | undefined;
w.__rssReply = bytes => { const resolve = rssWaiting!; rssWaiting = undefined; resolve(bytes); };

const host: Host = {
  engine,
  details: { userAgent: navigator.userAgent, hardwareConcurrency: navigator.hardwareConcurrency, heapCounter: memory ? "performance.memory" : "none", gc: typeof w.gc === "function" },
  now: () => performance.now(),
  nextTask: () => new Promise(resolve => { waiting.push(resolve); channel.port2.postMessage(0); }),
  settleEvents: () => new Promise(resolve => setTimeout(resolve, 30)),
  heapUsed: memory ? () => perf.memory!.usedJSHeapSize : undefined,
  gc: typeof w.gc === "function" ? w.gc : undefined,
  rss: !memory && handlers?.rss ? () => new Promise<number>(resolve => { rssWaiting = resolve; handlers.rss!.postMessage("rss"); }) : undefined,
  onUnhandledRejection: listener => window.addEventListener("unhandledrejection", event => { event.preventDefault(); listener(); }),
  log: line => { status.textContent = line; console.log(`[${engine}] ${line}`); },
};

async function main(): Promise<void> {
  let body: string;
  try {
    const report = query.get("mode") === "order" ? await runOrderChecks(host) : query.get("mode") === "memory"
      ? await runMemory(host, query.get("variant") ?? "", query.get("style") ?? "none", Number(query.get("count") ?? 100_000))
      : await runAll(host, quick
        ? { sampleMs: 20, warmupRounds: 1, rounds: 3, burstSize: 10_000, burstRounds: 2, retainedCount: 20_000 }
        : { sampleMs: 60, warmupRounds: 3, rounds: 7, burstSize: 10_000, burstRounds: 15, retainedCount: 20_000 });
    body = JSON.stringify(report);
  } catch (error) {
    body = JSON.stringify({ engine, error: String(error), stack: (error as Error).stack ?? "" });
  }

  if (handlers?.bench) {
    handlers.bench.postMessage(body);
    return;
  }

  await fetch("/results", { method: "POST", body, headers: { "Content-Type": "application/json" } });
  await fetch("/_spar/done");
}

void main();
