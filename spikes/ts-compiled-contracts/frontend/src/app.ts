// Usage that must compile under every configuration of the matrix (permissive to strictest).
import { configureSpar, SparError } from "@spar/app/sdk";
import { copyJobs, type CopyRecipe } from "@spar/app/copyJobs";

declare function show(text: string): void;
declare function highlight(table: string, columns: readonly string[]): void;

configureSpar({ onFailure: e => show(e.execution === "unknown" ? `${e.call}: result unknown, check the target` : e.message) });

export async function planAndRun(recipe: CopyRecipe, signal: AbortSignal): Promise<string> {
  const plan = await copyJobs.plan(recipe, { signal }).match({
    success: p => p,
    errors: {
      connectionNotFound: e => { show(`Unknown connection ${e.connectionId}`); return null; },
      tableMissing: e => { show(`${e.table} is missing on ${e.connection}`); return null; },
      columnsMismatch: e => { highlight(e.table, e.differences.map(d => d.column)); return null; },
    },
  });
  if (!plan) return "no plan";

  const message = await copyJobs.run({ planId: "p1", truncateTarget: false }, { signal }).match({
    success: r => `Copied ${r.rows} rows in ${r.tables} tables`,
    errors: {
      planOutdated: e => `Plan outdated: ${e.reason}`,
      targetNotEmpty: e => `${e.table} already has ${e.rows} rows`,
      constraintViolation: e => `${e.table}: error ${e.sqlNumber}`,
      partiallyCopied: e => `Stopped after ${e.rowsCommitted} rows: ${e.reason}`,
    },
    failure: e => e.execution === "notStarted" ? "Nothing was copied" : "Unknown: check the target before retrying",
  });

  const rows = await copyJobs.count().unwrap();
  try {
    await copyJobs.cancelAll().unwrap();
  } catch (e) {
    if (e instanceof SparError) show(String(e.error.code));
  }

  return `${message} (${rows} jobs)`;
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
        if (event.type === "event") show(`${event.value.table}: ${event.value.rows}`);
        else if (event.type === "gap") show(`${event.dropped} updates skipped`);
      },
      failure: e => show(`Progress stopped: ${e.message}`),
    });
  }
}
