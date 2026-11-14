// Prototype of the Spar Vite plugin's theme step (spike only).
//
// At build time it adds to each HTML entry the app's first-frame theme, so the page agrees with the host's window
// colour before any script runs:
//   - <meta name="color-scheme" content="light dark">;
//   - a small <style> with the declared light and dark background colours;
//   - optionally the splash: its <style>, its static markup (first child of <body>) and aria-busy on <body>.
// After the HTML files are written it hashes every inline <style> exactly as emitted and writes the CSP sources
// ('sha256-…') to a JSON file for the host, which adds them to style-src. Spar would write that file under obj/ and
// carry it in the generated catalog; the spike writes it next to the bundle.

import { createHash } from "node:crypto";
import { existsSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import type { Plugin, ResolvedConfig } from "vite";

export interface SparSplashOptions {
  /** Time before the splash may become visible; a start faster than this never shows it. */
  readonly delayMs: number;
  /** Once visible, the splash stays at least this long (read by the SDK from data-min-visible). */
  readonly minVisibleMs: number;
  /** Fade-in duration after the delay. */
  readonly fadeInMs: number;
  /** Fade-out duration used by the SDK (data-fade-out). */
  readonly fadeOutMs: number;
  /** Spike only: the first design (transparent until the delay). */
  readonly v1?: boolean;
}

export interface SparThemeOptions {
  readonly light: string;
  readonly dark: string;
  readonly lightText: string;
  readonly darkText: string;
  /** Spike: which parts each HTML entry gets (the real plugin has one index.html). */
  readonly page: (fileName: string) => { theme: boolean; splash?: SparSplashOptions };
  /** Written after the build, relative to build.outDir. */
  readonly cspFile: string;
}

const colour = /^#[0-9a-fA-F]{6}$/;

export function themeStyle(o: SparThemeOptions): string {
  // One line, no comments: the hash covers these exact bytes.
  return `:root{color-scheme:light dark;background-color:${o.light};color:${o.lightText}}` +
    `@media (prefers-color-scheme:dark){:root{background-color:${o.dark};color:${o.darkText}}}`;
}

/**
 * Splash v2 (the default): an opaque cover in the background colour from the first frame, so nothing of the app shows
 * before ready(); only the mark fades in after the delay. A fast start never shows the mark, and the app appears in
 * one step when the cover goes.
 */
export function splashStyle(o: SparThemeOptions, s: SparSplashOptions): string {
  return `#spar-splash{position:fixed;inset:0;z-index:2147483647;display:flex;align-items:center;justify-content:center;` +
    `background-color:${o.light}}` +
    `@media (prefers-color-scheme:dark){#spar-splash{background-color:${o.dark}}}` +
    `#spar-splash .spar-splash-mark{width:96px;height:96px;border-radius:20px;background-color:#e8a33d;opacity:0;` +
    `animation:spar-splash-in ${s.fadeInMs}ms ease-out ${s.delayMs}ms forwards,spar-splash-pulse 900ms ease-in-out ${s.delayMs}ms infinite alternate}` +
    `@keyframes spar-splash-in{from{opacity:0}to{opacity:1}}` +
    `@keyframes spar-splash-pulse{from{transform:scale(1)}to{transform:scale(.9)}}` +
    // Reduced motion: the delay stays (it prevents flashes); the fade and the pulse go.
    `@media (prefers-reduced-motion:reduce){#spar-splash .spar-splash-mark{animation:spar-splash-in 1ms step-end ${s.delayMs}ms forwards}}`;
}

/** Splash v1 (measured first, kept for comparison): the whole element is transparent until the delay. */
export function splashStyleV1(o: SparThemeOptions, s: SparSplashOptions): string {
  return `#spar-splash{position:fixed;inset:0;z-index:2147483647;display:flex;align-items:center;justify-content:center;` +
    `background-color:${o.light};opacity:0;pointer-events:none;animation:spar-splash-in ${s.fadeInMs}ms ease-out ${s.delayMs}ms forwards}` +
    `@media (prefers-color-scheme:dark){#spar-splash{background-color:${o.dark}}}` +
    `#spar-splash .spar-splash-mark{width:96px;height:96px;border-radius:20px;background-color:#e8a33d;` +
    `animation:spar-splash-pulse 900ms ease-in-out infinite alternate}` +
    `@keyframes spar-splash-in{from{opacity:0}to{opacity:1}}` +
    `@keyframes spar-splash-pulse{from{transform:scale(1)}to{transform:scale(.9)}}` +
    // Reduced motion: the delay stays (it prevents flashes), the fade and the pulse go.
    `@media (prefers-reduced-motion:reduce){#spar-splash{animation-duration:1ms;animation-timing-function:step-end}` +
    `#spar-splash .spar-splash-mark{animation:none}}`;
}

export function sparTheme(options: SparThemeOptions): Plugin {
  for (const value of [options.light, options.dark, options.lightText, options.darkText]) {
    if (!colour.test(value)) throw new Error(`spar-theme: colours must be #rrggbb constants, got '${value}'.`);
  }

  let config: ResolvedConfig;
  return {
    name: "spar-theme",
    configResolved(resolved) {
      config = resolved;
    },
    transformIndexHtml: {
      order: "post",
      handler(html, context) {
        const fileName = context.filename.replace(/\\/g, "/").split("/").pop()!;
        const parts = options.page(fileName);
        if (!parts.theme) return html;

        // Right after the charset meta (which must stay inside the first 1024 bytes), before any stylesheet or script.
        let head = `\n  <meta name="color-scheme" content="light dark">\n  <style id="spar-theme">${themeStyle(options)}</style>`;
        if (parts.splash) {
          const s = parts.splash;
          head += `\n  <style id="spar-splash-style">${s.v1 ? splashStyleV1(options, s) : splashStyle(options, s)}</style>`;
          const markup = `<div id="spar-splash" role="status" aria-label="Loading" data-min-visible="${s.minVisibleMs}" ` +
            `data-fade-out="${s.fadeOutMs}"><div class="spar-splash-mark"></div></div>`;
          html = html.replace(/<body([^>]*)>/, `<body$1 aria-busy="true">\n  ${markup}`);
        }

        const charset = /<meta\s+charset=[^>]*>/i;
        if (!charset.test(html)) throw new Error(`spar-theme: ${fileName} needs <meta charset="utf-8">.`);
        return html.replace(charset, (meta) => meta + head);
      },
    },
    writeBundle(_, bundle) {
      // Hash what was actually emitted, not what we meant to emit.
      const sources = new Set<string>();
      const perPage: Record<string, string[]> = {};
      for (const [fileName, output] of Object.entries(bundle)) {
        if (output.type !== "asset" || !fileName.endsWith(".html")) continue;
        const html = typeof output.source === "string" ? output.source : new TextDecoder().decode(output.source);
        const hashes: string[] = [];
        for (const match of html.matchAll(/<style\b[^>]*>([\s\S]*?)<\/style>/g)) {
          const hash = `'sha256-${createHash("sha256").update(match[1], "utf8").digest("base64")}'`;
          hashes.push(hash);
          sources.add(hash);
        }
        perPage[fileName] = hashes;
      }

      const file = join(config.root, config.build.outDir, options.cspFile);
      const content = JSON.stringify({ styleSrc: [...sources].sort(), pages: perPage }, null, 2) + "\n";
      // Only when the content changes, so an unchanged frontend build gives the host build no new input.
      if (!existsSync(file) || readFileSync(file, "utf8") !== content) writeFileSync(file, content);
    },
  };
}
