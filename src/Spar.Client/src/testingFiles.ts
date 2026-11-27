// Page tests of files (`@spar/app/testing`): the host's dialogs as scriptable commands, grants the fake keeps the
// contents of, and drops. A test makes a file with `spar.file(name, contents)`, scripts a dialog to give it, or drops it
// on an area with `spar.drop`; the page then reads and writes it through the fake, as it would through a host.
import { fakeBlob } from "./blobs.js";
import type { CommandCodec } from "./testing.js";
import { files, readFile, writeFile, type FileAccess, type PickedFile } from "./files.js";

/** What a page test reads back of a file the page wrote: its bytes, or undefined for a file the fake does not know. */
export type FakeContents = Uint8Array | undefined;

const encoder = new TextEncoder();

/** The page's file calls the fake answers by itself, from the grants it made. */
interface FileStore {
  readonly contents: Map<string, Uint8Array>;
  readonly drops: (readonly PickedFile[])[];
}

/** The SDK's dialog functions and the host's own file commands, by the function a test passes to `spar.on`. */
export function fileCommands(): Map<unknown, CommandCodec> {
  const dialogs = new Map<unknown, CommandCodec>();
  dialogs.set(files.open, dialogCodec("openFile", value => writeFile(value as PickedFile)));
  dialogs.set(files.openMany, dialogCodec("openFiles", value => `[${(value as readonly PickedFile[]).map(writeFile).join(",")}]`));
  dialogs.set(files.save, dialogCodec("saveFile", value => writeFile(value as PickedFile)));
  return dialogs;
}

function dialogCodec(command: string, value: (value: unknown) => string): CommandCodec {
  return {
    contract: "spar",
    command,
    request: json => JSON.parse(json) as unknown,
    value,
    error: (code, payload) => JSON.stringify(payload === undefined ? { code } : { code, ...(payload as object) }),
  };
}

/** A fresh store, and the host commands that read and write it. */
export function fileStore(): { readonly store: FileStore; readonly codecs: readonly [CommandCodec, (request: unknown) => unknown][] } {
  const store: FileStore = { contents: new Map(), drops: [] };
  const json = (text: string): unknown => JSON.parse(text) as unknown;
  const codecs: [CommandCodec, (request: unknown) => unknown][] = [
    [
      { contract: "spar", command: "readFile", request: json, value: value => JSON.stringify(value), error: code => JSON.stringify({ code }) },
      request => read(store, request as { grant: string; as: "text" | "bytes" }),
    ],
    [
      { contract: "spar", command: "writeFile", request: json, error: code => JSON.stringify({ code }) },
      request => write(store, request as { grant: string; text?: string; bytes?: string }),
    ],
    [
      { contract: "spar", command: "fileBlob", request: json, value: value => JSON.stringify(value) },
      request => blobOf(store, request as { grant: string; type?: string }),
    ],
    [
      { contract: "spar", command: "dropped", request: json, value: value => value as string },
      () => dropped(store),
    ],
  ];
  return { store, codecs };
}

/** A file for a page test, its contents kept by the fake under the grant's id. */
export function fakeFile(store: FileStore, name: string, contents: string | Uint8Array, access: FileAccess): PickedFile {
  const bytes = typeof contents === "string" ? encoder.encode(contents) : contents;
  const id = `fg_${Array.from(crypto.getRandomValues(new Uint8Array(16)), b => b.toString(16).padStart(2, "0")).join("")}`;
  store.contents.set(id, bytes);
  return readFile({ id, name, size: bytes.length, access });
}

/** The bytes the fake holds for a file: what a test made, or what the page wrote since. */
export function contentsOf(store: FileStore, file: PickedFile): FakeContents {
  return store.contents.get(idOf(file));
}

/**
 * Drops `dropped` on `area` as a person would: drag events with files on the element, and the fake answers the
 * page's claim with these grants.
 */
export function dropOn(store: FileStore, area: Element, dropped: readonly PickedFile[]): void {
  store.drops.push(dropped);
  const transfer = { types: ["Files"], files: dropped.map(file => new File([], file.name)), dropEffect: "none" };
  for (const type of ["dragenter", "dragover", "drop"]) {
    const event = new Event(type, { bubbles: true, cancelable: true });
    Object.defineProperty(event, "dataTransfer", { value: transfer });
    area.dispatchEvent(event);
  }
}

type Answer = { readonly kind: "value"; readonly value: unknown } | { readonly kind: "error"; readonly code: string; readonly payload: unknown };

function read(store: FileStore, request: { grant: string; as: "text" | "bytes" }): Answer {
  const bytes = store.contents.get(request.grant);
  if (bytes === undefined) {
    return { kind: "error", code: "notFound", payload: undefined };
  }

  const value = request.as === "text" ? new TextDecoder().decode(bytes) : btoa(String.fromCharCode(...bytes));
  return { kind: "value", value };
}

function write(store: FileStore, request: { grant: string; text?: string; bytes?: string }): Answer {
  const bytes = request.text !== undefined
    ? encoder.encode(request.text)
    : Uint8Array.from(atob(request.bytes ?? ""), character => character.charCodeAt(0));
  store.contents.set(request.grant, bytes);
  return { kind: "value", value: undefined };
}

function blobOf(store: FileStore, request: { grant: string; type?: string }): Answer {
  const bytes = store.contents.get(request.grant);
  if (bytes === undefined) {
    return { kind: "error", code: "notFound", payload: undefined };
  }

  const blob = fakeBlob(bytes, request.type ?? "application/octet-stream", null);
  const id = (blob as unknown as { readonly id: string }).id;
  return { kind: "value", value: { id, contentType: blob.contentType, size: blob.size } };
}

function dropped(store: FileStore): Answer {
  const next = store.drops.shift() ?? [];
  return { kind: "value", value: `{"files":[${next.map(writeFile).join(",")}],"folders":[]}` };
}

function idOf(file: PickedFile): string {
  return (JSON.parse(writeFile(file)) as { id: string }).id;
}
