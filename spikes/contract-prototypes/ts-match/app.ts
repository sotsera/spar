// How an app would use it: everything here must compile.
import { configureSpar, match, unwrap } from "./sdk.js";
import { copyJobs, type CopyRecipe } from "./copyJobs.js";

declare function show(text: string): void;
declare function highlight(table: string, columns: readonly string[]): void;

configureSpar({ onFailure: e => show(e.execution === "unknown" ? `${e.call}: result unknown, check the target` : e.message) });

export async function planAndRun(recipe: CopyRecipe, signal: AbortSignal): Promise<void> {
  const planned = await copyJobs.plan(recipe, { signal });
  const plan = match(planned, {
    success: p => p,
    errors: {
      connectionNotFound: e => { show(`Unknown connection ${e.connectionId}`); return null; },
      tableMissing: e => { show(`${e.table} is missing on ${e.connection}`); return null; },
      columnsMismatch: e => { highlight(e.table, e.differences.map(d => d.column)); return null; },
    },
    // no `failure`: transport, cancellation and protocol failures go to the interceptor
  });
  if (!plan) return;

  const message = match(await copyJobs.run({ planId: "p1", truncateTarget: false }, { signal }), {
    success: r => `Copied ${r.rows} rows in ${r.tables} tables`,
    errors: {
      planOutdated: e => `Plan outdated: ${e.reason}`,
      targetNotEmpty: e => `${e.table} already has ${e.rows} rows`,
      constraintViolation: e => `${e.table}: error ${e.sqlNumber}`,
      partiallyCopied: e => `Stopped after ${e.rowsCommitted} rows: ${e.reason}`,
    },
    failure: e => e.execution === "notStarted" ? "Nothing was copied" : "Unknown: check the target before retrying",
  });
  show(message);

  const total = match(await copyJobs.count(), { success: n => n });
  show(String(total ?? 0));

  const rows = unwrap(await copyJobs.count());
  show(String(rows + 1));

  const r = await copyJobs.run({ planId: "p1", truncateTarget: true });
  if (r.ok) show(String(r.value.rows));
  else if (r.error.kind === "application" && r.error.code === "targetNotEmpty") show(String(r.error.rows));
}
