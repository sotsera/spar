// @vitest-environment happy-dom
// The SDK's side of the splash (plan section 3d): `connection.ready()` sends `ready` at once without a splash; with
// one, it removes the splash first, at once while its mark is not due, or after its minimum visible time once it is
// (counted from the first frame that rendered it, or from now when none has yet), takes `aria-busy` off the body, and
// only then tells the host. happy-dom runs no CSS animations, so each test
// gives the mark the entrance animation the plugin's style would; the real engines are checked by the window tests.
import { afterEach, beforeEach, describe, expect, test, vi } from "vitest";
import { connectSpar } from "../src/sdk.js";
import { finishSplash, notePaintedForTests, resetSplashForTests } from "../src/splash.js";
import { FakeHost } from "./fakeHost.js";

/** Writes a splash whose mark's entrance started at `startTime` with `delay`, and custom properties as the plugin does. */
function writeSplash(startTime: number | null, delay = 350, minimum = 120, fadeOut = 30): HTMLElement {
  document.body.setAttribute("aria-busy", "true");
  const splash = document.createElement("div");
  splash.id = "spar-splash";
  const mark = document.createElement("span");
  splash.append(mark);
  document.body.append(splash);

  const entrance = { animationName: "spar-splash-in", startTime, effect: { getTiming: () => ({ delay }) } };
  splash.getAnimations = () => [entrance as unknown as Animation];
  const properties: Record<string, string> = { "--spar-splash-minimum": String(minimum), "--spar-splash-fade-out": String(fadeOut) };
  vi.spyOn(window, "getComputedStyle").mockReturnValue({ getPropertyValue: (name: string) => properties[name] ?? "" } as CSSStyleDeclaration);
  return splash;
}

beforeEach(() => {
  resetSplashForTests();
  document.body.replaceChildren();
  document.body.removeAttribute("aria-busy");
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe("ready without a splash", () => {
  test("is sent at once, and only once per document", () => {
    const host = new FakeHost();
    const connection = connectSpar(host, "0123456789abcdef");
    host.open();

    connection.ready();
    connection.ready();

    expect(host.sent.filter(message => message.type === "ready")).toHaveLength(1);
  });
});

describe("ready with a splash", () => {
  test("a mark that has not appeared yet: the splash goes at once, then the host hears ready", async () => {
    const splash = writeSplash(performance.now(), 10_000);
    const host = new FakeHost();
    const connection = connectSpar(host, "0123456789abcdef");
    host.open();

    connection.ready();

    expect(splash.isConnected).toBe(false);
    expect(document.body.hasAttribute("aria-busy")).toBe(false);
    expect(host.last("ready")).toBeUndefined();
    await vi.waitFor(() => expect(host.last("ready")).toBeDefined());
  });

  test("an animation that has not started (a page that never rendered) counts as not appeared", async () => {
    writeSplash(null);

    const outcome = await finishSplash();

    expect(outcome).toEqual({ present: true, shown: false, keptMs: 0 });
  });

  test("a mark that appeared just now stays for its minimum visible time, then fades out", async () => {
    const splash = writeSplash(performance.now() - 350, 350, 120, 30);
    const removed = vi.fn();
    const outcome = finishSplash().then(result => {
      removed();
      return result;
    });

    await new Promise(resolve => setTimeout(resolve, 60));
    expect(splash.isConnected).toBe(true);
    expect(removed).not.toHaveBeenCalled();

    const result = await outcome;
    expect(result.present && result.shown).toBe(true);
    expect(result.keptMs).toBeGreaterThanOrEqual(110);
    expect(splash.isConnected).toBe(false);
    expect(document.body.hasAttribute("aria-busy")).toBe(false);
  });

  test("a mark a frame rendered long ago goes without waiting for its minimum", async () => {
    writeSplash(performance.now() - 2_000, 350, 120, 0);
    notePaintedForTests(performance.now() - 1_600);

    const result = await finishSplash();

    expect(result.shown).toBe(true);
    expect(result.keptMs).toBeLessThan(110);
  });

  test("a mark due long ago that no frame has rendered yet counts from now: a long task just ended", async () => {
    writeSplash(performance.now() - 2_000, 350, 120, 0);

    const result = await finishSplash();

    expect(result.shown).toBe(true);
    expect(result.keptMs).toBeGreaterThanOrEqual(110);
  });

  test("the host hears ready only once the splash is gone", async () => {
    const splash = writeSplash(performance.now() - 400, 350, 80, 0);
    const host = new FakeHost();
    const connection = connectSpar(host, "0123456789abcdef");
    host.open();

    connection.ready();
    await vi.waitFor(() => expect(host.last("ready")).toBeDefined(), { timeout: 2_000 });

    expect(splash.isConnected).toBe(false);
  });
});
