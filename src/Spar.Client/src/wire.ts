// Wire values read and written one field at a time (plan section 2, the wire-rules table), for the generated contract
// modules. Readers check each value's JSON type and throw a TypeError on a mismatch, which the connection reports as
// protocol / malformed. Writers produce JSON text directly: `long` values are `bigint`, which JSON.stringify cannot
// write, and a value the backend would refuse (NaN, a fraction where an integer belongs) is refused here first.

// As the C# reader: no sign on zero, no leading zeros, no plus sign, at most 19 digits.
const canonicalInteger = /^(0|-?[1-9][0-9]{0,18})$/;
const int64Min = -(2n ** 63n);
const int64Max = 2n ** 63n - 1n;
const int32Min = -(2 ** 31);
const int32Max = 2 ** 31 - 1;

/** A JSON object's own properties, as JSON.parse produced them. */
export type JsonObject = Readonly<Record<string, unknown>>;

export function readObject(value: unknown): JsonObject {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    throw new TypeError(`Expected a JSON object, got ${describe(value)}.`);
  }

  return value as JsonObject;
}

/** One property of a parsed object: its own value only, never one inherited from `Object.prototype`. */
export function field(object: JsonObject, name: string): unknown {
  return Object.hasOwn(object, name) ? object[name] : undefined;
}

export function readString(value: unknown): string {
  if (typeof value !== "string") {
    throw new TypeError(`Expected a string, got ${describe(value)}.`);
  }

  return value;
}

export function readBoolean(value: unknown): boolean {
  if (typeof value !== "boolean") {
    throw new TypeError(`Expected a boolean, got ${describe(value)}.`);
  }

  return value;
}

export function readInt32(value: unknown): number {
  if (typeof value !== "number" || !Number.isInteger(value) || value < int32Min || value > int32Max) {
    throw new TypeError(`Expected a 32-bit integer, got ${describe(value)}.`);
  }

  return value;
}

/** A `long`: always a canonical decimal string on the wire, so no digit is lost. */
export function readInt64(value: unknown): bigint {
  if (typeof value !== "string" || !canonicalInteger.test(value)) {
    throw new TypeError(`Expected a 64-bit integer as a decimal string, got ${describe(value)}.`);
  }

  const parsed = BigInt(value);
  if (parsed < int64Min || parsed > int64Max) {
    throw new TypeError(`The 64-bit integer ${value} is out of range.`);
  }

  return parsed;
}

export function readDouble(value: unknown): number {
  if (typeof value !== "number") {
    throw new TypeError(`Expected a number, got ${describe(value)}.`);
  }

  return value;
}

export function readList<T>(value: unknown, read: (item: unknown) => T): readonly T[] {
  if (!Array.isArray(value)) {
    throw new TypeError(`Expected a JSON array, got ${describe(value)}.`);
  }

  return value.map(item => read(item));
}

/** A nullable value: `null`, or missing, is `null`. */
export function readNullable<T>(value: unknown, read: (value: unknown) => T): T | null {
  return value === null || value === undefined ? null : read(value);
}

/** Lone surrogates become U+FFFD, as every host does (wire rules). */
export function writeString(value: string): string {
  if (typeof value !== "string") {
    throw new TypeError(`Expected a string, got ${describe(value)}.`);
  }

  return JSON.stringify(value.toWellFormed());
}

export function writeBoolean(value: boolean): string {
  if (typeof value !== "boolean") {
    throw new TypeError(`Expected a boolean, got ${describe(value)}.`);
  }

  return value ? "true" : "false";
}

export function writeInt32(value: number): string {
  if (!Number.isInteger(value) || value < int32Min || value > int32Max) {
    throw new RangeError(`Expected a 32-bit integer, got ${describe(value)}.`);
  }

  return String(value);
}

export function writeInt64(value: bigint): string {
  if (typeof value !== "bigint" || value < int64Min || value > int64Max) {
    throw new RangeError(`Expected a 64-bit integer as a bigint, got ${describe(value)}.`);
  }

  return `"${value}"`;
}

export function writeDouble(value: number): string {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    throw new RangeError(`Expected a finite number, got ${describe(value)}.`);
  }

  return JSON.stringify(value);
}

export function writeList<T>(items: readonly T[], write: (item: T) => string): string {
  if (!Array.isArray(items)) {
    throw new TypeError(`Expected an array, got ${describe(items)}.`);
  }

  return `[${items.map(item => write(item)).join(",")}]`;
}

/** One property of a JSON object being written: its name, then its already written value. */
export function member(name: string, json: string): string {
  return `${JSON.stringify(name)}:${json}`;
}

export function writeObject(members: readonly string[]): string {
  return `{${members.join(",")}}`;
}

export function writeNullable<T>(value: T | null | undefined, write: (value: T) => string): string {
  return value === null || value === undefined ? "null" : write(value);
}

function describe(value: unknown): string {
  if (value === null) {
    return "null";
  }

  if (Array.isArray(value)) {
    return "an array";
  }

  return typeof value === "string" ? "a string" : `${typeof value} ${String(value).slice(0, 40)}`;
}
