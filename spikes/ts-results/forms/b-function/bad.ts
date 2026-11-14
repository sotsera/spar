// Mistakes the compiler must reject. Each numbered line must produce exactly the diagnostics listed in RESULTS.md.
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

  // 5. reading the value without match
  console.log(planned.value);

  // 6. errors given to a command that declares none
  match(await copyJobs.count(), { success: n => n, errors: {} });

  // 7. success forgotten
  match(await copyJobs.count(), { failure: () => 0 });

  // 8. the promise matched without await
  match(copyJobs.count(), { success: n => n });

  // 9. every case handled plus one that is not declared
  match(planned, { success: () => 1, errors: { connectionNotFound: () => 2, tableMissing: () => 3, columnsMismatch: () => 4, columnMismatch: () => 5 } });

  // 10. control for quick fixes: a type used without its import
  const report: CopyReport | null = null;
  console.log(report);

  // 11. an empty errors object
  match(planned, { success: () => 1, errors: {} });
}
