// The Windows host's injected script (src/Spar.Windows/PageScript.js), run here against a fake WebView port and window,
// with the settings the host writes in (PageScript.cs). It is plain JavaScript in the host's folder, embedded there.
import { readFileSync } from "node:fs";
import { afterEach, beforeEach, describe, expect, test, vi } from "vitest";

const source = readFileSync(new URL("../../Spar.Windows/PageScript.js", import.meta.url), "utf8");

interface Fake {
  readonly posted: string[];
  deliver(data: unknown): void;
  violate(fields: Partial<Record<string, unknown>>): void;
  readonly windowListeners: number;
}

function run(options: { reports?: boolean; origin?: string; top?: boolean; maxGroups?: number; maxViolations?: number } = {}): Fake {
  const posted: string[] = [];
  const messageListeners: Array<(event: { data: unknown }) => void> = [];
  let violationListeners: Array<(event: Record<string, unknown>) => void> = [];
  const webview = {
    postMessage: (message: string) => posted.push(message),
    addEventListener: (_type: string, listener: (event: { data: unknown }) => void) => messageListeners.push(listener),
  };
  const window: Record<string, unknown> = {
    addEventListener: (type: string, listener: (event: Record<string, unknown>) => void, capture: boolean) => {
      expect([type, capture]).toEqual(["securitypolicyviolation", true]);
      violationListeners.push(listener);
    },
    removeEventListener: (type: string, listener: (event: Record<string, unknown>) => void, capture: boolean) => {
      expect([type, capture]).toEqual(["securitypolicyviolation", true]);
      violationListeners = violationListeners.filter(l => l !== listener);
    },
  };
  window["top"] = options.top === false ? {} : window;

  const text = source
    .replace("__SPAR_REPORT_VIOLATIONS__", String(options.reports ?? true))
    .replace("__SPAR_REPORT_INTERVAL_MS__", "1000")
    .replace("__SPAR_REPORT_FIRST_DELAY_MS__", "100")
    .replace("__SPAR_REPORT_MAX_GROUPS__", String(options.maxGroups ?? 100))
    .replace("__SPAR_REPORT_MAX_VIOLATIONS__", String(options.maxViolations ?? 1_000_000));
  const globals = { chrome: { webview } };
  const location = { origin: options.origin ?? "https://app.localhost" };
  new Function("globalThis", "location", "window", "performance", "setTimeout", text)(
    globals, location, window, { now: () => Date.now() }, setTimeout);

  return {
    posted,
    deliver: data => messageListeners.forEach(listener => listener({ data })),
    violate: fields => violationListeners.forEach(listener => listener({
      effectiveDirective: "img-src",
      blockedURI: "https://example.com/a.png",
      sourceFile: "https://app.localhost/assets/index.js",
      lineNumber: 1,
      columnNumber: 20,
      sample: "",
      ...fields,
    })),
    get windowListeners() {
      return violationListeners.length;
    },
  };
}

function batches(posted: readonly string[]): Array<{ groups: Array<Record<string, unknown>>; overflow: number }> {
  return posted.filter(m => m.startsWith("{\"type\":\"csp\"}\t")).map(m => JSON.parse(m.slice(m.indexOf("\t") + 1)));
}

beforeEach(() => {
  vi.useFakeTimers();
});

afterEach(() => {
  vi.useRealTimers();
});

describe("the heartbeat", () => {
  test("each ping is answered at once, from the handler, with a pong of the same seq", () => {
    const page = run();

    page.deliver("{\"type\":\"ping\",\"seq\":1}\t");
    page.deliver("{\"type\":\"ping\",\"seq\":9007199254740991}\t");

    expect(page.posted).toEqual(["{\"type\":\"pong\",\"seq\":1}\t", "{\"type\":\"pong\",\"seq\":9007199254740991}\t"]);
  });

  test("anything else is left to the SDK: other messages, malformed pings, objects", () => {
    const page = run();

    page.deliver("{\"type\":\"result\",\"id\":1,\"ok\":true}\t{}");
    page.deliver("{\"type\":\"ping\",\"seq\":0}\t");
    page.deliver("{\"type\":\"ping\",\"seq\":\"1\"}\t");
    page.deliver("{\"type\":\"ping\",\"seq\":1}\tx");
    page.deliver({ type: "ping", seq: 1 });

    expect(page.posted).toEqual([]);
  });

  test("a frame or a page of another origin gets nothing of it", () => {
    const frame = run({ top: false });
    const foreign = run({ origin: "https://example.com" });

    frame.deliver("{\"type\":\"ping\",\"seq\":1}\t");
    foreign.deliver("{\"type\":\"ping\",\"seq\":1}\t");

    expect([frame.posted, foreign.posted, frame.windowListeners, foreign.windowListeners]).toEqual([[], [], 0, 0]);
  });
});

describe("the violation listener", () => {
  test("100,000 violations of one group are one message, with their count, after the first delay", () => {
    const page = run();

    for (let i = 0; i < 100_000; i++) {
      page.violate({});
    }

    expect(page.posted).toEqual([]);
    vi.advanceTimersByTime(100);

    const sent = batches(page.posted);
    expect(sent).toHaveLength(1);
    expect(sent[0]!.groups).toEqual([{
      directive: "img-src",
      blocked: "https://example.com/a.png",
      source: "https://app.localhost/assets/index.js",
      line: 1,
      column: 20,
      sample: "",
      count: 100_000,
    }]);
  });

  test("at most one message per interval, however the violations come", () => {
    const page = run();
    const times: number[] = [];

    // A violation every 100 ms for 5 s, then quiet.
    for (let tick = 0; tick < 50; tick++) {
      page.violate({ lineNumber: (tick % 10) + 1 });
      vi.advanceTimersByTime(100);
      while (times.length < batches(page.posted).length) {
        times.push(Date.now());
      }
    }

    vi.advanceTimersByTime(1_000);
    const gaps = times.slice(1).map((time, index) => time - times[index]!);
    expect(batches(page.posted).map(batch => batch.groups.reduce((sum, g) => sum + (g["count"] as number), 0)))
      .toEqual([1, 10, 10, 10, 10, 9]);
    expect(gaps.every(gap => gap >= 1_000)).toBe(true);
  });

  test("past the bound of distinct groups per document, violations are only counted", () => {
    const page = run({ maxGroups: 3 });

    for (let i = 0; i < 10; i++) {
      page.violate({ blockedURI: `https://example.com/${i}.png` });
    }

    page.violate({ blockedURI: "https://example.com/0.png" });
    vi.advanceTimersByTime(100);

    const [sent] = batches(page.posted);
    expect(sent!.groups.map(g => [g["blocked"], g["count"]])).toEqual([
      ["https://example.com/0.png", 2],
      ["https://example.com/1.png", 1],
      ["https://example.com/2.png", 1],
    ]);
    expect(sent!.overflow).toBe(7);
  });

  test("a group seen before is counted again in a later message, and texts are cut short", () => {
    const page = run();
    const long = "x".repeat(1_000);

    page.violate({ sample: long });
    vi.advanceTimersByTime(100);
    page.violate({ sample: long });
    vi.advanceTimersByTime(1_000);

    const sent = batches(page.posted);
    expect(sent.map(batch => batch.groups[0]!["count"])).toEqual([1, 1]);
    expect((sent[0]!.groups[0]!["sample"] as string).length).toBe(40);
  });

  test("past the bound of violations per document it stops listening, and says so", () => {
    const page = run({ maxViolations: 5 });

    for (let i = 0; i < 8; i++) {
      page.violate({ blockedURI: `https://example.com/${i}.png` });
    }

    vi.advanceTimersByTime(100);

    const [sent] = batches(page.posted);
    expect(sent!.groups).toHaveLength(5);
    expect(sent).toMatchObject({ overflow: 0, stopped: true });
    expect(page.windowListeners).toBe(0);
  });

  test("with the listener off, only the heartbeat is there", () => {
    const page = run({ reports: false });

    page.deliver("{\"type\":\"ping\",\"seq\":3}\t");

    expect(page.windowListeners).toBe(0);
    expect(page.posted).toEqual(["{\"type\":\"pong\",\"seq\":3}\t"]);
  });
});
