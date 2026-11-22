import { defineConfig } from "vitest/config";

// The page tests: the page's own code in Node, against the fake backend of @spar/app/testing. The browser and window
// tests of MyApp.EndToEndTests run it in a real engine against the real backend.
export default defineConfig({
  test: {
    include: ["src/**/*.test.ts"],
  },
});
