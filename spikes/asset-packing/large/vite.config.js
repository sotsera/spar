// The synthetic "large" frontend: real libraries bundled and minified by Vite, written under the spike's obj/.
export default {
  build: {
    outDir: "../obj/inputs/large",
    emptyOutDir: true,
    chunkSizeWarningLimit: 10000,
  },
};
