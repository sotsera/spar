// Connects this page to the app's backend. On the loopback host (browser tests now, the development loop later) the
// channel is the loopback one; the desktop hosts hand the page their own channel when they arrive.
import { configureSpar, connectSpar, loopbackChannel, type CallError } from "@spar/app/sdk";
import { fingerprint } from "@spar/app/spar";

export function startSpar(onFailure: (error: CallError) => void): { ready(): void } {
  configureSpar({ onFailure });
  return connectSpar(loopbackChannel(), fingerprint);
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
