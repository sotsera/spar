// The stylesheet of what the SDK draws in the page (decision "M3d's decide-first questions", item 6): the page-drawn
// title bar's drag area and caption buttons (docs/app-model.md 6.9). The Vite plugin adds it to every page as
// `/spar.css`, linked right after the theme style, so the app's own stylesheets come later and win; every rule is
// inside `:where()`, with no specificity, so any rule of the app's overrides it. It is themed by CSS variables, set on
// `:root` or on the title bar:
// - `--spar-caption-color`: the buttons' icons (the bar's own colour by default);
// - `--spar-caption-hover`, `--spar-caption-pressed`: a button's background under the pointer and pressed;
// - `--spar-caption-close-hover`, `--spar-caption-close-hover-color`: the close button's, red on Windows;
// - `--spar-caption-width`: a Windows button's width (46px; the bar's height gives its height).
// The buttons look as the OS's do: flat and full height on Windows, round on Linux (`data-spar-platform` on `<html>`).

/** The file the plugin adds, at the root of the page's origin. */
export const sdkStyleFile = "spar.css";

/**
 * The elements inside a drag area that stay the page's; `src/titleBar.ts` measures the same ones for the host (a test
 * keeps the two lists equal).
 */
export const noDragSelector =
  "a[href], button, input, select, textarea, label, summary, [contenteditable]:not([contenteditable=\"false\"]), " +
  "[tabindex], [role=\"button\"], [role=\"link\"], [role=\"tab\"], [role=\"menuitem\"], [role=\"checkbox\"], " +
  "[role=\"switch\"], [data-spar-no-drag], [data-spar-caption]";

const button = ":where([data-spar-caption-button])";

const windowsButton = ":where(html[data-spar-platform=\"windows\"] [data-spar-caption-button])";

const linuxButton = ":where(html[data-spar-platform=\"linux\"] [data-spar-caption-button])";

/** The stylesheet's text. */
export const sdkStyleSheet = [
  "/* Spar: the page-drawn title bar (docs/app-model.md 6.9). */",
  ":where(:root){--spar-caption-hover:rgb(0 0 0/.06);--spar-caption-pressed:rgb(0 0 0/.1);" +
    "--spar-caption-close-hover:#c42b1c;--spar-caption-close-hover-color:#fff;--spar-caption-width:46px}",
  "@media (prefers-color-scheme:dark){:where(:root){--spar-caption-hover:rgb(255 255 255/.08);" +
    "--spar-caption-pressed:rgb(255 255 255/.12)}}",
  ":where([data-spar-titlebar]){-webkit-app-region:drag;app-region:drag;-webkit-user-select:none;user-select:none}",
  `:where([data-spar-titlebar]) :where(${noDragSelector}){-webkit-app-region:no-drag;app-region:no-drag}`,
  ":where([data-spar-caption=\"\"]){display:flex;flex:none;align-self:stretch;align-items:center}",
  `${button}{display:inline-flex;align-items:center;justify-content:center;margin:0;padding:0;border:0;` +
    "background:transparent;color:var(--spar-caption-color,inherit);font:inherit;cursor:default;outline-offset:-2px}",
  `${button} :where(svg){display:block;pointer-events:none}`,
  ":where([data-spar-icon=\"restore\"]){display:none}",
  ":where(html[data-spar-window=\"maximized\"]) :where([data-spar-icon=\"maximize\"]){display:none}",
  ":where(html[data-spar-window=\"maximized\"]) :where([data-spar-icon=\"restore\"]){display:block}",
  `${windowsButton}{align-self:stretch;width:var(--spar-caption-width)}`,
  `${windowsButton}:where(:hover,[data-spar-hover]){background:var(--spar-caption-hover)}`,
  `${windowsButton}:where(:active,[data-spar-pressed]){background:var(--spar-caption-pressed)}`,
  `:where(html[data-spar-platform="windows"] [data-spar-caption-button="close"]):where(:hover,[data-spar-hover],:active,[data-spar-pressed])` +
    "{background:var(--spar-caption-close-hover);color:var(--spar-caption-close-hover-color)}",
  `${linuxButton}{width:24px;height:24px;margin:0 3px;border-radius:50%;background:var(--spar-caption-hover)}`,
  `${linuxButton}:where(:hover){background:var(--spar-caption-pressed)}`,
  `${linuxButton}:where(:active){background:var(--spar-caption-pressed);filter:brightness(.9)}`,
  `${linuxButton} :where(svg){width:8px;height:8px}`,
  "",
].join("\n");

/**
 * The page with the stylesheet linked right after the theme style the plugin adds before it (`pageStyle.ts`), so it
 * comes before the app's own stylesheets; a page without the theme style gets it at the start of its `<head>`.
 */
export function addSdkStyle(html: string): string {
  const link = `<link rel="stylesheet" href="/${sdkStyleFile}">`;
  const theme = html.indexOf("<style id=\"spar-theme\">");
  const themeEnd = theme < 0 ? -1 : html.indexOf("</style>", theme);
  if (themeEnd >= 0) {
    const at = themeEnd + "</style>".length;
    return html.slice(0, at) + link + html.slice(at);
  }

  const head = /<head[^>]*>/i.exec(html);
  if (head !== null) {
    const at = head.index + head[0].length;
    return html.slice(0, at) + link + html.slice(at);
  }

  return link + html;
}
