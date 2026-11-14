// Editor probes. A marker comment starting with a bar puts the cursor at the comment; one starting with a caret puts it on the character before.
import { copyJobs, type CopyRecipe } from "@spar/app/copyJobs";

export async function probes(): Promise<void> {
  const recipe: CopyRecipe = { source/*^dto-prop-literal*/: "a", target: "b", tables: [] };
  const planned = await copyJobs.plan/*^command*/(recipe);

  planned.match({ success: p => p, errors: { /*|errors-empty*/ } });
  planned.match({ success: p => p, errors: { connectionNotFound: () => null, /*|errors-partial*/ } });

  planned.match({
    success: p => p,
    errors: {
      connectionNotFound: e => e.connectionId,
      tableMissing/*^case-key*/: e => e.table/*^payload-field*/,
      columnsMismatch: e => e.differences.length,
    },
  });

  const shown = planned.match/*^sdk-match*/({
    success: () => "ok",
    errors: { connectionNotFound: () => 1, tableMissing: () => true, columnsMismatch: () => null },
  });
  console.log(shown, recipe.source/*^dto-prop-access*/, copyJobs/*^contract*/);
}
