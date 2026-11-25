// The development server's side of a Debug build (plan section 4; decision "M1d step 1's shape: Debug through the app's
// own origin"). The app fetches every page request from this Vite server in-process and answers it as its own origin
// (`https://app.localhost` on Windows, `spar://app` on Linux), with its own policy. For that, the server:
// - listens on 127.0.0.1 only, on a port known before it starts (`strictPort`), so the page's HMR client can be told
//   where to connect: `ws://127.0.0.1:<port>`, which the app's Debug policy allows in `connect-src`. With Vite's default
//   the client would derive `wss://app.localhost` from the page and get no updates;
// - writes `html.cspNonce` as a placeholder, which the app replaces with a fresh nonce per document, in the page and in
//   its policy, for Vite's inline preamble and the styles its client injects;
// - when the app started it (`npm run dev` with SPAR_DEV_SERVER_* in its environment): records its real address in the
//   app's `obj/spar-dev-server.json` once it listens, answers its identity at `/__spar/dev-server`, so a later run of the
//   app can reuse it, removes the record when it closes, and ends itself after a minute with no request and no HMR
//   connection (it also ends with its standard input, as Vite does).
import { readFileSync, renameSync, rmSync, writeFileSync } from "node:fs";
import type { AddressInfo } from "node:net";
import type { UserConfig, ViteDevServer } from "vite";

/** The placeholder Vite writes as every nonce; the app replaces it (Spar's `DevServerProxy.NoncePlaceholder`). */
export const devNoncePlaceholder = "spar-dev-nonce";

/** Where the plugin answers the app's identity check (Spar's `DevServerSession.IdentityPath`). */
export const identityPath = "/__spar/dev-server";

/** What the app passes to a server it starts; absent when a developer runs Vite themselves. */
export interface DevServerLaunch {
  /** `obj/spar-dev-server.json` of the app. */
  readonly stateFile: string;
  readonly port: number;
  /** The id the app gave this server, which the identity check answers. */
  readonly instance: string;
  /** How long the server may go unused before it ends. */
  readonly idleSeconds: number;
}

/** The launch the app's variables describe, or undefined when the app did not start this server. */
export function readLaunch(environment: NodeJS.ProcessEnv): DevServerLaunch | undefined {
  const stateFile = environment["SPAR_DEV_SERVER_FILE"];
  const port = Number(environment["SPAR_DEV_SERVER_PORT"]);
  const instance = environment["SPAR_DEV_SERVER_INSTANCE"];
  const idle = Number(environment["SPAR_DEV_SERVER_IDLE_SECONDS"] ?? "60");
  if (!stateFile || !instance || !Number.isInteger(port) || port <= 0 || port > 65535) {
    return undefined;
  }

  return { stateFile, port, instance, idleSeconds: Number.isFinite(idle) && idle > 0 ? idle : 60 };
}

/**
 * The development server's settings, merged into the app's own Vite config. A server the app started gets the port
 * it chose; one a developer runs keeps the port and host they configured, by default 5173 on 127.0.0.1. Settings the
 * app's config already has are left alone, except the nonce placeholder, which the app's origin needs.
 */
export function devServerConfig(user: UserConfig, launch: DevServerLaunch | undefined): UserConfig {
  const server = user.server ?? {};
  const port = launch?.port ?? server.port ?? 5173;
  const config: UserConfig = {
    html: { cspNonce: devNoncePlaceholder },
    server: {
      port,
      strictPort: launch !== undefined || server.strictPort === undefined ? true : server.strictPort,
    },
  };

  if (launch !== undefined || server.host === undefined) {
    config.server!.host = "127.0.0.1";
  }

  // Vite 8 names the HMR socket's settings server.ws (server.hmr's are deprecated).
  if (launch !== undefined || (server.ws === undefined && typeof server.hmr !== "object")) {
    config.server!.ws = { protocol: "ws", host: "127.0.0.1", clientPort: port };
  }

  return config;
}

/** The record the app reads (Spar's `DevServerState`, version 1). */
export function stateRecord(url: string, instance: string, pid: number, startedMilliseconds: number): string {
  return `${JSON.stringify({ version: 1, url, instance, pid, started: startedMilliseconds })}\n`;
}

/** Whether a server with `clients` HMR connections, last used at `lastUsed`, has been idle long enough to end. */
export function isIdle(clients: number, lastUsed: number, now: number, idleSeconds: number): boolean {
  return clients === 0 && now - lastUsed >= idleSeconds * 1000;
}

/** The identity endpoint, the record and the watchdog of a server the app started. */
export function attachLaunch(server: ViteDevServer, launch: DevServerLaunch): void {
  let lastUsed = Date.now();
  server.middlewares.use((request, response, next) => {
    lastUsed = Date.now();
    if (request.url !== identityPath) {
      next();
      return;
    }

    response.setHeader("Content-Type", "text/plain; charset=utf-8");
    response.setHeader("Cache-Control", "no-store");
    response.end(launch.instance);
  });

  const httpServer = server.httpServer;
  if (httpServer === null) {
    return;
  }

  httpServer.once("listening", () => {
    const address = httpServer.address() as AddressInfo | null;
    if (address === null || typeof address !== "object") {
      return;
    }

    const started = Math.round(Date.now() - process.uptime() * 1000);
    const record = stateRecord(`http://127.0.0.1:${address.port}`, launch.instance, process.pid, started);
    const temporary = `${launch.stateFile}.${process.pid}.tmp`;
    writeFileSync(temporary, record);
    renameSync(temporary, launch.stateFile);
  });

  httpServer.once("close", () => removeRecord(launch));
  process.once("exit", () => removeRecord(launch));

  // The server ends with its standard input, which is the app's launcher's when that is a pipe (the app passes a pipe
  // that never ends otherwise). Vite listens for the input's end too, but never reads it, so a paused input never ends.
  process.stdin.on("end", () => {
    server.config.logger.info("[spar] standard input ended: the development server ends.");
    void server.close().finally(() => process.exit(0));
  });
  process.stdin.on("error", () => undefined);
  process.stdin.resume();

  const watchdog = setInterval(() => {
    if (!isIdle(server.ws.clients.size, lastUsed, Date.now(), launch.idleSeconds)) {
      return;
    }

    clearInterval(watchdog);
    server.config.logger.info(`[spar] no request and no HMR connection for ${launch.idleSeconds} s: the development server ends.`);
    void server.close().finally(() => process.exit(0));
  }, 1000);
  watchdog.unref();
}

/** Removes the record when it is still this server's, so a newer server's record is never touched. */
function removeRecord(launch: DevServerLaunch): void {
  try {
    const recorded: unknown = JSON.parse(readFileSync(launch.stateFile, "utf8"));
    const instance = typeof recorded === "object" && recorded !== null ? Reflect.get(recorded, "instance") : undefined;
    if (instance === launch.instance) {
      rmSync(launch.stateFile, { force: true });
    }
  } catch {
    // Gone already, or unreadable; the app checks a record's instance before it uses it anyway.
  }
}
