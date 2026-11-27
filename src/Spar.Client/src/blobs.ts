// Binary data from the backend (decision "M3b's decide-first questions", item 2), read on the app's own origin rather
// than through the bridge: a handle a result gave the page (`SparBlob`), valid until the page releases it or its
// document goes, and the address of a resource, which runs its command at each load and keeps nothing.
import { unknownErrorCase, type Decoder } from "./connection.js";
import { invoke, pageAddressKey } from "./sdk.js";
import { field, readObject, readString } from "./wire.js";

/**
 * Binary data a command returned, read from the app's own origin: put `url` in an `<img>`, a `<video>` or a download
 * link, or read the bytes. It stays valid until `release()` or until the page's document goes (a reload, a
 * navigation); the backend keeps it until then, so release the handles the page no longer shows.
 *
 * ```ts
 * const report = (await reports.export(request)).unwrap();
 * link.href = report.url;
 * link.download = report.name ?? "report.csv";
 * ```
 */
export interface SparBlob {
  /** The blob's address on the app's origin. */
  readonly url: string;
  /** Its media type, as `image/png`. */
  readonly contentType: string;
  /** Its size in bytes. */
  readonly size: number;
  /** The name the backend gave it, or null. */
  readonly name: string | null;
  /** Reads the bytes; rejects when the handle was released or its document went. */
  bytes(signal?: AbortSignal): Promise<Uint8Array>;
  /** Reads the bytes as a `Blob` of its type. */
  blob(signal?: AbortSignal): Promise<Blob>;
  /** Lets the backend drop it; releasing one twice, or after its document went, is no error. */
  release(): Promise<void>;
}

/** @internal Page tests: where a blob made by `spar.blob(…)` keeps its bytes, by its id, instead of the origin. */
export const fakeBlobs = new Map<string, Uint8Array>();

const blobPrefix = "/_spar/blob/";
const resourcePrefix = "/_spar/resource/";

const releaseDecoder: Decoder<void> = { value: () => undefined, error: () => unknownErrorCase };

/** @internal Called by generated contract modules: the handle a result carries, `{"id","contentType","size","name"}`. */
export function readBlob(value: unknown): SparBlob {
  const object = readObject(value);
  const id = readString(field(object, "id"));
  if (!/^[0-9a-f]{32}$/.test(id)) {
    throw new TypeError(`Expected a blob id of 32 hex digits, got ${JSON.stringify(id)}.`);
  }

  const size = field(object, "size");
  if (typeof size !== "number" || !Number.isSafeInteger(size) || size < 0) {
    throw new TypeError(`Expected a blob size, got ${JSON.stringify(size)}.`);
  }

  const name = field(object, "name");
  return new Handle(id, readString(field(object, "contentType")), size, name === undefined || name === null ? null : readString(name));
}

/** @internal Page tests: writes a blob as a handle, for the fake backend's replies. */
export function writeBlob(blob: SparBlob): string {
  if (!(blob instanceof Handle)) {
    throw new TypeError("A fake reply's SparBlob must come from spar.blob(…).");
  }

  const name = blob.name === null ? "" : `,"name":${JSON.stringify(blob.name)}`;
  return `{"id":${JSON.stringify(blob.id)},"contentType":${JSON.stringify(blob.contentType)},"size":${blob.size}${name}}`;
}

/** @internal Page tests: a blob whose bytes the fake keeps. */
export function fakeBlob(bytes: Uint8Array, contentType: string, name: string | null): SparBlob {
  const id = Array.from(crypto.getRandomValues(new Uint8Array(16)), byte => byte.toString(16).padStart(2, "0")).join("");
  fakeBlobs.set(id, bytes);
  return new Handle(id, contentType, bytes.length, name);
}

/**
 * @internal Called by generated contract modules: a resource's address for one request, its JSON in `r=`. A request
 * the encoder refuses (NaN, a `long` out of range) throws, as building an address for it cannot succeed.
 */
export function resourceUrl(contract: string, command: string, encode: (() => string) | undefined): string {
  const query: string[] = [];
  if (encode !== undefined) {
    query.push(`r=${encodeURIComponent(encode())}`);
  }

  return withKey(`${resourcePrefix}${contract}/${command}`, query);
}

class Handle implements SparBlob {
  readonly id: string;
  readonly contentType: string;
  readonly size: number;
  readonly name: string | null;

  constructor(id: string, contentType: string, size: number, name: string | null) {
    this.id = id;
    this.contentType = contentType;
    this.size = size;
    this.name = name;
  }

  get url(): string {
    return withKey(blobPrefix + this.id, []);
  }

  async bytes(signal?: AbortSignal): Promise<Uint8Array> {
    const fake = fakeBlobs.get(this.id);
    if (fake !== undefined) {
      return fake;
    }

    const response = await fetch(this.url, { signal: signal ?? null, cache: "no-store" });
    if (!response.ok) {
      throw new Error(`The blob ${this.name ?? this.id} could not be read: ${response.status} ${response.statusText}.`);
    }

    return new Uint8Array(await response.arrayBuffer());
  }

  async blob(signal?: AbortSignal): Promise<Blob> {
    const bytes = await this.bytes(signal);
    return new Blob([bytes as Uint8Array<ArrayBuffer>], { type: this.contentType });
  }

  async release(): Promise<void> {
    if (fakeBlobs.delete(this.id)) {
      return;
    }

    // A failure (the document is going) leaves nothing to do: the handle goes with the document.
    await invoke<void, {}>("spar", "releaseBlob", () => JSON.stringify(this.id), releaseDecoder);
  }
}

/** An address on the page's origin, with the channel's key on loopback. */
function withKey(path: string, query: string[]): string {
  const key = pageAddressKey();
  const parameters = key === undefined ? query : [...query, `k=${key}`];
  return parameters.length === 0 ? path : `${path}?${parameters.join("&")}`;
}
