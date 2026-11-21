// @spar/app/vite: the Vite plugin every Spar frontend uses (docs/app-model.md section 1). For a production build it
// sets what the default Content Security Policy needs (plan section 3b): no asset inlined as a `data:` URL, which the
// policy blocks, no source maps, which never ship (plan section 3c), and pages served from the root of the app's own
// origin; and it collects the bundled dependencies' licences for the executable's third-party notices (plan 3c).
// It also applies Subresource Integrity to the built page (`integrity.ts`), unless Program.cs turned it off, and fails
// on an external script or stylesheet without a hash, in every build and on the development server. The page security
// Program.cs declares reaches it as `csp.json`, which the build writes next to this package. On the development server
// (`vite`, which a Debug build's app starts with `npm run dev`), it sets what the app's origin needs to serve the page
// from it (`devServer.ts`); the app's host sends the policy header, on every response, worker scripts included.
import { readFileSync, writeFileSync } from "node:fs";
import { join, resolve } from "node:path";
import type { ConfigEnv, Plugin, ResolvedConfig, UserConfig, ViteDevServer } from "vite";
import { attachLaunch, devServerConfig, readLaunch } from "./devServer.js";
import { addIntegrity, missingIntegrity, missingIntegrityMessage } from "./integrity.js";

/** The parts of `csp.json` this plugin reads; the build writes it from `spar.ContentSecurity(…)` in Program.cs. */
interface PageSecurity {
  /** False after `csp.WithoutIntegrity()`. */
  readonly integrity: boolean;
}

/** Spar's Vite plugin: `plugins: [react(), spar()]` in `vite.config.ts`. */
export default function spar(): Plugin {
  let outDir = "";
  return {
    name: "spar",
    config(user: UserConfig, env: ConfigEnv): UserConfig {
      // The development server only: not a preview of a build, and not Vitest, which loads the same config.
      if (env.command === "serve" && !env.isPreview && env.mode !== "test") {
        return { base: "/", ...devServerConfig(user, readLaunch(process.env)) };
      }

      return {
        base: "/",
        build: {
          // A small asset would otherwise become a data: URL, which img-src 'self' and font-src 'self' block.
          assetsInlineLimit: 0,
          sourcemap: false,
          // The engines Spar supports (WebView2, WebKitGTK 2.48 and newer) all run ES2022.
          target: "es2022",
          // The bundled dependencies' licences, which the packing step embeds as the app's third-party notices.
          license: true,
        },
      };
    },
    configResolved(config: ResolvedConfig): void {
      outDir = resolve(config.root, config.build.outDir);
    },
    configureServer(server: ViteDevServer): void {
      const launch = readLaunch(process.env);
      if (launch !== undefined) {
        attachLaunch(server, launch);
      }
    },
    transformIndexHtml: {
      order: "pre",
      handler(html: string, context: { readonly path: string }): string {
        checkExternals(context.path, html);
        return html;
      },
    },
    writeBundle(_options, bundle): void {
      if (!readPageSecurity().integrity) {
        return;
      }

      const files = new Map<string, Uint8Array | string>();
      const lazyChunks: string[] = [];
      for (const output of Object.values(bundle)) {
        files.set(output.fileName, output.type === "chunk" ? output.code : output.source);
        if (output.type === "chunk") {
          lazyChunks.push(output.fileName);
        }
      }

      const readFile = (path: string): Uint8Array | string | undefined => files.get(path) ?? readFromDisk(outDir, path);
      for (const [fileName, content] of files) {
        if (!fileName.endsWith(".html")) {
          continue;
        }

        const html = typeof content === "string" ? content : new TextDecoder().decode(content);
        checkExternals(fileName, html);
        const result = addIntegrity(html, readFile, lazyChunks);
        // In place: Vite has written the bundle, and the packing step reads the folder after this build.
        writeFileSync(join(outDir, fileName), result.html);
      }
    },
  };
}

/** Fails on an external script or stylesheet without a hash, unless Program.cs turned integrity off. */
function checkExternals(page: string, html: string): void {
  const missing = missingIntegrity(html);
  if (missing.length > 0 && readPageSecurity().integrity) {
    throw new Error(missingIntegrityMessage(page, missing));
  }
}

/**
 * `csp.json`, read on every use, relative to this file: the plugin is `sdk/vite/index.js` in the generated package and
 * the file is at the package's root. Without it (the SDK's own tests) the defaults apply.
 */
function readPageSecurity(): PageSecurity {
  let text: string;
  try {
    text = readFileSync(new URL("../../csp.json", import.meta.url), "utf8");
  } catch {
    return { integrity: true };
  }

  const parsed: unknown = JSON.parse(text);
  const integrity = typeof parsed === "object" && parsed !== null ? Reflect.get(parsed, "integrity") : undefined;
  return { integrity: integrity !== false };
}

/** A file the bundle did not produce but the page loads from the app (Vite's public folder). */
function readFromDisk(outDir: string, path: string): Uint8Array | undefined {
  try {
    return readFileSync(join(outDir, path));
  } catch {
    return undefined;
  }
}
