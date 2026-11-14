// The snippets RESULTS.md shows as documentation, type-checked with the strict settings.
import { configureSpar } from "./sdk.js";
import { copyJobs, type CopyRecipe, type PreviewRow, type RunCopy } from "./copyJobs.js";

declare function notify(text: string): void;
declare function setProgress(table: string, rows: number): void;
declare function reload(): void;

export function setup(): void {
  // App-wide: infrastructure failures that a call site does not handle.
  configureSpar({ onFailure: e => notify(e.execution === "unknown" ? `${e.call}: outcome unknown` : e.message) });
}

export async function unary(recipe: CopyRecipe, request: RunCopy, signal: AbortSignal): Promise<void> {
  // A command with declared errors. Every case is required; `failure` is optional.
  const plan = await copyJobs.plan(recipe, { signal }).match({
    success: plan => plan,
    errors: {
      connectionNotFound: e => { notify(`Unknown connection ${e.connectionId}`); return null; },
      tableMissing: e => { notify(`${e.table} is missing on ${e.connection}`); return null; },
      columnsMismatch: e => { notify(`${e.table}: ${e.differences.length} columns differ`); return null; },
    },
  });
  // plan: CopyPlan | null | undefined (undefined: a failure went to onFailure)
  if (!plan) return;

  const message = await copyJobs.run(request, { signal }).match({
    success: r => `Copied ${r.rows} rows in ${r.tables} tables`,
    errors: {
      planOutdated: e => `Plan outdated: ${e.reason}`,
      targetNotEmpty: e => `${e.table} already has ${e.rows} rows`,
      constraintViolation: e => `${e.table}: SQL error ${e.sqlNumber}`,
      partiallyCopied: e => `Stopped after ${e.rowsCommitted} rows: ${e.reason}`,
    },
    failure: e => e.execution === "notStarted" ? "Nothing was copied" : "Outcome unknown: check the target",
  });
  // message: string
  notify(message);
}

export async function noErrors(): Promise<void> {
  // A command without declared errors: no `errors` key.
  const total = await copyJobs.count().match({ success: n => n });
  // total: number | undefined

  await copyJobs.cancelAll().match({ success: () => notify("Cancelled") });

  // Exceptions instead, when a library expects a rejected promise.
  const rows = await copyJobs.count().unwrap();
  // rows: number; rejects with SparError
  notify(`${total ?? "?"} / ${rows}`);
}

export async function stream(recipe: CopyRecipe, signal: AbortSignal): Promise<PreviewRow[]> {
  // A stream: every item is a result; a failure ends the stream.
  const rows: PreviewRow[] = [];
  for await (const item of copyJobs.preview(recipe, { signal })) {
    item.match({
      success: row => { rows.push(row); },
      errors: { tableMissing: e => notify(`${e.table} is missing`) },
    });
  }

  return rows;
}

export async function subscription(jobId: string, signal: AbortSignal): Promise<void> {
  // A subscription: events, gaps and resets arrive as success values.
  for await (const item of copyJobs.progress({ jobId }, { signal })) {
    item.match({
      success: event => {
        if (event.type === "event") setProgress(event.value.table, event.value.rows);
        else if (event.type === "reset") reload();
        else if (event.type === "gap") notify(`${event.dropped} updates skipped`);
      },
      failure: e => notify(`Progress stopped: ${e.message}`),
    });
  }
}
