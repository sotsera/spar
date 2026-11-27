// The page's zoom (docs/app-model.md 6.8): the factor the host reports, read without a call, the host's own
// `spar.setZoom` to change it, and the handlers of its changes. The host zooms on Ctrl+wheel and Ctrl+plus, minus and
// 0, keeps the factor within the limits `Program.cs` sets, remembers it with the window's placement, and sends the
// event `zoom` with `{ factor }` when the document opens zoomed and at each change.
import { unknownErrorCase, type Connection, type Decoder } from "./connection.js";
import type { Call, CallOptions } from "./results.js";
import { invoke } from "./sdk.js";

/** The page's zoom, as the window has it: 1 is the page's own size. */
export interface ZoomApi {
  /** The factor now: 1 until the host has told a zoomed page its factor, just after it connects. */
  get(): number;
  /**
   * Sets the factor, which the host keeps within the window's limits (0.25 to 5 at most); refused where the window
   * does not zoom. The new factor reaches `get` and the `onChange` handlers once the host applied it.
   */
  set(factor: number, options?: CallOptions): Call<void>;
  /** Calls `handler` with each new factor, whoever changed it; returns a function that removes it. */
  onChange(handler: (factor: number) => void): () => void;
}

const zoomEvent = "zoom";

let factor = 1;

const handlers = new Set<(factor: number) => void>();

const setDecoder: Decoder<void> = {
  value: () => undefined,
  error: () => unknownErrorCase,
};

/** The request of `set`: a finite positive factor; one that is not never leaves the page. */
function setRequest(value: number): string {
  if (typeof value !== "number" || !Number.isFinite(value) || value <= 0) {
    throw new TypeError("zoom.set takes a positive number, such as 1.25 for 125 %.");
  }

  return JSON.stringify({ factor: value });
}

/** The page's zoom. */
export const zoom: ZoomApi = {
  get: () => factor,

  set(value: number, options?: CallOptions): Call<void> {
    return invoke<void, Record<never, never>>("spar", "setZoom", () => setRequest(value), setDecoder, options);
  },

  onChange(handler: (factor: number) => void): () => void {
    if (typeof handler !== "function") {
      throw new TypeError("zoom.onChange takes a function, called with each new factor.");
    }

    // A wrapper of its own, so the same function added twice runs twice and each unsubscribe removes one.
    const registered = (next: number): void => handler(next);
    handlers.add(registered);
    return () => {
      handlers.delete(registered);
    };
  },
};

/** @internal Listens on a new connection for the host's zoom; the new document starts from 1 until told otherwise. */
export function listenForZoom(connection: Connection): void {
  factor = 1;
  connection.onEvent(zoomEvent, receiveZoom);
}

/** @internal One `zoom` event: `{ factor }`; anything else is ignored with a warning. */
export function receiveZoom(payload: string): void {
  let next: unknown;
  try {
    next = (JSON.parse(payload) as { readonly factor?: unknown }).factor;
  } catch {
    next = undefined;
  }

  if (typeof next !== "number" || !Number.isFinite(next) || next <= 0) {
    console.warn("Spar: a zoom event that could not be read was ignored.");
    return;
  }

  if (next === factor) {
    return;
  }

  factor = next;
  for (const handler of [...handlers]) {
    try {
      handler(next);
    } catch (error) {
      console.error("Spar: a zoom.onChange handler threw", error);
    }
  }
}
