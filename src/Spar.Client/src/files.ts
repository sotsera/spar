// Files the user chooses (decision "M3c's decide-first questions", items 2 and 3, and "The drop design, from the
// file-drop spike"): the host's open, save and folder dialogs, and drops of files from the OS on areas the page
// declares. Each chosen file or folder is a grant of the window: the page sees its name, never its path, reads and
// writes it through the host within the bridge's 4 MiB message, reads a file of any size as a blob on the app's own
// origin, and hands it to a command, whose C# reads its path.
import { readBlob, type SparBlob } from "./blobs.js";
import { unknownErrorCase, type Decoder } from "./connection.js";
import type { Call, CallOptions, ErrorCases, Result } from "./results.js";
import { invoke, invokeWithFiles } from "./sdk.js";
import { isWebKitHost } from "./webkit.js";
import { field, member, readBoolean, readBytes, readDouble, readList, readObject, readString, writeBytes, writeObject, writeString } from "./wire.js";

/** What a grant allows: reading, or reading and writing. */
export type FileAccess = "read" | "readWrite";

/** A dialog's filter: what it shows, and the extensions it takes, as `["md", "markdown"]`. */
export interface FileFilter {
  readonly name: string;
  readonly extensions: readonly string[];
}

export interface OpenFileOptions extends CallOptions {
  readonly title?: string;
  readonly filters?: readonly FileFilter[];
}

export interface SaveFileOptions extends OpenFileOptions {
  /** The name the dialog suggests, as `notes.md`. */
  readonly suggestedName?: string;
}

export interface OpenFolderOptions extends CallOptions {
  readonly title?: string;
  /** `read` (the default) lists and reads; `readWrite` also writes, creates folders and deletes. */
  readonly access?: FileAccess;
}

/** Why a dialog gave no file. A closed dialog is `dismissed`, never the `cancelled` of an aborted call. */
export interface FileDialogError {
  /** The user closed the dialog without choosing. */
  readonly dismissed: undefined;
  /** The OS or a sandbox refused the chosen location. */
  readonly accessDenied: { readonly path: string };
  /** No dialog can be shown, as on a Linux desktop without a file-chooser portal; `reason` says why. */
  readonly unavailable: { readonly reason: string };
  /** A dialog is already open for this window, or the window holds the most grants it may. */
  readonly busy: undefined;
}

/** Why a granted file or folder could not be read or written. */
export interface FileAccessError {
  /** The file was deleted or moved since it was chosen, or the path in the folder does not exist. */
  readonly notFound: undefined;
  readonly accessDenied: undefined;
  /** Another program holds the file. */
  readonly inUse: undefined;
  /** Past what one message carries (`limit` bytes): read it with `blob()`, or write it through a command. */
  readonly tooLarge: { readonly limit: number };
  /** The grant's window closed, or its expiry passed. */
  readonly grantExpired: undefined;
  /** The path leaves the folder: absolute, `..`, or a link pointing outside. */
  readonly outsideFolder: { readonly path: string };
  readonly readOnlyGrant: undefined;
  /** Any other failure of the file system, such as a full disk. */
  readonly failed: { readonly reason: string };
}

/** Why a drop on an area gave no files. */
export interface DropError {
  /** A file the area does not take (its extension, or a folder where folders are not taken). */
  readonly notAccepted: { readonly name: string };
  /** More files than the area takes at once. */
  readonly tooMany: { readonly limit: number };
}

/** An entry of a granted folder: its path relative to the folder, with `/` between its parts. */
export interface FolderEntry {
  readonly name: string;
  readonly isFolder: boolean;
  readonly size: number;
  /** When it was last written, as an ISO 8601 time. */
  readonly modified: string;
}

/** A file the user chose. Its path stays with the host; a command that takes it as a `FileGrant` reads it in C#. */
export interface PickedFile {
  readonly name: string;
  /** Its size in bytes when it was chosen; 0 for a new file from a save dialog. */
  readonly size: number;
  readonly access: FileAccess;
  /** The whole file as text (UTF-8 unless its byte order mark says otherwise), at most 4 MiB. */
  readText(options?: CallOptions): Call<string, FileAccessError>;
  /** The whole file, at most 4 MiB. */
  readBytes(options?: CallOptions): Call<Uint8Array, FileAccessError>;
  /** The file as a blob on the app's own origin, for any size: put its `url` in an `<img>` or read it in parts. */
  blob(contentType?: string, options?: CallOptions): Call<SparBlob, FileAccessError>;
  /** Replaces the file safely (a temporary file beside it, then a replace); needs a save dialog's grant. */
  writeText(text: string, options?: CallOptions): Call<void, FileAccessError>;
  writeBytes(bytes: Uint8Array, options?: CallOptions): Call<void, FileAccessError>;
}

/** A folder the user chose. Every path is relative to it and cannot leave it. */
export interface PickedFolder {
  readonly name: string;
  readonly access: FileAccess;
  /** The entries of the folder, or of one of its folders, sorted by name. */
  list(path?: string, options?: CallOptions): Call<readonly FolderEntry[], FileAccessError>;
  readText(path: string, options?: CallOptions): Call<string, FileAccessError>;
  readBytes(path: string, options?: CallOptions): Call<Uint8Array, FileAccessError>;
  blob(path: string, contentType?: string, options?: CallOptions): Call<SparBlob, FileAccessError>;
  writeText(path: string, text: string, options?: CallOptions): Call<void, FileAccessError>;
  writeBytes(path: string, bytes: Uint8Array, options?: CallOptions): Call<void, FileAccessError>;
  /** Creates a folder and the ones above it inside the grant. */
  createFolder(path: string, options?: CallOptions): Call<void, FileAccessError>;
  /** Deletes a file, or a folder with everything in it. */
  delete(path: string, options?: CallOptions): Call<void, FileAccessError>;
}

/** What one drop on an area gave. */
export interface DroppedItems {
  readonly files: readonly PickedFile[];
  /** Folders, when the area takes them. */
  readonly folders: readonly PickedFolder[];
}

/** What a drop area takes. */
export interface DropTargetOptions {
  /** The extensions it takes, as `[".csv"]`; any file without it. */
  readonly accept?: readonly string[];
  /** Whether it takes folders (false by default). */
  readonly folders?: boolean;
  /** The most files one drop gives; 100 by default. */
  readonly limit?: number;
}

const contract = "spar";

const dialogCodes = ["dismissed", "accessDenied", "unavailable", "busy"];
const accessCodes = ["notFound", "accessDenied", "inUse", "tooLarge", "grantExpired", "outsideFolder", "readOnlyGrant", "failed"];
const dropCodes = ["notAccepted", "tooMany"];

/** A decoder whose declared errors are `codes`, each payload kept as the host sent it. */
function decoderOf<T>(value: (json: string) => T, codes: readonly string[]): Decoder<T> {
  return {
    value,
    error: (code, payload) => (codes.includes(code) ? errorPayload(payload) : unknownErrorCase),
  };
}

/** A declared error's fields, copied one by one by their known names (never a spread of parsed JSON). */
function errorPayload(payload: Readonly<Record<string, unknown>>): unknown {
  const fields: { path?: string; reason?: string; name?: string; limit?: number } = {};
  let any = false;
  for (const name of ["path", "reason", "name"] as const) {
    if (typeof payload[name] === "string") {
      fields[name] = payload[name];
      any = true;
    }
  }

  if (typeof payload.limit === "number") {
    fields.limit = payload.limit;
    any = true;
  }

  return any ? fields : undefined;
}

const noValue = (): undefined => undefined;
const textValue = (json: string): string => readString(JSON.parse(json));
const bytesValue = (json: string): Uint8Array => readBytes(JSON.parse(json));
const blobValue = (json: string): SparBlob => readBlob(JSON.parse(json));
const fileValue = (json: string): PickedFile => readFile(JSON.parse(json));
const filesValue = (json: string): readonly PickedFile[] => readList(JSON.parse(json), readFile);
const folderValue = (json: string): PickedFolder => readFolder(JSON.parse(json));
const entriesValue = (json: string): readonly FolderEntry[] => readList(JSON.parse(json), readEntry);

const grantId = /^f[gd]_[0-9a-f]{32}$/;

/** @internal Called by generated contract modules: a file grant a result carries, `{"id","name","size","access"}`. */
export function readFile(value: unknown): PickedFile {
  const object = readObject(value);
  const id = readGrantId(field(object, "id"), "fg_");
  const size = readDouble(field(object, "size"));
  if (!Number.isSafeInteger(size) || size < 0) {
    throw new TypeError(`Expected a file size, got ${JSON.stringify(size)}.`);
  }

  return new GrantedFile(id, readString(field(object, "name")), size, readAccess(field(object, "access")));
}

/** @internal Called by generated contract modules: a folder grant a result carries, `{"id","name","access"}`. */
export function readFolder(value: unknown): PickedFolder {
  const object = readObject(value);
  const id = readGrantId(field(object, "id"), "fd_");
  return new GrantedFolder(id, readString(field(object, "name")), readAccess(field(object, "access")));
}

/**
 * @internal Called by generated contract modules: a file grant in a request, or in a page test's scripted reply. The
 * backend reads only its id, which it resolves to the window's own grant; the other fields are what a result carries.
 */
export function writeFile(file: PickedFile): string {
  if (!(file instanceof GrantedFile)) {
    throw new TypeError("A FileGrant parameter takes a PickedFile from a dialog, a drop or a command's result.");
  }

  const members = [member("id", writeString(GrantedFile.idOf(file))), member("name", writeString(file.name))];
  members.push(member("size", String(file.size)), member("access", writeString(file.access)));
  return writeObject(members);
}

/** @internal Called by generated contract modules: a folder grant in a request, or in a scripted reply, as for a file. */
export function writeFolder(folder: PickedFolder): string {
  if (!(folder instanceof GrantedFolder)) {
    throw new TypeError("A FolderGrant parameter takes a PickedFolder from a dialog, a drop or a command's result.");
  }

  const members = [member("id", writeString(GrantedFolder.idOf(folder))), member("name", writeString(folder.name))];
  members.push(member("access", writeString(folder.access)));
  return writeObject(members);
}

function readGrantId(value: unknown, prefix: string): string {
  const id = readString(value);
  if (!grantId.test(id) || !id.startsWith(prefix)) {
    throw new TypeError(`Expected a grant id starting ${prefix}, got ${JSON.stringify(id)}.`);
  }

  return id;
}

function readAccess(value: unknown): FileAccess {
  const access = readString(value);
  if (access !== "read" && access !== "readWrite") {
    throw new TypeError(`Expected "read" or "readWrite", got ${JSON.stringify(access)}.`);
  }

  return access;
}

function readEntry(value: unknown): FolderEntry {
  const object = readObject(value);
  return {
    name: readString(field(object, "name")),
    isFolder: readBoolean(field(object, "isFolder")),
    size: readDouble(field(object, "size")),
    modified: readString(field(object, "modified")),
  };
}

function call<T, E extends ErrorCases>(command: string, request: () => string, decoder: Decoder<T>, options?: CallOptions): Call<T, E> {
  return invoke<T, E>(contract, command, request, decoder, options);
}

/**
 * A request object: strings through the wire's writer (a lone surrogate becomes U+FFFD), anything else as JSON, and
 * members that are undefined left out.
 */
function requestOf(members: Record<string, unknown>): () => string {
  return () => {
    const parts: string[] = [];
    for (const [name, value] of Object.entries(members)) {
      if (value === undefined) {
        continue;
      }

      parts.push(member(name, typeof value === "string" ? writeString(value) : JSON.stringify(value)));
    }

    return writeObject(parts);
  };
}

function filtersOf(filters: readonly FileFilter[] | undefined): unknown {
  return filters?.map(filter => ({ name: filter.name, extensions: [...filter.extensions] }));
}

function writeRequest(id: string, path: string | undefined, content: { text: string } | { bytes: Uint8Array }): () => string {
  return () => {
    const parts = [member("grant", writeString(id))];
    if (path !== undefined) {
      parts.push(member("path", writeString(path)));
    }

    parts.push("text" in content ? member("text", writeString(content.text)) : member("bytes", writeBytes(content.bytes)));
    return writeObject(parts);
  };
}

class GrantedFile implements PickedFile {
  readonly #id: string;
  readonly name: string;
  readonly size: number;
  readonly access: FileAccess;

  constructor(id: string, name: string, size: number, access: FileAccess) {
    this.#id = id;
    this.name = name;
    this.size = size;
    this.access = access;
  }

  static idOf(file: GrantedFile): string {
    return file.#id;
  }

  get [Symbol.toStringTag](): string {
    return "PickedFile";
  }

  readText(options?: CallOptions): Call<string, FileAccessError> {
    return call("readFile", requestOf({ grant: this.#id, as: "text" }), decoderOf(textValue, accessCodes), options);
  }

  readBytes(options?: CallOptions): Call<Uint8Array, FileAccessError> {
    return call("readFile", requestOf({ grant: this.#id, as: "bytes" }), decoderOf(bytesValue, accessCodes), options);
  }

  blob(contentType?: string, options?: CallOptions): Call<SparBlob, FileAccessError> {
    return call("fileBlob", requestOf({ grant: this.#id, type: contentType }), decoderOf(blobValue, accessCodes), options);
  }

  writeText(text: string, options?: CallOptions): Call<void, FileAccessError> {
    return call("writeFile", writeRequest(this.#id, undefined, { text }), decoderOf(noValue, accessCodes), options);
  }

  writeBytes(bytes: Uint8Array, options?: CallOptions): Call<void, FileAccessError> {
    return call("writeFile", writeRequest(this.#id, undefined, { bytes }), decoderOf(noValue, accessCodes), options);
  }
}

class GrantedFolder implements PickedFolder {
  readonly #id: string;
  readonly name: string;
  readonly access: FileAccess;

  constructor(id: string, name: string, access: FileAccess) {
    this.#id = id;
    this.name = name;
    this.access = access;
  }

  static idOf(folder: GrantedFolder): string {
    return folder.#id;
  }

  get [Symbol.toStringTag](): string {
    return "PickedFolder";
  }

  list(path?: string, options?: CallOptions): Call<readonly FolderEntry[], FileAccessError> {
    return call("listFolder", requestOf({ grant: this.#id, path }), decoderOf(entriesValue, accessCodes), options);
  }

  readText(path: string, options?: CallOptions): Call<string, FileAccessError> {
    return call("readFile", requestOf({ grant: this.#id, path, as: "text" }), decoderOf(textValue, accessCodes), options);
  }

  readBytes(path: string, options?: CallOptions): Call<Uint8Array, FileAccessError> {
    return call("readFile", requestOf({ grant: this.#id, path, as: "bytes" }), decoderOf(bytesValue, accessCodes), options);
  }

  blob(path: string, contentType?: string, options?: CallOptions): Call<SparBlob, FileAccessError> {
    return call("fileBlob", requestOf({ grant: this.#id, path, type: contentType }), decoderOf(blobValue, accessCodes), options);
  }

  writeText(path: string, text: string, options?: CallOptions): Call<void, FileAccessError> {
    return call("writeFile", writeRequest(this.#id, path, { text }), decoderOf(noValue, accessCodes), options);
  }

  writeBytes(path: string, bytes: Uint8Array, options?: CallOptions): Call<void, FileAccessError> {
    return call("writeFile", writeRequest(this.#id, path, { bytes }), decoderOf(noValue, accessCodes), options);
  }

  createFolder(path: string, options?: CallOptions): Call<void, FileAccessError> {
    return call("createFolder", requestOf({ grant: this.#id, path }), decoderOf(noValue, accessCodes), options);
  }

  delete(path: string, options?: CallOptions): Call<void, FileAccessError> {
    return call("deleteEntry", requestOf({ grant: this.#id, path }), decoderOf(noValue, accessCodes), options);
  }
}

const droppedValue = (json: string): DroppedItems => {
  const object = readObject(JSON.parse(json));
  return {
    files: readList(field(object, "files"), readFile),
    folders: readList(field(object, "folders"), readFolder),
  };
};

/**
 * Whether a drag may carry files from the OS (and not text or an element of the page). WebKitGTK lists a drag of files
 * as `text/uri-list` only, without `Files` (WebKit bugs 320301 and 323277), so there a URI list counts too: the Linux
 * host reads the drag's files itself and grants only local files, so a drag of a web link gives nothing.
 */
function carriesFiles(event: DragEvent): boolean {
  const types = event.dataTransfer?.types;
  if (types === undefined) {
    return false;
  }

  return types.includes("Files") || (isWebKitHost() && types.includes("text/uri-list"));
}

/**
 * Makes `area` take files dropped from the OS; returns the function that stops it, for example in a React effect's
 * cleanup. While a drag of files hovers the area, it has `data-spar-drop="over"`, for CSS. Each drop gives `handler`
 * one settled result: the dropped files and folders as grants, or why the area did not take them; a failure of the
 * call (the host went away) goes to its `failure` handler or to `configureSpar`'s `onFailure`, as for any call. Drags of the page's own
 * elements are left alone, and a drop anywhere else stays refused.
 */
function dropTarget(area: Element, options: DropTargetOptions, handler: (result: Result<DroppedItems, DropError>) => void): () => void {
  const request = requestOf({ accept: options.accept, folders: options.folders, limit: options.limit });
  let depth = 0;

  const over = (event: Event): void => {
    const drag = event as DragEvent;
    if (!carriesFiles(drag)) {
      return;
    }

    // Taking the drag over is what lets it drop here: without it the browser would refuse the drop.
    drag.preventDefault();
    if (drag.dataTransfer !== null) {
      drag.dataTransfer.dropEffect = "copy";
    }

    if (event.type === "dragenter") {
      depth += 1;
      area.setAttribute("data-spar-drop", "over");
    }
  };

  const leave = (event: Event): void => {
    if (!carriesFiles(event as DragEvent)) {
      return;
    }

    depth = Math.max(0, depth - 1);
    if (depth === 0) {
      area.removeAttribute("data-spar-drop");
    }
  };

  const drop = (event: Event): void => {
    const drag = event as DragEvent;
    if (!carriesFiles(drag)) {
      return;
    }

    drag.preventDefault();
    depth = 0;
    area.removeAttribute("data-spar-drop");

    // The files must be taken now: the DataTransfer is empty once the event ends. WebView2 hands them to the host with
    // the message; the Linux host read them itself as the drag entered, and ignores these.
    const dropped = Array.from(drag.dataTransfer?.files ?? []);
    void invokeWithFiles<DroppedItems, DropError>(contract, "dropped", request, decoderOf(droppedValue, dropCodes), dropped).then(handler);
  };

  area.addEventListener("dragenter", over);
  area.addEventListener("dragover", over);
  area.addEventListener("dragleave", leave);
  area.addEventListener("drop", drop);
  return () => {
    area.removeEventListener("dragenter", over);
    area.removeEventListener("dragover", over);
    area.removeEventListener("dragleave", leave);
    area.removeEventListener("drop", drop);
    area.removeAttribute("data-spar-drop");
  };
}

/**
 * The host's file dialogs and the page's drop areas.
 *
 * ```ts
 * await files.open({ title: "Open notes", filters: [{ name: "Text", extensions: ["txt", "md"] }] }).match({
 *   success: async file => show(await file.readText().unwrap()),
 *   errors: { dismissed: () => {}, accessDenied: e => toast(e.path), unavailable: e => toast(e.reason), busy: () => {} },
 * });
 * ```
 */
export const files = {
  /** Lets the user choose one file to read. */
  open(options: OpenFileOptions = {}): Call<PickedFile, FileDialogError> {
    return call("openFile", requestOf({ title: options.title, filters: filtersOf(options.filters) }), decoderOf(fileValue, dialogCodes), options);
  },

  /** Lets the user choose one or more files to read. */
  openMany(options: OpenFileOptions = {}): Call<readonly PickedFile[], FileDialogError> {
    return call("openFiles", requestOf({ title: options.title, filters: filtersOf(options.filters) }), decoderOf(filesValue, dialogCodes), options);
  },

  /** Lets the user choose where to save; the file it gives can be written, and need not exist yet. */
  save(options: SaveFileOptions = {}): Call<PickedFile, FileDialogError> {
    const request = requestOf({ title: options.title, filters: filtersOf(options.filters), suggestedName: options.suggestedName });
    return call("saveFile", request, decoderOf(fileValue, dialogCodes), options);
  },

  /** Lets the user choose a folder. */
  openFolder(options: OpenFolderOptions = {}): Call<PickedFolder, FileDialogError> {
    return call("openFolder", requestOf({ title: options.title, access: options.access }), decoderOf(folderValue, dialogCodes), options);
  },

  dropTarget,
} as const;
