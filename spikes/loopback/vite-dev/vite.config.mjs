import { fileURLToPath } from "node:url";

// Dev server over the app's authored frontend. Every bridge call is proxied to the running loopback backend,
// so frontend edits never restart the backend.
//
// The generated client lives under obj/ and must resolve as a module (Vite refuses to import publicDir files).
// The real design links it as @spar/* packages in node_modules; aliases stand in for that here.
const here = (path) => fileURLToPath(new URL(path, import.meta.url));
const frontend = here("../Loop.App/frontend");
const client = here("../Loop.App/obj/Debug/net11.0/win-x64/spar/client");

export default {
  root: frontend,
  logLevel: "warn",
  resolve: {
    alias: [
      { find: /^\.\/notes\.js$/, replacement: `${client}/notes.js` },
      { find: /^\.\/spar-sdk\.js$/, replacement: `${frontend}/spar-sdk.js` },
    ],
  },
  server: {
    host: "127.0.0.1",
    port: 0,
    strictPort: false,
    fs: { allow: [frontend, client] },
    // The page's origin is the dev server's; the backend only accepts its own. The proxy is a trusted local
    // component, so it presents the backend's origin rather than the backend loosening its check.
    proxy: {
      "/_spar": { target: process.env.SPAR_BACKEND, changeOrigin: true, headers: { origin: process.env.SPAR_BACKEND } },
    },
  },
};
