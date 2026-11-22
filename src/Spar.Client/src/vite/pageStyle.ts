// The page side of start-up (plan section 3d): the theme style every page gets, and the splash's style for a page that
// writes one. Pure functions over the HTML text, so the Vite plugin (index.ts) stays small and the rules are tested
// without a build. The host hashes both styles exactly as they are written here (src/Spar/Hosting/PageHtml.cs), from
// their opening tags, `<style id="spar-theme">` and `<style id="spar-splash-style">`; the development server gives
// them Vite's nonce instead, which the host replaces with each document's own.

/** The window colours Program.cs declares, `#rrggbb`: `theme.json` in the generated package. */
export interface ThemeColors {
  readonly light: string;
  readonly dark: string;
}

/**
 * The splash's timing, in milliseconds (plan section 3d's defaults). The mark appears `delay` after the page's first
 * frame, through a CSS animation delay, so a start faster than that never shows it; once visible it stays at least
 * `minimumVisible`, then fades out over `fadeOut`.
 */
export interface SplashTiming {
  /** From the page's first frame to the mark's appearance: 350 by default. */
  readonly delay: number;
  /** The least time the mark stays once it has appeared: 400 by default. */
  readonly minimumVisible: number;
  /** The mark's fade-in: 120 by default. */
  readonly fadeIn: number;
  /** The cover's fade-out once the app is ready: 150 by default. */
  readonly fadeOut: number;
}

/** The element the app writes its splash in, in `index.html`. */
export const splashId = "spar-splash";

/** The colours of a window that declares none, as the host paints it. */
export const defaultColors: ThemeColors = { light: "#ffffff", dark: "#1f1f1f" };

/** Section 3d's measured defaults. */
export const defaultTiming: SplashTiming = { delay: 350, minimumVisible: 400, fadeIn: 120, fadeOut: 150 };

const colour = /^#[0-9a-f]{6}$/;

/**
 * The theme style: the window's colours as the page's own background, in the scheme `prefers-color-scheme` reports
 * (which the host sets from the app's choice or the system's), and `color-scheme` for form controls and scrollbars.
 * `:where()` keeps it below any rule of the app's, which always wins.
 */
export function themeStyle(colors: ThemeColors): string {
  return `:where(html){color-scheme:light dark;background-color:${colors.light}}` +
    `@media (prefers-color-scheme:dark){:where(html){background-color:${colors.dark}}}`;
}

/**
 * The splash's style: an opaque cover in the theme colour from the first frame, whose children (the app's mark) fade
 * in after the delay; under reduced motion they appear at once when the delay ends. The SDK reads the minimum and the
 * fade-out from the custom properties when the app is ready.
 */
export function splashStyle(colors: ThemeColors, timing: SplashTiming): string {
  const cover = "position:fixed;inset:0;z-index:2147483647;display:flex;flex-direction:column;align-items:center;" +
    `justify-content:center;gap:16px;background-color:${colors.light};` +
    `--spar-splash-minimum:${timing.minimumVisible};--spar-splash-fade-out:${timing.fadeOut}`;
  return `#${splashId}{${cover}}` +
    `@media (prefers-color-scheme:dark){#${splashId}{background-color:${colors.dark}}}` +
    `#${splashId}>*{opacity:0;animation:spar-splash-in ${timing.fadeIn}ms ease-out ${timing.delay}ms both}` +
    "@keyframes spar-splash-in{from{opacity:0}to{opacity:1}}" +
    `@media (prefers-reduced-motion:reduce){#${splashId}>*{animation-timing-function:step-start}}`;
}

/** Whether the HTML has an element with the splash's id, outside comments (the template's example is one). */
export function hasSplash(html: string): boolean {
  const markup = html.replace(/<!--[\s\S]*?-->/g, "");
  return new RegExp(`<[a-z][a-z0-9-]*\\b[^>]*\\sid\\s*=\\s*["']?${splashId}["'\\s>/]`, "i").test(markup);
}

/**
 * The page with its start-up styles: `<meta name="color-scheme">` unless it has one, the theme style, and, when it
 * has a splash element, the splash style and `aria-busy="true"` on `<body>` until the SDK removes the splash. They go
 * early in `<head>` (`insertionPoint`), before anything that could paint.
 */
export function addPageStyle(html: string, colors: ThemeColors, timing: SplashTiming): string {
  const splash = hasSplash(html);
  let added = "";
  if (!/<meta\b[^>]*\bname\s*=\s*["']?color-scheme\b/i.test(html)) {
    added += '<meta name="color-scheme" content="light dark">';
  }

  added += `<style id="spar-theme">${themeStyle(colors)}</style>`;
  if (splash) {
    added += `<style id="spar-splash-style">${splashStyle(colors, timing)}</style>`;
  }

  const at = insertionPoint(html);
  const withStyles = html.slice(0, at) + added + html.slice(at);
  return splash ? markBusy(withStyles) : withStyles;
}

/**
 * Just after `<meta charset>`, which must stay within the document's first 1024 bytes, else just after `<head>`; at
 * the start (after the doctype) of a document without either. Either way before every stylesheet and script.
 */
function insertionPoint(html: string): number {
  const charset = /<meta\b[^>]*\bcharset\s*=[^>]*>/i.exec(html);
  if (charset !== null) {
    return charset.index + charset[0].length;
  }

  const head = /<head\b[^>]*>/i.exec(html);
  return head === null ? afterDoctype(html) : head.index + head[0].length;
}

/** Reads `theme.json`'s text; the defaults when it is absent or not what the build writes. */
export function readThemeColors(text: string | undefined): ThemeColors {
  if (text === undefined) {
    return defaultColors;
  }

  const parsed: unknown = JSON.parse(text);
  const light = typeof parsed === "object" && parsed !== null ? Reflect.get(parsed, "light") : undefined;
  const dark = typeof parsed === "object" && parsed !== null ? Reflect.get(parsed, "dark") : undefined;
  if (typeof light !== "string" || typeof dark !== "string" || !colour.test(light) || !colour.test(dark)) {
    throw new Error(`Spar: theme.json holds no window colours of the form #rrggbb: ${text}`);
  }

  return { light, dark };
}

/** The splash timing from the plugin's options, each a whole number of milliseconds from 0 to 10,000. */
export function splashTiming(options: Partial<SplashTiming> | undefined): SplashTiming {
  const timing = { ...defaultTiming, ...definedOnly(options) };
  for (const [name, value] of Object.entries(timing)) {
    if (!Number.isInteger(value) || value < 0 || value > 10_000) {
      throw new Error(`Spar: splash.${name} must be a whole number of milliseconds from 0 to 10000, not ${String(value)}.`);
    }
  }

  return timing;
}

function definedOnly(options: Partial<SplashTiming> | undefined): Partial<SplashTiming> {
  const kept: { -readonly [K in keyof SplashTiming]?: number } = {};
  for (const key of ["delay", "minimumVisible", "fadeIn", "fadeOut"] as const) {
    const value = options?.[key];
    if (value !== undefined) {
      kept[key] = value;
    }
  }

  return kept;
}

function afterDoctype(html: string): number {
  const doctype = /^\s*<!doctype[^>]*>/i.exec(html);
  return doctype === null ? 0 : doctype[0].length;
}

function markBusy(html: string): string {
  const body = /<body\b([^>]*)>/i.exec(html);
  if (body === null || /\saria-busy\s*=/i.test(body[1]!)) {
    return html;
  }

  const end = body.index + body[0].length - 1;
  return `${html.slice(0, end)} aria-busy="true"${html.slice(end)}`;
}
