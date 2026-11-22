// The SDK's `setColorScheme` (plan section 3d): one of the host's own commands, `spar.setColorScheme`, with the
// scheme's name as its request, no value, and the declared error `notSaved`; a scheme that is not one of the three
// never leaves the page; and `fakeSpar()` answers it for page tests, success unless a test scripts otherwise.
import { afterEach, describe, expect, test, vi } from "vitest";
import { connectSpar, setColorScheme, type CallError } from "../src/sdk.js";
import { createFakeSpar } from "../src/testing.js";
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

describe("setColorScheme", () => {
  test("calls the host's own command with the scheme's name", async () => {
    const host = connected();

    const call = setColorScheme("dark");
    await vi.waitFor(() => expect(host.last("call")).toBeDefined());
    const sent = host.last("call")!;
    host.deliver({ type: "result", id: sent.id!, ok: true, payload: "" });

    expect([sent.contract, sent.command, sent.payload]).toEqual(["spar", "setColorScheme", '"dark"']);
    expect(await call.unwrap()).toBeUndefined();
  });

  test("notSaved is a declared error without a payload", async () => {
    const host = connected();

    const call = setColorScheme("system");
    await vi.waitFor(() => expect(host.last("call")).toBeDefined());
    host.deliver({ type: "result", id: host.last("call")!.id!, ok: false, payload: '{"code":"notSaved"}' });

    expect(await call.match({ success: () => "saved", errors: { notSaved: () => "this run only" } })).toBe("this run only");
  });

  test("a scheme that is not one of the three never leaves the page", async () => {
    const host = connected();
    vi.spyOn(console, "error").mockImplementation(() => undefined);

    const error = await setColorScheme("blue" as never).match({
      success: () => undefined,
      errors: { notSaved: () => undefined },
      failure: (failure: CallError) => failure,
    });

    expect(error).toMatchObject({ kind: "rejected", code: "invalid-request", execution: "notStarted", contract: "spar" });
    expect(host.last("call")).toBeUndefined();
  });
});

describe("fakeSpar and setColorScheme", () => {
  test("succeeds by default, and records the schemes the page chose", async () => {
    const spar = createFakeSpar(new Map(), "0123456789abcdef");

    await setColorScheme("dark").unwrap();
    await setColorScheme("system").unwrap();

    expect(spar.calls(setColorScheme)).toEqual(["dark", "system"]);
  });

  test("can be scripted like any command", async () => {
    const spar = createFakeSpar(new Map(), "0123456789abcdef");
    spar.on(setColorScheme).fail("notSaved");

    const outcome = await setColorScheme("light").match({ success: () => "saved", errors: { notSaved: () => "not saved" } });

    expect(outcome).toBe("not saved");
  });
});
