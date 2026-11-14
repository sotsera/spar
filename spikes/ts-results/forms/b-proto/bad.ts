// Mistakes the compiler must reject.
import { match } from "./sdk.js";
import { copyJobs } from "./copyJobs.js";

export async function mistakes(): Promise<void> {
  const planned = await copyJobs.plan({ source: "a", target: "b", tables: [] });

  // 1. a declared error is not handled
  match(planned, { success: () => 1, errors: { connectionNotFound: () => 2, tableMissing: () => 3 } });

  // 2. a typo in an error name
  match(planned, { success: () => 1, errors: { connectionNotFound: () => 2, tableMissing: () => 3, columnMismatch: () => 4 } });

  // 3. a payload field that does not exist
  match(planned, { success: () => 1, errors: { connectionNotFound: e => e.connection, tableMissing: () => 3, columnsMismatch: () => 4 } });

  // 4. errors forgotten entirely
  match(planned, { success: () => 1 });

  // 5. using the value without checking ok
  console.log(planned.value);

  // 10. control for quick fixes: a type used without its import
  const report: CopyReport | null = null;
  console.log(report);

  // 11. an empty errors object
  match(planned, { success: () => 1, errors: {} });
}
