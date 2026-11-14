import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Production by default; FRAMEWORK_OUT picks the folder (tools/build-all.mjs builds prod and dev variants).
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: process.env.FRAMEWORK_OUT ?? "../../../artifacts/frontend-frameworks/react",
    emptyOutDir: true,
    assetsInlineLimit: 0,
  },
});
