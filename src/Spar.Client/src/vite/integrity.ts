// Subresource Integrity for the Release bundle (plan section 3b), and the check that every external script and
// stylesheet carries a hash of its own. Pure functions over the HTML text and the bundle's files, so the Vite plugin
// (index.ts) stays small and the rules are tested without a build.
import { createHash } from "node:crypto";

/** One tag the page loads code or styles with, as found in its HTML. */
export interface LoadingTag {
  /** `script` or `link`. */
  readonly element: string;
  /** The address it loads (`src` or `href`), as written. */
  readonly address: string;
  /** The tag's attributes, names in lower case. */
  readonly attributes: ReadonlyMap<string, string>;
  /** Where the tag starts and ends in the HTML. */
  readonly start: number;
  readonly end: number;
}

/** What a build of the page needs to know about one of its files, by its path from the page root (`assets/x.js`). */
export type ReadFile = (path: string) => Uint8Array | string | undefined;

/** An address on another host (`https://…`, `http://…` or `//…`), which the app does not serve itself. */
export function isExternal(address: string): boolean {
  return /^(?:[a-z][a-z0-9+.-]*:)?\/\//i.test(address);
}

/**
 * The tags that load scripts and stylesheets: `<script src>`, and `<link href>` whose `rel` is `stylesheet` or
 * `modulepreload`. Fonts (`<link rel="preload" as="font">`) cannot carry SRI and are not among them.
 */
export function loadingTags(html: string): LoadingTag[] {
  const tags: LoadingTag[] = [];
  for (const match of html.matchAll(/<(script|link)\b([^>]*)>/gi)) {
    const element = match[1]!.toLowerCase();
    const attributes = readAttributes(match[2]!);
    const address = element === "script" ? attributes.get("src") : attributes.get("href");
    if (address === undefined) {
      continue;
    }

    if (element === "link" && !relations(attributes).some(rel => rel === "stylesheet" || rel === "modulepreload")) {
      continue;
    }

    tags.push({ element, address, attributes, start: match.index, end: match.index + match[0].length });
  }

  return tags;
}

/**
 * The external scripts and stylesheets without an `integrity` attribute: each one fails the build, since its content
 * could change under the app with the page's full access (decision "Page security's public shape").
 */
export function missingIntegrity(html: string): string[] {
  return loadingTags(html)
    .filter(tag => isExternal(tag.address) && !tag.attributes.has("integrity"))
    .map(tag => tag.address);
}

/** The message for those tags, naming the fixes. */
export function missingIntegrityMessage(page: string, addresses: readonly string[]): string {
  const list = addresses.map(address => `  ${address}`).join("\n");
  return `Spar: ${page} loads code or styles from another host without an integrity hash:\n${list}\n` +
    "Bundle the file into the app, or add integrity=\"sha384-…\" and crossorigin=\"anonymous\" with the file's hash " +
    "(plan section 3b). csp.WithoutIntegrity() in Program.cs turns Subresource Integrity off; its documentation " +
    "explains the risk.";
}

/** `sha384-…` of a file's bytes, as `integrity` takes it. */
export function integrityOf(content: Uint8Array | string): string {
  return `sha384-${createHash("sha384").update(content).digest("base64")}`;
}

/** The result of adding integrity to one HTML page. */
export interface IntegrityResult {
  readonly html: string;
  /** How many tags and lazy chunks got a hash. */
  readonly hashed: number;
  /** The import map's text, when the page has lazy chunks to cover; its sha256 goes into `script-src`. */
  readonly importMap: string | undefined;
}

/**
 * Adds `integrity` to every script, stylesheet and module preload the page loads from the app itself, and an import
 * map with the hashes of the other chunks (`lazyChunks`, loaded later by `import()`), placed first in `<head>` so it
 * precedes every module script. The hosts hash the import map from the served HTML, exactly as it is written here
 * (`<script type="importmap">`), and name it in `script-src`. Tags that already carry `integrity` are left as they are.
 */
export function addIntegrity(html: string, readFile: ReadFile, lazyChunks: readonly string[]): IntegrityResult {
  let hashed = 0;
  const referenced = new Set<string>();
  let rewritten = "";
  let last = 0;
  for (const tag of loadingTags(html)) {
    if (isExternal(tag.address) || tag.attributes.has("integrity")) {
      continue;
    }

    const path = pagePath(tag.address);
    const content = readFile(path);
    if (content === undefined) {
      throw new Error(`Spar: the page loads ${tag.address}, which the build did not produce; it cannot be given a hash.`);
    }

    referenced.add(path);
    const opening = html.slice(tag.start, tag.end);
    const closing = opening.endsWith("/>") ? "/>" : ">";
    const withHash = `${opening.slice(0, -closing.length)} integrity="${integrityOf(content)}"${closing}`;
    rewritten += html.slice(last, tag.start) + withHash;
    last = tag.end;
    hashed++;
  }

  rewritten += html.slice(last);

  const integrity: Record<string, string> = {};
  for (const chunk of lazyChunks) {
    if (referenced.has(chunk)) {
      continue;
    }

    const content = readFile(chunk);
    if (content !== undefined) {
      integrity[`/${chunk}`] = integrityOf(content);
      hashed++;
    }
  }

  if (Object.keys(integrity).length === 0) {
    return { html: rewritten, hashed, importMap: undefined };
  }

  const importMap = JSON.stringify({ imports: {}, integrity });
  const element = `<script type="importmap">${importMap}</script>`;
  const head = /<head\b[^>]*>/i.exec(rewritten);
  const at = head === null ? 0 : head.index + head[0].length;
  return { html: rewritten.slice(0, at) + element + rewritten.slice(at), hashed, importMap };
}

/** `'sha256-…'` of an inline script's text, as `script-src` names it. */
export function inlineScriptHash(text: string): string {
  return `'sha256-${createHash("sha256").update(text).digest("base64")}'`;
}

/** A local address as a path from the page root: `/assets/x.js?v=1` → `assets/x.js`. */
function pagePath(address: string): string {
  const end = address.search(/[?#]/);
  const path = end < 0 ? address : address.slice(0, end);
  return path.replace(/^\.?\//, "");
}

function relations(attributes: ReadonlyMap<string, string>): string[] {
  return (attributes.get("rel") ?? "").toLowerCase().split(/\s+/).filter(rel => rel.length > 0);
}

function readAttributes(text: string): Map<string, string> {
  const attributes = new Map<string, string>();
  for (const match of text.matchAll(/([^\s"'=<>/]+)(?:\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'=<>`]+)))?/g)) {
    const name = match[1]!.toLowerCase();
    if (!attributes.has(name)) {
      attributes.set(name, match[2] ?? match[3] ?? match[4] ?? "");
    }
  }

  return attributes;
}
