// The TypeScript side of the shared conformance vectors (tests/conformance/bridge-messages.json).
import { readFileSync } from "node:fs";
import { describe, expect, test } from "vitest";
import { decode, encode, type BridgeMessage } from "../src/protocol.js";

interface Vector {
  readonly name: string;
  readonly wire: string;
  readonly message?: Record<string, unknown>;
  readonly id?: number | null;
}

interface Vectors {
  readonly canonical: readonly Vector[];
  readonly decode: readonly Vector[];
  readonly malformed: readonly Vector[];
}

const vectors = JSON.parse(
  readFileSync(new URL("../../../tests/conformance/bridge-messages.json", import.meta.url), "utf8"),
) as Vectors;

/** The vector's message as the decoder returns it: `unknown` plus `typeName` for a type this side does not know. */
function expected(message: Record<string, unknown>): BridgeMessage {
  return message as unknown as BridgeMessage;
}

describe("canonical messages", () => {
  test.each(vectors.canonical.map(v => [v.name, v] as const))("%s: the encoder writes the canonical bytes", (_, vector) => {
    expect(encode(expected(vector.message!))).toBe(vector.wire);
  });

  test.each(vectors.canonical.map(v => [v.name, v] as const))("%s: the decoder reads them back", (_, vector) => {
    const decoded = decode(vector.wire);

    expect(decoded).toEqual({ ok: true, message: expected(vector.message!) });
  });
});

describe("other valid forms", () => {
  test.each(vectors.decode.map(v => [v.name, v] as const))("%s", (_, vector) => {
    const decoded = decode(vector.wire);

    expect(decoded).toEqual({ ok: true, message: expected(vector.message!) });
  });
});

describe("malformed messages", () => {
  test.each(vectors.malformed.map(v => [v.name, v] as const))("%s", (_, vector) => {
    const decoded = decode(vector.wire);

    expect(decoded.ok).toBe(false);
    expect(decoded.ok ? "decoded" : decoded.id ?? null).toBe(vector.id ?? null);
  });
});
