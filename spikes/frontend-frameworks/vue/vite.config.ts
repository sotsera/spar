import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";

// VUE_FULL=1 builds full.html with the full Vue build (runtime template compiler) instead of the runtime-only default.
const full = process.env.VUE_FULL === "1";
export default defineConfig({
  plugins: [vue()],
  resolve: full ? { alias: { vue: "vue/dist/vue.esm-bundler.js" } } : {},
  build: {
    outDir: process.env.FRAMEWORK_OUT ?? (full ? "../../../artifacts/frontend-frameworks/vue-full" : "../../../artifacts/frontend-frameworks/vue"),
    emptyOutDir: true,
    assetsInlineLimit: 0,
    rollupOptions: { input: full ? "full.html" : "index.html" },
  },
});
