// Spar's development overlay (plan section 4, "Hot Reload signal"): a small toast per backend update, in a corner of
// the page, dismissable, gone by itself after a few seconds. Debug builds only (reached behind `import.meta.env?.DEV`).
// It runs under the page's strict policy and Trusted Types: its elements are built with the DOM, its text set with
// `textContent`, and its look is a constructed stylesheet adopted by its own shadow root, so no `<style>` element, no
// `style` attribute and no HTML string is ever used, and the app's CSS never reaches it nor it the app's.
import type { BackendUpdate } from "./backendUpdate.js";

/** How long a toast stays when nobody dismisses it. */
const toastMs = 8_000;

/** At most this many toasts at once; a new one pushes the oldest out. */
const maxToasts = 3;

const overlayCss = `
:host { all: initial; }
.region {
  position: fixed; right: 16px; bottom: 16px; z-index: 2147483647;
  display: flex; flex-direction: column; gap: 8px; align-items: flex-end;
  pointer-events: none;
  font: 13px/1.4 system-ui, -apple-system, "Segoe UI", sans-serif;
}
.toast {
  display: flex; align-items: center; gap: 10px; max-width: min(420px, calc(100vw - 32px));
  padding: 8px 8px 8px 12px; border-radius: 8px; pointer-events: auto;
  color: #f4f4f5; background: #27272a; box-shadow: 0 4px 16px rgb(0 0 0 / 0.25);
}
.text { overflow-wrap: anywhere; }
.dismiss {
  flex: none; width: 24px; height: 24px; padding: 0; border: 0; border-radius: 6px;
  color: inherit; background: transparent; font: inherit; font-size: 16px; line-height: 1; cursor: pointer;
}
.dismiss:hover, .dismiss:focus-visible { background: rgb(255 255 255 / 0.15); outline: none; }
@media (prefers-color-scheme: light) {
  .toast { color: #18181b; background: #ffffff; box-shadow: 0 4px 16px rgb(0 0 0 / 0.18); }
  .dismiss:hover, .dismiss:focus-visible { background: rgb(0 0 0 / 0.08); }
}
`;

let region: HTMLElement | undefined;

/** The toast's text: "Backend updated: PagesService (revision 3)", the types by their short names. */
export function backendUpdateText(update: BackendUpdate): string {
  const names = update.types.map(shortName);
  const which = names.length === 0 ? "" : `: ${names.join(", ")}`;
  return `Backend updated${which} (revision ${update.revision})`;
}

/** @internal Shows one toast for the update. */
export function showBackendUpdateToast(update: BackendUpdate): void {
  const container = overlayRegion();
  if (container === undefined) {
    return;
  }

  while (container.childElementCount >= maxToasts) {
    container.firstElementChild?.remove();
  }

  const toast = document.createElement("div");
  toast.className = "toast";
  toast.setAttribute("role", "status");

  const text = document.createElement("span");
  text.className = "text";
  text.textContent = backendUpdateText(update);

  const dismiss = document.createElement("button");
  dismiss.type = "button";
  dismiss.className = "dismiss";
  dismiss.setAttribute("aria-label", "Dismiss");
  dismiss.textContent = "×";

  const timer = setTimeout(() => toast.remove(), toastMs);
  dismiss.addEventListener("click", () => {
    clearTimeout(timer);
    toast.remove();
  });

  toast.append(text, dismiss);
  container.append(toast);
}

/** The overlay's region, made once per document: a host element at the end of the body, with its own shadow root. */
function overlayRegion(): HTMLElement | undefined {
  if (region?.isConnected === true) {
    return region;
  }

  const parent = document.body ?? document.documentElement;
  if (parent === null) {
    return undefined;
  }

  const host = document.createElement("div");
  host.setAttribute("data-spar-dev-overlay", "");
  const shadow = host.attachShadow({ mode: "open" });
  const sheet = new CSSStyleSheet();
  sheet.replaceSync(overlayCss);
  shadow.adoptedStyleSheets = [sheet];

  const container = document.createElement("div");
  container.className = "region";
  container.setAttribute("role", "region");
  container.setAttribute("aria-label", "Spar development notices");
  shadow.append(container);
  parent.append(host);
  region = container;
  return container;
}

/** `Notebook.Pages.PagesService` to `PagesService`; a nested type's `Outer+Inner` to `Inner`. */
function shortName(name: string): string {
  const cut = Math.max(name.lastIndexOf("."), name.lastIndexOf("+"));
  return cut < 0 ? name : name.slice(cut + 1);
}
