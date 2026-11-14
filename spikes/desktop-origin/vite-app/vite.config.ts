import { fileURLToPath } from "node:url";
import react from "@vitejs/plugin-react";
import type { Plugin } from "vite";

// Desktop-origin spike (question 6): the React spike's frontend served by Vite, either loaded directly by the WebView
// (http://127.0.0.1:5199) or through the host's WebResourceRequested proxy on https://app.localhost.
// In proxy mode (SPAR_HMR_DIRECT=1) the HMR WebSocket cannot go through the host, so the client connects to Vite
// directly: protocol ws, host 127.0.0.1, port and clientPort 5199.
const port = 5199;
const direct = process.env.SPAR_HMR_DIRECT === "1";

// No backend here: /_spar/* is answered by the desktop host when it sees it, otherwise with 204 here.
function sparStub(): Plugin {
  return {
    name: "spar-stub",
    configureServer(server) {
      server.middlewares.use("/_spar", (_req, res) => {
        res.statusCode = 204;
        res.end();
      });
    },
  };
}

export default {
  plugins: [react(), sparStub()],
  cacheDir: fileURLToPath(new URL("./node_modules/.vite", import.meta.url)),
  server: {
    host: "127.0.0.1",
    port,
    strictPort: true,
    hmr: direct ? { protocol: "ws", host: "127.0.0.1", port, clientPort: port } : undefined,
  },
};
