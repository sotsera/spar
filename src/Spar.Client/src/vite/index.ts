// @spar/app/vite: the Vite plugin every Spar frontend uses (docs/app-model.md section 1). For a production build it
// sets what the default Content Security Policy needs (plan section 3b): no asset inlined as a `data:` URL, which the
// policy blocks, no source maps, which never ship (plan section 3c), and pages served from the root of the app's own
// origin; and it collects the bundled dependencies' licences for the executable's third-party notices (plan 3c).
// It also applies Subresource Integrity to the built page (`integrity.ts`), unless Program.cs turned it off, and fails
// on an external script or stylesheet without a hash, in every build and on the development server. The page security
// Program.cs declares reaches it as `csp.json`, which the build writes next to this package. On the development server
// (`vite`, which a Debug build's app starts with `npm run dev`), it sets what the app's origin needs to serve the page
// from it (`devServer.ts`); the app's host sends the policy header, on every response, worker scripts included.
// Every page gets the theme style, from the window colours Program.cs declares (`theme.json`, written by the build like
// `csp.json`), and a page with a splash element gets the splash style (`pageStyle.ts`, plan section 3d).
// Every page also links the stylesheet of what the SDK draws, the page-drawn title bar's buttons (`sdkStyle.ts`), which
// a build emits as `spar.css` and the development server serves at the same path.
import { readFileSync, writeFileSync } from "node:fs";
import { join, resolve } from "node:path";
import type { ConfigEnv, Plugin, ResolvedConfig, Rolldown, UserConfig, ViteDevServer } from "vite";
import { attachLaunch, devServerConfig, readLaunch } from "./devServer.js";
import { addIntegrity, missingIntegrity, missingIntegrityMessage } from "./integrity.js";
import { addPageStyle, readThemeColors, splashTiming, type SplashTiming } from "./pageStyle.js";
import { addSdkStyle, sdkStyleFile, sdkStyleSheet } from "./sdkStyle.js";

export type { SplashTiming } from "./pageStyle.js";

/** The options of Spar's Vite plugin. */
export interface SparPluginOptions {
  /**
   * The splash's timing in milliseconds, for a page whose `index.html` has an element with the id `spar-splash`:
   * `delay` from the page's first frame to the mark's appearance (350), `minimumVisible` once it appeared (400),
   * `fadeIn` (120) and `fadeOut` (150). A page without the element gets no splash style.
   */
  readonly splash?: Partial<SplashTiming>;
}

/** The parts of `csp.json` this plugin reads; the build writes it from `spar.ContentSecurity(…)` in Program.cs. */
interface PageSecurity {
  /** False after `csp.WithoutIntegrity()`. */
  readonly integrity: boolean;
}

/** Spar's Vite plugin: `plugins: [react(), spar()]` in `vite.config.ts`, or `spar({ splash: { delay: 500 } })`. */
export default function spar(options: SparPluginOptions = {}): Plugin {
  const timing = splashTiming(options.splash);
  let outDir = "";
  return {
    name: "spar",
    config(user: UserConfig, env: ConfigEnv): UserConfig {
      // The development server only: not a preview of a build, and not Vitest, which loads the same config.
      if (env.command === "serve" && !env.isPreview && env.mode !== "test") {
        return { base: "/", define: telemetryDefine(), ...devServerConfig(user, readLaunch(process.env)) };
      }

      return {
        base: "/",
        define: telemetryDefine(),
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
      // The SDK's stylesheet, at the path the page links it from.
      server.middlewares.use((request, response, next) => {
        if (request.url?.split("?")[0] !== `/${sdkStyleFile}`) {
          next();
          return;
        }

        response.setHeader("Content-Type", "text/css; charset=utf-8");
        response.end(sdkStyleSheet);
      });
      if (launch !== undefined) {
        attachLaunch(server, launch);
      }
    },
    transformIndexHtml: {
      // After Vite's own transforms, so it never rewrites the styles, which the host hashes as written; Vite's nonce
      // (on the development server) comes after this.
      order: "post",
      handler(html: string, context: { readonly path: string }): string {
        checkExternals(context.path, html);
        return addSdkStyle(addPageStyle(html, readThemeColors(readPackageFile("theme.json")), timing));
      },
    },
    generateBundle(): void {
      this.emitFile({ type: "asset", fileName: sdkStyleFile, source: sdkStyleSheet });
    },
    writeBundle(_options, bundle): void {
      addTelemetryLicenses(outDir, bundle);
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
  const text = readPackageFile("csp.json");
  if (text === undefined) {
    return { integrity: true };
  }

  const parsed: unknown = JSON.parse(text);
  const integrity = typeof parsed === "object" && parsed !== null ? Reflect.get(parsed, "integrity") : undefined;
  return { integrity: integrity !== false };
}

/**
 * `import.meta.env.SPAR_TELEMETRY`, from `telemetry.json`: true when the app references Spar.OpenTelemetry. The SDK
 * starts the page's telemetry only then, and `vite build` drops that code, with the lazy chunk it imports, when false.
 */
function telemetryDefine(): Record<string, string> {
  const text = readPackageFile("telemetry.json");
  const parsed: unknown = text === undefined ? undefined : JSON.parse(text);
  const page = typeof parsed === "object" && parsed !== null && Reflect.get(parsed, "page") === true;
  return { "import.meta.env.SPAR_TELEMETRY": JSON.stringify(page) };
}

/**
 * The telemetry chunk comes prebundled, so Vite's licence file (`build.license`) does not see the OpenTelemetry
 * packages inside it: when the bundle has the chunk, their sections, which the SDK's build wrote beside it, are added.
 */
function addTelemetryLicenses(outDir: string, bundle: Rolldown.OutputBundle): void {
  const outputs = Object.values(bundle);
  const hasTelemetry = outputs.some(output =>
    output.type === "chunk" && output.moduleIds.some(id => id.replaceAll("\\", "/").endsWith("/sdk/telemetry/index.js")));
  const licenseFile = outputs.find(output => output.type === "asset" && output.fileName.endsWith("license.md"));
  if (!hasTelemetry || licenseFile === undefined) {
    return;
  }

  const sections = readPackageFile("sdk/telemetry/licenses.md");
  if (sections === undefined) {
    throw new Error("The page bundles Spar's telemetry chunk, but its licenses.md is missing from @spar/app.");
  }

  const path = join(outDir, licenseFile.fileName);
  const text = readFileSync(path, "utf8");
  writeFileSync(path, `${text.trimEnd()}\n\n${sections.trimEnd()}\n`);
}

/** A file the build writes at the generated package's root, beside `sdk/`; undefined when there is none. */
function readPackageFile(name: string): string | undefined {
  try {
    return readFileSync(new URL(`../../${name}`, import.meta.url), "utf8");
  } catch {
    return undefined;
  }
}

/** A file the bundle did not produce but the page loads from the app (Vite's public folder). */
function readFromDisk(outDir: string, path: string): Uint8Array | undefined {
  try {
    return readFileSync(join(outDir, path));
  } catch {
    return undefined;
  }
}
