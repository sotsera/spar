import spar from "@spar/app/vite";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [spar()],
  build: {
    rollupOptions: {
      input: { index: "index.html", splash: "splash.html", blank: "blank.html" },
    },
  },
});
