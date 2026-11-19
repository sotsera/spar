import { createHash } from "node:crypto";
import { describe, expect, test } from "vitest";
import {
  addIntegrity,
  inlineScriptHash,
  integrityOf,
  isExternal,
  loadingTags,
  missingIntegrity,
  missingIntegrityMessage,
} from "../src/vite/integrity.js";

const sha384 = (text: string): string => `sha384-${createHash("sha384").update(text).digest("base64")}`;

const built = `<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8">
    <title>Notebook</title>
    <script type="module" crossorigin src="/assets/index-a1.js"></script>
    <link rel="modulepreload" crossorigin href="/assets/vendor-b2.js">
    <link rel="stylesheet" crossorigin href="/assets/index-c3.css">
    <link rel="icon" href="/favicon.svg">
  </head>
  <body><div id="root"></div></body>
</html>`;

const files: Record<string, string> = {
  "assets/index-a1.js": "import './vendor-b2.js'; import('./lazy-d4.js');",
  "assets/vendor-b2.js": "export const vendor = 1;",
  "assets/index-c3.css": "body { margin: 0 }",
  "assets/lazy-d4.js": "export const lazy = 2;",
  "favicon.svg": "<svg/>",
};

const readFile = (path: string): string | undefined => files[path];

describe("integrity of the built page", () => {
  test("the entry script, module preloads and stylesheets get sha384 of their final bytes; other links do not", () => {
    const result = addIntegrity(built, readFile, ["assets/index-a1.js", "assets/vendor-b2.js", "assets/lazy-d4.js"]);

    expect(result.html).toContain(`src="/assets/index-a1.js" integrity="${sha384(files["assets/index-a1.js"]!)}">`);
    expect(result.html).toContain(`href="/assets/vendor-b2.js" integrity="${sha384(files["assets/vendor-b2.js"]!)}">`);
    expect(result.html).toContain(`href="/assets/index-c3.css" integrity="${sha384(files["assets/index-c3.css"]!)}">`);
    expect(result.html).toContain(`<link rel="icon" href="/favicon.svg">`);
    expect(result.hashed).toBe(4);
  });

  test("lazy chunks are covered by an import map, first in head, whose text the hosts hash", () => {
    const result = addIntegrity(built, readFile, ["assets/index-a1.js", "assets/vendor-b2.js", "assets/lazy-d4.js"]);

    const map = `{"imports":{},"integrity":{"/assets/lazy-d4.js":"${sha384(files["assets/lazy-d4.js"]!)}"}}`;
    expect(result.importMap).toBe(map);
    expect(result.html).toContain(`<head><script type="importmap">${map}</script>\n    <meta charset="utf-8">`);
    expect(inlineScriptHash(map)).toBe(`'sha256-${createHash("sha256").update(map).digest("base64")}'`);
  });

  test("a page without lazy chunks gets no import map, so no inline script to allow", () => {
    const result = addIntegrity(built, readFile, ["assets/index-a1.js", "assets/vendor-b2.js"]);

    expect(result.importMap).toBeUndefined();
    expect(result.html).not.toContain("importmap");
  });

  test("tags that carry integrity, and external ones, are left as they are", () => {
    const html = `<head><script src="/assets/index-a1.js" integrity="sha384-own"></script>` +
      `<script src="https://cdn.example.com/x.js" integrity="sha384-cdn" crossorigin="anonymous"></script></head>`;

    const result = addIntegrity(html, readFile, []);

    expect(result.html).toBe(html);
    expect(result.hashed).toBe(0);
  });

  test("a local file the build did not produce cannot get a hash, and fails the build", () => {
    expect(() => addIntegrity(`<script src="/missing.js"></script>`, readFile, [])).toThrow(/did not produce/);
  });

  test("hashes bytes as well as text", () => {
    expect(integrityOf(new TextEncoder().encode("abc"))).toBe(sha384("abc"));
  });
});

describe("external scripts and stylesheets", () => {
  test("need integrity: scripts, stylesheets and module preloads on another host", () => {
    const html = `
      <script src="https://cdn.example.com/a.js"></script>
      <script src="//cdn.example.com/b.js"></script>
      <link rel="stylesheet" href="https://cdn.example.com/c.css">
      <link rel="modulepreload" href="https://cdn.example.com/d.js">
      <script src="https://cdn.example.com/e.js" integrity="sha384-x" crossorigin="anonymous"></script>
      <script src="/assets/local.js"></script>`;

    expect(missingIntegrity(html)).toEqual([
      "https://cdn.example.com/a.js",
      "//cdn.example.com/b.js",
      "https://cdn.example.com/c.css",
      "https://cdn.example.com/d.js",
    ]);
  });

  test("fonts are exempt, since they cannot carry SRI", () => {
    const html = `<link rel="preload" as="font" href="https://fonts.gstatic.com/x.woff2" crossorigin>
      <link rel="preconnect" href="https://fonts.gstatic.com">`;

    expect(missingIntegrity(html)).toEqual([]);
  });

  test("attributes are read in any quoting and order, and in upper case", () => {
    const tags = loadingTags(`<LINK HREF='https://x.example/s.css' REL=stylesheet><script defer src=/a.js></script>`);

    expect(tags.map(tag => [tag.element, tag.address])).toEqual([["link", "https://x.example/s.css"], ["script", "/a.js"]]);
  });

  test("the message names each file and the three ways out", () => {
    const message = missingIntegrityMessage("index.html", ["https://cdn.example.com/a.js"]);

    expect(message).toContain("index.html");
    expect(message).toContain("  https://cdn.example.com/a.js");
    expect(message).toContain("Bundle the file");
    expect(message).toContain("integrity=");
    expect(message).toContain("csp.WithoutIntegrity()");
  });

  test("only absolute and protocol-relative addresses are external", () => {
    expect([isExternal("https://a.example/x"), isExternal("//a.example/x"), isExternal("/x"), isExternal("x.js")])
      .toEqual([true, true, false, false]);
  });
});
