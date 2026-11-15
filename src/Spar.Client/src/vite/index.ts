// @spar/app/vite: the Vite plugin every Spar frontend uses (docs/app-model.md section 1). For a production build it
// sets what the default Content Security Policy needs (plan section 3b): no asset inlined as a `data:` URL, which the
// policy blocks, no source maps, which never ship (plan section 3c), and pages served from the root of the app's own
// origin. The development server's part arrives with the development loop.
import type { Plugin, UserConfig } from "vite";

/** Spar's Vite plugin: `plugins: [react(), spar()]` in `vite.config.ts`. */
export default function spar(): Plugin {
  return {
    name: "spar",
    config(): UserConfig {
      return {
        base: "/",
        build: {
          // A small asset would otherwise become a data: URL, which img-src 'self' and font-src 'self' block.
          assetsInlineLimit: 0,
          sourcemap: false,
          // The engines Spar supports (WebView2, WebKitGTK 2.48 and newer) all run ES2022.
          target: "es2022",
        },
      };
    },
  };
}
