// Where each outcome goes: match handlers, the failure hooks, and unwrap.
import { afterEach, describe, expect, test } from "vitest";
import { callOf, configureSpar, Result, SparError, type CallError, type CallErrorKind, type Outcome } from "../src/results.js";

type PageErrors = { readonly notFound: { readonly id: string }; readonly locked: undefined };

const call = { contract: "pages", command: "get" } as const;
const fail = (kind: CallErrorKind): Outcome => ({ ok: false, error: { kind, code: kind, execution: "unknown", ...call } });
const result = (outcome: Outcome): Result<string, PageErrors> => new Result<string, PageErrors>(outcome);
const seen: string[] = [];

function watchHooks(): void {
  seen.length = 0;
  configureSpar({ onFailure: e => seen.push(`onFailure ${e.kind}`), observe: e => seen.push(`observe ${e.kind}`) });
}

afterEach(() => configureSpar({}));

describe("match", () => {
  const handlers = {
    success: (value: string) => `value ${value}`,
    errors: { notFound: (e: { readonly id: string }) => `missing ${e.id}`, locked: () => "locked" },
  };

  test("a value goes to success and never to the hooks", () => {
    watchHooks();

    expect(result({ ok: true, value: "a" }).match(handlers)).toBe("value a");
    expect(seen).toEqual([]);
  });

  test("a declared error goes to its handler, with or without a payload", () => {
    expect(result({ ok: false, code: "notFound", payload: { id: "x" }, call }).match(handlers)).toBe("missing x");
    expect(result({ ok: false, code: "locked", payload: undefined, call }).match(handlers)).toBe("locked");
  });

  test("a failure without a handler is observed, goes to onFailure, and gives undefined", () => {
    watchHooks();

    expect(result(fail("transport")).match(handlers)).toBeUndefined();
    expect(seen).toEqual(["observe transport", "onFailure transport"]);
  });

  test("a failure function handles every failure; observe still sees it", () => {
    watchHooks();

    expect(result(fail("failed")).match({ ...handlers, failure: (e: CallError) => `failed ${e.command}` })).toBe("failed get");
    expect(seen).toEqual(["observe failed"]);
  });

  test("a failure object handles the kinds it lists; the rest go to onFailure", () => {
    watchHooks();
    const byKind = { ...handlers, failure: { unavailable: () => "later" } };

    expect(result(fail("unavailable")).match(byKind)).toBe("later");
    expect(result(fail("failed")).match(byKind)).toBeUndefined();
    expect(seen).toEqual(["observe unavailable", "observe failed", "onFailure failed"]);
  });

  test("an error case the handlers do not know is protocol / unknown-error-case", () => {
    const outcome: Outcome = { ok: false, code: "archived", payload: {}, call };

    const handled = result(outcome).match({ ...handlers, failure: (e: CallError) => e });

    expect(handled).toEqual({ kind: "protocol", code: "unknown-error-case", execution: "unknown", contract: "pages", command: "get" });
  });
});

describe("unwrap", () => {
  test("a value is returned", () => {
    expect(result({ ok: true, value: "a" }).unwrap()).toBe("a");
  });

  test("a declared error throws SparError with the flattened error", () => {
    const thrown = (() => {
      try {
        result({ ok: false, code: "notFound", payload: { id: "x" }, call }).unwrap();
      } catch (error) {
        return error;
      }

      return undefined;
    })();

    expect(thrown).toBeInstanceOf(SparError);
    expect((thrown as SparError).error).toEqual({ code: "notFound", id: "x" });
  });

  test("a failure throws, is observed, and never reaches onFailure", () => {
    watchHooks();

    expect(() => result(fail("rejected")).unwrap()).toThrow(SparError);
    expect(seen).toEqual(["observe rejected"]);
  });
});

describe("Call", () => {
  test("it is a Promise of its Result whose then returns a plain promise", async () => {
    const pending = callOf<string, PageErrors>(Promise.resolve({ ok: true, value: "a" }));

    const chained = pending.then(r => r);

    expect(pending).toBeInstanceOf(Promise);
    expect(chained.constructor).toBe(Promise);
    expect(Object.prototype.toString.call(pending)).toBe("[object SparCall]");
    expect(await pending.match({ success: v => v.length, errors: { notFound: () => 0, locked: () => 0 } })).toBe(1);
    expect(await callOf<string, {}>(Promise.resolve({ ok: true, value: "b" })).unwrap()).toBe("b");
  });

  test("a Result is tagged so frameworks that proxy objects leave it raw", () => {
    expect(Object.prototype.toString.call(result({ ok: true, value: "a" }))).toBe("[object SparResult]");
  });
});
