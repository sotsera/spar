// The engine of @spar/app/windows: the page's side of its window. It opens windows of the kinds Program.cs declares
// (decision "Secondary and modal windows"; docs/app-model.md 6.7) through the host's own commands under `spar`, shows
// modals and waits for their result, closes its own window with a value for its opener, and reads the params its opener
// gave. Its close guard (decision "Two-stage close"; docs/app-model.md 6.11) is asked before the backend's when the
// window is asked to close, and answers close, keep, or ask, which Spar shows as one native confirmation owned by the
// window. The connection runs the guards when the host sends `close-request` and answers `close-answer`. The generated
// module types the kinds as the app's `WindowKind` and `ModalKind` unions.
import { unknownErrorCase, type Decoder } from "./connection.js";
import type { Call, CallOptions } from "./results.js";
import { invoke } from "./sdk.js";

/** A request to close this window. */
export interface CloseRequest {
  /** The close button, Alt+F4 or the taskbar (`user`); the app's own code or the tray's Quit (`app`); Windows ending the session (`sessionEnd`). */
  readonly reason: "user" | "app" | "sessionEnd";
  /** Tests or an agent are closing the app: do not wait for a person; `ask` counts as close. */
  readonly automated: boolean;
  /** Aborted when the request ends without this guard: a deadline at session end, or the window closing anyway. */
  readonly signal: AbortSignal;
}

/** A close guard's answer: `ask` shows the question in a native confirmation owned by the window. */
export type CloseAnswer = "close" | "keep" | { readonly ask: string };

/** A close guard: it may save, keep the window open, or have Spar ask the user. */
export type CloseGuard = (request: CloseRequest) => CloseAnswer | Promise<CloseAnswer>;

/** @internal What the page's guards came to: `failed` when one threw or answered something that is not an answer. */
export type GuardsOutcome = CloseAnswer | "failed";

interface Registration {
  readonly guard: CloseGuard;
}

const registrations: Registration[] = [];

/** Why `windows.open` opened no window. */
export interface OpenError {
  /**
   * No window was opened: a browser on loopback (development, browser tests) opens none of the app's windows, the app
   * is closing, or the host could not open it; `reason` says which.
   */
  readonly notOpened: { readonly reason: string };
}

/** How a modal ended without a value, or why it never opened. */
export interface ModalError extends OpenError {
  /**
   * The modal closed without `closeWith`: its close button, Alt+F4, or the app closing. Not `cancelled`, which is the
   * `CallError` of an aborted call.
   */
  readonly dismissed: undefined;
}

/** What a window's opener hands it: JSON, at most 64 KB. Keep larger data in the backend and pass its id. */
export type WindowParams = Readonly<Record<string, unknown>>;

/**
 * This window and the app's other windows, as the page sees them; `K` is the app's `WindowKind` union, and `M` its
 * `ModalKind` union.
 */
export interface WindowsApi<K extends string, M extends string = never> {
  /**
   * Opens a window of a kind `Program.cs` declares, showing its route with `params`; a single-instance kind that is open
   * is focused instead. Succeeds once the window is shown.
   */
  open(kind: K, params?: WindowParams, options?: CallOptions): Call<void, OpenError>;
  /**
   * Shows a modal of a kind `Program.cs` declares with `Modal()`, owned by this window, which takes no input until it
   * closes, and waits until it does: the value its page gave `closeWith`, typed by this page, or `dismissed`. A
   * single-instance modal that is open already is brought to the front and gives `notOpened`. Aborting the call ends
   * only the wait: the modal stays open, and its result is dropped.
   */
  showModal<T = unknown>(kind: M, params?: WindowParams, options?: CallOptions): Call<T, ModalError>;
  /**
   * Closes this window at once, without asking its close guards, and hands `value` (JSON, at most 64 KB) to the page
   * that showed it, when it is a modal. A failure, such as a value too large or the main window, goes to
   * `configureSpar`'s `onFailure`, and the window stays open.
   */
  closeWith(value?: unknown): void;
  /** Brings this window to the front, restoring it when it is minimized; the OS may only flash it. */
  focus(options?: CallOptions): Call<void>;
  minimize(options?: CallOptions): Call<void>;
  maximize(options?: CallOptions): Call<void>;
  /** A minimized window back to its place, or a maximized one to its normal size. */
  restore(options?: CallOptions): Call<void>;
  /** The size of this page, in CSS pixels at a zoom of 1 (whole numbers from 1 to 100,000), kept within the window's minimum. */
  setSize(width: number, height: number, options?: CallOptions): Call<void>;
  /** This window's title, as the taskbar and the window switcher show it; at most 1,024 characters. */
  setTitle(title: string, options?: CallOptions): Call<void>;
  /**
   * Asks this window to close, as the close button does but as the app's own code: its own modals first, then its
   * close guards, then the backend's. A failure goes to `configureSpar`'s `onFailure`.
   */
  close(): void;
  /** The params this window's opener gave, typed by the page; null for the main window or a window opened without any. */
  params<T extends WindowParams = WindowParams>(options?: CallOptions): Call<T | null>;
  /**
   * This window's close guard, asked before the backend's: it may save, keep the window open, or have Spar ask the
   * user. Returns a function that removes it.
   */
  onCloseRequested(guard: CloseGuard): () => void;
}

const openDecoder: Decoder<void> = {
  value: () => undefined,
  error: (code, payload) =>
    code === "notOpened" && typeof payload["reason"] === "string" ? { reason: payload["reason"] } : unknownErrorCase,
};

const modalDecoder: Decoder<unknown> = {
  value: json => (json === "" ? null : JSON.parse(json)),
  error: (code, payload) => {
    if (code === "dismissed") {
      return undefined;
    }

    return openDecoder.error(code, payload);
  },
};

const closeDecoder: Decoder<void> = {
  value: () => undefined,
  error: () => unknownErrorCase,
};

const paramsDecoder: Decoder<WindowParams | null> = {
  value: json => readParams(json),
  error: () => unknownErrorCase,
};

/** The params' JSON as an object the page owns, or null; anything but an object or null is a protocol error. */
function readParams(json: string): WindowParams | null {
  const parsed: unknown = JSON.parse(json);
  if (parsed === null) {
    return null;
  }

  if (typeof parsed !== "object" || Array.isArray(parsed)) {
    throw new TypeError("A window's params are a JSON object.");
  }

  return parsed as WindowParams;
}

/** The request of `open`: the kind and its params; a value JSON cannot carry fails here, before anything is sent. */
function openRequest(kind: string, params: WindowParams | undefined): string {
  if (typeof kind !== "string" || kind.length === 0) {
    throw new TypeError("windows.open and windows.showModal take the name of a window kind that Program.cs declares.");
  }

  if (params !== undefined && (params === null || typeof params !== "object" || Array.isArray(params))) {
    throw new TypeError("A window's params are an object, such as { tab: \"connections\" }.");
  }

  return JSON.stringify({ kind, params: params ?? null });
}

/** The request of `closeWith`: the value, which must be JSON; one JSON cannot carry fails here, before anything is sent. */
function closeRequest(value: unknown): string {
  const json = JSON.stringify(value === undefined ? null : value);
  if (json === undefined) {
    throw new TypeError("windows.closeWith takes a value JSON can carry, such as { changed: true }.");
  }

  return `{"value":${json}}`;
}

/** One of this window's own operations, through the host's `spar.windowAction`; a request the page refuses never leaves. */
function windowAction(request: () => Readonly<Record<string, string | number>>, options?: CallOptions): Call<void> {
  return invoke<void, Record<never, never>>("spar", "windowAction", () => JSON.stringify(request()), closeDecoder, options);
}

/** A size the host takes: a whole number of CSS pixels from 1 to 100,000. */
function wholeSize(value: number, name: string): number {
  if (!Number.isInteger(value) || value < 1 || value > 100_000) {
    throw new TypeError(`windows.setSize takes a ${name} that is a whole number from 1 to 100,000.`);
  }

  return value;
}

/** A title the host takes: a string of at most 1,024 characters. */
function titleOf(title: string): string {
  if (typeof title !== "string" || title.length > 1_024) {
    throw new TypeError("windows.setTitle takes a string of at most 1,024 characters.");
  }

  return title;
}

/** This window, as the page sees it. */
export const windows: WindowsApi<string, string> = {
  open(kind: string, params?: WindowParams, options?: CallOptions): Call<void, OpenError> {
    return invoke<void, OpenError>("spar", "openWindow", () => openRequest(kind, params), openDecoder, options);
  },

  showModal<T = unknown>(kind: string, params?: WindowParams, options?: CallOptions): Call<T, ModalError> {
    return invoke<unknown, ModalError>("spar", "showModal", () => openRequest(kind, params), modalDecoder, options) as Call<T, ModalError>;
  },

  closeWith(value?: unknown): void {
    // No handler for failures: they reach configureSpar's onFailure, as an unhandled call's do.
    void invoke<void, Record<never, never>>("spar", "closeWindow", () => closeRequest(value), closeDecoder).match({
      success: () => undefined,
    });
  },

  focus: options => windowAction(() => ({ action: "focus" }), options),
  minimize: options => windowAction(() => ({ action: "minimize" }), options),
  maximize: options => windowAction(() => ({ action: "maximize" }), options),
  restore: options => windowAction(() => ({ action: "restore" }), options),

  setSize(width: number, height: number, options?: CallOptions): Call<void> {
    const size = () => ({ action: "setSize", width: wholeSize(width, "width"), height: wholeSize(height, "height") });
    return windowAction(size, options);
  },

  setTitle(title: string, options?: CallOptions): Call<void> {
    return windowAction(() => ({ action: "setTitle", title: titleOf(title) }), options);
  },

  close(): void {
    void windowAction(() => ({ action: "close" })).match({ success: () => undefined });
  },

  params<T extends WindowParams = WindowParams>(options?: CallOptions): Call<T | null> {
    return invoke<WindowParams | null, Record<never, never>>("spar", "windowParams", () => "", paramsDecoder, options) as Call<T | null>;
  },

  /**
   * This window's close guard, asked before the backend's: it may save, keep the window open, or have Spar ask the
   * user. Guards run one after another in the order they were added, until one keeps the window or asks; the
   * window closes when every one answers close. Returns a function that removes this guard.
   */
  onCloseRequested(guard: CloseGuard): () => void {
    if (typeof guard !== "function") {
      throw new TypeError("windows.onCloseRequested takes a function that answers \"close\", \"keep\" or { ask }.");
    }

    const registration: Registration = { guard };
    registrations.push(registration);
    return () => {
      const index = registrations.indexOf(registration);
      if (index >= 0) {
        registrations.splice(index, 1);
      }
    };
  },
};

/**
 * @internal Runs the guards for one close request. A guard that throws, or answers anything but `"close"`, `"keep"`
 * or `{ ask: "…" }`, makes the outcome `failed`, with a console error: Spar then asks whether to close anyway, as for a
 * backend guard that failed.
 */
export async function runCloseGuards(reason: CloseRequest["reason"], automated: boolean, signal: AbortSignal): Promise<GuardsOutcome> {
  const request: CloseRequest = Object.freeze({ reason, automated, signal });
  for (const { guard } of [...registrations]) {
    let answer: unknown;
    try {
      answer = await guard(request);
    } catch (error) {
      console.error("Spar: a close guard threw; the window asks whether to close anyway.", error);
      return "failed";
    }

    if (answer === "close") {
      continue;
    }

    if (answer === "keep") {
      return "keep";
    }

    if (isAsk(answer)) {
      return { ask: answer.ask };
    }

    console.error("Spar: a close guard answered something other than \"close\", \"keep\" or { ask }.", answer);
    return "failed";
  }

  return "close";
}

function isAsk(answer: unknown): answer is { readonly ask: string } {
  if (typeof answer !== "object" || answer === null) {
    return false;
  }

  const ask: unknown = (answer as { readonly ask?: unknown }).ask;
  return typeof ask === "string" && ask.trim() !== "";
}
