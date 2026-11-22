// One start of the probe's splash (plan section 3d), as its parameters pick it:
//   late=<ms>   the bundle starts this much later (a wait before the SDK is connected);
//   work=<ms>   synchronous start-up work, and at=same (in the task that connects the SDK, after it), at=later (in a
//               task of its own after it) or at=before (before the SDK is connected: no frame was requested first);
//   ready=<ms>  how long after that the app reports ready (its first view after its data).
// It notes what the page saw on its own clock: when the bundle started and the SDK connected, the first frame after
// that, the mark's entrance as its animation reports it, ready, and when the splash left. The test reads the frames.
import { connectSpar, hostChannel } from "@spar/app/sdk";
import { fingerprint } from "@spar/app/spar";
import { probe } from "@spar/app/probe";

/**
 * The starts a test window's first page can take, by its width beyond 800 in steps of 40 (Program.cs sets it from
 * STARTUP_PROBE_START): a first page has no address of its own to carry them. Index 0 is a plain fast start.
 */
export const firstPageStarts = [
  "ready=0",
  "late=300&ready=0",
  "late=1200&ready=0",
  "ready=1500",
  "late=600&work=600&at=same&ready=0",
  "late=600&work=600&at=later&ready=0",
  "late=600&work=600&at=before&ready=0",
  "work=1500&at=same&ready=0",
];

function busy(ms) {
  const until = performance.now() + ms;
  while (performance.now() < until) {
    // Start-up work that holds the page's thread.
  }
}

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

/** Runs one start; resolves with the SDK's connection once the page noted it, its splash gone. */
export async function runStart(parameters) {
  const late = Number(parameters.get("late") ?? 0);
  const work = Number(parameters.get("work") ?? 0);
  const at = parameters.get("at") ?? "same";
  const readyAfter = Number(parameters.get("ready") ?? 0);
  const marks = { timeOrigin: performance.timeOrigin, bundle: Math.round(performance.now()), query: parameters.toString() };

  if (late > 0) {
    await sleep(late);
  }

  if (work > 0 && at === "before") {
    busy(work);
    marks.workDone = Math.round(performance.now());
  }

  const connection = connectSpar(hostChannel(), fingerprint);
  marks.connected = Math.round(performance.now());
  requestAnimationFrame(time => {
    // When the callback ran, and the frame's own time stamp, which Chromium takes when it begins the frame.
    marks.firstFrame = Math.round(performance.now());
    marks.firstFrameStamp = Math.round(time);
  });

  if (work > 0 && at === "same") {
    busy(work);
    marks.workDone = Math.round(performance.now());
  }

  if (work > 0 && at === "later") {
    await sleep(0);
    busy(work);
    marks.workDone = Math.round(performance.now());
  }

  const splash = document.getElementById("spar-splash");
  new MutationObserver(() => {
    if (!splash.isConnected && marks.removed === undefined) {
      marks.removed = Math.round(performance.now());
    }
  }).observe(document.body, { childList: true });

  await sleep(readyAfter);

  const entrance = splash.getAnimations({ subtree: true }).find(animation => animation.animationName === "spar-splash-in");
  marks.entranceStart = entrance?.startTime == null ? null : Math.round(entrance.startTime);
  marks.entranceDelay = entrance?.effect?.getTiming().delay ?? null;
  marks.ready = Math.round(performance.now());
  document.getElementById("app").hidden = false;
  connection.ready();

  while (splash.isConnected) {
    await sleep(10);
  }

  // The observer's record of the removal comes in a microtask after it.
  await sleep(0);
  marks.paints = performance.getEntriesByType("paint").map(entry => `${entry.name}=${Math.round(entry.startTime)}`).join(" ");
  marks.reducedMotion = matchMedia("(prefers-reduced-motion: reduce)").matches;
  marks.busy = document.body.getAttribute("aria-busy");
  await probe.note(JSON.stringify(marks));
  return connection;
}
