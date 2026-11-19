// @spar/app/sdk: the SDK's public surface for app code, and the one entry generated modules call.
import { Connection, type BridgeChannel, type Decoder } from "./connection.js";
import { callOf, type Call, type CallOptions, type ErrorCases, type Outcome } from "./results.js";

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
} from "./results.js";
export { unknownErrorCase, type BridgeChannel, type Decoder } from "./connection.js";
export { loopbackChannel, takeToken, type LoopbackOptions, type TokenPage } from "./loopback.js";
export { hostChannel, webViewChannel, webViewPort, type WebViewOptions, type WebViewPort } from "./webview.js";
export { isWebKitHost, webKitChannel, type RelayTarget, type WebKitOptions } from "./webkit.js";

let current: Connection | undefined;

/** Opens the bridge for this document over the host's channel; the host's start-up code calls it once. */
export function connectSpar(channel: BridgeChannel, contracts: string): Connection {
  current = new Connection(channel, contracts);
  return current;
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

  return callOf<T, E>(current.invoke(contract, command, request, decoder as Decoder<unknown>, options));
}
