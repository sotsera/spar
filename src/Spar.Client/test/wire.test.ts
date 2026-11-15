// The field-by-field readers and writers that generated modules call.
import { describe, expect, test } from "vitest";
import { connectSpar, invoke } from "../src/sdk.js";
import * as wire from "../src/wire.js";
import { FakeHost } from "./fakeHost.js";

describe("readers", () => {
  test("each accepts its own JSON type and refuses the others", () => {
    expect(wire.readString("a")).toBe("a");
    expect(wire.readBoolean(false)).toBe(false);
    expect(wire.readInt32(-2147483648)).toBe(-2147483648);
    expect(wire.readDouble(0.5)).toBe(0.5);
    expect(() => wire.readString(1)).toThrow(TypeError);
    expect(() => wire.readBoolean("true")).toThrow(TypeError);
    expect(() => wire.readInt32(1.5)).toThrow(TypeError);
    expect(() => wire.readInt32(2147483648)).toThrow(TypeError);
    expect(() => wire.readObject([])).toThrow(TypeError);
    expect(() => wire.readObject(null)).toThrow(TypeError);
  });

  test("a long is a canonical decimal string, every digit kept", () => {
    expect(wire.readInt64("9007199254740993")).toBe(9007199254740993n);
    expect(wire.readInt64("-9223372036854775808")).toBe(-9223372036854775808n);
    for (const bad of [9007199254740993, "+1", "007", "-0", "1e3", "9223372036854775808", ""]) {
      expect(() => wire.readInt64(bad)).toThrow(TypeError);
    }
  });

  test("lists read every item, and null or missing is null", () => {
    expect(wire.readList(["a", "b"], wire.readString)).toEqual(["a", "b"]);
    expect(wire.readNullable(null, wire.readString)).toBeNull();
    expect(wire.readNullable(undefined, wire.readString)).toBeNull();
    expect(() => wire.readList({}, wire.readString)).toThrow(TypeError);
  });
});

describe("writers", () => {
  test("they write JSON text, longs as strings", () => {
    expect(wire.writeString("a\"b")).toBe("\"a\\\"b\"");
    expect(wire.writeInt64(-9223372036854775808n)).toBe("\"-9223372036854775808\"");
    expect(wire.writeList([1, 2], wire.writeInt32)).toBe("[1,2]");
    expect(wire.writeNullable(undefined, wire.writeString)).toBe("null");
    expect(wire.writeDouble(0.1)).toBe("0.1");
    expect(wire.writeBoolean(true)).toBe("true");
  });

  test("a lone surrogate becomes U+FFFD", () => {
    expect(JSON.parse(wire.writeString("a" + String.fromCharCode(0xd800)))).toBe("a" + String.fromCharCode(0xfffd));
  });

  test("values the backend would refuse are refused here", () => {
    expect(() => wire.writeDouble(Number.NaN)).toThrow(RangeError);
    expect(() => wire.writeDouble(Number.POSITIVE_INFINITY)).toThrow(RangeError);
    expect(() => wire.writeInt32(0.5)).toThrow(RangeError);
    expect(() => wire.writeInt64(2n ** 63n)).toThrow(RangeError);
  });
});

describe("invoke", () => {
  test("a request the encoder refuses never leaves and ends rejected / invalid-request", async () => {
    const host = new FakeHost();
    connectSpar(host, "0123456789abcdef");
    host.open();
    const decoder = { value: (json: string) => json, error: () => undefined };

    const refused = invoke("echo", "ratio", () => wire.writeDouble(Number.NaN), decoder);

    const outcome = await refused.match({ success: () => "sent", failure: e => `${e.kind} ${e.code} ${e.execution}` });
    expect(outcome).toBe("rejected invalid-request notStarted");
    expect(host.sent.filter(m => m.type === "call")).toHaveLength(0);
  });
});
