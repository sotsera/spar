// Editor probes. A marker comment starting with a bar puts the cursor at the comment; one starting with a caret puts it on the character before.
import { copyJobs } from "./copyJobs.js";
import { match } from "./sdk.js";

export async function probes(): Promise<void> {
  const planned = await copyJobs.plan({ source: "a", target: "b", tables: [] });
  const counted = await copyJobs.count();

  match(planned, { /*|keys-empty*/ });
  match(planned, { success: p => p, /*|keys-after-success*/ });
  match(planned, { success: p => p, errors: { /*|errors-empty*/ } });
  match(planned, { success: p => p, errors: { connectionNotFound: () => null, /*|errors-partial*/ } });
  match(counted, { /*|keys-no-errors*/ });
  void planned./*|members*/x;

  match(planned, {
    success: p/*^hover-success-param*/ => p,
    errors: {
      connectionNotFound: e => e.connectionId,
      tableMissing/*^hover-case-key*/: e/*^hover-case-param*/ => e.table/*^definition-payload-field*/,
      columnsMismatch: e => e.differences.length,
    },
    failure: e/*^hover-failure-param*/ => e.message,
  });

  const shown = match/*^hover-match*/(planned, {
    success: () => "ok",
    errors: { connectionNotFound: () => 1, tableMissing: () => true, columnsMismatch: () => null },
  });
  console.log(shown/*^hover-inferred-result*/);
}
