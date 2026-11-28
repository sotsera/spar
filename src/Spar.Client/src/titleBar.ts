// The page-drawn title bar (docs/app-model.md 6.9): a window whose `Program.cs` sets `TitleBar(TitleBar.Custom)` hears
// the host event `titleBar`, `{ maximized }`, when its document opens and at each change. From the first one the SDK:
// - fills each empty `data-spar-caption` element with the standard minimize, maximize and close buttons, labelled for
//   screen readers (the labels come from `data-spar-label-minimize`, `-maximize`, `-restore` and `-close` on it), and
//   leaves an app's own `<button data-spar-caption="close">` as it is;
// - sets `data-spar-window` on `<html>` to `maximized` or `normal`, so CSS can swap the maximize and restore icons;
// - posts the title bar's layout to the host (`spar.titleBar`) whenever it changes, which the host hit-tests from;
// - runs a caption button's action when the page gets its click: the keyboard, a screen reader, and on Linux the mouse
//   (on Windows the host takes the mouse over the buttons, for Snap Layouts, and shows their hover and pressed look
//   through the event `caption`, `{ hover, pressed }`).
// The stylesheet that draws the buttons and marks the drag area is `vite/sdkStyle.ts`, which the Vite plugin adds.
import { unknownErrorCase, type Connection, type Decoder } from "./connection.js";
import { invoke } from "./sdk.js";
import { isWebKitHost } from "./webkit.js";
import { windows } from "./windows.js";

/** A caption button's action. */
export type CaptionAction = "minimize" | "maximize" | "close";

const titleBarEvent = "titleBar";

const captionEvent = "caption";

/** The drag area: the element and everything in it but its controls. */
const barSelector = "[data-spar-titlebar]";

/**
 * What inside a drag area stays the page's: its controls, and anything marked `data-spar-no-drag`. The stylesheet
 * gives the same elements `app-region: no-drag` (vite/sdkStyle.ts; a test keeps the two lists equal).
 */
export const noDragSelector =
  "a[href], button, input, select, textarea, label, summary, [contenteditable]:not([contenteditable=\"false\"]), " +
  "[tabindex], [role=\"button\"], [role=\"link\"], [role=\"tab\"], [role=\"menuitem\"], [role=\"checkbox\"], " +
  "[role=\"switch\"], [data-spar-no-drag], [data-spar-caption]";

/** A caption element: empty for the SDK's buttons, or an app's own button with its action. */
const captionSelector = "[data-spar-caption]";

/** The SDK's own buttons, inside an empty caption element. */
const sdkButtonSelector = "[data-spar-caption-button]";

const actions: readonly CaptionAction[] = ["minimize", "maximize", "close"];

const defaultLabels = { minimize: "Minimize", maximize: "Maximize", restore: "Restore", close: "Close" } as const;

/** The icons, in a 10 × 10 box drawn with the button's colour. */
const iconPaths = {
  minimize: "M0 5.5h10",
  maximize: "M0.5 0.5h9v9h-9z",
  restore: "M0.5 2.5h7v7h-7z M2.5 2.5v-2h7v7h-2",
  close: "M0.5 0.5l9 9 M9.5 0.5l-9 9",
} as const;

const svgNamespace = "http://www.w3.org/2000/svg";

const layoutDecoder: Decoder<void> = {
  value: () => undefined,
  error: () => unknownErrorCase,
};

/** A rectangle of the viewport in CSS pixels: `[x, y, width, height]`. */
type Rect = readonly [number, number, number, number];

/** The layout the host hit-tests from. */
interface TitleBarLayout {
  readonly bars: readonly Rect[];
  readonly holes: readonly Rect[];
  readonly minimize: Rect | null;
  readonly maximize: Rect | null;
  readonly close: Rect | null;
}

let started = false;

let maximized = false;

let lastPosted = "";

let scheduled = false;

let resizes: ResizeObserver | undefined;

/** The elements the size observer watches. */
const observed = new Set<HTMLElement>();

/** @internal Listens on a new connection for the host's title bar events; a new document starts over. */
export function listenForTitleBar(connection: Connection): void {
  lastPosted = "";
  connection.onEvent(titleBarEvent, receiveTitleBar);
  connection.onEvent(captionEvent, receiveCaption);
}

/** @internal One `titleBar` event, `{ maximized }`: the window's state, and the start of the title bar's work. */
export function receiveTitleBar(payload: string): void {
  const state = readObject(payload);
  if (state === undefined || typeof state.maximized !== "boolean") {
    console.warn("Spar: a titleBar event that could not be read was ignored.");
    return;
  }

  maximized = state.maximized;
  if (!started) {
    start();
  }

  showState();
}

/** @internal One `caption` event, `{ hover, pressed }`: the look of the button the host has the pointer over. */
export function receiveCaption(payload: string): void {
  const state = readObject(payload);
  if (state === undefined) {
    console.warn("Spar: a caption event that could not be read was ignored.");
    return;
  }

  for (const button of captionButtons()) {
    const action = actionOf(button);
    toggle(button, "data-spar-hover", state.hover === action);
    toggle(button, "data-spar-pressed", state.pressed === action);
  }
}

/** @internal The layout of the document's title bar now, in CSS pixels of the viewport. */
export function measureTitleBar(root: ParentNode = document): TitleBarLayout {
  const bars: Rect[] = [];
  const holes: Rect[] = [];
  for (const bar of root.querySelectorAll<HTMLElement>(barSelector)) {
    const rect = rectOf(bar);
    if (rect === undefined) {
      continue;
    }

    bars.push(rect);
    for (const hole of bar.querySelectorAll<HTMLElement>(noDragSelector)) {
      const holeRect = rectOf(hole);
      if (holeRect !== undefined) {
        holes.push(holeRect);
      }
    }
  }

  const buttons = new Map<CaptionAction, Rect>();
  for (const button of captionButtons(root)) {
    const action = actionOf(button);
    const rect = rectOf(button);
    if (action !== undefined && rect !== undefined && !buttons.has(action)) {
      buttons.set(action, rect);
    }
  }

  return {
    bars: bars.slice(0, 16),
    holes: holes.slice(0, 256),
    minimize: buttons.get("minimize") ?? null,
    maximize: buttons.get("maximize") ?? null,
    close: buttons.get("close") ?? null,
  };
}

/** @internal Fills every empty caption element of the document with the SDK's buttons. */
export function fillCaptions(root: ParentNode = document): void {
  for (const caption of root.querySelectorAll<HTMLElement>(captionSelector)) {
    if (caption.getAttribute("data-spar-caption") === "" && caption.querySelector(sdkButtonSelector) === null) {
      for (const action of actions) {
        caption.append(standardButton(caption, action));
      }
    }
  }

  showState();
}

/** The title bar's work for this document: its buttons, its layout followed, and the buttons' clicks. */
function start(): void {
  started = true;
  const html = document.documentElement;
  setIfChanged(html, "data-spar-platform", isWebKitHost() ? "linux" : "windows");
  document.addEventListener("click", onClick);
  globalThis.addEventListener("resize", schedule);
  resizes = new ResizeObserver(schedule);
  const changes = new MutationObserver(onMutation);
  changes.observe(html, {
    subtree: true,
    childList: true,
    attributes: true,
    attributeFilter: ["data-spar-titlebar", "data-spar-caption", "data-spar-no-drag", "class", "hidden"],
  });
  fillCaptions();
  schedule();
}

function onMutation(): void {
  fillCaptions();
  schedule();
}

/** One post per frame at most, after the frame's layout. */
function schedule(): void {
  if (scheduled) {
    return;
  }

  scheduled = true;
  requestAnimationFrame(() => {
    scheduled = false;
    postLayout();
  });
}

/** Posts the layout when it changed; the elements it measured are watched for their next change of size. */
function postLayout(): void {
  observeSizes();
  const layout = JSON.stringify(measureTitleBar());
  if (layout === lastPosted) {
    return;
  }

  lastPosted = layout;
  void invoke<void, Record<never, never>>("spar", "titleBar", () => layout, layoutDecoder).match({
    success: () => undefined,
    failure: error => console.warn(`Spar: the title bar's layout was not taken (${error.kind} / ${error.code}).`, error.detail ?? ""),
  });
}

/**
 * Watches the size of the bars and their controls, each element once: observing an element again makes the observer
 * report it again, which would schedule another post every frame.
 */
function observeSizes(): void {
  if (resizes === undefined) {
    return;
  }

  const now = new Set(document.querySelectorAll<HTMLElement>(`${barSelector}, ${barSelector} :is(${noDragSelector})`));
  for (const element of observed) {
    if (!now.has(element)) {
      resizes.unobserve(element);
      observed.delete(element);
    }
  }

  for (const element of now) {
    if (!observed.has(element)) {
      resizes.observe(element);
      observed.add(element);
    }
  }
}

/** A click on a caption button: its action. On Windows a mouse click there goes to the host instead. */
function onClick(event: MouseEvent): void {
  const target = event.target instanceof Element ? event.target : null;
  const button = target?.closest<HTMLElement>(`${sdkButtonSelector}, ${captionSelector}`);
  const action = button === null || button === undefined ? undefined : actionOf(button);
  if (action === undefined) {
    return;
  }

  event.preventDefault();
  if (action === "minimize") {
    void windows.minimize().match({ success: () => undefined });
  } else if (action === "maximize") {
    void (maximized ? windows.restore() : windows.maximize()).match({ success: () => undefined });
  } else {
    windows.close();
  }
}

/** The window's state on `<html>`, and the maximize button's name. */
function showState(): void {
  if (!started) {
    return;
  }

  setIfChanged(document.documentElement, "data-spar-window", maximized ? "maximized" : "normal");
  for (const button of document.querySelectorAll<HTMLElement>(`${sdkButtonSelector}[data-spar-caption-button="maximize"]`)) {
    const caption = button.closest<HTMLElement>(captionSelector);
    setIfChanged(button, "aria-label", labelOf(caption, maximized ? "restore" : "maximize"));
  }
}

/** A standard button: its action, its name for screen readers, and its icons. */
function standardButton(caption: HTMLElement, action: CaptionAction): HTMLButtonElement {
  const button = document.createElement("button");
  button.type = "button";
  button.tabIndex = -1;
  button.setAttribute("data-spar-caption-button", action);
  button.setAttribute("aria-label", labelOf(caption, action === "maximize" && maximized ? "restore" : action));
  button.append(icon(action));
  if (action === "maximize") {
    button.append(icon("restore"));
  }

  return button;
}

function icon(name: keyof typeof iconPaths): SVGSVGElement {
  const svg = document.createElementNS(svgNamespace, "svg");
  svg.setAttribute("viewBox", "0 0 10 10");
  svg.setAttribute("width", "10");
  svg.setAttribute("height", "10");
  svg.setAttribute("aria-hidden", "true");
  svg.setAttribute("focusable", "false");
  svg.setAttribute("data-spar-icon", name);
  const path = document.createElementNS(svgNamespace, "path");
  path.setAttribute("d", iconPaths[name]);
  path.setAttribute("fill", "none");
  path.setAttribute("stroke", "currentColor");
  path.setAttribute("stroke-width", "1");
  svg.append(path);
  return svg;
}

function labelOf(caption: HTMLElement | null, name: keyof typeof defaultLabels): string {
  return caption?.getAttribute(`data-spar-label-${name}`) ?? defaultLabels[name];
}

/** Every caption button of the document: the SDK's, and an app's own. */
function captionButtons(root: ParentNode = document): HTMLElement[] {
  return [...root.querySelectorAll<HTMLElement>(`${sdkButtonSelector}, ${captionSelector}`)]
    .filter(element => actionOf(element) !== undefined);
}

function actionOf(element: Element): CaptionAction | undefined {
  const name = element.getAttribute("data-spar-caption-button") ?? element.getAttribute("data-spar-caption");
  return actions.find(action => action === name);
}

/** The element's rectangle, rounded to hundredths; undefined when it takes no space (hidden or empty). */
function rectOf(element: Element): Rect | undefined {
  const box = element.getBoundingClientRect();
  if (box.width <= 0 || box.height <= 0) {
    return undefined;
  }

  return [round(box.x), round(box.y), round(box.width), round(box.height)];
}

function round(value: number): number {
  return Math.round(value * 100) / 100;
}

/**
 * Sets an attribute only when its value changes: setting it to the same value is a mutation too, and a page whose own
 * observer answers the SDK's attributes (a class on `<html>` for `data-spar-window`) would otherwise loop with the SDK's.
 */
function setIfChanged(element: Element, name: string, value: string): void {
  if (element.getAttribute(name) !== value) {
    element.setAttribute(name, value);
  }
}

function toggle(element: Element, name: string, on: boolean): void {
  if (on && !element.hasAttribute(name)) {
    element.setAttribute(name, "");
  } else if (!on && element.hasAttribute(name)) {
    element.removeAttribute(name);
  }
}

function readObject(payload: string): Readonly<Record<string, unknown>> | undefined {
  try {
    const value: unknown = JSON.parse(payload);
    return typeof value === "object" && value !== null ? (value as Readonly<Record<string, unknown>>) : undefined;
  } catch {
    return undefined;
  }
}
