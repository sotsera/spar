import { configureSpar, SparError } from "@spar/app/sdk";
import { copyJobs, type CopyRecipe } from "@spar/app/copyJobs";

declare function toast(text: string): void;

configureSpar({
  // The interceptor: failures that no call handled. It may notify, never retry or change a result.
  onFailure: e => toast(e.execution === "unknown" ? `${e.call}: outcome unknown, check the target` : e.message),
  // Every failure, handled or not, for logs and telemetry.
  observe: e => console.warn(e.call, e.kind, e.code, e.execution),
});

export async function planCopy(recipe: CopyRecipe, signal: AbortSignal): Promise<string | undefined> {
  // Every declared error must have a handler. `failure` by kind handles only the kinds it lists;
  // the others go to the interceptor and the result is undefined.
  return copyJobs.plan(recipe, { signal }).match({
    success: plan => `${plan.steps.length} tables, about ${plan.steps.reduce((n, s) => n + s.estimatedRows, 0n)} rows`,
    errors: {
      connectionNotFound: e => `Unknown connection ${e.connectionId}`,
      tableMissing: e => `${e.table} is missing on ${e.connection}`,
    },
    failure: {
      unavailable: () => "The backend is not ready yet",
    },
  });
}

export async function runCopy(planId: string): Promise<string> {
  // With a `failure` function, every outcome is handled here and nothing reaches the interceptor.
  return copyJobs.run({ planId, truncateTarget: false }).match({
    success: r => `Copied ${r.rows} rows in ${r.tables} tables`,
    errors: {
      planOutdated: e => `Plan outdated: ${e.reason}`,
      targetNotEmpty: e => `${e.table} already has ${e.rows} rows`,
      partiallyCopied: e => `Stopped after ${e.rowsCommitted} rows: ${e.reason}`,
    },
    failure: e => (e.execution === "notStarted" ? "Nothing was copied" : "Outcome unknown: check the target before retrying"),
  });
}

export async function cancelEverything(): Promise<void> {
  // unwrap: the value or an exception, for code that prefers exceptions. It never reaches the interceptor.
  try {
    await copyJobs.cancelAll().unwrap();
  } catch (e) {
    if (e instanceof SparError) toast(e.message);
  }
}
