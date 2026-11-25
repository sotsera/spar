import { describe, expect, test } from "vitest";
import { devNoncePlaceholder, devServerConfig, isIdle, readLaunch, stateRecord } from "../src/vite/devServer.js";

const launchVariables = {
  SPAR_DEV_SERVER_FILE: "/app/obj/spar-dev-server.json",
  SPAR_DEV_SERVER_PORT: "61234",
  SPAR_DEV_SERVER_INSTANCE: "ab12cd34",
  SPAR_DEV_SERVER_IDLE_SECONDS: "60",
};

describe("the app's launch", () => {
  test("is read from the variables the app sets", () => {
    expect(readLaunch(launchVariables)).toEqual({
      stateFile: "/app/obj/spar-dev-server.json",
      port: 61234,
      instance: "ab12cd34",
      idleSeconds: 60,
    });
  });

  test("is absent when a developer runs Vite themselves, or the variables are not usable", () => {
    expect(readLaunch({})).toBeUndefined();
    expect(readLaunch({ ...launchVariables, SPAR_DEV_SERVER_PORT: "70000" })).toBeUndefined();
    expect(readLaunch({ ...launchVariables, SPAR_DEV_SERVER_INSTANCE: "" })).toBeUndefined();
    expect(readLaunch({ ...launchVariables, SPAR_DEV_SERVER_IDLE_SECONDS: "soon" })?.idleSeconds).toBe(60);
  });
});

describe("the development server's settings", () => {
  test("a server the app started listens on 127.0.0.1 at its port and points the HMR client there", () => {
    const config = devServerConfig({ server: { host: "0.0.0.0", port: 3000, strictPort: false } }, readLaunch(launchVariables));

    expect(config).toEqual({
      html: { cspNonce: devNoncePlaceholder },
      server: {
        host: "127.0.0.1",
        port: 61234,
        strictPort: true,
        ws: { protocol: "ws", host: "127.0.0.1", clientPort: 61234 },
      },
    });
  });

  test("a server a developer runs keeps what its config chose", () => {
    const config = devServerConfig({ server: { host: "localhost", port: 4000, strictPort: false, ws: { clientPort: 4443 } } }, undefined);

    expect(config).toEqual({ html: { cspNonce: devNoncePlaceholder }, server: { port: 4000, strictPort: false } });
  });

  test("a server with nothing configured gets 127.0.0.1:5173, strictly", () => {
    expect(devServerConfig({}, undefined)).toEqual({
      html: { cspNonce: devNoncePlaceholder },
      server: { host: "127.0.0.1", port: 5173, strictPort: true, ws: { protocol: "ws", host: "127.0.0.1", clientPort: 5173 } },
    });
  });
});

describe("the record and the watchdog", () => {
  test("the record is the app's version 1, on one line", () => {
    const record = stateRecord("http://127.0.0.1:61234", "ab12cd34", 4242, 1795282406595);

    expect(record.endsWith("\n")).toBe(true);
    expect(JSON.parse(record)).toEqual({ version: 1, url: "http://127.0.0.1:61234", instance: "ab12cd34", pid: 4242, started: 1795282406595 });
  });

  test("a server is idle only with no HMR connection and no request for the whole time", () => {
    expect(isIdle(0, 1_000, 61_000, 60)).toBe(true);
    expect(isIdle(0, 1_000, 60_999, 60)).toBe(false);
    expect(isIdle(1, 0, 1_000_000, 60)).toBe(false);
  });
});
