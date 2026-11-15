// Not from docs/app-model.md. Stand-in generated declarations for two `Manual` subscriptions
// (C# `[SparSubscription(Resume = SubscriptionResume.Manual)]`):
// - `follow`, so that bad/bad.ts can pass it `resume: "automatic"`;
// - `tail`, which declares errors (C# `IAsyncEnumerable<Result<LogLine, LogError>>`), so that probes/ok.ts and
//   bad/bad.ts can check that a subscription item's `match` takes `errors` as well as `events`.
import type { SubscribeOptions, Subscription } from "@spar/app/sdk";

export interface LogLine {
  readonly text: string;
}

/** Why a log can no longer be followed. */
export interface LogError {
  readonly logDeleted: {
    readonly jobId: string;
  };
}

export declare const jobLog: {
  readonly follow: (jobId: string, options?: SubscribeOptions<"manual">) => Subscription<LogLine>;

  readonly tail: (jobId: string, options?: SubscribeOptions<"manual">) => Subscription<LogLine, LogError>;
};
