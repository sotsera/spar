// Exact inferred types (strict configurations only: without strictNullChecks, null and undefined vanish from unions).
import { copyJobs, type CopyPlan, type CopyRecipe } from "@spar/app/copyJobs";

type Equal<A, B> = (<V>() => V extends A ? 1 : 2) extends (<V>() => V extends B ? 1 : 2) ? true : false;
function expectType<A, B>(_: Equal<A, B>): void {}

export async function inferred(recipe: CopyRecipe): Promise<void> {
  const plan = await copyJobs.plan(recipe).match({
    success: p => p,
    errors: { connectionNotFound: () => null, tableMissing: () => null, columnsMismatch: () => null },
  });
  expectType<typeof plan, CopyPlan | null | undefined>(true);

  const mixed = (await copyJobs.run({ planId: "p", truncateTarget: false })).match({
    success: r => r.rows,
    errors: { planOutdated: () => "outdated", targetNotEmpty: e => e.rows > 0, constraintViolation: () => null, partiallyCopied: e => e.rowsCommitted },
    failure: () => undefined,
  });
  expectType<typeof mixed, number | string | boolean | null | undefined>(true);

  const total = await copyJobs.count().match({ success: n => n });
  expectType<typeof total, number | undefined>(true);
}
