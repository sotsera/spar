// Spike stand-in for the Spar SDK's theme and splash parts. The host channel is chrome.webview on WebView2, and the
// same shape on WebKitGTK (a document-start user script presents the script message handler that way).

export type ColorScheme = "light" | "dark" | "system";

interface HostChannel {
  postMessage(message: string): void;
  addEventListener(type: "message", listener: (event: MessageEvent) => void): void;
}

const host = (globalThis as { chrome?: { webview?: HostChannel } }).chrome?.webview;

export function post(message: Record<string, unknown>): void {
  host?.postMessage(JSON.stringify(message));
}

export function mark(name: string, extra?: Record<string, unknown>): void {
  post({ t: "mark", n: name, p: Math.round(performance.now() * 10) / 10, ...extra });
}

/** The app's own choice. The host saves it, applies it now (window, title bar, WebView, prefers-color-scheme), and at the next start. */
export function setColorScheme(scheme: ColorScheme): void {
  post({ t: "scheme", v: scheme });
}

const frame = () => new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));
const sleep = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms));

/** What the page looked like when the bundle started: proves the inline styles ran before any script. */
function snapshot() {
  const root = getComputedStyle(document.documentElement);
  const splash = document.getElementById("spar-splash");
  const mark = splash?.querySelector(".spar-splash-mark");
  const splashStyle = splash ? getComputedStyle(splash) : undefined;
  const markStyle = mark ? getComputedStyle(mark) : undefined;
  return {
    scheme: matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light",
    reducedMotion: matchMedia("(prefers-reduced-motion: reduce)").matches,
    rootBackground: root.backgroundColor,
    rootColorScheme: root.colorScheme,
    // The bundle stylesheet: loaded and applied? (the header is blue only through it)
    cssLinks: [...document.querySelectorAll("link[rel=stylesheet]")].map((l) => { const sheet = (l as HTMLLinkElement).sheet; if (!sheet) return "NOT LOADED"; try { return "rules=" + sheet.cssRules.length; } catch { return "rules=inaccessible"; } }).join(","),
    header: document.querySelector("header") ? getComputedStyle(document.querySelector("header")!).backgroundColor : null,
    ariaBusy: document.body.getAttribute("aria-busy"),
    // A blocked inline <style> (CSP without its hash) has no sheet.
    themeSheet: (document.getElementById("spar-theme") as HTMLStyleElement | null)?.sheet ? "applied" : document.getElementById("spar-theme") ? "BLOCKED" : "absent",
    splashSheet: (document.getElementById("spar-splash-style") as HTMLStyleElement | null)?.sheet ? "applied" : document.getElementById("spar-splash-style") ? "BLOCKED" : "absent",
    splash: splashStyle
      ? {
          animation: splashStyle.animationName, duration: splashStyle.animationDuration, delay: splashStyle.animationDelay, opacity: splashStyle.opacity,
          mark: markStyle ? { animation: markStyle.animationName, duration: markStyle.animationDuration, delay: markStyle.animationDelay, opacity: markStyle.opacity } : null,
        }
      : null,
  };
}

const atStart = snapshot();

// The animation timeline keeps running while the main thread is blocked, but no frame reaches the screen then. The
// first frame after the bundle starts tells whether start-up work blocked rendering; if it did, the splash cannot
// have been seen before that frame.
const scriptStart = performance.now();
// Spike switch: ?timelineOnly=1 measures the first rule (animation timeline only), for comparison.
const splashFrameRule = { off: new URLSearchParams(location.search).has("timelineOnly") };
let firstFrameAfterStart: number | null = null;
requestAnimationFrame((time) => {
  firstFrameAfterStart = time;
});
let manual = false;
let readyStarted = false;

/** Manual mode: the splash stays until the app calls ready(), for example after its first data. */
export function useManualReady(): void {
  manual = true;
}

/** Called by the app's bootstrap after its first render. */
export function appMounted(): void {
  mark("mounted");
  if (!manual) void ready();
}

interface SplashResult {
  readonly present: boolean;
  readonly shown: boolean;
  readonly visibleMs: number;
  readonly waitedMs: number;
}

/**
 * Removes the splash. Not yet visible (still inside its CSS delay): removed at once, so a fast start never shows it.
 * Visible: kept until it has been visible for its minimum time, then faded out, so it never flashes on and off.
 * The visible time comes from the CSS animation's start time, so it is right even if the bundle ran late.
 */
async function removeSplash(): Promise<SplashResult> {
  const splash = document.getElementById("spar-splash");
  if (!splash) return { present: false, shown: false, visibleMs: 0, waitedMs: 0 };

  // v2: the entrance animation is on the mark inside the cover; v1: on the splash element itself.
  const entrance = splash.getAnimations({ subtree: true }).find((a) => (a as CSSAnimation).animationName === "spar-splash-in");
  const delay = Number(entrance?.effect?.getTiming().delay ?? 0);
  const now = Number(document.timeline.currentTime ?? performance.now());
  let visibleSince = entrance?.startTime == null ? null : Number(entrance.startTime) + delay;
  const blockedUntil = firstFrameAfterStart !== null && firstFrameAfterStart - scriptStart > 100 ? firstFrameAfterStart : null;
  if (visibleSince !== null && blockedUntil !== null && visibleSince < blockedUntil && !splashFrameRule.off) {
    mark("splash-blocked", { timeline: Math.round(visibleSince), firstFrame: Math.round(blockedUntil) });
    visibleSince = blockedUntil;
  }
  if (visibleSince === null || now < visibleSince) {
    splash.remove();
    mark("splash-removed", { shown: false });
    return { present: true, shown: false, visibleMs: 0, waitedMs: 0 };
  }

  const minimum = Number(splash.dataset.minVisible ?? 400);
  const waited = Math.max(0, minimum - (now - visibleSince));
  mark("splash-visible-since", { since: Math.round(visibleSince), waitMs: Math.round(waited) });
  if (waited > 0) await sleep(waited);

  if (!matchMedia("(prefers-reduced-motion: reduce)").matches) {
    const fade = Number(splash.dataset.fadeOut ?? 150);
    const from = getComputedStyle(splash).opacity;
    await splash.animate([{ opacity: from }, { opacity: 0 }], { duration: fade, easing: "ease-in", fill: "forwards" }).finished;
  }

  splash.remove();
  const visibleMs = Number(document.timeline.currentTime ?? performance.now()) - visibleSince;
  mark("splash-removed", { shown: true, visibleMs: Math.round(visibleMs) });
  return { present: true, shown: true, visibleMs: Math.round(visibleMs), waitedMs: Math.round(waited) };
}

/** ui-ready: the app has painted, the splash is gone, and that removal has painted too. Only then does the host hear it. */
export async function ready(): Promise<void> {
  if (readyStarted) return;
  readyStarted = true;
  await frame();
  await frame();
  const splash = await removeSplash();
  document.body.removeAttribute("aria-busy");
  await frame();
  await frame();
  const navigation = performance.getEntriesByType("navigation")[0] as PerformanceNavigationTiming | undefined;
  const timing = {
    responseEnd: Math.round(navigation?.responseEnd ?? -1),
    domInteractive: Math.round(navigation?.domInteractive ?? -1),
    paints: performance.getEntriesByType("paint").map((e) => `${e.name}=${Math.round(e.startTime)}`).join(" "),
  };
  post({ t: "ready", p: Math.round(performance.now() * 10) / 10, splash, timing, atStart, atReady: snapshot() });
}

// Host → page: the host asks the app to switch (stands in for the app's own settings UI), and theme reports.
host?.addEventListener("message", (event) => {
  const data = String(event.data);
  if (data.startsWith("set-scheme:")) setColorScheme(data.slice(11) as ColorScheme);
});

matchMedia("(prefers-color-scheme: dark)").addEventListener("change", (event) =>
  mark("prefers-color-scheme", { dark: event.matches, bg: getComputedStyle(document.documentElement).backgroundColor }));
