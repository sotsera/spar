// The engine of @spar/app/windows: the page's side of its window. Its close guard (decision "Two-stage close";
// docs/app-model.md 6.11) is asked before the backend's when the window is asked to close, and answers close, keep, or
// ask, which Spar shows as one native confirmation owned by the window. The connection runs the guards when the host
// sends `close-request` and answers `close-answer`.

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

/** This window, as the page sees it. */
export const windows = {
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
