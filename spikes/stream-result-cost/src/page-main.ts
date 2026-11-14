// Page runner for WebView2 (Chromium/V8) and WebKitGTK (JSC). Query:
//   ?engine=<name>[&quick=1]                         full benchmark
//   ?engine=<name>&mode=alloc                        allocation per item (V8; run with a large young generation)
//   ?engine=<name>&mode=retained&combo=<key>&window=<batches>   one retained-memory reading (fresh process each)
// Reports through one of the host channels:
// - WebKitGTK: window.webkit.messageHandlers.bench (result) and .proc (RSS/CPU requests, answered via window.__procReply);
// - WebView2 via the desktop-origin host: POST /results to the static server, then GET /_spar/done (closes the host).
import { runAll, runAllocation, runRetainedOnly, type Host } from "./bench.js";

interface PageWindow {
  webkit?: { messageHandlers: Record<string, { postMessage(value: unknown): void }> };
  __procReply?: (rss: number, cpuMs: number) => void;
  gc?: () => void;
}

const w = window as unknown as PageWindow;
const query = new URLSearchParams(location.search);
const engine = query.get("engine") ?? "browser";
const quick = query.get("quick") === "1";
const only = query.get("only")?.split(",");
const status = document.getElementById("status")!;

const channel = new MessageChannel();
const waiting: (() => void)[] = [];
channel.port1.onmessage = () => waiting.shift()!();

// performance.memory returns a new snapshot object on every read, so read it each time.
const perf = performance as unknown as { readonly memory?: { readonly usedJSHeapSize: number } };
const memory = perf.memory !== undefined;
const handlers = w.webkit?.messageHandlers;

let procWaiting: ((value: { rss: number; cpuMs: number }) => void) | undefined;
w.__procReply = (rss, cpuMs) => { const resolve = procWaiting!; procWaiting = undefined; resolve({ rss, cpuMs }); };

const entries: { type: string; start: number; duration: number }[] = [];
let longTaskObserver = false;
try {
  new PerformanceObserver(list => {
    for (const e of list.getEntries()) entries.push({ type: e.entryType, start: e.startTime, duration: e.duration });
  }).observe({ type: "longtask", buffered: true });
  longTaskObserver = PerformanceObserver.supportedEntryTypes.includes("longtask");
} catch {
  longTaskObserver = false;
}

const host: Host = {
  engine,
  details: {
    userAgent: navigator.userAgent, hardwareConcurrency: navigator.hardwareConcurrency, heapCounter: memory ? "performance.memory" : "none",
    gc: typeof w.gc === "function", crossOriginIsolated: globalThis.crossOriginIsolated === true, visibilityState: document.visibilityState,
    longTaskObserver,
  },
  now: () => performance.now(),
  nextTask: () => new Promise(resolve => { waiting.push(resolve); channel.port2.postMessage(0); }),
  timer: ms => new Promise(resolve => setTimeout(resolve, ms)),
  heapUsed: memory ? () => perf.memory!.usedJSHeapSize : undefined,
  gc: typeof w.gc === "function" ? w.gc : undefined,
  proc: !memory && handlers?.["proc"] ? () => new Promise(resolve => { procWaiting = resolve; handlers["proc"]!.postMessage("proc"); }) : undefined,
  entries: () => entries,
  log: line => { status.textContent = line; console.log(`[${engine}] ${line}`); },
};

async function main(): Promise<void> {
  let body: string;
  try {
    const mode = query.get("mode");
    const report = mode === "alloc" ? await runAllocation(host, quick ? 3 : 15, only)
      : mode === "retained" ? await runRetainedOnly(host, query.get("combo") ?? "plain", Number(query.get("window") ?? 8))
      : await runAll(host, quick
        ? { rounds100k: 2, rounds1M: 1, roundsSlow: 1, pushSeconds: 1, pushRate: 20_000, retainedReps: 1, only }
        : { rounds100k: 7, rounds1M: 3, roundsSlow: 5, pushSeconds: 3, pushRate: 20_000, retainedReps: 3, only });
    (report["details"] as Record<string, unknown>)["visibilityStateAtEnd"] = document.visibilityState;
    body = JSON.stringify(report);
  } catch (error) {
    body = JSON.stringify({ engine, error: String(error), stack: (error as Error).stack ?? "" });
  }

  if (handlers?.["bench"]) {
    handlers["bench"].postMessage(body);
    return;
  }

  await fetch("/results", { method: "POST", body, headers: { "Content-Type": "application/json" } });
  await fetch("/_spar/done");
}

void main();
