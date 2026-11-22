// A loopback dev session's restarts (plan section 4; decision "M1d step 2's shape: loopback in the development loop"):
// when the backend stops, the open page waits for its next run at the same address and token, then reloads by itself,
// once. The calls that were in flight have already ended as `transport` / `lost`, execution unknown (connection.ts):
// nothing is replayed, and the reloaded page starts afresh.

/** The header of a dev session's channel that names the backend's run; the test host sends none. */
export const runHeader = "X-Spar-Run";

/** How many automatic reloads in a row may each end within `shortLifeMs` before the page stops reloading. */
const maxShortReloads = 3;

/** A document that stayed connected at least this long breaks a run of short-lived reloads. */
const shortLifeMs = 5_000;

/** How long one look at the backend waits; a backend that is starting may take a moment to answer. */
const probeTimeoutMs = 3_000;

/** After waiting this long, the page looks less often. */
const patientAfterMs = 60_000;

const reloadsKey = "spar.restart.reloads";

/** What happened: the page reloaded, or why it stopped waiting. */
export type RestartOutcome = "reloaded" | "same-run" | "refused" | "foreign" | "too-many";

/** What waiting needs from the page; the browser's own objects by default. */
export interface RestartPage {
  readonly reload: () => void;
  readonly sessionStorage: Pick<Storage, "getItem" | "setItem">;
  readonly now: () => number;
  readonly wait: (ms: number) => Promise<void>;
  readonly fetch: typeof fetch;
  readonly log: Pick<Console, "info" | "warn" | "error">;
}

/** The backend this page had and when its document connected. */
export interface LostBackend {
  readonly origin: string;
  readonly token: string;
  /** The run named by the channel's `X-Spar-Run` header. */
  readonly run: string;
  /** When the document received `open`, by `page.now()`. */
  readonly openedAt: number;
}

/**
 * Waits for the backend's next run, then reloads the page once. It looks at `POST /_spar/session` with the page's
 * token, more slowly as time passes, and stops without reloading when the backend still runs the same run (the
 * channel ended for another reason), refuses the token (a new dev session: the backend printed a new address), or is
 * not Spar's; and when the page has already reloaded `maxShortReloads` times in a row into documents that each lost
 * their backend within `shortLifeMs`, so a backend that keeps failing never makes the page reload in a loop.
 */
export async function awaitRestart(lost: LostBackend, page: RestartPage = browserPage()): Promise<RestartOutcome> {
  const lived = page.now() - lost.openedAt;
  const shortReloads = lived >= shortLifeMs ? 0 : readReloads(page.sessionStorage);
  if (shortReloads >= maxShortReloads) {
    page.log.error(`Spar: the backend at ${lost.origin} keeps stopping soon after the page connects, so the page no longer reloads by itself. Reload it once the backend is fixed.`);
    writeReloads(page.sessionStorage, 0);
    return "too-many";
  }

  page.log.info(`Spar: the backend at ${lost.origin} stopped; the page reloads when it runs again.`);
  const started = page.now();
  for (let attempt = 0; ; attempt++) {
    await page.wait(delayFor(attempt, page.now() - started));
    const answer = await probe(lost, page.fetch);
    if (answer.kind === "down") {
      continue;
    }

    if (answer.kind === "run" && answer.run !== lost.run) {
      writeReloads(page.sessionStorage, shortReloads + 1);
      page.reload();
      return "reloaded";
    }

    return stopWaiting(answer.kind === "run" ? "same-run" : answer.kind, lost, page);
  }
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

function stopWaiting(outcome: Exclude<RestartOutcome, "reloaded" | "too-many">, lost: LostBackend, page: RestartPage): RestartOutcome {
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
    sessionStorage,
    now: () => Date.now(),
    wait: ms => new Promise(resolve => setTimeout(resolve, ms)),
    fetch: globalThis.fetch.bind(globalThis),
    log: console,
  };
}
