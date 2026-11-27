// @spar/app/sdk: the SDK's public surface for app code, and the one entry generated modules call.
import { addBackendUpdateHandler, listenForBackendUpdates, type BackendUpdate } from "./backendUpdate.js";
import { Connection, type BridgeChannel, type Decoder } from "./connection.js";
import { callOf, resumes, Result, type Call, type CallOptions, type ErrorCases, type EventCases, type Outcome, type SubscribeOptions } from "./results.js";
import { openStream, openSubscription, type StreamDecoder } from "./streams.js";
import { watchFrames } from "./splash.js";
import { startPageTelemetry } from "./telemetryBoot.js";

export {
  configureSpar,
  Result,
  SparError,
  type ApplicationError,
  type Call,
  type CallError,
  type CallErrorKind,
  type CallName,
  type CallOptions,
  type ErrorCases,
  type ErrorHandlers,
  type Execution,
  type Handlers,
  type EventCases,
  type LatestOnlyEvents,
  type SubscribeOptions,
  type Subscription,
  type SubscriptionEvents,
  type SparConfiguration,
} from "./results.js";
export type { BackendUpdate } from "./backendUpdate.js";
export { unknownErrorCase, type BridgeChannel, type Decoder } from "./connection.js";
export type { StreamDecoder } from "./streams.js";
export { loopbackChannel, takeToken, type LoopbackOptions, type TokenPage } from "./loopback.js";
export { hostChannel, webViewChannel, webViewPort, type WebViewOptions, type WebViewPort } from "./webview.js";
export { isWebKitHost, webKitChannel, type RelayTarget, type WebKitOptions } from "./webkit.js";
export { setColorScheme, type ColorScheme, type ColorSchemeError } from "./colorScheme.js";
export { traced } from "./tracing.js";
export { readBlob, resourceUrl, type SparBlob } from "./blobs.js";
export {
  files,
  type DropError,
  type DroppedItems,
  type DropTargetOptions,
  type FileAccess,
  type FileAccessError,
  type FileDialogError,
  type FileFilter,
  type FolderEntry,
  type OpenFileOptions,
  type OpenFolderOptions,
  type PickedFile,
  type PickedFolder,
  type SaveFileOptions,
} from "./files.js";

let current: Connection | undefined;

/** Opens the bridge for this document over the host's channel; the host's start-up code calls it once. */
export function connectSpar(channel: BridgeChannel, contracts: string): Connection {
  // At the bundle's start, before the app's own start-up work: the splash counts its minimum visible time from frames.
  watchFrames();
  current = new Connection(channel, contracts);
  // Development only. `vite build` folds the test to false and drops what it guards; written with `?.` because a page
  // can load the SDK without Vite (a test page served as it is), where `import.meta.env` does not exist.
  if (import.meta.env?.DEV === true) {
    listenForBackendUpdates(current);
  }

  // Only when `Program.cs` turned telemetry on: the Vite plugin defines the flag, and `vite build` drops the start and
  // its lazy chunk otherwise. It runs alongside the app's start and never delays it.
  if (import.meta.env?.SPAR_TELEMETRY === true) {
    void startPageTelemetry(current, channel);
  }

  return current;
}

/**
 * Debug builds only: calls `handler` after each C# edit Hot Reload applied to the running backend, for example to
 * fetch the current view's data again. Returns the function that stops the calls. The update can arrive after the
 * patched code already ran once; edits Hot Reload rejected stay in `dotnet watch`'s terminal, and contract edits
 * restart the backend instead. In a production build (`vite build`) this does nothing, and the bundler removes it.
 *
 * ```ts
 * useEffect(() => onBackendUpdate(() => void refresh()), [refresh]);
 * ```
 */
export function onBackendUpdate(handler: (update: BackendUpdate) => void): () => void {
  if (import.meta.env?.DEV !== true) {
    return () => {};
  }

  return addBackendUpdateHandler(handler);
}

/**
 * @internal Called by generated contract modules: one command, the encoder of its request, and its decoders. A request
 * the encoder refuses (NaN, a `long` out of range) never leaves: the call ends `rejected` / `invalid-request`.
 */
export function invoke<T, E extends ErrorCases>(
  contract: string,
  command: string,
  encode: () => string,
  decoder: Decoder<T>,
  options?: CallOptions,
  files?: readonly File[],
): Call<T, E> {
  if (current === undefined) {
    throw new Error("Spar is not connected: the host's start-up script has not run in this document.");
  }

  let request: string;
  try {
    request = encode();
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error);
    console.error(`Spar: ${contract}.${command} was not sent: ${reason}`);
    const refused: Outcome = {
      ok: false,
      error: { kind: "rejected", code: "invalid-request", execution: "notStarted", contract, command },
    };
    return callOf<T, E>(Promise.resolve(refused));
  }

  return callOf<T, E>(current.invoke(contract, command, request, decoder as Decoder<unknown>, options, files));
}

/**
 * @internal The page's drop areas (files.ts): a call that carries the files of a drop to a channel that takes them
 * (WebView2), and goes as an ordinary call elsewhere.
 */
export function invokeWithFiles<T, E extends ErrorCases>(
  contract: string,
  command: string,
  encode: () => string,
  decoder: Decoder<T>,
  files: readonly File[],
): Call<T, E> {
  return invoke<T, E>(contract, command, encode, decoder, undefined, files);
}

/**
 * @internal Called by generated contract modules for a stream command. A request the encoder refuses never leaves: the
 * stream's one item is the failure `rejected` / `invalid-request`.
 */
export function stream<T, E extends ErrorCases>(
  contract: string,
  command: string,
  encode: () => string,
  decoder: StreamDecoder<T>,
  options?: CallOptions,
): AsyncIterable<Result<T, E>> {
  const request = encodeStreamRequest(contract, command, encode);
  if (request === undefined) {
    return refusedStream<T, E, {}>(contract, command);
  }

  return openStream<T, E>(connected(), { contract, command }, request, decoder, options?.signal);
}

/**
 * @internal Called by generated contract modules for a subscription: `latestOnly` and `automatic` come from its
 * `[SparSubscription]`, and the page's options and configuration may only make it more cautious.
 */
export function subscribe<T, E extends ErrorCases, V extends EventCases>(
  contract: string,
  command: string,
  encode: () => string,
  decoder: StreamDecoder<T>,
  latestOnly: boolean,
  automatic: boolean,
  options?: SubscribeOptions<"automatic" | "manual">,
): AsyncIterable<Result<T, E, V>> {
  const request = encodeStreamRequest(contract, command, encode);
  if (request === undefined) {
    return refusedStream<T, E, V>(contract, command);
  }

  const kind = { latestOnly, resumes: resumes(automatic, options) };
  return openSubscription<T, E, V>(connected(), { contract, command }, request, decoder, options?.signal, kind);
}

/** @internal The key blob and resource addresses carry on this document's channel; undefined on the desktop hosts. */
export function pageAddressKey(): string | undefined {
  return current?.addressKey;
}

function connected(): Connection {
  if (current === undefined) {
    throw new Error("Spar is not connected: the host's start-up script has not run in this document.");
  }

  return current;
}

function encodeStreamRequest(contract: string, command: string, encode: () => string): string | undefined {
  connected();
  try {
    return encode();
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error);
    console.error(`Spar: ${contract}.${command} was not sent: ${reason}`);
    return undefined;
  }
}

/** A stream whose request never left: its one item is the refusal. */
function refusedStream<T, E extends ErrorCases, V extends EventCases>(contract: string, command: string): AsyncIterable<Result<T, E, V>> {
  const refused: Outcome = {
    ok: false,
    error: { kind: "rejected", code: "invalid-request", execution: "notStarted", contract, command },
  };
  const items = [new Result<T, E, V>(refused)];
  return {
    [Symbol.asyncIterator]: () => {
      let next = 0;
      return {
        next: () => Promise.resolve(next < items.length ? { done: false, value: items[next++]! } : { done: true, value: undefined }),
      };
    },
  };
}
