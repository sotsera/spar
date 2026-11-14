// Spike version of the Spar Vite plugin's security part.
// - Reads the policy the build extracted from C# (csp.json), relative to this file, on every page load.
// - Dev: sends the same policy as a header on every response, adding only a per-request nonce (Vite's html.cspNonce
//   placeholder, replaced in the HTML) and, if asked, the HMR WebSocket in connect-src; plus the other headers.
// - Dev: answers POST /_spar/log (the spike's result channel) into a JSONL file.
// - Build: assetsInlineLimit 0 (no data: URLs), and optionally Subresource Integrity on the entry script, CSS and
//   modulepreload links plus an import map with integrity for the other chunks, computed from the written files.
import { createHash, randomBytes } from "node:crypto";
import { appendFileSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import type { Plugin } from "vite";

export interface SparSecurityOptions {
  /** csp.json written by the Spar build from the C# declaration. */
  readonly cspFile: string;
  /** Adds require-trusted-types-for 'script' (dev and the policy file for the hosts). */
  readonly trustedTypes?: boolean;
  /** Adds the HMR WebSocket origin to connect-src in dev. */
  readonly hmrConnect?: boolean;
  /** Adds integrity attributes and an integrity import map to the built HTML. */
  readonly sri?: boolean;
  /** Keeps Vite's default assetsInlineLimit (for comparison only). */
  readonly keepInlineLimit?: boolean;
  /** Where dev-server page results go. */
  readonly logFile?: string;
}

interface PolicyFile {
  readonly contentSecurityPolicy: string;
  readonly permissionsPolicy: string;
}

// Same length as the per-request nonce (18 random bytes in base64), so Content-Length stays right.
const placeholder = "SPARNONCEPLACEHOLDER0000";

function readPolicy(file: string): PolicyFile {
  return JSON.parse(readFileSync(file, "utf8")) as PolicyFile;
}

function withSources(policy: string, directive: string, sources: string): string {
  const parts = policy.split(";").map(p => p.trim());
  const index = parts.findIndex(p => p === directive || p.startsWith(directive + " "));
  if (index < 0) return [...parts, `${directive} ${sources}`].join("; ");
  parts[index] = `${parts[index]} ${sources}`;
  return parts.join("; ");
}

export function sparSecurity(options: SparSecurityOptions): Plugin {
  return {
    name: "spar-security",
    config(_, env) {
      return {
        build: options.keepInlineLimit ? {} : { assetsInlineLimit: 0 },
        html: env.command === "serve" ? { cspNonce: placeholder } : {},
      };
    },
    configureServer(server) {
      server.middlewares.use((req, res, next) => {
        if (req.url?.startsWith("/_spar/log") && req.method === "POST") {
          let body = "";
          req.on("data", chunk => (body += chunk));
          req.on("end", () => {
            const line = JSON.stringify({ at: Date.now(), ua: req.headers["user-agent"]?.slice(0, 40), message: JSON.parse(body) });
            if (options.logFile) appendFileSync(options.logFile, line + "\n");
            console.log("SPAR_DEV_LOG " + body.slice(0, 300));
            res.statusCode = 200;
            res.end("ok");
          });
          return;
        }

        const nonce = randomBytes(18).toString("base64");
        const file = readPolicy(options.cspFile);
        let csp = file.contentSecurityPolicy;
        csp = withSources(csp, "script-src", `'nonce-${nonce}'`);
        csp = withSources(csp, "style-src", `'nonce-${nonce}'`);
        if (options.hmrConnect) csp = withSources(csp, "connect-src", `ws://${req.headers.host}`);
        if (options.trustedTypes) csp += "; require-trusted-types-for 'script'";
        // SPAR_NO_CSP=1: comparison runs without the policy header (spike only).
        if (process.env.SPAR_NO_CSP !== "1") res.setHeader("Content-Security-Policy", csp);
        res.setHeader("X-Content-Type-Options", "nosniff");
        res.setHeader("Referrer-Policy", "no-referrer");
        res.setHeader("Cross-Origin-Opener-Policy", "same-origin");
        res.setHeader("Cross-Origin-Resource-Policy", "same-origin");
        res.setHeader("Permissions-Policy", file.permissionsPolicy);

        // The page must never be revalidated: Vite's ETag is computed before the nonce is swapped in, so a 304 would
        // reuse a body whose nonce no longer matches the new header (observed in WebView2: Vite's styles blocked).
        if (req.headers.accept?.includes("text/html")) delete req.headers["if-none-match"];

        // Only HTML carries the placeholder; swap in this request's nonce.
        const end = res.end.bind(res) as (...args: unknown[]) => unknown;
        (res as unknown as { end: (...args: unknown[]) => unknown }).end = (chunk?: unknown, ...rest: unknown[]) => {
          const html = String(res.getHeader("Content-Type") ?? "").includes("text/html");
          if (html) {
            res.removeHeader("ETag");
            res.setHeader("Cache-Control", "no-store");
          }
          if (typeof chunk === "string" && chunk.includes(placeholder)) chunk = chunk.replaceAll(placeholder, nonce);
          else if (Buffer.isBuffer(chunk) && chunk.includes(placeholder)) {
            chunk = Buffer.from(chunk.toString("utf8").replaceAll(placeholder, nonce), "utf8");
          }
          return end(chunk, ...rest);
        };
        next();
      });
    },
    writeBundle(output, bundle) {
      if (!options.sri) return;
      const started = performance.now();
      const dir = output.dir!;
      const hash = (file: string, algorithm = "sha384") =>
        `${algorithm}-${createHash(algorithm).update(readFileSync(join(dir, file))).digest("base64")}`;
      let hashed = 0;
      for (const name of Object.keys(bundle).filter(n => n.endsWith(".html"))) {
        let html = readFileSync(join(dir, name), "utf8");
        const referenced = new Set<string>();
        html = html.replace(/<(script|link)\b([^>]*?)\s(src|href)="\/([^"]+)"([^>]*)>/g, (tag, element, before, attribute, path, after) => {
          const isTarget = element === "script" || /rel="(stylesheet|modulepreload)"/.test(tag);
          if (!isTarget || tag.includes("integrity=")) return tag;
          referenced.add(path);
          hashed++;
          return `<${element}${before} ${attribute}="/${path}" integrity="${hash(path)}"${after}>`;
        });

        // Chunks loaded later (dynamic import) are covered through the import map's integrity section.
        const integrity: Record<string, string> = {};
        for (const chunk of Object.values(bundle)) {
          if (chunk.type === "chunk" && !referenced.has(chunk.fileName)) {
            integrity["/" + chunk.fileName] = hash(chunk.fileName);
            hashed++;
          }
        }

        const importMap = JSON.stringify({ imports: {}, integrity });
        html = html.replace(/<head>/i, `<head><script type="importmap">${importMap}</script>`);
        writeFileSync(join(dir, name), html);
        const mapHash = `sha256-${createHash("sha256").update(importMap).digest("base64")}`;
        writeFileSync(join(dir, "spar-sri.json"), JSON.stringify({ importMapHash: mapHash, files: hashed }, null, 2));
      }

      console.log(`spar-security: SRI for ${hashed} files in ${(performance.now() - started).toFixed(1)} ms`);
    },
  };
}
