import { fileURLToPath } from "node:url";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";
import { sparTheme, type SparSplashOptions } from "./spar-theme-plugin.ts";

// Spike pages, one bundle:
//   baseline.html  today's page: no color-scheme meta, no theme style;
//   themed.html    + meta and theme style (hashed);
//   splash.html    + the splash (delay 350 ms, minimum visible 400 ms);
//   splash-NNN.html other delays, to compare; splash-text.html with static text in #root; splash-v1.html the
//                  first splash design (transparent until the delay).
const splash = (delayMs: number): SparSplashOptions => ({ delayMs, minVisibleMs: 400, fadeInMs: 120, fadeOutMs: 150 });
const pages = ["baseline", "themed", "splash", "splash-200", "splash-500", "splash-text", "splash-v1"];

export default defineConfig({
  plugins: [
    react(),
    sparTheme({
      light: "#f3f3f3",
      dark: "#1f1f1f",
      lightText: "#1b1b1b",
      darkText: "#f0f0f0",
      page: (file) => {
        if (file === "baseline.html") return { theme: false };
        if (file === "themed.html") return { theme: true };
        if (file === "splash-v1.html") return { theme: true, splash: { ...splash(350), v1: true } };
        const delay = /^splash-(\d+)\.html$/.exec(file);
        return { theme: true, splash: splash(delay ? Number(delay[1]) : 350) };
      },
      cspFile: "spar-csp.json",
    }),
  ],
  build: {
    outDir: "dist",
    emptyOutDir: true,
    assetsInlineLimit: 0,
    rollupOptions: {
      input: Object.fromEntries(pages.map((p) => [p, fileURLToPath(new URL(`./${p}.html`, import.meta.url))])),
    },
  },
});
