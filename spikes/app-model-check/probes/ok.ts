// Positive controls for bad/bad.ts: the same calls, written correctly, must compile. Not from docs/app-model.md.
import { copyJobs, type CopyRecipe } from "@spar/app/copyJobs";
import { jobLog } from "@spar/app/jobLog";

const recipe: CopyRecipe = { source: "src", target: "dst", tables: ["dbo.Orders"] };
const signal = new AbortController().signal;

// An Automatic subscription: default, switched to manual, or automatic stated explicitly.
export const automaticDefault = copyJobs.progress("job-1");
export const automaticToManual = copyJobs.progress("job-1", { signal, resume: "manual" });
export const automaticExplicit = copyJobs.progress("job-1", { resume: "automatic" });

// A Manual subscription accepts "manual".
export const manual = jobLog.follow("job-1", { resume: "manual" });

// Every case handled, with the payload fields that exist.
export const planned: Promise<string | undefined> = copyJobs.plan(recipe).match({
  success: plan => `${plan.planId} ${plan.steps.length}`,
  errors: { connectionNotFound: e => e.connectionId, tableMissing: e => `${e.connection}/${e.table}` },
});

// A command without declared errors takes no `errors`.
export const cancelled: Promise<string | undefined> = copyJobs.cancelAll().match({ success: () => "done" });

// The awaited Result has match and unwrap too.
export async function awaited(): Promise<bigint> {
  const result = await copyJobs.run({ planId: "p", truncateTarget: false });
  return result.match({
    success: r => r.rows,
    errors: { planOutdated: () => 0n, targetNotEmpty: e => e.rows, partiallyCopied: e => e.rowsCommitted },
    failure: () => -1n,
  });
}
