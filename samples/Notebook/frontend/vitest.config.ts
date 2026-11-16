import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

// The page tests (plan section 5, level 3): the page's own code in Node, against @spar/app/testing's fake backend.
// The real engine is covered by the browser tests in Notebook.EndToEndTests.
export default defineConfig({
  plugins: [react()],
  test: {
    include: ["src/**/*.test.{ts,tsx}"],
    environment: "happy-dom",
    cacheDir: "node_modules/.vitest",
  },
});
