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
const uint64Max = 2n ** 64n - 1n;
const canonicalUnsigned = /^(0|[1-9][0-9]{0,19})$/;

// As the C# reader writes them back: no plus sign, no leading zeros, the scale kept, no exponent, no negative zero.
const canonicalDecimal = /^-?(0|[1-9][0-9]*)(\.[0-9]+)?$/;
const negativeZero = /^-0(\.0+)?$/;

/**
 * Padded base64 with no whitespace, as the C# reader accepts it. A loop rather than one regular expression: a repeated
 * group over a value of a few megabytes made the engine give up, and every large `byte[]` failed as malformed.
 */
function isPaddedBase64(text: string): boolean {
  if (text.length % 4 !== 0) {
    return false;
  }

  const padding = text.endsWith("==") ? 2 : text.endsWith("=") ? 1 : 0;
  const end = text.length - padding;
  for (let i = 0; i < end; i++) {
    const code = text.charCodeAt(i);
    const letter = (code >= 65 && code <= 90) || (code >= 97 && code <= 122);
    const digit = code >= 48 && code <= 57;
    if (!letter && !digit && code !== 43 && code !== 47) {
      return false;
    }
  }

  return true;
}

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

export function readByte(value: unknown): number {
  return readInteger(value, 0, 255, "an 8-bit unsigned integer");
}

export function readSByte(value: unknown): number {
  return readInteger(value, -128, 127, "an 8-bit integer");
}

export function readInt16(value: unknown): number {
  return readInteger(value, -32768, 32767, "a 16-bit integer");
}

export function readUInt16(value: unknown): number {
  return readInteger(value, 0, 65535, "a 16-bit unsigned integer");
}

export function readInt32(value: unknown): number {
  return readInteger(value, int32Min, int32Max, "a 32-bit integer");
}

export function readUInt32(value: unknown): number {
  return readInteger(value, 0, 2 ** 32 - 1, "a 32-bit unsigned integer");
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

/** A `ulong`: a canonical decimal string, like `long`. */
export function readUInt64(value: unknown): bigint {
  if (typeof value !== "string" || !canonicalUnsigned.test(value)) {
    throw new TypeError(`Expected a 64-bit unsigned integer as a decimal string, got ${describe(value)}.`);
  }

  const parsed = BigInt(value);
  if (parsed > uint64Max) {
    throw new TypeError(`The 64-bit unsigned integer ${value} is out of range.`);
  }

  return parsed;
}

export function readSingle(value: unknown): number {
  return readDouble(value);
}

/** A `decimal` stays a string, so no digit or trailing zero is lost. */
export function readDecimal(value: unknown): string {
  if (typeof value !== "string" || !isCanonicalDecimal(value)) {
    throw new TypeError(`Expected a decimal as a string, got ${describe(value)}.`);
  }

  return value;
}

/** A `char`: one UTF-16 code unit. */
export function readChar(value: unknown): string {
  if (typeof value !== "string" || value.length !== 1) {
    throw new TypeError(`Expected one character, got ${describe(value)}.`);
  }

  return value;
}

/** A `byte[]`: padded base64 on the wire. */
export function readBytes(value: unknown): Uint8Array {
  if (typeof value !== "string" || !isPaddedBase64(value)) {
    throw new TypeError(`Expected padded base64, got ${describe(value)}.`);
  }

  const binary = atob(value);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i);
  }

  return bytes;
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

export function writeByte(value: number): string {
  return writeInteger(value, 0, 255, "an 8-bit unsigned integer");
}

export function writeSByte(value: number): string {
  return writeInteger(value, -128, 127, "an 8-bit integer");
}

export function writeInt16(value: number): string {
  return writeInteger(value, -32768, 32767, "a 16-bit integer");
}

export function writeUInt16(value: number): string {
  return writeInteger(value, 0, 65535, "a 16-bit unsigned integer");
}

export function writeInt32(value: number): string {
  return writeInteger(value, int32Min, int32Max, "a 32-bit integer");
}

export function writeUInt32(value: number): string {
  return writeInteger(value, 0, 2 ** 32 - 1, "a 32-bit unsigned integer");
}

export function writeInt64(value: bigint): string {
  if (typeof value !== "bigint" || value < int64Min || value > int64Max) {
    throw new RangeError(`Expected a 64-bit integer as a bigint, got ${describe(value)}.`);
  }

  return `"${value}"`;
}

export function writeUInt64(value: bigint): string {
  if (typeof value !== "bigint" || value < 0n || value > uint64Max) {
    throw new RangeError(`Expected a 64-bit unsigned integer as a bigint, got ${describe(value)}.`);
  }

  return `"${value}"`;
}

/** A `float`: finite, and still finite once rounded to single precision, or the backend would refuse it. */
export function writeSingle(value: number): string {
  if (typeof value !== "number" || !Number.isFinite(Math.fround(value))) {
    throw new RangeError(`Expected a number in the range of a float, got ${describe(value)}.`);
  }

  return JSON.stringify(value);
}

export function writeDecimal(value: string): string {
  if (typeof value !== "string" || !isCanonicalDecimal(value)) {
    throw new RangeError(`Expected a decimal as a string such as "-12.50", got ${describe(value)}.`);
  }

  return JSON.stringify(value);
}

export function writeChar(value: string): string {
  if (typeof value !== "string" || value.length !== 1) {
    throw new RangeError(`Expected one character, got ${describe(value)}.`);
  }

  return writeString(value);
}

export function writeBytes(value: Uint8Array): string {
  if (!(value instanceof Uint8Array)) {
    throw new TypeError(`Expected a Uint8Array, got ${describe(value)}.`);
  }

  // In slices, so a large array never becomes one huge argument list.
  let binary = "";
  for (let start = 0; start < value.length; start += 0x8000) {
    binary += String.fromCharCode(...value.subarray(start, start + 0x8000));
  }

  return `"${btoa(binary)}"`;
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

/** An enum's reader met a value that is none of its member names. */
export function unknownMember(value: unknown, type: string): never {
  throw new TypeError(`Expected a member of ${type}, got ${describe(value)}.`);
}

/** An enum's writer was given a value that is none of its member names. */
export function refusedMember(value: unknown, type: string): never {
  throw new RangeError(`Expected a member of ${type}, got ${describe(value)}.`);
}

function readInteger(value: unknown, min: number, max: number, what: string): number {
  if (typeof value !== "number" || !Number.isInteger(value) || value < min || value > max) {
    throw new TypeError(`Expected ${what}, got ${describe(value)}.`);
  }

  return value;
}

function writeInteger(value: number, min: number, max: number, what: string): string {
  if (!Number.isInteger(value) || value < min || value > max) {
    throw new RangeError(`Expected ${what}, got ${describe(value)}.`);
  }

  return String(value);
}

function isCanonicalDecimal(value: string): boolean {
  return canonicalDecimal.test(value) && !negativeZero.test(value);
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
