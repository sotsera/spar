// Two builds: "default" (Radix's own style helper, which takes the nonce) and "cssom" (the helper replaced by
// src/cssom-singleton.ts). Nothing is inlined as a data: URL, which the strict policy blocks.
import react from "@vitejs/plugin-react";
import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";

export default defineConfig(({ mode }) => ({
  plugins: [react()],
  define: { __SPAR_BUILD__: JSON.stringify(mode === "cssom" ? "cssom" : "default") },
  resolve: {
    alias: mode === "cssom"
      ? [{ find: "react-style-singleton", replacement: fileURLToPath(new URL("./src/cssom-singleton.ts", import.meta.url)) }]
      : [],
  },
  build: { assetsInlineLimit: 0, sourcemap: false },
}));
