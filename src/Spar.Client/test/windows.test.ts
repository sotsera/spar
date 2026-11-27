// The SDK's `windows` calls (decision "M3d's decide-first questions", items 3 and 4), the host's own commands:
// `spar.openWindow`, with `{ kind, params }` as its request, no value, and the declared error `notOpened`;
// `spar.showModal`, the same request, the modal's value, `dismissed` or `notOpened`; `spar.closeWindow`, `{ value }`;
// and `spar.windowParams`, the opener's params or null. Requests JSON cannot carry never leave the page, and
// `fakeSpar()` answers each for page tests until a test scripts otherwise.
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

describe("windows.showModal and closeWith", () => {
  test("sends the kind and params to spar.showModal and succeeds with the value the modal's page gave", async () => {
    const host = connected();

    const call = windows.showModal<{ readonly changed: boolean }>("settings", { tab: "connections" });
    await vi.waitFor(() => expect(host.last("call")).toBeDefined());
    const sent = host.last("call")!;
    host.deliver({ type: "result", id: sent.id!, ok: true, payload: '{"changed":true}' });

    expect([sent.contract, sent.command, JSON.parse(sent.payload)]).toEqual([
      "spar",
      "showModal",
      { kind: "settings", params: { tab: "connections" } },
    ]);
    expect((await call.unwrap()).changed).toBe(true);
  });

  test("a modal closed any other way is dismissed, and one never opened is notOpened with the reason", async () => {
    const host = connected();

    const first = windows.showModal("settings");
    await vi.waitFor(() => expect(host.last("call")).toBeDefined());
    const sent = host.last("call")!;
    host.deliver({ type: "result", id: sent.id!, ok: false, payload: '{"code":"dismissed"}' });
    const dismissed = await first.match({ success: () => "value", errors: { dismissed: () => "dismissed", notOpened: e => e.reason } });

    const second = windows.showModal("settings");
    await vi.waitFor(() => expect(host.last("call")!.id).not.toBe(sent.id));
    host.deliver({ type: "result", id: host.last("call")!.id!, ok: false, payload: '{"code":"notOpened","reason":"The app is closing."}' });
    const notOpened = await second.match({ success: () => "value", errors: { dismissed: () => "dismissed", notOpened: e => e.reason } });

    expect([dismissed, notOpened]).toEqual(["dismissed", "The app is closing."]);
  });

  test("closeWith sends its value, null for none, and a value JSON cannot carry never leaves the page", async () => {
    const host = connected();
    const onFailure = vi.fn();
    vi.spyOn(console, "error").mockImplementation(() => undefined);
    const { configureSpar } = await import("../src/sdk.js");
    configureSpar({ onFailure });

    windows.closeWith({ changed: true });
    await vi.waitFor(() => expect(host.last("call")).toBeDefined());
    const first = host.last("call")!;
    host.deliver({ type: "result", id: first.id!, ok: true, payload: "" });
    windows.closeWith();
    await vi.waitFor(() => expect(host.last("call")!.id).not.toBe(first.id));
    const second = host.last("call")!;
    windows.closeWith(() => 1);
    await vi.waitFor(() => expect(onFailure).toHaveBeenCalled());

    expect([first.contract, first.command, JSON.parse(first.payload)]).toEqual(["spar", "closeWindow", { value: { changed: true } }]);
    expect(JSON.parse(second.payload)).toEqual({ value: null });
    expect(onFailure.mock.calls[0]![0]).toMatchObject({ kind: "rejected", code: "invalid-request", command: "closeWindow" });
    expect(host.last("call")!.id).toBe(second.id);
  });

  test("fakeSpar dismisses a modal until scripted and lists what the page gave closeWith", async () => {
    const spar = createFakeSpar(new Map(), "0123456789abcdef");

    const before = await windows.showModal("settings").match({ success: () => "value", errors: { dismissed: () => "dismissed", notOpened: () => "" } });
    spar.on(windows.showModal).succeed({ changed: true });
    const after = await windows.showModal<{ readonly changed: boolean }>("settings", { tab: "a" }).unwrap();
    windows.closeWith({ saved: 1 });
    windows.closeWith();
    await vi.waitFor(() => expect(spar.closedWith()).toHaveLength(2));

    expect(before).toBe("dismissed");
    expect(after.changed).toBe(true);
    expect(spar.calls(windows.showModal)).toEqual([{ kind: "settings", params: null }, { kind: "settings", params: { tab: "a" } }]);
    expect(spar.closedWith()).toEqual([{ saved: 1 }, null]);
  });
});

describe("this window's own operations", () => {
  test("each sends its action to spar.windowAction, and a size or title the host would refuse never leaves", async () => {
    const host = connected();
    vi.spyOn(console, "error").mockImplementation(() => undefined);
    const sent: unknown[] = [];
    const answer = async (call: Promise<unknown>): Promise<void> => {
      await vi.waitFor(() => expect(host.last("call")).toBeDefined());
      const last = host.last("call")!;
      sent.push([last.command, JSON.parse(last.payload)]);
      host.deliver({ type: "result", id: last.id!, ok: true, payload: "" });
      await call;
    };

    await answer(windows.minimize().unwrap());
    await answer(windows.setSize(800, 600).unwrap());
    await answer(windows.setTitle("Report — 2026").unwrap());
    const refused = await windows.setSize(0, 600).match({ success: () => undefined, failure: (error: CallError) => error });

    expect(sent).toEqual([
      ["windowAction", { action: "minimize" }],
      ["windowAction", { action: "setSize", width: 800, height: 600 }],
      ["windowAction", { action: "setTitle", title: "Report — 2026" }],
    ]);
    expect(refused).toMatchObject({ kind: "rejected", code: "invalid-request", execution: "notStarted" });
  });

  test("fakeSpar answers them and lists what the page asked, close among them", async () => {
    const spar = createFakeSpar(new Map(), "0123456789abcdef");

    await windows.maximize().unwrap();
    await windows.setSize(640, 480).unwrap();
    windows.close();
    await vi.waitFor(() => expect(spar.calls(windows.minimize)).toHaveLength(3));

    expect(spar.calls(windows.focus)).toEqual([
      { action: "maximize" },
      { action: "setSize", width: 640, height: 480 },
      { action: "close" },
    ]);
  });
});
