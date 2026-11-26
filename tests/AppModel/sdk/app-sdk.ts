// Stand-in for `@spar/app/sdk`. Form C of docs/evidence/ts-results in ./core.ts and ./c-call/sdk.ts, with `Call` as a Promise subclass (2026-11-14),
// plus what docs/app-model.md uses beyond it: SubscribeOptions, connectSpar() and its ready(), and setColorScheme().
import type { CallOptions } from "./core.js";
import { invoke, type Call, type Result } from "./c-call/sdk.js";

export { type Call, Result, configureSpar, SparError, type SparConfiguration, type CallError, type CallErrorKind, type CallOptions, type CoalescedEvents, type Subscription, type SubscriptionEvents } from "./c-call/sdk.js";

/** How a subscription resumes after a lost connection, as declared in C# (`SubscriptionResume`). */
export type SubscriptionResume = "automatic" | "manual";

/**
 * Options of a subscription whose C# declaration resumes as `R`.
 * An `Automatic` subscription may be switched to manual resumption; a `Manual` one cannot become automatic.
 */
export interface SubscribeOptions<R extends SubscriptionResume> extends CallOptions {
  readonly resume?: R extends "automatic" ? SubscriptionResume : "manual";
}

export type ColorScheme = "light" | "dark" | "system";

/** One C# edit that Hot Reload applied to the running backend (Debug builds only), as in src/Spar.Client/src/backendUpdate.ts. */
export interface BackendUpdate {
  readonly revision: number;
  readonly time: string;
  readonly types: readonly string[];
}

/** Stand-in: the handlers `onBackendUpdate` registered. */
export const backendUpdateHandlers = new Set<(update: BackendUpdate) => void>();

/** Debug builds only: calls `handler` after each applied Hot Reload update; returns the function that stops the calls. */
export function onBackendUpdate(handler: (update: BackendUpdate) => void): () => void {
  backendUpdateHandlers.add(handler);
  return () => {
    backendUpdateHandlers.delete(handler);
  };
}

/** Stand-in for the host bridge: what the page would post to the host, in order. */
export const hostMessages: unknown[] = [];

/** The host's channel for one document, as in src/Spar.Client/src/connection.ts. */
export interface BridgeChannel {
  send(message: string): void;
  listen(received: (message: string) => void, closed: () => void): void;
}

/** One document's side of the bridge, as in src/Spar.Client/src/connection.ts: the part an app calls. */
export interface Connection {
  /**
   * Once the first view has rendered: removes the splash (after its minimum time once it has appeared), then the host
   * reports ui-ready. Later calls in the same document do nothing.
   */
  ready(): void;
}

/** The channel of the host the page runs in: the window's, or loopback's in a browser. */
export function hostChannel(): BridgeChannel {
  return { send: message => hostMessages.push(message), listen: () => undefined };
}

/** Opens the bridge for this document over the host's channel. */
export function connectSpar(channel: BridgeChannel, contracts: string): Connection {
  channel.send(JSON.stringify({ type: "hello", contracts }));
  return { ready: () => channel.send(JSON.stringify({ type: "ready" })) };
}

/** What `setColorScheme` can end with besides success, as in src/Spar.Client/src/colorScheme.ts. */
export interface ColorSchemeError {
  /** The window took the choice, but it could not be saved: the next start shows the earlier one. */
  readonly notSaved: undefined;
}

/** Sets the app's colour scheme: applied at once, and saved for the next start. */
export function setColorScheme(scheme: ColorScheme, options?: CallOptions): Call<void, ColorSchemeError> {
  return invoke<void, ColorSchemeError>("spar.setColorScheme", scheme, options);
}

/** The cases of a file dialog that gave no file. */
export interface FileDialogError {
  /** The user closed the dialog. */
  readonly dismissed: undefined;
  readonly accessDenied: { readonly path: string };
  /** No dialog can be shown: no file portal, or no display. */
  readonly unavailable: { readonly reason: string };
  /** A dialog is already open for this window. */
  readonly busy: undefined;
}

/** Why a granted file could not be read or written. */
export interface FileAccessError {
  readonly notFound: {};
  readonly accessDenied: {};
  readonly inUse: {};
  readonly tooLarge: { readonly limit: number };
  readonly grantExpired: {};
  /** The path leaves the granted folder. */
  readonly outsideFolder: { readonly path: string };
  readonly readOnlyGrant: {};
}

/** An entry of a granted folder: a relative name, never a full path. */
export interface FolderEntry { readonly name: string; readonly isFolder: boolean; readonly size: number; readonly modified: string; }

/** A folder the user picked; every path is relative to it. */
export interface PickedFolder {
  readonly name: string;
  /** Shows the folder in Explorer or the file manager. */
  reveal(options?: CallOptions): Call<void, ShellError>;
  list(relativePath?: string, options?: CallOptions): Call<readonly FolderEntry[], FileAccessError>;
}

export interface OpenFolderOptions extends CallOptions {
  readonly title?: string;
  readonly access?: "read" | "readWrite";
}

/** A file the user picked: its name and a grant; never its path. */
export interface PickedFile {
  readonly name: string;
  readText(options?: CallOptions): Call<string, FileAccessError>;
  /** Opens the file with its default program. Files that can run code are notAllowed unless C# opts in. */
  openWithDefaultApp(options?: CallOptions): Call<void, ShellError>;
}

/** Why something could not be opened outside the app. */
export interface ShellError {
  readonly notAllowed: { readonly reason: string };
  readonly noHandler: {};
  readonly failed: { readonly reason: string };
}

export interface OpenFileOptions extends CallOptions {
  readonly title?: string;
  readonly filters?: readonly { readonly name: string; readonly extensions: readonly string[] }[];
}

/** Built-in file dialogs, on the window's own thread. */
/** Why a drop gave no grants. */
export interface DropError {
  /** A dropped item does not match `accept`, or is a folder where folders are not accepted. */
  readonly notAccepted: { readonly name: string };
  readonly tooMany: { readonly limit: number };
}

/** What was dropped: grants, never paths. A drop grants nothing on the parent folder. */
export interface DroppedItems {
  readonly files: readonly PickedFile[];
  /** Read-only folder grants, when `folders: true`. */
  readonly folders: readonly PickedFolder[];
}

export interface DropTargetOptions {
  /** File extensions, for example `[".csv"]`. Empty accepts every file. */
  readonly accept?: readonly string[];
  readonly folders?: boolean;
  readonly limit?: number;
}

export const files: {
  open(options?: OpenFileOptions): Call<PickedFile, FileDialogError>;
  openFolder(options?: OpenFolderOptions): Call<PickedFolder, FileDialogError>;
  /** Makes an element a drop area for files from the OS. Returns a function that removes it. */
  dropTarget(element: Element, options: DropTargetOptions, onDrop: (result: Result<DroppedItems, DropError>) => void): () => void;
} = {
  open: options => invoke<PickedFile, FileDialogError>("spar.files.open", options ?? {}, options),
  openFolder: options => invoke<PickedFolder, FileDialogError>("spar.files.openFolder", options ?? {}, options),
  dropTarget: (element, _options, _onDrop) => {
    element.setAttribute("data-spar-drop", "ready");
    return () => element.removeAttribute("data-spar-drop");
  },
};

/** Why printing gave no output. */
export interface PrintError {
  readonly dismissed: undefined;
  readonly noPrinter: undefined;
  readonly unavailable: { readonly reason: string };
  readonly failed: { readonly reason: string };
}

export interface PdfOptions extends CallOptions {
  readonly paper?: "A4" | "Letter";
  readonly landscape?: boolean;
  readonly margins?: "default" | "none";
  readonly background?: boolean;
}

/** Printing of the current page; style it with CSS @media print. window.print() does the same as show(). */
export const print: {
  show(options?: CallOptions): Call<void, PrintError>;
  /** Asks where to save through the save dialog, then writes the PDF there safely. */
  toPdf(options?: PdfOptions): Call<PickedFile, PrintError & FileDialogError>;
} = {
  show: options => invoke<void, PrintError>("spar.print.show", {}, options),
  toPdf: options => invoke<PickedFile, PrintError & FileDialogError>("spar.print.toPdf", options ?? {}, options),
};

/** A shortcut: the same on every OS, or one per OS, with `default` for an OS not listed. */
export type Shortcut = string | { readonly windows?: string; readonly linux?: string; readonly default?: string };

/** What an action does, shown in menus with its shortcut. */
export interface ActionSpec {
  readonly label: string;
  /** For example "Ctrl+K", or { windows: "Ctrl+Y", linux: "Ctrl+Shift+Z" }. Plain editing keys never reach an action while the user types in an input. */
  readonly shortcut?: Shortcut;
  readonly run: () => void;
}

/** A defined action; menus reference it, never a string id. */
export interface Action {
  readonly id: string;
  readonly label: string;
  /** The shortcut that applies on this OS. */
  readonly shortcut?: string;
  enabled: boolean;
  checked?: boolean;
}

/** A menu item: an action, a separator, or an inline item. */
export type MenuItem = Action | "-" | { readonly label: string; readonly run: () => void };

const defined = new Map<string, Action>();

const onWindows = typeof navigator !== "undefined" && navigator.userAgent.includes("Windows");

function forThisOs(shortcut: Shortcut | undefined): string | undefined {
  if (shortcut === undefined || typeof shortcut === "string") return shortcut;
  return (onWindows ? shortcut.windows : shortcut.linux) ?? shortcut.default;
}

export const actions: {
  /** Declares the app's actions at once, so a duplicate id is TS1117 and a typo in a menu is a tsc error. A second define of an existing id throws. */
  define<const T extends Record<string, ActionSpec>>(specs: T): { readonly [K in keyof T]: Action };
  /** Actions of one view, active until the returned dispose runs; they may shadow app-wide shortcuts while active. */
  scoped<const T extends Record<string, ActionSpec>>(specs: T): { readonly actions: { readonly [K in keyof T]: Action }; dispose(): void };
} = {
  define: specs => {
    const result: Record<string, Action> = {};
    for (const [id, spec] of Object.entries(specs)) {
      if (defined.has(id)) throw new Error(`Action "${id}" is already defined`);
      const action: Action = { id, label: spec.label, enabled: true, ...(forThisOs(spec.shortcut) === undefined ? {} : { shortcut: forThisOs(spec.shortcut)! }) };
      defined.set(id, action);
      result[id] = action;
    }
    return result as never;
  },
  scoped: specs => {
    const result: Record<string, Action> = {};
    for (const [id, spec] of Object.entries(specs)) {
      result[id] = { id, label: spec.label, enabled: true, ...(forThisOs(spec.shortcut) === undefined ? {} : { shortcut: forThisOs(spec.shortcut)! }) };
    }
    return { actions: result as never, dispose: () => {} };
  },
};

export const menus: {
  /** Shows a native context menu at the pointer of `event`. */
  show(event: MouseEvent, items: readonly MenuItem[], options?: CallOptions): Call<void, { readonly dismissed: undefined }>;
} = {
  show: (_event, items, options) => invoke<void, { readonly dismissed: undefined }>("spar.menus.show", items.length, options),
};

export interface MessageOptions<B extends string> extends CallOptions {
  readonly title: string;
  readonly text: string;
  readonly buttons: readonly B[];
  readonly default?: NoInfer<B>;
}

/** Native message dialogs: a task dialog on Windows, GtkAlertDialog on Linux. alert/confirm/prompt are shown natively too. */
export const dialogs: {
  /** Resolves with the chosen button's name; closing the dialog is `dismissed`. */
  message<const B extends string>(options: MessageOptions<B>): Call<B, { readonly dismissed: undefined }>;
} = {
  message: options => invoke("spar.dialogs.message", { title: options.title, text: options.text }, options),
};

export const shell: {
  /** Opens a link in the system browser or mail program: http(s), mailto and tel by default, only after a user action. */
  openExternal(url: string, options?: CallOptions): Call<void, Pick<ShellError, "notAllowed" | "failed">>;
} = {
  openExternal: (url, options) => invoke("spar.shell.openExternal", url, options),
};

/** Taskbar progress and badge of this window. Where unsupported the calls do nothing. */
export const taskbar: {
  setProgress(value: number | "indeterminate" | "paused" | "error"): void;
  clearProgress(): void;
  setBadge(count: number | null): void;
  readonly isAvailable: boolean;
} = {
  setProgress: () => {},
  clearProgress: () => {},
  setBadge: () => {},
  isAvailable: false,
};

export type SystemEventName = "suspending" | "resumed" | "locked" | "unlocked" | "sessionEnding";

/** Power and session events. The page is notified; saving on session end happens in C#. */
export const system: {
  /** Returns a function that removes the handler. */
  on(event: SystemEventName, handler: () => void): () => void;
} = {
  on: () => () => {},
};

/** The page zoom of this window: on by default, saved with the window's placement. */
export const zoom: {
  get(): number;
  set(factor: number): void;
  /** Returns a function that removes the handler. */
  onChange(handler: (factor: number) => void): () => void;
} = {
  get: () => 1,
  set: () => {},
  onChange: () => () => {},
};

/** Find in page through the engine: WebView2's find API, WebKitFindController on Linux. */
export const find: {
  /** Highlights the matches and resolves with their count. */
  start(text: string, options?: { readonly matchCase?: boolean } & CallOptions): Call<number, { readonly unavailable: { readonly reason: string } }>;
  next(): void;
  previous(): void;
  stop(): void;
} = {
  start: (text, options) => invoke("spar.find.start", text, options),
  next: () => {},
  previous: () => {},
  stop: () => {},
};

/** Capture protection of this window: blank in screenshots and screen sharing where the OS allows it. Not a security boundary. */
export const capture: {
  /** Resolves with whether it took effect (false on Linux and older Windows). */
  protect(enabled: boolean): Promise<boolean>;
  readonly isAvailable: boolean;
} = {
  protect: async () => false,
  isAvailable: false,
};

/** Diagnostics for users: logs and crash records packed into one zip, saved through the save dialog. */
export const diagnostics: {
  collect(options?: CallOptions): Call<PickedFile, FileDialogError & FileAccessError>;
} = {
  collect: options => invoke("spar.diagnostics.collect", {}, options),
};

/** Stand-in: groups the calls `run` starts under one span when telemetry is on; otherwise only runs `run` (6.16). */
export function traced<T>(name: string, run: () => T): T {
  void name;
  return run();
}
