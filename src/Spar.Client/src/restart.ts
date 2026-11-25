// A loopback dev session's restarts (plan section 4; decision "M1d step 2's shape: loopback in the development loop"):
// when the backend stops, the open page waits for its next run at the same address and token, then reloads by itself,
// once. The calls that were in flight have already ended as `transport` / `lost`, execution unknown (connection.ts):
// nothing is replayed, and the reloaded page starts afresh. A reload counts against the loop guard only when the
// reloaded document then gets no backend at all (no `open`), never when a newer run answers it (decision "Step 3's two
// follow-ups"): `dotnet watch` may restart the backend every few seconds, and each new run is worth a reload.

/** The header of a dev session's channel that names the backend's run; the test host sends none. */
export const runHeader = "X-Spar-Run";

/** How many automatic reloads in a row may end with no backend (no `open`) before the page stops reloading. */
const maxFailedReloads = 3;

/** How long one look at the backend waits; a backend that is starting may take a moment to answer. */
const probeTimeoutMs = 3_000;

/** After waiting this long, the page looks less often. */
const patientAfterMs = 60_000;

/** The automatic reloads in a row whose document got no backend. */
const reloadsKey = "spar.restart.reloads";

/** The run the page last reloaded for: set by an automatic reload, removed when its document gets `open`. */
const reloadedForKey = "spar.restart.run";

/**
 * What happened: the page reloaded, why it stopped waiting, or `none` when there was nothing to wait for (a channel
 * that names no run, as the test host's; a document that never got its backend and that no automatic reload made).
 */
export type RestartOutcome = "reloaded" | "same-run" | "refused" | "foreign" | "too-many" | "none";

/** What waiting needs from the page; the browser's own objects by default. */
export interface RestartPage {
  readonly reload: () => void;
  readonly sessionStorage: Pick<Storage, "getItem" | "setItem" | "removeItem">;
  readonly now: () => number;
  readonly wait: (ms: number) => Promise<void>;
  readonly fetch: typeof fetch;
  readonly log: Pick<Console, "info" | "warn" | "error">;
}

/** A document's channel that ended: its backend, the run the channel named, and whether the document got `open`. */
export interface LostBackend {
  readonly origin: string;
  readonly token: string;
  /** The run named by the channel's `X-Spar-Run` header; undefined when the channel never started or names none. */
  readonly run: string | undefined;
  /** Whether the document received `open`, that is whether it got its backend. */
  readonly opened: boolean;
}

/**
 * The document got its backend (`open`): the automatic reload that made it, if any, succeeded, so the count of reloads
 * that found no backend starts again.
 */
export function backendOpened(page: Pick<RestartPage, "sessionStorage"> = browserPage()): void {
  if (page.sessionStorage.getItem(reloadedForKey) !== null) {
    page.sessionStorage.removeItem(reloadedForKey);
    writeReloads(page.sessionStorage, 0);
  }
}

/**
 * Waits for the backend's next run, then reloads the page once. It looks at `POST /_spar/session` with the page's
 * token, more slowly as time passes, and stops without reloading when the backend still runs the same run (the
 * channel ended for another reason), refuses the token (a new dev session: the backend printed a new address), or is
 * not Spar's. A document that got its backend always waits. One that never did waits only when an automatic reload made
 * it, and that reload counts: after `maxFailedReloads` in a row the page stops, so a backend that keeps failing before
 * the page connects never makes it reload in a loop.
 */
export async function awaitRestart(lost: LostBackend, page: RestartPage = browserPage()): Promise<RestartOutcome> {
  const run = lost.opened ? lost.run : failedReload(lost, page);
  if (run === undefined || run === "too-many") {
    return run ?? "none";
  }

  page.log.info(`Spar: the backend at ${lost.origin} stopped; the page reloads when it runs again.`);
  const started = page.now();
  for (let attempt = 0; ; attempt++) {
    await page.wait(delayFor(attempt, page.now() - started));
    const answer = await probe(lost, page.fetch);
    if (answer.kind === "down") {
      continue;
    }

    if (answer.kind === "run" && answer.run !== run) {
      page.sessionStorage.setItem(reloadedForKey, answer.run);
      page.reload();
      return "reloaded";
    }

    return stopWaiting(answer.kind === "run" ? "same-run" : answer.kind, lost, page);
  }
}

/**
 * A document that never got its backend: when an automatic reload made it, that reload counts, and the run to wait
 * past is the channel's or the one the page reloaded for; `too-many` after `maxFailedReloads` in a row; undefined when
 * no automatic reload made it (nothing to wait for).
 */
function failedReload(lost: LostBackend, page: RestartPage): string | "too-many" | undefined {
  const reloadedFor = page.sessionStorage.getItem(reloadedForKey);
  if (reloadedFor === null) {
    return undefined;
  }

  const failed = readReloads(page.sessionStorage) + 1;
  if (failed >= maxFailedReloads) {
    page.log.error(`Spar: the backend at ${lost.origin} stopped before the page could connect, ${failed} times in a row, so the page no longer reloads by itself. Reload it once the backend is fixed.`);
    page.sessionStorage.removeItem(reloadedForKey);
    writeReloads(page.sessionStorage, 0);
    return "too-many";
  }

  writeReloads(page.sessionStorage, failed);
  return lost.run ?? reloadedFor;
}

type Answer = { readonly kind: "run"; readonly run: string } | { readonly kind: "down" | "refused" | "foreign" };

async function probe(lost: LostBackend, fetchFunction: typeof fetch): Promise<Answer> {
  let response: Response;
  try {
    response = await fetchFunction(`${lost.origin}/_spar/session`, {
      method: "POST",
      headers: { Authorization: `Bearer ${lost.token}` },
      cache: "no-store",
      signal: AbortSignal.timeout(probeTimeoutMs),
    });
  } catch {
    return { kind: "down" };
  }

  if (response.status === 401 || response.status === 403) {
    return { kind: "refused" };
  }

  if (response.status === 404) {
    return { kind: "foreign" };
  }

  if (!response.ok) {
    // 503 while the old run is stopping, or a server that is not ready yet.
    return { kind: "down" };
  }

  try {
    return { kind: "run", run: (await response.text()).trim() };
  } catch {
    return { kind: "down" };
  }
}

function stopWaiting(outcome: Exclude<RestartOutcome, "reloaded" | "too-many" | "none">, lost: LostBackend, page: RestartPage): RestartOutcome {
  switch (outcome) {
    case "same-run":
      page.log.warn(`Spar: the page lost its channel, but the backend at ${lost.origin} still runs; reload the page to connect again.`);
      break;
    case "refused":
      page.log.warn(`Spar: the backend at ${lost.origin} runs a new dev session with a new token; open the address it printed.`);
      break;
    case "foreign":
      page.log.warn(`Spar: something other than this app's backend answers at ${lost.origin}; the page stops waiting.`);
      break;
  }

  return outcome;
}

/** Quick at first, since a restart takes seconds; then every 5 s for a backend that is down for longer (a build error). */
export function delayFor(attempt: number, waitedMs: number): number {
  if (waitedMs >= patientAfterMs) {
    return 5_000;
  }

  return Math.min(250 * 2 ** attempt, 1_000);
}

function readReloads(storage: Pick<Storage, "getItem">): number {
  const count = Number.parseInt(storage.getItem(reloadsKey) ?? "0", 10);
  return Number.isSafeInteger(count) && count > 0 ? count : 0;
}

function writeReloads(storage: Pick<Storage, "setItem">, count: number): void {
  storage.setItem(reloadsKey, String(count));
}

function browserPage(): RestartPage {
  return {
    reload: () => location.reload(),
    // Read when used, never when the page is described: a channel that ends where there is no session storage (Node,
    // in the SDK's own tests) must not throw.
    sessionStorage: {
      getItem: key => globalThis.sessionStorage?.getItem(key) ?? null,
      setItem: (key, value) => globalThis.sessionStorage?.setItem(key, value),
      removeItem: key => globalThis.sessionStorage?.removeItem(key),
    },
    now: () => Date.now(),
    wait: ms => new Promise(resolve => setTimeout(resolve, ms)),
    fetch: globalThis.fetch.bind(globalThis),
    log: console,
  };
}
