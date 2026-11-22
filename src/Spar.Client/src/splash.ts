// The page side of start-up (plan section 3d): the splash the app writes in index.html, inside `#spar-splash`, which
// the Vite plugin styles as an opaque cover whose mark fades in after a CSS animation delay. The SDK removes it when
// the app reports ready: at once while the mark is not due (a fast start never shows it), or once it has been on
// screen for its minimum time, then faded out. Only then does the host hear `ready`, so the host's `ui-ready` comes
// after the splash is gone. No `<style>` element or `style` attribute: the fade-out is a Web Animation.

/** The element the app writes its splash in. */
const splashId = "spar-splash";

/** The splash style's entrance animation, on the cover's children (the mark). */
const entranceName = "spar-splash-in";

/** The defaults the plugin writes, should its custom properties be missing (an app that styled the splash itself). */
const defaultMinimumMs = 400;
const defaultFadeOutMs = 150;

/** A frame that never comes (a hidden page) is waited for this long at most. */
const frameFallbackMs = 100;

/**
 * Within this much of its appearance, the mark counts as due: a compositor can show it a frame before the page's own
 * clock reaches it, and removing it then would flash it for one frame.
 */
const appearanceMarginMs = 20;

/** The frame watch: from the bundle's start until the first rendered frame at or after the mark's appearance. */
let watching = false;
let entrance: Animation | undefined;
let paintedAt: number | undefined;

/**
 * Requests animation frames from the bundle's start (`connectSpar`) while the page has a splash, until the first frame
 * the page renders at or after the mark's scheduled appearance: the time the SDK counts the mark's minimum visible time
 * from. Measured in M1d step 5 (`docs/evidence/m1d-page-startup.md`): WebKitGTK drew the mark only at the first frame
 * after a long task, so counted from its scheduled time it was on screen for about 115 ms instead of 400; WebView2
 * animated it on its compositor through the task. Neither the document timeline nor anything else the page keeps tells
 * whether a frame rendered before the bundle ran (both engines' timelines read the current time inside a long task),
 * so a mark already due when the watch starts counts from the watch's first frame: up to the minimum longer than needed
 * after a slow download, never a flash. A page without a splash requests nothing.
 */
export function watchFrames(): void {
  if (watching || typeof requestAnimationFrame !== "function" || !hasSplash()) {
    return;
  }

  watching = true;
  requestAnimationFrame(frame);
}

function frame(): void {
  if (!watching || paintedAt !== undefined) {
    return;
  }

  // The clock when the callback runs, not the frame's own time stamp: Chromium stamps a frame with the time it began,
  // which is before a long task that kept the callback from running (measured: 11 ms for a callback after 1.5 s).
  const now = performance.now();
  const appears = appearanceOf(entrance ??= entranceAnimation());
  if (appears !== undefined && now >= appears) {
    paintedAt = now;
    return;
  }

  requestAnimationFrame(frame);
}

/** What the splash did, for the tests and the evidence. */
export interface SplashOutcome {
  /** Whether the page had a splash element. */
  readonly present: boolean;
  /** Whether its mark was due when the app was ready. */
  readonly shown: boolean;
  /** How long the SDK kept it after the app was ready, fade-out included, in milliseconds. */
  readonly keptMs: number;
}

let finishing: Promise<SplashOutcome> | undefined;

/**
 * Removes the splash, once per document, and resolves once its removal has had a frame: at once when there is none or
 * its mark is not due, else after its minimum visible time and its fade-out. `aria-busy` leaves `<body>` with it.
 */
export function finishSplash(): Promise<SplashOutcome> {
  finishing ??= removeSplash();
  return finishing;
}

/** Whether the page has a splash element now. */
export function hasSplash(): boolean {
  return typeof document !== "undefined" && document.getElementById(splashId) !== null;
}

async function removeSplash(): Promise<SplashOutcome> {
  const splash = typeof document === "undefined" ? null : document.getElementById(splashId);
  watching = false;
  if (splash === null) {
    return { present: false, shown: false, keptMs: 0 };
  }

  const readyAt = performance.now();
  const since = visibleSince(splash, readyAt);
  if (since === undefined) {
    remove(splash);
    await frames(2);
    return { present: true, shown: false, keptMs: 0 };
  }

  const style = getComputedStyle(splash);
  const minimum = numberProperty(style, "--spar-splash-minimum", defaultMinimumMs);
  const waitMs = Math.max(0, minimum - (readyAt - since));
  if (waitMs > 0) {
    await delay(waitMs);
  }

  if (!matchMedia("(prefers-reduced-motion: reduce)").matches) {
    await fadeOut(splash, numberProperty(style, "--spar-splash-fade-out", defaultFadeOutMs));
  }

  remove(splash);
  await frames(2);
  return { present: true, shown: true, keptMs: Math.round(performance.now() - readyAt) };
}

/**
 * When the mark is counted as visible from, on the page's clock, or undefined while it is not due: the first frame
 * the page rendered at or after its scheduled appearance (its entrance animation's start time plus its delay). When no
 * frame has rendered since it was due (a long task just ended, or the bundle started after it was due), from now: an engine that draws animations only in the
 * page's own frames has not drawn it yet, and one that animates on its compositor has been showing it.
 */
function visibleSince(splash: HTMLElement, now: number): number | undefined {
  const appears = appearanceOf(entrance ?? entranceAnimation(splash));
  if (appears === undefined || now + appearanceMarginMs < appears) {
    return undefined;
  }

  return paintedAt ?? now;
}

/** The mark's scheduled appearance on the page's clock, or undefined while its animation has not started. */
function appearanceOf(animation: Animation | undefined): number | undefined {
  const start = animation?.startTime;
  if (animation === undefined || start === null || start === undefined) {
    return undefined;
  }

  return Number(start) + Number(animation.effect?.getTiming().delay ?? 0);
}

function entranceAnimation(splash = document.getElementById(splashId)): Animation | undefined {
  return splash?.getAnimations({ subtree: true }).find(isEntrance);
}

function isEntrance(animation: Animation): boolean {
  return "animationName" in animation && (animation as CSSAnimation).animationName === entranceName;
}

function numberProperty(style: CSSStyleDeclaration, name: string, fallback: number): number {
  const value = Number.parseFloat(style.getPropertyValue(name));
  return Number.isFinite(value) && value >= 0 ? value : fallback;
}

async function fadeOut(splash: HTMLElement, durationMs: number): Promise<void> {
  if (durationMs <= 0 || typeof splash.animate !== "function") {
    return;
  }

  const fade = splash.animate([{ opacity: 1 }, { opacity: 0 }], { duration: durationMs, easing: "ease-in", fill: "forwards" });
  await Promise.race([fade.finished.then(() => undefined, () => undefined), delay(durationMs + frameFallbackMs)]);
}

function remove(splash: HTMLElement): void {
  splash.remove();
  document.body?.removeAttribute("aria-busy");
}

function delay(ms: number): Promise<void> {
  return new Promise(resolve => setTimeout(resolve, ms));
}

/** Resolves after `count` animation frames, each waited for at most a short while. */
async function frames(count: number): Promise<void> {
  for (let i = 0; i < count; i++) {
    await new Promise<void>(resolve => {
      const timer = setTimeout(resolve, frameFallbackMs);
      if (typeof requestAnimationFrame !== "function") {
        return;
      }

      requestAnimationFrame(() => {
        clearTimeout(timer);
        resolve();
      });
    });
  }
}

/** @internal For the SDK's tests: forgets this document's start, as a new document would. */
export function resetSplashForTests(): void {
  watching = false;
  entrance = undefined;
  paintedAt = undefined;
  finishing = undefined;
}

/** @internal For the SDK's tests: the frame watch's record of the mark's first rendered frame. */
export function notePaintedForTests(time: number | undefined): void {
  paintedAt = time;
}
