// Editor probes. A marker comment starting with a bar puts the cursor at the comment; one starting with a caret puts it on the character before.
import { copyJobs } from "./copyJobs.js";

export async function probes(): Promise<void> {
  const planned = await copyJobs.plan({ source: "a", target: "b", tables: [] });
  const counted = await copyJobs.count();
  void copyJobs.plan({ source: "a", target: "b", tables: [] }).match({ success: p => p, errors: { /*|errors-on-call*/ } });

  planned.match({ /*|keys-empty*/ });
  planned.match({ success: p => p, /*|keys-after-success*/ });
  planned.match({ success: p => p, errors: { /*|errors-empty*/ } });
  planned.match({ success: p => p, errors: { connectionNotFound: () => null, /*|errors-partial*/ } });
  counted.match({ /*|keys-no-errors*/ });
  planned./*|members*/match({ success: p => p, errors: { connectionNotFound: () => null, tableMissing: () => null, columnsMismatch: () => null } });

  planned.match({
    success: p/*^hover-success-param*/ => p,
    errors: {
      connectionNotFound: e => e.connectionId,
      tableMissing/*^hover-case-key*/: e/*^hover-case-param*/ => e.table/*^definition-payload-field*/,
      columnsMismatch: e => e.differences.length,
    },
    failure: e/*^hover-failure-param*/ => e.message,
  });

  const shown = planned.match/*^hover-match*/({
    success: () => "ok",
    errors: { connectionNotFound: () => 1, tableMissing: () => true, columnsMismatch: () => null },
  });
  console.log(shown/*^hover-inferred-result*/);
}
