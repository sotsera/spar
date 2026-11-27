// The page's zoom (docs/app-model.md 6.8): `zoom.get()` is the factor the host last reported in its `zoom` event, 1 for
// a new document until then; `zoom.set` sends the host's own `spar.setZoom`; `zoom.onChange` handlers see each new
// factor. `fakeSpar()` answers `set` and reports the factor, and `spar.zoom(factor)` changes it as Ctrl+wheel would.
import { afterEach, describe, expect, test, vi } from "vitest";
import { connectSpar, type CallError } from "../src/sdk.js";
import { createFakeSpar } from "../src/testing.js";
import { zoom } from "../src/zoom.js";
import { FakeHost } from "./fakeHost.js";

afterEach(() => {
  vi.restoreAllMocks();
});

function connected(): FakeHost {
  const host = new FakeHost();
  connectSpar(host, "0123456789abcdef");
  host.open();
  return host;
}

describe("zoom", () => {
  test("a new document is at 1 until the host reports its factor, and each change reaches the handlers", async () => {
    const host = connected();
    const seen: number[] = [];
    const stop = zoom.onChange(factor => seen.push(factor));

    const before = zoom.get();
    host.deliver({ type: "event", name: "zoom", payload: '{"factor":1.25}' });
    await vi.waitFor(() => expect(zoom.get()).toBe(1.25));
    host.deliver({ type: "event", name: "zoom", payload: '{"factor":1.25}' });
    stop();
    host.deliver({ type: "event", name: "zoom", payload: '{"factor":2}' });
    await vi.waitFor(() => expect(zoom.get()).toBe(2));

    expect(before).toBe(1);
    expect(seen).toEqual([1.25]);
  });

  test("set sends the factor to spar.setZoom, and one that is not a positive number never leaves", async () => {
    const host = connected();
    vi.spyOn(console, "error").mockImplementation(() => undefined);

    const call = zoom.set(1.5);
    await vi.waitFor(() => expect(host.last("call")).toBeDefined());
    const sent = host.last("call")!;
    host.deliver({ type: "result", id: sent.id!, ok: true, payload: "" });
    await call.unwrap();
    const refused = await zoom.set(-1).match({ success: () => undefined, failure: (error: CallError) => error });

    expect([sent.contract, sent.command, JSON.parse(sent.payload)]).toEqual(["spar", "setZoom", { factor: 1.5 }]);
    expect(refused).toMatchObject({ kind: "rejected", code: "invalid-request", execution: "notStarted" });
  });

  test("fakeSpar reports what set asked, kept within the limits, and spar.zoom changes it", async () => {
    const spar = createFakeSpar(new Map(), "0123456789abcdef");
    const seen: number[] = [];
    zoom.onChange(factor => seen.push(factor));

    await zoom.set(9).unwrap();
    await vi.waitFor(() => expect(zoom.get()).toBe(5));
    spar.zoom(0.8);
    await vi.waitFor(() => expect(zoom.get()).toBe(0.8));

    expect(spar.calls(zoom.set)).toEqual([{ factor: 9 }]);
    expect(seen).toEqual([5, 0.8]);
  });
});
