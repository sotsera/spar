// How an app would use form A. Everything here must compile, and the type assertions must hold.
import { configureSpar, SparError } from "./sdk.js";
import { copyJobs, type CopyPlan, type CopyRecipe } from "./copyJobs.js";

declare function show(text: string): void;
declare function highlight(table: string, columns: readonly string[]): void;
type Equal<A, B> = (<V>() => V extends A ? 1 : 2) extends (<V>() => V extends B ? 1 : 2) ? true : false;
function expectType<A, B>(_: Equal<A, B>): void {}

configureSpar({ onFailure: e => show(e.execution === "unknown" ? `${e.call}: result unknown, check the target` : e.message) });

export async function planAndRun(recipe: CopyRecipe, signal: AbortSignal): Promise<void> {
  // Unary with declared errors; `failure` left to the interceptor, so `undefined` joins the result.
  const plan = await copyJobs.plan(recipe, { signal }).match({
    success: p => p,
    errors: {
      connectionNotFound: e => { show(`Unknown connection ${e.connectionId}`); return null; },
      tableMissing: e => { show(`${e.table} is missing on ${e.connection}`); return null; },
      columnsMismatch: e => { highlight(e.table, e.differences.map(d => d.column)); return null; },
    },
  });
  expectType<typeof plan, CopyPlan | null | undefined>(true);
  if (!plan) return;

  // Every handler returns a string, `failure` included: no `undefined`.
  const run = await copyJobs.run({ planId: "p1", truncateTarget: false }, { signal });
  const message = run.match({
    success: r => `Copied ${r.rows} rows in ${r.tables} tables`,
    errors: {
      planOutdated: e => `Plan outdated: ${e.reason}`,
      targetNotEmpty: e => `${e.table} already has ${e.rows} rows`,
      constraintViolation: e => `${e.table}: error ${e.sqlNumber}`,
      partiallyCopied: e => `Stopped after ${e.rowsCommitted} rows: ${e.reason}`,
    },
    failure: e => e.execution === "notStarted" ? "Nothing was copied" : "Unknown: check the target before retrying",
  });
  expectType<typeof message, string>(true);
  show(message);

  // Mixed return types across handlers: the union is inferred.
  const mixed = run.match({
    success: r => r.rows,
    errors: {
      planOutdated: () => "outdated",
      targetNotEmpty: e => e.rows > 0,
      constraintViolation: () => null,
      partiallyCopied: e => e.rowsCommitted,
    },
    failure: () => undefined,
  });
  expectType<typeof mixed, number | string | boolean | null | undefined>(true);

  // No declared errors: no `errors` key.
  const total = await copyJobs.count().match({ success: n => n });
  expectType<typeof total, number | undefined>(true);
  await copyJobs.cancelAll().match({ success: () => show("cancelled"), failure: e => show(e.message) });

  // Exceptions on request.
  const rows = await copyJobs.count().unwrap();
  show(String(rows + 1));
  try {
    await copyJobs.run({ planId: "p1", truncateTarget: true }).unwrap();
  } catch (e) {
    if (e instanceof SparError) show(String(e.error.code));
  }
}

export async function previewRows(recipe: CopyRecipe, signal: AbortSignal): Promise<string[][]> {
  const rows: string[][] = [];
  for await (const item of copyJobs.preview(recipe, { signal })) {
    item.match({
      success: row => { rows.push(row.cells.map(c => c ?? "")); },
      errors: { tableMissing: e => show(`${e.table} is missing`) },
    });
  }

  return rows;
}

export async function watchProgress(jobId: string, signal: AbortSignal): Promise<void> {
  for await (const item of copyJobs.progress({ jobId }, { signal })) {
    item.match({
      success: event => {
        switch (event.type) {
          case "event": show(`${event.value.table}: ${event.value.rows}`); break;
          case "gap": show(`${event.dropped} updates skipped`); break;
          case "reset": show("Progress restarted"); break;
          case "reconnecting":
          case "resumed": break;
        }
      },
      failure: e => show(`Progress stopped: ${e.message}`),
    });
  }
}
