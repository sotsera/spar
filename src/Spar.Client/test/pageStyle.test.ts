// The page side of start-up in the Vite plugin (plan section 3d): the theme style every page gets, the splash style
// only a page with `#spar-splash` gets, their exact opening tags (the host hashes from them), the timing options, and
// `theme.json`. What a host and a browser do with them is checked by the window tests (tests/Spar.WindowTests).
import { createHash } from "node:crypto";
import { describe, expect, test } from "vitest";
import spar from "../src/vite/index.js";
import {
  addPageStyle,
  defaultColors,
  defaultTiming,
  hasSplash,
  readThemeColors,
  splashStyle,
  splashTiming,
  themeStyle,
} from "../src/vite/pageStyle.js";

const colors = { light: "#ffffff", dark: "#1d1f24" };
const page = '<!doctype html><html><head><meta charset="utf-8"><title>App</title></head><body><div id="root"></div></body></html>';
const splashPage = page.replace('<div id="root">', '<div id="spar-splash" role="status" aria-label="Loading"><svg></svg></div><div id="root">');

/** The text between an opening tag and its `</style>`, as the host reads it. */
function styleText(html: string, openingTag: string): string | undefined {
  const start = html.indexOf(openingTag);
  if (start < 0) {
    return undefined;
  }

  const from = start + openingTag.length;
  return html.slice(from, html.indexOf("</style>", from));
}

describe("the theme style", () => {
  test("every page gets the meta and the style with both colours, first in its head", () => {
    const html = addPageStyle(page, colors, defaultTiming);

    expect(html).toContain('<meta charset="utf-8"><meta name="color-scheme" content="light dark"><style id="spar-theme">');
    expect(styleText(html, '<style id="spar-theme">')).toBe(themeStyle(colors));
    expect(themeStyle(colors)).toBe(
      ":where(html){color-scheme:light dark;background-color:#ffffff}@media (prefers-color-scheme:dark){:where(html){background-color:#1d1f24}}");
  });

  test("they go after the charset, which must stay in the first 1024 bytes, or first in the head without one", () => {
    const noCharset = page.replace('<meta charset="utf-8">', "");

    expect(addPageStyle(noCharset, colors, defaultTiming)).toContain('<head><meta name="color-scheme"');
    expect(addPageStyle("<p>bare</p>", colors, defaultTiming).startsWith('<meta name="color-scheme"')).toBe(true);
  });

  test("a page with its own color-scheme meta keeps it, and gets no second one", () => {
    const own = page.replace("<title>", '<meta name="color-scheme" content="light"><title>');

    const html = addPageStyle(own, colors, defaultTiming);

    expect(html.match(/name="color-scheme"/g)).toHaveLength(1);
    expect(html).toContain('content="light"');
  });

  test("a page without a splash gets no splash style and no aria-busy", () => {
    const html = addPageStyle(page, colors, defaultTiming);

    expect(html).not.toContain("spar-splash-style");
    expect(html).not.toContain("aria-busy");
  });

  test("its hash is the SHA-256 of the style's text, as the host computes it", () => {
    const html = addPageStyle(page, colors, defaultTiming);
    const text = styleText(html, '<style id="spar-theme">')!;

    const hash = createHash("sha256").update(text, "utf8").digest("base64");

    expect(hash).toMatch(/^[A-Za-z0-9+/]{43}=$/);
  });
});

describe("the splash", () => {
  test("is found by its id, and not by an id that only starts with it", () => {
    expect(hasSplash(splashPage)).toBe(true);
    expect(hasSplash('<p id=spar-splash>')).toBe(true);
    expect(hasSplash(page)).toBe(false);
    expect(hasSplash('<style id="spar-splash-style"></style>')).toBe(false);
    expect(hasSplash('<!-- <div id="spar-splash">An example</div> -->')).toBe(false);
  });

  test("a page with a splash gets its style after the theme's, and aria-busy on its body", () => {
    const html = addPageStyle(splashPage, colors, defaultTiming);

    expect(html).toContain('</style><style id="spar-splash-style">');
    expect(styleText(html, '<style id="spar-splash-style">')).toBe(splashStyle(colors, defaultTiming));
    expect(html).toContain('<body aria-busy="true">');
  });

  test("an aria-busy the app wrote stays as it is", () => {
    const busy = splashPage.replace("<body>", '<body class="app" aria-busy="false">');

    expect(addPageStyle(busy, colors, defaultTiming)).toContain('<body class="app" aria-busy="false">');
  });

  test("the style is an opaque cover in both colours whose children fade in after the delay", () => {
    const style = splashStyle(colors, defaultTiming);

    expect(style).toContain("#spar-splash{position:fixed;inset:0;z-index:2147483647;");
    expect(style).toContain("background-color:#ffffff;");
    expect(style).toContain("@media (prefers-color-scheme:dark){#spar-splash{background-color:#1d1f24}}");
    expect(style).toContain("#spar-splash>*{opacity:0;animation:spar-splash-in 120ms ease-out 350ms both}");
    expect(style).toContain("--spar-splash-minimum:400;--spar-splash-fade-out:150");
    expect(style).toContain("@media (prefers-reduced-motion:reduce){#spar-splash>*{animation-timing-function:step-start}}");
  });

  test("the timing comes from the plugin's options, each checked", () => {
    expect(splashTiming(undefined)).toEqual(defaultTiming);
    expect(splashTiming({ delay: 500, fadeOut: 0 })).toEqual({ ...defaultTiming, delay: 500, fadeOut: 0 });
    expect(splashStyle(colors, splashTiming({ delay: 500, fadeIn: 80, minimumVisible: 600 })))
      .toContain("animation:spar-splash-in 80ms ease-out 500ms both");
    expect(() => splashTiming({ delay: -1 })).toThrow(/splash.delay/);
    expect(() => splashTiming({ minimumVisible: 1.5 })).toThrow(/splash.minimumVisible/);
    expect(() => splashTiming({ fadeIn: 20_000 })).toThrow(/splash.fadeIn/);
  });
});

describe("theme.json", () => {
  test("gives the window colours, or the defaults without the file", () => {
    expect(readThemeColors('{"light":"#fafafa","dark":"#101010"}\n')).toEqual({ light: "#fafafa", dark: "#101010" });
    expect(readThemeColors(undefined)).toEqual(defaultColors);
  });

  test("anything but two #rrggbb colours fails the build", () => {
    expect(() => readThemeColors('{"light":"white","dark":"#101010"}')).toThrow(/theme.json/);
    expect(() => readThemeColors('{"light":"#fff","dark":"#101010"}')).toThrow(/theme.json/);
    expect(() => readThemeColors("[]")).toThrow(/theme.json/);
  });
});

describe("the plugin", () => {
  test("adds the styles after Vite's own transforms, so Vite neither rewrites them nor misses them with its nonce", () => {
    const plugin = spar({ splash: { delay: 500 } });
    const hook = plugin.transformIndexHtml as { order: string; handler: (html: string, context: { path: string }) => string };

    const html = hook.handler(splashPage, { path: "/index.html" });

    expect(hook.order).toBe("post");
    expect(html).toContain('<style id="spar-theme">');
    expect(html).toContain("ease-out 500ms both");
  });

  test("refuses splash options it cannot use when it is created", () => {
    expect(() => spar({ splash: { delay: Number.NaN } })).toThrow(/splash.delay/);
  });
});
