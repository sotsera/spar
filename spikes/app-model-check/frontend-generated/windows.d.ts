// Not from docs/app-model.md. Stand-in for what the generator emits from the window kinds declared in Program.cs.
import type { Call, CallOptions } from "@spar/app/sdk";

/** The window kinds declared in C# with spar.Window(name, …). */
export type WindowKind = "settings" | "queryEditor";

export declare const windows: {
  /** Opens a window of a kind; a single-instance kind focuses the open one. */
  open(kind: WindowKind, params?: Readonly<Record<string, unknown>>, options?: CallOptions): Call<void, { readonly notOpened: { readonly reason: string } }>;
  /** Opens a modal window and waits until it closes. Its close button gives dismissed. */
  showModal<T = unknown>(kind: WindowKind, params?: Readonly<Record<string, unknown>>): Call<T, { readonly dismissed: {} }>;
  /** Inside a modal: closes it and hands the value to the opener. */
  closeWith(value: unknown): void;
};
