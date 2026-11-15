// A connected page for the framework tests: connectSpar over a scripted host that answers the page's latest call.
import { connectSpar } from "../../src/sdk.js";
import { FakeHost } from "../fakeHost.js";

export const fingerprint = "0123456789abcdef";

export function connectPage(): FakeHost {
  const host = new FakeHost();
  connectSpar(host, fingerprint);
  host.open(fingerprint);
  return host;
}

/** The id of the newest call the page sent. */
export function lastCallId(host: FakeHost): number {
  const call = host.last("call");
  if (call?.id === undefined) {
    throw new Error("The page has sent no call.");
  }

  return call.id;
}

export function answerPlan(host: FakeHost, tables: number, id = lastCallId(host)): void {
  host.deliver({ type: "result", id, ok: true, payload: JSON.stringify({ id: "p1", tables }) });
}

export function answerTableMissing(host: FakeHost, table: string, id = lastCallId(host)): void {
  host.deliver({ type: "result", id, ok: false, payload: JSON.stringify({ code: "tableMissing", table }) });
}

/** Lets pending promise callbacks run. */
export const settle = (): Promise<void> => new Promise(resolve => setTimeout(resolve, 0));
