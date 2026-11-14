import { fileURLToPath } from "node:url";
import react from "@vitejs/plugin-react";
import { sparSecurity } from "./spar-security";

// Spike switches (environment): SPAR_SRI=1, SPAR_TT=1 (dev), SPAR_HMR_CONNECT=1 (dev), SPAR_KEEP_INLINE=1,
// SPAR_OUT=<dist folder>, SPAR_VITE_LOG=<jsonl>, SPAR_ALLOW_DOCKER=1 (lets the Linux container reach the dev server).
const env = process.env;

export default {
  plugins: [
    react(),
    sparSecurity({
      cspFile: fileURLToPath(new URL("../obj/spar/client/csp.json", import.meta.url)),
      trustedTypes: env.SPAR_TT === "1",
      hmrConnect: env.SPAR_HMR_CONNECT === "1",
      sri: env.SPAR_SRI === "1",
      keepInlineLimit: env.SPAR_KEEP_INLINE === "1",
      logFile: env.SPAR_VITE_LOG,
    }),
  ],
  server: {
    host: "127.0.0.1",
    port: 5273,
    strictPort: true,
    allowedHosts: env.SPAR_ALLOW_DOCKER === "1" ? ["host.docker.internal"] : [],
  },
  build: { outDir: env.SPAR_OUT ?? "dist", emptyOutDir: true },
};
