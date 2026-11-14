// Not from docs/app-model.md. Stand-in generated declarations for a `Manual` subscription
// (C# `[SparSubscription(Resume = SubscriptionResume.Manual)]`), so that bad/bad.ts can pass it `resume: "automatic"`.
import type { SubscribeOptions, Subscription } from "@spar/app/sdk";

export interface LogLine { readonly text: string; }

export declare const jobLog: {
  readonly follow: (jobId: string, options?: SubscribeOptions<"manual">) => Subscription<LogLine>;
};
