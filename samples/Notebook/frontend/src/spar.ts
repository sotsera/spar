// Connects this page to the app's backend: through WebView2 in the app's window, and through the loopback host in
// browser tests (and the development loop later).
import { configureSpar, connectSpar, hostChannel, type CallError } from "@spar/app/sdk";
import { fingerprint } from "@spar/app/spar";

/**
 * Connects the page. A call that failed for a reason no handler took is shown to the person through `onFailure`, and
 * written with console.error, which reaches the app's log in its window: so a failure only the page saw (a lost
 * connection, a timeout) is in the log a user sends with a support request.
 */
export function startSpar(onFailure: (error: CallError) => void): { ready(): void } {
  configureSpar({
    onFailure: error => {
      const call = `${error.contract}.${error.command}`;
      console.error(`Notebook: ${call} failed: ${error.kind} / ${error.code} (${error.execution})`);
      onFailure(error);
    },
  });
  return connectSpar(hostChannel(), fingerprint);
}

/** What a person reads when a call failed for a reason the page did not plan for. */
export function describeFailure(error: CallError): string {
  switch (error.kind) {
    case "unavailable":
    case "transport":
      return error.execution === "notStarted"
        ? "The notebook is not reachable right now. Nothing was changed."
        : "The connection to the notebook was lost. Your last change may not have been saved.";
    case "cancelled":
      return "The action was cancelled.";
    default:
      return `Something went wrong (${error.code}). Your notes are safe on disk.`;
  }
}
