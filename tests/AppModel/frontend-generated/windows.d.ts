// Not from docs/app-model.md. Stand-in for what the generator emits from the window kinds declared in Program.cs.
import type { Call, CallOptions } from "@spar/app/sdk";

/** The window kinds declared in C# with spar.Window(name, …) that windows.open opens. */
export type WindowKind = "queryEditor";

/** The window kinds declared with Modal(), which windows.showModal shows. */
export type ModalKind = "settings";

export declare const windows: {
  /** Opens a window of a kind; a single-instance kind focuses the open one. */
  open(kind: WindowKind, params?: Readonly<Record<string, unknown>>, options?: CallOptions): Call<void, { readonly notOpened: { readonly reason: string } }>;
  /** The params this window's opener gave, typed by the page; null for the main window or a window opened without any. */
  params<T extends Readonly<Record<string, unknown>> = Readonly<Record<string, unknown>>>(options?: CallOptions): Call<T | null>;
  /** Shows a modal and waits until it closes: closeWith's value, or dismissed; notOpened when it could not open. */
  showModal<T = unknown>(kind: ModalKind, params?: Readonly<Record<string, unknown>>, options?: CallOptions): Call<T, { readonly dismissed: undefined; readonly notOpened: { readonly reason: string } }>;
  /** Closes this window without its guards and hands the value to a modal's opener. */
  closeWith(value?: unknown): void;
  /**
   * This window's close guard, asked before the backend's: it may save, keep the window open, or have Spar ask the
   * user. Returns a function that removes it.
   */
  onCloseRequested(guard: (request: CloseRequest) => CloseAnswer | Promise<CloseAnswer>): () => void;
};

/** A request to close this window. */
export interface CloseRequest {
  readonly reason: "user" | "app" | "sessionEnd";
  /** Tests or an agent are closing the app: do not wait for a person; `ask` counts as close. */
  readonly automated: boolean;
  /** Aborted when the request ends without this guard: a deadline at session end, or the window closing anyway. */
  readonly signal: AbortSignal;
}

/** A close guard's answer: `ask` shows the question in a native confirmation owned by the window. */
export type CloseAnswer = "close" | "keep" | { readonly ask: string };
