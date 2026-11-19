// The page's close guard (decision "Two-stage close"; docs/app-model.md 6.11): windows.onCloseRequested, run by the
// connection on `close-request` and answered with `close-answer`, and the page-test fake that drives it.
import { afterEach, beforeEach, describe, expect, test, vi } from "vitest";
import { Connection } from "../src/connection.js";
import type { BridgeMessage } from "../src/protocol.js";
import { createFakeSpar } from "../src/testing.js";
import { windows, type CloseRequest } from "../src/windows.js";
import { FakeHost } from "./fakeHost.js";

let host: FakeHost;
const removers: Array<() => void> = [];

beforeEach(() => {
  host = new FakeHost();
  new Connection(host, "0123456789abcdef");
  host.open();
});

afterEach(() => {
  removers.splice(0).forEach(remove => remove());
  vi.restoreAllMocks();
});

function guard(answer: Parameters<typeof windows.onCloseRequested>[0]): void {
  removers.push(windows.onCloseRequested(answer));
}

function request(seq: number, reason = "user", automated = false): void {
  host.deliver({ type: "close-request", seq, payload: JSON.stringify({ reason, automated }), });
}

async function answerOf(seq: number): Promise<unknown> {
  await vi.waitFor(() => expect(host.sent.some(m => m.type === "close-answer" && m.seq === seq)).toBe(true));
  const message = host.sent.find(m => m.type === "close-answer" && m.seq === seq) as BridgeMessage;
  return JSON.parse(message.payload);
}

describe("the page's close guards", () => {
  test("without a guard the page answers close at once", async () => {
    request(1);

    expect(await answerOf(1)).toEqual({ answer: "close" });
  });

  test("a guard sees the reason, the mode and a live signal", async () => {
    let seen: CloseRequest | undefined;
    guard(request => {
      seen = request;
      return "close";
    });

    request(4, "sessionEnd", true);

    expect(await answerOf(4)).toEqual({ answer: "close" });
    expect(seen?.reason).toBe("sessionEnd");
    expect(seen?.automated).toBe(true);
    expect(seen?.signal.aborted).toBe(false);
  });

  test("keep and ask are sent as they are; an ask carries its question", async () => {
    guard(() => "keep");
    request(1);
    expect(await answerOf(1)).toEqual({ answer: "keep" });

    removers.splice(0).forEach(remove => remove());
    guard(async () => ({ ask: "Close without saving?" }));
    request(2);
    expect(await answerOf(2)).toEqual({ answer: "ask", question: "Close without saving?" });
  });

  test("guards run in the order they were added and the first keep or ask ends the run", async () => {
    const ran: string[] = [];
    guard(() => {
      ran.push("first");
      return "close";
    });
    guard(() => {
      ran.push("second");
      return { ask: "Sure?" };
    });
    guard(() => {
      ran.push("third");
      return "close";
    });

    request(1);

    expect(await answerOf(1)).toEqual({ answer: "ask", question: "Sure?" });
    expect(ran).toEqual(["first", "second"]);
  });

  test("a removed guard is not asked", async () => {
    const remove = windows.onCloseRequested(() => "keep");
    remove();
    remove();

    request(1);

    expect(await answerOf(1)).toEqual({ answer: "close" });
  });

  test("a guard that throws or answers nonsense makes the answer failed, with a console error", async () => {
    const errors = vi.spyOn(console, "error").mockImplementation(() => undefined);
    guard(() => {
      throw new Error("broken");
    });
    request(1);
    expect(await answerOf(1)).toEqual({ answer: "failed" });

    removers.splice(0).forEach(remove => remove());
    guard(() => ({ ask: "  " }) as never);
    request(2);
    expect(await answerOf(2)).toEqual({ answer: "failed" });
    expect(errors).toHaveBeenCalledTimes(2);
  });

  test("close-ended aborts the guard's signal, and the page sends no answer", async () => {
    let signal: AbortSignal | undefined;
    let finish: (() => void) | undefined;
    guard(request => new Promise(resolve => {
      signal = request.signal;
      finish = () => resolve("keep");
    }));

    request(7);
    await vi.waitFor(() => expect(signal).toBeDefined());
    host.deliver({ type: "close-ended", seq: 7, payload: "" });
    finish!();
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(signal!.aborted).toBe(true);
    expect(host.sent.some(m => m.type === "close-answer")).toBe(false);
  });

  test("a payload that cannot be read is a user's close, not automated", async () => {
    let seen: CloseRequest | undefined;
    guard(request => {
      seen = request;
      return "close";
    });

    host.deliver({ type: "close-request", seq: 1, payload: "{\"reason\":\"reboot\",\"automated\":\"yes\"}" });

    expect(await answerOf(1)).toEqual({ answer: "close" });
    expect([seen?.reason, seen?.automated]).toEqual(["user", false]);
  });

  test("onCloseRequested refuses what is not a function", () => {
    expect(() => windows.onCloseRequested("close" as never)).toThrow(TypeError);
  });
});

describe("fakeSpar.requestClose", () => {
  test("drives the page's guards the way the host does", async () => {
    const spar = createFakeSpar(new Map(), "0123456789abcdef");
    removers.push(windows.onCloseRequested(request => (request.automated ? "close" : { ask: "Discard the draft?" })));

    expect(await spar.requestClose()).toEqual({ ask: "Discard the draft?" });
    expect(await spar.requestClose({ automated: true })).toBe("close");
  });

  test("an aborted request ends without the guards, whose signal is aborted", async () => {
    const spar = createFakeSpar(new Map(), "0123456789abcdef");
    let signal: AbortSignal | undefined;
    removers.push(windows.onCloseRequested(request => {
      signal = request.signal;
      return new Promise(() => undefined);
    }));
    const controller = new AbortController();

    const answer = spar.requestClose({ reason: "sessionEnd", signal: controller.signal });
    await vi.waitFor(() => expect(signal).toBeDefined());
    controller.abort(new Error("deadline"));

    await expect(answer).rejects.toThrow("deadline");
    await vi.waitFor(() => expect(signal!.aborted).toBe(true));
  });
});
