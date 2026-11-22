// The page's side of the Hot Reload signal (plan section 4, "Hot Reload signal"): in a Debug build, after each C# edit
// Hot Reload applied, the host sends `backend-updated` with the update's revision, its time and the updated types. The
// SDK hands it to every `onBackendUpdate` handler, then shows the development overlay's toast unless the app turned it
// off with `configureSpar({ backendUpdateToast: false })`. Everything here is reached only behind
// `import.meta.env?.DEV`, so a production bundle (`vite build`) carries none of it.
import type { Connection } from "./connection.js";
import { showBackendUpdateToast } from "./overlay.js";

/** One C# edit that Hot Reload applied to the running backend (Debug builds only). */
export interface BackendUpdate {
  /** The update's number in this run of the backend, from 1. A restart starts again from 1. */
  readonly revision: number;
  /** When the backend applied it, as an ISO 8601 date and time. */
  readonly time: string;
  /** The full names of the types the edit changed, such as `Notebook.Pages.PagesService`; empty when .NET named none. */
  readonly types: readonly string[];
}

/** The bridge event's name. */
export const backendUpdateEvent = "backend-updated";

const handlers = new Set<(update: BackendUpdate) => void>();
let toast = true;

/** @internal Registers a handler; returns the function that removes it. */
export function addBackendUpdateHandler(handler: (update: BackendUpdate) => void): () => void {
  if (typeof handler !== "function") {
    throw new TypeError("onBackendUpdate takes a function, called with each backend update.");
  }

  // A wrapper of its own, so the same function added twice runs twice and each unsubscribe removes one.
  const registered = (update: BackendUpdate): void => handler(update);
  handlers.add(registered);
  return () => {
    handlers.delete(registered);
  };
}

/** @internal `configureSpar({ backendUpdateToast })`: whether the development overlay shows a toast per update. */
export function setBackendUpdateToast(enabled: boolean): void {
  toast = enabled;
}

/** @internal Listens on a new connection for the host's event. */
export function listenForBackendUpdates(connection: Connection): void {
  connection.onEvent(backendUpdateEvent, receiveBackendUpdate);
}

/** @internal One event's payload: the handlers in the order they were added, then the toast. */
export function receiveBackendUpdate(payload: string): void {
  const update = readBackendUpdate(payload);
  if (update === undefined) {
    console.warn("Spar: a backend-updated event that could not be read was ignored.");
    return;
  }

  for (const handler of [...handlers]) {
    try {
      handler(update);
    } catch (error) {
      console.error("Spar: an onBackendUpdate handler threw", error);
    }
  }

  if (toast) {
    showBackendUpdateToast(update);
  }
}

/**
 * The payload `{"revision":3,"time":"…","types":["…"]}`, read as untrusted: own properties only, each of its type,
 * built field by field. Undefined when any is missing or wrong.
 */
export function readBackendUpdate(payload: string): BackendUpdate | undefined {
  let parsed: unknown;
  try {
    parsed = JSON.parse(payload);
  } catch {
    return undefined;
  }

  if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) {
    return undefined;
  }

  const revision = ownField(parsed, "revision");
  const time = ownField(parsed, "time");
  const types = ownField(parsed, "types");
  if (!Number.isSafeInteger(revision) || typeof time !== "string" || !Array.isArray(types)) {
    return undefined;
  }

  const names: string[] = [];
  for (const name of types as unknown[]) {
    if (typeof name !== "string") {
      return undefined;
    }

    names.push(name);
  }

  return { revision: revision as number, time, types: names };
}

function ownField(object: object, key: string): unknown {
  return Object.prototype.hasOwnProperty.call(object, key) ? (object as Record<string, unknown>)[key] : undefined;
}
