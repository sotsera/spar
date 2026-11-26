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

  test("each smaller integer keeps to its own range", () => {
    const ranges: [(value: unknown) => number, number, number][] = [
      [wire.readByte, 0, 255],
      [wire.readSByte, -128, 127],
      [wire.readInt16, -32768, 32767],
      [wire.readUInt16, 0, 65535],
      [wire.readUInt32, 0, 4294967295],
    ];
    for (const [read, min, max] of ranges) {
      expect(read(min)).toBe(min);
      expect(read(max)).toBe(max);
      expect(() => read(min - 1)).toThrow(TypeError);
      expect(() => read(max + 1)).toThrow(TypeError);
      expect(() => read(0.5)).toThrow(TypeError);
      expect(() => read("1")).toThrow(TypeError);
    }
  });

  test("a ulong is a canonical decimal string up to 2^64 - 1", () => {
    expect(wire.readUInt64("18446744073709551615")).toBe(18446744073709551615n);
    expect(wire.readUInt64("0")).toBe(0n);
    for (const bad of ["-1", "18446744073709551616", "+1", "01", 1]) {
      expect(() => wire.readUInt64(bad)).toThrow(TypeError);
    }
  });

  test("a decimal stays a string with its scale, in its one spelling", () => {
    for (const good of ["0", "1.50", "-12.345", "0.001", "79228162514264337593543950335"]) {
      expect(wire.readDecimal(good)).toBe(good);
    }

    for (const bad of ["-0", "-0.00", "+1", "01.5", "1e3", ".5", "1.", " 1", 1.5]) {
      expect(() => wire.readDecimal(bad)).toThrow(TypeError);
    }
  });

  test("a char is one UTF-16 code unit", () => {
    expect(wire.readChar("é")).toBe("é");
    expect(() => wire.readChar("")).toThrow(TypeError);
    expect(() => wire.readChar("😀")).toThrow(TypeError);
  });

  test("bytes are padded base64 with no whitespace", () => {
    expect(wire.readBytes("AQID/w==")).toEqual(new Uint8Array([1, 2, 3, 255]));
    expect(wire.readBytes("")).toEqual(new Uint8Array([]));
    for (const bad of ["AQID/w", "AQ ID", "AQID\n", "AQ==AQ==", "A===", "AQI*", "AQ=D", 1]) {
      expect(() => wire.readBytes(bad)).toThrow(TypeError);
    }
  });

  test("a value of several megabytes reads whole", () => {
    // The binary-data spike's 4 MB result failed as malformed: one regular expression over the base64 gave up.
    const large = new Uint8Array(6 * 1024 * 1024).map((_, i) => (i * 31) % 256);
    const read = wire.readBytes(JSON.parse(wire.writeBytes(large)));

    // Compared in a loop: a deep equality over six million items is slower than the decoding it checks.
    expect(read.length).toBe(large.length);
    let differences = 0;
    for (let i = 0; i < large.length; i++) {
      differences += read[i] === large[i] ? 0 : 1;
    }

    expect(differences).toBe(0);
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
    expect(() => wire.writeByte(256)).toThrow(RangeError);
    expect(() => wire.writeSByte(-129)).toThrow(RangeError);
    expect(() => wire.writeInt16(32768)).toThrow(RangeError);
    expect(() => wire.writeUInt16(-1)).toThrow(RangeError);
    expect(() => wire.writeUInt32(2 ** 32)).toThrow(RangeError);
    expect(() => wire.writeUInt64(-1n)).toThrow(RangeError);
    expect(() => wire.writeUInt64(2n ** 64n)).toThrow(RangeError);
    expect(() => wire.writeSingle(1e39)).toThrow(RangeError);
    expect(() => wire.writeSingle(Number.NaN)).toThrow(RangeError);
    expect(() => wire.writeDecimal("1e3")).toThrow(RangeError);
    expect(() => wire.writeDecimal("-0")).toThrow(RangeError);
    expect(() => wire.writeChar("ab")).toThrow(RangeError);
  });

  test("an enum's refusals throw the reader's and the writer's errors", () => {
    expect(() => wire.unknownMember("Urgent", "Priority")).toThrow(new TypeError("Expected a member of Priority, got a string."));
    expect(() => wire.refusedMember(1, "Priority")).toThrow(RangeError);
  });

  test("the other kinds write their own JSON", () => {
    expect(wire.writeByte(255)).toBe("255");
    expect(wire.writeUInt32(4294967295)).toBe("4294967295");
    expect(wire.writeUInt64(18446744073709551615n)).toBe("\"18446744073709551615\"");
    expect(wire.writeSingle(0.1)).toBe("0.1");
    expect(wire.writeDecimal("-12.50")).toBe("\"-12.50\"");
    expect(wire.writeChar("é")).toBe("\"é\"");
    expect(wire.writeBytes(new Uint8Array([1, 2, 3, 255]))).toBe("\"AQID/w==\"");
    expect(wire.writeBytes(new Uint8Array(0))).toBe("\"\"");
    const large = new Uint8Array(100_000).map((_, i) => i % 256);
    expect(wire.readBytes(JSON.parse(wire.writeBytes(large)))).toEqual(large);
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
