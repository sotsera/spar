// @vitest-environment happy-dom
// The Hot Reload signal's page side (plan section 4): `onBackendUpdate` and its handlers, the payload read as untrusted,
// the development overlay's toast (its text, dismissal, its limit, and that it uses no style attribute or <style>
// element), `configureSpar({ backendUpdateToast: false })`, the fake backend's `backendUpdate`, and the no-op of a
// production build. That a production bundle carries none of it is checked on the Notebook's bundle by Spar.BuildTests.
import { afterEach, beforeEach, describe, expect, test, vi } from "vitest";
import { readBackendUpdate } from "../src/backendUpdate.js";
import { backendUpdateText } from "../src/overlay.js";
import { configureSpar, connectSpar, onBackendUpdate, type BackendUpdate } from "../src/sdk.js";
import { createFakeSpar } from "../src/testing.js";
import { FakeHost } from "./fakeHost.js";

const payload = (revision: number, types: readonly string[] = ["Notebook.Pages.PagesService"]): string =>
  JSON.stringify({ revision, time: "2026-11-22T09:30:00.123+00:00", types });

function connected(): FakeHost {
  const host = new FakeHost();
  connectSpar(host, "0123456789abcdef");
  host.open();
  return host;
}

function send(host: FakeHost, text: string): void {
  host.deliver({ type: "event", name: "backend-updated", payload: text });
}

function toasts(): HTMLElement[] {
  const overlay = document.querySelector("[data-spar-dev-overlay]");
  return [...(overlay?.shadowRoot?.querySelectorAll<HTMLElement>(".toast") ?? [])];
}

const stops: Array<() => void> = [];

function listen(handler: (update: BackendUpdate) => void): void {
  stops.push(onBackendUpdate(handler));
}

beforeEach(() => {
  configureSpar({});
  document.body.replaceChildren();
});

afterEach(() => {
  for (const stop of stops.splice(0)) {
    stop();
  }

  vi.useRealTimers();
  vi.unstubAllEnvs();
  vi.restoreAllMocks();
});

describe("onBackendUpdate", () => {
  test("each handler gets the update, in the order added, until it stops listening", () => {
    const host = connected();
    const seen: string[] = [];
    const stopFirst = onBackendUpdate(update => seen.push(`first ${update.revision}`));
    listen(update => seen.push(`second ${update.revision} ${update.types.join()} ${update.time}`));

    send(host, payload(1));
    stopFirst();
    send(host, payload(2));

    expect(seen).toEqual([
      "first 1",
      "second 1 Notebook.Pages.PagesService 2026-11-22T09:30:00.123+00:00",
      "second 2 Notebook.Pages.PagesService 2026-11-22T09:30:00.123+00:00",
    ]);
  });

  test("a handler that throws does not stop the others", () => {
    const host = connected();
    const error = vi.spyOn(console, "error").mockImplementation(() => undefined);
    const seen: number[] = [];
    listen(() => {
      throw new Error("broken handler");
    });
    listen(update => seen.push(update.revision));

    send(host, payload(4));

    expect(seen).toEqual([4]);
    expect(error).toHaveBeenCalledWith("Spar: an onBackendUpdate handler threw", expect.any(Error));
  });

  test("an event that cannot be read reaches no handler and shows no toast", () => {
    const host = connected();
    const warn = vi.spyOn(console, "warn").mockImplementation(() => undefined);
    const seen: number[] = [];
    listen(update => seen.push(update.revision));

    send(host, "{not json");
    send(host, JSON.stringify({ revision: 1.5, time: "t", types: [] }));

    expect(seen).toEqual([]);
    expect(toasts()).toHaveLength(0);
    expect(warn).toHaveBeenCalledTimes(2);
  });

  test("the same function added twice runs twice, and each stop removes one", () => {
    const host = connected();
    let calls = 0;
    const handler = (): void => {
      calls++;
    };
    const stop = onBackendUpdate(handler);
    listen(handler);

    send(host, payload(1));
    stop();
    send(host, payload(2));

    expect(calls).toBe(3);
  });

  test("a handler that is not a function is refused at once", () => {
    expect(() => onBackendUpdate("refresh" as never)).toThrow(TypeError);
  });

  test("in a production build it does nothing and the connection does not listen", () => {
    vi.stubEnv("DEV", false);
    const host = connected();
    const seen: number[] = [];
    listen(update => seen.push(update.revision));

    send(host, payload(1));

    expect(seen).toEqual([]);
    expect(toasts()).toHaveLength(0);
  });
});

describe("the payload", () => {
  test("is read field by field: own properties of the right type only", () => {
    expect(readBackendUpdate(payload(3, ["A", "B+C"]))).toEqual({ revision: 3, time: "2026-11-22T09:30:00.123+00:00", types: ["A", "B+C"] });
    expect(readBackendUpdate("[]")).toBeUndefined();
    expect(readBackendUpdate("null")).toBeUndefined();
    expect(readBackendUpdate(JSON.stringify({ revision: 1, time: "t" }))).toBeUndefined();
    expect(readBackendUpdate(JSON.stringify({ revision: "1", time: "t", types: [] }))).toBeUndefined();
    expect(readBackendUpdate(JSON.stringify({ revision: 1, time: 5, types: [] }))).toBeUndefined();
    expect(readBackendUpdate(JSON.stringify({ revision: 1, time: "t", types: [1] }))).toBeUndefined();
    expect(readBackendUpdate('{"__proto__":{"revision":1},"time":"t","types":[]}')).toBeUndefined();
  });

  test("a parsed __proto__ key never becomes the update's prototype", () => {
    const update = readBackendUpdate('{"revision":1,"time":"t","types":[],"__proto__":{"polluted":true}}');
    expect(Object.getPrototypeOf(update)).toBe(Object.prototype);
  });
});

describe("the toast", () => {
  test("names the updated types by their short names and the revision", () => {
    expect(backendUpdateText({ revision: 3, time: "t", types: ["App.CopyJobs.CopyJobsService"] })).toBe(
      "Backend updated: CopyJobsService (revision 3)",
    );
    expect(backendUpdateText({ revision: 4, time: "t", types: ["A.B", "Outer+Inner"] })).toBe("Backend updated: B, Inner (revision 4)");
    expect(backendUpdateText({ revision: 5, time: "t", types: [] })).toBe("Backend updated (revision 5)");
  });

  test("shows one per update, as a status, after the handlers", () => {
    const host = connected();
    let toastsWhenHandled = -1;
    listen(() => {
      toastsWhenHandled = toasts().length;
    });

    send(host, payload(1));

    const [toast] = toasts();
    expect(toastsWhenHandled).toBe(0);
    expect(toast?.getAttribute("role")).toBe("status");
    expect(toast?.textContent).toContain("Backend updated: PagesService (revision 1)");
  });

  test("is dismissed by its button, or by itself after 8 seconds", () => {
    vi.useFakeTimers();
    const host = connected();
    send(host, payload(1));
    send(host, payload(2));

    toasts()[0]?.querySelector<HTMLButtonElement>("button[aria-label='Dismiss']")?.click();
    expect(toasts().map(t => t.textContent)).toEqual([expect.stringContaining("revision 2")]);

    vi.advanceTimersByTime(8_000);
    expect(toasts()).toHaveLength(0);
  });

  test("keeps at most three, the newest", () => {
    const host = connected();
    for (const revision of [1, 2, 3, 4]) {
      send(host, payload(revision));
    }

    expect(toasts().map(t => t.querySelector(".text")?.textContent)).toEqual([
      "Backend updated: PagesService (revision 2)",
      "Backend updated: PagesService (revision 3)",
      "Backend updated: PagesService (revision 4)",
    ]);
  });

  test("uses no style attribute, no <style> element and no HTML string: a stylesheet its shadow root adopts", () => {
    const host = connected();
    send(host, payload(1));

    const overlay = document.querySelector("[data-spar-dev-overlay]");
    const shadow = overlay?.shadowRoot;
    expect(shadow?.adoptedStyleSheets).toHaveLength(1);
    expect(document.querySelectorAll("style, [style]")).toHaveLength(0);
    expect(shadow?.querySelectorAll("style, [style]")).toHaveLength(0);
  });

  test("configureSpar({ backendUpdateToast: false }) turns it off; the handlers still run", () => {
    const host = connected();
    const seen: number[] = [];
    listen(update => seen.push(update.revision));
    configureSpar({ backendUpdateToast: false });

    send(host, payload(1));
    expect(seen).toEqual([1]);
    expect(toasts()).toHaveLength(0);

    // Each call sets every setting: one without the option brings the default back.
    configureSpar({});
    send(host, payload(2));
    expect(toasts()).toHaveLength(1);
  });
});

describe("the fake backend", () => {
  test("backendUpdate sends the event as a host does, counting revisions from 1", async () => {
    const spar = createFakeSpar(new Map(), "0123456789abcdef");
    const seen: BackendUpdate[] = [];
    listen(update => seen.push(update));

    spar.backendUpdate({ types: ["Notebook.Pages.PagesService"] });
    spar.backendUpdate();
    await vi.waitFor(() => expect(seen).toHaveLength(2));

    expect(seen.map(u => [u.revision, u.types])).toEqual([[1, ["Notebook.Pages.PagesService"]], [2, []]]);
    expect(toasts()).toHaveLength(2);
  });

  test("resets the toast setting, as it resets the failure hooks", () => {
    configureSpar({ backendUpdateToast: false });
    const spar = createFakeSpar(new Map(), "0123456789abcdef");

    spar.backendUpdate();

    return vi.waitFor(() => expect(toasts()).toHaveLength(1));
  });
});
