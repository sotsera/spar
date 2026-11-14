// Stand-in for `@spar/app/sdk`. Form C of spikes/ts-results, copied unchanged into ./core.ts and ./c-call/sdk.ts,
// plus what docs/app-model.md uses beyond it: SubscribeOptions, ready() and setColorScheme().
import type { CallOptions } from "./core.js";

export { Call, Result, configureSpar, SparError, type CallError, type CallOptions, type SubscriptionEvent } from "./c-call/sdk.js";

/** How a subscription resumes after a lost connection, as declared in C# (`SubscriptionResume`). */
export type SubscriptionResume = "automatic" | "manual";

/**
 * Options of a subscription whose C# declaration resumes as `R`.
 * An `Automatic` subscription may be switched to manual resumption; a `Manual` one cannot become automatic.
 */
export interface SubscribeOptions<R extends SubscriptionResume> extends CallOptions {
  readonly resume?: R extends "automatic" ? SubscriptionResume : "manual";
}

export type ColorScheme = "light" | "dark" | "system";

/** Stand-in for the host bridge: what the page would post to the host, in order. */
export const hostMessages: unknown[] = [];

/** Manual splash mode only: reports ui-ready, so the host removes the splash. */
export function ready(): void {
  hostMessages.push({ type: "ui-ready" });
}

/** Stores the app's theme choice; the host applies it before the next start. */
export function setColorScheme(scheme: ColorScheme): void {
  hostMessages.push({ type: "color-scheme", scheme });
}
