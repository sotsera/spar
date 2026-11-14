// Spike measurement page (built only into non-production bundles). Drivers call window.sparBench.* and read JSON.
import { lab, type MonitorEvent } from "@spar/lab";
import { notes } from "@spar/notes";
import {
  CancelledError,
  NotExecutedError,
  OutcomeUnknownError,
  configure,
  keepSubscribed,
  type SubscriptionEvent,
} from "@spar/sdk";

type StreamTransport = "fetch" | "channel";
type UnaryTransport = "fetch" | "message";

const sleep = (ms: number) => new Promise<void>(resolve => setTimeout(resolve, ms));
const mb = (bytes: number) => Math.round(bytes / 1024 / 1024 * 10) / 10;

function heap(): number {
  return (performance as unknown as { memory?: { usedJSHeapSize: number } }).memory?.usedJSHeapSize ?? 0;
}

function percentile(sorted: readonly number[], p: number): number {
  return sorted.length === 0 ? 0 : sorted[Math.min(sorted.length - 1, Math.floor(sorted.length * p))]!;
}

function round(value: number): number {
  return Math.round(value * 100) / 100;
}

function outcome(error: unknown): string {
  if (error instanceof CancelledError) return "cancelled";
  if (error instanceof OutcomeUnknownError) return `unknown:${error.reason}`;
  if (error instanceof NotExecutedError) return `not-executed:${error.code}`;
  return `other:${String(error)}`;
}

/** Main-thread health while something runs: long tasks (over 50 ms) and the longest gap between animation frames. */
function startProbe() {
  let longTasks = 0;
  let longTaskMs = 0;
  const observer = new PerformanceObserver(list => {
    for (const entry of list.getEntries()) {
      longTasks++;
      longTaskMs += entry.duration;
    }
  });
  observer.observe({ type: "longtask" });

  let last = 0;
  let maxGap = 0;
  let running = true;
  const frame = (time: number) => {
    if (last > 0) maxGap = Math.max(maxGap, time - last);
    last = time;
    if (running) requestAnimationFrame(frame);
  };
  requestAnimationFrame(frame);

  return {
    stop() {
      running = false;
      for (const entry of observer.takeRecords()) {
        longTasks++;
        longTaskMs += entry.duration;
      }

      observer.disconnect();
      return { longTasks, longTaskMs: round(longTaskMs), maxFrameGapMs: round(maxGap) };
    },
  };
}

async function backendMemory<T>(run: () => Promise<T>) {
  const start = await lab.memoryStart();
  const result = await run();
  const end = await lab.memoryStop();
  return {
    result,
    backend: {
      peakWorkingSetDeltaMB: mb(end.peakWorkingSetBytes - start.workingSetBytes),
      peakGcHeapDeltaMB: mb(end.peakGcHeapBytes - start.gcHeapBytes),
      cpuMs: round(end.cpuMs),
    },
  };
}

/** Consumes a row stream: time to first row, total, rows/s, page heap, main-thread blocking, backend memory and CPU. */
async function stream(options: { rows: number; batch: number; transport: StreamTransport; window?: number; keep?: boolean; delayEveryRows?: number; delayMs?: number }) {
  configure({ streams: options.transport });
  const { result, backend } = await backendMemory(async () => {
    const heapBefore = heap();
    let heapPeak = heapBefore;
    const kept: unknown[] = [];
    const probe = startProbe();
    const started = performance.now();
    let first = 0;
    let count = 0;
    let checksum = 0;
    const query = { count: options.rows, delayEveryRows: options.delayEveryRows ?? null, delayMs: options.delayMs ?? null };
    for await (const row of lab.rows(query, { batch: options.batch, window: options.window })) {
      if (count === 0) first = performance.now() - started;
      count++;
      checksum += row.id;
      if (options.keep) kept.push(row);
      if ((count & 1023) === 0) heapPeak = Math.max(heapPeak, heap());
    }

    const total = performance.now() - started;
    const main = probe.stop();
    heapPeak = Math.max(heapPeak, heap());
    return {
      rows: count,
      correct: checksum === options.rows * (options.rows - 1) / 2,
      firstRowMs: round(first),
      totalMs: round(total),
      rowsPerSecond: Math.round(count / total * 1000),
      heapBeforeMB: mb(heapBefore),
      heapPeakDeltaMB: mb(heapPeak - heapBefore),
      kept: kept.length,
      ...main,
    };
  });
  return { ...options, ...result, backend };
}

/** A consumer that stops reading: how far does the producer get ahead of it? */
async function backpressure(options: { batch: number; transport: StreamTransport; window?: number }) {
  configure({ streams: options.transport });
  const iterator = lab.rows({ count: 1_000_000, delayEveryRows: null, delayMs: null }, { batch: options.batch, window: options.window })[Symbol.asyncIterator]();
  let consumed = 0;
  while (consumed < 5000) {
    await iterator.next();
    consumed++;
  }

  await sleep(1000);
  const first = await lab.stats();
  await sleep(1000);
  const second = await lab.stats();
  const started = performance.now();
  let closeOutcome = "stopped";
  try {
    await iterator.return?.();
  } catch (error) {
    closeOutcome = outcome(error);
  }


  return {
    ...options,
    consumed,
    producedAfter1s: first.rowsProduced,
    producedAfter2s: second.rowsProduced,
    aheadOfConsumer: second.rowsProduced - consumed,
    producerStalled: first.rowsProduced === second.rowsProduced,
    closeMs: round(performance.now() - started),
    closeOutcome,
  };
}

/** Unary cancellation: does the page learn the truth, how fast, and did the backend's work really stop? */
async function cancelUnary(options: { transport: UnaryTransport; honor: boolean; confirmMs?: number; tickMs?: number }) {
  configure({ unary: options.transport, cancelConfirmMs: options.confirmMs ?? 5000 });
  const controller = new AbortController();
  const before = await lab.stats();
  const work = lab.work({ durationMs: options.honor ? 60_000 : 2500, tickMs: options.tickMs ?? 1, honorCancellation: options.honor }, { signal: controller.signal });
  await sleep(200);
  const atAbort = await lab.stats();
  const aborted = performance.now();
  controller.abort();
  let result: string;
  try {
    await work;
    result = "completed";
  } catch (error) {
    result = outcome(error);
  }

  const settledMs = performance.now() - aborted;
  const afterOutcome = await lab.stats();
  await sleep(300);
  const later = await lab.stats();
  await sleep(options.honor ? 0 : 3000);
  const final = await lab.stats();
  return {
    ...options,
    outcome: result,
    settledMs: round(settledMs),
    ticksBeforeAbort: atAbort.counter - before.counter,
    counterMovedWithin300msAfterOutcome: later.counter - afterOutcome.counter,
    workRunningAtOutcome: afterOutcome.workStarted - afterOutcome.workStopped,
    workRunningAtEnd: final.workStarted - final.workStopped,
  };
}

/** Stream cancellation, by abort or by leaving the loop. */
async function cancelStream(options: { transport: StreamTransport; how: "abort" | "break"; honor: boolean; tickMs?: number; confirmMs?: number }) {
  configure({ streams: options.transport, cancelConfirmMs: options.confirmMs ?? 5000 });
  const controller = new AbortController();
  let received = 0;
  let result = "completed";
  let aborted = 0;
  try {
    for await (const tick of lab.ticks({ durationMs: 60_000, tickMs: options.tickMs ?? 1, honorCancellation: options.honor }, { batch: 1, signal: controller.signal })) {
      received++;
      void tick;
      if (received === 20) {
        aborted = performance.now();
        if (options.how === "break") break;
        controller.abort();
      }
    }

    if (options.how === "break") result = "stopped-by-break";
  } catch (error) {
    result = outcome(error);
  }

  const settledMs = performance.now() - aborted;
  const afterOutcome = await lab.stats();
  await sleep(300);
  const later = await lab.stats();
  return {
    ...options,
    outcome: result,
    settledMs: round(settledMs),
    received,
    counterMovedWithin300msAfterOutcome: later.counter - afterOutcome.counter,
    workRunningAtOutcome: afterOutcome.workStarted - afterOutcome.workStopped,
  };
}

/** Live events at a fixed rate: latency from backend production to page delivery, gaps, main thread. */
async function push(options: { transport: StreamTransport; rate: number; seconds: number; batch?: number; window?: number; slowConsumerMs?: number }) {
  configure({ streams: options.transport });
  const { result, backend } = await backendMemory(async () => {
    const latencies: number[] = [];
    let events = 0;
    let gaps = 0;
    let dropped = 0;
    let outOfOrder = 0;
    let last = -1;
    const heapBefore = heap();
    const probe = startProbe();
    const started = performance.now();
    for await (const event of lab.watch({ ratePerSecond: options.rate, seconds: options.seconds }, { batch: options.batch ?? 100, window: options.window ?? 8 })) {
      if (event.type === "gap") {
        gaps++;
        dropped += event.dropped;
        continue;
      }

      if (event.type !== "event") continue;
      latencies.push(performance.timeOrigin + performance.now() - event.value.sentAtMs);
      if (event.value.sequence <= last) outOfOrder++;
      last = event.value.sequence;
      events++;
      if (options.slowConsumerMs && events % 100 === 0) await sleep(options.slowConsumerMs);
    }

    const elapsed = performance.now() - started;
    const main = probe.stop();
    latencies.sort((a, b) => a - b);
    return {
      events,
      produced: last + 1,
      gaps,
      dropped,
      accounted: events + dropped === last + 1,
      outOfOrder,
      elapsedMs: Math.round(elapsed),
      eventsPerSecond: Math.round(events / elapsed * 1000),
      latencyMs: {
        p50: round(percentile(latencies, 0.5)),
        p95: round(percentile(latencies, 0.95)),
        p99: round(percentile(latencies, 0.99)),
        max: round(latencies.at(-1) ?? 0),
      },
      heapDeltaMB: mb(heap() - heapBefore),
      ...main,
    };
  });
  return { ...options, ...result, backend };
}

/** Small unary calls through the SDK (both transports), for the transport comparison with the full protocol. */
async function unary(options: { transport: UnaryTransport; n: number; size: number }) {
  configure({ unary: options.transport });
  const data = "x".repeat(options.size);
  const times: number[] = [];
  for (let i = 0; i < options.n + 10; i++) {
    const started = performance.now();
    if (options.size === 0) await notes.count();
    else await notes.echo({ data });
    if (i >= 10) times.push(performance.now() - started);
  }

  times.sort((a, b) => a - b);
  return { ...options, medianMs: round(percentile(times, 0.5)), p95Ms: round(percentile(times, 0.95)) };
}

/** Holds N live subscriptions open, then times a unary call: shows per-connection limits of streamed responses. */
async function connectionLimit(options: { transport: StreamTransport; subscriptions: number; confirmMs?: number }) {
  configure({ streams: options.transport, unary: "fetch", cancelConfirmMs: options.confirmMs ?? 2000 });
  const controller = new AbortController();
  const outcomes: Promise<string>[] = [];
  for (let i = 0; i < options.subscriptions; i++) {
    outcomes.push((async () => {
      try {
        for await (const event of lab.watch({ ratePerSecond: 10, seconds: null }, { signal: controller.signal })) void event;
        return "completed";
      } catch (error) {
        return outcome(error);
      }
    })());
  }

  await sleep(500);
  const started = performance.now();
  const count = await Promise.race([notes.count().then(() => "ok"), sleep(3000).then(() => "blocked")]);
  const unaryMs = performance.now() - started;
  controller.abort();
  const results = await Promise.all(outcomes);
  return {
    ...options,
    unary: count,
    unaryMs: round(unaryMs),
    subscriptionOutcomes: results.reduce<Record<string, number>>((all, r) => ({ ...all, [r]: (all[r] ?? 0) + 1 }), {}),
  };
}

// A long-lived subscription that survives as far as the app allows: used for reload and backend-restart checks.
const watchState = { events: 0, resets: 0, gaps: 0, lastSequence: -1, firstEventAt: 0, lastResetAt: 0, error: "" };
let watchController: AbortController | undefined;

function watchForever(options: { transport: StreamTransport; rate: number; resubscribe: boolean }) {
  configure({ streams: options.transport });
  watchController?.abort();
  const controller = watchController = new AbortController();
  const open = () => lab.watch({ ratePerSecond: options.rate, seconds: null }, { signal: controller.signal });
  const events: AsyncIterable<SubscriptionEvent<MonitorEvent>> = options.resubscribe ? keepSubscribed(open, { signal: controller.signal, retryMs: 100 }) : open();
  void (async () => {
    try {
      for await (const event of events) {
        if (event.type === "reset") {
          watchState.resets++;
          watchState.lastResetAt = Date.now();
        } else if (event.type === "gap") {
          watchState.gaps++;
        } else {
          watchState.events++;
          watchState.lastSequence = event.value.sequence;
          if (watchState.firstEventAt === 0) watchState.firstEventAt = Date.now();
        }
      }
    } catch (error) {
      watchState.error = outcome(error);
    }
  })();
  return { started: true };
}

declare global {
  interface Window {
    sparBench: Record<string, (options: never) => unknown>;
    sparBenchReady: boolean;
  }
}

window.sparBench = {
  stream,
  backpressure,
  cancelUnary,
  cancelStream,
  push,
  unary,
  connectionLimit,
  watchForever,
  watchStatus: () => ({ ...watchState }),
  stats: () => lab.stats(),
};
window.sparBenchReady = true;
document.getElementById("status")!.textContent = "Ready";
