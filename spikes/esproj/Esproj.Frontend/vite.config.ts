import { readFileSync, writeFileSync, renameSync } from "node:fs";
import { request } from "node:http";
import { fileURLToPath } from "node:url";
import react from "@vitejs/plugin-react";
import type { Plugin } from "vite";

// The backend writes its current address and token to the development session file on every (re)start, so the dev
// server stays at one URL while the backend restarts behind it. Bridge calls are forwarded to whatever backend the
// file names right now. The proxy adds the backend token itself, so the page needs none; it accepts only requests
// from its own origin, and presents the backend's origin upstream (the backend's origin check stays strict).
const sessionFile = fileURLToPath(new URL("../Esproj.App/obj/spar-dev-session.json", import.meta.url));
const stateFile = fileURLToPath(new URL("../Esproj.App/obj/spar-dev-server.json", import.meta.url));
const owned = process.env.SPAR_DEV_SERVER_OWNED === "1";
const idleExitMs = Number(process.env.SPAR_DEV_SERVER_IDLE_MS ?? 60000);

interface Session {
  readonly backend: string;
  readonly token: string;
  readonly pid: number;
}

function readSession(): Session | undefined {
  // An orchestrator (Aspire) passes the backend directly; otherwise the backend's own session file says where it is.
  const assigned = process.env.SPAR_BACKEND_URL;
  if (assigned) {
    // Aspire names endpoints "localhost"; the backend listens on 127.0.0.1 only and checks its own origin.
    const url = new URL(assigned);
    if (url.hostname === "localhost") url.hostname = "127.0.0.1";
    return { backend: url.origin, token: process.env.SPAR_BACKEND_TOKEN ?? "", pid: process.pid };
  }

  try {
    return JSON.parse(readFileSync(sessionFile, "utf8")) as Session;
  } catch {
    return undefined;
  }
}

// A desktop host in a development build loads the page from this server and records itself here.
const ownerFile = fileURLToPath(new URL("../Esproj.App/obj/spar-dev-owner.json", import.meta.url));

function readOwner(): { readonly pid: number } | undefined {
  try {
    return JSON.parse(readFileSync(ownerFile, "utf8")) as { readonly pid: number };
  } catch {
    return undefined;
  }
}

function isAlive(pid: number): boolean {
  try {
    process.kill(pid, 0);
    return true;
  } catch {
    return false;
  }
}

function sparBridge(): Plugin {
  return {
    name: "spar-bridge",
    configureServer(server) {
      server.middlewares.use("/_spar", (req, res) => {
        const self = `http://${req.headers.host}`;
        if (req.headers.origin !== undefined && req.headers.origin !== self) {
          res.statusCode = 403;
          res.end(JSON.stringify({ error: "forbidden-origin", detail: req.headers.origin }));
          return;
        }

        const session = readSession();
        if (!session) {
          res.statusCode = 503;
          res.end(JSON.stringify({ error: "backend-not-running", detail: sessionFile }));
          return;
        }

        const target = new URL("/_spar" + (req.url ?? ""), session.backend);
        const upstream = request(
          target,
          { method: req.method, headers: { ...req.headers, host: target.host, origin: session.backend, authorization: `Bearer ${session.token}` } },
          (response) => {
            res.writeHead(response.statusCode ?? 502, response.headers);
            response.pipe(res);
          });
        upstream.on("error", () => {
          res.statusCode = 503;
          res.end(JSON.stringify({ error: "backend-restarting", detail: session.backend }));
        });
        req.pipe(upstream);
      });

      if (!owned) return;

      // Owned by the app: announce ourselves, and leave once the backend has been gone longer than a restart takes.
      server.httpServer?.once("listening", () => {
        const address = server.httpServer!.address();
        if (address === null || typeof address === "string") return;
        const temporary = stateFile + ".tmp";
        writeFileSync(temporary, JSON.stringify({ url: `http://127.0.0.1:${address.port}/`, pid: process.pid }));
        renameSync(temporary, stateFile);
      });

      let goneSince: number | undefined;
      setInterval(() => {
        const session = readSession();
        const desktop = readOwner();
        if ((session && isAlive(session.pid)) || (desktop && isAlive(desktop.pid))) {
          goneSince = undefined;
          return;
        }

        goneSince ??= Date.now();
        if (Date.now() - goneSince >= idleExitMs) {
          console.log(`spar-bridge: backend gone for ${idleExitMs} ms, exiting`);
          void server.close().then(() => process.exit(0));
        }
      }, 1000).unref();
    },
  };
}

export default {
  plugins: [react(), sparBridge()],
  server: { host: "127.0.0.1", port: 5173, strictPort: false },
};
