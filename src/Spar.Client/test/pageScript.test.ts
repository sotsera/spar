// The Windows host's injected script (src/Spar.Windows/PageScript.js), run here against a fake WebView port, window and
// console, with the settings the host writes in (PageScript.cs). It is plain JavaScript in the host's folder, embedded
// there.
import { readFileSync } from "node:fs";
import { afterEach, beforeEach, describe, expect, test, vi } from "vitest";

const source = readFileSync(new URL("../../Spar.Windows/PageScript.js", import.meta.url), "utf8");

type Listener = (event: Record<string, unknown>) => void;

interface Fake {
  readonly posted: string[];
  /** What the page's original console.error received, in order. */
  readonly logged: unknown[][];
  readonly console: { error: (...args: unknown[]) => unknown };
  deliver(data: unknown): void;
  violate(fields: Partial<Record<string, unknown>>): void;
  /** Dispatches an event of `type` to the window's listeners; `target` defaults to the window. */
  dispatch(type: string, fields: Record<string, unknown>): void;
  listeners(type: string): number;
  readonly windowListeners: number;
}

/** A stand-in for the DOM's Event: the script reads `target` through the prototype's getter, taken before the page runs. */
class FakeEvent {
  get target(): unknown {
    return (this as unknown as { fakeTarget: unknown }).fakeTarget;
  }
}

/** A DOM with just what the dialog check reads: querySelectorAll on the document and checkVisibility on elements. */
function fakeDom(dialogs: readonly boolean[]) {
  class Element {
    constructor(readonly visible: boolean) {}

    checkVisibility(): boolean {
      return this.visible;
    }
  }

  class Document {
    selectors: string[] = [];

    querySelectorAll(selector: string): Element[] {
      this.selectors.push(selector);
      return dialogs.map(visible => new Element(visible));
    }
  }

  return { Document, Element, document: new Document() };
}

function run(options: {
  reports?: boolean;
  origin?: string;
  top?: boolean;
  maxGroups?: number;
  maxViolations?: number;
  maxErrorGroups?: number;
  maxErrors?: number;
  wrapConsole?: boolean;
  /** The modal dialogs the page shows: each found by the selector, visible or not; no DOM at all when absent. */
  dialogs?: readonly boolean[];
} = {}): Fake {
  const posted: string[] = [];
  const logged: unknown[][] = [];
  const messageListeners: Array<(event: { data: unknown }) => void> = [];
  const listeners = new Map<string, Listener[]>();
  const webview = {
    postMessage: (message: string) => posted.push(message),
    addEventListener: (_type: string, listener: (event: { data: unknown }) => void) => messageListeners.push(listener),
  };
  const window: Record<string, unknown> = {
    addEventListener: (type: string, listener: Listener, capture: boolean) => {
      expect(capture).toBe(true);
      listeners.set(type, [...(listeners.get(type) ?? []), listener]);
    },
    removeEventListener: (type: string, listener: Listener, capture: boolean) => {
      expect(capture).toBe(true);
      listeners.set(type, (listeners.get(type) ?? []).filter(l => l !== listener));
    },
  };
  window["top"] = options.top === false ? {} : window;
  const fakeConsole = {
    error(...args: unknown[]): unknown {
      logged.push(args);
      return "original";
    },
  };

  const text = source
    .replace("__SPAR_REPORT_VIOLATIONS__", String(options.reports ?? true))
    .replace("__SPAR_REPORT_INTERVAL_MS__", "1000")
    .replace("__SPAR_REPORT_FIRST_DELAY_MS__", "100")
    .replace("__SPAR_REPORT_MAX_GROUPS__", String(options.maxGroups ?? 100))
    .replace("__SPAR_REPORT_MAX_VIOLATIONS__", String(options.maxViolations ?? 1_000_000))
    .replace("__SPAR_ERROR_MAX_GROUPS__", String(options.maxErrorGroups ?? 100))
    .replace("__SPAR_ERROR_MAX_ERRORS__", String(options.maxErrors ?? 1_000_000))
    .replace("__SPAR_WRAP_CONSOLE__", String(options.wrapConsole ?? true));
  const globals = { chrome: { webview }, console: fakeConsole };
  const location = { origin: options.origin ?? "https://app.localhost" };
  const dom = options.dialogs === undefined ? undefined : fakeDom(options.dialogs);
  new Function("globalThis", "location", "window", "performance", "setTimeout", "Event", "Document", "Element", "document", text)(
    globals, location, window, { now: () => Date.now() }, setTimeout, FakeEvent, dom?.Document, dom?.Element, dom?.document);

  const dispatch = (type: string, fields: Record<string, unknown>) => {
    const event = Object.assign(new FakeEvent(), { fakeTarget: window, ...fields });
    (listeners.get(type) ?? []).forEach(listener => listener(event as unknown as Record<string, unknown>));
  };

  return {
    posted,
    logged,
    console: fakeConsole,
    deliver: data => messageListeners.forEach(listener => listener({ data })),
    violate: fields => dispatch("securitypolicyviolation", {
      effectiveDirective: "img-src",
      blockedURI: "https://example.com/a.png",
      sourceFile: "https://app.localhost/assets/index.js",
      lineNumber: 1,
      columnNumber: 20,
      sample: "",
      ...fields,
    }),
    dispatch,
    listeners: type => listeners.get(type)?.length ?? 0,
    get windowListeners() {
      return listeners.get("securitypolicyviolation")?.length ?? 0;
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

  test("a pong says when the page shows a visible modal dialog of its own", () => {
    const none = run({ dialogs: [] });
    const hidden = run({ dialogs: [false] });
    const shown = run({ dialogs: [false, true] });

    for (const page of [none, hidden, shown]) {
      page.deliver("{\"type\":\"ping\",\"seq\":2}	");
    }

    expect(none.posted).toEqual(["{\"type\":\"pong\",\"seq\":2}	"]);
    expect(hidden.posted).toEqual(["{\"type\":\"pong\",\"seq\":2}	"]);
    expect(shown.posted).toEqual(["{\"type\":\"pong\",\"seq\":2}	{\"dialog\":true}"]);
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

  test("with the listener off, no violation listener is added", () => {
    const page = run({ reports: false });

    page.deliver("{\"type\":\"ping\",\"seq\":3}\t");

    expect(page.windowListeners).toBe(0);
    expect(page.posted).toEqual(["{\"type\":\"pong\",\"seq\":3}\t"]);
  });
});

function errorBatches(posted: readonly string[]): Array<{ groups: Array<Record<string, unknown>>; overflow: number; stopped?: boolean }> {
  return posted.filter(m => m.startsWith("{\"type\":\"errors\"}\t")).map(m => JSON.parse(m.slice(m.indexOf("\t") + 1)));
}

describe("the page's errors", () => {
  test("console.error still reaches the page's console, with its arguments, this and result", () => {
    const page = run();
    const thrown = new Error("disk full");

    const result = page.console.error("Saving failed:", thrown, 42);

    expect(result).toBe("original");
    expect(page.logged).toEqual([["Saving failed:", thrown, 42]]);
    expect(page.console.error.name).toBe("error");
  });

  test("100,000 identical console errors are one group, with their count, the caller's place and the error's stack", () => {
    const page = run();
    const thrown = new Error("disk full");

    for (let i = 0; i < 100_000; i++) {
      page.console.error("Saving failed:", thrown);
    }

    vi.advanceTimersByTime(100);
    const sent = errorBatches(page.posted);
    expect(sent).toHaveLength(1);
    const [group] = sent[0]!.groups;
    expect(group).toMatchObject({ kind: "console", message: "Saving failed: Error: disk full", count: 100_000 });
    expect(group!["stack"]).toBe((thrown.stack ?? "").slice(0, 2048));
    // The caller's place is this test file: Node gives file:// addresses, which the script does not take as a page's.
    expect(group!["source"]).toBe("");
    // Each call reads the caller's stack: about 4 s here and 5.2 s on CI's Windows runner, past Vitest's default 5 s.
    // The test asserts the grouping, not a time.
  }, 30_000);

  test("an uncaught exception and an unhandled rejection are reported with their place and stack", () => {
    const page = run();
    const thrown = new TypeError("x is not a function");
    thrown.stack = "TypeError: x is not a function\n    at load (https://app.localhost/assets/index.js:3:14)";

    const source = "https://app.localhost/assets/index.js";
    page.dispatch("error", { message: "Uncaught TypeError: x is not a function", filename: source, lineno: 3, colno: 14, error: thrown });
    page.dispatch("unhandledrejection", { reason: thrown });
    page.dispatch("unhandledrejection", { reason: "plain text" });
    vi.advanceTimersByTime(100);

    const [sent] = errorBatches(page.posted);
    expect(sent!.groups).toEqual([
      { kind: "error", message: "Uncaught TypeError: x is not a function", source, line: 3, column: 14, stack: thrown.stack, count: 1 },
      { kind: "rejection", message: "TypeError: x is not a function", source, line: 3, column: 14, stack: thrown.stack, count: 1 },
      { kind: "rejection", message: "plain text", source: "", line: 0, column: 0, stack: "", count: 1 },
    ]);
  });

  test("a resource that failed to load is no exception", () => {
    const page = run();

    page.dispatch("error", { fakeTarget: { tagName: "IMG" }, message: "" });
    vi.advanceTimersByTime(1_000);

    expect(errorBatches(page.posted)).toEqual([]);
  });

  test("texts are cut short, and no page toString runs", () => {
    const page = run();
    let called = 0;
    const sneaky = { toString: () => { called++; return "x"; }, toJSON: () => { called++; return "x"; } };

    page.console.error("y".repeat(5_000), sneaky);
    vi.advanceTimersByTime(100);

    const [group] = errorBatches(page.posted)[0]!.groups;
    expect((group!["message"] as string).length).toBe(512);
    expect(called).toBe(0);
  });

  test("a value that throws when read, or a console.error from inside a read, reports nothing and breaks nothing", () => {
    const page = run();
    const hostile = { get message(): string { page.console.error("inside"); throw new Error("no"); } };

    expect(page.console.error(hostile)).toBe("original");
    vi.advanceTimersByTime(100);

    expect(page.logged).toEqual([["inside"], [hostile]]);
    expect(errorBatches(page.posted)).toEqual([]);
  });

  test("past the bound of distinct groups per document, errors are only counted", () => {
    const page = run({ maxErrorGroups: 2 });

    for (let i = 0; i < 5; i++) {
      page.console.error(`error ${i}`);
    }

    vi.advanceTimersByTime(100);
    const [sent] = errorBatches(page.posted);
    expect(sent!.groups.map(g => g["message"])).toEqual(["error 0", "error 1"]);
    expect(sent!.overflow).toBe(3);
  });

  test("past the bound of errors per document it stops, restores console.error and says so", () => {
    const page = run({ maxErrors: 3 });
    const wrapped = page.console.error;

    for (let i = 0; i < 5; i++) {
      page.console.error(`error ${i}`);
    }

    page.dispatch("error", { message: "late" });
    vi.advanceTimersByTime(100);

    const [sent] = errorBatches(page.posted);
    expect(sent!.groups).toHaveLength(3);
    expect(sent!.stopped).toBe(true);
    expect(page.console.error).not.toBe(wrapped);
    expect([page.listeners("error"), page.listeners("unhandledrejection")]).toEqual([0, 0]);
    expect(page.logged).toHaveLength(5);
  });

  test("a console.error the page wrapped after the script is left in place at the bound", () => {
    const page = run({ maxErrors: 1 });
    const ours = page.console.error;
    const theirs = (...args: unknown[]) => ours(...args);
    page.console.error = theirs;

    page.console.error("one");
    page.console.error("two");

    expect(page.console.error).toBe(theirs);
    expect(page.logged).toEqual([["one"], ["two"]]);
  });
});

describe("the place of a console error", () => {
  test("without an error among the arguments, the stack is the caller's, without the wrapper's frames", () => {
    vi.useFakeTimers();
    const page = run();

    page.console.error("just text");
    vi.advanceTimersByTime(100);

    const [group] = errorBatches(page.posted)[0]!.groups;
    const stack = group!["stack"] as string;
    expect(stack.startsWith("    at ")).toBe(true);
    expect(stack).not.toContain("onConsole");
    expect(stack).toContain("pageScript.test.ts");
  });
});

describe("a Development build", () => {
  test("leaves console.error unwrapped, since the host reads it from the DevTools Protocol, and still reports exceptions", () => {
    vi.useFakeTimers();
    const page = run({ wrapConsole: false });
    const original = page.console.error;

    page.console.error("not reported by the script");
    page.dispatch("error", { message: "Uncaught Error: boom", filename: "https://app.localhost/a.js", lineno: 1, colno: 2 });
    vi.advanceTimersByTime(100);

    expect(page.console.error).toBe(original);
    expect(errorBatches(page.posted)[0]!.groups.map(g => g["kind"])).toEqual(["error"]);
  });
});
