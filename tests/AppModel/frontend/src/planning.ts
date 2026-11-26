import { traced } from "@spar/app/sdk";
import { copyJobs, type CopyRecipe } from "@spar/app/copyJobs";

// The "Compare plans" button: both plans in one trace, since both calls start inside `traced`, before any `await`.
export function comparePlans(first: CopyRecipe, second: CopyRecipe) {
  return traced("compare plans", () => Promise.all([copyJobs.plan(first), copyJobs.plan(second)]));
}
