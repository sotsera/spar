import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

// The built page goes under obj/ (generated output), served by both harnesses from there.
export default defineConfig({
  plugins: [react(), tailwindcss()],
  build: { outDir: "obj/dist", emptyOutDir: true },
});
