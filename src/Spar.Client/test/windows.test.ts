// The SDK's `windows.open` and `windows.params` (decision "M3d's decide-first questions", item 3): the host's own
// commands `spar.openWindow`, with `{ kind, params }` as its request, no value, and the declared error `notOpened`; and
// `spar.windowParams`, the opener's params or null. Params that are not an object never leave the page, and
// `fakeSpar()` answers both for page tests until a test scripts otherwise.
import { afterEach, describe, expect, test, vi } from "vitest";
import { connectSpar, type CallError } from "../src/sdk.js";
import { createFakeSpar } from "../src/testing.js";
import { windows } from "../src/windows.js";
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

describe("windows.open", () => {
  test("sends the kind and its params to the host's own command and succeeds once the window is shown", async () => {
    const host = connected();

    const call = windows.open("settings", { tab: "connections", n: 2 });
    await vi.waitFor(() => expect(host.last("call")).toBeDefined());
    const sent = host.last("call")!;
    host.deliver({ type: "result", id: sent.id!, ok: true, payload: "" });

    expect([sent.contract, sent.command, JSON.parse(sent.payload)]).toEqual([
      "spar",
      "openWindow",
      { kind: "settings", params: { tab: "connections", n: 2 } },
    ]);
    expect(await call.unwrap()).toBeUndefined();
  });

  test("without params it sends null, and notOpened carries the host's reason", async () => {
    const host = connected();

    const call = windows.open("report");
    await vi.waitFor(() => expect(host.last("call")).toBeDefined());
    const sent = host.last("call")!;
    host.deliver({ type: "result", id: sent.id!, ok: false, payload: '{"code":"notOpened","reason":"The app is closing."}' });

    expect(JSON.parse(sent.payload)).toEqual({ kind: "report", params: null });
    expect(await call.match({ success: () => "opened", errors: { notOpened: e => e.reason } })).toBe("The app is closing.");
  });

  test("params that are not an object never leave the page", async () => {
    const host = connected();
    vi.spyOn(console, "error").mockImplementation(() => undefined);

    const failure = await windows.open("settings", [1, 2] as never).match({
      success: () => undefined,
      errors: { notOpened: () => undefined },
      failure: (error: CallError) => error,
    });

    expect(failure).toMatchObject({ kind: "rejected", code: "invalid-request", execution: "notStarted", contract: "spar" });
    expect(host.last("call")).toBeUndefined();
  });
});

describe("windows.params", () => {
  test("reads the opener's params, and null for a window opened without any", async () => {
    const host = connected();

    const withParams = windows.params<{ readonly tab: string }>();
    await vi.waitFor(() => expect(host.last("call")).toBeDefined());
    const first = host.last("call")!;
    host.deliver({ type: "result", id: first.id!, ok: true, payload: '{"tab":"connections"}' });
    const tab = (await withParams.unwrap())?.tab;

    const without = windows.params();
    await vi.waitFor(() => expect(host.last("call")!.id).not.toBe(first.id));
    host.deliver({ type: "result", id: host.last("call")!.id!, ok: true, payload: "null" });

    expect([first.contract, first.command]).toEqual(["spar", "windowParams"]);
    expect(tab).toBe("connections");
    expect(await without.unwrap()).toBeNull();
  });
});

describe("fakeSpar and windows", () => {
  test("open succeeds by default and records what the page opened; params are null until scripted", async () => {
    const spar = createFakeSpar(new Map(), "0123456789abcdef");

    await windows.open("settings", { tab: "general" }).unwrap();
    const before = await windows.params().unwrap();
    spar.on(windows.params).succeed({ tab: "general" });
    const after = await windows.params<{ readonly tab: string }>().unwrap();
    spar.on(windows.open).fail("notOpened", { reason: "no windows here" });
    const refused = await windows.open("settings").match({ success: () => "", errors: { notOpened: e => e.reason } });

    expect(spar.calls(windows.open)).toEqual([{ kind: "settings", params: { tab: "general" } }, { kind: "settings", params: null }]);
    expect(before).toBeNull();
    expect(after?.tab).toBe("general");
    expect(refused).toBe("no windows here");
  });
});
